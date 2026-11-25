using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Translate.Core;
namespace Translate.UI;
public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly BufferedTextOutput _output;
    private CancellationTokenSource? _translation,_test;
    private long _generation;
    private bool _loading=true,_closed;
    internal bool AllowClose { get; set; }
    internal MainWindow(AppController controller)
    {
        _controller=controller;InitializeComponent();_output=new BufferedTextOutput(OutputBox);ThemeService.WatchWindow(this);
        LoadSettings();_loading=false;
        Closing+=OnClosing;Closed+=(_,_)=>{_closed=true;_output.Dispose();};
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
        _test?.Cancel();var operation=new CancellationTokenSource();_test=operation;
        var token=operation.Token;TestButton.IsEnabled=false;Status("正在验证连接…");
        try {
            var text=new StringBuilder();
            await foreach(var part in _controller.Client.TranslateAsync("Hello, world!",draft,token))text.Append(part);
            if(ReferenceEquals(_test,operation)&&!token.IsCancellationRequested&&!_closed)Status("连接成功。测试译文："+text+"。请保存设置后使用。");
        } catch(Exception error){if(ReferenceEquals(_test,operation)&&!token.IsCancellationRequested&&!_closed)Status(TranslationClient.FriendlyError(error),true);}
        finally{if(ReferenceEquals(_test,operation)){_test=null;if(!_closed)TestButton.IsEnabled=true;}operation.Dispose();}
    }
    private async void TranslateClicked(object sender,RoutedEventArgs e)
    {
        if(!_controller.Settings.IsConfigured){OpenSettings();Status("请先填写 API Key 并保存设置。",true);return;}
        var text=InputBox.Text.Trim();if(text.Length==0){Status("请先输入或粘贴中文或英文。",true);InputBox.Focus();return;}
        _translation?.Cancel();var operation=new CancellationTokenSource();_translation=operation;
        var token=operation.Token;var generation=++_generation;
        _output.Clear();OutputPlaceholder.Visibility=Visibility.Collapsed;TranslateButton.IsEnabled=false;CancelButton.Visibility=Visibility.Visible;CopyButton.IsEnabled=false;Status("正在翻译…");
        try {
            await foreach(var chunk in _controller.Client.TranslateAsync(text,_controller.Settings.Copy(),token)) {
                if(generation!=_generation||token.IsCancellationRequested)return;
                _output.Append(chunk);if(!CopyButton.IsEnabled)CopyButton.IsEnabled=true;
            }
            if(generation==_generation&&!token.IsCancellationRequested&&!_closed)Status("翻译完成。");
        } catch(Exception error){if(generation==_generation&&!_closed)Status(token.IsCancellationRequested?"已取消翻译。":TranslationClient.FriendlyError(error),!token.IsCancellationRequested);}
        finally {
            if(ReferenceEquals(_translation,operation))_translation=null;
            if(generation==_generation&&!_closed){_output.Flush();TranslateButton.IsEnabled=true;CancelButton.Visibility=Visibility.Collapsed;}
            operation.Dispose();
        }
    }
    private void InputChanged(object sender,TextChangedEventArgs e)
    {
        if(DirectionLabel!=null)DirectionLabel.Text=TranslationLanguage.Label(InputBox.Text);
        if(_translation!=null){_generation++;_translation.Cancel();_translation=null;_output.Clear();OutputPlaceholder.Visibility=Visibility.Visible;CopyButton.IsEnabled=false;TranslateButton.IsEnabled=true;CancelButton.Visibility=Visibility.Collapsed;Status("原文已更新，请重新翻译。");}
    }
    private void CancelClicked(object sender,RoutedEventArgs e)=>_translation?.Cancel();
    private void CopyClicked(object sender,RoutedEventArgs e) {try{_output.Flush();Clipboard.SetText(OutputBox.Text);Status("译文已复制。");}catch{Status("剪贴板正被占用，请稍后重试。",true);}}
    private void OnClosing(object? sender,CancelEventArgs e)
    {
        _translation?.Cancel();_test?.Cancel();
        if(!AllowClose){e.Cancel=true;Hide();}
        else{_generation++;_translation=null;_test=null;}
    }
}
