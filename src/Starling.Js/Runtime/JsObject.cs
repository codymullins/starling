namespace Tessera.Js.Runtime;

/// <summary>
/// Basic JS object — a string-keyed property bag. The spec also has
/// symbol keys, prototype chains, and accessor properties; those are
/// follow-up work (M3-05+).
/// </summary>
public class JsObject
{
    private readonly Dictionary<string, JsValue> _properties =
        new(StringComparer.Ordinal);

    public JsValue Get(string name)
        => _properties.TryGetValue(name, out var v) ? v : JsValue.Undefined;

    public void Set(string name, JsValue value) => _properties[name] = value;

    public bool Has(string name) => _properties.ContainsKey(name);

    public bool Delete(string name) => _properties.Remove(name);

    public IEnumerable<string> Keys => _properties.Keys;

    public override string ToString() => "[object Object]";
}

/// <summary>
/// A native (host-side) function callable from JS via the global object.
/// Receives the unwrapped argument array; returns a JsValue.
/// </summary>
public sealed class JsNativeFunction : JsObject
{
    public string Name { get; }
    public Func<JsValue[], JsValue> Body { get; }

    public JsNativeFunction(string name, Func<JsValue[], JsValue> body)
    {
        Name = name;
        Body = body ?? throw new ArgumentNullException(nameof(body));
    }

    public override string ToString() => $"function {Name}() {{ [native code] }}";
}
