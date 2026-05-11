using FluentAssertions;
using SixLabors.ImageSharp;
using Tessera.Html;
using Xunit;

namespace Tessera.Engine.Tests;

public class EngineRenderTests
{
    [Fact]
    public void Render_writes_a_non_empty_png_for_hello_html()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.html");
        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");

        try
        {
            File.WriteAllText(fixture, "<!doctype html><body><p>Hello, world.</p></body>");
            var engine = new TesseraEngine();
            var result = engine.Render(
                "file://" + fixture.Replace('\\', '/'),
                new RenderOptions(new Size(400, 200), 28f),
                output);

            result.IsOk.Should().BeTrue($"Render should succeed; got: {(result.IsErr ? result.Error.Message : "")}");
            File.Exists(output).Should().BeTrue();
            new FileInfo(output).Length.Should().BeGreaterThan(100, "PNG header alone is ~50 bytes; real output is larger");
            result.Value.DisplayText.Should().Be("Hello, world.");
        }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public void Render_returns_err_for_missing_file()
    {
        var engine = new TesseraEngine();
        var result = engine.Render(
            "file:///definitely-not-there.html",
            RenderOptions.Default,
            Path.Combine(Path.GetTempPath(), "unused.png"));

        result.IsErr.Should().BeTrue();
        result.Error.Message.Should().Contain("File not found");
    }

    [Fact]
    public void ExtractDisplayText_collapses_whitespace()
    {
        var doc = HtmlParser.Parse("<body>  Hello,   world. \n\t Next line. </body>");
        TesseraEngine.ExtractDisplayText(doc).Should().Be("Hello, world. Next line.");
    }
}
