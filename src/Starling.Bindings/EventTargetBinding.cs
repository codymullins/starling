using System.Runtime.CompilerServices;
using Starling.Dom.Events;
using Starling.Js.Runtime;

namespace Starling.Bindings;

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
        // DOM §2.2 — cancelBubble: getter returns stopPropagation state;
        // setter of true calls stopPropagation (false is a no-op per spec).
        DefineAccessor(realm, evProto, "cancelBubble",
            (thisV, _) => TryGetHostEvent(thisV, out var e) ? JsValue.Boolean(e.PropagationStopped) : JsValue.False,
            (thisV, args) =>
            {
                if (args.Length > 0 && JsValue.ToBoolean(args[0]))
                    if (TryGetHostEvent(thisV, out var e)) e.StopPropagation();
                return JsValue.Undefined;
            });
        // DOM §2.2 — returnValue: legacy alias for !defaultPrevented (setter = preventDefault if false).
        DefineAccessor(realm, evProto, "returnValue",
            (thisV, _) => TryGetHostEvent(thisV, out var e) ? JsValue.Boolean(!e.DefaultPrevented) : JsValue.True,
            (thisV, args) =>
            {
                if (args.Length > 0 && !JsValue.ToBoolean(args[0]))
                    if (TryGetHostEvent(thisV, out var e)) e.PreventDefault();
                return JsValue.Undefined;
            });
        // Legacy Event.initEvent (DOM §2.9) — used with document.createEvent.
        DefineMethod(realm, evProto, "initEvent", (thisV, args) =>
        {
            if (TryGetHostEvent(thisV, out var e))
                e.InitEvent(
                    args.Length > 0 ? JsValue.ToStringValue(args[0]) : "",
                    args.Length > 1 && JsValue.ToBoolean(args[1]),
                    args.Length > 2 && JsValue.ToBoolean(args[2]));
            return JsValue.Undefined;
        }, length: 3);

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
        // DOM §2.2 — EventPhase constants on both the constructor and prototype.
        foreach (var (name, val) in new[] { ("NONE", 0), ("CAPTURING_PHASE", 1), ("AT_TARGET", 2), ("BUBBLING_PHASE", 3) })
        {
            var desc = PropertyDescriptor.Data(JsValue.Number(val), writable: false, enumerable: true, configurable: false);
            evCtor.DefineOwnProperty(name, desc);
            evProto.DefineOwnProperty(name, desc);
        }
        realm.EventConstructor = evCtor;
        realm.GlobalObject.DefineOwnProperty("Event",
            PropertyDescriptor.Data(JsValue.Object(evCtor), writable: true, enumerable: false, configurable: true));

        // ----- CustomEvent (inherits from Event)
        // DOM §2.2 CustomEvent — adds the `detail` property to Event. The host
        // CustomEvent class stores detail as object? but JS consumers expect it
        // as a JsValue. We stash the detail on the JsEventWrapper itself.
        var customEvProto = new JsObject(evProto);
        realm.CustomEventPrototype = customEvProto;
        DefineAccessor(realm, customEvProto, "detail", (thisV, _) =>
        {
            if (thisV.IsObject && thisV.AsObject is JsCustomEventWrapper cw) return cw.Detail;
            return JsValue.Null;
        });
        // Legacy CustomEvent.initCustomEvent (DOM §2.3) — used with createEvent.
        DefineMethod(realm, customEvProto, "initCustomEvent", (thisV, args) =>
        {
            if (TryGetHostEvent(thisV, out var e))
            {
                e.InitEvent(
                    args.Length > 0 ? JsValue.ToStringValue(args[0]) : "",
                    args.Length > 1 && JsValue.ToBoolean(args[1]),
                    args.Length > 2 && JsValue.ToBoolean(args[2]));
                if (thisV.AsObject is JsCustomEventWrapper cw)
                    cw.Detail = args.Length > 3 ? args[3] : JsValue.Null;
            }
            return JsValue.Undefined;
        }, length: 4);

        var customEvCtor = new JsNativeFunction(realm, "CustomEvent", 1, (_, args) =>
        {
            if (args.Length == 0 || args[0].IsUndefined)
                throw new JsThrow(realm.NewTypeError("CustomEvent constructor requires a type"));
            var type = JsValue.ToStringValue(args[0]);
            var init = default(EventInit);
            var detail = JsValue.Null;
            if (args.Length > 1 && args[1].IsObject)
            {
                var initObj = args[1].AsObject;
                init = new EventInit(
                    Bubbles: JsValue.ToBoolean(initObj.Get("bubbles")),
                    Cancelable: JsValue.ToBoolean(initObj.Get("cancelable")),
                    Composed: JsValue.ToBoolean(initObj.Get("composed")));
                var d = initObj.Get("detail");
                if (!d.IsUndefined) detail = d;
            }
            var host = new CustomEvent(type, init);
            CacheCustomEventDetail(host, detail);
            return JsValue.Object(new JsCustomEventWrapper(customEvProto, host, detail));
        }, isConstructor: true);
        customEvCtor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(customEvProto), writable: false, enumerable: false, configurable: false));
        customEvProto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(customEvCtor), writable: true, enumerable: false, configurable: true));
        realm.GlobalObject.DefineOwnProperty("CustomEvent",
            PropertyDescriptor.Data(JsValue.Object(customEvCtor), writable: true, enumerable: false, configurable: true));

        // ----- UIEvent / MouseEvent / KeyboardEvent / FocusEvent (UIEvents).
        // Prototypes chain through Event.prototype; constructors read the init
        // dictionary into the host subtype. Stored on the realm so createEvent()
        // can return the correct prototype too.
        var uiEvProto = new JsObject(evProto);
        realm.UiEventPrototype = uiEvProto;
        DefineAccessor(realm, uiEvProto, "detail", (t, _) => TryGetHostEvent(t, out var e) && e is UiEvent u ? JsValue.Number(u.Detail) : JsValue.Number(0));
        DefineAccessor(realm, uiEvProto, "view", (_, _) => JsValue.Null);
        DefineSubtypeCtor(realm, uiEvProto, "UIEvent", (type, init) =>
            new UiEvent(type, ReadInit(init)) { Detail = (int)NumOf(init, "detail") });

        var mouseEvProto = new JsObject(uiEvProto);
        realm.MouseEventPrototype = mouseEvProto;
        DefineAccessor(realm, mouseEvProto, "clientX", (t, _) => MouseNum(t, m => m.ClientX));
        DefineAccessor(realm, mouseEvProto, "clientY", (t, _) => MouseNum(t, m => m.ClientY));
        DefineAccessor(realm, mouseEvProto, "x", (t, _) => MouseNum(t, m => m.ClientX));
        DefineAccessor(realm, mouseEvProto, "y", (t, _) => MouseNum(t, m => m.ClientY));
        DefineAccessor(realm, mouseEvProto, "screenX", (t, _) => MouseNum(t, m => m.ScreenX));
        DefineAccessor(realm, mouseEvProto, "screenY", (t, _) => MouseNum(t, m => m.ScreenY));
        DefineAccessor(realm, mouseEvProto, "pageX", (t, _) => MouseNum(t, m => m.ClientX));
        DefineAccessor(realm, mouseEvProto, "pageY", (t, _) => MouseNum(t, m => m.ClientY));
        DefineAccessor(realm, mouseEvProto, "button", (t, _) => MouseNum(t, m => m.Button));
        DefineAccessor(realm, mouseEvProto, "buttons", (t, _) => JsValue.Number(0));
        DefineAccessor(realm, mouseEvProto, "ctrlKey", (t, _) => MouseBool(t, m => m.CtrlKey));
        DefineAccessor(realm, mouseEvProto, "shiftKey", (t, _) => MouseBool(t, m => m.ShiftKey));
        DefineAccessor(realm, mouseEvProto, "altKey", (t, _) => MouseBool(t, m => m.AltKey));
        DefineAccessor(realm, mouseEvProto, "metaKey", (t, _) => MouseBool(t, m => m.MetaKey));
        DefineAccessor(realm, mouseEvProto, "relatedTarget", (t, _) =>
            TryGetHostEvent(t, out var e) && e is MouseEvent m && m.RelatedTarget is { } rt ? JsValue.Object(DomWrappers.Wrap(realm, rt)) : JsValue.Null);
        DefineSubtypeCtor(realm, mouseEvProto, "MouseEvent", (type, init) => new MouseEvent(type, ReadInit(init))
        {
            ClientX = NumOf(init, "clientX"), ClientY = NumOf(init, "clientY"),
            ScreenX = NumOf(init, "screenX"), ScreenY = NumOf(init, "screenY"),
            Button = (short)NumOf(init, "button"), Detail = (int)NumOf(init, "detail"),
            CtrlKey = BoolOf(init, "ctrlKey"), ShiftKey = BoolOf(init, "shiftKey"),
            AltKey = BoolOf(init, "altKey"), MetaKey = BoolOf(init, "metaKey"),
            RelatedTarget = init.IsObject ? ResolveHost(init.AsObject.Get("relatedTarget")) : null,
        });

        var kbdEvProto = new JsObject(uiEvProto);
        realm.KeyboardEventPrototype = kbdEvProto;
        DefineAccessor(realm, kbdEvProto, "key", (t, _) => KbdStr(t, k => k.Key));
        DefineAccessor(realm, kbdEvProto, "code", (t, _) => KbdStr(t, k => k.Code));
        DefineAccessor(realm, kbdEvProto, "repeat", (t, _) => TryGetHostEvent(t, out var e) && e is KeyboardEvent k ? JsValue.Boolean(k.Repeat) : JsValue.False);
        DefineAccessor(realm, kbdEvProto, "location", (_, _) => JsValue.Number(0));
        DefineAccessor(realm, kbdEvProto, "ctrlKey", (t, _) => KbdBool(t, k => k.CtrlKey));
        DefineAccessor(realm, kbdEvProto, "shiftKey", (t, _) => KbdBool(t, k => k.ShiftKey));
        DefineAccessor(realm, kbdEvProto, "altKey", (t, _) => KbdBool(t, k => k.AltKey));
        DefineAccessor(realm, kbdEvProto, "metaKey", (t, _) => KbdBool(t, k => k.MetaKey));
        DefineSubtypeCtor(realm, kbdEvProto, "KeyboardEvent", (type, init) => new KeyboardEvent(type, ReadInit(init))
        {
            Key = StrOf(init, "key"), Code = StrOf(init, "code"), Repeat = BoolOf(init, "repeat"),
            CtrlKey = BoolOf(init, "ctrlKey"), ShiftKey = BoolOf(init, "shiftKey"),
            AltKey = BoolOf(init, "altKey"), MetaKey = BoolOf(init, "metaKey"),
            Detail = (int)NumOf(init, "detail"),
        });

        var focusEvProto = new JsObject(uiEvProto);
        realm.FocusEventPrototype = focusEvProto;
        DefineAccessor(realm, focusEvProto, "relatedTarget", (t, _) =>
            TryGetHostEvent(t, out var e) && e is FocusEvent f && f.RelatedTarget is { } rt ? JsValue.Object(DomWrappers.Wrap(realm, rt)) : JsValue.Null);
        DefineSubtypeCtor(realm, focusEvProto, "FocusEvent", (type, init) => new FocusEvent(type, ReadInit(init))
        {
            Detail = (int)NumOf(init, "detail"),
            RelatedTarget = init.IsObject ? ResolveHost(init.AsObject.Get("relatedTarget")) : null,
        });

        // WheelEvent / InputEvent / CompositionEvent — minimal stubs that inherit
        // from UIEvent. No extra properties for now; these just need to be
        // constructable so `new WheelEvent('wheel')` doesn't throw.
        var wheelEvProto = new JsObject(mouseEvProto);
        DefineAccessor(realm, wheelEvProto, "deltaX", (_, _) => JsValue.Number(0));
        DefineAccessor(realm, wheelEvProto, "deltaY", (_, _) => JsValue.Number(0));
        DefineAccessor(realm, wheelEvProto, "deltaZ", (_, _) => JsValue.Number(0));
        DefineAccessor(realm, wheelEvProto, "deltaMode", (_, _) => JsValue.Number(0));
        DefineSubtypeCtor(realm, wheelEvProto, "WheelEvent", (type, init) => new MouseEvent(type, ReadInit(init)));

        var inputEvProto = new JsObject(uiEvProto);
        DefineSubtypeCtor(realm, inputEvProto, "InputEvent", (type, init) => new UiEvent(type, ReadInit(init)));

        var compositionEvProto = new JsObject(uiEvProto);
        DefineAccessor(realm, compositionEvProto, "data", (_, _) => JsValue.String(""));
        DefineSubtypeCtor(realm, compositionEvProto, "CompositionEvent", (type, init) => new UiEvent(type, ReadInit(init)));

        // HashChangeEvent / PopStateEvent / StorageEvent / MessageEvent — stubs
        // for tests that reference these constructors.
        foreach (var (name, proto) in new[]
        {
            ("HashChangeEvent", new JsObject(evProto)),
            ("MessageEvent", new JsObject(evProto)),
            ("StorageEvent", new JsObject(evProto)),
            ("BeforeUnloadEvent", new JsObject(evProto)),
            ("TextEvent", new JsObject(uiEvProto)),
            ("DragEvent", new JsObject(mouseEvProto)),
        })
        {
            var captured = proto;
            var capturedName = name;
            DefineSubtypeCtor(realm, captured, capturedName,
                (type, init) => new Event(type, ReadInit(init)));
        }

        // NodeList, HTMLCollection, DOMTokenList — expose interface constructor
        // objects on window so `instanceof NodeList` etc. can be checked.
        // These don't need real constructors since no WPT test creates instances
        // directly; they just need to exist as non-null objects.
        foreach (var ifaceName in new[] { "NodeList", "HTMLCollection", "DOMTokenList" })
        {
            var ifaceProto = new JsObject(realm.ObjectPrototype);
            var ifaceCtor = new JsNativeFunction(realm, ifaceName, 0, (_, _) =>
                throw new JsThrow(realm.NewTypeError("Illegal constructor")), isConstructor: false);
            ifaceCtor.DefineOwnProperty("prototype",
                PropertyDescriptor.Data(JsValue.Object(ifaceProto), writable: false, enumerable: false, configurable: false));
            ifaceProto.DefineOwnProperty("constructor",
                PropertyDescriptor.Data(JsValue.Object(ifaceCtor), writable: true, enumerable: false, configurable: true));
            realm.GlobalObject.DefineOwnProperty(ifaceName,
                PropertyDescriptor.Data(JsValue.Object(ifaceCtor), writable: true, enumerable: false, configurable: true));
        }
    }

    // ----- helpers for the UIEvents subtype constructors -----
    private static EventInit ReadInit(JsValue v) => v.IsObject
        ? new EventInit(JsValue.ToBoolean(v.AsObject.Get("bubbles")),
                        JsValue.ToBoolean(v.AsObject.Get("cancelable")),
                        JsValue.ToBoolean(v.AsObject.Get("composed")))
        : default;
    private static double NumOf(JsValue v, string k) => v.IsObject && !v.AsObject.Get(k).IsUndefined ? JsValue.ToNumber(v.AsObject.Get(k)) : 0;
    private static bool BoolOf(JsValue v, string k) => v.IsObject && JsValue.ToBoolean(v.AsObject.Get(k));
    private static string StrOf(JsValue v, string k) => v.IsObject && v.AsObject.Get(k).IsString ? v.AsObject.Get(k).AsString : "";
    private static JsValue MouseNum(JsValue t, Func<MouseEvent, double> f) => TryGetHostEvent(t, out var e) && e is MouseEvent m ? JsValue.Number(f(m)) : JsValue.Number(0);
    private static JsValue MouseBool(JsValue t, Func<MouseEvent, bool> f) => TryGetHostEvent(t, out var e) && e is MouseEvent m ? JsValue.Boolean(f(m)) : JsValue.False;
    private static JsValue KbdStr(JsValue t, Func<KeyboardEvent, string> f) => TryGetHostEvent(t, out var e) && e is KeyboardEvent k ? JsValue.String(f(k)) : JsValue.String("");
    private static JsValue KbdBool(JsValue t, Func<KeyboardEvent, bool> f) => TryGetHostEvent(t, out var e) && e is KeyboardEvent k ? JsValue.Boolean(f(k)) : JsValue.False;

    private static void DefineSubtypeCtor(JsRealm realm, JsObject proto, string name, Func<string, JsValue, Event> build)
    {
        var ctor = new JsNativeFunction(realm, name, 1, (_, args) =>
        {
            if (args.Length == 0 || args[0].IsUndefined)
                throw new JsThrow(realm.NewTypeError($"{name} constructor requires a type"));
            return JsValue.Object(new JsEventWrapper(proto, build(JsValue.ToStringValue(args[0]), args.Length > 1 ? args[1] : JsValue.Undefined)));
        }, isConstructor: true);
        ctor.DefineOwnProperty("prototype", PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor", PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
        realm.GlobalObject.DefineOwnProperty(name, PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    /// <summary>Legacy <c>document.createEvent(interface)</c> (DOM §2.9). Returns
    /// an uninitialized event of the requested legacy interface; the caller must
    /// call <c>initEvent</c>/<c>initCustomEvent</c> before dispatch. Throws a
    /// TypeError for an unrecognized interface (DOMException NotSupportedError is
    /// not yet modeled). Subtype-specific accessors aren't surfaced here; the
    /// legacy path is dominated by Event/CustomEvent + init + dispatch.</summary>
    public static JsValue CreateLegacyEvent(JsRealm realm, string interfaceName)
    {
        ArgumentNullException.ThrowIfNull(realm);
        switch ((interfaceName ?? "").ToLowerInvariant())
        {
            case "customevent":
                return JsValue.Object(new JsCustomEventWrapper(realm.CustomEventPrototype!, new CustomEvent(""), JsValue.Null));
            case "event":
            case "events":
            case "htmlevents":
            case "svgevents":
                return WrapUninitEvent(realm, new Event(""));
            case "uievent":
            case "uievents":
                return WrapUninitEvent(realm, new UiEvent(""));
            case "mouseevent":
            case "mouseevents":
                return WrapUninitEvent(realm, new MouseEvent(""));
            case "keyboardevent":
            case "keyevents":
                return WrapUninitEvent(realm, new KeyboardEvent(""));
            case "focusevent":
                return WrapUninitEvent(realm, new FocusEvent(""));
            default:
                throw new JsThrow(realm.NewTypeError($"createEvent: unsupported interface '{interfaceName}'"));
        }
    }

    private static JsValue WrapUninitEvent(JsRealm realm, Event host)
    {
        // DOM §2.9: events created via createEvent() are uninitialized until
        // initEvent() is called. Mark them so dispatchEvent can throw
        // InvalidStateError if dispatch is attempted before initialization.
        host.MarkAsUninitialized();
        var proto = host switch
        {
            MouseEvent => realm.MouseEventPrototype,
            KeyboardEvent => realm.KeyboardEventPrototype,
            FocusEvent => realm.FocusEventPrototype,
            UiEvent => realm.UiEventPrototype,
            _ => realm.EventPrototype,
        } ?? realm.EventPrototype!;
        return JsValue.Object(new JsEventWrapper(proto, host));
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

    /// <summary>Dispatch a host-built event on the EventTarget backing a JS
    /// wrapper (e.g. HTMLElement.click()'s synthetic MouseEvent). Runs the same
    /// host listeners — including JS-bridged ones — as dispatchEvent.</summary>
    public static bool DispatchHostEvent(JsValue target, Event ev)
    {
        var host = ResolveHost(target);
        return host is not null && host.DispatchEvent(ev);
    }

    private static JsValue DispatchEvent(JsRealm realm, JsValue thisV, JsValue[] args)
    {
        var host = ResolveHost(thisV);
        if (host is null) return JsValue.False;
        if (args.Length == 0 || !args[0].IsObject || args[0].AsObject is not JsEventWrapper wrapper)
            throw new JsThrow(realm.NewTypeError("dispatchEvent requires an Event instance"));
        var ev = wrapper.HostEvent;
        // DOM §2.7 step 1: "If event's dispatch flag is set … throw InvalidStateError"
        if (ev.IsBeingDispatched)
            throw DomExceptionBinding.Throw(realm, "InvalidStateError",
                "The event is already being dispatched.");
        // DOM §2.7 step 2: "If event's initialized flag is not set … throw InvalidStateError"
        if (!ev.Initialized)
            throw DomExceptionBinding.Throw(realm, "InvalidStateError",
                "The event has not been initialized (call initEvent first).");
        try
        {
            return JsValue.Boolean(host.DispatchEvent(ev));
        }
        catch (InvalidOperationException ex)
        {
            throw DomExceptionBinding.Throw(realm, "InvalidStateError", ex.Message);
        }
    }

    // ---- DispatchEvent overload helper (accepts CustomEvent wrapper too)
    // JsCustomEventWrapper inherits JsEventWrapper so the cast above covers it.

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
    {
        // When the host event is a CustomEvent that originated from JS (and
        // was therefore a JsCustomEventWrapper), we need to preserve the JS
        // detail value. The listener wrapper for the JS-created custom event
        // IS itself the JsCustomEventWrapper stored in the runtime.  We walk
        // the realm's custom event registry to find it, but the simplest
        // approach is: wrap a fresh JsCustomEventWrapper when the host event
        // is a CustomEvent so the listener sees `e.detail`.
        if (ev is CustomEvent ce)
        {
            var ceProto = GetCustomEventProto(realm);
            // Try to locate the JS detail value that was stored when the event
            // was constructed. We stash it in CustomEventDetailCache.
            var detail = CustomEventDetailCache.TryGetValue(ce, out var box) ? box.Value : JsValue.Null;
            return new JsCustomEventWrapper(ceProto ?? realm.EventPrototype ?? realm.ObjectPrototype, ce, detail);
        }
        return new JsEventWrapper(realm.EventPrototype ?? realm.ObjectPrototype, ev);
    }

    // Cache: CustomEvent → JsValue detail. Populated when JS constructs new CustomEvent(...).
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<CustomEvent, JsValueBox>
        CustomEventDetailCache = new();

    internal static void CacheCustomEventDetail(CustomEvent ev, JsValue detail)
    {
        CustomEventDetailCache.GetValue(ev, _ => new JsValueBox(detail));
    }

    private static JsObject? GetCustomEventProto(JsRealm realm)
    {
        // The CustomEvent prototype is stored as the prototype of any
        // JsCustomEventWrapper in this realm — we retrieve it via the
        // global "CustomEvent" constructor's .prototype property.
        var customEvCtor = realm.GlobalObject.Get("CustomEvent");
        if (customEvCtor.IsObject)
        {
            var proto = customEvCtor.AsObject.Get("prototype");
            if (proto.IsObject) return proto.AsObject;
        }
        return null;
    }

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
internal class JsEventWrapper : JsObject
{
    public Event HostEvent { get; }
    public JsEventWrapper(JsObject proto, Event hostEvent) : base(proto) { HostEvent = hostEvent; }
}

/// <summary>Wrapper for <c>CustomEvent</c> that carries a JS-value
/// <c>detail</c> slot alongside the host event object.</summary>
internal sealed class JsCustomEventWrapper : JsEventWrapper
{
    // Settable so legacy initCustomEvent(type, …, detail) can (re)assign it.
    public JsValue Detail { get; set; }
    public JsCustomEventWrapper(JsObject proto, CustomEvent hostEvent, JsValue detail)
        : base(proto, hostEvent) { Detail = detail; }
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

/// <summary>Boxing wrapper so <see cref="JsValue"/> (a struct) can be stored
/// in a <see cref="System.Runtime.CompilerServices.ConditionalWeakTable{TKey,TValue}"/>
/// (which requires reference-type values).</summary>
internal sealed class JsValueBox
{
    public JsValue Value { get; }
    public JsValueBox(JsValue v) { Value = v; }
}
