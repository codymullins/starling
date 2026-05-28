using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssTransforms2;

/// <summary>
/// Property + cascade conformance for the Level 2 additions of
/// <see href="https://www.w3.org/TR/css-transforms-2/">CSS Transforms 2</see> —
/// the individual transform properties (§3), 3D transform functions (§4), and the
/// <c>perspective</c> / <c>perspective-origin</c> properties. Level 1 parsing
/// (2D functions, <c>matrix()</c>, the <c>transform</c> function list) is already
/// covered by <c>CssTransformParserTests</c> and <c>TransformPropertyTests</c>
/// and is not duplicated here.
/// </summary>
[TestClass]
[Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/")]
public sealed class CssTransforms2Tests
{
    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    // ----- §3 Individual transform properties: translate -----

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_single_length()
    {
        var value = ValueOf("translate: 10px;", PropertyId.Translate);
        value.Should().Be(new CssLength(10, CssLengthUnit.Px));
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_two_lengths()
    {
        var value = ValueOf("translate: 10px 20px;", PropertyId.Translate);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssLength(10, CssLengthUnit.Px));
        list.Values[1].Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_three_lengths_includes_z()
    {
        var value = ValueOf("translate: 1px 2px 3px;", PropertyId.Translate);
        value.Should().BeOfType<CssValueList>();
        ((CssValueList)value).Values.Should().HaveCount(3);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_accepts_percentages()
    {
        var value = ValueOf("translate: 50% 25%;", PropertyId.Translate);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values[0].Should().Be(new CssPercentage(50));
        list.Values[1].Should().Be(new CssPercentage(25));
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_none_keyword()
        => ValueOf("translate: none;", PropertyId.Translate).Should().Be(new CssKeyword("none"));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Translate_property_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Translate).Should().Be(new CssKeyword("none"));

    // ----- §3 Individual transform properties: scale -----

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Scale_property_single_number()
        => ValueOf("scale: 2;", PropertyId.Scale).Should().Be(new CssNumber(2));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Scale_property_two_numbers()
    {
        var value = ValueOf("scale: 2 0.5;", PropertyId.Scale);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssNumber(2));
        list.Values[1].Should().Be(new CssNumber(0.5));
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Scale_property_three_numbers_includes_z()
    {
        var value = ValueOf("scale: 1 2 3;", PropertyId.Scale);
        value.Should().BeOfType<CssValueList>();
        ((CssValueList)value).Values.Should().HaveCount(3);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Scale_property_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Scale).Should().Be(new CssKeyword("none"));

    // ----- §3 Individual transform properties: rotate -----

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Rotate_property_bare_angle()
    {
        var value = ValueOf("rotate: 45deg;", PropertyId.Rotate);
        value.Should().BeOfType<CssAngle>();
        ((CssAngle)value).InDegrees.Should().BeApproximately(45, 1e-9);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Rotate_property_named_axis_and_angle()
    {
        // rotate: x 45deg names the rotation axis before the angle.
        var value = ValueOf("rotate: x 45deg;", PropertyId.Rotate);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssKeyword("x"));
        list.Values[1].Should().BeOfType<CssAngle>();
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Rotate_property_vector_axis_and_angle()
    {
        // rotate: 1 1 1 45deg names a vector axis before the angle.
        var value = ValueOf("rotate: 1 1 1 45deg;", PropertyId.Rotate);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(4);
        list.Values[3].Should().BeOfType<CssAngle>();
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#individual-transforms", section: "3")]
    [SpecFact]
    public void Rotate_property_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Rotate).Should().Be(new CssKeyword("none"));

    // ----- §4 3D transform functions inside `transform` -----
    // These functions are accepted syntactically by the value parser as generic
    // CssFunctionValues (the matrix-resolving CssTransformParser still rejects
    // them, see CssTransformParserTests). These tests assert the parse shape.

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_translate3d_parses_as_function()
    {
        var value = ValueOf("transform: translate3d(1px, 2px, 3px);", PropertyId.Transform);
        value.Should().BeOfType<CssFunctionValue>();
        var fn = (CssFunctionValue)value;
        fn.Name.Should().Be("translate3d");
        fn.Arguments.Should().HaveCount(3);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_translateZ_parses_as_function()
    {
        var fn = (CssFunctionValue)ValueOf("transform: translateZ(5px);", PropertyId.Transform);
        fn.Name.Should().Be("translatez");
        fn.Arguments.Should().HaveCount(1);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_scale3d_parses_as_function()
    {
        var fn = (CssFunctionValue)ValueOf("transform: scale3d(2, 3, 4);", PropertyId.Transform);
        fn.Name.Should().Be("scale3d");
        fn.Arguments.Should().HaveCount(3);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_scaleZ_parses_as_function()
    {
        var fn = (CssFunctionValue)ValueOf("transform: scaleZ(2);", PropertyId.Transform);
        fn.Name.Should().Be("scalez");
        fn.Arguments.Should().HaveCount(1);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_rotate3d_parses_as_function()
    {
        var fn = (CssFunctionValue)ValueOf("transform: rotate3d(1, 1, 1, 45deg);", PropertyId.Transform);
        fn.Name.Should().Be("rotate3d");
        fn.Arguments.Should().HaveCount(4);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_rotateX_Y_Z_parse_as_functions()
    {
        ((CssFunctionValue)ValueOf("transform: rotateX(45deg);", PropertyId.Transform)).Name.Should().Be("rotatex");
        ((CssFunctionValue)ValueOf("transform: rotateY(45deg);", PropertyId.Transform)).Name.Should().Be("rotatey");
        ((CssFunctionValue)ValueOf("transform: rotateZ(45deg);", PropertyId.Transform)).Name.Should().Be("rotatez");
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_perspective_function_parses()
    {
        var fn = (CssFunctionValue)ValueOf("transform: perspective(500px);", PropertyId.Transform);
        fn.Name.Should().Be("perspective");
        fn.Arguments.Should().HaveCount(1);
        fn.Arguments[0].Should().Be(new CssLength(500, CssLengthUnit.Px));
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_matrix3d_parses_with_sixteen_args()
    {
        var fn = (CssFunctionValue)ValueOf(
            "transform: matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1);",
            PropertyId.Transform);
        fn.Name.Should().Be("matrix3d");
        fn.Arguments.Should().HaveCount(16);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#three-d-transform-functions", section: "4")]
    [SpecFact]
    public void Transform_mixed_2d_and_3d_function_list_parses()
    {
        var value = ValueOf("transform: rotateX(45deg) translate(10px, 20px);", PropertyId.Transform);
        value.Should().BeOfType<CssValueList>();
        var list = (CssValueList)value;
        list.Values.Should().HaveCount(2);
        ((CssFunctionValue)list.Values[0]).Name.Should().Be("rotatex");
        ((CssFunctionValue)list.Values[1]).Name.Should().Be("translate");
    }

    // ----- §6 perspective / perspective-origin -----

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#perspective-property", section: "6.1")]
    [SpecFact]
    public void Perspective_none_keyword()
        => ValueOf("perspective: none;", PropertyId.Perspective).Should().Be(new CssKeyword("none"));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#perspective-property", section: "6.1")]
    [SpecFact]
    public void Perspective_length()
        => ValueOf("perspective: 800px;", PropertyId.Perspective).Should().Be(new CssLength(800, CssLengthUnit.Px));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#perspective-property", section: "6.1")]
    [SpecFact]
    public void Perspective_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Perspective).Should().Be(new CssKeyword("none"));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#perspective-origin-property", section: "6.2")]
    [SpecFact]
    public void Perspective_origin_position()
    {
        var value = ValueOf("perspective-origin: 25% 75%;", PropertyId.PerspectiveOrigin);
        value.Should().BeOfType<CssValueList>();
        ((CssValueList)value).Values.Should().HaveCount(2);
    }

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#perspective-origin-property", section: "6.2")]
    [SpecFact]
    public void Perspective_origin_initial_value()
        => PropertyRegistry.InitialValue(PropertyId.PerspectiveOrigin).Should().Be(new CssKeyword("50% 50%"));

    // ----- §3.1 transform-box (used by individual transforms) -----

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#transform-box", section: "3.1")]
    [SpecFact]
    public void Transform_box_keyword_parses()
        => ValueOf("transform-box: fill-box;", PropertyId.TransformBox).Should().Be(new CssKeyword("fill-box"));

    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#transform-box", section: "3.1")]
    [SpecFact]
    public void Transform_box_initial_value()
        => PropertyRegistry.InitialValue(PropertyId.TransformBox).Should().Be(new CssKeyword("view-box"));

    // ----- §5 transform-style / backface-visibility -----

    [SpecFact]
    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#transform-style-property", section: "5")]
    public void Transform_style_flat_or_preserve3d_parses()
    {
        Expand("transform-style: flat;").Single(d => d.Id == PropertyId.TransformStyle)
            .Value.Should().Be(new CssKeyword("flat"));
        Expand("transform-style: preserve-3d;").Single(d => d.Id == PropertyId.TransformStyle)
            .Value.Should().Be(new CssKeyword("preserve-3d"));
        PropertyRegistry.InitialValue(PropertyId.TransformStyle).Should().Be(new CssKeyword("flat"));
    }

    [SpecFact]
    [Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/#backface-visibility-property", section: "5")]
    public void Backface_visibility_visible_or_hidden_parses()
    {
        Expand("backface-visibility: hidden;").Single(d => d.Id == PropertyId.BackfaceVisibility)
            .Value.Should().Be(new CssKeyword("hidden"));
        Expand("backface-visibility: visible;").Single(d => d.Id == PropertyId.BackfaceVisibility)
            .Value.Should().Be(new CssKeyword("visible"));
        PropertyRegistry.InitialValue(PropertyId.BackfaceVisibility).Should().Be(new CssKeyword("visible"));
    }
}
