using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts4;

/// <summary>
/// Conformance tests for the <c>font-family</c> property.
/// Spec: <see href="https://www.w3.org/TR/css-fonts-4/#propdef-font-family">CSS Fonts 4 §3.1</see>.
/// </summary>
[TestClass]
[Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#propdef-font-family")]
public sealed class FontFamilyTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    // ── single generic keyword ────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_serif()
    {
        var decls = Expand("font-family: serif;");
        decls.Should().ContainSingle();
        decls[0].Id.Should().Be(PropertyId.FontFamily);
        decls[0].Value.Should().Be(new CssKeyword("serif"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_sans_serif()
    {
        var decls = Expand("font-family: sans-serif;");
        decls[0].Value.Should().Be(new CssKeyword("sans-serif"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_monospace()
    {
        var decls = Expand("font-family: monospace;");
        decls[0].Value.Should().Be(new CssKeyword("monospace"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_cursive()
    {
        var decls = Expand("font-family: cursive;");
        decls[0].Value.Should().Be(new CssKeyword("cursive"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_fantasy()
    {
        var decls = Expand("font-family: fantasy;");
        decls[0].Value.Should().Be(new CssKeyword("fantasy"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_system_ui()
    {
        var decls = Expand("font-family: system-ui;");
        decls[0].Value.Should().Be(new CssKeyword("system-ui"));
    }

    // ── generic keyword is case-insensitive ───────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#generic-font-families")]
    public void Generic_keyword_normalised_to_lowercase()
    {
        // CSS spec: generic family names are case-insensitive; the impl stores them lowercase.
        var decls = Expand("font-family: SERIF;");
        decls[0].Value.Should().Be(new CssKeyword("serif"));
    }

    // ── single named family (ident, case preserved) ───────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Single_ident_family_preserves_case()
    {
        // CSS Fonts 4 §3.1: unquoted family names keep the authored case.
        var decls = Expand("font-family: Helvetica;");
        decls[0].Value.Should().Be(new CssKeyword("Helvetica"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Single_quoted_family_preserves_case()
    {
        var decls = Expand("font-family: \"Helvetica Neue\";");
        decls[0].Value.Should().Be(new CssString("Helvetica Neue"));
    }

    // ── multi-word unquoted family name ───────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Multi_word_unquoted_family_joined_with_space()
    {
        // "Open Sans" unquoted is two idents; they should be joined to one keyword.
        var decls = Expand("font-family: Open Sans;");
        decls[0].Value.Should().Be(new CssKeyword("Open Sans"));
    }

    // ── comma list ────────────────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Comma_list_produces_CssValueList()
    {
        var decls = Expand("font-family: Helvetica, Arial, sans-serif;");
        decls.Should().ContainSingle();
        var list = decls[0].Value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(3);
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Comma_list_preserves_ident_case_for_named_families()
    {
        var decls = Expand("font-family: Helvetica, Arial, sans-serif;");
        var list = (CssValueList)decls[0].Value;
        list.Values[0].Should().Be(new CssKeyword("Helvetica"));
        list.Values[1].Should().Be(new CssKeyword("Arial"));
        list.Values[2].Should().Be(new CssKeyword("sans-serif")); // generic normalised
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Comma_list_with_multiword_and_generic()
    {
        var decls = Expand("font-family: Open Sans, monospace;");
        var list = (CssValueList)decls[0].Value;
        list.Values[0].Should().Be(new CssKeyword("Open Sans"));
        list.Values[1].Should().Be(new CssKeyword("monospace"));
    }

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Comma_list_with_quoted_name()
    {
        var decls = Expand("font-family: \"Times New Roman\", serif;");
        var list = (CssValueList)decls[0].Value;
        list.Values[0].Should().Be(new CssString("Times New Roman"));
        list.Values[1].Should().Be(new CssKeyword("serif"));
    }

    // ── property is inherited ─────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Font_family_is_inherited()
    {
        // CSS Fonts 4 §3.1 — font-family is inherited.
        PropertyRegistry.Inherits(PropertyId.FontFamily).Should().BeTrue();
    }

    // ── initial value ─────────────────────────────────────────────────────

    [SpecFact]
    [Spec("css-fonts-4", "https://www.w3.org/TR/css-fonts-4/", "#font-family-prop")]
    public void Font_family_initial_value_is_serif()
    {
        // CSS Fonts 4 §3.1 — initial is UA-dependent; Starling uses "serif".
        PropertyRegistry.InitialValue(PropertyId.FontFamily).Should().Be(new CssKeyword("serif"));
    }
}
