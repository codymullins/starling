using FluentAssertions;
using Starling.Paint.Backend;
namespace Starling.Paint.Tests;

[TestClass]
public sealed class PaintBackendSelectorTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    public void Unset_or_blank_defaults_to_imagesharp_webgpu(string? value)
        => PaintBackendSelector.Parse(value).Should().Be(PaintBackendKind.ImageSharpWebGpu);

    [TestMethod]
    [DataRow("imagesharp", "ImageSharp")]
    [DataRow("ImageSharp", "ImageSharp")]
    [DataRow(" IMAGESHARP ", "ImageSharp")]
    [DataRow("imagesharp-webgpu", "ImageSharpWebGpu")]
    [DataRow("imagesharp-gpu", "ImageSharpWebGpu")]
    public void Recognised_values_parse_case_and_whitespace_insensitive(string value, string expectedName)
        => PaintBackendSelector.Parse(value).ToString().Should().Be(expectedName);

    [TestMethod]
    [DataRow("skia")]
    [DataRow("graphite")]
    [DataRow("vello")]
    public void Unknown_values_throw_naming_the_env_var_and_allowed_values(string value)
    {
        var act = () => PaintBackendSelector.Parse(value);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*STARLING_PAINT_BACKEND*")
            .WithMessage("*imagesharp*");
    }
}
