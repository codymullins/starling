using FluentAssertions;
using SixLabors.ImageSharp;
using Tessera.Layout.Box;
using Xunit;

namespace Tessera.Engine.Tests;

/// <summary>
/// Smoke-tests the interactive layout path that powers the GUI: cascade + layout
/// without rasterization, returning a <see cref="LaidOutPage"/> with positions
/// callers can walk to emit native views, hit-test taps, and drive Cmd-F.
/// </summary>
public class EngineLayoutPageTests
{
    [Fact]
    public async Task LayoutPageAsync_returns_box_tree_for_file_url()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"tessera-layout-{Guid.NewGuid():N}.html");
        try
        {
            File.WriteAllText(fixture,
                "<!doctype html><html><head><title>Smoke</title></head>" +
                "<body><h1>Heading</h1><p>First paragraph.</p><p>Second.</p></body></html>");

            var engine = new TesseraEngine();
            var result = await engine.LayoutPageAsync(
                "file://" + fixture.Replace('\\', '/'),
                new RenderOptions(new Size(800, 600), FontSize: 16f),
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            using var page = result.Value;

            page.Title.Should().Be("Smoke");
            page.Viewport.Width.Should().Be(800);
            page.Root.Frame.Width.Should().BeGreaterThan(0);
            page.DocumentHeight.Should().BeGreaterThan(0, "the page should occupy vertical space");
            page.Document.Should().NotBeNull();

            // Box tree should contain text fragments for both paragraphs in the laid-out result.
            var fragments = CollectTextFragments(page.Root).ToList();
            fragments.Should().Contain(f => f.Text.Contains("Heading", StringComparison.Ordinal));
            fragments.Should().Contain(f => f.Text.Contains("First", StringComparison.Ordinal));
            fragments.Should().Contain(f => f.Text.Contains("Second", StringComparison.Ordinal));
        }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
        }
    }

    [Fact]
    public async Task LayoutPageAsync_disposes_idempotently()
    {
        var fixture = Path.Combine(Path.GetTempPath(), $"tessera-layout-{Guid.NewGuid():N}.html");
        try
        {
            File.WriteAllText(fixture, "<!doctype html><body><p>x</p></body>");
            var engine = new TesseraEngine();
            var result = await engine.LayoutPageAsync(
                "file://" + fixture.Replace('\\', '/'),
                new RenderOptions(new Size(200, 200)),
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue();
            var page = result.Value;
            page.Dispose();
            page.Dispose(); // second dispose must be a no-op
        }
        finally
        {
            if (File.Exists(fixture)) File.Delete(fixture);
        }
    }

    private static IEnumerable<(string Text, double Y)> CollectTextFragments(Box box)
    {
        if (box is TextBox tb)
        {
            foreach (var f in tb.Fragments)
                yield return (f.Text, box.Frame.Y + f.Y);
        }
        foreach (var child in box.Children)
            foreach (var f in CollectTextFragments(child))
                yield return f;
    }
}
