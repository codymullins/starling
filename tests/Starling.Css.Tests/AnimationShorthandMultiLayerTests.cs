using FluentAssertions;
using Starling.Css.Animations;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Dom;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-animations-1", "https://www.w3.org/TR/css-animations-1/")]

[TestClass]
public sealed class AnimationShorthandMultiLayerTests
{
    private static List<PropertyDeclaration> ParseShorthand(string source)
    {
        var sheet = new CssParser($"x {{ animation: {source} }}").ParseStyleSheet(StyleOrigin.Author);
        var rule = (StyleRule)sheet.Rules[0];
        return PropertyRegistry.Parse(rule.Declarations[0]).ToList();
    }

    private static CssValue ValueOf(List<PropertyDeclaration> decls, PropertyId id)
        => decls.First(d => d.Id == id).Value;

    [TestMethod]
    public void Single_layer_round_trips_through_longhands()
    {
        var decls = ParseShorthand("fade 1s linear infinite");
        ValueOf(decls, PropertyId.AnimationName).Should().BeOfType<CssKeyword>()
            .Which.Name.Should().Be("fade");
        ((CssTime)ValueOf(decls, PropertyId.AnimationDuration)).InSeconds.Should().Be(1);
        ((CssKeyword)ValueOf(decls, PropertyId.AnimationTimingFunction)).Name.Should().Be("linear");
        ((CssKeyword)ValueOf(decls, PropertyId.AnimationIterationCount)).Name.Should().Be("infinite");
    }

    [TestMethod]
    public void Two_layers_produce_parallel_value_lists()
    {
        var decls = ParseShorthand("a 1s, b 2s linear infinite");
        var names = (CssValueList)ValueOf(decls, PropertyId.AnimationName);
        names.Values.Should().HaveCount(2);
        ((CssKeyword)names.Values[0]).Name.Should().Be("a");
        ((CssKeyword)names.Values[1]).Name.Should().Be("b");

        var durations = (CssValueList)ValueOf(decls, PropertyId.AnimationDuration);
        ((CssTime)durations.Values[0]).InSeconds.Should().Be(1);
        ((CssTime)durations.Values[1]).InSeconds.Should().Be(2);

        var timings = (CssValueList)ValueOf(decls, PropertyId.AnimationTimingFunction);
        ((CssKeyword)timings.Values[0]).Name.Should().Be("ease"); // default
        ((CssKeyword)timings.Values[1]).Name.Should().Be("linear");
    }

    [TestMethod]
    public void BuildDeclarations_zips_with_cycle_on_short_lists()
    {
        var style = ComputeStyle(
            "animation-name: a, b, c; animation-duration: 1s, 2s");
        var built = AnimationCompositor.BuildDeclarations(style);
        built.Should().HaveCount(3);
        built[0].Name.Should().Be("a");
        built[0].DurationMs.Should().Be(1000);
        built[1].Name.Should().Be("b");
        built[1].DurationMs.Should().Be(2000);
        built[2].Name.Should().Be("c");
        built[2].DurationMs.Should().Be(1000); // cycled
    }

    [TestMethod]
    public void BuildDeclarations_skips_name_none_layers()
    {
        var style = ComputeStyle("animation-name: a, none, b");
        var built = AnimationCompositor.BuildDeclarations(style);
        built.Select(b => b.Name).Should().Equal("a", "b");
    }

    [TestMethod]
    public void BuildDeclarations_parses_iteration_infinite_and_keywords()
    {
        var style = ComputeStyle(
            "animation: fade 1s ease-in infinite reverse forwards paused");
        var built = AnimationCompositor.BuildDeclarations(style);
        built.Should().HaveCount(1);
        var d = built[0];
        d.IterationCount.Should().Be(double.PositiveInfinity);
        d.Direction.Should().Be(AnimationDirection.Reverse);
        d.FillMode.Should().Be(AnimationFillMode.Forwards);
        d.PlayState.Should().Be(AnimationPlayState.Paused);
    }

    [TestMethod]
    public void Two_durations_in_one_layer_map_to_duration_and_delay()
    {
        var decls = ParseShorthand("fade 1s 250ms");
        ((CssTime)ValueOf(decls, PropertyId.AnimationDuration)).InSeconds.Should().Be(1);
        ((CssTime)ValueOf(decls, PropertyId.AnimationDelay)).Value.Should().Be(250);
    }

    private static ComputedStyle ComputeStyle(string declarations)
    {
        var engine = new StyleEngine(includeUserAgentStyleSheet: false);
        engine.AddStyleSheet(new CssParser($"x {{ {declarations} }}")
            .ParseStyleSheet(StyleOrigin.Author));
        var doc = new Document();
        var el = doc.CreateElement("x");
        doc.AppendChild(el);
        return engine.Compute(el);
    }
}
