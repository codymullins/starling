using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssScrollbars1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/css-scrollbars-1/">CSS Scrollbars Styling Module Level 1</see>:
/// the <c>scrollbar-width</c> and <c>scrollbar-color</c> properties.
/// </summary>
[TestClass]
[Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/")]
public sealed class ScrollbarPropertiesTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ----- scrollbar-width (§3) -----

    [Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/#scrollbar-width", section: "3")]
    [SpecFact]
    public void Scrollbar_width_parses_auto_thin_none()
    {
        ValueOf("scrollbar-width: auto", PropertyId.ScrollbarWidth).Should().Be(new CssKeyword("auto"));
        ValueOf("scrollbar-width: thin", PropertyId.ScrollbarWidth).Should().Be(new CssKeyword("thin"));
        ValueOf("scrollbar-width: none", PropertyId.ScrollbarWidth).Should().Be(new CssKeyword("none"));
    }

    [Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/#scrollbar-width", section: "3")]
    [SpecFact]
    public void Scrollbar_width_initial_is_auto_and_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.ScrollbarWidth).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.Inherits(PropertyId.ScrollbarWidth).Should().BeTrue();
    }

    // ----- scrollbar-color (§4) -----

    [Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/#scrollbar-color", section: "4")]
    [SpecFact]
    public void Scrollbar_color_parses_auto()
        => ValueOf("scrollbar-color: auto", PropertyId.ScrollbarColor).Should().Be(new CssKeyword("auto"));

    [Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/#scrollbar-color", section: "4")]
    [SpecFact]
    public void Scrollbar_color_parses_thumb_then_track_color_pair()
    {
        // §4: `scrollbar-color: <color>{2}` — first is the thumb, second the track.
        var value = ValueOf("scrollbar-color: red blue", PropertyId.ScrollbarColor);
        var list = value.Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().BeOfType<Starling.Css.Values.CssColor>();
        list.Values[1].Should().BeOfType<Starling.Css.Values.CssColor>();
    }

    [Spec("css-scrollbars-1", "https://www.w3.org/TR/css-scrollbars-1/#scrollbar-color", section: "4")]
    [SpecFact]
    public void Scrollbar_color_initial_is_auto_and_inherited()
    {
        PropertyRegistry.InitialValue(PropertyId.ScrollbarColor).Should().Be(new CssKeyword("auto"));
        PropertyRegistry.Inherits(PropertyId.ScrollbarColor).Should().BeTrue();
    }
}
