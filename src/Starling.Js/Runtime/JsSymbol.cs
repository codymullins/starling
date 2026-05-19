namespace Tessera.Js.Runtime;

/// <summary>ECMA-262 §6.1.5 Symbol value identity: an immutable, unique primitive token.</summary>
public sealed class JsSymbol
{
    private static long s_nextId;

    public JsSymbol(string? description)
    {
        Description = description;
        Id = Interlocked.Increment(ref s_nextId);
    }

    public string? Description { get; }
    public long Id { get; }

    public string DescriptiveString => Description is null ? "Symbol()" : $"Symbol({Description})";

    public override string ToString() => DescriptiveString;
}
