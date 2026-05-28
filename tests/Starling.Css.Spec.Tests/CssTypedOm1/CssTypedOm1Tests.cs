using AwesomeAssertions;
using Starling.Css.TypedOm;

namespace Starling.Css.Spec.Tests.CssTypedOm1;

/// <summary>
/// Conformance tests for
/// <see href="https://www.w3.org/TR/css-typed-om-1/">CSS Typed OM Level 1</see>.
/// </summary>
[TestClass]
[Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/")]
public sealed class CssTypedOm1Tests
{
    // ── CssStyleValue.Parse — §3.2 ──────────────────────────────────────────

    /// <summary>
    /// Parse <c>10px</c> — a single dimension token — into
    /// <see cref="CssUnitValue"/> with Value=10 and Unit="px".
    /// CSS Typed OM 1 §3.2 / §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-dimension")]
    [SpecFact]
    public void Parse_Dimension_ReturnsCssUnitValue()
    {
        var result = CssStyleValue.Parse("width", "10px");

        result.Should().BeOfType<CssUnitValue>();
        var unit = (CssUnitValue)result;
        unit.Value.Should().Be(10.0);
        unit.Unit.Should().Be("px");
    }

    /// <summary>
    /// Parse <c>50%</c> — a percentage token — into
    /// <see cref="CssUnitValue"/> with Value=50 and Unit="%".
    /// CSS Typed OM 1 §3.2 / §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-percentage")]
    [SpecFact]
    public void Parse_Percentage_ReturnsCssUnitValue()
    {
        var result = CssStyleValue.Parse("width", "50%");

        result.Should().BeOfType<CssUnitValue>();
        var unit = (CssUnitValue)result;
        unit.Value.Should().Be(50.0);
        unit.Unit.Should().Be("%");
    }

    /// <summary>
    /// Parse <c>5</c> — a bare number token — into
    /// <see cref="CssUnitValue"/> with Value=5 and Unit="number".
    /// CSS Typed OM 1 §3.2 / §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-number")]
    [SpecFact]
    public void Parse_Number_ReturnsCssUnitValueWithUnitNumber()
    {
        var result = CssStyleValue.Parse("opacity", "5");

        result.Should().BeOfType<CssUnitValue>();
        var unit = (CssUnitValue)result;
        unit.Value.Should().Be(5.0);
        unit.Unit.Should().Be("number");
    }

    /// <summary>
    /// Parse <c>auto</c> — a single identifier token — into
    /// <see cref="CssKeywordValue"/> with Value="auto".
    /// CSS Typed OM 1 §3.2 / §5.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-keyword")]
    [SpecFact]
    public void Parse_Keyword_ReturnsCssKeywordValue()
    {
        var result = CssStyleValue.Parse("width", "auto");

        result.Should().BeOfType<CssKeywordValue>();
        var kw = (CssKeywordValue)result;
        kw.Value.Should().Be("auto");
    }

    /// <summary>
    /// Parse a value that cannot be modelled as a typed value — fall back to
    /// <see cref="CssUnparsedValue"/> rather than throwing.
    /// CSS Typed OM 1 §3.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-fallback")]
    [SpecFact]
    public void Parse_Unmodelable_ReturnsCssUnparsedValue()
    {
        // "1px solid red" is three tokens — cannot map to a single typed value.
        var result = CssStyleValue.Parse("border", "1px solid red");

        result.Should().BeOfType<CssUnparsedValue>();
        var unparsed = (CssUnparsedValue)result;
        unparsed.RawText.Should().Be("1px solid red");
    }

    // ── ToString round-trips — §3 ────────────────────────────────────────────

    /// <summary>
    /// <c>new CssUnitValue(10, "px").ToString()</c> must round-trip to <c>"10px"</c>.
    /// CSS Typed OM 1 §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "tostring-px")]
    [SpecFact]
    public void ToString_UnitValue_Px_RoundTrips()
    {
        new CssUnitValue(10, "px").ToString().Should().Be("10px");
    }

    /// <summary>
    /// <c>new CssUnitValue(50, "%").ToString()</c> must round-trip to <c>"50%"</c>.
    /// CSS Typed OM 1 §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "tostring-pct")]
    [SpecFact]
    public void ToString_UnitValue_Percentage_RoundTrips()
    {
        new CssUnitValue(50, "%").ToString().Should().Be("50%");
    }

    /// <summary>
    /// <c>new CssUnitValue(5, "number").ToString()</c> must produce <c>"5"</c>
    /// (no suffix for dimensionless numbers).
    /// CSS Typed OM 1 §4.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "tostring-number")]
    [SpecFact]
    public void ToString_UnitValue_Number_NoSuffix()
    {
        new CssUnitValue(5, "number").ToString().Should().Be("5");
    }

    /// <summary>
    /// <c>new CssKeywordValue("auto").ToString()</c> must round-trip to <c>"auto"</c>.
    /// CSS Typed OM 1 §5.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "tostring-keyword")]
    [SpecFact]
    public void ToString_KeywordValue_RoundTrips()
    {
        new CssKeywordValue("auto").ToString().Should().Be("auto");
    }

    // ── Type hierarchy — §3 / §4 ─────────────────────────────────────────────

    /// <summary>
    /// <see cref="CssUnitValue"/> must derive from <see cref="CssNumericValue"/>
    /// which in turn derives from <see cref="CssStyleValue"/>.
    /// CSS Typed OM 1 §4.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "hierarchy")]
    [SpecFact]
    public void CssUnitValue_InheritsFromCssNumericValue()
    {
        var value = new CssUnitValue(1, "px");
        value.Should().BeAssignableTo<CssNumericValue>();
        value.Should().BeAssignableTo<CssStyleValue>();
    }

    /// <summary>
    /// Parse preserves leading/trailing whitespace trimming — <c>" 10px "</c>
    /// is still one dimension token and maps to <see cref="CssUnitValue"/>.
    /// CSS Typed OM 1 §3.2.
    /// </summary>
    [Spec("css-typed-om-1", "https://www.w3.org/TR/css-typed-om-1/", "parse-whitespace")]
    [SpecFact]
    public void Parse_DimensionWithSurroundingWhitespace_ReturnsCssUnitValue()
    {
        var result = CssStyleValue.Parse("width", "  10px  ");

        result.Should().BeOfType<CssUnitValue>();
        var unit = (CssUnitValue)result;
        unit.Value.Should().Be(10.0);
        unit.Unit.Should().Be("px");
    }
}
