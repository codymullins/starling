namespace Starling.Css.CounterStyle;

/// <summary>
/// The algorithm a counter style uses to map an integer to its symbols
/// (CSS Counter Styles 3 §3.1 — the <c>system</c> descriptor).
/// </summary>
public enum CounterSystem
{
    /// <summary>Cycles through the symbols repeatedly (§2.1).</summary>
    Cyclic,

    /// <summary>Runs through the symbols once, then falls back (§2.2).</summary>
    Fixed,

    /// <summary>Symbolic numbering: 1→a, 2→b, …, n→nth symbol repeated (§2.3).</summary>
    Symbolic,

    /// <summary>Bijective base-N alphabetic numbering (§2.4).</summary>
    Alphabetic,

    /// <summary>Positional base-N numbering (§2.5).</summary>
    Numeric,

    /// <summary>Sign-value notation from value/symbol weights (§2.6).</summary>
    Additive,

    /// <summary>Inherits another style's algorithm and descriptors (§3.1).</summary>
    Extends,
}

/// <summary>
/// One additive tuple from the <c>additive-symbols</c> descriptor: a weight and
/// the symbol used to represent that weight (CSS Counter Styles 3 §3.1.4).
/// </summary>
public readonly record struct AdditiveSymbol(int Weight, string Symbol);

/// <summary>
/// A parsed <c>@counter-style</c> at-rule (CSS Counter Styles 3 §3). Holds the
/// descriptor model that <see cref="CounterStyleResolver"/> samples to render a
/// counter integer to its marker string. Defaults follow §3.1's per-descriptor
/// initial values.
/// </summary>
public sealed record CounterStyleRule
{
    public required string Name { get; init; }

    public CounterSystem System { get; init; } = CounterSystem.Symbolic;

    /// <summary>For <c>fixed</c>: the integer the first symbol represents
    /// (§3.1.1). Defaults to 1.</summary>
    public int FixedFirstValue { get; init; } = 1;

    /// <summary>For <c>extends</c>: the name of the extended style (§3.1).</summary>
    public string? ExtendsName { get; init; }

    /// <summary>The ordered <c>symbols</c> list (§3.1.2).</summary>
    public IReadOnlyList<string> Symbols { get; init; } = [];

    /// <summary>The <c>additive-symbols</c> list, sorted descending by weight
    /// (§3.1.4).</summary>
    public IReadOnlyList<AdditiveSymbol> AdditiveSymbols { get; init; } = [];

    /// <summary>The <c>negative</c> descriptor prefix (§3.2). Defaults to the
    /// hyphen-minus "-".</summary>
    public string NegativePrefix { get; init; } = "-";

    /// <summary>The <c>negative</c> descriptor suffix (§3.2). Defaults to "".</summary>
    public string NegativeSuffix { get; init; } = "";

    /// <summary>The <c>prefix</c> descriptor (§3.3). Defaults to "".</summary>
    public string Prefix { get; init; } = "";

    /// <summary>The <c>suffix</c> descriptor (§3.3). Defaults to ". " for most
    /// predefined styles; "" for symbolic glyph styles.</summary>
    public string Suffix { get; init; } = ". ";

    /// <summary>The <c>range</c> descriptor's lower bound, null = auto (§3.4).</summary>
    public int? RangeLow { get; init; }

    /// <summary>The <c>range</c> descriptor's upper bound, null = auto (§3.4).</summary>
    public int? RangeHigh { get; init; }

    /// <summary>True when <c>range</c> was given explicitly (vs. <c>auto</c>),
    /// so the resolver knows whether to honor the bounds (§3.4).</summary>
    public bool HasExplicitRange { get; init; }

    /// <summary>The <c>pad</c> descriptor's minimum length (§3.5). 0 = no pad.</summary>
    public int PadLength { get; init; }

    /// <summary>The <c>pad</c> descriptor's pad symbol (§3.5).</summary>
    public string PadSymbol { get; init; } = "";

    /// <summary>The <c>fallback</c> style name (§3.6). Defaults to "decimal".</summary>
    public string Fallback { get; init; } = "decimal";
}
