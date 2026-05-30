using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;

namespace Starling.Paint.Tests;

/// <summary>
/// LTF-05: the fast integer-aligned, opacity-1 composite blit must be
/// byte-identical to the general inverse-mapped bilinear path. The general path
/// is forced via the internal <c>DisableFastBlit</c> test seam.
/// </summary>
[TestClass]
public sealed class FastBlitTests
{
    private static BlockBox Layout(string html, int w, int h)
        => new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance)
            .LayoutDocument(HtmlParser.Parse(html), new Size(w, h));

    [TestMethod]
    public void Fast_blit_is_byte_identical_to_the_general_path()
    {
        const int W = 240, H = 180;
        const float scale = 1f;
        // A page with an opaque base layer plus a translucent promoted layer and
        // text — exercises both opaque straight-copy and partial-alpha-over rows.
        var html =
            "<body style=\"margin:0;background-color:#ffffff\">" +
            "<div style=\"width:240px;height:80px;background-color:#3366cc\">Header text</div>" +
            "<div style=\"opacity:0.6;position:absolute;left:20px;top:40px;width:120px;height:90px;background-color:#cc2222\"></div>" +
            "</body>";
        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        // Identity transforms throughout → every layer qualifies for the fast blit.
        var tree = new LayerTreeBuilder().Build(root);
        using var fast = new CompositorEngine(backend).Render(tree, new LayoutRect(0, 0, W, H), scale);

        var treeGeneral = new LayerTreeBuilder().Build(root);
        using var general = new CompositorEngine(backend) { DisableFastBlit = true }
            .Render(treeGeneral, new LayoutRect(0, 0, W, H), scale);

        fast.Width.Should().Be(general.Width);
        fast.Height.Should().Be(general.Height);
        fast.Rgba.AsSpan().SequenceEqual(general.Rgba).Should()
            .BeTrue("the fast integer-aligned blit must reproduce the general bilinear path exactly");
    }

    [TestMethod]
    public void Fast_blit_matches_general_path_with_overflow_clip()
    {
        const int W = 200, H = 200;
        const float scale = 1f;
        // overflow:hidden installs an axis-aligned clip; the fast path clamps to it.
        var html =
            "<body style=\"margin:0;background-color:#101820\">" +
            "<div style=\"overflow:hidden;position:absolute;left:10px;top:10px;width:80px;height:80px\">" +
            "<div style=\"width:200px;height:200px;background-color:#e0a020\"></div></div>" +
            "</body>";
        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        using var fast = new CompositorEngine(backend)
            .Render(new LayerTreeBuilder().Build(root), new LayoutRect(0, 0, W, H), scale);
        using var general = new CompositorEngine(backend) { DisableFastBlit = true }
            .Render(new LayerTreeBuilder().Build(root), new LayoutRect(0, 0, W, H), scale);

        fast.Rgba.AsSpan().SequenceEqual(general.Rgba).Should()
            .BeTrue("the clipped fast blit must match the clipped general path exactly");
    }
}
