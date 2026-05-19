using Tessera.Js.Intrinsics;

namespace Tessera.Js.Runtime;

/// <summary>
/// Host-facing entry point for the JS engine. Owns the active <see cref="JsRealm"/>
/// and exposes API for registering native functions + running scripts.
/// </summary>
/// <remarks>
/// In a real browser the runtime sits behind the Window binding (M4-02 / B5).
/// For now it's a standalone unit so the headless CLI can run JS scripts
/// against a configurable host surface.
/// </remarks>
public sealed class JsRuntime
{
    public JsRealm Realm { get; }
    public JsObject Global => Realm.GlobalObject;

    /// <summary>When true, top-level uncaught throws are swallowed and logged
    /// rather than propagated to the host. Path-A behavior so the engine can
    /// run inline scripts that reference missing globals without aborting the
    /// page render.</summary>
    public bool IgnoreUncaught { get; set; }

    /// <summary>Compatibility wrapper for <c>console.*</c> output. Prefer
    /// <see cref="JsRealm.ConsoleSink"/> for new host integrations.</summary>
    public Action<string, string> ConsoleSink
    {
        get => (level, message) => Realm.ConsoleSink(ParseLevel(level), message);
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Realm.ConsoleSink = (level, message) => value(LevelName(level), message);
        }
    }

    public JsRuntime()
    {
        Realm = new JsRealm();
        ObjectCtor.Install(Realm);
        FunctionCtor.Install(Realm); // B2-2 — single-line region so the B3-4 merge stays trivial.
        ArrayCtor.Install(Realm);    // B2-4
        ErrorCtor.Install(Realm);
        StringCtor.Install(Realm);
        NumberCtor.Install(Realm);
        BooleanCtor.Install(Realm);
        SymbolCtor.Install(Realm);
        IteratorIntrinsics.Install(Realm); // B3-2 — depends on SymbolCtor for @@iterator.
        MapCtor.Install(Realm);            // B3-3 — depends on B3-2 iterator protocol.
        SetCtor.Install(Realm);
        WeakMapCtor.Install(Realm);
        WeakSetCtor.Install(Realm);
        Globals.Install(Realm);
        MathObj.Install(Realm);
        JsonObj.Install(Realm);
        ArrayBufferCtor.Install(Realm);
        DataViewCtor.Install(Realm);
        TypedArrayCtors.Install(Realm);
        ConsoleObj.Install(Realm);
        PromiseCtor.Install(Realm); // B3-4 — depends on Object/Function/Error protos.
        RegExpCtor.Install(Realm);  // B4-1 — depends on Function/Error/Array protos.
        DateCtor.Install(Realm);    // B4-2 — depends on Function.prototype only.
        ProxyCtor.Install(Realm);   // B4-4 — depends on Function.prototype only.
        ReflectObj.Install(Realm);  // B4-4 — depends on Symbol (for @@toStringTag).
        Global.Set("globalThis", JsValue.Object(Global));
        Global.Set("undefined", JsValue.Undefined);
        Global.Set("NaN", JsValue.NaN);
        Global.Set("Infinity", JsValue.Number(double.PositiveInfinity));

        // Route uncaught microtask exceptions through the console sink so
        // they don't crash the embedder. The full unhandledrejection event
        // pipe lands with B5-1.
        Realm.Microtasks.UncaughtHandler = ex =>
            Realm.ConsoleSink(ConsoleLevel.Error, $"Uncaught (in promise) {ex.Message}");
    }

    /// <summary>Drain any queued microtasks against the realm. Called
    /// automatically at the bottom of every top-level
    /// <see cref="JsVm.Run(Tessera.Js.Bytecode.Chunk)"/>; embedders running
    /// scripts via a custom path can invoke this explicitly. No-op when a
    /// host scheduler is installed — the host pumps its own loop.</summary>
    public void DrainMicrotasks() => Realm.Microtasks.DrainAll();

    /// <summary>Lazily-allocated primary VM used by <see cref="WithActiveVm"/>
    /// when there's no script-driven VM on the stack. Held privately so it
    /// stays an implementation detail of the helper.</summary>
    private JsVm? _primaryVm;

    private JsVm GetOrCreatePrimaryVm() => _primaryVm ??= new JsVm(this);

    /// <summary>
    /// Run <paramref name="body"/> with <see cref="JsRealm.ActiveVm"/> set, then
    /// drain the microtask queue. Use when re-entering JS from host code
    /// (timers, fetch completions, event dispatch) where there's no enclosing
    /// <see cref="JsVm.Run(Tessera.Js.Bytecode.Chunk)"/> on the stack to
    /// publish the VM and drain reactions. Restores the previous
    /// <see cref="JsRealm.ActiveVm"/> on exit, even when the body throws.
    /// </summary>
    public void WithActiveVm(Action body)
    {
        ArgumentNullException.ThrowIfNull(body);
        var previous = Realm.ActiveVm;
        Realm.ActiveVm = previous ?? GetOrCreatePrimaryVm();
        try
        {
            body();
            DrainMicrotasks();
        }
        finally
        {
            Realm.ActiveVm = previous;
        }
    }

    /// <summary>Register a host-side function as a global named <paramref name="name"/>.
    /// Uses the realm-aware <see cref="JsNativeFunction"/> ctor so the function
    /// inherits from <c>Function.prototype</c> (call/apply/bind chain). The
    /// host doesn't tell us the declared arity, so <c>length</c> is set to the
    /// ECMAScript-default of <c>0</c>; callers needing a specific arity should
    /// either redefine the property or use the full (thisValue, args)
    /// overload paired with a manual <c>length</c> stamp.</summary>
    public void RegisterGlobal(string name, Func<JsValue[], JsValue> body)
        => Global.Set(name, JsValue.Object(new JsNativeFunction(Realm, name, length: 0, (_, a) => body(a), isConstructor: false)));

    /// <summary>Register a host-side function with full (thisValue, args) signature.
    /// As with the simpler overload, <c>length</c> defaults to <c>0</c>; the
    /// caller can override by editing the function's own <c>length</c>
    /// property after registration.</summary>
    public void RegisterGlobal(string name, Func<JsValue, JsValue[], JsValue> body)
        => Global.Set(name, JsValue.Object(new JsNativeFunction(Realm, name, length: 0, body, isConstructor: false)));

    /// <summary>Set or replace a global value (variable or object).</summary>
    public void SetGlobal(string name, JsValue value) => Global.Set(name, value);

    /// <summary>Bridge a host's microtask scheduler (e.g. the renderer's
    /// <c>WebEventLoop</c>) into the realm. Pass <c>null</c> to revert to the
    /// internal drain. Thin convenience wrapper around
    /// <see cref="MicrotaskQueue.SetHostScheduler"/> so hosts don't need to
    /// reach through the realm.</summary>
    public void SetMicrotaskScheduler(Action<Action>? scheduler)
        => Realm.Microtasks.SetHostScheduler(scheduler);

    /// <summary>Look up a global. Returns Undefined if absent.</summary>
    public JsValue GetGlobal(string name) => Global.Get(name);

    private static string LevelName(ConsoleLevel level) => level switch
    {
        ConsoleLevel.Log => "log",
        ConsoleLevel.Info => "info",
        ConsoleLevel.Warn => "warn",
        ConsoleLevel.Error => "error",
        ConsoleLevel.Debug => "debug",
        ConsoleLevel.Dir => "dir",
        ConsoleLevel.Table => "table",
        ConsoleLevel.Trace => "trace",
        _ => "log",
    };

    private static ConsoleLevel ParseLevel(string level) => level switch
    {
        "info" => ConsoleLevel.Info,
        "warn" => ConsoleLevel.Warn,
        "error" => ConsoleLevel.Error,
        "debug" => ConsoleLevel.Debug,
        "dir" => ConsoleLevel.Dir,
        "table" => ConsoleLevel.Table,
        "trace" => ConsoleLevel.Trace,
        _ => ConsoleLevel.Log,
    };
}
