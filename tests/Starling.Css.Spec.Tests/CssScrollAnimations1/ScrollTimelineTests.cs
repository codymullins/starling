using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssScrollAnimations1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/scroll-animations-1/">CSS Scroll-Driven Animations Level 1</see>:
/// the scroll-timeline / view-timeline named-timeline properties.
/// Parse + cascade level — driving animations from scroll progress is not implemented.
/// </summary>
[TestClass]
[Spec("scroll-animations-1", "https://www.w3.org/TR/scroll-animations-1/")]
public sealed class ScrollTimelineTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    [Spec("scroll-animations-1", "https://www.w3.org/TR/scroll-animations-1/#scroll-timeline-name", section: "2.1")]
    [SpecFact]
    public void Scroll_timeline_name_and_axis_parse()
    {
        ValueOf("scroll-timeline-name: --t", PropertyId.ScrollTimelineName).Should().Be(new CssKeyword("--t"));
        ValueOf("scroll-timeline-axis: inline", PropertyId.ScrollTimelineAxis).Should().Be(new CssKeyword("inline"));
    }

    [Spec("scroll-animations-1", "https://www.w3.org/TR/scroll-animations-1/#scroll-timeline-shorthand", section: "2.3")]
    [SpecFact]
    public void Scroll_timeline_shorthand_sets_name_and_axis()
    {
        var decls = Expand("scroll-timeline: --t inline");
        decls.Single(d => d.Id == PropertyId.ScrollTimelineName).Value.Should().Be(new CssKeyword("--t"));
        decls.Single(d => d.Id == PropertyId.ScrollTimelineAxis).Value.Should().Be(new CssKeyword("inline"));
    }

    [Spec("scroll-animations-1", "https://www.w3.org/TR/scroll-animations-1/#view-timeline", section: "4")]
    [SpecFact]
    public void View_timeline_name_axis_and_shorthand_parse()
    {
        ValueOf("view-timeline-name: --v", PropertyId.ViewTimelineName).Should().Be(new CssKeyword("--v"));
        var decls = Expand("view-timeline: --v block");
        decls.Single(d => d.Id == PropertyId.ViewTimelineName).Value.Should().Be(new CssKeyword("--v"));
        decls.Single(d => d.Id == PropertyId.ViewTimelineAxis).Value.Should().Be(new CssKeyword("block"));
    }

    [Spec("scroll-animations-1", "https://www.w3.org/TR/scroll-animations-1/", section: "2")]
    [SpecFact]
    public void Timeline_properties_initial_values_and_not_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.ScrollTimelineName).Should().Be(new CssKeyword("none"));
        PropertyRegistry.InitialValue(PropertyId.ScrollTimelineAxis).Should().Be(new CssKeyword("block"));
        PropertyRegistry.InitialValue(PropertyId.TimelineScope).Should().Be(new CssKeyword("none"));
        PropertyRegistry.Inherits(PropertyId.ScrollTimelineName).Should().BeFalse();
        PropertyRegistry.Inherits(PropertyId.ViewTimelineName).Should().BeFalse();
    }
}
