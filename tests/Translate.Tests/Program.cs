using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Translate.Core;
using Translate.Native;
using Translate.UI;
using Application=System.Windows.Application;
using Clipboard=System.Windows.Clipboard;
using DataObject=System.Windows.DataObject;
using TextBox=System.Windows.Controls.TextBox;
using Button=System.Windows.Controls.Button;
using Point=System.Windows.Point;
namespace Translate.Tests;
internal static class Program
{
    private static int _count;
    private static void Check(bool condition,string name) {if(!condition)throw new Exception("FAIL: "+name);Console.WriteLine("PASS: "+name);_count++;}
    [STAThread] private static int Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false);Console.InputEncoding=new UTF8Encoding(false);
        try {
            switch(args.FirstOrDefault()) {
                case "--ci": Unit(false);break;
                case "--live": Live().GetAwaiter().GetResult();break;
                case "--live-config": Live(true).GetAwaiter().GetResult();break;
                case "--ui": UiSmoke(args.ElementAtOrDefault(1)??"artifacts/qa");break;
                case "--fixture": Fixture(args[1],args.ElementAtOrDefault(2)=="fallback");break;
                default: Unit();break;
            }
            Console.WriteLine($"Completed: {_count} checks.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e is TranslationException?e.Message:e.ToString());return 1;}
    }
    private static void Unit(bool interactive=true)
    {
        LanguageChecks();
        var g=new RightClickGesture();
        Check(g.Down(100,100,0)==GestureEffect.Suppress,"capture right-down");
        Check(g.Up(100)==GestureEffect.ReplayClick,"short press replays context click");
        g.Down(100,100,1000);Check(g.Tick(1299)==GestureEffect.Pass,"threshold not reached");
        Check(g.Tick(1300)==GestureEffect.Trigger,"300ms triggers");Check(g.Tick(1800)==GestureEffect.Pass,"one trigger per hold");Check(g.Up(2000)==GestureEffect.Suppress,"long release suppresses context menu");
        g.Down(100,100,0);Check(g.Move(108,100)==GestureEffect.ReplayDown,"movement restores right drag");Check(g.Up(600)==GestureEffect.Pass,"right drag release passes through");
        g.HoldMilliseconds=600;g.Down(0,0,0);Check(g.Tick(500)==GestureEffect.Pass,"custom threshold honored");Check(g.Up(601)==GestureEffect.Trigger,"release catches delayed timer");
        g.Down(0,0,0);g.Cancel();Check(g.Tick(1000)==GestureEffect.Pass&&g.Up(1000)==GestureEffect.Suppress,"canceled hold cannot translate");
        Check(TranslationClient.NormalizeEndpoint("https://api.deepseek.com/").AbsoluteUri=="https://api.deepseek.com/chat/completions","base URL normalization");
        Check(TranslationClient.NormalizeEndpoint("https://example.com/v1/").AbsoluteUri=="https://example.com/v1/chat/completions","compatible v1 endpoint");
        Check(TranslationClient.NormalizeEndpoint("https://example.com/v1/chat/completions/").AbsolutePath=="/v1/chat/completions","full endpoint not duplicated");
        bool invalid=false;try{TranslationClient.NormalizeEndpoint("file:///tmp");}catch(ArgumentException){invalid=true;}Check(invalid,"invalid scheme rejected");
        var temp=Path.Combine(Path.GetTempPath(),"Translate-tests-"+Guid.NewGuid().ToString("N"));
        try {
            var store=new SettingsStore(temp);var settings=new AppSettings{ApiKey="unit-test-credential",HoldMilliseconds=800};store.Save(settings);
            Check(!File.ReadAllText(store.FilePath).Contains(settings.ApiKey),"saved credential is encrypted");
            var loaded=store.Load();Check(loaded.ApiKey==settings.ApiKey&&loaded.HoldMilliseconds==800,"configuration round trip");
            File.WriteAllText(store.FilePath,"invalid JSON");Check(!store.Load().IsConfigured&&store.LoadWarning!=null,"corrupt configuration handled");
        }finally{DeleteTestDirectory(temp);}
        if(interactive)ClipboardChecks();else Console.WriteLine("INFO: CI excludes desktop clipboard and global input checks.");ApiChecks().GetAwaiter().GetResult();
        SingleInstanceChecks();
    }
    private static void SingleInstanceChecks()
    {
        var scope="tests-"+Guid.NewGuid().ToString("N");
        var owner=new SingleInstance(scope);bool disposed=false;
        using var client=new SingleInstance(scope);
        try {
            Check(owner.IsOwner&&!client.IsOwner,"second instance detected");
            using var shown=new AutoResetEvent(false);using var shutdown=new AutoResetEvent(false);
            owner.ShowRequested+=()=>shown.Set();owner.ExitRequested+=()=>shutdown.Set();owner.Listen();
            client.Signal();Check(shown.WaitOne(3000),"second launch signals existing instance");
            var stopping=Task.Run(client.ShutdownRunningCopy);
            Check(shutdown.WaitOne(3000),"matching executable requests shutdown");
            owner.Dispose();disposed=true;
            Check(stopping.GetAwaiter().GetResult(),"shutdown client waits for original instance exit");
        } finally {if(!disposed)owner.Dispose();}
    }
    private static void DeleteTestDirectory(string directory)
    {
        var resolved=Path.GetFullPath(directory);var parent=Path.GetDirectoryName(resolved);
        var expected=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        var name=Path.GetFileName(resolved);
        if(!string.Equals(parent,expected,StringComparison.OrdinalIgnoreCase)||!(name.StartsWith("Translate-tests-")||name.StartsWith("Translate-ui-")))throw new Exception("Unsafe test cleanup target.");
        if(Directory.Exists(resolved))Directory.Delete(resolved,true);
    }
    private static void LanguageChecks()
    {
        Check(TranslationLanguage.Detect("workspace")==TranslationDirection.EnglishToChinese,"English word detection");
        Check(TranslationLanguage.Detect("The unit tests failed at line 59.")==TranslationDirection.EnglishToChinese,"English sentence detection");
        Check(TranslationLanguage.Detect("工作区")==TranslationDirection.ChineseToEnglish,"simplified Chinese detection");
        Check(TranslationLanguage.Detect("請翻譯這段文字")==TranslationDirection.ChineseToEnglish,"traditional Chinese detection");
        Check(TranslationLanguage.Detect("请检查 workspace 的内容。")==TranslationDirection.ChineseToEnglish,"Chinese with English identifier detection");
        Check(TranslationLanguage.Detect("Translate the word 工作区 into English please.")==TranslationDirection.EnglishToChinese,"English with quoted Chinese detection");
        Check(TranslationLanguage.Detect("123 + 🙂")==TranslationDirection.EnglishToChinese,"neutral text default");
        Check(TranslationLanguage.Detect("\U00020000")==TranslationDirection.ChineseToEnglish,"supplementary Han character detection");
        Check(TranslationLanguage.Label("")=="自动识别 · 中英互译","empty input automatic direction label");
        Check(TranslationLanguage.Prompt("workspace").Contains("翻译为简体中文"),"English target prompt");
        Check(TranslationLanguage.Prompt("工作区").Contains("翻译为英文"),"Chinese target prompt");
    }
    private static void ClipboardChecks()
    {
        DataObject? original;
        try{original=SelectionReader.SnapshotClipboard();}catch{Console.WriteLine("SKIP: clipboard preservation checks (unavailable original clipboard)");return;}
        try {
            Clipboard.SetText("previous clipboard");var saved=SelectionReader.SnapshotClipboard();Clipboard.SetText("temporary selected text");
            Check(SelectionReader.RestoreClipboard(saved,Win32.GetClipboardSequenceNumber())&&Clipboard.GetText()=="previous clipboard","original clipboard restored");
            Clipboard.SetText("temporary selected text");var copied=Win32.GetClipboardSequenceNumber();Clipboard.SetText("new user copy");
            Check(!SelectionReader.RestoreClipboard(saved,copied)&&Clipboard.GetText()=="new user copy","later user copy preserved");
            var rich=new DataObject();rich.SetText("plain");rich.SetData(DataFormats.Html,"<p><b>rich</b></p>");Clipboard.SetDataObject(rich,true);
            var richSaved=SelectionReader.SnapshotClipboard();Clipboard.SetText("temporary");SelectionReader.RestoreClipboard(richSaved,Win32.GetClipboardSequenceNumber());Check((string?)Clipboard.GetData(DataFormats.Html)=="<p><b>rich</b></p>","HTML clipboard retained");
            var bitmap=BitmapSource.Create(2,2,96,96,PixelFormats.Bgra32,null,new byte[16],8);Clipboard.SetImage(bitmap);
            Check(Clipboard.ContainsImage(),"image fixture available");var imageSaved=SelectionReader.SnapshotClipboard();Clipboard.SetText("temporary");SelectionReader.RestoreClipboard(imageSaved,Win32.GetClipboardSequenceNumber());Check(Clipboard.ContainsImage(),"image clipboard retained");
            var drop=new DataObject();drop.SetData(DataFormats.FileDrop,new[]{Path.GetTempPath()});Clipboard.SetDataObject(drop,true);var files=SelectionReader.SnapshotClipboard();Clipboard.SetText("temporary");SelectionReader.RestoreClipboard(files,Win32.GetClipboardSequenceNumber());Check(Clipboard.ContainsFileDropList(),"file clipboard retained");
        } finally {if(original==null)Clipboard.Clear();else Clipboard.SetDataObject(original,true);}
    }
    private sealed class Handler(Func<HttpRequestMessage,CancellationToken,Task<HttpResponseMessage>> send) : HttpMessageHandler
    {protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>send(request,cancellationToken);}
    internal static HttpClient Mock(string sse="data: {\"choices\":[{\"delta\":{\"content\":\"工作区\"}}]}\n\ndata: {\"choices\":[{\"delta\":{},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n") => new(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(sse,Encoding.UTF8,"text/event-stream")})));
    private static async Task<string> Collect(TranslationClient client,AppSettings s,CancellationToken token=default) {var text="";await foreach(var chunk in client.TranslateAsync("workspace",s,token))text+=chunk;return text;}
    private static async Task ApiChecks()
    {
        var s=new AppSettings{ApiKey="unit-test-credential"};
        foreach(var source in new[]{"workspace","工作区"}) {
            using var http=new HttpClient(new Handler(async(request,token)=>{
                using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));var messages=json.RootElement.GetProperty("messages");
                Check(messages[0].GetProperty("content").GetString()==TranslationLanguage.Prompt(source),"API direction prompt for "+source);
                Check(messages[1].GetProperty("content").GetString()==source,"API preserves source for "+source);
                return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"translated\"}}]}",Encoding.UTF8,"application/json")};
            }));using var client=new TranslationClient(http);
            await foreach(var chunk in client.TranslateAsync(source,s)){}
        }
        using(var http=new HttpClient(new Handler(async(request,token)=>{
            Check(request.Headers.Authorization?.Scheme=="Bearer"&&request.Headers.Authorization.Parameter==s.ApiKey,"Bearer authentication");
            using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));Check(body.RootElement.GetProperty("thinking").GetProperty("type").GetString()=="disabled","DeepSeek thinking disabled");
            return new(HttpStatusCode.OK){Content=new StringContent("data: {\"choices\":[{\"delta\":{\"content\":\"工作\"}}]}\n\n: heartbeat\n\ndata: {\"choices\":[{\"delta\":{\"content\":\"区\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }))) using(var client=new TranslationClient(http)){Check(await Collect(client,s)=="工作区","stream chunks joined and heartbeat ignored");}
        using(var http=Mock("data: {\"choices\":\ndata: [{\"delta\":{\"content\":\"你好\"},\"finish_reason\":\"stop\"}]}\n\ndata: [DONE]\n\n"))using(var client=new TranslationClient(http)){Check(await Collect(client,s)=="你好","multiline SSE data frames");}
        foreach(var status in new[]{HttpStatusCode.Unauthorized,HttpStatusCode.PaymentRequired,HttpStatusCode.TooManyRequests,HttpStatusCode.ServiceUnavailable}){
            using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(status))));using var client=new TranslationClient(http);bool handled=false;
            try{await Collect(client,s);}catch(TranslationException e){handled=e.Message==TranslationClient.ErrorMessage(status);}Check(handled,"HTTP "+(int)status+" actionable message");
        }
        using(var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"工作区\"}}]}",Encoding.UTF8,"application/json")}))))using(var client=new TranslationClient(http)){Check(await Collect(client,s)=="工作区","nonstream compatible response");}
        using(var http=Mock("data: {\"choices\":[{\"delta\":{\"content\":\"工作\"}}]}\n\n"))using(var client=new TranslationClient(http)){bool incomplete=false;try{await Collect(client,s);}catch(TranslationException){incomplete=true;}Check(incomplete,"truncated stream reported");}
        using(var http=Mock("data: not-json\n\n"))using(var client=new TranslationClient(http)){bool malformed=false;try{await Collect(client,s);}catch(TranslationException){malformed=true;}Check(malformed,"malformed API response handled");}
        using(var http=new HttpClient(new Handler(async(_,token)=>{await Task.Delay(Timeout.Infinite,token);return new(HttpStatusCode.OK);})))using(var client=new TranslationClient(http))using(var cancel=new CancellationTokenSource(30)){bool stopped=false;try{await Collect(client,s,cancel.Token);}catch(OperationCanceledException){stopped=true;}Check(stopped,"request cancellation");}
        using(var http=Mock())using(var client=new TranslationClient(http))using(var cancel=new CancellationTokenSource()){
            bool stopped=false;try{await foreach(var chunk in client.TranslateAsync("workspace",s,cancel.Token)){cancel.Cancel();}}catch(OperationCanceledException){stopped=true;}Check(stopped,"cancellation during streaming");
        }
        using(var http=new HttpClient(new Handler(async(request,token)=>{using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));Check(!body.RootElement.TryGetProperty("thinking",out _),"compatible provider omits vendor options");return new(HttpStatusCode.OK){Content=new StringContent("{\"choices\":[{\"message\":{\"content\":\"你好\"}}]}",Encoding.UTF8,"application/json")};})))using(var client=new TranslationClient(http)){await Collect(client,new AppSettings{Provider="Custom",BaseUrl="https://example.com/v1",Model="custom-model",ApiKey="unit-test-credential"});}
    }
    private static async Task Live(bool useSavedSettings=false)
    {
        // Credentials stay in memory: stdin for ad-hoc validation, or DPAPI-protected user settings.
        var settings=useSavedSettings?new SettingsStore().Load():new AppSettings{ApiKey=(await Console.In.ReadLineAsync())?.Trim()??""};
        if(!settings.IsConfigured)throw new Exception("No live credential available.");
        using var client=new TranslationClient();
        foreach(var source in new[]{"Select text and hold the right mouse button to translate.","选中文字后，长按鼠标右键即可翻译。"}) {
            var result="";var chunks=0;
            await foreach(var chunk in client.TranslateAsync(source,settings)){result+=chunk;chunks++;}
            bool chinese=result.Any(c=>c is >= '\u4e00' and <= '\u9fff');
            Check(TranslationLanguage.Detect(source)==TranslationDirection.EnglishToChinese?chinese:!chinese&&result.Any(c=>c is >= 'a' and <= 'z'),"real API "+TranslationLanguage.Label(source));
            Check(chunks>0,"real API streaming received");Console.WriteLine("Translation: "+result);
        }
        settings.ApiKey="";
    }
    private static void UiSmoke(string output)
    {
        Directory.CreateDirectory(output);var temp=Path.Combine(Path.GetTempPath(),"Translate-ui-"+Guid.NewGuid().ToString("N"));
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};app.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("pack://application:,,,/Translate;component/UI/Styles.xaml")});
        var requests=new List<string>();
        using var mock=new HttpClient(new Handler(async(request,token)=>{
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var source=json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;requests.Add(source);
            if(source=="slow request")await Task.Delay(500,token);
            var translated=TranslationLanguage.Detect(source)==TranslationDirection.ChineseToEnglish?"workspace":"工作区";
            var frame=JsonSerializer.Serialize(new { choices=new[] { new { delta=new { content=translated },finish_reason="stop" } } });
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+frame+"\n\ndata: [DONE]\n\n",Encoding.UTF8,"text/event-stream")};
        }));using var controller=new AppController(new SettingsStore(temp),mock);
        int exitCode=0;
        app.Startup+=async(_,_)=>{
            try{
                controller.ShowMain(true);await Task.Delay(250);ThemeService.Apply(false);await Task.Delay(100);Capture(controller.Main,Path.Combine(output,"settings-light.png"));
                var main=controller.Main;main.BaseUrlBox.Text="https://api.deepseek.com";main.ModelBox.Text="deepseek-flash";main.KeyBox.Password="ui-test-credential";main.SaveButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Check(controller.Settings.IsConfigured,"settings form saves encrypted configuration");main.TranslateNav.IsChecked=true;main.InputBox.Text="workspace";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(200);
                Check(main.OutputBox.Text=="工作区","manual translation UI streams result");Capture(main,Path.Combine(output,"translation-light.png"));
                ThemeService.Apply(true);await Task.Delay(100);Check(main.Background is SolidColorBrush darkBackground && darkBackground.Color.R < 50,"dark theme updates main window background");Capture(main,Path.Combine(output,"translation-dark.png"));
                Win32.GetWindowRect(new WindowInteropHelper(main).Handle,out var rect);var anchor=new Win32.POINT(rect.Right-15,rect.Bottom-15);var foreground=Win32.GetForegroundWindow();controller.Popup.Open(anchor,"workspace");controller.Popup.SetSource("workspace");controller.Popup.Append("工作区");controller.Popup.Complete();await Task.Delay(150);
                Check(Win32.GetForegroundWindow()==foreground,"popup does not steal foreground focus");
                var hwnd=new WindowInteropHelper(controller.Popup).Handle;Win32.GetWindowRect(hwnd,out var popupRect);var monitor=Win32.MonitorFromPoint(anchor,2);var info=new Win32.MONITORINFO{cbSize=(uint)System.Runtime.InteropServices.Marshal.SizeOf<Win32.MONITORINFO>()};Win32.GetMonitorInfo(monitor,ref info);
                Check(popupRect.Left>=info.rcWork.Left&&popupRect.Right<=info.rcWork.Right&&popupRect.Top>=info.rcWork.Top&&popupRect.Bottom<=info.rcWork.Bottom,"popup stays within monitor work area");Capture(controller.Popup,Path.Combine(output,"popup-dark.png"));ThemeService.Apply(false);await Task.Delay(80);Capture(controller.Popup,Path.Combine(output,"popup-light.png"));controller.Popup.SetSource(string.Join(" ",Enumerable.Repeat("A longer paragraph for translation.",20)));Check(controller.Popup.Height>=360,"long selection expands popup");Win32.GetWindowRect(hwnd,out var grownRect);Check(grownRect.Bottom<=info.rcWork.Bottom&&grownRect.Right<=info.rcWork.Right,"expanded popup remains within work area");controller.Popup.Dismiss();
                await PopupFeatureChecks(controller,output,requests);
                main.InputBox.Text="工作区";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitUntil(()=>main.OutputBox.Text=="workspace");
                Check(main.DirectionLabel.Text=="中文 → 英文","main window follows automatic direction");Capture(main,Path.Combine(output,"translation-chinese.png"));
                main.Close();Check(!main.IsVisible,"close hides main window for tray operation");controller.ShowMain();Check(main.IsVisible,"main window reopens");
                // Install the actual native hooks, then exercise a separate foreground fixture process.
                controller.Start();main.Hide();await InputChecks(controller,output,temp);await CompatibilityChecks(controller,output,temp);
            }catch(Exception e){Console.Error.WriteLine(e);exitCode=1;}
            finally{controller.Exit();}
        };
        app.Run();DeleteTestDirectory(temp);if(exitCode!=0)throw new Exception("UI smoke checks failed.");
    }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern nint SendMessage(nint hwnd,uint message,nint w,nint l);
    private static nint PackedPoint(int x,int y)=>new((y<<16)|(x&0xffff));
    private static void SendEnter()=>Win32.Send(Win32.Key(0x0D),Win32.Key(0x0D,true));
    private static async Task PopupFeatureChecks(AppController controller,string output,List<string> requests)
    {
        var popup=controller.Popup;popup.Open(new Win32.POINT(100,150),"workspace");await Task.Delay(120);
        var hwnd=new WindowInteropHelper(popup).Handle;Win32.GetWindowRect(hwnd,out var bounds);
        int l=bounds.Left+6,r=bounds.Right-7,t=bounds.Top+6,b=bounds.Bottom-7,x=(l+r)/2,y=(t+b)/2;
        foreach(var spot in new[]{(l,y,10,"left"),(r,y,11,"right"),(x,t,12,"top"),(x,b,15,"bottom"),(l,t,13,"top-left"),(r,t,14,"top-right"),(l,b,16,"bottom-left"),(r,b,17,"bottom-right")})
            Check((int)SendMessage(hwnd,0x84,0,PackedPoint(spot.Item1,spot.Item2))==spot.Item3,"native resize hit test "+spot.Item4);
        Win32.Send(UnmarkedMouse(4),UnmarkedMouse(16));popup.Activate();await Task.Delay(150);
        Win32.GetCursorPos(out var cursor);
        try {
            var drag=Task.Run(async()=>{
                Win32.SetCursorPos(bounds.Right-7,bounds.Bottom-7);await Task.Delay(180);Console.WriteLine($"Resize input: foreground={Win32.GetForegroundWindow()==hwnd}, left={(Win32.GetAsyncKeyState(1)&0x8000)!=0}, right={(Win32.GetAsyncKeyState(2)&0x8000)!=0}");if(!Win32.Send(UnmarkedMouse(2)))throw new Exception("Resize mouse injection failed.");await Task.Delay(250);
                for(int i=1;i<=6;i++){Win32.SetCursorPos(bounds.Right-7+i*15,bounds.Bottom-7+i*9);await Task.Delay(60);}
                Win32.Send(UnmarkedMouse(4));
            });
            await drag;await Task.Delay(180);Win32.GetWindowRect(hwnd,out var resized);
            Console.WriteLine($"Resize dimensions: {bounds.Right-bounds.Left}x{bounds.Bottom-bounds.Top} → {resized.Right-resized.Left}x{resized.Bottom-resized.Top}");
            Check(resized.Right-resized.Left>bounds.Right-bounds.Left+40&&resized.Bottom-resized.Top>bounds.Bottom-bounds.Top+25,"actual corner drag resizes popup");
            var size=new Size(popup.ActualWidth,popup.ActualHeight);popup.SetSource("Changed text");
            Check(Math.Abs(popup.ActualWidth-size.Width)<2&&Math.Abs(popup.ActualHeight-size.Height)<2,"translation does not reset user size");
            popup.Dismiss();popup.Open(new Win32.POINT(100,150),"workspace");await Task.Delay(80);
            Check(Math.Abs(popup.ActualWidth-size.Width)<2&&Math.Abs(popup.ActualHeight-size.Height)<2,"reopened popup retains session size");
        } finally{Win32.Send(UnmarkedMouse(4),UnmarkedMouse(16));Win32.SetCursorPos(cursor.X,cursor.Y);}
        Check(!popup.SourceBox.IsReadOnly&&popup.SourceBox.AcceptsReturn,"popup source is editable multiline input");
        popup.Activate();popup.SourceBox.Focus();popup.SourceBox.Text="工作区";await Task.Delay(60);SendEnter();
        await WaitUntil(()=>popup.ResultBox.Text=="workspace");Check(popup.DirectionLabel.Text=="中文 → 英文","Enter translates edited Chinese input");
        Check(requests.Last()=="工作区","Enter sends current edited source");Capture(popup,Path.Combine(output,"popup-chinese-resized.png"));
        popup.SourceBox.Text="workspace";await Task.Delay(30);SendEnter();await WaitUntil(()=>popup.ResultBox.Text=="工作区");
        Check(popup.DirectionLabel.Text=="英文 → 简体中文","Enter translates edited English input");Capture(popup,Path.Combine(output,"popup-english-resized.png"));
        popup.SourceBox.Text="另一段中文";Check(popup.ResultBox.Text.Length==0&&!popup.CopyResultButton.IsEnabled,"editing invalidates old translation");
        popup.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitUntil(()=>popup.ResultBox.Text=="workspace");Check(requests.Last()=="另一段中文","retry uses current input");
        popup.SourceBox.Text="";var count=requests.Count;SendEnter();await Task.Delay(80);Check(requests.Count==count&&popup.MessageText.Text.Contains("请先输入"),"empty Enter does not send API request");
        popup.SourceBox.Text="line one";popup.SourceBox.CaretIndex=popup.SourceBox.Text.Length;
        Win32.Send(Win32.Key(0x10),Win32.Key(0x0D),Win32.Key(0x0D,true),Win32.Key(0x10,true));await Task.Delay(80);
        Check(popup.SourceBox.Text.Contains('\n')&&requests.Count==count,"Shift+Enter inserts newline without translation");
        var composing=new System.Windows.Input.TextComposition(System.Windows.Input.InputManager.Current,popup.SourceBox,"中");
        popup.SourceBox.RaiseEvent(new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,composing){RoutedEvent=System.Windows.Input.TextCompositionManager.PreviewTextInputStartEvent});
        SendEnter();await Task.Delay(50);Check(requests.Count==count,"IME candidate-confirming Enter does not submit");
        popup.SourceBox.RaiseEvent(new System.Windows.Input.TextCompositionEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,composing){RoutedEvent=System.Windows.Input.TextCompositionManager.PreviewTextInputEvent});
        popup.SourceBox.Text="slow request";SendEnter();await Task.Delay(50);popup.SourceBox.Text="新的中文";SendEnter();await WaitUntil(()=>popup.ResultBox.Text=="workspace");await Task.Delay(550);
        Check(popup.ResultBox.Text=="workspace"&&requests.Last()=="新的中文","editing cancels old request and keeps latest result");
        popup.Dismiss();
    }
    private static void Capture(Window window,string filename)
    {
        window.UpdateLayout();Win32.GetWindowRect(new WindowInteropHelper(window).Handle,out var rect);
        using var bitmap=new System.Drawing.Bitmap(rect.Right-rect.Left,rect.Bottom-rect.Top);using(var graphics=System.Drawing.Graphics.FromImage(bitmap))graphics.CopyFromScreen(rect.Left,rect.Top,0,0,bitmap.Size);bitmap.Save(filename,System.Drawing.Imaging.ImageFormat.Png);
    }
    private static Win32.INPUT UnmarkedMouse(uint flags){var input=Win32.Mouse(flags);input.data.mi.dwExtraInfo=0;return input;}
    private static async Task WaitUntil(Func<bool> check,int milliseconds=2500)
    {for(int i=0;i<milliseconds/25;i++){if(check())return;await Task.Delay(25);}throw new Exception("Timed out waiting for UI state.");}
    private static async Task InputChecks(AppController controller,string output,string temp)
    {
        if((Win32.GetAsyncKeyState(1)&0x8000)!=0)Win32.Send(UnmarkedMouse(4));if((Win32.GetAsyncKeyState(2)&0x8000)!=0)Win32.Send(UnmarkedMouse(16));Win32.GetCursorPos(out var originalCursor);DataObject? original=SelectionReader.SnapshotClipboard();
        try{
            foreach(bool fallback in new[]{false,true}){
                var status=Path.Combine(temp,fallback?"fallback-status.txt":"selection-status.txt");
                var exe=Environment.ProcessPath!;var start=new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=false};start.ArgumentList.Add("--fixture");start.ArgumentList.Add(status);if(fallback)start.ArgumentList.Add("fallback");
                using var fixture=System.Diagnostics.Process.Start(start)!;
                try{
                    await WaitUntil(()=>File.Exists(status));await Task.Delay(150);fixture.Refresh();var hwnd=fixture.MainWindowHandle;Win32.SetForegroundWindow(hwnd);await Task.Delay(350);Win32.GetWindowRect(hwnd,out var rect);var fixtureScale=Win32.GetDpiForWindow(hwnd)/96.0;Win32.SetCursorPos(rect.Left+(int)(70*fixtureScale),rect.Top+(int)(85*fixtureScale));
                    Check(Win32.GetForegroundWindow()==hwnd,"fixture is foreground ("+(fallback?"clipboard":"UIA")+")");
                    if(!fallback){
                        Win32.Send(UnmarkedMouse(8));await Task.Delay(80);Win32.Send(UnmarkedMouse(16));await WaitUntil(()=>File.ReadAllText(status).Contains("menu"));Check(true,"real short right-click opens target context menu");Win32.Send(Win32.Key(0x1B),Win32.Key(0x1B,true));await Task.Delay(100);
                    }
                    Win32.SetForegroundWindow(hwnd);await Task.Delay(200);Win32.SetCursorPos(rect.Left+(int)(70*fixtureScale),rect.Top+(int)(85*fixtureScale));await Task.Delay(100);if(!fallback){Win32.Send(Win32.Key(0x11),Win32.Key(0x41),Win32.Key(0x41,true),Win32.Key(0x11,true));await Task.Delay(100);}var menusBefore=File.ReadAllLines(status).Count(line=>line=="menu");Clipboard.SetText("user clipboard before translation");
                    Win32.Send(UnmarkedMouse(8));await Task.Delay(400);Win32.Send(UnmarkedMouse(16));try{await WaitUntil(()=>controller.Popup.IsVisible&&controller.Popup.ResultBox.Text=="工作区");}catch{Console.Error.WriteLine($"Input diagnostic: visible={controller.Popup.IsVisible}; message={controller.Popup.MessageText.Text}; source={controller.Popup.SourceBox.Text}; fixture={File.ReadAllText(status).Trim().Replace("\n","/")}; modifiers={Win32.ModifiersPressed()}");throw;}
                    Check(controller.Popup.SourceBox.Text=="workspace","real "+(fallback?"clipboard fallback":"UIA selection")+" extraction");Check(Clipboard.GetText()=="user clipboard before translation","global translation preserves clipboard");
                    Check(File.ReadAllLines(status).Count(line=>line=="menu")==menusBefore,"long press does not add target context menu");Capture(controller.Popup,Path.Combine(output,fallback?"popup-clipboard.png":"popup-global.png"));
                    var escape=Win32.Key(0x1B);escape.data.ki.dwExtraInfo=0;var escapeUp=Win32.Key(0x1B,true);escapeUp.data.ki.dwExtraInfo=0;Win32.Send(escape,escapeUp);await WaitUntil(()=>!controller.Popup.IsVisible);Check(true,"Escape closes passive popup");
                    Win32.Send(UnmarkedMouse(8));await Task.Delay(380);Win32.Send(UnmarkedMouse(16));await WaitUntil(()=>controller.Popup.IsVisible&&controller.Popup.ResultBox.Text=="工作区");
                    Win32.SetCursorPos(rect.Left+20,rect.Top+40);Win32.Send(UnmarkedMouse(2),UnmarkedMouse(4));await WaitUntil(()=>!controller.Popup.IsVisible);Check(true,"outside click dismisses popup");
                    if(fallback){
                        Win32.SetForegroundWindow(hwnd);await Task.Delay(200);
                        var copies=File.ReadAllLines(status).Count(line=>line=="copy");Clipboard.SetText("clipboard before canceled capture");Win32.SetCursorPos(rect.Left+(int)(70*fixtureScale),rect.Top+(int)(85*fixtureScale));
                        Win32.Send(UnmarkedMouse(8));try{await WaitUntil(()=>File.ReadAllLines(status).Count(line=>line=="copy")>copies);controller.Popup.Dismiss();}finally{Win32.Send(UnmarkedMouse(16));}await Task.Delay(300);
                        Check(Clipboard.GetText()=="clipboard before canceled capture","closing popup during copy still restores clipboard");
                    }
                }finally{controller.Popup.Dismiss();if(!fixture.HasExited){fixture.Kill();fixture.WaitForExit(1500);}}
            }
        }finally{Win32.Send(UnmarkedMouse(4),UnmarkedMouse(16));Win32.SetCursorPos(originalCursor.X,originalCursor.Y);if(original==null)Clipboard.Clear();else Clipboard.SetDataObject(original,true);}
    }

    private static nint FindWindow(string title)
    {
        nint result=0;Win32.EnumWindows((hwnd,_)=>{var text=new StringBuilder(512);Win32.GetWindowText(hwnd,text,512);if(Win32.IsWindowVisible(hwnd)&&text.ToString().Contains(title,StringComparison.OrdinalIgnoreCase)){result=hwnd;return false;}return true;},0);return result;
    }
    private static async Task CompatibilityChecks(AppController controller,string output,string temp)
    {
        Win32.GetCursorPos(out var originalCursor);var original=SelectionReader.SnapshotClipboard();
        try {
            foreach(var name in new[]{"Edge","Notepad","VSCode"}) {
                string id="Translate-compat-"+name+"-"+Guid.NewGuid().ToString("N")[..8];
                string filename=Path.Combine(temp,id+(name=="Edge"?".html":".txt"));
                await File.WriteAllTextAsync(filename,name=="Edge"?"<!doctype html><meta charset='utf-8'><title>"+id+"</title><style>body{font:28px Segoe UI;padding:40px}</style><div id='text' tabindex='0' contenteditable>workspace</div><script>onload=()=>{let e=document.getElementById('text');e.focus();let r=document.createRange();r.selectNodeContents(e);let s=getSelection();s.removeAllRanges();s.addRange(r);};</script>":"workspace");
                var exe=name switch {"Edge"=>@"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe","Notepad"=>@"C:\Windows\System32\notepad.exe",_=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),@"Programs\Microsoft VS Code\Code.exe")};
                if(!File.Exists(exe)){Console.WriteLine("SKIP: "+name+" not installed");continue;}
                var start=new System.Diagnostics.ProcessStartInfo(exe){UseShellExecute=false};
                if(name=="Edge") {
                    start.ArgumentList.Add("--user-data-dir="+Path.Combine(temp,"edge-profile"));start.ArgumentList.Add("--no-first-run");start.ArgumentList.Add("--no-default-browser-check");start.ArgumentList.Add("--disable-background-networking");start.ArgumentList.Add("--force-renderer-accessibility");start.ArgumentList.Add("--window-size=650,380");start.ArgumentList.Add("--app="+new Uri(filename).AbsoluteUri);
                } else if(name=="VSCode") {
                    start.ArgumentList.Add("--user-data-dir");start.ArgumentList.Add(Path.Combine(temp,"code-profile"));start.ArgumentList.Add("--extensions-dir");start.ArgumentList.Add(Path.Combine(temp,"code-extensions"));start.ArgumentList.Add("--disable-extensions");start.ArgumentList.Add("--force-renderer-accessibility");start.ArgumentList.Add("--skip-welcome");start.ArgumentList.Add("--new-window");start.ArgumentList.Add(filename);
                } else start.ArgumentList.Add(filename);
                using var process=System.Diagnostics.Process.Start(start)!;nint hwnd=0;
                try {
                    await WaitUntil(()=>{hwnd=FindWindow(id);return hwnd!=0;},20000);
                    Win32.SetForegroundWindow(hwnd);await Task.Delay(name=="VSCode"?2200:600);
                    if(name=="VSCode"){Win32.Send(Win32.Key(0x11),Win32.Key(0x31),Win32.Key(0x31,true),Win32.Key(0x11,true));await Task.Delay(500);}
                    if(name!="Edge")Win32.Send(Win32.Key(0x11),Win32.Key(0x41),Win32.Key(0x41,true),Win32.Key(0x11,true));
                    await Task.Delay(250);Win32.GetWindowRect(hwnd,out var rect);Win32.SetCursorPos(rect.Left+150,rect.Top+180);
                    Clipboard.SetText("clipboard preserved during "+name);
                    Win32.Send(UnmarkedMouse(8));await Task.Delay(380);Win32.Send(UnmarkedMouse(16));
                    try{await WaitUntil(()=>controller.Popup.IsVisible&&controller.Popup.ResultBox.Text=="工作区",5000);}catch{Console.Error.WriteLine($"Compatibility diagnostic: app={name}; visible={controller.Popup.IsVisible}; message={controller.Popup.MessageText.Text}; source={controller.Popup.SourceBox.Text}; result={controller.Popup.ResultBox.Text}; foreground={Win32.GetForegroundWindow()==hwnd}");throw;}
                    Check(controller.Popup.SourceBox.Text.Trim()=="workspace",name+" selected text translation");
                    Check(Clipboard.GetText()=="clipboard preserved during "+name,name+" clipboard preservation");Capture(controller.Popup,Path.Combine(output,"popup-"+name.ToLowerInvariant()+".png"));
                } finally {
                    controller.Popup.Dismiss();
                    if(name=="Notepad"&&hwnd!=0){Win32.SetForegroundWindow(hwnd);Win32.Send(Win32.Key(0x11),Win32.Key(0x57),Win32.Key(0x57,true),Win32.Key(0x11,true));}
                    else {if(hwnd!=0){Win32.PostMessage(hwnd,0x10,0,0);await Task.Delay(500);}if(!process.HasExited){process.Kill(true);process.WaitForExit(2000);}}
                }
            }
        } finally{Win32.SetCursorPos(originalCursor.X,originalCursor.Y);if(original==null)Clipboard.Clear();else Clipboard.SetDataObject(original,true);}
    }

    private static void Fixture(string status,bool fallback)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(status)!);var app=new Application();var window=new Window{Title="Translate selection fixture",Width=580,Height=260,Left=55,Top=80,WindowStartupLocation=WindowStartupLocation.Manual};
        var menu=new System.Windows.Controls.ContextMenu();menu.Items.Add(new MenuItem{Header="Fixture menu"});menu.Opened+=(_,_)=>File.AppendAllText(status,"menu\n");
        if(!fallback){var box=new TextBox{Text="workspace",FontSize=26,Margin=new Thickness(28),ContextMenu=menu};window.Content=box;window.Loaded+=(_,_)=>{window.Activate();box.Focus();box.SelectAll();File.WriteAllText(status,"ready\n");};}
        else{
            var panel=new System.Windows.Controls.Border{Background=Brushes.White,Focusable=true,ContextMenu=menu,Child=new TextBlock{Text="workspace",FontSize=26,Margin=new Thickness(28)}};window.Content=panel;
            window.PreviewKeyDown+=async(_,e)=>{if(e.Key==System.Windows.Input.Key.C&&System.Windows.Input.Keyboard.Modifiers.HasFlag(System.Windows.Input.ModifierKeys.Control)){e.Handled=true;File.AppendAllText(status,"copy\n");await Task.Delay(120);Clipboard.SetText("workspace");}};
            window.Loaded+=(_,_)=>{window.Activate();panel.Focus();File.WriteAllText(status,"ready\n");};
        }
        app.Run(window);
    }
}
