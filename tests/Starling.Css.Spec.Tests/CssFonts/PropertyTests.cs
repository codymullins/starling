using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFonts;

/// <summary>
/// Property conformance for <see href="https://drafts.csswg.org/css-fonts-4/">CSS Fonts Module Level 4</see>.
/// </summary>
[TestClass]
[Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/")]
public sealed class PropertyTests
{

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-fonts-4/#propdef-font-family"/>
    /// <para>Property <c>font-family</c> — value <c>[ &lt;font-family-name&gt; | &lt;generic-font-family&gt; ]#</c>; initial <c>depends on user agent</c>.</para>
    /// </summary>
    [Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/#propdef-font-family")]
    [SpecFact]
    public void Parses_font_family()
    {
        var decls = Expand("font-family: sans-serif;");
        decls.Single().Id.Should().Be(PropertyId.FontFamily);
        decls.Single().Value.Should().Be(new CssKeyword("sans-serif"));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-fonts-4/#propdef-font-weight"/>
    /// <para>Property <c>font-weight</c> — value <c>&lt;font-weight-absolute&gt; | bolder | lighter</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/#propdef-font-weight")]
    [SpecFact]
    public void Parses_font_weight()
    {
        var decls = Expand("font-weight: 700;");
        decls.Single().Id.Should().Be(PropertyId.FontWeight);
        decls.Single().Value.Should().Be(new CssNumber(700));
    }

    /// <summary>Spec: <see href="https://drafts.csswg.org/css-fonts-4/#propdef-font-style"/>
    /// <para>Property <c>font-style</c> — value <c>normal | italic | left | right | oblique &lt;angle [-90deg,90deg]&gt;?</c>; initial <c>normal</c>.</para>
    /// </summary>
    [Spec("css-fonts", "https://drafts.csswg.org/css-fonts-4/#propdef-font-style")]
    [SpecFact]
    public void Parses_font_style()
    {
        var decls = Expand("font-style: italic;");
        decls.Single().Id.Should().Be(PropertyId.FontStyle);
        decls.Single().Value.Should().Be(new CssKeyword("italic"));
    }

}
