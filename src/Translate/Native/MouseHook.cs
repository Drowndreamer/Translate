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
    private readonly int _ownIntegrity=Win32.IntegrityLevel((uint)Environment.ProcessId);
    private nint _mouse,_keyboard;
    private nuint _holdTimer;
    private uint _threadId;
    private volatile bool _enabled=true,_popupVisible,_stopping;
    private int _hold=300,_disposed;
    private bool _escapeHeld;
    private Exception? _callbackFailure;
    private SelectionRequest _request;
    public event Action<SelectionRequest>? TranslateRequested;
    public event Action<Win32.POINT>? PointerDown;
    public event Action? EscapePressed;
    public string? StartupError { get; private set; }
    private static double Now => Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
    public MouseHook()
    {
        _mouseProc=MouseCallback;_keyProc=KeyboardCallback;
        _thread=new Thread(Run){IsBackground=true,Name="Translate input"};_thread.Start();
        _ready.Wait(TimeSpan.FromSeconds(3));
        if(_mouse==0)StartupError="全局鼠标监听未能启动，可以先使用手动翻译。";
        else if(_keyboard==0)StartupError="Esc 监听未能启动，可以点击浮窗外关闭。";
    }
    public void Configure(bool enabled,int hold) { _enabled=enabled;Volatile.Write(ref _hold,Math.Clamp(hold,200,2000)); }
    public bool PopupVisible { set => _popupVisible=value; }
    private void Run()
    {
        try {
            _threadId=Win32.GetCurrentThreadId();Win32.PeekMessage(out _,0,0,0,0);
            if(_stopping)return;
            _mouse=Win32.SetWindowsHookEx(14,_mouseProc,Win32.GetModuleHandle(null),0);
            _keyboard=Win32.SetWindowsHookEx(13,_keyProc,Win32.GetModuleHandle(null),0);
            _ready.Set();
            while(!_stopping&&Win32.GetMessage(out var msg,0,0,0)>0) {
                if(msg.message==Win32.WM_TIMER&&msg.wParam==_holdTimer&&_holdTimer!=0) {

                    if(!_gesture.IsPending){StopHoldTimer();continue;}
                    if(!_enabled||Win32.ModifiersPressed()||Win32.GetForegroundWindow()!=_request.Window){_gesture.Cancel();StopHoldTimer();}
                    else if(_gesture.Tick(Now)==GestureEffect.Trigger)Fire();
                    // A stale timer message can arrive just after another down. Recheck
                    // every 10ms only in that rare case; normal holds use one timer tick.
                    else {_holdTimer=Win32.SetTimer(0,_holdTimer,10,0);}
                } else if(msg.message==Win32.WM_HOOKERROR) {
                    var failure=Interlocked.Exchange(ref _callbackFailure,null);
                    if(failure!=null)AppDiagnostics.Record("input-hook",failure);
                } else { Win32.TranslateMessage(in msg);Win32.DispatchMessage(in msg); }
            }
        } catch(Exception error) {StartupError="全局输入监听已停止，可以先使用手动翻译。";AppDiagnostics.Record("input-thread",error);}
        finally {
            StopHoldTimer();
            if(_mouse!=0)Win32.UnhookWindowsHookEx(_mouse);if(_keyboard!=0)Win32.UnhookWindowsHookEx(_keyboard);
            _ready.Set();
        }
    }
    private void StopHoldTimer(){if(_holdTimer!=0){Win32.KillTimer(0,_holdTimer);_holdTimer=0;}}
    private void CallbackFailed(Exception error)
    {
        // No logging, UIA, clipboard work, waits, or network I/O in a native callback.
        Interlocked.CompareExchange(ref _callbackFailure,error,null);
        Win32.PostThreadMessage(_threadId,Win32.WM_HOOKERROR,0,0);
    }
    private nint MouseCallback(int code,nuint w,nint l)
    {
        try{return HandleMouse(code,w,l);}
        catch(Exception error){CallbackFailed(error);StopHoldTimer();_gesture.Up(Now);return Win32.CallNextHookEx(_mouse,code,w,l);}
    }
    private nint HandleMouse(int code,nuint w,nint l)
    {
        if(code<0||_stopping)return Win32.CallNextHookEx(_mouse,code,w,l);
        var message=(uint)w;
        if(message is not (Win32.WM_MOUSEMOVE or Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_RBUTTONUP or Win32.WM_MBUTTONDOWN))return Win32.CallNextHookEx(_mouse,code,w,l);
        if(message==Win32.WM_MOUSEMOVE&&!_gesture.IsPending)return Win32.CallNextHookEx(_mouse,code,w,l);
        var data=Marshal.PtrToStructure<Win32.MSLLHOOKSTRUCT>(l);
        if(data.dwExtraInfo==Win32.InputMarker)return Win32.CallNextHookEx(_mouse,code,w,l);
        if(_popupVisible&&message is (Win32.WM_LBUTTONDOWN or Win32.WM_RBUTTONDOWN or Win32.WM_MBUTTONDOWN))PointerDown?.Invoke(data.pt);
        if(message==Win32.WM_RBUTTONDOWN) {
            if(!_enabled||Win32.ModifiersPressed())return Win32.CallNextHookEx(_mouse,code,w,l);
            var hwnd=Win32.GetForegroundWindow();Win32.GetWindowThreadProcessId(hwnd,out var pid);
            if(pid==0||pid==(uint)Environment.ProcessId)return Win32.CallNextHookEx(_mouse,code,w,l);
            // Query only an eligible right-down. No idle polling, unbounded PID cache,
            // or stale permission entry when Windows reuses a process ID.
            var level=Win32.IntegrityLevel(pid);if(level>_ownIntegrity)return Win32.CallNextHookEx(_mouse,code,w,l);
            var gui=new Win32.GUITHREADINFO { cbSize=(uint)Marshal.SizeOf<Win32.GUITHREADINFO>() };Win32.GetGUIThreadInfo(0,ref gui);
            _request=new(hwnd,gui.hwndFocus,data.pt);_gesture.HoldMilliseconds=Volatile.Read(ref _hold);
            if(_gesture.Down(data.pt.X,data.pt.Y,Now)==GestureEffect.Suppress) {
                StopHoldTimer();_holdTimer=Win32.SetTimer(0,0,(uint)_gesture.HoldMilliseconds,0);return 1;
            }
        } else if(message==Win32.WM_MOUSEMOVE) {
            if(_gesture.Move(data.pt.X,data.pt.Y)==GestureEffect.ReplayDown) {StopHoldTimer();ReplayDragStart(_request.Position,data.pt);}
        } else if(message==Win32.WM_RBUTTONUP) {
            StopHoldTimer();var effect=_gesture.Up(Now);
            if(effect==GestureEffect.ReplayClick){Win32.Send(Win32.Mouse(0x8),Win32.Mouse(0x10));return 1;}
            if(effect==GestureEffect.Trigger){if(_enabled&&Win32.GetForegroundWindow()==_request.Window&&!Win32.ModifiersPressed())Fire();return 1;}
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
    private void Fire(){StopHoldTimer();TranslateRequested?.Invoke(_request);}
    private nint KeyboardCallback(int code,nuint w,nint l)
    {
        try {
            if(code>=0&&!_stopping) {
                var data=Marshal.PtrToStructure<Win32.KBDLLHOOKSTRUCT>(l);
                if(data.vkCode==0x1B&&data.dwExtraInfo!=Win32.InputMarker) {
                    if((uint)w is Win32.WM_KEYDOWN or Win32.WM_SYSKEYDOWN&&_popupVisible){if(!_escapeHeld)EscapePressed?.Invoke();_escapeHeld=true;return 1;}
                    if((uint)w is Win32.WM_KEYUP or Win32.WM_SYSKEYUP&&_escapeHeld){_escapeHeld=false;return 1;}
                }
            }
            return Win32.CallNextHookEx(_keyboard,code,w,l);
        } catch(Exception error){CallbackFailed(error);return Win32.CallNextHookEx(_keyboard,code,w,l);}
    }
    public void Dispose()
    {
        if(Interlocked.Exchange(ref _disposed,1)!=0)return;
        _enabled=false;_stopping=true;
        if(_threadId!=0)Win32.PostThreadMessage(_threadId,Win32.WM_QUIT,0,0);
        if(_thread.Join(1000))_ready.Dispose();
    }
}
