using System.Windows;
using System.Windows.Threading;
using Translate.Core;
namespace Translate;
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;
    public App()
    {
        DispatcherUnhandledException+=DispatcherFailed;
        AppDomain.CurrentDomain.UnhandledException+=ProcessFailed;
        TaskScheduler.UnobservedTaskException+=TaskFailed;
    }
    private void DispatcherFailed(object sender,DispatcherUnhandledExceptionEventArgs e)=>AppDiagnostics.Record("dispatcher",e.Exception);
    private void ProcessFailed(object sender,UnhandledExceptionEventArgs e){if(e.ExceptionObject is Exception error)AppDiagnostics.Record("process",error);}
    private void TaskFailed(object? sender,UnobservedTaskExceptionEventArgs e){AppDiagnostics.Record("background-task",e.Exception);e.SetObserved();}
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);_instance=new SingleInstance();
        if(e.Args.Length==1&&e.Args[0]=="--shutdown"){
            // Installer/uninstaller command: no settings form, network, or credential writes.
            int result=_instance.IsOwner||_instance.ShutdownRunningCopy()?0:1;Shutdown(result);return;
        }
        if(!_instance.IsOwner){_instance.Signal();Shutdown();return;}
        _controller=new AppController();
        _instance.ShowRequested+=()=>Dispatcher.BeginInvoke(()=>_controller?.ShowMain());
        _instance.ExitRequested+=()=>Dispatcher.BeginInvoke(()=>_controller?.Exit());_instance.Listen();_controller.Start();
    }
    protected override void OnExit(ExitEventArgs e)
    {
        try{_controller?.Dispose();_instance?.Dispose();}
        finally {
            DispatcherUnhandledException-=DispatcherFailed;AppDomain.CurrentDomain.UnhandledException-=ProcessFailed;TaskScheduler.UnobservedTaskException-=TaskFailed;
            base.OnExit(e);
        }
    }
}
