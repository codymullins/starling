using System.Runtime.CompilerServices;

namespace Tessera.Js.Runtime;

/// <summary>ECMA-262 property keys are strings or Symbols (§6.1.7).</summary>
public readonly struct JsPropertyKey : IEquatable<JsPropertyKey>
{
    private readonly string? _string;
    private readonly JsSymbol? _symbol;

    private JsPropertyKey(string? @string, JsSymbol? symbol)
    {
        _string = @string;
        _symbol = symbol;
    }

    public bool IsSymbol => _symbol is not null;
    public bool IsString => _symbol is null;
    public string AsString => _string ?? throw new InvalidOperationException("property key is a Symbol");
    public JsSymbol AsSymbol => _symbol ?? throw new InvalidOperationException("property key is a string");

    public static JsPropertyKey String(string value) => new(value ?? throw new ArgumentNullException(nameof(value)), null);
    public static JsPropertyKey Symbol(JsSymbol value) => new(null, value ?? throw new ArgumentNullException(nameof(value)));

    public bool Equals(JsPropertyKey other) => IsSymbol == other.IsSymbol &&
        (IsSymbol ? ReferenceEquals(_symbol, other._symbol) : string.Equals(_string, other._string, StringComparison.Ordinal));

    public override bool Equals(object? obj) => obj is JsPropertyKey key && Equals(key);
    public override int GetHashCode() => IsSymbol ? RuntimeHelpers.GetHashCode(_symbol!) : StringComparer.Ordinal.GetHashCode(_string!);
    public override string ToString() => IsSymbol ? AsSymbol.DescriptiveString : AsString;

    public static implicit operator JsPropertyKey(string value) => String(value);
}
