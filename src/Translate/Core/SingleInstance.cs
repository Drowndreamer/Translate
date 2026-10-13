using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
namespace Translate.Core;
internal sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private readonly string _name;
    private readonly byte[] _executableIdentity=SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(Environment.ProcessPath!).ToUpperInvariant()));
    private readonly CancellationTokenSource _stop=new();
    private Task? _server;
    public bool IsOwner { get; }
    public event Action? ShowRequested;
    public event Action? ExitRequested;
    internal SingleInstance(string? testScope=null)
    {
        _name="Translate-"+Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Environment.UserDomainName+"\\"+Environment.UserName)))[..16];
        if(testScope!=null)_name+="-"+testScope; // Tests do not attach to the user's running app.
        _mutex=new Mutex(true,"Local\\"+_name,out var created);IsOwner=created;
    }
    public void Listen() => _server=Task.Run(async()=> {
        while(!_stop.IsCancellationRequested) {
            try {
                await using var pipe=new NamedPipeServerStream(_name,PipeDirection.In,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous|PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_stop.Token);
                // A stalled client cannot block future launches indefinitely.
                using var timeout=CancellationTokenSource.CreateLinkedTokenSource(_stop.Token);timeout.CancelAfter(3000);
                var command=new byte[1];await pipe.ReadExactlyAsync(command,timeout.Token);
                if(command[0]==1)ShowRequested?.Invoke();
                else if(command[0]==2){
                    var identity=new byte[32];await pipe.ReadExactlyAsync(identity,timeout.Token);
                    // Uninstalling one copy must not stop a portable copy in another directory.
                    if(CryptographicOperations.FixedTimeEquals(identity,_executableIdentity))ExitRequested?.Invoke();
                }
            } catch(OperationCanceledException){if(_stop.IsCancellationRequested)break;}catch(IOException){if(_stop.IsCancellationRequested)break;}
        }
    });
    public void Signal()=>Send(1);
    public bool ShutdownRunningCopy()
    {
        if(!Send(2))return false;
        bool acquired=false;
        try {
            try{acquired=_mutex.WaitOne(8000);}catch(AbandonedMutexException){acquired=true;}
            return acquired;
        } finally{if(acquired)_mutex.ReleaseMutex();}
    }
    private bool Send(byte command)
    {
        try{
            using var pipe=new NamedPipeClientStream(".",_name,PipeDirection.Out,PipeOptions.CurrentUserOnly);pipe.Connect(1500);pipe.WriteByte(command);
            if(command==2)pipe.Write(_executableIdentity);return true;
        }catch(TimeoutException){return false;}catch(IOException){return false;}
    }
    public void Dispose(){_stop.Cancel();if(IsOwner)_mutex.ReleaseMutex();_mutex.Dispose();_stop.Dispose();}
}
