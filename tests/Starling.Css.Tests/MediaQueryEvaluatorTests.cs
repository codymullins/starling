using FluentAssertions;
using Starling.Css.Cascade;
using Starling.Css.Media;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("mediaqueries-5", "https://www.w3.org/TR/mediaqueries-5/")]

[TestClass]
public sealed class MediaQueryEvaluatorTests
{
    private static MediaQueryList Parse(string query)
    {
        var sheet = CssParser.ParseStyleSheet($"@media {query} {{ }}");
        var at = sheet.Rules.OfType<AtRule>().Single();
        return MediaQueryParser.ParseList(at.Prelude);
    }

    [TestMethod]
    public void Min_width_matches_when_viewport_is_wider()
    {
        var list = Parse("(min-width: 400px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 300)).Should().BeFalse();
    }

    [TestMethod]
    public void Max_width_inverts_min_width()
    {
        var list = Parse("(max-width: 600px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 700)).Should().BeFalse();
    }

    [TestMethod]
    public void Range_syntax_greater_or_equal_works()
    {
        var list = Parse("(width >= 400px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 400)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 399)).Should().BeFalse();
    }

    [TestMethod]
    public void Range_syntax_double_bounds_match()
    {
        var list = Parse("(400px <= width <= 800px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 800)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 900)).Should().BeFalse();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 300)).Should().BeFalse();
    }

    [TestMethod]
    public void Orientation_portrait_matches_when_height_ge_width()
    {
        var list = Parse("(orientation: portrait)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 800)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 800, ViewportHeightPx: 400)).Should().BeFalse();
    }

    [TestMethod]
    public void Prefers_color_scheme_dark_matches_dark_context()
    {
        var list = Parse("(prefers-color-scheme: dark)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ColorScheme: ColorScheme.Dark)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ColorScheme: ColorScheme.Light)).Should().BeFalse();
    }

    [TestMethod]
    public void Not_inverts_query()
    {
        var list = Parse("not (min-width: 400px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 300)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500)).Should().BeFalse();
    }

    [TestMethod]
    public void Only_keyword_requires_media_type_to_match()
    {
        var list = Parse("only screen and (min-width: 400px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(MediaType: "screen", ViewportWidthPx: 500)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(MediaType: "print", ViewportWidthPx: 500)).Should().BeFalse();
    }

    [TestMethod]
    public void And_requires_all_conditions()
    {
        var list = Parse("(min-width: 400px) and (orientation: landscape)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 300)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 500, ViewportHeightPx: 800)).Should().BeFalse();
    }

    [TestMethod]
    public void Or_requires_any_condition()
    {
        var list = Parse("(min-width: 1000px) or (orientation: portrait)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 800)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 400, ViewportHeightPx: 300)).Should().BeFalse();
    }

    [TestMethod]
    public void Comma_list_is_or()
    {
        var list = Parse("(min-width: 1000px), (max-width: 400px)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 300)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 1200)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 600)).Should().BeFalse();
    }

    [TestMethod]
    public void Aspect_ratio_compares_ratio_value()
    {
        var list = Parse("(min-aspect-ratio: 16/9)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 1920, ViewportHeightPx: 1080)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(ViewportWidthPx: 800, ViewportHeightPx: 600)).Should().BeFalse();
    }

    [TestMethod]
    public void Resolution_dppx_units()
    {
        var list = Parse("(min-resolution: 2dppx)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Resolution: 2.0)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Resolution: 1.0)).Should().BeFalse();
    }

    [TestMethod]
    public void Hover_keyword_matches_hover_context()
    {
        var list = Parse("(hover: hover)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Hover: Hover.Hover)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Hover: Hover.None)).Should().BeFalse();
    }

    [TestMethod]
    public void Boolean_feature_works()
    {
        var list = Parse("(color)");
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Color: 8)).Should().BeTrue();
        MediaQueryEvaluator.Evaluate(list, new MediaContext(Color: 0)).Should().BeFalse();
    }

    [TestMethod]
    public void StyleEngine_skips_non_matching_media_block()
    {
        // Regression for "unconditional @media" bug.
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 400);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @media (min-width: 600px) { p { color: red; } }
            """));

        var style = engine.Compute(p);
        style.GetColor(PropertyId.Color).Should().Be(CssColor.Black,
            "the @media block requires a viewport at least 600px wide");
    }

    [TestMethod]
    public void StyleEngine_applies_matching_media_block()
    {
        var doc = new Document();
        var p = doc.CreateElement("p");
        doc.AppendChild(p);
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MediaContext = new MediaContext(ViewportWidthPx: 800);
        engine.AddStyleSheet(CssParser.ParseStyleSheet("""
            @media (min-width: 600px) { p { color: red; } }
            """));

        var style = engine.Compute(p);
        style.GetColor(PropertyId.Color).Should().Be(new CssColor(255, 0, 0));
    }

    [TestMethod]
    public void MatchMedia_string_api_works()
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.MatchMedia("(min-width: 600px)", new MediaContext(ViewportWidthPx: 800)).Should().BeTrue();
        engine.MatchMedia("(min-width: 600px)", new MediaContext(ViewportWidthPx: 400)).Should().BeFalse();
    }
}
