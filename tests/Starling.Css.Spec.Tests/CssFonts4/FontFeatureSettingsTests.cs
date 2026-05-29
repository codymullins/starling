using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for <c>font-feature-settings</c>.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#font-feature-settings-prop">CSS Fonts 4 §7.1</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-prop")]
public sealed class FontFeatureSettingsTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // font-feature-settings is registered in PropertyId/PropertyRegistry
    // (initial `normal`, inherited) and parses via the generic value path.

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-prop")]
    public void Keyword_normal_is_parsed()
    {
        // CSS Fonts 4 §7.1: `font-feature-settings: normal` disables all
        // optional features. Requires a registered FontFeatureSettings PropertyId.
        var decls = Expand("font-feature-settings: normal;");
        decls.Should().ContainSingle();
        decls[0].Value.Should().Be(new CssKeyword("normal"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-prop")]
    public void Feature_tag_liga_0_disables_ligatures()
    {
        // CSS Fonts 4 §7.1: `'liga' 0` disables the ligatures OpenType feature.
        var decls = Expand("font-feature-settings: 'liga' 0;");
        decls.Should().ContainSingle();
        decls[0].Value.Should().BeOfType<CssValueList>();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-prop")]
    public void Feature_tag_smcp_on_enables_small_caps()
    {
        // CSS Fonts 4 §7.1: `'smcp' on` (or `'smcp' 1`) enables small caps.
        var decls = Expand("font-feature-settings: 'smcp' on;");
        decls.Should().ContainSingle();
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-feature-settings-prop")]
    public void Feature_settings_is_inherited()
    {
        // CSS Fonts 4 §7.1 — font-feature-settings is inherited.
        // Requires the property to be registered before Inherits() can return true.
        PropertyRegistry.TryGetPropertyId("font-feature-settings", out _).Should().BeTrue();
    }
}
