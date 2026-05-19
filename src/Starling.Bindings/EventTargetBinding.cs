using System.Runtime.CompilerServices;
using Tessera.Dom.Events;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// B5-1 — Installs the JS-side <c>EventTarget</c> + <c>Event</c> bridge into a
/// realm. Populates <see cref="JsRealm.EventTargetPrototype"/>,
/// <see cref="JsRealm.EventPrototype"/>, and the matching constructor slots.
/// Listener registration on host <see cref="EventTarget"/>s is bridged via
/// a JS-listener → C#-delegate map keyed by the listener's JS object identity
/// so <c>removeEventListener(type, fn)</c> finds the same wrapper it added.
/// </summary>
/// <remarks>
/// <para>The listener wrapper looks up the realm's <see cref="JsRealm.ActiveVm"/>
/// at dispatch time. When the host fires events outside a JS-driven entry
/// (e.g. layout phase synthesizing a <c>resize</c>), a fresh <see cref="JsVm"/>
/// is created on demand. Exceptions thrown inside JS listeners are reported
/// through <see cref="JsRealm.ConsoleSink"/> and never propagate to the host.</para>
/// </remarks>
public static class EventTargetBinding
{
    // Lookup the host EventTarget that backs a JS wrapper.
    private static readonly ConditionalWeakTable<JsObject, EventTarget> WrapperToTarget = new();
    // Per host-EventTarget map of (type, capture) → (JS-listener → delegate).
    private static readonly ConditionalWeakTable<EventTarget, ListenerRegistry> Registries = new();

    /// <summary>Install the EventTarget + Event constructors and prototypes onto the realm.</summary>
    public static void Install(JsRealm realm)
    {
        ArgumentNullException.ThrowIfNull(realm);
        if (realm.EventTargetPrototype is not null) return; // idempotent

        // ----- EventTarget.prototype
        var etProto = new JsObject(realm.ObjectPrototype);
        realm.EventTargetPrototype = etProto;

        DefineMethod(realm, etProto, "addEventListener", (thisV, args) => AddListener(realm, thisV, args), length: 2);
        DefineMethod(realm, etProto, "removeEventListener", (thisV, args) => RemoveListener(realm, thisV, args), length: 2);
        DefineMethod(realm, etProto, "dispatchEvent", (thisV, args) => DispatchEvent(realm, thisV, args), length: 1);

        var etCtor = new JsNativeFunction(realm, "EventTarget", 0, (thisV, args) =>
        {
            var inst = new JsObject(etProto);
            // A plain JS-created EventTarget gets a fresh host-side InMemoryEventTarget.
            var host = new InMemoryEventTarget();
            WrapperToTarget.Add(inst, host);
            return JsValue.Object(inst);
        }, isConstructor: true);
        etCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(etProto), writable: false, enumerable: false, configurable: false));
        etProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(etCtor), writable: true, enumerable: false, configurable: true));
        realm.EventTargetConstructor = etCtor;
        realm.GlobalObject.DefineOwnProperty("EventTarget",
            PropertyDescriptor.Data(JsValue.Object(etCtor), writable: true, enumerable: false, configurable: true));

        // ----- Event.prototype
        var evProto = new JsObject(realm.ObjectPrototype);
        realm.EventPrototype = evProto;

        DefineAccessor(realm, evProto, "type", (thisV, _) => HostEventOr(thisV, e => JsValue.String(e.Type), JsValue.String("")));
        DefineAccessor(realm, evProto, "target", (thisV, _) => HostEventOr(thisV, e =>
            e.Target is null ? JsValue.Null : JsValue.Object(DomWrappers.Wrap(realm, e.Target)), JsValue.Null));
        DefineAccessor(realm, evProto, "currentTarget", (thisV, _) => HostEventOr(thisV, e =>
            e.CurrentTarget is null ? JsValue.Null : JsValue.Object(DomWrappers.Wrap(realm, e.CurrentTarget)), JsValue.Null));
        DefineAccessor(realm, evProto, "bubbles", (thisV, _) => HostEventOr(thisV, e => JsValue.Boolean(e.Bubbles), JsValue.False));
        DefineAccessor(realm, evProto, "cancelable", (thisV, _) => HostEventOr(thisV, e => JsValue.Boolean(e.Cancelable), JsValue.False));
        DefineAccessor(realm, evProto, "composed", (thisV, _) => HostEventOr(thisV, e => JsValue.Boolean(e.Composed), JsValue.False));
        DefineAccessor(realm, evProto, "defaultPrevented", (thisV, _) => HostEventOr(thisV, e => JsValue.Boolean(e.DefaultPrevented), JsValue.False));
        DefineAccessor(realm, evProto, "eventPhase", (thisV, _) => HostEventOr(thisV, e => JsValue.Number((int)e.EventPhase), JsValue.Number(0)));
        DefineAccessor(realm, evProto, "timeStamp", (thisV, _) => HostEventOr(thisV, e => JsValue.Number(e.TimeStamp), JsValue.Number(0)));
        DefineAccessor(realm, evProto, "isTrusted", (thisV, _) => HostEventOr(thisV, e => JsValue.Boolean(e.IsTrusted), JsValue.False));
        // PopStateEvent.state surfaces on the shared Event prototype: any
        // non-popstate event returns undefined here, which matches the
        // observable behavior of real browsers where only PopStateEvent has
        // the slot.
        DefineAccessor(realm, evProto, "state",
            (thisV, _) => TryGetHostEvent(thisV, out var e) && e is PopStateEvent p && p.State is JsValue jv
                ? jv
                : JsValue.Undefined);

        DefineMethod(realm, evProto, "preventDefault", (thisV, _) =>
        {
            if (TryGetHostEvent(thisV, out var e)) e.PreventDefault();
            return JsValue.Undefined;
        }, length: 0);
        DefineMethod(realm, evProto, "stopPropagation", (thisV, _) =>
        {
            if (TryGetHostEvent(thisV, out var e)) e.StopPropagation();
            return JsValue.Undefined;
        }, length: 0);
        DefineMethod(realm, evProto, "stopImmediatePropagation", (thisV, _) =>
        {
            if (TryGetHostEvent(thisV, out var e)) e.StopImmediatePropagation();
            return JsValue.Undefined;
        }, length: 0);

        var evCtor = new JsNativeFunction(realm, "Event", 1, (thisV, args) =>
        {
            if (args.Length == 0 || args[0].IsUndefined)
                throw new JsThrow(realm.NewTypeError("Event constructor requires a type"));
            var type = JsValue.ToStringValue(args[0]);
            var init = default(EventInit);
            if (args.Length > 1 && args[1].IsObject)
            {
                var initObj = args[1].AsObject;
                init = new EventInit(
                    Bubbles: JsValue.ToBoolean(initObj.Get("bubbles")),
                    Cancelable: JsValue.ToBoolean(initObj.Get("cancelable")),
                    Composed: JsValue.ToBoolean(initObj.Get("composed")));
            }
            var host = new Event(type, init);
            return JsValue.Object(new JsEventWrapper(evProto, host));
        }, isConstructor: true);
        evCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(evProto), writable: false, enumerable: false, configurable: false));
        evProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(evCtor), writable: true, enumerable: false, configurable: true));
        realm.EventConstructor = evCtor;
        realm.GlobalObject.DefineOwnProperty("Event",
            PropertyDescriptor.Data(JsValue.Object(evCtor), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Associate an existing host <see cref="EventTarget"/> with a JS
    /// wrapper object so JS-side listener registrations route to the same
    /// host instance. Called by <see cref="DomWrappers"/> when a Node is
    /// wrapped for the first time, and by Window install.</summary>
    public static void BindWrapper(JsObject wrapper, EventTarget host)
    {
        if (WrapperToTarget.TryGetValue(wrapper, out _)) return;
        WrapperToTarget.Add(wrapper, host);
    }

    /// <summary>Resolve the JS wrapper's underlying host EventTarget, or null.</summary>
    internal static EventTarget? ResolveHost(JsValue thisV) =>
        thisV.IsObject && WrapperToTarget.TryGetValue(thisV.AsObject, out var t) ? t : null;

    // ---- addEventListener / removeEventListener / dispatchEvent

    private static JsValue AddListener(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var host = ResolveHost(thisV);
        if (host is null || args.Length < 2 || !args[1].IsObject) return JsValue.Undefined;
        if (!AbstractOperations.IsCallable(args[1])) return JsValue.Undefined;
        var type = JsValue.ToStringValue(args[0]);
        var (capture, once, passive) = ParseListenerOptions(args.Length > 2 ? args[2] : JsValue.Undefined);
        var listenerObj = args[1].AsObject;

        var registry = Registries.GetValue(host, _ => new ListenerRegistry());
        var key = (type, capture);
        var byListener = registry.GetOrCreate(key);
        if (byListener.ContainsKey(listenerObj)) return JsValue.Undefined;

        // Create a delegate that re-enters the JS engine on dispatch. When
        // `once` is set, the host auto-removes its own entry; we clean up the
        // JS-side identity map inside the wrapper so a subsequent
        // addEventListener with the same listener re-registers cleanly.
        EventListener wrapper = null!;
        wrapper = ev =>
        {
            if (once) byListener.Remove(listenerObj);
            InvokeJsListener(realm, listenerObj, ev);
        };
        byListener[listenerObj] = wrapper;
        host.AddEventListener(type, wrapper, new AddEventListenerOptions(capture, once, passive));
        return JsValue.Undefined;
    }

    private static JsValue RemoveListener(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var host = ResolveHost(thisV);
        if (host is null || args.Length < 2 || !args[1].IsObject) return JsValue.Undefined;
        var type = JsValue.ToStringValue(args[0]);
        var (capture, _, _) = ParseListenerOptions(args.Length > 2 ? args[2] : JsValue.Undefined);
        var listenerObj = args[1].AsObject;

        if (!Registries.TryGetValue(host, out var registry)) return JsValue.Undefined;
        var key = (type, capture);
        var byListener = registry.TryGet(key);
        if (byListener is null) return JsValue.Undefined;
        if (!byListener.TryGetValue(listenerObj, out var wrapper)) return JsValue.Undefined;
        byListener.Remove(listenerObj);
        host.RemoveEventListener(type, wrapper, new RemoveEventListenerOptions(capture));
        return JsValue.Undefined;
    }

    private static JsValue DispatchEvent(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var host = ResolveHost(thisV);
        if (host is null) return JsValue.False;
        if (args.Length == 0 || !args[0].IsObject || args[0].AsObject is not JsEventWrapper wrapper)
            throw new JsThrow(realm.NewTypeError("dispatchEvent requires an Event instance"));
        return JsValue.Boolean(host.DispatchEvent(wrapper.HostEvent));
    }

    // ---- Helpers

    /// <summary>Pump a JS listener call. Synthesizes a VM if none is active so
    /// host-driven dispatches still work.</summary>
    private static void InvokeJsListener(JsRealm realm, JsObject listener, Event ev)
    {
        try
        {
            var vm = realm.ActiveVm ?? new JsVm(GetRuntimeForRealm(realm));
            JsValue thisVal = ev.CurrentTarget is null
                ? JsValue.Undefined
                : JsValue.Object(DomWrappers.Wrap(realm, ev.CurrentTarget));
            var jsEvent = JsValue.Object(WrapHostEvent(realm, ev));
            AbstractOperations.Call(vm, JsValue.Object(listener), thisVal, new[] { jsEvent });
        }
        catch (JsThrow jt)
        {
            realm.ConsoleSink(ConsoleLevel.Error, $"Uncaught (in event listener) {JsValue.ToStringValue(jt.Value)}");
        }
        catch (Exception ex)
        {
            realm.ConsoleSink(ConsoleLevel.Error, $"Uncaught (in event listener) {ex.Message}");
        }
    }

    /// <summary>Resolve the runtime that owns the given realm. <see cref="JsRuntime"/>
    /// does not expose itself on the realm directly, so we stash it on install.</summary>
    private static JsRuntime GetRuntimeForRealm(JsRealm realm)
        => WindowBinding.RuntimeForRealm(realm) ?? throw new InvalidOperationException(
            "Cannot synthesize a VM: realm has no associated JsRuntime. Was WindowBinding.Install ever called?");

    private static JsEventWrapper WrapHostEvent(JsRealm realm, Event ev)
        => new(realm.EventPrototype ?? realm.ObjectPrototype, ev);

    internal static bool TryGetHostEvent(JsValue v, out Event ev)
    {
        if (v.IsObject && v.AsObject is JsEventWrapper w) { ev = w.HostEvent; return true; }
        ev = null!;
        return false;
    }

    private static JsValue HostEventOr(JsValue thisV, Func<Event, JsValue> read, JsValue fallback)
        => TryGetHostEvent(thisV, out var e) ? read(e) : fallback;

    private static (bool capture, bool once, bool passive) ParseListenerOptions(JsValue opt)
    {
        if (opt.IsUndefined || opt.IsNull) return (false, false, false);
        if (opt.IsBoolean) return (opt.AsBool, false, false);
        if (opt.IsObject)
        {
            var o = opt.AsObject;
            return (
                JsValue.ToBoolean(o.Get("capture")),
                JsValue.ToBoolean(o.Get("once")),
                JsValue.ToBoolean(o.Get("passive")));
        }
        return (false, false, false);
    }

    internal static void DefineMethod(JsRealm realm, JsObject target, string name,
        Func<JsValue, JsValue[], JsValue> body, int length)
    {
        var fn = new JsNativeFunction(realm, name, length, body, isConstructor: false);
        target.DefineOwnProperty(name, PropertyDescriptor.BuiltinMethod(JsValue.Object(fn)));
    }

    internal static void DefineAccessor(JsRealm realm, JsObject target, string name,
        Func<JsValue, JsValue[], JsValue> getter,
        Func<JsValue, JsValue[], JsValue>? setter = null)
    {
        var get = new JsNativeFunction(realm, $"get {name}", 0, getter, isConstructor: false);
        JsObject? set = null;
        if (setter is not null)
            set = new JsNativeFunction(realm, $"set {name}", 1, setter, isConstructor: false);
        target.DefineOwnProperty(name,
            PropertyDescriptor.Accessor(get, set, enumerable: false, configurable: true));
    }
}

/// <summary>JS wrapper for a host <see cref="Event"/>. Identity is the spec
/// [[HostEvent]] slot — accessor methods read directly off it.</summary>
internal sealed class JsEventWrapper : JsObject
{
    public Event HostEvent { get; }
    public JsEventWrapper(JsObject proto, Event hostEvent) : base(proto) { HostEvent = hostEvent; }
}

/// <summary>Per-target listener bookkeeping. Listeners are mapped from the
/// JS listener object to its host-side delegate so removeEventListener can
/// find the same wrapper to detach.</summary>
internal sealed class ListenerRegistry
{
    private readonly Dictionary<(string Type, bool Capture), Dictionary<JsObject, EventListener>> _byKey = new();

    public Dictionary<JsObject, EventListener> GetOrCreate((string Type, bool Capture) key)
    {
        if (!_byKey.TryGetValue(key, out var map))
        {
            map = new Dictionary<JsObject, EventListener>(ReferenceEqualityComparer.Instance);
            _byKey[key] = map;
        }
        return map;
    }

    public Dictionary<JsObject, EventListener>? TryGet((string Type, bool Capture) key)
        => _byKey.TryGetValue(key, out var map) ? map : null;
}

/// <summary>A bare host <see cref="EventTarget"/> used when JS constructs
/// <c>new EventTarget()</c> directly (i.e. not attached to a DOM node).</summary>
internal sealed class InMemoryEventTarget : EventTarget { }
