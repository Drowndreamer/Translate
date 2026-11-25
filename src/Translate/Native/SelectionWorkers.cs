using System.Collections.Concurrent;
using System.Windows.Threading;
namespace Translate.Native;
// At most one UIA provider call can be active. A hung provider cannot consume
// additional thread-pool threads or build up a queue of abandoned selections.
internal sealed class AutomationWorker : IDisposable
{
    private readonly BlockingCollection<Action> _queue=new(1);
    private readonly object _sync=new();
    private readonly Thread _thread;
    private int _busy;
    private bool _disposed;
    internal AutomationWorker()
    {
        _thread=new Thread(()=> {
            try{foreach(var work in _queue.GetConsumingEnumerable())work();}
            finally{_queue.Dispose();}
        }) { IsBackground=true,Name="Translate UI Automation" };
        _thread.SetApartmentState(ApartmentState.MTA);_thread.Start();
    }
    internal Task<T>? TryRun<T>(Func<T> action)
    {
        lock(_sync) {
            if(_disposed||Interlocked.CompareExchange(ref _busy,1,0)!=0)return null;
            var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _queue.Add(()=> {
                try{result.TrySetResult(action());}
                catch(Exception error){result.TrySetException(error);}
                finally{Volatile.Write(ref _busy,0);}
            });
            // Observe failures even when the caller has already timed out or cancelled.
            _=result.Task.ContinueWith(t=>{_ = t.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);
            return result.Task;
        }
    }
    public void Dispose(){lock(_sync){if(_disposed)return;_disposed=true;_queue.CompleteAdding();}}
}
// OLE clipboard access needs an STA dispatcher. Keep it off the UI thread and
// let an already-started copy transaction restore the clipboard before shutdown.
internal sealed class ClipboardWorker : IDisposable
{
    private readonly object _sync=new();
    private readonly TaskCompletionSource<Dispatcher> _ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _active;
    private bool _disposed;
    internal ClipboardWorker()
    {
        var thread=new Thread(()=> {
            try {
                var dispatcher=Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                _ready.SetResult(dispatcher);Dispatcher.Run();
            }catch(Exception error){_ready.TrySetException(error);Translate.Core.AppDiagnostics.Record("clipboard-thread",error);}
        }) { IsBackground=true,Name="Translate clipboard" };
        thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    internal async Task<T> RunAsync<T>(Func<Task<T>> action,CancellationToken token)
    {
        lock(_sync){ObjectDisposedException.ThrowIf(_disposed,this);_active++;}
        try {
            var dispatcher=await _ready.Task.WaitAsync(token).ConfigureAwait(false);
            var operation=dispatcher.InvokeAsync(action,DispatcherPriority.Normal,token);
            return await (await operation.Task.ConfigureAwait(false)).ConfigureAwait(false);
        } finally {
            bool stop;lock(_sync){_active--;stop=_disposed&&_active==0;}
            if(stop)await StopAsync().ConfigureAwait(false);
        }
    }
    private async Task StopAsync()
    {
        var dispatcher=await _ready.Task.ConfigureAwait(false);
        if(!dispatcher.HasShutdownStarted)dispatcher.BeginInvokeShutdown(DispatcherPriority.ContextIdle);
    }
    public void Dispose()
    {
        bool stop;lock(_sync){if(_disposed)return;_disposed=true;stop=_active==0;}
        if(stop)_=StopAsync();
    }
}
