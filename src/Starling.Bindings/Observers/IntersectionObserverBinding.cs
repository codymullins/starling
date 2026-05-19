using System.Runtime.CompilerServices;
using Tessera.Dom;
using Tessera.Js.Runtime;

namespace Tessera.Bindings.Observers;

/*
 * TODO (B5-4 partial impl): real intersection firing.
 *
 * IntersectionObserver fires entries at the "run the update intersection
 * observations steps" render step (IO spec §3.2.2). That requires:
 *   - A layout result that the bindings can read (target bounding box,
 *     root bounding box, viewport).
 *   - A render-step hook in the event loop where the bindings can iterate
 *     each registered (observer, target) and compute the intersection ratio
 *     against the configured thresholds.
 *
 * Neither hook is in place for B5-4. This binding therefore only exposes
 * the constructable JS surface; no IntersectionObserverEntry will ever be
 * delivered. Once layout + render-step hooks land (see B6-* and
 * browser-plan/07_LAYOUT.md), wire a per-frame pass that calls
 * ObserverRecords.BuildIntersectionEntry and queues a delivery microtask
 * through runtime.WithActiveVm.
 */

/// <summary>
/// B5-4 — installs the JS-visible <c>IntersectionObserver</c> constructor and
/// prototype. <b>Partial implementation:</b> JS surface only — entries are
/// never produced (see file-level TODO).
/// </summary>
public static class IntersectionObserverBinding
{
    internal static readonly ConditionalWeakTable<JsObject, IntersectionObserverState> States = new();

    public static void Install(JsRuntime runtime, Document document)
    {
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(document);
        var realm = runtime.Realm;
        if (realm.IntersectionObserverConstructor is not null) return;

        var proto = new JsObject(realm.ObjectPrototype);
        realm.IntersectionObserverPrototype = proto;
        realm.IntersectionObserverEntryPrototype = new JsObject(realm.ObjectPrototype);

        EventTargetBinding.DefineAccessor(realm, proto, "root", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            return s?.Root is { } r ? JsValue.Object(DomWrappers.Wrap(realm, r)) : JsValue.Null;
        });
        EventTargetBinding.DefineAccessor(realm, proto, "rootMargin",
            (thisV, _) => JsValue.String(ResolveState(thisV)?.RootMargin ?? "0px 0px 0px 0px"));
        EventTargetBinding.DefineAccessor(realm, proto, "thresholds", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            var items = new List<JsValue>();
            if (s is not null) foreach (var t in s.Thresholds) items.Add(JsValue.Number(t));
            return JsValue.Object(new JsArray(realm, items));
        });

        EventTargetBinding.DefineMethod(realm, proto, "observe", (thisV, args) =>
        {
            var state = ResolveState(thisV)
                ?? throw new JsThrow(realm.NewTypeError("Illegal invocation: observe called on non-IntersectionObserver"));
            if (args.Length == 0 || DomWrappers.UnwrapElement(args[0]) is not { } el)
                throw new JsThrow(realm.NewTypeError("IntersectionObserver.observe: target must be an Element"));
            state.AddTarget(el);
            return JsValue.Undefined;
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, proto, "unobserve", (thisV, args) =>
        {
            var state = ResolveState(thisV);
            if (state is null || args.Length == 0) return JsValue.Undefined;
            if (DomWrappers.UnwrapElement(args[0]) is { } el) state.RemoveTarget(el);
            return JsValue.Undefined;
        }, length: 1);

        EventTargetBinding.DefineMethod(realm, proto, "disconnect", (thisV, _) =>
        {
            ResolveState(thisV)?.Disconnect();
            return JsValue.Undefined;
        }, length: 0);

        EventTargetBinding.DefineMethod(realm, proto, "takeRecords", (thisV, _) =>
        {
            var s = ResolveState(thisV);
            return s is null ? JsValue.Object(new JsArray(realm)) : JsValue.Object(s.DrainRecords(realm));
        }, length: 0);

        var ctor = new JsNativeFunction(realm, "IntersectionObserver", 1, (thisV, args) =>
        {
            if (args.Length == 0 || !AbstractOperations.IsCallable(args[0]))
                throw new JsThrow(realm.NewTypeError("IntersectionObserver requires a callback function"));
            var (root, rootMargin, thresholds) = ParseOptions(realm, args.Length > 1 ? args[1] : JsValue.Undefined);
            var inst = new JsObject(proto);
            States.Add(inst, new IntersectionObserverState(runtime, inst, args[0], root, rootMargin, thresholds));
            return JsValue.Object(inst);
        }, isConstructor: true);

        ctor.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(proto), writable: false, enumerable: false, configurable: false));
        proto.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));

        realm.IntersectionObserverConstructor = ctor;
        realm.GlobalObject.DefineOwnProperty("IntersectionObserver",
            PropertyDescriptor.Data(JsValue.Object(ctor), writable: true, enumerable: false, configurable: true));
    }

    private static IntersectionObserverState? ResolveState(JsValue thisV)
        => thisV.IsObject && States.TryGetValue(thisV.AsObject, out var s) ? s : null;

    private static (Element? root, string rootMargin, IReadOnlyList<double> thresholds) ParseOptions(JsRealm realm, JsValue raw)
    {
        Element? root = null;
        var rootMargin = "0px 0px 0px 0px";
        IReadOnlyList<double> thresholds = new[] { 0.0 };

        if (raw.IsUndefined || raw.IsNull) return (root, rootMargin, thresholds);
        if (!raw.IsObject)
            throw new JsThrow(realm.NewTypeError("IntersectionObserver: options must be an object"));
        var o = raw.AsObject;

        var rootV = o.Get("root");
        if (!rootV.IsUndefined && !rootV.IsNull)
        {
            if (DomWrappers.UnwrapElement(rootV) is { } el) root = el;
            // Document roots are spec-allowed too; we silently ignore non-Element roots for now.
        }

        var rm = o.Get("rootMargin");
        if (!rm.IsUndefined && !rm.IsNull)
            rootMargin = JsValue.ToStringValue(rm);

        var th = o.Get("threshold");
        if (!th.IsUndefined && !th.IsNull)
        {
            if (th.IsObject && th.AsObject is JsArray arr)
            {
                var list = new List<double>(arr.Length);
                for (var i = 0; i < arr.Length; i++)
                {
                    var n = JsValue.ToNumber(arr[i]);
                    if (double.IsNaN(n) || n < 0 || n > 1)
                        throw new JsThrow(realm.NewRangeError("IntersectionObserver: threshold values must be in [0, 1]"));
                    list.Add(n);
                }
                thresholds = list;
            }
            else
            {
                var n = JsValue.ToNumber(th);
                if (double.IsNaN(n) || n < 0 || n > 1)
                    throw new JsThrow(realm.NewRangeError("IntersectionObserver: threshold values must be in [0, 1]"));
                thresholds = new[] { n };
            }
        }

        return (root, rootMargin, thresholds);
    }
}

internal sealed class IntersectionObserverState
{
    private readonly JsRuntime _runtime;
    private readonly JsObject _observerWrapper;
    private readonly JsValue _callback;
    private readonly List<Element> _targets = new();
    private readonly List<JsObject> _pending = new();

    public Element? Root { get; }
    public string RootMargin { get; }
    public IReadOnlyList<double> Thresholds { get; }

    public IntersectionObserverState(JsRuntime runtime, JsObject observerWrapper, JsValue callback,
        Element? root, string rootMargin, IReadOnlyList<double> thresholds)
    {
        _runtime = runtime;
        _observerWrapper = observerWrapper;
        _callback = callback;
        Root = root;
        RootMargin = rootMargin;
        Thresholds = thresholds;
    }

    public void AddTarget(Element target)
    {
        foreach (var t in _targets) if (ReferenceEquals(t, target)) return;
        _targets.Add(target);
    }

    public void RemoveTarget(Element target)
    {
        for (var i = 0; i < _targets.Count; i++)
            if (ReferenceEquals(_targets[i], target)) { _targets.RemoveAt(i); return; }
    }

    public void Disconnect()
    {
        _targets.Clear();
        _pending.Clear();
    }

    public JsArray DrainRecords(JsRealm realm)
    {
        if (_pending.Count == 0) return new JsArray(realm);
        var items = new List<JsValue>(_pending.Count);
        foreach (var r in _pending) items.Add(JsValue.Object(r));
        _pending.Clear();
        return new JsArray(realm, items);
    }
}
