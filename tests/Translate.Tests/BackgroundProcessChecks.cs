using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Translate.Native;
namespace Translate.Tests;
internal static partial class Program
{
    [DllImport("user32.dll",EntryPoint="SendMessageTimeoutW",SetLastError=true)]
    private static extern nint SendMessageTimeout(nint hwnd,uint message,nuint w,nint l,uint flags,uint milliseconds,out nuint result);
    private static async Task BackgroundProcessChecks(int processId)
    {
        using var process=Process.GetProcessById(processId);
        Check(process.ProcessName=="Translate","background notification target is a Translate process");
        var started=process.StartTime;
        void EnsureHealthy()
        {
            process.Refresh();
            if(process.HasExited||process.Id!=processId||process.StartTime!=started)
                throw new Exception("FAIL: target process exited or changed during background notifications");
            if(!process.Responding)
                throw new Exception("FAIL: target process stopped responding during background notifications");
        }
        EnsureHealthy();
        var windows=new List<nint>();
        bool enumerated=Win32.EnumWindows((hwnd,_)=> {
            Win32.GetWindowThreadProcessId(hwnd,out var owner);
            if(owner!=(uint)processId||Win32.IsWindowVisible(hwnd))return true;
            var title=new StringBuilder(512);
            Win32.GetWindowText(hwnd,title,title.Capacity);
            if(title.ToString().StartsWith(".NET-BroadcastEventWindow.",StringComparison.Ordinal))windows.Add(hwnd);
            return true;
        },0);
        Check(enumerated&&windows.Count>0,"target process has a hidden SystemEvents window");
        const int notifications=2000,batchSize=50;
        for(int sent=0;sent<notifications;sent+=batchSize) {
            EnsureHealthy();
            for(int index=0;index<batchSize;index++) {
                var hwnd=windows[(sent+index)%windows.Count];
                Win32.GetWindowThreadProcessId(hwnd,out var owner);
                // WM_SETTINGCHANGE has a pointer parameter and requires synchronous
                // cross-process marshalling, even when that parameter is null.
                if(owner!=(uint)processId||Win32.IsWindowVisible(hwnd)||SendMessageTimeout(hwnd,0x001A,0,0,0x22,1000,out _)==0)
                    throw new Exception("FAIL: target SystemEvents window disappeared or rejected WM_SETTINGCHANGE");
            }
            await Task.Delay(125);
            EnsureHealthy();
        }
        await Task.Delay(500);
        EnsureHealthy();
        Check(true,"production process remains alive and responding after 2000 background preference notifications");
    }
}
