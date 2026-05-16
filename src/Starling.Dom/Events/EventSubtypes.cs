namespace Tessera.Dom.Events;

/// <summary>UIEvent — see [UI Events §3.5](https://w3c.github.io/uievents/#interface-UIEvent).</summary>
public class UiEvent : Event
{
    public UiEvent(string type, EventInit init = default) : base(type, init) { }
    public int Detail { get; init; }
}

/// <summary>MouseEvent — pointer/mouse events.</summary>
public class MouseEvent : UiEvent
{
    public MouseEvent(string type, EventInit init = default) : base(type, init) { }
    public double ClientX { get; init; }
    public double ClientY { get; init; }
    public double ScreenX { get; init; }
    public double ScreenY { get; init; }
    public short Button { get; init; }
    public bool CtrlKey { get; init; }
    public bool ShiftKey { get; init; }
    public bool AltKey { get; init; }
    public bool MetaKey { get; init; }
    public EventTarget? RelatedTarget { get; init; }
}

/// <summary>KeyboardEvent — `keydown` / `keyup`. `keypress` is intentionally unimplemented.</summary>
public class KeyboardEvent : UiEvent
{
    public KeyboardEvent(string type, EventInit init = default) : base(type, init) { }
    public string Key { get; init; } = string.Empty;
    public string Code { get; init; } = string.Empty;
    public bool Repeat { get; init; }
    public bool CtrlKey { get; init; }
    public bool ShiftKey { get; init; }
    public bool AltKey { get; init; }
    public bool MetaKey { get; init; }
}

/// <summary>InputEvent — `input` / `beforeinput` with optional data payload.</summary>
public class InputEvent : UiEvent
{
    public InputEvent(string type, EventInit init = default) : base(type, init) { }
    public string? Data { get; init; }
    public string InputType { get; init; } = string.Empty;
}

/// <summary>FocusEvent — `focus` / `blur` / `focusin` / `focusout`.</summary>
public class FocusEvent : UiEvent
{
    public FocusEvent(string type, EventInit init = default) : base(type, init) { }
    public EventTarget? RelatedTarget { get; init; }
}

/// <summary>CustomEvent — user-defined event with arbitrary detail payload.</summary>
public class CustomEvent : Event
{
    public CustomEvent(string type, EventInit init = default) : base(type, init) { }
    public object? Detail { get; init; }
}

/// <summary>PopStateEvent — fires when navigating through history entries.</summary>
public class PopStateEvent : Event
{
    public PopStateEvent(string type, EventInit init = default) : base(type, init) { }
    public object? State { get; init; }
}
