using System.Windows;
using Translate.Native;
using Translate.UI;
using Forms=System.Windows.Forms;
namespace Translate.Core;
internal sealed class AppController : IDisposable
{
    private readonly SettingsStore _store;
    private readonly SelectionReader _reader=new();
    private readonly ThemeService _theme;
    private MouseHook? _hook;
    private Forms.NotifyIcon? _tray;
    private Forms.ToolStripMenuItem? _toggle;
    private MainWindow? _main;
    private readonly TranslationWindow _popup;
    private CancellationTokenSource? _translation;
    private long _generation;
    private bool _disposed;
    public AppSettings Settings { get; private set; }
    public TranslationClient Client { get; }
    internal TranslationWindow Popup=>_popup;
    internal MainWindow Main=>_main??=new MainWindow(this);
    internal AppController(SettingsStore? store=null,HttpClient? http=null)
    {
        _store=store??new SettingsStore();Settings=_store.Load();Client=new TranslationClient(http);_theme=new ThemeService();
        _popup=new TranslationWindow();
        _popup.Dismissed+=CancelPopup;
        _popup.TranslationRequested+=StartTranslation;
        _popup.SourceEdited+=()=>{CancelPopup();SetPopupVisible();};
        _popup.ConfigureRequested+=()=>ShowMain(true);
    }
    internal void Start()
    {
        _hook=new MouseHook();_hook.Configure(Settings.RightClickEnabled,Settings.HoldMilliseconds);
        _hook.TranslateRequested+=request=>Application.Current.Dispatcher.BeginInvoke(()=>BeginSelection(request));
        _hook.PointerDown+=point=>Application.Current.Dispatcher.BeginInvoke(()=>{if(_popup.IsVisible&&!_popup.Contains(point))_popup.Dismiss();});
        _hook.EscapePressed+=()=>Application.Current.Dispatcher.BeginInvoke(_popup.Dismiss);
        CreateTray();ShowMain(!Settings.IsConfigured);
        if(_store.LoadWarning!=null)Main.Status(_store.LoadWarning,true);
        if(_hook.StartupError!=null)Main.Status(_hook.StartupError,true);
    }
    internal void ShowMain(bool settings=false)
    {
        if(_disposed)return;
        var main=Main;if(settings)main.OpenSettings();main.Show();if(main.WindowState==WindowState.Minimized)main.WindowState=WindowState.Normal;main.Activate();
    }
    internal void SaveSettings(AppSettings settings)
    {
        _store.Save(settings);Settings=settings.Copy();_hook?.Configure(settings.RightClickEnabled,settings.HoldMilliseconds);
        if(_toggle!=null)_toggle.Checked=settings.RightClickEnabled;
    }
    private void CreateTray()
    {
        using var iconStream=Application.GetResourceStream(new Uri("pack://application:,,,/Translate;component/Assets/translate.ico"))!.Stream;
        _tray=new Forms.NotifyIcon {Icon=new System.Drawing.Icon(iconStream),Text="Translate · 右键划词翻译",Visible=true};
        var menu=new Forms.ContextMenuStrip();
        menu.Items.Add("打开 Translate",null,(_,_)=>ShowMain());menu.Items.Add("设置",null,(_,_)=>ShowMain(true));
        _toggle=new Forms.ToolStripMenuItem("启用右键翻译") {Checked=Settings.RightClickEnabled};
        _toggle.Click+=(_,_)=>{try{var s=Settings.Copy();s.RightClickEnabled=!s.RightClickEnabled;SaveSettings(s);_main?.LoadSettings();}catch{ShowMain(true);Main.Status("设置无法保存，请检查本机文件权限。",true);}};
        menu.Items.Add(_toggle);menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("退出",null,(_,_)=>Exit());
        _tray.ContextMenuStrip=menu;_tray.DoubleClick+=(_,_)=>ShowMain();
    }
    private async void BeginSelection(SelectionRequest request)
    {
        if(_disposed)return;
        CancelPopup();_translation=new();var token=_translation.Token;var generation=_generation;
        if(!Settings.IsConfigured){_popup.Open(request.Position);SetPopupVisible();_popup.Error("请先填写 API 地址和 Key。",true);return;}
        // UIA / simulated copy must finish while the original selection still has focus.
        try {
            var read=_reader.ReadAsync(request,token);
            _popup.Open(request.Position);SetPopupVisible();
            var text=await read;
            if(generation!=_generation || token.IsCancellationRequested)return;
            if(string.IsNullOrWhiteSpace(text)){_popup.Error("没有读取到选中文字。请重新选中，或直接在上方输入翻译。");return;}
            _popup.SetSource(text);await RunTranslation(text,token,generation);
        } catch(Exception error){if(generation==_generation&&!token.IsCancellationRequested)_popup.Error(error is InvalidOperationException?error.Message:TranslationClient.FriendlyError(error));}
    }
    private void SetPopupVisible(){if(_hook!=null)_hook.PopupVisible=true;}
    private async void StartTranslation(string text)
    {
        if(string.IsNullOrWhiteSpace(text))return;
        if(!Settings.IsConfigured){_popup.Error("请先填写 API 地址和 Key。",true);return;}
        CancelPopup();_translation=new();var token=_translation.Token;var generation=_generation;SetPopupVisible();
        _popup.Reset(text);_popup.SetSource(text);await RunTranslation(text,token,generation);
    }
    private async Task RunTranslation(string text,CancellationToken token,long generation)
    {
        try {
            await foreach(var chunk in Client.TranslateAsync(text,Settings.Copy(),token)){if(generation!=_generation)return;_popup.Append(chunk);}
            if(generation==_generation)_popup.Complete();
        }catch(Exception error){if(generation==_generation&&!token.IsCancellationRequested)_popup.Error(TranslationClient.FriendlyError(error));}
    }
    private void CancelPopup(){_translation?.Cancel();_translation?.Dispose();_translation=null;_generation++;if(_hook!=null)_hook.PopupVisible=false;}
    internal void Exit(){Dispose();Application.Current.Shutdown();}
    public void Dispose()
    {
        if(_disposed)return;_disposed=true;CancelPopup();_hook?.Dispose();
        if(_tray!=null){_tray.Visible=false;_tray.ContextMenuStrip?.Dispose();_tray.Icon?.Dispose();_tray.Dispose();}
        if(_main!=null){_main.AllowClose=true;_main.Close();}
        _popup.AllowClose=true;_popup.Close();_theme.Dispose();Client.Dispose();
    }
}
