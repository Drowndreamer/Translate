using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Translate.Native;
namespace Translate.UI;
internal sealed class ThemeService : IDisposable
{
    internal static bool IsDark { get; private set; }
    private static bool _applied;
    private readonly Dispatcher _dispatcher;
    private int _refreshQueued;
    private volatile bool _disposed;
    public ThemeService() {_dispatcher=Application.Current.Dispatcher;Apply();SystemEvents.UserPreferenceChanged+=Changed;}
    private void Changed(object sender,UserPreferenceChangedEventArgs e)
    {
        if(_disposed||_dispatcher.HasShutdownStarted||Interlocked.Exchange(ref _refreshQueued,1)!=0)return;
        // Apply has an optional argument. Passing the method group to BeginInvoke creates
        // an Action<bool?> that Dispatcher cannot invoke without an argument.
        _dispatcher.InvokeAsync(()=> {
            Interlocked.Exchange(ref _refreshQueued,0);
            if(!_disposed)Apply();
        },DispatcherPriority.Background);
    }
    internal static void Apply(bool? forceDark=null)
    {
        bool dark=forceDark??((Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) as int?)==0);
        if(_applied&&dark==IsDark)return;
        IsDark=dark;_applied=true;
        string[] keys={"CanvasBrush","SurfaceBrush","TextBrush","MutedBrush","LineBrush","AccentBrush","AccentSoftBrush","ErrorBrush"};
        string[] colors=dark?new[]{"#161A21","#202630","#F1F4F8","#A5B1C2","#343E4D","#78A9F1","#283C59","#FF9B9B"}:new[]{"#F7F8FA","#FFFFFF","#162234","#667386","#DDE4ED","#2367D1","#EAF1FC","#BB3838"};
        for(int i=0;i<keys.Length;i++){var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(colors[i]));brush.Freeze();Application.Current.Resources[keys[i]]=brush;}
        foreach(Window window in Application.Current.Windows)ApplyChrome(window);
    }
    internal static void WatchWindow(Window window)=>window.SourceInitialized+=(_,_)=>ApplyChrome(window);
    private static void ApplyChrome(Window window)
    {
        var hwnd=new WindowInteropHelper(window).Handle;if(hwnd==0)return;int dark=IsDark?1:0;Win32.DwmSetWindowAttribute(hwnd,20,ref dark,sizeof(int));int corner=2;Win32.DwmSetWindowAttribute(hwnd,33,ref corner,sizeof(int));
    }
    public void Dispose(){_disposed=true;SystemEvents.UserPreferenceChanged-=Changed;}
}
