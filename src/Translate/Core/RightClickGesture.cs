namespace Translate.Core;
public enum GestureEffect { Pass, Suppress, ReplayClick, ReplayDown, Trigger }
public sealed class RightClickGesture
{
    private enum State { Idle, Pending, Triggered, Dragging }
    private State _state;
    private double _started;
    private int _x, _y;
    public int HoldMilliseconds { get; set; } = 300;
    public bool IsPending => _state == State.Pending;
    public bool IsCaptured => _state is State.Pending or State.Triggered;
    public GestureEffect Down(int x, int y, double now)
    { if (_state != State.Idle) return GestureEffect.Pass; _x = x; _y = y; _started = now; _state = State.Pending; return GestureEffect.Suppress; }
    public GestureEffect Move(int x, int y)
    {
        if (_state == State.Pending && (Math.Abs(x - _x) > 6 || Math.Abs(y - _y) > 6))
        { _state = State.Dragging; return GestureEffect.ReplayDown; }
        return GestureEffect.Pass;
    }
    public GestureEffect Tick(double now)
    { if (_state == State.Pending && now - _started >= HoldMilliseconds) { _state = State.Triggered; return GestureEffect.Trigger; } return GestureEffect.Pass; }
    public GestureEffect Up(double now)
    {
        var state = _state; _state = State.Idle;
        return state switch { State.Pending => now - _started >= HoldMilliseconds ? GestureEffect.Trigger : GestureEffect.ReplayClick,
            State.Triggered => GestureEffect.Suppress, _ => GestureEffect.Pass };
    }
    public void Cancel() { if (_state == State.Pending) _state = State.Triggered; }
}
