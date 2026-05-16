using FluentAssertions;
using Tessera.Css.Values;
using Xunit;

namespace Tessera.Css.Tests;

public class CssCalcResolverTests
{
    private static CssResolutionContext Ctx(double fontSize = 16, double rootFontSize = 16,
        double lineHeight = 19.2, double vw = 1024, double vh = 768,
        double cw = 1024, double ch = 768, double pctBasis = 0)
        => CssResolutionContext.Default with
        {
            FontSizePx = fontSize,
            RootFontSizePx = rootFontSize,
            LineHeightPx = lineHeight,
            RootLineHeightPx = lineHeight,
            ViewportWidthPx = vw,
            ViewportHeightPx = vh,
            SmallViewportWidthPx = vw,
            SmallViewportHeightPx = vh,
            LargeViewportWidthPx = vw,
            LargeViewportHeightPx = vh,
            DynamicViewportWidthPx = vw,
            DynamicViewportHeightPx = vh,
            ContainerWidthPx = cw,
            ContainerHeightPx = ch,
            PercentageBasisPx = pctBasis,
        };

    [Fact]
    public void Em_resolves_against_FontSize()
    {
        var l = new CssLength(2, CssLengthUnit.Em);
        var r = CssCalcResolver.Resolve(l, Ctx(fontSize: 20));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(40);
    }

    [Fact]
    public void Rem_resolves_against_RootFontSize()
    {
        var l = new CssLength(1.5, CssLengthUnit.Rem);
        var r = CssCalcResolver.Resolve(l, Ctx(rootFontSize: 18));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(27);
    }

    [Fact]
    public void Lh_resolves_against_LineHeight()
    {
        var l = new CssLength(2, CssLengthUnit.Lh);
        var r = CssCalcResolver.Resolve(l, Ctx(lineHeight: 20));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(40);
    }

    [Fact]
    public void Vh_and_Vw_resolve_against_viewport()
    {
        var ctx = Ctx(vw: 1000, vh: 500);
        CssCalcResolver.Resolve(new CssLength(50, CssLengthUnit.Vw), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(500);
        CssCalcResolver.Resolve(new CssLength(100, CssLengthUnit.Vh), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(500);
    }

    [Fact]
    public void Svh_lvh_dvh_resolve_independently()
    {
        var ctx = CssResolutionContext.Default with
        {
            SmallViewportHeightPx = 500,
            LargeViewportHeightPx = 900,
            DynamicViewportHeightPx = 700,
        };
        CssCalcResolver.Resolve(new CssLength(100, CssLengthUnit.Svh), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(500);
        CssCalcResolver.Resolve(new CssLength(100, CssLengthUnit.Lvh), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(900);
        CssCalcResolver.Resolve(new CssLength(100, CssLengthUnit.Dvh), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(700);
    }

    [Fact]
    public void Cqw_resolves_against_container_width()
    {
        var ctx = Ctx(cw: 400, ch: 200);
        CssCalcResolver.Resolve(new CssLength(50, CssLengthUnit.Cqw), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(200);
        CssCalcResolver.Resolve(new CssLength(50, CssLengthUnit.Cqh), ctx)
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(100);
    }

    [Fact]
    public void Percentage_resolves_against_percentage_basis()
    {
        var pct = new CssPercentage(50);
        CssCalcResolver.Resolve(pct, Ctx(pctBasis: 400))
            .Should().BeOfType<CssLength>().Which.Value.Should().Be(200);
    }

    [Fact]
    public void Calc_100vh_minus_80px_resolves_to_pixels()
    {
        // build calc(100vh - 80px) tree manually
        var tree = new CalcBinary(
            CalcOperator.Subtract,
            new CalcLength(100, CssLengthUnit.Vh),
            new CalcLength(80, CssLengthUnit.Px),
            NumericType.Length);
        var calc = new CssCalc(tree);
        var r = calc.Resolve(Ctx(vh: 800));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(720);
    }

    [Fact]
    public void Calc_with_percentage_resolves_against_basis()
    {
        var tree = new CalcBinary(
            CalcOperator.Add,
            new CalcPercentage(50),
            new CalcLength(10, CssLengthUnit.Px),
            NumericType.LengthPercentage);
        var calc = new CssCalc(tree);
        var r = calc.Resolve(Ctx(pctBasis: 200));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(110);
    }

    [Fact]
    public void Calc_min_of_mixed_units_resolves()
    {
        var tree = new CalcFunction("min",
            new CalcNode[] { new CalcLength(50, CssLengthUnit.Vw), new CalcLength(100, CssLengthUnit.Px) },
            NumericType.Length);
        var calc = new CssCalc(tree);
        // 50vw at vw=300 = 150 → min is 100px
        var r = calc.Resolve(Ctx(vw: 300));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(100);
    }

    [Fact]
    public void Calc_clamp_of_mixed_units_resolves()
    {
        var tree = new CalcFunction("clamp",
            new CalcNode[]
            {
                new CalcLength(1, CssLengthUnit.Rem),
                new CalcLength(50, CssLengthUnit.Vw),
                new CalcLength(3, CssLengthUnit.Rem),
            },
            NumericType.Length);
        var calc = new CssCalc(tree);
        // 1rem=16, 50vw at vw=100 → 50, 3rem=48 → clamp(16, 50, 48) = 48
        var r = calc.Resolve(Ctx(rootFontSize: 16, vw: 100));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(48);
    }

    [Fact]
    public void Calc_nested_with_multiply_resolves()
    {
        // calc(2em + 100% * 0.5)
        var tree = new CalcBinary(
            CalcOperator.Add,
            new CalcLength(2, CssLengthUnit.Em),
            new CalcBinary(CalcOperator.Multiply,
                new CalcPercentage(100),
                new CalcNumber(0.5),
                NumericType.Percentage),
            NumericType.LengthPercentage);
        var calc = new CssCalc(tree);
        // 2em = 32, 100% * 0.5 = 50%, basis=200 → 100, total 132
        var r = calc.Resolve(Ctx(fontSize: 16, pctBasis: 200));
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(132);
    }

    [Fact]
    public void Calc_with_absolute_only_passes_through()
    {
        var tree = new CalcBinary(
            CalcOperator.Add,
            new CalcLength(10, CssLengthUnit.Px),
            new CalcLength(20, CssLengthUnit.Px),
            NumericType.Length);
        var calc = new CssCalc(tree);
        var r = calc.Resolve(CssResolutionContext.Default);
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(30);
    }

    [Fact]
    public void Resolve_non_length_value_returns_input()
    {
        var v = new CssNumber(42);
        CssCalcResolver.Resolve(v, CssResolutionContext.Default).Should().BeSameAs(v);
    }

    [Fact]
    public void Resolve_pixel_length_returns_pixels()
    {
        var v = new CssLength(50, CssLengthUnit.Px);
        var r = CssCalcResolver.Resolve(v, CssResolutionContext.Default);
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(50);
        ((CssLength)r).Unit.Should().Be(CssLengthUnit.Px);
    }

    [Fact]
    public void Resolve_absolute_unit_converts_to_px()
    {
        var v = new CssLength(1, CssLengthUnit.In);
        var r = CssCalcResolver.Resolve(v, CssResolutionContext.Default);
        r.Should().BeOfType<CssLength>().Which.Value.Should().Be(96);
    }
}
