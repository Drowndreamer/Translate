using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using Translate.Core;
using Translate.Native;
namespace Translate.Tests;
internal static partial class Program
{
    private static async Task ReliabilityUnitChecks()
    {
        var settings=new AppSettings { ApiKey="unit-test-credential" };
        foreach(var body in new[]{"[]","{\"choices\":null}","{\"choices\":{}}","{\"choices\":[null]}"}) {
            foreach(var media in new[]{"application/json","text/event-stream"}) {
                var payload=media=="application/json"?body:"data: "+body+"\n\n";
                using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(payload,Encoding.UTF8,media)})));
                using var client=new TranslationClient(http);
                bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
                Check(rejected,"invalid response shape handled: "+media+" "+body);
            }
        }
        bool sent=false;
        using(var http=new HttpClient(new Handler((_,_)=>{sent=true;throw new Exception("Input should be rejected before sending.");})))
        using(var client=new TranslationClient(http)) {
            bool rejected=false;try{await foreach(var part in client.TranslateAsync(new string('x',TranslationLimits.InputCharacters+1),settings)){} }catch(ArgumentException){rejected=true;}
            Check(rejected&&!sent,"oversized input rejected before a network request");
        }
        foreach(var media in new[]{"application/json","text/event-stream"}) {
            var content=new string('x',TranslationLimits.OutputCharacters+1);
            var body=media=="application/json"?JsonSerializer.Serialize(new{choices=new[]{new{message=new{content}}}}):"data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content},finish_reason="stop"}}})+"\n\n";
            using var http=new HttpClient(new Handler((_,_)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(body,Encoding.UTF8,media)})));
            using var client=new TranslationClient(http);
            bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
            Check(rejected,"oversized translated output bounded: "+media);
        }
        var manyFrames=string.Concat(Enumerable.Repeat("data: {\"choices\":[{\"delta\":{\"content\":\""+new string('x',1000)+"\"}}]}\n\n",201));
        using(var http=Mock(manyFrames))using(var client=new TranslationClient(http)) {
            bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
            Check(rejected,"cumulative streamed output bounded across frames");
        }
        using(var http=Mock("data: "+new string('x',TranslationLimits.EventCharacters+1)+"\n\n"))using(var client=new TranslationClient(http)) {
            bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
            Check(rejected,"oversized SSE event bounded");
        }
        using(var bytes=new GeneratedStream(TranslationLimits.ResponseBytes+64*1024))
        using(var http=StreamHttp(bytes))using(var client=new TranslationClient(http)) {
            bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
            Check(rejected&&bytes.BytesRead<=TranslationLimits.ResponseBytes+4096,"unknown-length response without newline bounded in bytes");
            Check(bytes.Disposed,"malformed or oversized response stream disposed");
        }
        using(var bytes=new GeneratedStream(64))
        using(var http=StreamHttp(bytes,TranslationLimits.ResponseBytes+1))using(var client=new TranslationClient(http)) {
            bool rejected=false;try{await Collect(client,settings);}catch(TranslationException){rejected=true;}
            Check(rejected&&bytes.BytesRead==0,"declared oversized response rejected before reading body");
        }
        var finalFrame="data: {\"choices\":[{\"delta\":{\"content\":\"complete\"},\"finish_reason\":\"stop\"}]}\n\n";
        using(var bytes=new OpenTailStream(finalFrame))using(var http=StreamHttp(bytes))using(var client=new TranslationClient(http))using(var timeout=new CancellationTokenSource(1500)) {
            Check(await Collect(client,settings,timeout.Token)=="complete","finish_reason completes even when server never sends DONE or closes");
            Check(bytes.Disposed,"successful response stream disposed");
        }
        using(var bytes=new OpenTailStream("data: {\"choices\":[{\"delta\":{\"content\":\"partial\"}}]}\n\n"))
        using(var http=StreamHttp(bytes))using(var client=new TranslationClient(http))using(var timeout=new CancellationTokenSource(40)) {
            bool cancelled=false;try{await Collect(client,settings,timeout.Token);}catch(OperationCanceledException){cancelled=true;}
            Check(cancelled&&bytes.Disposed,"stalled stream cancellation releases response resources");
        }
        await WorkerChecks();
        var folder=Path.Combine(Path.GetTempPath(),"Translate-tests-"+Guid.NewGuid().ToString("N"));
        try {
            AppDiagnostics.Record("test",new InvalidOperationException("private-source-and-credential-marker"),folder);
            var log=Path.Combine(folder,"errors.log");var content=File.ReadAllText(log);
            Check(content.Contains(nameof(InvalidOperationException))&&!content.Contains("private-source-and-credential-marker"),"diagnostic records exclude exception messages and user content");
            File.WriteAllText(log,new string('x',256*1024));AppDiagnostics.Record("test",new IOException("private-marker"),folder);
            Check(File.Exists(Path.Combine(folder,"errors.previous.log"))&&new FileInfo(log).Length<256*1024,"diagnostic file rotation bounds disk use");
        }finally{DeleteTestDirectory(folder);}
        var listeners=new List<Task>();
        for(int i=0;i<20;i++) {
            var instance=new SingleInstance("reliability-"+Guid.NewGuid().ToString("N"));instance.Listen();instance.Listen();
            listeners.Add((Task)typeof(SingleInstance).GetField("_server",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(instance)!);
            instance.Dispose();instance.Dispose();
        }
        await Task.WhenAll(listeners).WaitAsync(TimeSpan.FromSeconds(3));
        Check(listeners.All(t=>t.IsCompletedSuccessfully),"repeated single-instance start/stop has no disposed-token race");
    }
    private static async Task WorkerChecks()
    {
        using(var worker=new AutomationWorker())using(var release=new ManualResetEventSlim()) {
            var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var read=worker.TryRun(()=>{entered.SetResult(true);release.Wait();return Thread.CurrentThread.GetApartmentState();})!;
            await entered.Task;
            try {
                bool timedOut=false;try{await read.WaitAsync(TimeSpan.FromMilliseconds(25));}catch(TimeoutException){timedOut=true;}
                Check(timedOut&&worker.TryRun(()=>1)==null,"hung UIA provider cannot queue more worker calls");
                var watch=Stopwatch.StartNew();worker.Dispose();
                Check(watch.ElapsedMilliseconds<500,"disposing a busy UIA worker does not wait for its provider");
            }finally{release.Set();}
            Check(await read==ApartmentState.MTA,"UI Automation runs on its dedicated MTA thread");
        }
        using(var worker=new ClipboardWorker()) {
            Check(await worker.RunAsync(async()=>{int thread=Environment.CurrentManagedThreadId;await Task.Delay(15);return Thread.CurrentThread.GetApartmentState()==ApartmentState.STA&&Environment.CurrentManagedThreadId==thread;},CancellationToken.None),"clipboard continuations stay on their dedicated STA thread");
            var entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var active=worker.RunAsync(async()=>{entered.SetResult(true);await release.Task;return 42;},CancellationToken.None);
            await entered.Task;worker.Dispose();release.SetResult(true);
            Check(await active==42,"clipboard worker finishes an active transaction before shutdown");
            bool rejected=false;try{await worker.RunAsync(()=>Task.FromResult(1),CancellationToken.None);}catch(ObjectDisposedException){rejected=true;}
            Check(rejected,"disposed clipboard worker rejects new transactions");
        }
    }
    private static HttpClient StreamHttp(Stream stream,long? length=null)=>new(new Handler((_,_)=> {
        var content=new StreamContent(stream);content.Headers.ContentType=new("text/event-stream");if(length!=null)content.Headers.ContentLength=length;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=content});
    }));
    private sealed class GeneratedStream(long length) : Stream
    {
        internal long BytesRead { get; private set; }
        internal bool Disposed { get; private set; }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count){int read=(int)Math.Min(count,length-BytesRead);buffer.AsSpan(offset,read).Fill((byte)'x');BytesRead+=read;return read;}
        public override ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default){token.ThrowIfCancellationRequested();int read=(int)Math.Min(buffer.Length,length-BytesRead);buffer.Span[..read].Fill((byte)'x');BytesRead+=read;return ValueTask.FromResult(read);}
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
    private sealed class OpenTailStream(string prefix) : Stream
    {
        private readonly byte[] _bytes=Encoding.UTF8.GetBytes(prefix);private int _position;
        internal bool Disposed { get; private set; }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken token=default)
        {
            if(_position<_bytes.Length){int read=Math.Min(buffer.Length,_bytes.Length-_position);_bytes.AsMemory(_position,read).CopyTo(buffer);_position+=read;return read;}
            await Task.Delay(Timeout.Infinite,token);return 0;
        }
        public override void Flush()=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        protected override void Dispose(bool disposing){Disposed=true;base.Dispose(disposing);}
    }
}
