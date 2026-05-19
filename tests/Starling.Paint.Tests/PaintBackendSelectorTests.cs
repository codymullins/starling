using FluentAssertions;
using Starling.Paint.Backend;
using Xunit;

namespace Starling.Paint.Tests;

public sealed class PaintBackendSelectorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_or_blank_defaults_to_imagesharp_webgpu(string? value)
        => PaintBackendSelector.Parse(value).Should().Be(PaintBackendKind.ImageSharpWebGpu);

    [Theory]
    [InlineData("imagesharp", "ImageSharp")]
    [InlineData("ImageSharp", "ImageSharp")]
    [InlineData(" IMAGESHARP ", "ImageSharp")]
    [InlineData("imagesharp-webgpu", "ImageSharpWebGpu")]
    [InlineData("imagesharp-gpu", "ImageSharpWebGpu")]
    public void Recognised_values_parse_case_and_whitespace_insensitive(string value, string expectedName)
        => PaintBackendSelector.Parse(value).ToString().Should().Be(expectedName);

    [Theory]
    [InlineData("skia")]
    [InlineData("graphite")]
    [InlineData("vello")]
    public void Unknown_values_throw_naming_the_env_var_and_allowed_values(string value)
    {
        var act = () => PaintBackendSelector.Parse(value);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*STARLING_PAINT_BACKEND*")
            .WithMessage("*imagesharp*");
    }
}
