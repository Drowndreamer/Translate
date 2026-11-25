using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
using Translate.Core;
namespace Translate.Native;
internal sealed class SelectionReader : IDisposable
{
    private readonly SemaphoreSlim _clipboardGate=new(1,1);
    private readonly Lazy<AutomationWorker> _automation=new(()=>new AutomationWorker());
    private readonly Lazy<ClipboardWorker> _clipboard=new(()=>new ClipboardWorker());
    // Screenshot tools advertise bitmap, DIB and PNG representations together.
    // Budget their total data while retaining every format for a lossless restore.
    internal const long SnapshotBytes=128*1024*1024;
    private readonly object _workers=new();
    private volatile bool _disposed;
    private sealed record AutomationResult(string Text,bool Password=false,bool TooLong=false);
    internal async Task<string> ReadAsync(SelectionRequest request,CancellationToken token)
    {
        ObjectDisposedException.ThrowIf(_disposed,this);
        token.ThrowIfCancellationRequested();
        if(Win32.GetForegroundWindow()!=request.Window)return "";
        Task<AutomationResult>? read;
        lock(_workers){ObjectDisposedException.ThrowIf(_disposed,this);read=_automation.Value.TryRun(()=>ReadAutomation(request));}
        if(read!=null) {
            try {
                var result=await read.WaitAsync(TimeSpan.FromMilliseconds(350),token).ConfigureAwait(false);
                if(result.Password)throw new InvalidOperationException("密码输入框不会被读取。");
                if(result.TooLong)throw new InvalidOperationException("原文过长，请缩短到 20000 字符以内后重试。");
                if(!string.IsNullOrWhiteSpace(result.Text)){TranslationLimits.ValidateInput(result.Text);return result.Text;}
            } catch(TimeoutException) {} // A slow provider occupies only its own bounded worker.
        }
        token.ThrowIfCancellationRequested();
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(1500);var clipboardToken=timeout.Token;
        try {
            Task<string> copy;
            lock(_workers){ObjectDisposedException.ThrowIf(_disposed,this);copy=_clipboard.Value.RunAsync(()=>ReadClipboardAsync(request,clipboardToken),clipboardToken);}
            return await copy.WaitAsync(clipboardToken).ConfigureAwait(false);
        } catch(OperationCanceledException) when(!token.IsCancellationRequested) {
            throw new InvalidOperationException("读取剪贴板超时，请直接输入或粘贴到翻译页。");
        }
    }
    private static AutomationResult ReadAutomation(SelectionRequest request)
    {
        try {
            var elements=new List<AutomationElement?>();
            if(Win32.GetForegroundWindow()==request.Window)elements.Add(AutomationElement.FocusedElement);
            if(request.Focus!=0)elements.Add(AutomationElement.FromHandle(request.Focus));
            elements.Add(AutomationElement.FromHandle(request.Window));
            foreach(var element in elements) {
                var current=element;
                for(int depth=0;current!=null && depth<4;depth++,current=TreeWalker.ControlViewWalker.GetParent(current)) {
                    if(current.Current.IsPassword)return new("",true);
                    if(current.TryGetCurrentPattern(TextPattern.Pattern,out var pattern)) {
                        var ranges=((TextPattern)pattern).GetSelection();
                        var selected=new System.Text.StringBuilder();
                        foreach(var range in ranges) {
                            if(selected.Length>0)selected.Append('\n');
                            selected.Append(range.GetText(Math.Max(1,TranslationLimits.InputCharacters+1-selected.Length)));
                            if(selected.Length>TranslationLimits.InputCharacters)break;
                        }
                        if(selected.Length>TranslationLimits.InputCharacters)return new("",TooLong:true);
                        var text=selected.ToString().Trim();
                        if(!string.IsNullOrWhiteSpace(text))return new(text);
                    }
                }
            }
        } catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException) {}
        return new("");
    }
    internal static DataObject? SnapshotClipboard()
    {
        return SnapshotDataObject(Clipboard.GetDataObject());
    }
    internal static DataObject? SnapshotDataObject(IDataObject? source,long maximumBytes=SnapshotBytes)
    {
        if(maximumBytes<0)throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        if(source==null)return null;
        var snapshot=new DataObject();long remaining=maximumBytes;
        try {
            var formats=source.GetFormats(false);
            if(formats.Length>64)throw new InvalidOperationException("当前剪贴板内容过于复杂，请手动粘贴到翻译页。");
            foreach(var format in formats) {
                var value=source.GetData(format,false);if(value==null)continue;
                long size=value switch {
                    string text=>(long)text.Length*2, string[] paths=>paths.Sum(p=>(long)p.Length*2),
                    byte[] bytes=>bytes.LongLength, MemoryStream memory=>memory.Length,
                    BitmapSource bitmap=>(long)bitmap.PixelWidth*bitmap.PixelHeight*4,
                    System.Drawing.Image image=>(long)image.Width*image.Height*4,
                    int or long or short or bool=>8,
                    _=>throw new InvalidOperationException("当前剪贴板格式无法安全恢复，请手动粘贴到翻译页。")
                };
                if(size>remaining)throw new InvalidOperationException("剪贴板备份内容过大，请手动粘贴到翻译页。");
                remaining-=size;
                object copy=value switch {
                    string text=>text, string[] paths=>paths.ToArray(), byte[] bytes=>bytes.ToArray(),
                    MemoryStream memory=>new MemoryStream(memory.ToArray()),
                    BitmapSource bitmap=>CloneBitmap(bitmap), System.Drawing.Image image=>image.Clone(),
                    _=>value
                };
                if(format==DataFormats.Bitmap&&copy is BitmapSource imageCopy)snapshot.SetImage(imageCopy);
                else snapshot.SetData(format,copy,false);
            }
            return snapshot;
        } catch{DisposeSnapshot(snapshot);throw;}
    }
    internal static void DisposeSnapshot(DataObject? snapshot)
    {
        if(snapshot==null)return;
        var disposed=new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach(var format in snapshot.GetFormats(false))
            if(snapshot.GetData(format,false) is IDisposable resource&&disposed.Add(resource))resource.Dispose();
    }
    private static BitmapSource CloneBitmap(BitmapSource source) {var copy=source.Clone();copy.Freeze();return copy;}
    internal static bool RestoreClipboard(DataObject? snapshot,uint expectedSequence)
    {
        if(Win32.GetClipboardSequenceNumber()!=expectedSequence)return false;
        if(snapshot==null)Clipboard.Clear();else Clipboard.SetDataObject(snapshot,true);
        return true;
    }
    private async Task<string> ReadClipboardAsync(SelectionRequest request,CancellationToken token)
    {
        await _clipboardGate.WaitAsync(token);
        DataObject? snapshot=null;uint copiedSequence=0;
        try {
            if(Win32.GetForegroundWindow()!=request.Window || Win32.ModifiersPressed())return "";
            var before=Win32.GetClipboardSequenceNumber();
            try {snapshot=SnapshotClipboard();} catch(ExternalException) {throw new InvalidOperationException("剪贴板正被其他程序使用，请稍后重试。");}
            token.ThrowIfCancellationRequested();
            if(Win32.GetForegroundWindow()!=request.Window||Win32.ModifiersPressed()||Win32.GetClipboardSequenceNumber()!=before)return "";
            if(!Win32.Send(Win32.Key(0x11),Win32.Key(0x43),Win32.Key(0x43,true),Win32.Key(0x11,true)))return "";
            for(int i=0;i<24;i++) {
                // Once Ctrl+C is sent, finish the bounded transaction even if the popup closes.
                await Task.Delay(25);
                var seq=Win32.GetClipboardSequenceNumber();
                if(seq==before)continue;
                Win32.GetWindowThreadProcessId(Win32.GetClipboardOwner(),out var ownerPid);
                Win32.GetWindowThreadProcessId(request.Window,out var targetPid);
                if(ownerPid!=targetPid && ownerPid!=0)return "";
                copiedSequence=seq;
                try {
                    // A copy can publish an empty/intermediate clipboard first. Finish
                    // waiting for stable text even if the user has closed the popup.
                    if(!Clipboard.ContainsText())continue;
                    var text=Clipboard.GetText().Trim();
                    if(Win32.GetClipboardSequenceNumber()!=seq||text.Length==0)continue;
                    token.ThrowIfCancellationRequested();TranslationLimits.ValidateInput(text);return text;
                }catch(ExternalException){}
            }
            token.ThrowIfCancellationRequested();return "";
        } finally {
            try {
                for(int attempt=0;copiedSequence!=0&&attempt<3;attempt++) {
                    try{RestoreClipboard(snapshot,copiedSequence);break;}
                    catch(ExternalException){if(attempt<2)await Task.Delay(30);}
                }
            } finally {try{DisposeSnapshot(snapshot);}finally{_clipboardGate.Release();}}
        }
    }
    public void Dispose()
    {
        lock(_workers) {
            if(_disposed)return;_disposed=true;
            if(_automation.IsValueCreated)_automation.Value.Dispose();
            if(_clipboard.IsValueCreated)_clipboard.Value.Dispose();
        }
    }
}
