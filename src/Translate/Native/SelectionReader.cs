using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media.Imaging;
namespace Translate.Native;
internal sealed class SelectionReader
{
    private readonly SemaphoreSlim _automationGate=new(1,1),_clipboardGate=new(1,1);
    private sealed record AutomationResult(string Text,bool Password=false);
    internal async Task<string> ReadAsync(SelectionRequest request,CancellationToken token)
    {
        if(Win32.GetForegroundWindow()!=request.Window)return "";
        if(_automationGate.Wait(0)) {
            var read=Task.Run(()=> {try{return ReadAutomation(request);}finally{_automationGate.Release();}});
            try {
                var result=await read.WaitAsync(TimeSpan.FromMilliseconds(350),token);
                if(result.Password)throw new InvalidOperationException("密码输入框不会被读取。");
                if(!string.IsNullOrWhiteSpace(result.Text))return result.Text;
            } catch(TimeoutException) {} // A slow provider is isolated from the input hook and UI.
        }
        token.ThrowIfCancellationRequested();
        return await ReadClipboardAsync(request,token);
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
                        var text=string.Join("\n",ranges.Select(r=>r.GetText(-1))).Trim();
                        if(!string.IsNullOrWhiteSpace(text))return new(text);
                    }
                }
            }
        } catch(Exception e) when(e is ElementNotAvailableException or InvalidOperationException or COMException or UnauthorizedAccessException) {}
        return new("");
    }
    internal static DataObject? SnapshotClipboard()
    {
        var source=Clipboard.GetDataObject();if(source==null)return null;
        var snapshot=new DataObject();
        foreach(var format in source.GetFormats(false)) {
            var value=source.GetData(format,false);
            if(value==null)continue;
            object copy=value switch {
                string text=>text, string[] paths=>paths.ToArray(), byte[] bytes=>bytes.ToArray(),
                MemoryStream memory=>new MemoryStream(memory.ToArray()),
                BitmapSource bitmap=>CloneBitmap(bitmap),
                System.Drawing.Image image=>image.Clone(),
                int or long or short or bool=>value,
                _=>throw new InvalidOperationException("当前剪贴板格式无法安全恢复，请手动粘贴到翻译页。")
            };
            if(format==DataFormats.Bitmap && copy is BitmapSource imageCopy) snapshot.SetImage(imageCopy);
            else snapshot.SetData(format,copy,false);
        }
        return snapshot;
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
            try {snapshot=SnapshotClipboard();} catch(ExternalException) {throw new InvalidOperationException("剪贴板正被其他程序使用，请稍后重试。");}
            token.ThrowIfCancellationRequested();
            var before=Win32.GetClipboardSequenceNumber();
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
                token.ThrowIfCancellationRequested();
                try {return Clipboard.ContainsText()?Clipboard.GetText().Trim():"";}catch(ExternalException){return "";}
            }
            return "";
        } finally {
            try {if(copiedSequence!=0)RestoreClipboard(snapshot,copiedSequence);}catch(ExternalException){}
            _clipboardGate.Release();
        }
    }
}
