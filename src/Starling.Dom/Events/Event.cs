namespace Tessera.Dom.Events;

/// <summary>
/// Base DOM Event per [DOM §2.2](https://dom.spec.whatwg.org/#event).
/// </summary>
public class Event
{
    public Event(string type, EventInit init = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        Type = type;
        Bubbles = init.Bubbles;
        Cancelable = init.Cancelable;
        Composed = init.Composed;
        TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public string Type { get; }
    public EventTarget? Target { get; internal set; }
    public EventTarget? CurrentTarget { get; internal set; }
    public EventPhase EventPhase { get; internal set; } = EventPhase.None;
    public bool Bubbles { get; }
    public bool Cancelable { get; }
    public bool Composed { get; }
    public bool DefaultPrevented { get; internal set; }
    public bool IsTrusted { get; internal set; }
    public long TimeStamp { get; }

    internal bool PropagationStopped { get; private set; }
    internal bool ImmediatePropagationStopped { get; private set; }
    internal bool DispatchFlag { get; set; }

    public void StopPropagation() => PropagationStopped = true;

    public void StopImmediatePropagation()
    {
        PropagationStopped = true;
        ImmediatePropagationStopped = true;
    }

    public void PreventDefault()
    {
        if (Cancelable)
            DefaultPrevented = true;
    }

    /// <summary>Reset transient flags so the same instance can be re-dispatched (test-only path).</summary>
    internal void ResetForDispatch()
    {
        Target = null;
        CurrentTarget = null;
        EventPhase = EventPhase.None;
        DefaultPrevented = false;
        PropagationStopped = false;
        ImmediatePropagationStopped = false;
    }
}

public readonly record struct EventInit(
    bool Bubbles = false,
    bool Cancelable = false,
    bool Composed = false);

public enum EventPhase : byte
{
    None = 0,
    CapturingPhase = 1,
    AtTarget = 2,
    BubblingPhase = 3,
}
