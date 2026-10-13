using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Translate.Core;
namespace Translate.Native;
internal readonly record struct SelectionRequest(nint Window,nint Focus,Win32.POINT Position);
internal sealed class MouseHook : IDisposable
{
    private readonly RightClickGesture _gesture=new();
    private readonly Thread _thread;
    private readonly Win32.HookProc _mouseProc,_keyProc;
    private readonly ManualResetEventSlim _ready=new(false);
    private readonly ConcurrentDictionary<uint,int> _integrity=new();
    private readonly int _ownIntegrity=Win32.IntegrityLevel((uint)Environment.ProcessId);
    private readonly Timer _permissionTimer;
    private Timer? _timer;
    private nint _mouse,_keyboard;
    private uint _threadId;
    private volatile bool _enabled=true,_popupVisible;
    private int _hold=300;
    private bool _escapeHeld;
    private SelectionRequest _request;
    public event Action<SelectionRequest>? TranslateRequested;
    public event Action<Win32.POINT>? PointerDown;
    public event Action? EscapePressed;
    public string? StartupError { get; private set; }
    private static double Now => Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
    public MouseHook()
    {
        _mouseProc=MouseCallback;_keyProc=KeyboardCallback;
        _permissionTimer=new Timer(_=>RefreshPermission(),null,0,150);
        _thread=new Thread(Run){IsBackground=true,Name="Translate input"};_thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
        if(_mouse==0)StartupError="全局鼠标监听未能启动，可以先使用手动翻译。";
    }
    public void Configure(bool enabled,int hold) { _enabled=enabled;Volatile.Write(ref _hold,Math.Clamp(hold,200,2000)); }
    public bool PopupVisible { set => _popupVisible=value; }
    private void RefreshPermission()
    {
        var hwnd=Win32.GetForegroundWindow();Win32.GetWindowThreadProcessId(hwnd,out var pid);
        if(pid!=0&&!_integrity.ContainsKey(pid))_integrity[pid]=Win32.IntegrityLevel(pid);
    }
    private void Run()
    {
        _threadId=Win32.GetCurrentThreadId();Win32.PeekMessage(out _,0,0,0,0);
        _mouse=Win32.SetWindowsHookEx(14,_mouseProc,Win32.GetModuleHandle(null),0);
        _keyboard=Win32.SetWindowsHookEx(13,_keyProc,Win32.GetModuleHandle(null),0);
        _ready.Set();
        try {
            while(Win32.GetMessage(out var msg,0,0,0)>0) {
                if(msg.message==Win32.WM_TICK) {
                    if(_gesture.IsPending) {
                        if(!_enabled || Win32.ModifiersPressed() || Win32.GetForegroundWindow()!=_request.Window)_gesture.Cancel();
                        else if(_gesture.Tick(Now)==GestureEffect.Trigger)Fire();
                    }
                } else { Win32.TranslateMessage(in msg);Win32.DispatchMessage(in msg); }
            }
        } finally { if(_mouse!=0)Win32.UnhookWindowsHookEx(_mouse);if(_keyboard!=0)Win32.UnhookWindowsHookEx(_keyboard); }
    }
    private nint MouseCallback(int code,nuint w,nint l)
    {
        if(code<0)return Win32.CallNextHookEx(_mouse,code,w,l);
        var data=Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(l);
        if(data.dwExtraInfo==Win32.InputMarker)return Win32.CallNextHookEx(_mouse,code,w,l);
        var message=(uint)w;
        if(message is Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN)PointerDown?.Invoke(data.pt);
        if(message==Win32.WM_RBUTTONDOWN) {
            var hwnd=Win32.GetForegroundWindow();Win32.GetWindowThreadProcessId(hwnd,out var pid);
            if(!_enabled || Win32.ModifiersPressed() || pid==(uint)Environment.ProcessId)
                return Win32.CallNextHookEx(_mouse,code,w,l);
            // The cached foreground probe handles most clicks. A native token query avoids
            // skipping the first gesture after a fast foreground switch; no UIA or I/O runs here.
            if(!_integrity.TryGetValue(pid,out var level))_integrity[pid]=level=Win32.IntegrityLevel(pid);
            if(level>_ownIntegrity)return Win32.CallNextHookEx(_mouse,code,w,l);
            var gui=new Win32.GUITHREADINFO { cbSize=(uint)Marshal.SizeOf<Win32.GUITHREADINFO>() };Win32.GetGUIThreadInfo(0,ref gui);
            _request=new(hwnd,gui.hwndFocus,data.pt);_gesture.HoldMilliseconds=Volatile.Read(ref _hold);
            if(_gesture.Down(data.pt.X,data.pt.Y,Now)==GestureEffect.Suppress) {
                _timer?.Dispose();_timer=new Timer(_=>Win32.PostThreadMessage(_threadId,Win32.WM_TICK,0,0),null,10,10);return 1;
            }
        } else if(message==Win32.WM_MOUSEMOVE) {
            if(_gesture.Move(data.pt.X,data.pt.Y)==GestureEffect.ReplayDown) {
                _timer?.Dispose();_timer=null;
                ReplayDragStart(_request.Position,data.pt);
            }
        } else if(message==Win32.WM_RBUTTONUP) {
            _timer?.Dispose();_timer=null;
            var effect=_gesture.Up(Now);
            if(effect==GestureEffect.ReplayClick){Win32.Send(Win32.Mouse(0x8),Win32.Mouse(0x10));return 1;}
            if(effect==GestureEffect.Trigger){if(_enabled && Win32.GetForegroundWindow()==_request.Window && !Win32.ModifiersPressed())Fire();return 1;}
            if(effect==GestureEffect.Suppress)return 1;
        }
        return Win32.CallNextHookEx(_mouse,code,w,l);
    }
    private static void ReplayDragStart(Win32.POINT origin,Win32.POINT current)
    {
        var screen=System.Windows.Forms.SystemInformation.VirtualScreen;
        Win32.INPUT Move(Win32.POINT p)=>Win32.Mouse(0x8000|0x4000|1,(int)((p.X-screen.Left)*65535.0/Math.Max(1,screen.Width-1)),(int)((p.Y-screen.Top)*65535.0/Math.Max(1,screen.Height-1)));
        Win32.Send(Move(origin),Win32.Mouse(8),Move(current));
    }
    private void Fire() { _timer?.Dispose();_timer=null;TranslateRequested?.Invoke(_request); }
    private nint KeyboardCallback(int code,nuint w,nint l)
    {
        if(code>=0) {
            var data=Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(l);
            if(data.vkCode==0x1B && data.dwExtraInfo!=Win32.InputMarker) {
                if((uint)w is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN && _popupVisible) { if(!_escapeHeld)EscapePressed?.Invoke();_escapeHeld=true;return 1; }
                if((uint)w is Win32.WM_KEYUP or Win32.WM_SYSKEYUP && _escapeHeld){_escapeHeld=false;return 1;}
            }
        }
        return Win32.CallNextHookEx(_keyboard,code,w,l);
    }
    public void Dispose() { _enabled=false;_permissionTimer.Dispose();_timer?.Dispose();if(_threadId!=0)Win32.PostThreadMessage(_threadId,Win32.WM_QUIT,0,0);_thread.Join(1000);_ready.Dispose(); }
}
