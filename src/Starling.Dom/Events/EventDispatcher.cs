namespace Tessera.Dom.Events;

/// <summary>
/// Implements [DOM §2.9 Dispatching events](https://dom.spec.whatwg.org/#dispatching-events).
/// </summary>
internal static class EventDispatcher
{
    public static bool Dispatch(EventTarget target, Event @event)
    {
        @event.DispatchFlag = true;
        try
        {
            @event.Target = target;

            // Build the event path: target up to root. For Node targets, walk ParentNode.
            var path = BuildPath(target);

            // 1) Capture phase: root → target (exclusive of target).
            @event.EventPhase = EventPhase.CapturingPhase;
            for (var i = path.Count - 1; i >= 1; i--)
            {
                if (@event.PropagationStopped) break;
                InvokeListeners(path[i], @event, capture: true);
            }

            // 2) At target.
            if (!@event.PropagationStopped)
            {
                @event.EventPhase = EventPhase.AtTarget;
                InvokeListeners(target, @event, capture: null);
            }

            // 3) Bubble phase: target+1 → root (only if event bubbles).
            if (@event.Bubbles && !@event.PropagationStopped)
            {
                @event.EventPhase = EventPhase.BubblingPhase;
                for (var i = 1; i < path.Count; i++)
                {
                    if (@event.PropagationStopped) break;
                    InvokeListeners(path[i], @event, capture: false);
                }
            }
        }
        finally
        {
            @event.EventPhase = EventPhase.None;
            @event.CurrentTarget = null;
            @event.DispatchFlag = false;
        }

        return !@event.DefaultPrevented;
    }

    private static List<EventTarget> BuildPath(EventTarget target)
    {
        var path = new List<EventTarget> { target };
        if (target is Node node)
        {
            for (var ancestor = node.ParentNode; ancestor is not null; ancestor = ancestor.ParentNode)
                path.Add(ancestor);
        }
        return path;
    }

    /// <summary>
    /// Invoke listeners on <paramref name="target"/> matching the event type.
    /// <paramref name="capture"/>: true → capture-only, false → bubble-only, null → both (at-target phase).
    /// </summary>
    private static void InvokeListeners(EventTarget target, Event @event, bool? capture)
    {
        @event.CurrentTarget = target;
        // Snapshot to allow add/remove during dispatch (spec: only listeners present at start are invoked).
        var listeners = target.ListenersSnapshot();
        var needsCompact = false;

        foreach (var entry in listeners)
        {
            if (entry.Removed) continue;
            if (entry.Type != @event.Type) continue;
            if (capture is not null && entry.Capture != capture.Value) continue;

            if (entry.Once)
            {
                entry.Removed = true;
                needsCompact = true;
            }

            try
            {
                entry.Listener(@event);
            }
            catch
            {
                // Swallow listener exceptions per spec (browsers report to console, we drop on the floor in v1).
            }

            if (@event.ImmediatePropagationStopped) break;
        }

        if (needsCompact) target.CompactListeners();
    }
}
