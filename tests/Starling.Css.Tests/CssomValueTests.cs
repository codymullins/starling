using AwesomeAssertions;
using Starling.Css.Cssom;
using Starling.Css.Parser;
using Starling.Spec;

namespace Starling.Css.Tests;

/// <summary>
/// CSSOM value canonicalization + declaration/rule round-trips (CSSOM §6.4,
/// CSS Syntax §8). Mirrors css/css-syntax/decimal-points-in-numbers.html.
/// </summary>
[Spec("css-syntax-3", "https://drafts.csswg.org/css-syntax/#consume-number", "§8")]
[TestClass]
public sealed class CssomValueTests
{
    private static CssomStyleRule FirstRule(string css)
    {
        var sheet = new CssomStyleSheet(CssParser.ParseStyleSheet(css));
        return (CssomStyleRule)sheet.Rules.First(r => r is not null)!;
    }

    [TestMethod]
    [DataRow("1.0", "1")]
    [DataRow(".1", "0.1")]
    [DataRow("1.50", "1.5")]
    [DataRow("10", "10")]
    [DataRow("-0.5", "-0.5")]
    public void Canonicalizes_number_line_height(string input, string expected)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("line-height", "0", null);
        rule.Style.SetProperty("line-height", input, null);
        rule.Style.GetPropertyValue("line-height").Should().Be(expected);
    }

    [TestMethod]
    [DataRow("1.0px", "1px")]
    [DataRow(".1px", "0.1px")]
    [DataRow("12px", "12px")]
    public void Canonicalizes_dimension_width(string input, string expected)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("width", "0px", null);
        rule.Style.SetProperty("width", input, null);
        rule.Style.GetPropertyValue("width").Should().Be(expected);
    }

    [TestMethod]
    [DataRow("1.")]   // trailing decimal point is invalid
    [DataRow("1.px")] // invalid dimension
    public void Rejects_invalid_number(string input)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("line-height", "0", null);
        rule.Style.SetProperty("line-height", input, null);
        // The invalid value must be ignored, leaving the fallback.
        rule.Style.GetPropertyValue("line-height").Should().Be("0");
    }

    [TestMethod]
    public void SetProperty_then_getPropertyValue_round_trips()
    {
        var rule = FirstRule("div { color: red; }");
        rule.Style.GetPropertyValue("color").Should().Be("red");
        rule.Style.SetProperty("color", "blue", null);
        rule.Style.GetPropertyValue("color").Should().Be("blue");
    }

    [TestMethod]
    public void RemoveProperty_clears_value()
    {
        var rule = FirstRule("div { color: red; }");
        rule.Style.RemoveProperty("color").Should().Be("red");
        rule.Style.GetPropertyValue("color").Should().Be("");
    }

    [TestMethod]
    public void Important_priority_round_trips()
    {
        var rule = FirstRule("div { color: red !important; }");
        rule.Style.GetPropertyPriority("color").Should().Be("important");
        rule.Style.SetProperty("color", "blue", "important");
        rule.Style.GetPropertyPriority("color").Should().Be("important");
        rule.Style.CssText.Should().Contain("!important");
    }

    [TestMethod]
    public void CssText_serializes_declarations()
    {
        var rule = FirstRule("div { color: red; width: 10px; }");
        rule.Style.CssText.Should().Be("color: red; width: 10px;");
    }

    // ---------------------------------------------------------------
    // WPT-06: <urange> canonicalization (CSS Syntax §4.3.10)
    // ---------------------------------------------------------------

    [TestMethod]
    [DataRow("u+abc", "U+ABC")]
    [DataRow("u+a", "U+A")]
    [DataRow("u+a?", "U+A0-AF")]
    [DataRow("u+0-1", "U+0-1")]
    [DataRow("u+0", "U+0")]
    [DataRow("u+000000", "U+0")]
    [DataRow("u+0a", "U+A")]
    [DataRow("u+00000a", "U+A")]
    [DataRow("u+1e9a", "U+1E9A")]
    [DataRow("u+1e3", "U+1E3")]
    [DataRow("u+1e-20", "U+1E-20")]
    [DataRow("u+?", "U+0-F")]
    [DataRow("u+?????", "U+0-FFFFF")]
    [DataRow("u+0-10ffff", "U+0-10FFFF")]
    [Spec("css-syntax-3", "https://drafts.csswg.org/css-syntax/#urange-syntax", "4.3.10")]
    public void UrangeParser_canonicalizes_valid(string input, string expected)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("unicode-range", "U+1357", null); // valid fallback
        rule.Style.SetProperty("unicode-range", input, null);
        // Compare case-insensitively (WPT uses .toUpperCase() on both sides).
        rule.Style.GetPropertyValue("unicode-range").ToUpperInvariant()
            .Should().Be(expected.ToUpperInvariant());
    }

    [TestMethod]
    [DataRow("u+efg")]       // 'g' not hex
    [DataRow("u+ abc")]      // space after +
    [DataRow("u +abc")]      // space before +
    [DataRow("u+aaaaaaa")]   // 7 hex chars > 6
    [DataRow("u+0000000")]   // 7 zeros
    [DataRow("u+aaaaaa?")]   // 6 hex + 1 wildcard = 7
    [DataRow("u+a?a")]       // hex after wildcard
    [DataRow("u+222222")]    // > U+10FFFF
    [DataRow("u+0-110000")]  // end > U+10FFFF
    [DataRow("u+??????")]    // U+FFFFFF > max
    [Spec("css-syntax-3", "https://drafts.csswg.org/css-syntax/#urange-syntax", "4.3.10")]
    public void UrangeParser_rejects_invalid(string input)
    {
        var rule = FirstRule(".foo {}");
        rule.Style.SetProperty("unicode-range", "U+1357", null); // valid fallback
        rule.Style.SetProperty("unicode-range", input, null);
        // Invalid value must be ignored; the fallback should remain.
        rule.Style.GetPropertyValue("unicode-range").ToUpperInvariant()
            .Should().Be("U+1357");
    }

    [TestMethod]
    [DataRow("--foo-1:bar;", "bar")]
    [DataRow("--foo-2: bar;", "bar")]
    [DataRow("--foo-3:bar ;", "bar")]
    [DataRow("--foo-4: bar ;", "bar")]
    [Spec("cssom", "https://www.w3.org/TR/cssom-1/", "6.7.4")]
    public void Custom_property_declared_value_is_trimmed(string declaration, string expected)
    {
        // Verify that CssomDeclarationBlock stores trimmed custom property values.
        var block = new CssomDeclarationBlock();
        // Simulate stylesheet parsing: extract prop+value from the test declaration.
        var colon = declaration.IndexOf(':');
        var name = declaration[..colon].Trim();
        var rawValue = declaration[(colon + 1)..].TrimEnd(';', ' ');
        block.SetProperty(name, rawValue, null);
        block.GetPropertyValue(name).Should().Be(expected);
    }
}
