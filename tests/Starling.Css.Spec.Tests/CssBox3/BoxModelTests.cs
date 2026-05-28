using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssBox3;

/// <summary>
/// Conformance suite for <see href="https://www.w3.org/TR/css-box-3/">CSS Box Model Level 3</see>.
/// Covers margin and padding shorthand expansion (§4), longhand parsing, and
/// <c>box-sizing</c> (§3).
/// </summary>
[TestClass]
[Spec("css-box-3", "https://www.w3.org/TR/css-box-3/")]
public sealed class BoxModelTests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue Longhand(List<PropertyDeclaration> decls, PropertyId id)
        => decls.Single(d => d.Id == id).Value;

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: 1-value expansion
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_one_value_applies_to_all_four_sides()
    {
        // margin: 10px → all four longhands 10px.
        var decls = Expand("margin: 10px;");
        var px10 = new CssLength(10, CssLengthUnit.Px);
        Longhand(decls, PropertyId.MarginTop).Should().Be(px10);
        Longhand(decls, PropertyId.MarginRight).Should().Be(px10);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(px10);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(px10);
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: 2-value expansion (vertical / horizontal)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_two_values_first_is_vertical_second_is_horizontal()
    {
        // margin: 10px 20px → top/bottom=10px, right/left=20px.
        var decls = Expand("margin: 10px 20px;");
        var px10 = new CssLength(10, CssLengthUnit.Px);
        var px20 = new CssLength(20, CssLengthUnit.Px);
        Longhand(decls, PropertyId.MarginTop).Should().Be(px10);
        Longhand(decls, PropertyId.MarginRight).Should().Be(px20);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(px10);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(px20);
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: 3-value expansion (top / horizontal / bottom)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_three_values_top_horizontal_bottom()
    {
        // margin: 10px 20px 30px → top=10, right/left=20, bottom=30.
        var decls = Expand("margin: 10px 20px 30px;");
        var px10 = new CssLength(10, CssLengthUnit.Px);
        var px20 = new CssLength(20, CssLengthUnit.Px);
        var px30 = new CssLength(30, CssLengthUnit.Px);
        Longhand(decls, PropertyId.MarginTop).Should().Be(px10);
        Longhand(decls, PropertyId.MarginRight).Should().Be(px20);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(px30);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(px20);
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: 4-value expansion (clockwise: top right bottom left)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_four_values_clockwise_top_right_bottom_left()
    {
        // margin: 10px 20px 30px 40px → top=10, right=20, bottom=30, left=40.
        var decls = Expand("margin: 10px 20px 30px 40px;");
        Longhand(decls, PropertyId.MarginTop).Should().Be(new CssLength(10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginRight).Should().Be(new CssLength(20, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginBottom).Should().Be(new CssLength(30, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginLeft).Should().Be(new CssLength(40, CssLengthUnit.Px));
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: auto keyword
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_auto_sets_all_four_sides_to_auto()
    {
        // margin: auto → all four longhands = `auto`.
        var decls = Expand("margin: auto;");
        var auto = new CssKeyword("auto");
        Longhand(decls, PropertyId.MarginTop).Should().Be(auto);
        Longhand(decls, PropertyId.MarginRight).Should().Be(auto);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(auto);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(auto);
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: unitless zero (valid for length properties in CSS)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_zero_unitless_sets_all_sides()
    {
        // CSS allows unitless 0 for length properties. The parser preserves it
        // as CssNumber(0) rather than CssLength(0, Px).
        var decls = Expand("margin: 0;");
        var zero = new CssNumber(0);
        Longhand(decls, PropertyId.MarginTop).Should().Be(zero);
        Longhand(decls, PropertyId.MarginRight).Should().Be(zero);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(zero);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(zero);
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: negative margins (allowed by spec)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_negative_value_is_accepted()
    {
        // CSS Box Model 3 §4: negative margin values are explicitly permitted.
        var decls = Expand("margin: -10px;");
        var neg10 = new CssLength(-10, CssLengthUnit.Px);
        Longhand(decls, PropertyId.MarginTop).Should().Be(neg10);
        Longhand(decls, PropertyId.MarginRight).Should().Be(neg10);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(neg10);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(neg10);
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_two_values_negative_and_positive()
    {
        // margin: -10px 5px → top/bottom=-10px, right/left=5px.
        var decls = Expand("margin: -10px 5px;");
        Longhand(decls, PropertyId.MarginTop).Should().Be(new CssLength(-10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginRight).Should().Be(new CssLength(5, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginBottom).Should().Be(new CssLength(-10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.MarginLeft).Should().Be(new CssLength(5, CssLengthUnit.Px));
    }

    // -----------------------------------------------------------------------
    // §4 — margin shorthand: percentage
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_percentage_applies_to_all_sides()
    {
        // margin: 50% → all four sides = CssPercentage(50).
        var decls = Expand("margin: 50%;");
        var pct = new CssPercentage(50);
        Longhand(decls, PropertyId.MarginTop).Should().Be(pct);
        Longhand(decls, PropertyId.MarginRight).Should().Be(pct);
        Longhand(decls, PropertyId.MarginBottom).Should().Be(pct);
        Longhand(decls, PropertyId.MarginLeft).Should().Be(pct);
    }

    // -----------------------------------------------------------------------
    // §4 — margin longhands
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_longhand_px()
    {
        var decls = Expand("margin-top: 15px;");
        decls.Single().Id.Should().Be(PropertyId.MarginTop);
        decls.Single().Value.Should().Be(new CssLength(15, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-right", section: "4")]
    [SpecFact]
    public void Margin_right_longhand_px()
    {
        var decls = Expand("margin-right: 20px;");
        decls.Single().Id.Should().Be(PropertyId.MarginRight);
        decls.Single().Value.Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-bottom", section: "4")]
    [SpecFact]
    public void Margin_bottom_longhand_px()
    {
        var decls = Expand("margin-bottom: 25px;");
        decls.Single().Id.Should().Be(PropertyId.MarginBottom);
        decls.Single().Value.Should().Be(new CssLength(25, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-left", section: "4")]
    [SpecFact]
    public void Margin_left_longhand_px()
    {
        var decls = Expand("margin-left: 30px;");
        decls.Single().Id.Should().Be(PropertyId.MarginLeft);
        decls.Single().Value.Should().Be(new CssLength(30, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_longhand_auto()
    {
        var decls = Expand("margin-top: auto;");
        decls.Single().Value.Should().Be(new CssKeyword("auto"));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_longhand_negative()
    {
        // Negative margins are valid per CSS Box Model 3 §4.
        var decls = Expand("margin-top: -5px;");
        decls.Single().Value.Should().Be(new CssLength(-5, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_longhand_em()
    {
        var decls = Expand("margin-top: 2em;");
        decls.Single().Value.Should().Be(new CssLength(2, CssLengthUnit.Em));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_longhand_percentage()
    {
        // Percentage margins resolve against the containing block's inline size.
        var decls = Expand("margin-top: 10%;");
        decls.Single().Value.Should().Be(new CssPercentage(10));
    }

    // -----------------------------------------------------------------------
    // §4 — padding shorthand: 1/2/3/4-value expansion
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_one_value_applies_to_all_four_sides()
    {
        // padding: 10px → all four longhands 10px.
        var decls = Expand("padding: 10px;");
        var px10 = new CssLength(10, CssLengthUnit.Px);
        Longhand(decls, PropertyId.PaddingTop).Should().Be(px10);
        Longhand(decls, PropertyId.PaddingRight).Should().Be(px10);
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(px10);
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(px10);
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_two_values_vertical_horizontal()
    {
        // padding: 10px 20px → top/bottom=10px, right/left=20px.
        var decls = Expand("padding: 10px 20px;");
        Longhand(decls, PropertyId.PaddingTop).Should().Be(new CssLength(10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingRight).Should().Be(new CssLength(20, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(new CssLength(10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_three_values_top_horizontal_bottom()
    {
        // padding: 10px 20px 30px → top=10, right/left=20, bottom=30.
        var decls = Expand("padding: 10px 20px 30px;");
        Longhand(decls, PropertyId.PaddingTop).Should().Be(new CssLength(10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingRight).Should().Be(new CssLength(20, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(new CssLength(30, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_four_values_clockwise_top_right_bottom_left()
    {
        // padding: 10px 20px 30px 40px → top=10, right=20, bottom=30, left=40.
        var decls = Expand("padding: 10px 20px 30px 40px;");
        Longhand(decls, PropertyId.PaddingTop).Should().Be(new CssLength(10, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingRight).Should().Be(new CssLength(20, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(new CssLength(30, CssLengthUnit.Px));
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(new CssLength(40, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_zero_unitless_sets_all_sides()
    {
        // CSS allows unitless 0 for length properties.
        var decls = Expand("padding: 0;");
        var zero = new CssNumber(0);
        Longhand(decls, PropertyId.PaddingTop).Should().Be(zero);
        Longhand(decls, PropertyId.PaddingRight).Should().Be(zero);
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(zero);
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(zero);
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_percentage_applies_to_all_sides()
    {
        // Percentage padding resolves against the containing block's inline size.
        var decls = Expand("padding: 50%;");
        var pct = new CssPercentage(50);
        Longhand(decls, PropertyId.PaddingTop).Should().Be(pct);
        Longhand(decls, PropertyId.PaddingRight).Should().Be(pct);
        Longhand(decls, PropertyId.PaddingBottom).Should().Be(pct);
        Longhand(decls, PropertyId.PaddingLeft).Should().Be(pct);
    }

    // -----------------------------------------------------------------------
    // §4 — padding cannot be negative (CSS Box Model 3 §4)
    // The parser does NOT currently reject negative padding — it accepts the
    // value and produces a CssLength(-5, Px). This is a conformance gap.
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [PendingFact(
        "negative padding values are accepted by the parser instead of being " +
        "rejected; CSS Box Model 3 §4 requires padding to be non-negative",
        trackingWp: "wp:spec-css-box-3")]
    public void Padding_negative_shorthand_is_rejected()
    {
        // Spec: padding values must be non-negative. A negative value makes the
        // declaration invalid and the parser should produce no declarations.
        var decls = Expand("padding: -5px;");
        decls.Should().NotContain(d => d.Id == PropertyId.PaddingTop
            || d.Id == PropertyId.PaddingRight
            || d.Id == PropertyId.PaddingBottom
            || d.Id == PropertyId.PaddingLeft);
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [PendingFact(
        "negative padding-top is accepted by the parser instead of being rejected; " +
        "CSS Box Model 3 §4 requires padding to be non-negative",
        trackingWp: "wp:spec-css-box-3")]
    public void Padding_top_negative_longhand_is_rejected()
    {
        var decls = Expand("padding-top: -5px;");
        decls.Should().NotContain(d => d.Id == PropertyId.PaddingTop);
    }

    // -----------------------------------------------------------------------
    // §4 — padding longhands
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void Padding_top_longhand_px()
    {
        var decls = Expand("padding-top: 15px;");
        decls.Single().Id.Should().Be(PropertyId.PaddingTop);
        decls.Single().Value.Should().Be(new CssLength(15, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-right", section: "4")]
    [SpecFact]
    public void Padding_right_longhand_px()
    {
        var decls = Expand("padding-right: 20px;");
        decls.Single().Id.Should().Be(PropertyId.PaddingRight);
        decls.Single().Value.Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-bottom", section: "4")]
    [SpecFact]
    public void Padding_bottom_longhand_px()
    {
        var decls = Expand("padding-bottom: 25px;");
        decls.Single().Id.Should().Be(PropertyId.PaddingBottom);
        decls.Single().Value.Should().Be(new CssLength(25, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-left", section: "4")]
    [SpecFact]
    public void Padding_left_longhand_px()
    {
        var decls = Expand("padding-left: 30px;");
        decls.Single().Id.Should().Be(PropertyId.PaddingLeft);
        decls.Single().Value.Should().Be(new CssLength(30, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void Padding_top_longhand_em()
    {
        var decls = Expand("padding-top: 2em;");
        decls.Single().Value.Should().Be(new CssLength(2, CssLengthUnit.Em));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void Padding_top_longhand_percentage()
    {
        // Percentage padding resolves against the containing block's inline size.
        var decls = Expand("padding-top: 10%;");
        decls.Single().Value.Should().Be(new CssPercentage(10));
    }

    // -----------------------------------------------------------------------
    // §3 — box-sizing
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-box-sizing", section: "3")]
    [SpecFact]
    public void BoxSizing_parses_content_box()
    {
        // content-box is the CSS default: width/height set the content area.
        var decls = Expand("box-sizing: content-box;");
        decls.Single().Id.Should().Be(PropertyId.BoxSizing);
        decls.Single().Value.Should().Be(new CssKeyword("content-box"));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-box-sizing", section: "3")]
    [SpecFact]
    public void BoxSizing_parses_border_box()
    {
        // border-box: width/height include border and padding.
        var decls = Expand("box-sizing: border-box;");
        decls.Single().Id.Should().Be(PropertyId.BoxSizing);
        decls.Single().Value.Should().Be(new CssKeyword("border-box"));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-box-sizing", section: "3")]
    [SpecFact]
    public void BoxSizing_initial_is_content_box()
    {
        // CSS Box Model 3 §3: initial value is `content-box`.
        PropertyRegistry.InitialValue(PropertyId.BoxSizing)
            .Should().Be(new CssKeyword("content-box"));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-box-sizing", section: "3")]
    [SpecFact]
    public void BoxSizing_is_not_inherited()
    {
        // CSS Box Model 3 §3: Inherited: no.
        PropertyRegistry.Inherits(PropertyId.BoxSizing).Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // §4 — initial values for margin longhands (all 0)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void MarginTop_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MarginTop)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-right", section: "4")]
    [SpecFact]
    public void MarginRight_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MarginRight)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-bottom", section: "4")]
    [SpecFact]
    public void MarginBottom_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MarginBottom)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-left", section: "4")]
    [SpecFact]
    public void MarginLeft_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.MarginLeft)
            .Should().Be(CssLength.Zero);

    // -----------------------------------------------------------------------
    // §4 — initial values for padding longhands (all 0)
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void PaddingTop_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.PaddingTop)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-right", section: "4")]
    [SpecFact]
    public void PaddingRight_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.PaddingRight)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-bottom", section: "4")]
    [SpecFact]
    public void PaddingBottom_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.PaddingBottom)
            .Should().Be(CssLength.Zero);

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-left", section: "4")]
    [SpecFact]
    public void PaddingLeft_initial_value_is_zero()
        => PropertyRegistry.InitialValue(PropertyId.PaddingLeft)
            .Should().Be(CssLength.Zero);

    // -----------------------------------------------------------------------
    // §4 — margin longhands are not inherited
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void MarginTop_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MarginTop).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-right", section: "4")]
    [SpecFact]
    public void MarginRight_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MarginRight).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-bottom", section: "4")]
    [SpecFact]
    public void MarginBottom_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MarginBottom).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-left", section: "4")]
    [SpecFact]
    public void MarginLeft_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.MarginLeft).Should().BeFalse();

    // -----------------------------------------------------------------------
    // §4 — padding longhands are not inherited
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void PaddingTop_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.PaddingTop).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-right", section: "4")]
    [SpecFact]
    public void PaddingRight_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.PaddingRight).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-bottom", section: "4")]
    [SpecFact]
    public void PaddingBottom_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.PaddingBottom).Should().BeFalse();

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-left", section: "4")]
    [SpecFact]
    public void PaddingLeft_is_not_inherited()
        => PropertyRegistry.Inherits(PropertyId.PaddingLeft).Should().BeFalse();

    // -----------------------------------------------------------------------
    // §4 — length unit coverage for margin-top
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_parses_rem_unit()
    {
        var decls = Expand("margin-top: 1.5rem;");
        decls.Single().Value.Should().Be(new CssLength(1.5, CssLengthUnit.Rem));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin-top", section: "4")]
    [SpecFact]
    public void Margin_top_parses_zero_px()
    {
        var decls = Expand("margin-top: 0px;");
        decls.Single().Value.Should().Be(new CssLength(0, CssLengthUnit.Px));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void Padding_top_parses_rem_unit()
    {
        var decls = Expand("padding-top: 1.5rem;");
        decls.Single().Value.Should().Be(new CssLength(1.5, CssLengthUnit.Rem));
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding-top", section: "4")]
    [SpecFact]
    public void Padding_top_parses_zero_px()
    {
        var decls = Expand("padding-top: 0px;");
        decls.Single().Value.Should().Be(new CssLength(0, CssLengthUnit.Px));
    }

    // -----------------------------------------------------------------------
    // §4 — shorthand produces exactly four longhand declarations
    // -----------------------------------------------------------------------

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-margin", section: "4")]
    [SpecFact]
    public void Margin_shorthand_produces_exactly_four_declarations()
    {
        var decls = Expand("margin: 10px 20px 30px 40px;");
        decls.Should().HaveCount(4);
        decls.Select(d => d.Id).Should().BeEquivalentTo(
        [
            PropertyId.MarginTop,
            PropertyId.MarginRight,
            PropertyId.MarginBottom,
            PropertyId.MarginLeft,
        ]);
    }

    [Spec("css-box-3", "https://www.w3.org/TR/css-box-3/#propdef-padding", section: "4")]
    [SpecFact]
    public void Padding_shorthand_produces_exactly_four_declarations()
    {
        var decls = Expand("padding: 10px 20px 30px 40px;");
        decls.Should().HaveCount(4);
        decls.Select(d => d.Id).Should().BeEquivalentTo(
        [
            PropertyId.PaddingTop,
            PropertyId.PaddingRight,
            PropertyId.PaddingBottom,
            PropertyId.PaddingLeft,
        ]);
    }
}
