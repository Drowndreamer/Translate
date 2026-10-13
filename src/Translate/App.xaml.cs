using System.Windows;
using Translate.Core;
namespace Translate;
public partial class App : Application
{
    private SingleInstance? _instance;
    private AppController? _controller;
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
    protected override void OnExit(ExitEventArgs e){_controller?.Dispose();_instance?.Dispose();base.OnExit(e);}
}
