namespace Starling.Dom.Events;

/// <summary>
/// Base DOM Event per [DOM §2.2](https://dom.spec.whatwg.org/#event).
/// </summary>
public class Event
{
    public Event(string type, EventInit init = default)
    {
        // Empty type is allowed: document.createEvent() produces an event whose
        // type is "" until legacy initEvent() sets it (DOM §2.9).
        ArgumentNullException.ThrowIfNull(type);
        Type = type;
        Bubbles = init.Bubbles;
        Cancelable = init.Cancelable;
        Composed = init.Composed;
        TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public string Type { get; private set; }
    public EventTarget? Target { get; internal set; }
    public EventTarget? CurrentTarget { get; internal set; }
    public EventPhase EventPhase { get; internal set; } = EventPhase.None;
    public bool Bubbles { get; private set; }
    public bool Cancelable { get; private set; }
    public bool Composed { get; }

    /// <summary>True once a legacy <c>document.createEvent</c> + <c>initEvent</c>
    /// has initialized this event (or for constructor-built events, always).
    /// An uninitialized event must not be dispatched (DOM §2.9).</summary>
    public bool Initialized { get; internal set; } = true;

    /// <summary>Legacy <c>Event.initEvent</c> (DOM §2.9). Re-initializes a
    /// not-yet-dispatched event; a no-op while dispatching. Sets type/bubbles/
    /// cancelable and clears the propagation + canceled flags.</summary>
    public void InitEvent(string type, bool bubbles, bool cancelable)
    {
        if (DispatchFlag) return;
        Initialized = true;
        Type = type;
        Bubbles = bubbles;
        Cancelable = cancelable;
        PropagationStopped = false;
        ImmediatePropagationStopped = false;
        DefaultPrevented = false;
    }
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
