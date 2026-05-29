using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font</c> shorthand property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#font-prop">CSS Fonts 4 §4.8</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-prop")]
public sealed class FontShorthandTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── font shorthand is not yet expanded into its longhands ─────────────
    // The `font` shorthand requires a dedicated parser that classifies each
    // component into font-style, font-weight, font-size, line-height, and
    // font-family. That parser is not implemented yet (the property name
    // "font" has no PropertyId entry and no case in ExpandSwitch, so the
    // shorthand produces zero declarations today).

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-prop")]
    public void Shorthand_italic_bold_16px_1_5_sans_serif_expands_all_longhands()
    {
        // CSS Fonts 4 §4.8: `font: italic bold 16px/1.5 sans-serif` must set
        // font-style, font-weight, font-size, line-height, and font-family.
        var decls = Expand("font: italic bold 16px/1.5 sans-serif;");
        decls.Should().NotBeEmpty();

        var style = decls.FirstOrDefault(d => d.Id == PropertyId.FontStyle);
        style.Should().NotBeNull();
        style!.Value.Should().Be(new CssKeyword("italic"));

        var weight = decls.FirstOrDefault(d => d.Id == PropertyId.FontWeight);
        weight.Should().NotBeNull();
        weight!.Value.Should().Be(new CssKeyword("bold"));

        var size = decls.FirstOrDefault(d => d.Id == PropertyId.FontSize);
        size.Should().NotBeNull();
        size!.Value.Should().Be(new CssLength(16, CssLengthUnit.Px));

        var lh = decls.FirstOrDefault(d => d.Id == PropertyId.LineHeight);
        lh.Should().NotBeNull();
        lh!.Value.Should().Be(new CssNumber(1.5));

        var family = decls.FirstOrDefault(d => d.Id == PropertyId.FontFamily);
        family.Should().NotBeNull();
        family!.Value.Should().Be(new CssKeyword("sans-serif"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#system-font-values")]
    public void System_font_keyword_caption()
    {
        // CSS Fonts 4 §4.8: `font: caption` is a system font keyword that sets
        // all longhands from the UA's "caption" system font metrics.
        var decls = Expand("font: caption;");
        decls.Should().NotBeEmpty("system font keyword should produce at least one declaration");
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-prop")]
    public void Shorthand_with_only_size_and_family()
    {
        // Minimal valid `font` shorthand: font-size and font-family are required.
        var decls = Expand("font: 12px serif;");
        decls.Should().NotBeEmpty();

        var size = decls.FirstOrDefault(d => d.Id == PropertyId.FontSize);
        size.Should().NotBeNull();
        size!.Value.Should().Be(new CssLength(12, CssLengthUnit.Px));

        var family = decls.FirstOrDefault(d => d.Id == PropertyId.FontFamily);
        family.Should().NotBeNull();
        family!.Value.Should().Be(new CssKeyword("serif"));
    }
}
