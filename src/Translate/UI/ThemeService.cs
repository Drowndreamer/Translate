using Microsoft.Win32;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Translate.Native;
namespace Translate.UI;
internal sealed class ThemeService : IDisposable
{
    internal static bool IsDark { get; private set; }
    private readonly System.Windows.Threading.Dispatcher _dispatcher;
    public ThemeService() {_dispatcher=Application.Current.Dispatcher;Apply();SystemEvents.UserPreferenceChanged+=Changed;}
    private void Changed(object sender,UserPreferenceChangedEventArgs e){if(!_dispatcher.HasShutdownStarted)_dispatcher.BeginInvoke(Apply);}
    internal static void Apply(bool? forceDark=null)
    {
        bool dark=forceDark??((Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize","AppsUseLightTheme",1) as int?)==0);
        IsDark=dark;
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
    public void Dispose()=>SystemEvents.UserPreferenceChanged-=Changed;
}
