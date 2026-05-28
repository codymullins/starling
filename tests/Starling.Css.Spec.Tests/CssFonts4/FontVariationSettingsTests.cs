using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for <c>font-variation-settings</c>.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#descdef-font-face-font-variation-settings">CSS Fonts 4 §7.2</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-variation-settings-def")]
public sealed class FontVariationSettingsTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── normal ────────────────────────────────────────────────────────────

    [SpecFact]
    public void Keyword_normal_is_parsed()
    {
        // `font-variation-settings: normal` means no axis overrides.
        var decls = Expand("font-variation-settings: normal;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontVariationSettings);
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    // ── axis tag + value ──────────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-variation-settings-def")]
    public void Single_axis_tag_and_value_parses_to_value_list()
    {
        // CSS Fonts 4 §7.2: `font-variation-settings: 'wght' 300` is a string
        // axis tag followed by a number. The parser stores it as a CssValueList
        // containing the raw tokens.
        var decls = Expand("font-variation-settings: 'wght' 300;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontVariationSettings);
        // The parsed value is a list; inspect that it is non-empty.
        decls[0].Value.Should().BeOfType<CssValueList>()
            .Which.Values.Should().NotBeEmpty();
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    public void Font_variation_settings_is_inherited()
    {
        PropertyRegistry.Inherits(PropertyId.FontVariationSettings).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    public void Font_variation_settings_initial_is_normal()
    {
        PropertyRegistry.InitialValue(PropertyId.FontVariationSettings)
            .Should().Be(new CssKeyword("normal"));
    }

    // ── matrix gap: structured axis/value interpretation ─────────────────
    // The matrix notes that font-variation-settings has gaps. The parser
    // stores the raw tokens without validating axis tag format or interpreting
    // the numeric value as a typed axis setting.

    [PendingFact(
        "font-variation-settings axis/value round-trip not validated — parser stores raw tokens, no typed AxisSetting model",
        trackingWp: "wp:spec-css-fonts-4")]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-variation-settings-def")]
    public void Axis_tag_preserved_and_value_accessible_as_number()
    {
        // CSS Fonts 4 §7.2: the string token is the four-character axis tag
        // and the following number is the axis value. A conforming impl should
        // expose these in a typed way (e.g. a list of (tag, number) pairs).
        var decls = Expand("font-variation-settings: 'wght' 300, 'wdth' 75;");
        decls.Should().ContainSingle();
        var list = decls[0].Value.Should().BeOfType<CssValueList>().Subject;
        // Expect at least four values: two tags and two numbers.
        list.Values.Should().HaveCountGreaterThanOrEqualTo(4);
        list.Values[0].Should().BeOfType<CssString>()
            .Which.Value.Should().Be("wght");
        list.Values[1].Should().BeOfType<CssNumber>()
            .Which.Value.Should().Be(300);
    }
}
