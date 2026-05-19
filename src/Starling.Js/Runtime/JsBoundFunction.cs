namespace Tessera.Js.Runtime;

/// <summary>
/// §10.4.1 Bound Function Exotic Object. Produced by
/// <c>Function.prototype.bind</c>. Stores the target callable, the bound
/// <c>this</c> value, and a (possibly empty) sequence of leading arguments.
/// Invoked via <see cref="AbstractOperations.Call"/> /
/// <see cref="AbstractOperations.Construct"/>, which forward to the target.
/// </summary>
public sealed class JsBoundFunction : JsObject
{
    public JsObject Target { get; }
    public JsValue BoundThis { get; }
    public IReadOnlyList<JsValue> BoundArgs { get; }

    public JsBoundFunction(JsObject target, JsValue boundThis, IReadOnlyList<JsValue> boundArgs, JsObject? prototype)
        : base(prototype)
    {
        Target = target ?? throw new ArgumentNullException(nameof(target));
        BoundThis = boundThis;
        BoundArgs = boundArgs ?? Array.Empty<JsValue>();
    }

    public override string ToString() => $"function bound() {{ [native code] }}";
}
