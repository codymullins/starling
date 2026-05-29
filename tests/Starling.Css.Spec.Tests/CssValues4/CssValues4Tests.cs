using AwesomeAssertions;
using Starling.Css.Parser;
using Starling.Css.Values;

namespace Starling.Css.Spec.Tests.CssValues4;

/// <summary>
/// Conformance suite for <see href="https://www.w3.org/TR/css-values-4/">CSS Values and Units Module Level 4</see>.
/// Covers §2 CSS-wide keywords, §4 numbers, §5 percentages, §6 lengths,
/// §8 angles/time/frequency/resolution, §10 math functions (calc/min/max/clamp),
/// §11 attr(), and miscellaneous value types (string, url, custom properties).
/// </summary>
[TestClass]
[Spec("css-values-4", "https://www.w3.org/TR/css-values-4/")]
public sealed class CssValues4Tests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    private static CssValue Parse(string value)
    {
        var sheet = CssParser.ParseStyleSheet($"a{{x:{value}}}");
        var rule = (StyleRule)sheet.Rules.Single();
        return CssValueParser.Parse(rule.Declarations.Single().Value);
    }

    private const double Tolerance = 1e-9;

    // ---------------------------------------------------------------------------
    // §2 — CSS-wide keywords
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#common-keywords", "§2")]
    [SpecFact]
    public void Keyword_initial_parses_as_CssKeyword_initial()
    {
        var v = Parse("initial");
        v.Should().Be(new CssKeyword("initial"));
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#common-keywords", "§2")]
    [SpecFact]
    public void Keyword_inherit_parses_as_CssKeyword_inherit()
    {
        var v = Parse("inherit");
        v.Should().Be(new CssKeyword("inherit"));
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#common-keywords", "§2")]
    [SpecFact]
    public void Keyword_unset_parses_as_CssKeyword_unset()
    {
        var v = Parse("unset");
        v.Should().Be(new CssKeyword("unset"));
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#common-keywords", "§2")]
    [SpecFact]
    public void Keyword_revert_parses_as_CssKeyword_revert()
    {
        var v = Parse("revert");
        v.Should().Be(new CssKeyword("revert"));
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#common-keywords", "§2")]
    [SpecFact]
    public void Keywords_are_case_insensitive()
    {
        // Spec §2: CSS-wide keywords are ASCII case-insensitive.
        Parse("Initial").Should().Be(new CssKeyword("initial"));
        Parse("INHERIT").Should().Be(new CssKeyword("inherit"));
        Parse("Unset").Should().Be(new CssKeyword("unset"));
        Parse("REVERT").Should().Be(new CssKeyword("revert"));
    }

    // ---------------------------------------------------------------------------
    // §4 — Number values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Integer_parses_as_CssNumber()
    {
        var v = Parse("42");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().Be(42.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Decimal_number_parses_as_CssNumber()
    {
        var v = Parse("3.14");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(3.14, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Leading_dot_number_parses_as_CssNumber()
    {
        // ".5" is valid per CSS Syntax 3 §4.3.3.
        var v = Parse(".5");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(0.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Positive_signed_number_parses_as_CssNumber()
    {
        var v = Parse("+7");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().Be(7.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Negative_signed_number_parses_as_CssNumber()
    {
        var v = Parse("-3");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().Be(-3.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Zero_number_parses_as_CssNumber()
    {
        var v = Parse("0");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().Be(0.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Scientific_notation_number_parses_correctly()
    {
        // CSS Syntax 3 §4.3.3 allows E/e notation.
        var v = Parse("1e2");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(100.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Scientific_notation_negative_exponent_parses_correctly()
    {
        var v = Parse("1e-2");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(0.01, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Large_negative_number_parses_correctly()
    {
        var v = Parse("-1000");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().Be(-1000.0);
    }

    // ---------------------------------------------------------------------------
    // §5 — Percentage values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#percentage-value", "§5")]
    [SpecFact]
    public void Percentage_parses_as_CssPercentage()
    {
        var v = Parse("50%");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().Be(50.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#percentage-value", "§5")]
    [SpecFact]
    public void Zero_percentage_parses()
    {
        var v = Parse("0%");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().Be(0.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#percentage-value", "§5")]
    [SpecFact]
    public void Fractional_percentage_parses()
    {
        var v = Parse("33.33%");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().BeApproximately(33.33, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#percentage-value", "§5")]
    [SpecFact]
    public void Over_100_percentage_parses()
    {
        var v = Parse("150%");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().Be(150.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#percentage-value", "§5")]
    [SpecFact]
    public void Negative_percentage_parses()
    {
        var v = Parse("-10%");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().Be(-10.0);
    }

    // ---------------------------------------------------------------------------
    // §6 — Length values — absolute units (§6.2)
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_px_parses_as_CssLength_with_Px_unit()
    {
        var v = Parse("10px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().Be(10.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_zero_px_parses()
    {
        var v = Parse("0px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().Be(0.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_cm_parses_and_converts_to_px()
    {
        // 1cm = 96/2.54 px per CSS Values 4 §6.2.
        var v = Parse("1cm");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Cm);
        l.Value.Should().Be(1.0);
        // Assert the canonical engine conversion.
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(96.0 / 2.54, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_mm_parses_and_converts_to_px()
    {
        // 1mm = 96/25.4 px.
        var v = Parse("1mm");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Mm);
        l.Value.Should().Be(1.0);
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(96.0 / 25.4, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_Q_parses_and_converts_to_px()
    {
        // 1Q = 1mm/4 = 96/101.6 px.
        var v = Parse("1Q");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Q);
        l.Value.Should().Be(1.0);
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(96.0 / 101.6, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_in_parses_and_converts_96px()
    {
        // 1in = 96px per spec.
        var v = Parse("1in");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.In);
        l.Value.Should().Be(1.0);
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(96.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_pt_parses_and_converts_four_thirds_px()
    {
        // 1pt = 4/3 px per spec (96px/in ÷ 72pt/in).
        var v = Parse("1pt");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Pt);
        l.Value.Should().Be(1.0);
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(4.0 / 3.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Length_pc_parses_and_converts_16px()
    {
        // 1pc = 12pt = 16px.
        var v = Parse("1pc");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Pc);
        l.Value.Should().Be(1.0);
        l.Unit.AbsoluteToPx(l.Value).Should().BeApproximately(16.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Absolute_unit_IsAbsolute_returns_true()
    {
        CssLengthUnit.Px.IsAbsolute().Should().BeTrue();
        CssLengthUnit.Cm.IsAbsolute().Should().BeTrue();
        CssLengthUnit.Mm.IsAbsolute().Should().BeTrue();
        CssLengthUnit.In.IsAbsolute().Should().BeTrue();
        CssLengthUnit.Pt.IsAbsolute().Should().BeTrue();
        CssLengthUnit.Pc.IsAbsolute().Should().BeTrue();
        CssLengthUnit.Q.IsAbsolute().Should().BeTrue();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Font_relative_units_are_not_absolute()
    {
        CssLengthUnit.Em.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Rem.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Ex.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Ch.IsAbsolute().Should().BeFalse();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#absolute-lengths", "§6.2")]
    [SpecFact]
    public void Absolute_to_px_exact_conversion_table()
    {
        // Exact values asserted here are what the Starling engine uses.
        // cm: 96/2.54 = 37.79527559055118...
        CssLengthUnit.Cm.AbsoluteToPx(1.0).Should().BeApproximately(37.7952755905511, 1e-7);
        // mm: 96/25.4 = 3.779527559055118...
        CssLengthUnit.Mm.AbsoluteToPx(1.0).Should().BeApproximately(3.77952755905511, 1e-7);
        // Q: 96/101.6 = 0.9448818897637795...
        CssLengthUnit.Q.AbsoluteToPx(1.0).Should().BeApproximately(0.94488188976378, 1e-7);
        // in: exactly 96
        CssLengthUnit.In.AbsoluteToPx(1.0).Should().Be(96.0);
        // pt: 4/3
        CssLengthUnit.Pt.AbsoluteToPx(1.0).Should().BeApproximately(1.33333333333333, 1e-7);
        // pc: exactly 16
        CssLengthUnit.Pc.AbsoluteToPx(1.0).Should().Be(16.0);
        // px: exactly 1
        CssLengthUnit.Px.AbsoluteToPx(1.0).Should().Be(1.0);
    }

    // ---------------------------------------------------------------------------
    // §6 — Length values — font-relative units (§6.1)
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_em_parses_as_CssLength_with_Em_unit()
    {
        var v = Parse("1.5em");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Em);
        l.Value.Should().BeApproximately(1.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_rem_parses_as_CssLength_with_Rem_unit()
    {
        var v = Parse("2rem");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Rem);
        l.Value.Should().Be(2.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_ex_parses_as_CssLength_with_Ex_unit()
    {
        var v = Parse("1ex");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Ex);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_ch_parses_as_CssLength_with_Ch_unit()
    {
        var v = Parse("1ch");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Ch);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_cap_parses_as_CssLength_with_Cap_unit()
    {
        var v = Parse("1cap");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Cap);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_ic_parses_as_CssLength_with_Ic_unit()
    {
        var v = Parse("1ic");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Ic);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_lh_parses_as_CssLength_with_Lh_unit()
    {
        var v = Parse("1lh");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Lh);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#relative-lengths", "§6.1")]
    [SpecFact]
    public void Length_rlh_parses_as_CssLength_with_Rlh_unit()
    {
        var v = Parse("1rlh");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Rlh);
        l.Value.Should().Be(1.0);
    }

    // ---------------------------------------------------------------------------
    // §6 — Length values — viewport-relative units (§6.1.4)
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vw_parses_as_CssLength_with_Vw_unit()
    {
        var v = Parse("50vw");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vw);
        l.Value.Should().Be(50.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vh_parses_as_CssLength_with_Vh_unit()
    {
        var v = Parse("100vh");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vh);
        l.Value.Should().Be(100.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vmin_parses_as_CssLength_with_Vmin_unit()
    {
        var v = Parse("1vmin");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vmin);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vmax_parses_as_CssLength_with_Vmax_unit()
    {
        var v = Parse("1vmax");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vmax);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vi_parses_as_CssLength_with_Vi_unit()
    {
        var v = Parse("1vi");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vi);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Length_vb_parses_as_CssLength_with_Vb_unit()
    {
        var v = Parse("1vb");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Vb);
        l.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Small_viewport_units_svw_svh_parse()
    {
        var vw = Parse("1svw").Should().BeOfType<CssLength>().Subject;
        vw.Unit.Should().Be(CssLengthUnit.Svw);
        var vh = Parse("1svh").Should().BeOfType<CssLength>().Subject;
        vh.Unit.Should().Be(CssLengthUnit.Svh);
        var vmin = Parse("1svmin").Should().BeOfType<CssLength>().Subject;
        vmin.Unit.Should().Be(CssLengthUnit.Svmin);
        var vmax = Parse("1svmax").Should().BeOfType<CssLength>().Subject;
        vmax.Unit.Should().Be(CssLengthUnit.Svmax);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Large_viewport_units_lvw_lvh_parse()
    {
        var vw = Parse("1lvw").Should().BeOfType<CssLength>().Subject;
        vw.Unit.Should().Be(CssLengthUnit.Lvw);
        var vh = Parse("1lvh").Should().BeOfType<CssLength>().Subject;
        vh.Unit.Should().Be(CssLengthUnit.Lvh);
        var vmin = Parse("1lvmin").Should().BeOfType<CssLength>().Subject;
        vmin.Unit.Should().Be(CssLengthUnit.Lvmin);
        var vmax = Parse("1lvmax").Should().BeOfType<CssLength>().Subject;
        vmax.Unit.Should().Be(CssLengthUnit.Lvmax);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Dynamic_viewport_units_dvw_dvh_parse()
    {
        var vw = Parse("1dvw").Should().BeOfType<CssLength>().Subject;
        vw.Unit.Should().Be(CssLengthUnit.Dvw);
        var vh = Parse("1dvh").Should().BeOfType<CssLength>().Subject;
        vh.Unit.Should().Be(CssLengthUnit.Dvh);
        var vmin = Parse("1dvmin").Should().BeOfType<CssLength>().Subject;
        vmin.Unit.Should().Be(CssLengthUnit.Dvmin);
        var vmax = Parse("1dvmax").Should().BeOfType<CssLength>().Subject;
        vmax.Unit.Should().Be(CssLengthUnit.Dvmax);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#viewport-relative-lengths", "§6.1.4")]
    [SpecFact]
    public void Viewport_units_are_not_absolute()
    {
        CssLengthUnit.Vw.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Vh.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Vmin.IsAbsolute().Should().BeFalse();
        CssLengthUnit.Vmax.IsAbsolute().Should().BeFalse();
    }

    // ---------------------------------------------------------------------------
    // §6 — Length values — negative and fractional
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#length-value", "§6")]
    [SpecFact]
    public void Negative_length_parses()
    {
        var v = Parse("-5px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().Be(-5.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#length-value", "§6")]
    [SpecFact]
    public void Fractional_length_parses()
    {
        var v = Parse("0.5px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(0.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#length-value", "§6")]
    [SpecFact]
    public void Leading_dot_length_parses()
    {
        var v = Parse(".5px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(0.5, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §8.1 — Angle values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_deg_parses_as_CssAngle_Degrees()
    {
        var v = Parse("90deg");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Degrees);
        a.Value.Should().Be(90.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_grad_parses_as_CssAngle_Gradians()
    {
        var v = Parse("100grad");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Gradians);
        a.Value.Should().Be(100.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_rad_parses_as_CssAngle_Radians()
    {
        var v = Parse("1.5708rad");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Radians);
        a.Value.Should().BeApproximately(1.5708, 1e-4);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_turn_parses_as_CssAngle_Turns()
    {
        var v = Parse("0.5turn");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Turns);
        a.Value.Should().BeApproximately(0.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_conversions_to_degrees()
    {
        // 100grad = 90deg
        var grad = Parse("100grad").Should().BeOfType<CssAngle>().Subject;
        grad.InDegrees.Should().BeApproximately(90.0, Tolerance);

        // 0.5turn = 180deg
        var turn = Parse("0.5turn").Should().BeOfType<CssAngle>().Subject;
        turn.InDegrees.Should().BeApproximately(180.0, Tolerance);

        // pi rad = 180deg
        var rad = Parse("3.14159265358979rad").Should().BeOfType<CssAngle>().Subject;
        rad.InDegrees.Should().BeApproximately(180.0, 1e-4);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_negative_deg_parses()
    {
        var v = Parse("-45deg");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Degrees);
        a.Value.Should().Be(-45.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#angle-value", "§8.1")]
    [SpecFact]
    public void Angle_zero_deg_parses()
    {
        var v = Parse("0deg");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.Unit.Should().Be(CssAngleUnit.Degrees);
        a.Value.Should().Be(0.0);
    }

    // ---------------------------------------------------------------------------
    // §8.2 — Time values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_seconds_parses_as_CssTime_Seconds()
    {
        var v = Parse("2s");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.Unit.Should().Be(CssTimeUnit.Seconds);
        t.Value.Should().Be(2.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_milliseconds_parses_as_CssTime_Milliseconds()
    {
        var v = Parse("200ms");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.Unit.Should().Be(CssTimeUnit.Milliseconds);
        t.Value.Should().Be(200.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_fractional_seconds_parses()
    {
        var v = Parse("0.3s");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.Unit.Should().Be(CssTimeUnit.Seconds);
        t.Value.Should().BeApproximately(0.3, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_InSeconds_converts_ms_to_s()
    {
        // 500ms = 0.5s
        var t = Parse("500ms").Should().BeOfType<CssTime>().Subject;
        t.InSeconds.Should().BeApproximately(0.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_InSeconds_for_seconds_is_identity()
    {
        var t = Parse("1.5s").Should().BeOfType<CssTime>().Subject;
        t.InSeconds.Should().BeApproximately(1.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#time-value", "§8.2")]
    [SpecFact]
    public void Time_zero_s_parses()
    {
        var v = Parse("0s");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.Value.Should().Be(0.0);
        t.Unit.Should().Be(CssTimeUnit.Seconds);
    }

    // ---------------------------------------------------------------------------
    // §8.3 — Frequency values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#frequency-value", "§8.3")]
    [SpecFact]
    public void Frequency_hz_parses_as_CssFrequency_Hertz()
    {
        var v = Parse("440hz");
        var f = v.Should().BeOfType<CssFrequency>().Subject;
        f.Unit.Should().Be(CssFrequencyUnit.Hertz);
        f.Value.Should().Be(440.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#frequency-value", "§8.3")]
    [SpecFact]
    public void Frequency_khz_parses_as_CssFrequency_Kilohertz()
    {
        var v = Parse("1khz");
        var f = v.Should().BeOfType<CssFrequency>().Subject;
        f.Unit.Should().Be(CssFrequencyUnit.Kilohertz);
        f.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#frequency-value", "§8.3")]
    [SpecFact]
    public void Frequency_InHertz_converts_khz_to_hz()
    {
        // 1khz = 1000hz
        var f = Parse("1khz").Should().BeOfType<CssFrequency>().Subject;
        f.InHertz.Should().BeApproximately(1000.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#frequency-value", "§8.3")]
    [SpecFact]
    public void Frequency_InHertz_for_hz_is_identity()
    {
        var f = Parse("440hz").Should().BeOfType<CssFrequency>().Subject;
        f.InHertz.Should().Be(440.0);
    }

    // ---------------------------------------------------------------------------
    // §8.4 — Resolution values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_dpi_parses_as_CssResolution_Dpi()
    {
        var v = Parse("96dpi");
        var r = v.Should().BeOfType<CssResolution>().Subject;
        r.Unit.Should().Be(CssResolutionUnit.Dpi);
        r.Value.Should().Be(96.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_dpcm_parses_as_CssResolution_Dpcm()
    {
        var v = Parse("37dpcm");
        var r = v.Should().BeOfType<CssResolution>().Subject;
        r.Unit.Should().Be(CssResolutionUnit.Dpcm);
        r.Value.Should().Be(37.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_dppx_parses_as_CssResolution_Dppx()
    {
        var v = Parse("2dppx");
        var r = v.Should().BeOfType<CssResolution>().Subject;
        r.Unit.Should().Be(CssResolutionUnit.Dppx);
        r.Value.Should().Be(2.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_x_alias_parses_as_dppx()
    {
        // CSS Values 4 §8.4: 'x' is an alias for 'dppx'.
        var v = Parse("1x");
        var r = v.Should().BeOfType<CssResolution>().Subject;
        r.Unit.Should().Be(CssResolutionUnit.Dppx);
        r.Value.Should().Be(1.0);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_InDppx_converts_dpi_to_dppx()
    {
        // 96dpi = 1dppx
        var r = Parse("96dpi").Should().BeOfType<CssResolution>().Subject;
        r.InDppx.Should().BeApproximately(1.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#resolution-value", "§8.4")]
    [SpecFact]
    public void Resolution_InDppx_converts_dpcm_to_dppx()
    {
        // 1dpcm * 2.54 / 96 dppx
        var r = Parse("1dpcm").Should().BeOfType<CssResolution>().Subject;
        r.InDppx.Should().BeApproximately(2.54 / 96.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: calc()
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_two_px_plus_three_px_folds_to_five_px()
    {
        // calc(2px + 3px) — both absolute; engine folds at parse time.
        var v = Parse("calc(2px + 3px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_two_times_three_px_folds_to_six_px()
    {
        // calc(2 * 3px)
        var v = Parse("calc(2 * 3px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(6.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_ten_px_divided_by_two_folds_to_five_px()
    {
        // calc(10px / 2)
        var v = Parse("calc(10px / 2)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_subtraction_folds_correctly()
    {
        var v = Parse("calc(10px - 4px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(6.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_pure_number_folds_to_CssNumber()
    {
        var v = Parse("calc(2 + 3)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_pure_number_multiply_folds()
    {
        var v = Parse("calc(4 * 5)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(20.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_100pct_minus_10px_stays_as_CssCalc()
    {
        // Mixed length + percentage cannot fold at parse time.
        var v = Parse("calc(100% - 10px)");
        v.Should().BeOfType<CssCalc>();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_vh_minus_px_stays_as_CssCalc()
    {
        // Viewport-relative unit — cannot fold without resolution context.
        var v = Parse("calc(100vh - 80px)");
        v.Should().BeOfType<CssCalc>();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_em_mixed_stays_as_CssCalc()
    {
        var v = Parse("calc(1em + 10px)");
        v.Should().BeOfType<CssCalc>();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_percentage_only_folds_to_CssPercentage()
    {
        var v = Parse("calc(50% + 25%)");
        var p = v.Should().BeOfType<CssPercentage>().Subject;
        p.Value.Should().BeApproximately(75.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_nested_absolute_folds()
    {
        // calc(calc(2px + 3px) + 1px) = 6px — inner calc folds first.
        var v = Parse("calc(calc(2px + 3px) + 1px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(6.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_mixed_absolute_units_folds_to_px()
    {
        // calc(1in + 0px) — both absolute; result in px. 1in = 96px.
        var v = Parse("calc(1in + 0px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(96.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_angle_folds_to_CssAngle()
    {
        // calc(45deg + 45deg) = 90deg
        var v = Parse("calc(45deg + 45deg)");
        var a = v.Should().BeOfType<CssAngle>().Subject;
        a.InDegrees.Should().BeApproximately(90.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_time_same_unit_folds_to_CssTime()
    {
        // calc(2s + 1s) — both time, same unit — reducer should fold per spec.
        var v = Parse("calc(2s + 1s)");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.Value.Should().BeApproximately(3.0, Tolerance);
        t.Unit.Should().Be(CssTimeUnit.Seconds);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_constant_pi_is_recognized()
    {
        // calc(pi) = 3.14159...
        var v = Parse("calc(pi)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(Math.PI, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_constant_e_is_recognized()
    {
        var v = Parse("calc(e)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(Math.E, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: min() / max()
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Min_two_absolute_px_folds_to_smaller()
    {
        var v = Parse("min(1px, 2px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(1.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Max_two_absolute_px_folds_to_larger()
    {
        var v = Parse("max(1px, 2px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(2.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Min_with_relative_unit_stays_as_CssCalc()
    {
        var v = Parse("min(1em, 100px)");
        v.Should().BeOfType<CssCalc>();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Max_with_relative_unit_stays_as_CssCalc()
    {
        var v = Parse("max(50vw, 300px)");
        v.Should().BeOfType<CssCalc>();
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: clamp()
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Clamp_all_absolute_folds_to_clamped_value()
    {
        // clamp(1px, 5px, 10px) = 5px (value within bounds).
        var v = Parse("clamp(1px, 5px, 10px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Clamp_below_min_returns_min()
    {
        // clamp(10px, 2px, 20px) = 10px (value below min).
        var v = Parse("clamp(10px, 2px, 20px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(10.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Clamp_above_max_returns_max()
    {
        // clamp(1px, 50px, 10px) = 10px (value above max).
        var v = Parse("clamp(1px, 50px, 10px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(10.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Clamp_with_relative_unit_stays_as_CssCalc()
    {
        var v = Parse("clamp(1rem, 2vw + 1rem, 3rem)");
        v.Should().BeOfType<CssCalc>();
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: round()
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#round-func", "§10")]
    [SpecFact]
    public void Round_nearest_folds_correctly()
    {
        // round(nearest, 7px, 5px) = 5px (7 rounds to nearest 5)
        var v = Parse("round(nearest, 7px, 5px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#round-func", "§10")]
    [SpecFact]
    public void Round_up_folds_correctly()
    {
        // round(up, 6px, 5px) = 10px
        var v = Parse("round(up, 6px, 5px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(10.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#round-func", "§10")]
    [SpecFact]
    public void Round_down_folds_correctly()
    {
        // round(down, 9px, 5px) = 5px
        var v = Parse("round(down, 9px, 5px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(5.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: mod() / rem()
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#funcdef-mod", "§10")]
    [SpecFact]
    public void Mod_positive_values_folds()
    {
        // mod(18px, 5px) = 3px (18 mod 5 = 3)
        var v = Parse("mod(18px, 5px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(3.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#funcdef-rem", "§10")]
    [SpecFact]
    public void Rem_positive_values_folds()
    {
        // rem(18px, 5px) = 3px
        var v = Parse("rem(18px, 5px)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(3.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §10 — Math functions: trig
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Sin_zero_rad_is_zero()
    {
        var v = Parse("sin(0)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(0.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Cos_zero_is_one()
    {
        var v = Parse("cos(0)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(1.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Sqrt_four_is_two()
    {
        var v = Parse("sqrt(4)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(2.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Pow_two_three_is_eight()
    {
        var v = Parse("pow(2, 3)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(8.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Abs_negative_number_is_positive()
    {
        var v = Parse("abs(-5)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(5.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Sign_negative_number_is_minus_one()
    {
        var v = Parse("sign(-42)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(-1.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#trig-funcs", "§10")]
    [SpecFact]
    public void Hypot_three_four_is_five()
    {
        var v = Parse("hypot(3, 4)");
        var n = v.Should().BeOfType<CssNumber>().Subject;
        n.Value.Should().BeApproximately(5.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §7 — String values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#strings", "§7")]
    [SpecFact]
    public void String_double_quoted_parses_as_CssString()
    {
        var v = Parse("\"hello world\"");
        var s = v.Should().BeOfType<CssString>().Subject;
        s.Value.Should().Be("hello world");
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#strings", "§7")]
    [SpecFact]
    public void String_single_quoted_parses_as_CssString()
    {
        var v = Parse("'hello'");
        var s = v.Should().BeOfType<CssString>().Subject;
        s.Value.Should().Be("hello");
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#strings", "§7")]
    [SpecFact]
    public void Empty_string_parses()
    {
        var v = Parse("''");
        var s = v.Should().BeOfType<CssString>().Subject;
        s.Value.Should().Be("");
    }

    // ---------------------------------------------------------------------------
    // §9 — URL values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#urls", "§9")]
    [SpecFact]
    public void Url_function_with_quoted_string_parses_as_CssUrl()
    {
        var v = Parse("url('https://example.com/bg.png')");
        var u = v.Should().BeOfType<CssUrl>().Subject;
        u.Value.Should().Be("https://example.com/bg.png");
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#urls", "§9")]
    [SpecFact]
    public void Url_bare_token_parses_as_CssUrl()
    {
        // Bare url() token (no inner quotes) tokenizes as url-token.
        var v = Parse("url(https://example.com/)");
        var u = v.Should().BeOfType<CssUrl>().Subject;
        u.Value.Should().Be("https://example.com/");
    }

    // ---------------------------------------------------------------------------
    // §11 — attr() values
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#funcdef-attr", "§11")]
    [SpecFact]
    public void Attr_name_only_parses_as_CssAttrReference()
    {
        var v = Parse("attr(data-foo)");
        var a = v.Should().BeOfType<CssAttrReference>().Subject;
        a.AttrName.Should().Be("data-foo");
        a.TypeOrUnit.Should().BeNull();
        a.Fallback.Should().BeNull();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#funcdef-attr", "§11")]
    [SpecFact]
    public void Attr_with_type_and_fallback_parses_correctly()
    {
        var v = Parse("attr(data-count number, 0)");
        var a = v.Should().BeOfType<CssAttrReference>().Subject;
        a.AttrName.Should().Be("data-count");
        a.TypeOrUnit.Should().Be("number");
        a.Fallback.Should().BeOfType<CssNumber>().Which.Value.Should().Be(0);
    }

    // ---------------------------------------------------------------------------
    // §3 — var() custom property references
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#using-variables", "§3")]
    [SpecFact]
    public void Var_reference_without_fallback_parses_as_CssVarReference()
    {
        var v = Parse("var(--my-color)");
        var r = v.Should().BeOfType<CssVarReference>().Subject;
        r.Name.Should().Be("--my-color");
        r.Fallback.Should().BeNull();
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#using-variables", "§3")]
    [SpecFact]
    public void Var_reference_with_fallback_parses_fallback()
    {
        var v = Parse("var(--size, 16px)");
        var r = v.Should().BeOfType<CssVarReference>().Subject;
        r.Name.Should().Be("--size");
        r.Fallback.Should().BeOfType<CssLength>()
            .Which.Value.Should().Be(16.0);
    }

    // ---------------------------------------------------------------------------
    // §3 — env() references
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#using-variables", "§3")]
    [SpecFact]
    public void Env_reference_parses_as_CssEnvReference()
    {
        var v = Parse("env(safe-area-inset-top, 0px)");
        var e = v.Should().BeOfType<CssEnvReference>().Subject;
        e.Name.Should().Be("safe-area-inset-top");
        e.Fallback.Should().BeOfType<CssLength>();
    }

    // ---------------------------------------------------------------------------
    // §6 — CssLength.Zero sentinel
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#length-value", "§6")]
    [SpecFact]
    public void CssLength_Zero_is_zero_px()
    {
        CssLength.Zero.Value.Should().Be(0.0);
        CssLength.Zero.Unit.Should().Be(CssLengthUnit.Px);
    }

    // ---------------------------------------------------------------------------
    // §10 — NumericType resolution for calc() expression types
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-type-checking", "§10.2")]
    [SpecFact]
    public void Calc_mixed_length_and_percentage_has_LengthPercentage_type()
    {
        var v = Parse("calc(100% - 10px)");
        var c = v.Should().BeOfType<CssCalc>().Subject;
        c.Expression.Type.Should().Be(NumericType.LengthPercentage);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-type-checking", "§10.2")]
    [SpecFact]
    public void Calc_pure_length_has_Length_type()
    {
        // calc(10px + 5em) — both length, but cannot fold (em relative) — stays CssCalc.
        var v = Parse("calc(10px + 5em)");
        var c = v.Should().BeOfType<CssCalc>().Subject;
        c.Expression.Type.Should().Be(NumericType.Length);
    }

    // ---------------------------------------------------------------------------
    // §10 — CssCalc.Resolve via CssResolutionContext
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_vh_resolves_to_px_given_viewport_height()
    {
        // calc(100vh - 80px) with viewport height 600 → 520px.
        var v = Parse("calc(100vh - 80px)");
        var calc = v.Should().BeOfType<CssCalc>().Subject;
        var ctx = CssResolutionContext.Default with
        {
            ViewportWidthPx = 800,
            ViewportHeightPx = 600,
        };
        var resolved = calc.Resolve(ctx);
        var l = resolved.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(520.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_em_resolves_to_px_given_font_size()
    {
        // calc(2em + 4px) with font-size 16px → 36px.
        var v = Parse("calc(2em + 4px)");
        var calc = v.Should().BeOfType<CssCalc>().Subject;
        var ctx = CssResolutionContext.Default with
        {
            FontSizePx = 16,
        };
        var resolved = calc.Resolve(ctx);
        var l = resolved.Should().BeOfType<CssLength>().Subject;
        l.Value.Should().BeApproximately(36.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // §10 — CssCalcResolver internals — cross-unit absolute folding
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_cm_plus_mm_folds_to_px()
    {
        // calc(1cm + 10mm) = 96/2.54 + 10*(96/25.4) px
        var v = Parse("calc(1cm + 10mm)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        var expected = 96.0 / 2.54 + 10.0 * 96.0 / 25.4;
        l.Value.Should().BeApproximately(expected, 1e-6);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_pt_plus_pc_folds_to_px()
    {
        // calc(12pt + 1pc) = 12*(4/3) + 16 = 16 + 16 = 32px
        var v = Parse("calc(12pt + 1pc)");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(32.0, Tolerance);
    }

    // ---------------------------------------------------------------------------
    // PendingFacts — real gaps or unverified behaviors
    // ---------------------------------------------------------------------------

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#calc-notation", "§10")]
    [SpecFact]
    public void Calc_s_plus_ms_different_units_fold_to_single_time()
    {
        // calc(1s + 500ms) — both absolute time; spec says they should fold.
        // Current engine only folds same-unit time pairs.
        var v = Parse("calc(1s + 500ms)");
        var t = v.Should().BeOfType<CssTime>().Subject;
        t.InSeconds.Should().BeApproximately(1.5, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#frequency-value", "§8.3")]
    [SpecFact]
    public void Calc_hz_plus_khz_folds_to_single_frequency()
    {
        var v = Parse("calc(500hz + 0.5khz)");
        var f = v.Should().BeOfType<CssFrequency>().Subject;
        f.InHertz.Should().BeApproximately(1000.0, Tolerance);
    }

    [Spec("css-values-4", "https://www.w3.org/TR/css-values-4/#number-value", "§4")]
    [SpecFact]
    public void Scientific_notation_dimension_parses_correctly()
    {
        // 1e2px means 100px per CSS Syntax 3.
        var v = Parse("1e2px");
        var l = v.Should().BeOfType<CssLength>().Subject;
        l.Unit.Should().Be(CssLengthUnit.Px);
        l.Value.Should().BeApproximately(100.0, Tolerance);
    }
}
