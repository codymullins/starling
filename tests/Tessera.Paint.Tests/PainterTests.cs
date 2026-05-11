using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace Tessera.Paint.Tests;

public class PainterTests
{
    [Fact]
    public void RenderHelloWorld_produces_an_image_of_requested_size()
    {
        var painter = new Painter();
        using var img = painter.RenderHelloWorld("Hello, world.", new Size(800, 600));
        img.Width.Should().Be(800);
        img.Height.Should().Be(600);
    }

    [Fact]
    public void Rendered_text_leaves_at_least_some_non_white_pixels()
    {
        var painter = new Painter();
        using var img = painter.RenderText("Hello, world.", new Size(400, 200), fontSize: 48f);

        var nonWhite = 0;
        img.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                foreach (var px in row)
                {
                    if (px.R < 250 || px.G < 250 || px.B < 250)
                    {
                        nonWhite++;
                    }
                }
            }
        });
        nonWhite.Should().BeGreaterThan(50, "drawing 48pt text should produce visible black pixels");
    }
}
