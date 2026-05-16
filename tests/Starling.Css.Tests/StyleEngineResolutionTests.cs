using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Media;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class StyleEngineResolutionTests
{
    private static (Document doc, Element el, StyleEngine engine) Setup(string css)
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return (doc, el, engine);
    }

    [Fact]
    public void Em_resolves_against_computed_font_size()
    {
        var (_, el, engine) = Setup("div { font-size: 20px; margin: 2em; }");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.MarginTop).Should().Be(new CssLength(40, CssLengthUnit.Px));
    }

    [Fact]
    public void Rem_resolves_against_root_font_size()
    {
        var (_, el, engine) = Setup("div { font-size: 32px; padding: 1rem; }");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.PaddingTop).Should().Be(new CssLength(16, CssLengthUnit.Px));
    }

    [Fact]
    public void Vw_resolves_against_viewport()
    {
        var (_, el, engine) = Setup("div { width: 50vw; }");
        engine.MediaContext = MediaContext.Default with { ViewportWidthPx = 1000 };
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Should().Be(new CssLength(500, CssLengthUnit.Px));
    }

    [Fact]
    public void Vh_resolves_against_viewport()
    {
        var (_, el, engine) = Setup("div { height: 100vh; }");
        engine.MediaContext = MediaContext.Default with { ViewportHeightPx = 800 };
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Height).Should().Be(new CssLength(800, CssLengthUnit.Px));
    }

    [Fact]
    public void Lh_resolves_against_line_height()
    {
        // line-height defaults to font-size * 1.2 in our context — 16 * 1.2 = 19.2
        var (_, el, engine) = Setup("div { padding-top: 1lh; }");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.PaddingTop).Value.Should().BeApproximately(19.2, 0.001);
    }

    [Fact]
    public void Calc_with_vh_and_px_resolves_to_pixels()
    {
        var (_, el, engine) = Setup("div { height: calc(100vh - 80px); }");
        engine.MediaContext = MediaContext.Default with { ViewportHeightPx = 800 };
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Height).Should().Be(new CssLength(720, CssLengthUnit.Px));
    }

    [Fact]
    public void Calc_with_em_resolves_to_pixels()
    {
        var (_, el, engine) = Setup("div { font-size: 20px; margin: calc(2em + 10px); }");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.MarginTop).Value.Should().Be(50);
    }

    [Fact]
    public void Min_max_clamp_resolve_through_engine()
    {
        var (_, el, engine) = Setup("div { width: clamp(100px, 50vw, 300px); }");
        engine.MediaContext = MediaContext.Default with { ViewportWidthPx = 200 };
        var style = engine.Compute(el);
        // 50vw at vw=200 = 100, so clamp(100, 100, 300) = 100
        style.GetLength(PropertyId.Width).Should().Be(new CssLength(100, CssLengthUnit.Px));
    }

    [Fact]
    public void Percentage_stays_symbolic_at_cascade_time()
    {
        var (_, el, engine) = Setup("div { width: 50%; }");
        var style = engine.Compute(el);
        // Without containing-block basis, percentage must NOT collapse to a length.
        style.Get(PropertyId.Width).Should().BeOfType<CssPercentage>();
    }

    [Fact]
    public void Attr_px_unit_resolves_against_element_attribute()
    {
        var (_, el, engine) = Setup("div { width: attr(data-w px); }");
        el.SetAttribute("data-w", "150");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Should().Be(new CssLength(150, CssLengthUnit.Px));
    }

    [Fact]
    public void Attr_uses_fallback_when_attribute_missing()
    {
        var (_, el, engine) = Setup("div { width: attr(data-w px, 200px); }");
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Should().Be(new CssLength(200, CssLengthUnit.Px));
    }

    [Fact]
    public void Attr_with_color_type_resolves()
    {
        var (_, el, engine) = Setup("div { color: attr(data-color color, black); }");
        el.SetAttribute("data-color", "red");
        var style = engine.Compute(el);
        style.GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [Fact]
    public void Font_size_em_resolves_against_parent_font_size()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        parent.AppendChild(child);
        doc.AppendChild(parent);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { font-size: 20px; }
            p { font-size: 2em; }
            """));

        var style = engine.Compute(child);
        style.GetLength(PropertyId.FontSize).Should().Be(new CssLength(40, CssLengthUnit.Px));
    }

    [Fact]
    public void Em_in_child_resolves_against_own_font_size_not_parent()
    {
        var doc = new Document();
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        parent.AppendChild(child);
        doc.AppendChild(parent);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            div { font-size: 20px; }
            p { font-size: 30px; margin: 1em; }
            """));

        var style = engine.Compute(child);
        // margin's em should use 30px, not 20px
        style.GetLength(PropertyId.MarginTop).Should().Be(new CssLength(30, CssLengthUnit.Px));
    }

    [Fact]
    public void Cqw_resolves_against_container_context()
    {
        var (_, el, engine) = Setup("div { width: 50cqw; }");
        engine.MediaContext = MediaContext.Default with { ViewportWidthPx = 400 };
        var style = engine.Compute(el);
        // With no real containment, the engine falls back to viewport as container.
        style.GetLength(PropertyId.Width).Value.Should().Be(200);
    }

    [Fact]
    public void Dvh_resolves_separately_from_vh()
    {
        var (_, el, engine) = Setup("div { height: 100dvh; }");
        engine.MediaContext = MediaContext.Default with { ViewportHeightPx = 600 };
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Height).Value.Should().Be(600);
    }
}
