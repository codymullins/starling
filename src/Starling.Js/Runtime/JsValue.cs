using System.Globalization;

namespace Tessera.Js.Runtime;

/// <summary>
/// The seven JS primitive value types plus Object. Backing for
/// <see cref="JsValue"/>.
/// </summary>
public enum JsValueKind : byte
{
    Undefined = 0,
    Null,
    Boolean,
    Number,
    String,
    Object,
    BigInt,
    Symbol,
}

/// <summary>
/// Tagged JS value. Struct-backed to avoid boxing for primitives. NaN-boxing
/// is a v2 optimization (wp:M7-01); for v1 the simple struct layout is fine.
/// </summary>
/// <remarks>
/// Spec abstract operations (ToNumber, ToString, ToBoolean, abstract+strict
/// equality) live on this type as static methods so the VM dispatch loop can
/// inline them.
/// </remarks>
public readonly struct JsValue : IEquatable<JsValue>
{
    public readonly JsValueKind Kind;
    private readonly double _num;
    private readonly object? _ref;

    private JsValue(JsValueKind kind, double num = 0, object? @ref = null)
    {
        Kind = kind;
        _num = num;
        _ref = @ref;
    }

    public static readonly JsValue Undefined = default;
    public static readonly JsValue Null = new(JsValueKind.Null);
    public static readonly JsValue True = new(JsValueKind.Boolean, 1);
    public static readonly JsValue False = new(JsValueKind.Boolean, 0);
    public static readonly JsValue NaN = new(JsValueKind.Number, double.NaN);
    public static readonly JsValue Zero = new(JsValueKind.Number, 0);

    public static JsValue Number(double d) => new(JsValueKind.Number, d);
    public static JsValue Boolean(bool b) => b ? True : False;
    public static JsValue String(string s) => new(JsValueKind.String, 0, s);
    public static JsValue Object(JsObject o) => new(JsValueKind.Object, 0, o);
    public static JsValue BigInt(string digits) => new(JsValueKind.BigInt, 0, digits);
    public static JsValue Symbol(JsSymbol symbol) => new(JsValueKind.Symbol, 0, symbol);

    public bool IsUndefined => Kind == JsValueKind.Undefined;
    public bool IsNull => Kind == JsValueKind.Null;
    public bool IsNullish => Kind is JsValueKind.Undefined or JsValueKind.Null;
    public bool IsBoolean => Kind == JsValueKind.Boolean;
    public bool IsNumber => Kind == JsValueKind.Number;
    public bool IsString => Kind == JsValueKind.String;
    public bool IsObject => Kind == JsValueKind.Object;
    public bool IsSymbol => Kind == JsValueKind.Symbol;

    public double AsNumber => Kind == JsValueKind.Number
        ? _num
        : throw new InvalidOperationException($"value is {Kind}, not Number");
    public bool AsBool => Kind == JsValueKind.Boolean
        ? _num != 0
        : throw new InvalidOperationException($"value is {Kind}, not Boolean");
    public string AsString => Kind == JsValueKind.String
        ? (string)_ref!
        : throw new InvalidOperationException($"value is {Kind}, not String");
    public JsObject AsObject => Kind == JsValueKind.Object
        ? (JsObject)_ref!
        : throw new InvalidOperationException($"value is {Kind}, not Object");
    public JsSymbol AsSymbol => Kind == JsValueKind.Symbol
        ? (JsSymbol)_ref!
        : throw new InvalidOperationException($"value is {Kind}, not Symbol");

    // -----------------------------------------------------------------------
    // ES2024 abstract operations
    // -----------------------------------------------------------------------

    /// <summary>ToBoolean per §7.1.2.</summary>
    public static bool ToBoolean(JsValue v) => v.Kind switch
    {
        JsValueKind.Undefined => false,
        JsValueKind.Null => false,
        JsValueKind.Boolean => v._num != 0,
        JsValueKind.Number => v._num != 0 && !double.IsNaN(v._num),
        JsValueKind.String => ((string)v._ref!).Length > 0,
        JsValueKind.Object => true,
        JsValueKind.BigInt => ((string)v._ref!) != "0",
        JsValueKind.Symbol => true,
        _ => false,
    };

    /// <summary>ToNumber per §7.1.4.</summary>
    public static double ToNumber(JsValue v) => v.Kind switch
    {
        JsValueKind.Undefined => double.NaN,
        JsValueKind.Null => 0,
        JsValueKind.Boolean => v._num,
        JsValueKind.Number => v._num,
        JsValueKind.String => ParseNumber((string)v._ref!),
        JsValueKind.Object => double.NaN, // simplified: real spec calls ToPrimitive then ToNumber
        JsValueKind.BigInt => throw new InvalidOperationException("can't convert BigInt to Number"),
        JsValueKind.Symbol => throw new InvalidOperationException("can't convert Symbol to Number"),
        _ => double.NaN,
    };

    private static double ParseNumber(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return 0;
        if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return double.NaN;
    }

    /// <summary>ToString per §7.1.17.</summary>
    public static string ToStringValue(JsValue v) => v.Kind switch
    {
        JsValueKind.Undefined => "undefined",
        JsValueKind.Null => "null",
        JsValueKind.Boolean => v._num != 0 ? "true" : "false",
        JsValueKind.Number => NumberToString(v._num),
        JsValueKind.String => (string)v._ref!,
        JsValueKind.Object => "[object Object]", // simplified
        JsValueKind.BigInt => (string)v._ref!,
        JsValueKind.Symbol => ((JsSymbol)v._ref!).DescriptiveString,
        _ => "",
    };

    private static string NumberToString(double d)
    {
        if (double.IsNaN(d)) return "NaN";
        if (double.IsPositiveInfinity(d)) return "Infinity";
        if (double.IsNegativeInfinity(d)) return "-Infinity";
        if (d == 0) return "0";
        // Integer fast path.
        if (d == Math.Truncate(d) && Math.Abs(d) < 1e21)
            return ((long)d).ToString(CultureInfo.InvariantCulture);
        return d.ToString("R", CultureInfo.InvariantCulture);
    }

    /// <summary>Strict equality per §7.2.16 (no coercion).</summary>
    public static bool StrictEquals(JsValue a, JsValue b)
    {
        if (a.Kind != b.Kind) return false;
        return a.Kind switch
        {
            JsValueKind.Undefined => true,
            JsValueKind.Null => true,
            JsValueKind.Boolean => a._num == b._num,
            JsValueKind.Number =>
                double.IsNaN(a._num) || double.IsNaN(b._num)
                    ? false
                    : a._num == b._num,
            JsValueKind.String => string.Equals((string)a._ref!, (string)b._ref!, StringComparison.Ordinal),
            JsValueKind.Object => ReferenceEquals(a._ref, b._ref),
            JsValueKind.BigInt => string.Equals((string)a._ref!, (string)b._ref!, StringComparison.Ordinal),
            JsValueKind.Symbol => ReferenceEquals(a._ref, b._ref),
            _ => false,
        };
    }

    /// <summary>Abstract (loose) equality per §7.2.15. Performs type coercion.</summary>
    public static bool AbstractEquals(JsValue a, JsValue b)
    {
        if (a.Kind == b.Kind) return StrictEquals(a, b);
        // null == undefined.
        if (a.IsNullish && b.IsNullish) return true;
        // Number == String → coerce string to number.
        if (a.IsNumber && b.IsString)
            return !double.IsNaN(b._num /* unused */) && a._num == ParseNumber((string)b._ref!);
        if (a.IsString && b.IsNumber)
            return ParseNumber((string)a._ref!) == b._num;
        // Boolean → Number (either side).
        if (a.IsBoolean) return AbstractEquals(Number(a._num), b);
        if (b.IsBoolean) return AbstractEquals(a, Number(b._num));
        // Number-or-String == Object → coerce object to primitive (simplified: false).
        return false;
    }

    public bool Equals(JsValue other) => StrictEquals(this, other);
    public override bool Equals(object? obj) => obj is JsValue v && Equals(v);
    public override int GetHashCode() => HashCode.Combine(Kind, _num, _ref);

    public override string ToString() => ToStringValue(this);
}
