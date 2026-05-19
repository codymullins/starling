using FluentAssertions;
using Tessera.Css.Animations;
using Tessera.Css.Properties;
using Tessera.Css.Values;
using Xunit;
using Starling.Spec;

namespace Tessera.Css.Tests;

[Spec("web-animations-1", "https://www.w3.org/TR/web-animations-1/")]

public sealed class InterpolatorTests
{
    [Fact]
    public void Numbers_lerp_linearly()
    {
        var result = (CssNumber)Interpolator.Interpolate(PropertyId.Opacity,
            new CssNumber(0), new CssNumber(1), 0.25);
        result.Value.Should().BeApproximately(0.25, 1e-9);
    }

    [Fact]
    public void Same_unit_lengths_lerp_directly()
    {
        var r = (CssLength)Interpolator.Interpolate(PropertyId.Width,
            new CssLength(10, CssLengthUnit.Px), new CssLength(30, CssLengthUnit.Px), 0.5);
        r.Value.Should().Be(20);
        r.Unit.Should().Be(CssLengthUnit.Px);
    }

    [Fact]
    public void Cross_absolute_unit_lengths_lerp_in_px()
    {
        var r = (CssLength)Interpolator.Interpolate(PropertyId.Width,
            new CssLength(0, CssLengthUnit.Px),
            new CssLength(1, CssLengthUnit.In), 0.5);
        r.Unit.Should().Be(CssLengthUnit.Px);
        r.Value.Should().BeApproximately(48, 1e-6); // 1in = 96px, midpoint = 48px
    }

    [Fact]
    public void Mismatched_value_kinds_fall_back_to_discrete()
    {
        var below = Interpolator.Interpolate(PropertyId.Width,
            new CssLength(10, CssLengthUnit.Px),
            new CssKeyword("auto"), 0.4);
        below.Should().BeOfType<CssLength>();
        var above = Interpolator.Interpolate(PropertyId.Width,
            new CssLength(10, CssLengthUnit.Px),
            new CssKeyword("auto"), 0.7);
        above.Should().BeOfType<CssKeyword>();
    }

    [Fact]
    public void Color_lerps_in_premultiplied_srgb()
    {
        // red → blue midpoint should land near (128, 0, 128) once rounded.
        var mid = (CssColor)Interpolator.Interpolate(PropertyId.Color,
            new CssColor(255, 0, 0), new CssColor(0, 0, 255), 0.5);
        mid.R.Should().BeInRange(126, 130);
        mid.G.Should().Be(0);
        mid.B.Should().BeInRange(126, 130);
        mid.A.Should().Be(255);
    }

    [Fact]
    public void Color_fading_to_transparent_keeps_alpha_lerp()
    {
        var mid = (CssColor)Interpolator.Interpolate(PropertyId.Color,
            new CssColor(255, 0, 0, 255),
            new CssColor(255, 0, 0, 0), 0.5);
        mid.A.Should().BeInRange(126, 130);
    }

    [Fact]
    public void Transform_lerps_pairwise_when_function_signatures_match()
    {
        var from = new CssTransform(new CssTransformFunction[]
        {
            new CssTranslate(new CssLengthOrPercent(0, false), new CssLengthOrPercent(0, false)),
        });
        var to = new CssTransform(new CssTransformFunction[]
        {
            new CssTranslate(new CssLengthOrPercent(100, false), new CssLengthOrPercent(40, false)),
        });
        var mid = (CssTransform)Interpolator.Interpolate(PropertyId.Transform, from, to, 0.25);
        mid.Functions.Should().ContainSingle();
        var translate = (CssTranslate)mid.Functions[0];
        translate.X.Value.Should().BeApproximately(25, 1e-9);
        translate.Y.Value.Should().BeApproximately(10, 1e-9);
    }

    [Fact]
    public void Transform_with_mismatched_signatures_falls_back_to_matrix_lerp()
    {
        var from = new CssTransform(new CssTransformFunction[]
        {
            new CssTranslate(new CssLengthOrPercent(0, false), new CssLengthOrPercent(0, false)),
        });
        var to = new CssTransform(new CssTransformFunction[]
        {
            new CssScale(2, 2),
        });
        var mid = (CssTransform)Interpolator.Interpolate(PropertyId.Transform, from, to, 0.5);
        mid.Functions.Should().ContainSingle();
        mid.Functions[0].Should().BeOfType<CssMatrix>();
    }

    [Fact]
    public void IsAnimatable_returns_true_for_opacity_and_color()
    {
        Interpolator.IsAnimatable(PropertyId.Opacity).Should().BeTrue();
        Interpolator.IsAnimatable(PropertyId.Color).Should().BeTrue();
        Interpolator.IsAnimatable(PropertyId.Transform).Should().BeTrue();
        Interpolator.IsAnimatable(PropertyId.Display).Should().BeFalse();
    }
}
