using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Translate.Core;
namespace Translate.UI;
public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private CancellationTokenSource? _translation,_test;
    private long _generation;
    private bool _loading=true;
    internal bool AllowClose { get; set; }
    internal MainWindow(AppController controller)
    {
        _controller=controller;InitializeComponent();ThemeService.WatchWindow(this);
        LoadSettings();_loading=false;
        Closing+=OnClosing;
    }
    internal void LoadSettings()
    {
        if(BaseUrlBox==null)return;
        _loading=true;var s=_controller.Settings;
        DeepSeekRadio.IsChecked=s.Provider=="DeepSeek";CustomRadio.IsChecked=s.Provider!="DeepSeek";
        BaseUrlBox.Text=s.BaseUrl;ModelBox.Text=s.Model;KeyBox.Password=s.ApiKey;
        VisibleKeyBox.Text=s.ApiKey;GestureEnabled.IsChecked=s.RightClickEnabled;HoldSlider.Value=s.HoldMilliseconds/1000.0;
        ProviderLabel.Text=s.IsConfigured ? s.Provider=="DeepSeek"?"DeepSeek":"自定义接口" : "尚未配置 API";
        _loading=false;
    }
    internal void OpenSettings() => SettingsNav.IsChecked=true;
    internal void Status(string text,bool error=false) {StatusLabel.Text=text;StatusLabel.SetResourceReference(TextBlock.ForegroundProperty,error?"ErrorBrush":"MutedBrush");}
    private void TranslationPageSelected(object sender,RoutedEventArgs e) {if(TranslationPage==null)return;TranslationPage.Visibility=Visibility.Visible;SettingsPage.Visibility=Visibility.Collapsed;}
    private void SettingsPageSelected(object sender,RoutedEventArgs e) {if(SettingsPage==null)return;TranslationPage.Visibility=Visibility.Collapsed;SettingsPage.Visibility=Visibility.Visible;}
    private void ProviderChanged(object sender,RoutedEventArgs e)
    {
        if(_loading || BaseUrlBox==null)return;
        if(DeepSeekRadio.IsChecked==true){BaseUrlBox.Text="https://api.deepseek.com";ModelBox.Text="deepseek-flash";}
        else {BaseUrlBox.Text="";ModelBox.Text="";}
    }
    private void HoldChanged(object sender,RoutedPropertyChangedEventArgs<double> e) {if(HoldLabel!=null)HoldLabel.Text=e.NewValue.ToString("0.0",CultureInfo.InvariantCulture)+" 秒";}
    private void RevealChanged(object sender,RoutedEventArgs e)
    {
        if(KeyBox==null)return;
        if(RevealKey.IsChecked==true){VisibleKeyBox.Text=KeyBox.Password;VisibleKeyBox.Visibility=Visibility.Visible;KeyBox.Visibility=Visibility.Collapsed;}
        else{KeyBox.Password=VisibleKeyBox.Text;VisibleKeyBox.Visibility=Visibility.Collapsed;KeyBox.Visibility=Visibility.Visible;}
    }
    private AppSettings Draft()
    {
        var s=new AppSettings {Provider=DeepSeekRadio.IsChecked==true?"DeepSeek":"Custom",BaseUrl=BaseUrlBox.Text.Trim(),Model=ModelBox.Text.Trim(),ApiKey=(RevealKey.IsChecked==true?VisibleKeyBox.Text:KeyBox.Password).Trim(),RightClickEnabled=GestureEnabled.IsChecked==true,HoldMilliseconds=(int)Math.Round(HoldSlider.Value*1000)};
        s.Validate();return s;
    }
    private void SaveClicked(object sender,RoutedEventArgs e)
    {
        try {_controller.SaveSettings(Draft());ProviderLabel.Text=_controller.Settings.Provider=="DeepSeek"?"DeepSeek":"自定义接口";Status("设置已保存。选中文字，按住右键即可翻译。");}
        catch(Exception error){Status(error is ArgumentException?error.Message:"设置无法保存，请检查本机文件权限。",true);}
    }
    private async void TestClicked(object sender,RoutedEventArgs e)
    {
        AppSettings draft;try{draft=Draft();}catch(Exception error){Status(error.Message,true);return;}
        _test?.Cancel();_test=new();var token=_test.Token;TestButton.IsEnabled=false;Status("正在验证连接…");
        try {var text="";await foreach(var part in _controller.Client.TranslateAsync("Hello, world!",draft,token))text+=part;Status("连接成功。测试译文："+text+"。请保存设置后使用。");}
        catch(Exception error){if(!token.IsCancellationRequested)Status(TranslationClient.FriendlyError(error),true);}
        finally{TestButton.IsEnabled=true;}
    }
    private async void TranslateClicked(object sender,RoutedEventArgs e)
    {
        if(!_controller.Settings.IsConfigured){OpenSettings();Status("请先填写 API Key 并保存设置。",true);return;}
        var text=InputBox.Text.Trim();if(text.Length==0){Status("请先输入或粘贴中文或英文。",true);InputBox.Focus();return;}
        _translation?.Cancel();_translation=new();var token=_translation.Token;var generation=++_generation;
        OutputBox.Clear();OutputPlaceholder.Visibility=Visibility.Collapsed;TranslateButton.IsEnabled=false;CancelButton.Visibility=Visibility.Visible;CopyButton.IsEnabled=false;Status("正在翻译…");
        try {
            await foreach(var chunk in _controller.Client.TranslateAsync(text,_controller.Settings.Copy(),token)) {
                if(generation!=_generation)return;OutputBox.AppendText(chunk);CopyButton.IsEnabled=OutputBox.Text.Length>0;
            }
            if(generation==_generation)Status("翻译完成。");
        } catch(Exception error){if(generation==_generation)Status(token.IsCancellationRequested?"已取消翻译。":TranslationClient.FriendlyError(error),!token.IsCancellationRequested);}
        finally{if(generation==_generation){TranslateButton.IsEnabled=true;CancelButton.Visibility=Visibility.Collapsed;}}
    }
    private void InputChanged(object sender,TextChangedEventArgs e)
    {
        if(DirectionLabel!=null)DirectionLabel.Text=TranslationLanguage.Label(InputBox.Text);
    }
    private void CancelClicked(object sender,RoutedEventArgs e)=>_translation?.Cancel();
    private void CopyClicked(object sender,RoutedEventArgs e) {try{Clipboard.SetText(OutputBox.Text);Status("译文已复制。");}catch{Status("剪贴板正被占用，请稍后重试。",true);}}
    private void OnClosing(object? sender,CancelEventArgs e) {if(!AllowClose){e.Cancel=true;Hide();}else{_translation?.Cancel();_test?.Cancel();}}
}
