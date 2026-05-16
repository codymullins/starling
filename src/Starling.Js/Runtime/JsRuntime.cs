namespace Tessera.Js.Runtime;

/// <summary>
/// Host-facing entry point for the JS engine. Carries the global object
/// and exposes API for registering native functions + running scripts.
/// </summary>
/// <remarks>
/// In a real browser the runtime sits behind the Window binding (M4-02).
/// For now it's a standalone unit so the headless CLI can run JS scripts
/// against a configurable host surface.
/// </remarks>
public sealed class JsRuntime
{
    public JsObject Global { get; }

    public JsRuntime()
    {
        Global = new JsObject();
        // The global object refers to itself under "globalThis" per §19.1.
        Global.Set("globalThis", JsValue.Object(Global));
        Global.Set("undefined", JsValue.Undefined);
        Global.Set("NaN", JsValue.NaN);
        Global.Set("Infinity", JsValue.Number(double.PositiveInfinity));
    }

    /// <summary>
    /// Register a host-side function as a global named <paramref name="name"/>.
    /// </summary>
    public void RegisterGlobal(string name, Func<JsValue[], JsValue> body)
        => Global.Set(name, JsValue.Object(new JsNativeFunction(name, body)));

    /// <summary>
    /// Set or replace a global value (variable or object).
    /// </summary>
    public void SetGlobal(string name, JsValue value) => Global.Set(name, value);

    /// <summary>Look up a global. Returns Undefined if absent.</summary>
    public JsValue GetGlobal(string name) => Global.Get(name);
}
