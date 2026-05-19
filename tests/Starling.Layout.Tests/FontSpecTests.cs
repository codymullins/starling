using FluentAssertions;
using Starling.Css;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Layout.Text;
namespace Starling.Layout.Tests;

[TestClass]
public sealed class FontSpecTests
{
    [TestMethod]
    public void Numeric_font_weight_drives_wght_variation()
    {
        var spec = SpecFromCss("body { font-family: Test; font-weight: 350; }");
        spec.Variations.Should().Contain(v => v.Tag == "wght" && v.Value == 350f);
    }

    [TestMethod]
    public void Font_stretch_percentage_drives_wdth_variation()
    {
        var spec = SpecFromCss("body { font-family: Test; font-stretch: 80%; }");
        spec.Variations.Should().Contain(v => v.Tag == "wdth" && v.Value == 80f);
    }

    [TestMethod]
    public void Font_stretch_keyword_maps_to_wdth()
    {
        var spec = SpecFromCss("body { font-family: Test; font-stretch: condensed; }");
        spec.Variations.Should().Contain(v => v.Tag == "wdth" && v.Value == 75f);
    }

    [TestMethod]
    public void Explicit_variation_settings_override_derived_axes()
    {
        var spec = SpecFromCss(
            "body { font-family: Test; font-weight: 400; font-variation-settings: 'wght' 600, 'GRAD' 88; }");
        spec.Variations.Should().Contain(v => v.Tag == "wght" && v.Value == 600f);
        spec.Variations.Should().Contain(v => v.Tag == "GRAD" && v.Value == 88f);
    }

    [TestMethod]
    public void Specs_with_same_variations_are_equal()
    {
        var a = new FontSpec(["Foo"], false, false, new[] { new FontVariation("wght", 400) });
        var b = new FontSpec(["Foo"], false, false, new[] { new FontVariation("wght", 400) });
        a.Equals(b).Should().BeTrue();
        a.GetHashCode().Should().Be(b.GetHashCode());
    }

    [TestMethod]
    public void Specs_with_different_variations_are_distinct()
    {
        var a = new FontSpec(["Foo"], false, false, new[] { new FontVariation("wght", 400) });
        var b = new FontSpec(["Foo"], false, false, new[] { new FontVariation("wght", 700) });
        a.Equals(b).Should().BeFalse();
    }

    private static FontSpec SpecFromCss(string css)
    {
        var engine = new StyleEngine();
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css, StyleOrigin.Author));
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var style = engine.Compute(body, context: null);
        return FontSpec.FromStyle(style);
    }
}
