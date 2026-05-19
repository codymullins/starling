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
        StringCtor.Install(Realm);
        NumberCtor.Install(Realm);
        BooleanCtor.Install(Realm);
        SymbolCtor.Install(Realm);
        Globals.Install(Realm);
        MathObj.Install(Realm);
        JsonObj.Install(Realm);
        ConsoleObj.Install(Realm);
        Global.Set("globalThis", JsValue.Object(Global));
        Global.Set("undefined", JsValue.Undefined);
        Global.Set("NaN", JsValue.NaN);
        Global.Set("Infinity", JsValue.Number(double.PositiveInfinity));
    }

    /// <summary>Register a host-side function as a global named <paramref name="name"/>.</summary>
    public void RegisterGlobal(string name, Func<JsValue[], JsValue> body)
        => Global.Set(name, JsValue.Object(new JsNativeFunction(name, body)));

    /// <summary>Register a host-side function with full (thisValue, args) signature.</summary>
    public void RegisterGlobal(string name, Func<JsValue, JsValue[], JsValue> body)
        => Global.Set(name, JsValue.Object(new JsNativeFunction(name, body)));

    /// <summary>Set or replace a global value (variable or object).</summary>
    public void SetGlobal(string name, JsValue value) => Global.Set(name, value);

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
