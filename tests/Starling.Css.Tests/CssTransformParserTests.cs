using FluentAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;
using Starling.Spec;

namespace Starling.Css.Tests;

[Spec("css-transforms-2", "https://www.w3.org/TR/css-transforms-2/")]

[TestClass]
public sealed class CssTransformParserTests
{
    private static CssValue ParseValue(string source)
    {
        var sheet = new CssParser("a{transform:" + source + "}").ParseStyleSheet();
        var decl = ((StyleRule)sheet.Rules.Single()).Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    private static CssTransform Parse(string source) => CssTransformParser.Parse(ParseValue(source));

    [TestMethod]
    public void None_keyword_yields_empty_transform()
    {
        var t = Parse("none");
        t.IsNone.Should().BeTrue();
        t.ToMatrix(100, 100).Should().Be(Matrix2D.Identity);
    }

    [TestMethod]
    public void Translate_two_lengths()
    {
        var t = Parse("translate(10px, 20px)");
        t.Functions.Should().ContainSingle().Which.Should().BeOfType<CssTranslate>();
        t.ToMatrix(0, 0).Should().Be(Matrix2D.Translate(10, 20));
    }

    [TestMethod]
    public void Translate_single_arg_defaults_y_to_zero()
    {
        var t = Parse("translate(15px)");
        t.ToMatrix(0, 0).Should().Be(Matrix2D.Translate(15, 0));
    }

    [TestMethod]
    public void TranslateX_TranslateY_independent_axes()
    {
        Parse("translateX(8px)").ToMatrix(0, 0).Should().Be(Matrix2D.Translate(8, 0));
        Parse("translateY(8px)").ToMatrix(0, 0).Should().Be(Matrix2D.Translate(0, 8));
    }

    [TestMethod]
    public void Translate_percentages_resolve_against_reference_box()
    {
        var t = Parse("translate(50%, 25%)");
        t.ToMatrix(200, 80).Should().Be(Matrix2D.Translate(100, 20));
    }

    [TestMethod]
    public void Scale_single_arg_applies_uniformly()
    {
        Parse("scale(2)").ToMatrix(0, 0).Should().Be(Matrix2D.Scale(2, 2));
    }

    [TestMethod]
    public void Scale_two_args_applies_independently()
    {
        Parse("scale(2, 0.5)").ToMatrix(0, 0).Should().Be(Matrix2D.Scale(2, 0.5));
    }

    [TestMethod]
    public void Rotate_accepts_deg_rad_turn()
    {
        Parse("rotate(90deg)").ToMatrix(0, 0).A.Should().BeApproximately(0, 1e-9);
        Parse("rotate(0.5turn)").ToMatrix(0, 0).A.Should().BeApproximately(-1, 1e-9);
        Parse("rotate(3.14159rad)").ToMatrix(0, 0).A.Should().BeApproximately(-1, 1e-4);
    }

    [TestMethod]
    public void Skew_one_arg_skews_only_x()
    {
        var m = Parse("skew(45deg)").ToMatrix(0, 0);
        m.C.Should().BeApproximately(1, 1e-9);
        m.B.Should().Be(0);
    }

    [TestMethod]
    public void Matrix_function_passes_six_components_through()
    {
        var t = Parse("matrix(1, 2, 3, 4, 5, 6)");
        t.ToMatrix(0, 0).Should().Be(new Matrix2D(1, 2, 3, 4, 5, 6));
    }

    [TestMethod]
    public void Function_list_composes_left_to_right()
    {
        // translate then scale: translate is leftmost so it's the outer wrap.
        // Outer * Inner means a point gets scaled, then translated.
        var t = Parse("translate(10px, 20px) scale(2)");
        var (x, y) = t.ToMatrix(0, 0).Transform(3, 4);
        x.Should().Be(16);
        y.Should().Be(28);
    }

    [TestMethod]
    public void Three_d_variants_are_rejected_and_fall_back_to_none()
    {
        Parse("translate3d(1px, 2px, 3px)").IsNone.Should().BeTrue();
        Parse("rotateX(45deg)").IsNone.Should().BeTrue();
        Parse("matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1)").IsNone.Should().BeTrue();
        Parse("perspective(500px)").IsNone.Should().BeTrue();
    }

    [TestMethod]
    public void Unknown_function_in_list_invalidates_whole_value()
    {
        Parse("scale(2) bogus(1)").IsNone.Should().BeTrue();
    }

    [TestMethod]
    public void Wrong_arity_invalidates_function()
    {
        Parse("matrix(1, 2, 3)").IsNone.Should().BeTrue();
        Parse("rotate(10deg, 20deg)").IsNone.Should().BeTrue();
    }
}
