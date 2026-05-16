using FluentAssertions;
using Tessera.Css.Cascade;
using Tessera.Css.Media;
using Tessera.Css.Parser;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Tessera.Dom;
using Xunit;

namespace Tessera.Css.Tests;

public sealed class StyleEnginePunctedResolutionTests
{
    private static (Element el, StyleEngine engine) Setup(string css)
    {
        var doc = new Document();
        var el = doc.CreateElement("div");
        doc.AppendChild(el);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet(css));
        return (el, engine);
    }

    // ---------- A.5 — UsedValue / containing-block percentage resolution ----------

    [Fact]
    public void UsedValue_resolves_percentage_against_containing_block()
    {
        var (el, engine) = Setup("div { width: 50%; }");
        var style = engine.Compute(el);
        var used = style.UsedValue(PropertyId.Width, CssResolutionContext.Default with { PercentageBasisPx = 800 });
        used.Should().BeOfType<CssLength>().Which.Value.Should().Be(400);
    }

    [Fact]
    public void UsedLengthPx_resolves_percentage_directly_to_pixels()
    {
        var (el, engine) = Setup("div { width: 75%; }");
        var style = engine.Compute(el);
        var px = style.UsedLengthPx(PropertyId.Width, containingBlockPx: 400, CssResolutionContext.Default);
        px.Should().Be(300);
    }

    [Fact]
    public void UsedLengthPx_passes_through_absolute_lengths()
    {
        var (el, engine) = Setup("div { margin-left: 24px; }");
        var style = engine.Compute(el);
        var px = style.UsedLengthPx(PropertyId.MarginLeft, containingBlockPx: 1000, CssResolutionContext.Default);
        px.Should().Be(24);
    }

    [Fact]
    public void UsedValue_resolves_calc_with_percentage_at_layout_time()
    {
        var (el, engine) = Setup("div { width: calc(50% + 20px); }");
        var style = engine.Compute(el);
        var used = style.UsedValue(PropertyId.Width, CssResolutionContext.Default with { PercentageBasisPx = 600 });
        used.Should().BeOfType<CssLength>().Which.Value.Should().Be(320);
    }

    // ---------- A.6 — Real cq* via ContainerSizeLookup ----------

    [Fact]
    public void ContainerSizeLookup_supplies_cqw_size()
    {
        var doc = new Document();
        var container = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        container.AppendChild(child);
        doc.AppendChild(container);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { width: 50cqw; }"));
        engine.ContainerSizeLookup = el => el == container ? (400, 200) : null;

        var style = engine.Compute(child);
        style.GetLength(PropertyId.Width).Value.Should().Be(200);
    }

    [Fact]
    public void ContainerSizeLookup_supplies_cqh_size()
    {
        var doc = new Document();
        var container = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        container.AppendChild(child);
        doc.AppendChild(container);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { height: 50cqh; }"));
        engine.ContainerSizeLookup = el => el == container ? (400, 600) : null;

        var style = engine.Compute(child);
        style.GetLength(PropertyId.Height).Value.Should().Be(300);
    }

    [Fact]
    public void ContainerSizeLookup_cqmin_uses_smaller_dimension()
    {
        var doc = new Document();
        var container = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        container.AppendChild(child);
        doc.AppendChild(container);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { width: 100cqmin; }"));
        engine.ContainerSizeLookup = el => el == container ? (500, 200) : null;

        var style = engine.Compute(child);
        style.GetLength(PropertyId.Width).Value.Should().Be(200);
    }

    [Fact]
    public void ContainerSizeLookup_walks_up_to_nearest_container()
    {
        var doc = new Document();
        var grandparent = doc.CreateElement("section");
        var parent = doc.CreateElement("div");
        var child = doc.CreateElement("p");
        grandparent.AppendChild(parent);
        parent.AppendChild(child);
        doc.AppendChild(grandparent);

        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("p { width: 50cqw; }"));
        // parent is NOT a container, grandparent IS.
        engine.ContainerSizeLookup = el => el == grandparent ? (1000, 400) : null;

        var style = engine.Compute(child);
        style.GetLength(PropertyId.Width).Value.Should().Be(500);
    }

    [Fact]
    public void Cqw_without_lookup_falls_back_to_small_viewport()
    {
        var (el, engine) = Setup("div { width: 50cqw; }");
        engine.MediaContext = MediaContext.Default with { ViewportWidthPx = 600 };
        // No ContainerSizeLookup set — fallback to viewport.
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Value.Should().Be(300);
    }

    // ---------- A.7 — IFontMetricsProvider for ex/cap/ch/ic ----------

    private sealed class FakeFontMetrics : IFontMetricsProvider
    {
        public string? LastFamily;
        public double LastSize;
        public string? LastStyle;
        public double LastWeight;

        public FontMetrics Resolve(string fontFamily, double fontSizePx, string fontStyle, double fontWeight)
        {
            LastFamily = fontFamily;
            LastSize = fontSizePx;
            LastStyle = fontStyle;
            LastWeight = fontWeight;
            return new FontMetrics(
                XHeightPx: 9,
                CapHeightPx: 14,
                ZeroAdvancePx: 11,
                IcAdvancePx: 20);
        }
    }

    [Fact]
    public void Ex_resolves_against_provider_xheight()
    {
        var (el, engine) = Setup("div { padding-top: 2ex; }");
        engine.FontMetrics = new FakeFontMetrics();
        var style = engine.Compute(el);
        // 2 * 9 = 18
        style.GetLength(PropertyId.PaddingTop).Value.Should().Be(18);
    }

    [Fact]
    public void Cap_resolves_against_provider_capheight()
    {
        var (el, engine) = Setup("div { padding-top: 1cap; }");
        engine.FontMetrics = new FakeFontMetrics();
        var style = engine.Compute(el);
        style.GetLength(PropertyId.PaddingTop).Value.Should().Be(14);
    }

    [Fact]
    public void Ch_resolves_against_provider_zero_advance()
    {
        var (el, engine) = Setup("div { width: 4ch; }");
        engine.FontMetrics = new FakeFontMetrics();
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Value.Should().Be(44);
    }

    [Fact]
    public void Ic_resolves_against_provider_ic_advance()
    {
        var (el, engine) = Setup("div { width: 3ic; }");
        engine.FontMetrics = new FakeFontMetrics();
        var style = engine.Compute(el);
        style.GetLength(PropertyId.Width).Value.Should().Be(60);
    }

    [Fact]
    public void FontMetrics_provider_receives_cascaded_font_spec()
    {
        var (el, engine) = Setup("""
            div { font-family: "Helvetica"; font-size: 18px; font-style: italic; font-weight: 700; padding: 1ex; }
            """);
        var fake = new FakeFontMetrics();
        engine.FontMetrics = fake;
        engine.Compute(el);
        fake.LastFamily.Should().Be("Helvetica");
        fake.LastSize.Should().Be(18);
        fake.LastStyle.Should().Be("italic");
        fake.LastWeight.Should().Be(700);
    }

    [Fact]
    public void Heuristic_provider_is_the_default()
    {
        var (el, engine) = Setup("div { padding-top: 2ex; }");
        // Default provider returns font-size * 0.5 for x-height.
        var style = engine.Compute(el);
        // 2 * (16 * 0.5) = 16
        style.GetLength(PropertyId.PaddingTop).Value.Should().Be(16);
    }
}
