using System.Diagnostics;

namespace Starling.Dom.Events;

/// <summary>
/// Base type for any object that can receive DOM events. The DOM spec marks
/// <c>Node</c> as inheriting from <c>EventTarget</c>; we follow that shape so
/// downstream consumers can attach listeners to nodes uniformly.
/// </summary>
public abstract class EventTarget
{
    private List<ListenerEntry>? _listeners;

    public void AddEventListener(string type, EventListener listener, AddEventListenerOptions options = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(listener);

        _listeners ??= [];

        // Per spec, identical (type, callback, capture) triples are deduplicated.
        foreach (var entry in _listeners)
        {
            if (entry.Removed) continue;
            if (entry.Type == type && entry.Capture == options.Capture && entry.Listener == listener)
                return;
        }

        _listeners.Add(new ListenerEntry(type, listener, options));
    }

    public bool RemoveEventListener(string type, EventListener listener, RemoveEventListenerOptions options = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentNullException.ThrowIfNull(listener);
        if (_listeners is null) return false;

        for (var i = 0; i < _listeners.Count; i++)
        {
            var entry = _listeners[i];
            if (entry.Removed) continue;
            if (entry.Type == type && entry.Capture == options.Capture && entry.Listener == listener)
            {
                entry.Removed = true;
                return true;
            }
        }
        return false;
    }

    public bool DispatchEvent(Event @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        if (@event.DispatchFlag)
            throw new InvalidOperationException("Event is already being dispatched.");

        // Surface the dispatch in OpenTelemetry traces — the Starling.Engine
        // ActivitySource is shared with every other engine subsystem so the
        // span shows up beside fetch/parse/layout/paint in the dashboard.
        // StartActivity returns null when no listener is attached, so the
        // hot path stays allocation-free.
        using var activity = EventActivitySource.StartActivity("dom.event", ActivityKind.Internal);
        if (activity is not null)
        {
            activity.SetTag("event.type", @event.Type);
            activity.SetTag("event.bubbles", @event.Bubbles);
            activity.SetTag("event.cancelable", @event.Cancelable);
            activity.SetTag("event.target", DescribeTarget(this));
        }

        var notCanceled = EventDispatcher.Dispatch(this, @event);

        if (activity is not null)
        {
            activity.SetTag("event.defaultPrevented", @event.DefaultPrevented);
            activity.SetTag("event.propagationStopped", @event.PropagationStopped);
        }

        return notCanceled;
    }

    private static string DescribeTarget(EventTarget target) => target switch
    {
        Element el => string.IsNullOrEmpty(el.Id)
            ? el.TagName
            : $"{el.TagName}#{el.Id}",
        Document => "#document",
        _ => target.GetType().Name,
    };

    private static readonly ActivitySource EventActivitySource = new("Starling.Engine");

    internal IReadOnlyList<ListenerEntry> ListenersSnapshot()
        => _listeners is null ? Array.Empty<ListenerEntry>() : _listeners.ToArray();

    internal void CompactListeners()
    {
        if (_listeners is null) return;
        _listeners.RemoveAll(e => e.Removed);
    }
}

public delegate void EventListener(Event @event);

public readonly record struct AddEventListenerOptions(
    bool Capture = false,
    bool Once = false,
    bool Passive = false);

public readonly record struct RemoveEventListenerOptions(bool Capture = false);

internal sealed class ListenerEntry
{
    public ListenerEntry(string type, EventListener listener, AddEventListenerOptions options)
    {
        Type = type;
        Listener = listener;
        Capture = options.Capture;
        Once = options.Once;
        Passive = options.Passive;
    }

    public string Type { get; }
    public EventListener Listener { get; }
    public bool Capture { get; }
    public bool Once { get; }
    public bool Passive { get; }
    public bool Removed { get; set; }
}
