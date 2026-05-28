using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssFilterEffects1;

/// <summary>
/// Property + cascade conformance for
/// <see href="https://www.w3.org/TR/filter-effects-1/">Filter Effects 1</see>.
/// <para>
/// `filter` / `backdrop-filter` have no dedicated longhand parser. They fall
/// through the generic value path: a single component becomes that value, and a
/// space-separated chain becomes a <see cref="CssValueList"/>. Each filter
/// function tokenizes to a <see cref="CssFunctionValue"/> whose Name is the
/// lowercased function name and whose Arguments are comma-split, so a
/// space-separated argument list collapses to one <see cref="CssValueList"/>
/// argument. These tests assert that observed shape, not a future typed model.
/// </para>
/// </summary>
[TestClass]
[Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/")]
public sealed class FilterTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue FilterValue(string css)
        => Expand(css).Single(d => d.Id == PropertyId.Filter).Value;

    private static CssFunctionValue SingleFunction(string css)
        => FilterValue(css).Should().BeOfType<CssFunctionValue>().Subject;

    // ---- none / initial / inheritance ----

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "1")]
    [SpecFact]
    public void Filter_none_parses_as_keyword()
        => FilterValue("filter: none;").Should().Be(new CssKeyword("none"));

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "1")]
    [SpecFact]
    public void Filter_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Filter).Should().Be(new CssKeyword("none"));

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "1")]
    [SpecFact]
    public void Filter_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.Filter).Should().BeFalse();

    // ---- §1 filter functions ----

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-blur", section: "1")]
    [SpecFact]
    public void Blur_takes_a_length()
    {
        var fn = SingleFunction("filter: blur(5px);");
        fn.Name.Should().Be("blur");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-brightness", section: "1")]
    [SpecFact]
    public void Brightness_takes_a_number()
    {
        var fn = SingleFunction("filter: brightness(0.5);");
        fn.Name.Should().Be("brightness");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssNumber(0.5));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-contrast", section: "1")]
    [SpecFact]
    public void Contrast_takes_a_percentage()
    {
        var fn = SingleFunction("filter: contrast(150%);");
        fn.Name.Should().Be("contrast");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssPercentage(150));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-grayscale", section: "1")]
    [SpecFact]
    public void Grayscale_takes_a_percentage()
    {
        var fn = SingleFunction("filter: grayscale(100%);");
        fn.Name.Should().Be("grayscale");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssPercentage(100));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-sepia", section: "1")]
    [SpecFact]
    public void Sepia_takes_a_percentage()
    {
        var fn = SingleFunction("filter: sepia(60%);");
        fn.Name.Should().Be("sepia");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssPercentage(60));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-saturate", section: "1")]
    [SpecFact]
    public void Saturate_takes_a_number()
    {
        var fn = SingleFunction("filter: saturate(2);");
        fn.Name.Should().Be("saturate");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssNumber(2));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-hue-rotate", section: "1")]
    [SpecFact]
    public void Hue_rotate_takes_an_angle()
    {
        var fn = SingleFunction("filter: hue-rotate(90deg);");
        fn.Name.Should().Be("hue-rotate");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssAngle(90, CssAngleUnit.Degrees));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-invert", section: "1")]
    [SpecFact]
    public void Invert_takes_a_number()
    {
        var fn = SingleFunction("filter: invert(1);");
        fn.Name.Should().Be("invert");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssNumber(1));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-opacity", section: "1")]
    [SpecFact]
    public void Opacity_filter_takes_a_number()
    {
        var fn = SingleFunction("filter: opacity(0.5);");
        fn.Name.Should().Be("opacity");
        fn.Arguments.Should().ContainSingle()
            .Which.Should().Be(new CssNumber(0.5));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#funcdef-filter-drop-shadow", section: "1")]
    [SpecFact]
    public void Drop_shadow_keeps_offsets_blur_and_color()
    {
        var fn = SingleFunction("filter: drop-shadow(2px 2px 4px black);");
        fn.Name.Should().Be("drop-shadow");
        // Comma-split leaves one argument; the space-separated parts collapse
        // into a single CssValueList of offset-x, offset-y, blur, color.
        var args = fn.Arguments.Should().ContainSingle()
            .Which.Should().BeOfType<CssValueList>().Subject;
        args.Values.Should().HaveCount(4);
        args.Values[0].Should().Be(new CssLength(2, CssLengthUnit.Px));
        args.Values[1].Should().Be(new CssLength(2, CssLengthUnit.Px));
        args.Values[2].Should().Be(new CssLength(4, CssLengthUnit.Px));
        args.Values[3].Should().Be(Starling.Css.Values.CssColor.Black);
    }

    // ---- chained list ----

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#typedef-filter-value-list", section: "1")]
    [SpecFact]
    public void Chained_functions_keep_order()
    {
        var list = FilterValue("filter: blur(2px) brightness(0.8);")
            .Should().BeOfType<CssValueList>().Subject;
        list.Values.Should().HaveCount(2);

        var blur = list.Values[0].Should().BeOfType<CssFunctionValue>().Subject;
        blur.Name.Should().Be("blur");
        blur.Arguments.Single().Should().Be(new CssLength(2, CssLengthUnit.Px));

        var brightness = list.Values[1].Should().BeOfType<CssFunctionValue>().Subject;
        brightness.Name.Should().Be("brightness");
        brightness.Arguments.Single().Should().Be(new CssNumber(0.8));
    }

    // ---- url() reference form ----

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#FilterProperty", section: "1")]
    [SpecFact]
    public void Url_reference_parses_as_css_url()
        => FilterValue("filter: url(#f);").Should().Be(new CssUrl("#f"));

    // ---- backdrop-filter ----

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#BackdropFilterProperty", section: "1")]
    [SpecFact]
    public void Backdrop_filter_blur_takes_a_length()
    {
        var fn = Expand("backdrop-filter: blur(10px);")
            .Single(d => d.Id == PropertyId.BackdropFilter).Value
            .Should().BeOfType<CssFunctionValue>().Subject;
        fn.Name.Should().Be("blur");
        fn.Arguments.Single().Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#BackdropFilterProperty", section: "1")]
    [SpecFact]
    public void Backdrop_filter_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.BackdropFilter).Should().Be(new CssKeyword("none"));

    [Spec("css-filter-effects-1", "https://www.w3.org/TR/filter-effects-1/#BackdropFilterProperty", section: "1")]
    [SpecFact]
    public void Backdrop_filter_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.BackdropFilter).Should().BeFalse();
}
