using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;
using Translate.Core;
using Translate.Native;
using Translate.UI;
using Application=System.Windows.Application;
using Button=System.Windows.Controls.Button;
using TextBox=System.Windows.Controls.TextBox;
namespace Translate.Tests;
internal static partial class Program
{
    private static void Reliability(string output,bool selectionOnly=false,bool backgroundOnly=false)
    {
        Directory.CreateDirectory(output);
        var temp=Path.Combine(Path.GetTempPath(),"Translate-ui-"+Guid.NewGuid().ToString("N"));
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("pack://application:,,,/Translate;component/UI/Styles.xaml") });
        var errors=new List<Exception>();var backgroundErrors=new List<Exception>();
        app.DispatcherUnhandledException+=(_,e)=> { errors.Add(e.Exception);e.Handled=true; };
        EventHandler<UnobservedTaskExceptionEventArgs> unobserved=(_,e)=>{lock(backgroundErrors)backgroundErrors.Add(e.Exception);e.SetObserved();};
        TaskScheduler.UnobservedTaskException+=unobserved;
        int exitCode=0,connectionTests=0;
        using var mock=new HttpClient(new Handler(async(request,token)=> {
            using var json=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            var source=json.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!;
            if(source.StartsWith("slow",StringComparison.Ordinal))await Task.Delay(250,token);
            if(source=="Hello, world!"&&Interlocked.Increment(ref connectionTests)==1)await Task.Delay(250,token);
            var text=TranslationLanguage.Detect(source)==TranslationDirection.ChineseToEnglish?"workspace":"工作区";
            var frame=JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content=text},finish_reason="stop"}}});
            return new(HttpStatusCode.OK){Content=new StringContent("data: "+frame+"\n\n",Encoding.UTF8,"text/event-stream")};
        }));
        using var controller=new AppController(new SettingsStore(temp),mock);
        app.Startup+=async(_,_)=> {
            try {
                if(selectionOnly){await DirectSelectionChecks(output,temp);return;}
                using var theme=new ThemeService();
                var changed=typeof(ThemeService).GetMethod("Changed",BindingFlags.Instance|BindingFlags.NonPublic)!;
                await Task.Run(()=>changed.Invoke(theme,new object[] { theme,new UserPreferenceChangedEventArgs(UserPreferenceCategory.General) }));
                await Task.Delay(150);
                if(errors.Count>0)Console.WriteLine("Observed dispatcher error: "+errors[0].GetType().Name);
                Check(errors.Count==0,"system preference callback runs without dispatcher parameter mismatch");
                var brush=app.Resources["CanvasBrush"];
                await Task.Run(()=>{for(int i=0;i<2000;i++)changed.Invoke(theme,new object[]{theme,new UserPreferenceChangedEventArgs(UserPreferenceCategory.General)});});
                await Task.Delay(100);
                Check(errors.Count==0&&ReferenceEquals(brush,app.Resources["CanvasBrush"]),"2000 preference events preserve brushes when theme is unchanged");
                if(backgroundOnly)return;
                controller.SaveSettings(new AppSettings {ApiKey="unit-test-credential"});controller.ShowMain();
                var main=controller.Main;main.TranslateNav.IsChecked=true;
                main.InputBox.Text="workspace";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await WaitUntil(()=>main.OutputBox.Text=="工作区");
                Check(main.TranslateButton.IsEnabled&&!main.OutputBox.IsUndoEnabled,"main translation completes and output keeps no undo history");
                main.InputBox.Text="slow manual";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await Task.Delay(20);main.InputBox.Text="工作区";
                Check(main.TranslateButton.IsEnabled&&main.OutputBox.Text.Length==0,"editing main source cancels the request and clears its pending text");
                main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitUntil(()=>main.OutputBox.Text=="workspace");
                await Task.Delay(300);Check(main.OutputBox.Text=="workspace"&&main.StatusLabel.Text=="翻译完成。","cancelled manual request cannot overwrite new output or status");
                main.TestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await Task.Delay(10);
                main.TestButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitUntil(()=>main.StatusLabel.Text.StartsWith("连接成功",StringComparison.Ordinal));
                await Task.Delay(280);Check(main.TestButton.IsEnabled&&main.StatusLabel.Text.StartsWith("连接成功",StringComparison.Ordinal),"replaced connection test cannot overwrite current status");
                var popup=controller.Popup;popup.Open(new Win32.POINT(120,150),"workspace");
                popup.Append("pending old output");popup.Reset("工作区");await Task.Delay(80);
                Check(popup.ResultBox.Text.Length==0,"reset discards buffered text from the previous request");
                popup.Append("partial output");popup.Error("模拟连接中断");
                Check(popup.ResultBox.Text=="partial output"&&popup.StateText.Text=="模拟连接中断","stream error flushes pending partial output");
                for(int i=0;i<100;i++) {
                    popup.SourceBox.Text="slow "+i;popup.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    popup.SourceBox.Text="workspace";popup.RetryButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    await WaitUntil(()=>popup.ResultBox.Text=="工作区");
                    if(i%10==0)await Task.Delay(5);
                }
                await Task.Delay(300);
                Check(popup.ResultBox.Text=="工作区"&&popup.StateText.Text=="翻译完成","100 popup cancel/retry cycles retain only the latest result");
                Check(!popup.ResultBox.IsUndoEnabled,"popup translated output keeps no undo history");
                var benchmark=await OutputBenchmark();
                popup.Dismiss();
                var samples=new List<ResourceSample>();
                for(int batch=0;batch<3;batch++) {
                    for(int i=0;i<250;i++){popup.Open(new Win32.POINT(120,150),"workspace");popup.Append("工作区");popup.Complete();popup.Dismiss();}
                    await Task.Delay(80);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                    using var process=Process.GetCurrentProcess();process.Refresh();
                    samples.Add(new(batch+1,process.HandleCount,process.Threads.Count,GC.GetTotalMemory(false),process.PrivateMemorySize64));
                }
                Check(samples[^1].Handles-samples[0].Handles<32,"750 popup show/hide cycles do not accumulate native handles");
                Check(samples[^1].ManagedBytes-samples[0].ManagedBytes<4*1024*1024,"750 popup show/hide cycles keep managed memory bounded");
                await DirectSelectionChecks(output,temp);
                main.TranslateNav.IsChecked=true;main.InputBox.Text="workspace";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));await WaitUntil(()=>main.OutputBox.Text=="工作区");
                controller.ShowMain();Win32.Send(Win32.Key(0x12),Win32.Key(0x12,true));main.Activate();
                ThemeService.Apply(false);await Task.Delay(100);Capture(main,Path.Combine(output,"main-light.png"));
                popup.Open(new Win32.POINT(130,180),"workspace");popup.SetSource("workspace");popup.Append("工作区");popup.Complete();await Task.Delay(80);Capture(popup,Path.Combine(output,"popup-light.png"));
                ThemeService.Apply(true);await Task.Delay(80);Capture(popup,Path.Combine(output,"popup-dark.png"));popup.Dismiss();Capture(main,Path.Combine(output,"main-dark.png"));
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(50);
                Check(errors.Count==0,"desktop stress run has no unhandled dispatcher errors");
                lock(backgroundErrors)Check(backgroundErrors.Count==0,"desktop stress run has no unobserved background task failures");
                File.WriteAllText(Path.Combine(output,"metrics.json"),JsonSerializer.Serialize(new{Timestamp=DateTimeOffset.Now,ThemeEvents=2000,CancelRetryCycles=100,PopupCycles=750,Benchmark=benchmark,Resources=samples},new JsonSerializerOptions{WriteIndented=true}),new UTF8Encoding(false));
                main.InputBox.Text="slow shutdown";main.TranslateButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                controller.Dispose();controller.Dispose();await Task.Delay(80);
                Check(errors.Count==0,"shutdown during an active request is safe and idempotent");
            } catch(Exception error) {Console.Error.WriteLine(error);exitCode=1;}
            finally {controller.Dispose();app.Shutdown();}
        };
        app.Run();TaskScheduler.UnobservedTaskException-=unobserved;DeleteTestDirectory(temp);
        if(exitCode!=0)throw new Exception("Reliability checks failed.");
    }
    private sealed record ResourceSample(int Batch,int Handles,int Threads,long ManagedBytes,long PrivateBytes);
    private sealed record OutputMetrics(int Chunks,long DirectMilliseconds,long BufferedMilliseconds,int DirectChanges,int BufferedChanges,long DirectAllocatedBytes,long BufferedAllocatedBytes);
    private static async Task<OutputMetrics> OutputBenchmark()
    {
        const int chunks=10_000;
        var direct=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap};
        var buffered=new TextBox{IsReadOnly=true,TextWrapping=TextWrapping.Wrap};
        var grid=new Grid();grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new());grid.Children.Add(direct);grid.Children.Add(buffered);Grid.SetColumn(buffered,1);
        var window=new Window{Title="Translate render benchmark",Width=650,Height=260,Content=grid,ShowInTaskbar=false};
        try {
            window.Show();await Task.Delay(80);int directChanges=0,bufferedChanges=0;
            direct.TextChanged+=(_,_)=>directChanges++;buffered.TextChanged+=(_,_)=>bufferedChanges++;
            long allocated=GC.GetAllocatedBytesForCurrentThread();var watch=Stopwatch.StartNew();
            for(int i=0;i<chunks;i++)direct.AppendText("译");
            long directMs=watch.ElapsedMilliseconds,directBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            using var output=new BufferedTextOutput(buffered);allocated=GC.GetAllocatedBytesForCurrentThread();watch.Restart();
            for(int i=0;i<chunks;i++)output.Append("译");output.Flush();
            long bufferedMs=watch.ElapsedMilliseconds,bufferedBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;
            Check(buffered.Text==direct.Text&&buffered.Text.Length==chunks,"10000 buffered streaming fragments preserve all text");
            Check(bufferedChanges<directChanges/100,"buffering reduces 10000 individual TextBox changes by over 99 percent");
            Console.WriteLine($"PERF: fragments={chunks}; direct={directMs}ms/{directChanges} changes/{directBytes} allocated bytes; buffered={bufferedMs}ms/{bufferedChanges} changes/{bufferedBytes} allocated bytes");
            return new(chunks,directMs,bufferedMs,directChanges,bufferedChanges,directBytes,bufferedBytes);
        }finally{window.Close();}
    }
    private static async Task DirectSelectionChecks(string output,string temp)
    {
        System.Windows.DataObject? original;
        try{original=SelectionReader.SnapshotClipboard();}catch{Console.WriteLine("SKIP: direct selection checks (original clipboard cannot be safely preserved)");return;}
        try {
            foreach(var mode in new[]{"uia","clipboard","long"}) {
                bool fallback=mode=="clipboard",oversized=mode=="long";
                var status=Path.Combine(temp,"direct-"+mode+".txt");
                var start=new ProcessStartInfo(Environment.ProcessPath!){UseShellExecute=false,CreateNoWindow=true};start.ArgumentList.Add("--fixture");start.ArgumentList.Add(status);if(fallback)start.ArgumentList.Add("fallback");else if(oversized)start.ArgumentList.Add("long");
                using var process=Process.Start(start)!;using var reader=new SelectionReader();
                try {
                    await WaitUntil(()=>File.Exists(status)&&File.ReadAllText(status).Contains("ready"));
                    process.Refresh();var hwnd=process.MainWindowHandle;Win32.SetForegroundWindow(hwnd);await Task.Delay(250);
                    if(hwnd==0||Win32.GetForegroundWindow()!=hwnd)Console.WriteLine($"Foreground diagnostic: pid={process.Id}; handle={hwnd}; foreground={Win32.GetForegroundWindow()}; found={FindWindow("Translate selection fixture")}; alive={!process.HasExited}");
                    if(hwnd!=0&&Win32.GetForegroundWindow()!=hwnd){Win32.Send(Win32.Key(0x12),Win32.Key(0x12,true));Win32.SetForegroundWindow(hwnd);await Task.Delay(150);}
                    Check(hwnd!=0&&Win32.GetForegroundWindow()==hwnd,"direct selection fixture is foreground: "+fallback);
                    if(!fallback){Win32.Send(Win32.Key(0x11),Win32.Key(0x41),Win32.Key(0x41,true),Win32.Key(0x11,true));await Task.Delay(80);}
                    System.Windows.Clipboard.SetText("clipboard preserved during direct selection");
                    var request=new SelectionRequest(hwnd,0,new Win32.POINT(120,150));
                    var read=reader.ReadAsync(request,CancellationToken.None);
                    if(oversized) {
                        bool rejected=false;try{await read;}catch(InvalidOperationException error){rejected=error.Message.Contains("20000",StringComparison.Ordinal);}
                        Check(rejected,"oversized UIA selection is rejected without silently translating a prefix");continue;
                    }
                    int heartbeat=0;while(!read.IsCompleted){heartbeat++;await Task.Delay(10);}
                    var selected=await read;if(selected!="workspace")Console.WriteLine($"Selection diagnostic: fallback={fallback}; length={selected.Length}; foreground={Win32.GetForegroundWindow()==hwnd}; fixture={File.ReadAllText(status).Trim().Replace("\n","/")}");
                    Check(selected=="workspace",fallback?"dedicated clipboard worker reads fallback selection":"dedicated UIA worker reads selected text");
                    Check(System.Windows.Clipboard.GetText()=="clipboard preserved during direct selection","direct selection preserves original clipboard: "+fallback);
                    if(fallback) {
                        Check(heartbeat>=5,"clipboard fallback leaves UI dispatcher responsive");
                        File.WriteAllText(status,"ready\n");using var cancel=new CancellationTokenSource();
                        var cancelled=reader.ReadAsync(request,cancel.Token);
                        await WaitUntil(()=>File.ReadAllText(status).Contains("copy"));cancel.Cancel();reader.Dispose();
                        bool stopped=false;try{await cancelled;}catch(OperationCanceledException){stopped=true;}
                        await Task.Delay(300);
                        Check(stopped&&System.Windows.Clipboard.GetText()=="clipboard preserved during direct selection","cancel and dispose during copy still restore clipboard");
                    }
                }finally {if(!process.HasExited){process.Kill(true);await process.WaitForExitAsync();}}
            }
        }finally {
            if(original==null)System.Windows.Clipboard.Clear();else System.Windows.Clipboard.SetDataObject(original,true);
            SelectionReader.DisposeSnapshot(original);
        }
    }
}
