using System.Text;
using System.Windows.Controls;
using System.Windows.Threading;
namespace Translate.UI;
// The timer runs only while there is pending text; a completed request always flushes.
internal sealed class BufferedTextOutput : IDisposable
{
    private readonly TextBox _target;
    private readonly DispatcherTimer _timer;
    private readonly StringBuilder _pending=new();
    internal int Length { get; private set; }
    internal BufferedTextOutput(TextBox target)
    {
        _target=target;_target.IsUndoEnabled=false;
        _timer=new DispatcherTimer(DispatcherPriority.Background,target.Dispatcher) { Interval=TimeSpan.FromMilliseconds(40) };
        _timer.Tick+=OnTick;
    }
    internal void Append(string text)
    {
        if(text.Length==0)return;
        _pending.Append(text);Length+=text.Length;
        if(_pending.Length>=2048)Flush();else if(!_timer.IsEnabled)_timer.Start();
    }
    private void OnTick(object? sender,EventArgs e)=>Flush();
    internal void Flush()
    {
        _timer.Stop();
        if(_pending.Length==0)return;
        var text=_pending.ToString();_pending.Clear();_target.AppendText(text);
    }
    internal void Clear(){_timer.Stop();_pending.Clear();Length=0;_target.Clear();}
    public void Dispose(){_timer.Stop();_timer.Tick-=OnTick;_pending.Clear();}
}
