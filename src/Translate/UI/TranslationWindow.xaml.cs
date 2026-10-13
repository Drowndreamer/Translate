using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using Translate.Native;
using Translate.Core;
namespace Translate.UI;
public partial class TranslationWindow : Window
{
    internal event Action? Dismissed;
    internal event Action<string>? TranslationRequested;
    internal event Action? SourceEdited;
    internal event Action? ConfigureRequested;
    internal bool AllowClose { get; set; }
    private Win32.POINT _anchor;
    private bool _settingSource,_composing;
    private Size? _preferredSize;
    private Win32.RECT _sizeBeforeDrag;
    internal TranslationWindow()
    {
        InitializeComponent();ThemeService.WatchWindow(this);Closing+=OnClosing;
        SourceInitialized+=(_,_)=>HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WindowMessage);
        TextCompositionManager.AddPreviewTextInputStartHandler(SourceBox,(_,_)=>_composing=true);
        TextCompositionManager.AddPreviewTextInputHandler(SourceBox,(_,_)=>_composing=false);
        SourceBox.LostKeyboardFocus+=(_,_)=>_composing=false;
    }
    private nint WindowMessage(nint hwnd,int message,nint w,nint l,ref bool handled)
    {
        if(message==0x231)Win32.GetWindowRect(hwnd,out _sizeBeforeDrag); // WM_ENTERSIZEMOVE
        else if(message==0x232&&Win32.GetWindowRect(hwnd,out var after)&&
            (after.Right-after.Left!=_sizeBeforeDrag.Right-_sizeBeforeDrag.Left||after.Bottom-after.Top!=_sizeBeforeDrag.Bottom-_sizeBeforeDrag.Top))
            _preferredSize=new Size(ActualWidth,ActualHeight); // Only a user resize is remembered.
        return 0;
    }
    internal void Open(Win32.POINT anchor,string source="")
    {
        _anchor=anchor;Reset(source);
        Width=_preferredSize?.Width??420;Height=_preferredSize?.Height??SourceHeight(source);
        Show();Place(anchor);
    }
    private static double SourceHeight(string text)=>text.Length<=24?220:text.Length<=100?270:360;
    internal void SetSource(string text)
    {
        SetSourceText(text);RetryButton.IsEnabled=!string.IsNullOrWhiteSpace(text);MessageText.Text="正在翻译…";
        var desired=SourceHeight(text);
        if(IsVisible&&_preferredSize==null&&Math.Abs(Height-desired)>1){Height=desired;Place(_anchor);}
    }
    private void SetSourceText(string text)
    {
        _settingSource=true;
        try{if(SourceBox.Text!=text)SourceBox.Text=text;DirectionLabel.Text=TranslationLanguage.Label(text);}
        finally{_settingSource=false;}
    }
    internal void Reset(string source)
    {
        SetSourceText(source);ClearResult();MessageText.Text="正在读取选中文字…";RetryButton.IsEnabled=!string.IsNullOrWhiteSpace(source);
    }
    private void ClearResult()
    {
        ResultBox.Clear();StateText.Text="";StateText.ToolTip=null;MessagePanel.Visibility=Visibility.Visible;
        MessageText.SetResourceReference(TextBlock.ForegroundProperty,"MutedBrush");ConfigureButton.Visibility=Visibility.Collapsed;CopyResultButton.IsEnabled=false;
    }
    private void SourceChanged(object sender,TextChangedEventArgs e)
    {
        if(_settingSource||DirectionLabel==null||ResultBox==null)return;
        SourceEdited?.Invoke();DirectionLabel.Text=TranslationLanguage.Label(SourceBox.Text);ClearResult();
        MessageText.Text="输入中文或英文，按 Enter 翻译。";StateText.Text="Shift+Enter 换行";
        RetryButton.IsEnabled=!string.IsNullOrWhiteSpace(SourceBox.Text);
    }
    private void SourceKeyDown(object sender,KeyEventArgs e)
    {
        // Let IME consume candidate-confirming Enter. Shift+Enter belongs to TextBox.
        if(e.Key!=Key.Enter||_composing||e.IsRepeat||Keyboard.Modifiers!=ModifierKeys.None)return;
        e.Handled=true;SubmitSource();
    }
    private void SubmitSource()
    {
        var text=SourceBox.Text.Trim();
        if(text.Length==0){Error("请先输入中文或英文。");return;}
        TranslationRequested?.Invoke(text);
    }
    internal void Append(string chunk){MessagePanel.Visibility=Visibility.Collapsed;ResultBox.AppendText(chunk);CopyResultButton.IsEnabled=ResultBox.Text.Length>0;StateText.Text="正在翻译…";}
    internal void Complete()=>StateText.Text="翻译完成";
    internal void Error(string message,bool configure=false)
    {
        if(ResultBox.Text.Length==0){MessagePanel.Visibility=Visibility.Visible;MessageText.Text=message;MessageText.SetResourceReference(TextBlock.ForegroundProperty,"ErrorBrush");}
        else StateText.Text=message;
        ConfigureButton.Visibility=configure?Visibility.Visible:Visibility.Collapsed;StateText.ToolTip=message;
    }
    internal bool Contains(Win32.POINT p){var hwnd=new WindowInteropHelper(this).Handle;return IsVisible&&Win32.GetWindowRect(hwnd,out var r)&&p.X>=r.Left&&p.X<r.Right&&p.Y>=r.Top&&p.Y<r.Bottom;}
    private void Place(Win32.POINT p)
    {
        var monitor=Win32.MonitorFromPoint(p,2);var info=new Win32.MONITORINFO{cbSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>()};Win32.GetMonitorInfo(monitor,ref info);
        Win32.GetDpiForMonitor(monitor,0,out var dpi,out _);if(dpi==0)dpi=96;
        double scale=dpi/96.0;var width=Math.Min((int)Math.Ceiling(Width*scale),info.rcWork.Right-info.rcWork.Left);var height=Math.Min((int)Math.Ceiling(Height*scale),info.rcWork.Bottom-info.rcWork.Top);
        int x=p.X+16,y=p.Y+20;
        if(x+width>info.rcWork.Right)x=p.X-width-12;if(y+height>info.rcWork.Bottom)y=p.Y-height-12;
        x=Math.Clamp(x,info.rcWork.Left,Math.Max(info.rcWork.Left,info.rcWork.Right-width));y=Math.Clamp(y,info.rcWork.Top,Math.Max(info.rcWork.Top,info.rcWork.Bottom-height));
        Win32.SetWindowPos(new WindowInteropHelper(this).Handle,new nint(-1),x,y,width,height,0x10|0x40);
    }
    internal void Dismiss(){if(IsVisible)Hide();_composing=false;Dismissed?.Invoke();}
    private void CloseClicked(object sender,RoutedEventArgs e)=>Dismiss();
    private void RetryClicked(object sender,RoutedEventArgs e)=>SubmitSource();
    private void ConfigureClicked(object sender,RoutedEventArgs e){Dismiss();ConfigureRequested?.Invoke();}
    private void CopyClicked(object sender,RoutedEventArgs e){try{Clipboard.SetText(ResultBox.Text);StateText.Text="已复制";}catch{StateText.Text="剪贴板正被占用";}}
    private void HeaderDragged(object sender,MouseButtonEventArgs e)
    {
        var element=e.OriginalSource as DependencyObject;
        while(element!=null){if(element is Button)return;element=VisualTreeHelper.GetParent(element);}
        try{DragMove();}catch(InvalidOperationException){}
    }
    private void OnClosing(object? sender,CancelEventArgs e){if(!AllowClose){e.Cancel=true;Dismiss();}}
}
