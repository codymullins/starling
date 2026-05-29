using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Properties;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssTransforms1;

/// <summary>
/// Comprehensive conformance suite for
/// <see href="https://www.w3.org/TR/css-transforms-1/">CSS Transforms Level 1</see>.
/// Covers the 2D transform function surface — parse, function type, computed matrix,
/// function-list composition, <c>transform: none</c>, initial values,
/// <c>transform-origin</c>, and <c>transform-box</c>.
/// <para>
/// 3D functions (<c>matrix3d</c>, <c>rotate3d</c>, <c>translate3d</c>, etc.)
/// are out of scope here — they are covered separately in <c>CssTransforms2/</c>.
/// </para>
/// </summary>
[TestClass]
[Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/")]
public sealed class CssTransforms1Tests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static CssValue ParsePropertyValue(string source)
    {
        var sheet = new CssParser("a{transform:" + source + "}").ParseStyleSheet();
        var decl = ((StyleRule)sheet.Rules.Single()).Declarations.Single();
        return CssValueParser.Parse(decl.Value);
    }

    private static CssTransform ParseTransform(string source)
        => CssTransformParser.Parse(ParsePropertyValue(source));

    private static List<PropertyDeclaration> Expand(string css)
    {
        var sheet = CssParser.ParseStyleSheet($"x {{ {css} }}");
        var rule = (StyleRule)sheet.Rules[0];
        return rule.Declarations.SelectMany(PropertyRegistry.Parse).ToList();
    }

    private static CssValue ValueOf(string css, PropertyId id)
        => Expand(css).Single(d => d.Id == id).Value;

    private const double Tol = 1e-9;

    // -----------------------------------------------------------------------
    // §6.1  transform: none — initial / none keyword
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Transform_none_keyword_yields_IsNone_true()
    {
        var t = ParseTransform("none");
        t.IsNone.Should().BeTrue();
        t.Functions.Should().BeEmpty();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Transform_none_resolves_to_identity_matrix()
    {
        var m = ParseTransform("none").ToMatrix(100, 100);
        m.Should().Be(Matrix2D.Identity);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Transform_initial_value_is_none()
        => PropertyRegistry.InitialValue(PropertyId.Transform).Should().Be(new CssKeyword("none"));

    // -----------------------------------------------------------------------
    // §6.1  translate()  — CSS Transforms 1 §9 / function §6.1
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_two_px_args_produces_CssTranslate()
    {
        var t = ParseTransform("translate(10px, 20px)");
        t.Functions.Should().ContainSingle().Which.Should().BeOfType<CssTranslate>();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_two_px_args_matrix_has_correct_E_F()
    {
        var m = ParseTransform("translate(10px, 20px)").ToMatrix(0, 0);
        m.E.Should().BeApproximately(10, Tol);
        m.F.Should().BeApproximately(20, Tol);
        m.A.Should().BeApproximately(1, Tol);
        m.D.Should().BeApproximately(1, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_single_arg_defaults_ty_to_zero()
    {
        var m = ParseTransform("translate(15px)").ToMatrix(0, 0);
        m.E.Should().BeApproximately(15, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_percentage_tx_resolves_against_reference_width()
    {
        // translate(50%) should be 50% of the reference width.
        var m = ParseTransform("translate(50%)").ToMatrix(200, 100);
        m.E.Should().BeApproximately(100, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_percentage_tx_ty_resolves_against_reference_box()
    {
        var m = ParseTransform("translate(50%, 25%)").ToMatrix(200, 80);
        m.E.Should().BeApproximately(100, Tol);
        m.F.Should().BeApproximately(20, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translate", section: "9")]
    [SpecFact]
    public void Translate_zero_unitless_is_valid()
    {
        // Per spec, 0 without unit is allowed for lengths.
        var t = ParseTransform("translate(0, 0)");
        t.IsNone.Should().BeFalse();
        var m = t.ToMatrix(0, 0);
        m.E.Should().BeApproximately(0, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    // -----------------------------------------------------------------------
    // §9  translateX() / translateY()
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translatex", section: "9")]
    [SpecFact]
    public void TranslateX_sets_only_E_component()
    {
        var m = ParseTransform("translateX(8px)").ToMatrix(0, 0);
        m.E.Should().BeApproximately(8, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translatey", section: "9")]
    [SpecFact]
    public void TranslateY_sets_only_F_component()
    {
        var m = ParseTransform("translateY(8px)").ToMatrix(0, 0);
        m.E.Should().BeApproximately(0, Tol);
        m.F.Should().BeApproximately(8, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translatex", section: "9")]
    [SpecFact]
    public void TranslateX_percentage_resolves_against_width()
    {
        var m = ParseTransform("translateX(10%)").ToMatrix(100, 50);
        m.E.Should().BeApproximately(10, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-translatey", section: "9")]
    [SpecFact]
    public void TranslateY_percentage_resolves_against_height()
    {
        var m = ParseTransform("translateY(20%)").ToMatrix(100, 50);
        m.E.Should().BeApproximately(0, Tol);
        m.F.Should().BeApproximately(10, Tol);
    }

    // -----------------------------------------------------------------------
    // §9  scale()
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_single_arg_produces_CssScale_with_equal_axes()
    {
        var t = ParseTransform("scale(2)");
        var fn = t.Functions.Should().ContainSingle().Which.Should().BeOfType<CssScale>().Which;
        fn.X.Should().BeApproximately(2, Tol);
        fn.Y.Should().BeApproximately(2, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_single_arg_matrix_uniform()
    {
        var m = ParseTransform("scale(2)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(2, Tol);
        m.D.Should().BeApproximately(2, Tol);
        m.B.Should().BeApproximately(0, Tol);
        m.C.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_two_args_produces_independent_sx_sy()
    {
        var fn = (CssScale)ParseTransform("scale(2, 3)").Functions.Single();
        fn.X.Should().BeApproximately(2, Tol);
        fn.Y.Should().BeApproximately(3, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_two_args_matrix_has_correct_A_D()
    {
        var m = ParseTransform("scale(2, 3)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(2, Tol);
        m.D.Should().BeApproximately(3, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_fractional_value_rounds_trip()
    {
        var m = ParseTransform("scale(0.5)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(0.5, Tol);
        m.D.Should().BeApproximately(0.5, Tol);
    }

    // -----------------------------------------------------------------------
    // §9  scaleX() / scaleY()
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scalex", section: "9")]
    [SpecFact]
    public void ScaleX_sets_A_to_factor_D_stays_one()
    {
        var m = ParseTransform("scaleX(3)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(3, Tol);
        m.D.Should().BeApproximately(1, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scaley", section: "9")]
    [SpecFact]
    public void ScaleY_sets_D_to_factor_A_stays_one()
    {
        var m = ParseTransform("scaleY(4)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(1, Tol);
        m.D.Should().BeApproximately(4, Tol);
    }

    // -----------------------------------------------------------------------
    // §9  rotate()  — deg, rad, grad, turn
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_produces_CssRotate_function()
    {
        ParseTransform("rotate(45deg)").Functions.Should().ContainSingle()
            .Which.Should().BeOfType<CssRotate>();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_0deg_yields_identity_matrix()
    {
        var m = ParseTransform("rotate(0deg)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(1, Tol);
        m.B.Should().BeApproximately(0, Tol);
        m.C.Should().BeApproximately(0, Tol);
        m.D.Should().BeApproximately(1, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_90deg_matrix_is_cos0_sin1_negsin_cos()
    {
        // rotate(90deg) => matrix(cos90, sin90, -sin90, cos90, 0, 0) = (≈0, 1, -1, ≈0, 0, 0)
        var m = ParseTransform("rotate(90deg)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(0, Tol);
        m.B.Should().BeApproximately(1, Tol);
        m.C.Should().BeApproximately(-1, Tol);
        m.D.Should().BeApproximately(0, Tol);
        m.E.Should().BeApproximately(0, Tol);
        m.F.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_180deg_matrix_negates_both_axes()
    {
        // rotate(180deg) => matrix(-1, ≈0, ≈0, -1, 0, 0)
        var m = ParseTransform("rotate(180deg)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(-1, Tol);
        m.D.Should().BeApproximately(-1, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_deg_and_100grad_are_equivalent()
    {
        // 100 gradians = 90 degrees
        var mDeg = ParseTransform("rotate(90deg)").ToMatrix(0, 0);
        var mGrad = ParseTransform("rotate(100grad)").ToMatrix(0, 0);
        mGrad.A.Should().BeApproximately(mDeg.A, Tol);
        mGrad.B.Should().BeApproximately(mDeg.B, Tol);
        mGrad.C.Should().BeApproximately(mDeg.C, Tol);
        mGrad.D.Should().BeApproximately(mDeg.D, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_half_pi_rad_equals_90deg()
    {
        var mDeg = ParseTransform("rotate(90deg)").ToMatrix(0, 0);
        var mRad = ParseTransform("rotate(1.5707963267948966rad)").ToMatrix(0, 0);
        mRad.A.Should().BeApproximately(mDeg.A, 1e-7);
        mRad.B.Should().BeApproximately(mDeg.B, 1e-7);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_0point25turn_equals_90deg()
    {
        // 0.25 turn = 90 degrees
        var mDeg = ParseTransform("rotate(90deg)").ToMatrix(0, 0);
        var mTurn = ParseTransform("rotate(0.25turn)").ToMatrix(0, 0);
        mTurn.A.Should().BeApproximately(mDeg.A, Tol);
        mTurn.B.Should().BeApproximately(mDeg.B, Tol);
        mTurn.C.Should().BeApproximately(mDeg.C, Tol);
        mTurn.D.Should().BeApproximately(mDeg.D, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_0point5turn_equals_180deg()
    {
        var m = ParseTransform("rotate(0.5turn)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(-1, Tol);
        m.D.Should().BeApproximately(-1, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-rotate", section: "9")]
    [SpecFact]
    public void Rotate_zero_unitless_is_valid()
    {
        var t = ParseTransform("rotate(0)");
        t.IsNone.Should().BeFalse();
        var m = t.ToMatrix(0, 0);
        m.A.Should().BeApproximately(1, Tol);
    }

    // -----------------------------------------------------------------------
    // §9  skew() / skewX() / skewY()
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skewx", section: "9")]
    [SpecFact]
    public void SkewX_produces_CssSkew_with_y_zero()
    {
        var fn = (CssSkew)ParseTransform("skewX(30deg)").Functions.Single();
        fn.YRadians.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skewx", section: "9")]
    [SpecFact]
    public void SkewX_45deg_sets_C_to_one()
    {
        // skewX(45deg): matrix(1, 0, tan45, 1, 0, 0) → C ≈ 1, B = 0
        var m = ParseTransform("skewX(45deg)").ToMatrix(0, 0);
        m.C.Should().BeApproximately(1, Tol);
        m.B.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skewy", section: "9")]
    [SpecFact]
    public void SkewY_produces_CssSkew_with_x_zero()
    {
        var fn = (CssSkew)ParseTransform("skewY(30deg)").Functions.Single();
        fn.XRadians.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skewy", section: "9")]
    [SpecFact]
    public void SkewY_45deg_sets_B_to_one()
    {
        // skewY(45deg): matrix(1, tan45, 0, 1, 0, 0) → B ≈ 1, C = 0
        var m = ParseTransform("skewY(45deg)").ToMatrix(0, 0);
        m.B.Should().BeApproximately(1, Tol);
        m.C.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skew", section: "9")]
    [SpecFact]
    public void Skew_one_arg_skews_only_x_axis()
    {
        // skew(10deg) — y component is 0
        var m = ParseTransform("skew(10deg)").ToMatrix(0, 0);
        m.C.Should().BeApproximately(Math.Tan(10 * Math.PI / 180), Tol);
        m.B.Should().BeApproximately(0, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-skew", section: "9")]
    [SpecFact]
    public void Skew_two_args_sets_both_axes()
    {
        var m = ParseTransform("skew(10deg, 20deg)").ToMatrix(0, 0);
        m.C.Should().BeApproximately(Math.Tan(10 * Math.PI / 180), Tol);
        m.B.Should().BeApproximately(Math.Tan(20 * Math.PI / 180), Tol);
    }

    // -----------------------------------------------------------------------
    // §9  matrix()
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-matrix", section: "9")]
    [SpecFact]
    public void Matrix_six_args_produces_CssMatrix()
    {
        ParseTransform("matrix(1, 2, 3, 4, 5, 6)").Functions.Should()
            .ContainSingle().Which.Should().BeOfType<CssMatrix>();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-matrix", section: "9")]
    [SpecFact]
    public void Matrix_passes_six_components_through_unchanged()
    {
        var m = ParseTransform("matrix(1, 2, 3, 4, 5, 6)").ToMatrix(0, 0);
        m.Should().Be(new Matrix2D(1, 2, 3, 4, 5, 6));
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-matrix", section: "9")]
    [SpecFact]
    public void Matrix_identity_values_are_identity()
    {
        var m = ParseTransform("matrix(1, 0, 0, 1, 0, 0)").ToMatrix(0, 0);
        m.Should().Be(Matrix2D.Identity);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-matrix", section: "9")]
    [SpecFact]
    public void Matrix_wrong_arity_invalidates_whole_value()
    {
        ParseTransform("matrix(1, 2, 3)").IsNone.Should().BeTrue();
        ParseTransform("matrix(1, 2, 3, 4, 5, 6, 7)").IsNone.Should().BeTrue();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-matrix", section: "9")]
    [SpecFact]
    public void Matrix_float_components_round_trip()
    {
        var m = ParseTransform("matrix(0.5, -0.866, 0.866, 0.5, 100, 200)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(0.5, Tol);
        m.B.Should().BeApproximately(-0.866, 1e-6);
        m.C.Should().BeApproximately(0.866, 1e-6);
        m.D.Should().BeApproximately(0.5, Tol);
        m.E.Should().BeApproximately(100, Tol);
        m.F.Should().BeApproximately(200, Tol);
    }

    // -----------------------------------------------------------------------
    // §6.1  Computed matrix — known equivalences
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Rotate_90deg_computed_matrix_matches_spec_equivalence()
    {
        // CSS Transforms 1 §9: rotate(90deg) ≡ matrix(cos90, sin90, -sin90, cos90, 0, 0)
        //                                     = matrix(0, 1, -1, 0, 0, 0)
        var mRotate = ParseTransform("rotate(90deg)").ToMatrix(0, 0);
        var mMatrix = ParseTransform("matrix(0, 1, -1, 0, 0, 0)").ToMatrix(0, 0);
        mRotate.A.Should().BeApproximately(mMatrix.A, Tol);
        mRotate.B.Should().BeApproximately(mMatrix.B, Tol);
        mRotate.C.Should().BeApproximately(mMatrix.C, Tol);
        mRotate.D.Should().BeApproximately(mMatrix.D, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Scale_2_computed_matrix_matches_matrix_equivalent()
    {
        // scale(2) ≡ matrix(2, 0, 0, 2, 0, 0)
        var mScale = ParseTransform("scale(2)").ToMatrix(0, 0);
        var mMatrix = ParseTransform("matrix(2, 0, 0, 2, 0, 0)").ToMatrix(0, 0);
        mScale.Should().Be(mMatrix);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Translate_10_20_computed_matrix_matches_matrix_equivalent()
    {
        // translate(10px,20px) ≡ matrix(1, 0, 0, 1, 10, 20)
        var mTranslate = ParseTransform("translate(10px, 20px)").ToMatrix(0, 0);
        var mMatrix = ParseTransform("matrix(1, 0, 0, 1, 10, 20)").ToMatrix(0, 0);
        mTranslate.Should().Be(mMatrix);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void SkewX_45deg_computed_matrix_matches_matrix_equivalent()
    {
        // skewX(45deg) ≡ matrix(1, 0, tan(45°), 1, 0, 0) = matrix(1, 0, ≈1, 1, 0, 0)
        var mSkew = ParseTransform("skewX(45deg)").ToMatrix(0, 0);
        mSkew.C.Should().BeApproximately(1, Tol);
        mSkew.A.Should().BeApproximately(1, Tol);
        mSkew.D.Should().BeApproximately(1, Tol);
        mSkew.B.Should().BeApproximately(0, Tol);
    }

    // -----------------------------------------------------------------------
    // §6.1  Function-list composition (left-to-right)
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Function_list_translate_then_scale_composes_in_order()
    {
        // translate(10px,20px) scale(2): point (3,4) gets scaled (→ 6,8) then translated (→ 16,28).
        var t = ParseTransform("translate(10px, 20px) scale(2)");
        var (x, y) = t.ToMatrix(0, 0).Transform(3, 4);
        x.Should().BeApproximately(16, Tol);
        y.Should().BeApproximately(28, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Function_list_scale_then_translate_composes_differently()
    {
        // scale(2) translate(10px,20px): point (3,4) gets translated (→ 13,24) then scaled (→ 26,48).
        var t = ParseTransform("scale(2) translate(10px, 20px)");
        var (x, y) = t.ToMatrix(0, 0).Transform(3, 4);
        x.Should().BeApproximately(26, Tol);
        y.Should().BeApproximately(48, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Function_list_translate_rotate_composes_to_correct_matrix()
    {
        // translate(10px,20px) rotate(45deg):
        //   rotate is applied first, then translate.
        //   E = 10, F = 20 (translation unchanged), A=D=cos45, B=sin45, C=-sin45.
        var m = ParseTransform("translate(10px, 20px) rotate(45deg)").ToMatrix(0, 0);
        m.A.Should().BeApproximately(Math.Cos(Math.PI / 4), Tol);
        m.B.Should().BeApproximately(Math.Sin(Math.PI / 4), Tol);
        m.C.Should().BeApproximately(-Math.Sin(Math.PI / 4), Tol);
        m.D.Should().BeApproximately(Math.Cos(Math.PI / 4), Tol);
        m.E.Should().BeApproximately(10, Tol);
        m.F.Should().BeApproximately(20, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Function_list_three_functions_composes_all_three()
    {
        // scale(2) rotate(90deg) translate(5px,0)
        // Applying right-to-left: translate(5,0) → (8,0), rotate90° → (0,8), scale(2) → (0,16)
        var t = ParseTransform("scale(2) rotate(90deg) translate(5px, 0)");
        var (x, y) = t.ToMatrix(0, 0).Transform(3, 0);
        x.Should().BeApproximately(0, Tol);
        y.Should().BeApproximately(16, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Function_list_parses_all_functions()
    {
        var t = ParseTransform("translate(10px, 20px) rotate(45deg)");
        t.Functions.Should().HaveCount(2);
        t.Functions[0].Should().BeOfType<CssTranslate>();
        t.Functions[1].Should().BeOfType<CssRotate>();
    }

    // -----------------------------------------------------------------------
    // §6.1  Error / invalid grammar — whole declaration resolves to none
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Unknown_function_in_list_invalidates_whole_value()
    {
        ParseTransform("scale(2) bogus(1)").IsNone.Should().BeTrue();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Wrong_arity_rotate_invalidates_value()
    {
        // rotate() takes exactly one angle — two args is invalid.
        ParseTransform("rotate(10deg, 20deg)").IsNone.Should().BeTrue();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_no_args_invalidates_value()
    {
        ParseTransform("scale()").IsNone.Should().BeTrue();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#funcdef-transform-scale", section: "9")]
    [SpecFact]
    public void Scale_three_args_invalidates_value()
    {
        ParseTransform("scale(1, 2, 3)").IsNone.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // §6.1  Property value parsing — transform as CssFunctionValue / CssValueList
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Single_function_parses_as_function_value()
    {
        var v = ValueOf("transform: scale(2);", PropertyId.Transform);
        v.Should().BeOfType<CssFunctionValue>();
        ((CssFunctionValue)v).Name.Should().Be("scale");
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Two_functions_parse_as_value_list()
    {
        var v = ValueOf("transform: translate(10px, 20px) rotate(45deg);", PropertyId.Transform);
        v.Should().BeOfType<CssValueList>();
        ((CssValueList)v).Values.Should().HaveCount(2);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Transform_none_parses_as_keyword_value()
    {
        var v = ValueOf("transform: none;", PropertyId.Transform);
        v.Should().BeOfType<CssKeyword>();
        ((CssKeyword)v).Name.Should().Be("none");
    }

    // -----------------------------------------------------------------------
    // §6.1  transform-origin — §6.2 in CSS Transforms 1
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_parses_to_TransformOrigin_property()
    {
        var decls = Expand("transform-origin: 50% 50%;");
        decls.Should().ContainSingle(d => d.Id == PropertyId.TransformOrigin);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_50_50_percent_parses_as_value_list()
    {
        var v = ValueOf("transform-origin: 50% 50%;", PropertyId.TransformOrigin);
        v.Should().BeOfType<CssValueList>();
        var list = (CssValueList)v;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssPercentage(50));
        list.Values[1].Should().Be(new CssPercentage(50));
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_lengths_parse_as_value_list()
    {
        var v = ValueOf("transform-origin: 10px 20px;", PropertyId.TransformOrigin);
        v.Should().BeOfType<CssValueList>();
        var list = (CssValueList)v;
        list.Values.Should().HaveCount(2);
        list.Values[0].Should().Be(new CssLength(10, CssLengthUnit.Px));
        list.Values[1].Should().Be(new CssLength(20, CssLengthUnit.Px));
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_keyword_top_left_parses_as_value_list()
    {
        var v = ValueOf("transform-origin: top left;", PropertyId.TransformOrigin);
        // "top left" yields two idents parsed as a CssValueList.
        v.Should().BeOfType<CssValueList>();
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_single_center_keyword_parses()
    {
        var v = ValueOf("transform-origin: center;", PropertyId.TransformOrigin);
        // "center" alone maps to a single keyword value.
        v.Should().BeOfType<CssKeyword>();
        ((CssKeyword)v).Name.Should().Be("center");
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-origin", section: "6.2")]
    [SpecFact]
    public void Transform_origin_initial_value()
    {
        // CSS Transforms 1 §6.2 initial = 50% 50%; the implementation stores "50% 50% 0"
        // to accommodate the CSS Transforms 2 Z-component extension.
        var initial = PropertyRegistry.InitialValue(PropertyId.TransformOrigin);
        initial.Should().Be(new CssKeyword("50% 50% 0"));
    }

    // -----------------------------------------------------------------------
    // §6.1  transform-box
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_border_box_parses()
        => ValueOf("transform-box: border-box;", PropertyId.TransformBox)
            .Should().Be(new CssKeyword("border-box"));

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_fill_box_parses()
        => ValueOf("transform-box: fill-box;", PropertyId.TransformBox)
            .Should().Be(new CssKeyword("fill-box"));

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_view_box_parses()
        => ValueOf("transform-box: view-box;", PropertyId.TransformBox)
            .Should().Be(new CssKeyword("view-box"));

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_content_box_parses()
        => ValueOf("transform-box: content-box;", PropertyId.TransformBox)
            .Should().Be(new CssKeyword("content-box"));

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_stroke_box_parses()
        => ValueOf("transform-box: stroke-box;", PropertyId.TransformBox)
            .Should().Be(new CssKeyword("stroke-box"));

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#propdef-transform-box", section: "6.1")]
    [SpecFact]
    public void Transform_box_initial_value_is_view_box()
        => PropertyRegistry.InitialValue(PropertyId.TransformBox).Should().Be(new CssKeyword("view-box"));

    // -----------------------------------------------------------------------
    // Point-transform assertions via Matrix2D.Transform
    // -----------------------------------------------------------------------

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Translate_moves_point_by_tx_ty()
    {
        var (x, y) = ParseTransform("translate(100px, 200px)").ToMatrix(0, 0).Transform(0, 0);
        x.Should().BeApproximately(100, Tol);
        y.Should().BeApproximately(200, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Scale_2_doubles_point_coordinates()
    {
        var (x, y) = ParseTransform("scale(2)").ToMatrix(0, 0).Transform(3, 5);
        x.Should().BeApproximately(6, Tol);
        y.Should().BeApproximately(10, Tol);
    }

    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-property", section: "6")]
    [SpecFact]
    public void Rotate_90deg_maps_x_axis_to_y_axis()
    {
        // (1,0) rotated 90° clockwise → (0,1) in CSS (y-down) coordinate system.
        var (x, y) = ParseTransform("rotate(90deg)").ToMatrix(0, 0).Transform(1, 0);
        x.Should().BeApproximately(0, Tol);
        y.Should().BeApproximately(1, Tol);
    }
}
