using System.Runtime.InteropServices;
namespace Translate.Native;
internal static class Win32
{
    internal const uint WM_MOUSEMOVE=0x200, WM_LBUTTONDOWN=0x201, WM_RBUTTONDOWN=0x204, WM_RBUTTONUP=0x205, WM_MBUTTONDOWN=0x207, WM_KEYDOWN=0x100, WM_KEYUP=0x101, WM_SYSKEYDOWN=0x104, WM_SYSKEYUP=0x105, WM_QUIT=0x12, WM_TIMER=0x113, WM_HOOKERROR=0x800C;
    internal static readonly nuint InputMarker = 0x5452414E;
    internal delegate bool EnumWindowsProc(nint hwnd,nint parameter);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumWindowsProc callback,nint parameter);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern int GetWindowText(nint hwnd,System.Text.StringBuilder text,int count);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd,uint message,nuint w,nint l);
    internal delegate nint HookProc(int code, nuint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)] internal struct POINT { public int X,Y; public POINT(int x,int y) { X=x;Y=y; } }
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct MSG { public nint hwnd; public uint message; public nuint wParam; public nint lParam; public uint time; public POINT pt; public uint lPrivate; }
    [StructLayout(LayoutKind.Sequential)] internal struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData,flags,time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KBDLLHOOKSTRUCT { public uint vkCode,scanCode,flags,time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO { public uint cbSize,flags; public nint hwndActive,hwndFocus,hwndCapture,hwndMenuOwner,hwndMoveSize,hwndCaret; public RECT rcCaret; }
    [StructLayout(LayoutKind.Sequential)] internal struct MONITORINFO { public uint cbSize; public RECT rcMonitor,rcWork; public uint dwFlags; }
    [StructLayout(LayoutKind.Sequential)] internal struct MOUSEINPUT { public int dx,dy; public uint mouseData,dwFlags,time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Sequential)] internal struct KEYBDINPUT { public ushort wVk,wScan; public uint dwFlags,time; public nuint dwExtraInfo; }
    [StructLayout(LayoutKind.Explicit)] internal struct INPUTUNION { [FieldOffset(0)] public MOUSEINPUT mi; [FieldOffset(0)] public KEYBDINPUT ki; }
    [StructLayout(LayoutKind.Sequential)] internal struct INPUT { public uint type; public INPUTUNION data; }
    [DllImport("user32.dll",SetLastError=true)] internal static extern nint SetWindowsHookEx(int id,HookProc proc,nint module,uint threadId);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] internal static extern nint CallNextHookEx(nint hook,int code,nuint w,nint l);
    [DllImport("user32.dll")] internal static extern int GetMessage(out MSG msg,nint hwnd,uint min,uint max);
    [DllImport("user32.dll")] internal static extern bool PeekMessage(out MSG msg,nint hwnd,uint min,uint max,uint remove);
    [DllImport("user32.dll")] internal static extern bool TranslateMessage(in MSG msg);
    [DllImport("user32.dll")] internal static extern nint DispatchMessage(in MSG msg);
    [DllImport("user32.dll",SetLastError=true)] internal static extern nuint SetTimer(nint hwnd,nuint id,uint milliseconds,nint callback);
    [DllImport("user32.dll")] internal static extern bool KillTimer(nint hwnd,nuint id);
    [DllImport("user32.dll")] internal static extern bool PostThreadMessage(uint thread,uint message,nuint w,nint l);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] internal static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out POINT point);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO info);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint hwnd,out uint pid);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,INPUT[] inputs,int size);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] internal static extern nint GetClipboardOwner();
    [DllImport("user32.dll")] internal static extern nint MonitorFromPoint(POINT p,uint flags);
    [DllImport("user32.dll")] internal static extern bool GetMonitorInfo(nint monitor,ref MONITORINFO info);
    [DllImport("shcore.dll")] internal static extern int GetDpiForMonitor(nint monitor,int type,out uint x,out uint y);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint hwnd,nint after,int x,int y,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(nint hwnd,out RECT rect);
    [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(nint hwnd,int attribute,ref int value,int size);
    [DllImport("kernel32.dll",SetLastError=true)] internal static extern nint OpenProcess(uint access,bool inherit,uint pid);
    [DllImport("kernel32.dll")] internal static extern bool CloseHandle(nint handle);
    [DllImport("advapi32.dll",SetLastError=true)] internal static extern bool OpenProcessToken(nint process,uint access,out nint token);
    [DllImport("advapi32.dll",SetLastError=true)] internal static extern bool GetTokenInformation(nint token,int type,nint info,int length,out int returned);
    [DllImport("advapi32.dll")] internal static extern nint GetSidSubAuthorityCount(nint sid);
    [DllImport("advapi32.dll")] internal static extern nint GetSidSubAuthority(nint sid,uint index);
    internal static bool ModifiersPressed() => (GetAsyncKeyState(0x10)&0x8000)!=0 || (GetAsyncKeyState(0x11)&0x8000)!=0 || (GetAsyncKeyState(0x12)&0x8000)!=0 || (GetAsyncKeyState(0x5B)&0x8000)!=0 || (GetAsyncKeyState(0x5C)&0x8000)!=0;
    internal static INPUT Mouse(uint flags,int x=0,int y=0) => new() { type=0, data=new INPUTUNION { mi=new MOUSEINPUT { dx=x,dy=y,dwFlags=flags,dwExtraInfo=InputMarker } } };
    internal static INPUT Key(ushort key,bool up=false) => new() { type=1,data=new INPUTUNION { ki=new KEYBDINPUT { wVk=key,dwFlags=up?2u:0u,dwExtraInfo=InputMarker } } };
    internal static bool Send(params INPUT[] inputs) => SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<INPUT>())==(uint)inputs.Length;
    internal static int IntegrityLevel(uint pid)
    {
        nint process=OpenProcess(0x1000,false,pid), token=0,buffer=0;
        if(process==0)return int.MaxValue;
        try {
            if(!OpenProcessToken(process,8,out token))return int.MaxValue;
            GetTokenInformation(token,25,0,0,out var size);
            buffer=Marshal.AllocHGlobal(size);
            if(!GetTokenInformation(token,25,buffer,size,out _))return int.MaxValue;
            var sid=Marshal.ReadIntPtr(buffer);var count=Marshal.ReadByte(GetSidSubAuthorityCount(sid));
            return Marshal.ReadInt32(GetSidSubAuthority(sid,(uint)(count-1)));
        } finally { if(buffer!=0)Marshal.FreeHGlobal(buffer);if(token!=0)CloseHandle(token);CloseHandle(process); }
    }
}
