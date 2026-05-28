using System.Text;
using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Css.Values;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Acceptance tests for wp:M12-01 viewport-clipped paint: the output bitmap is
/// sized to the viewport (not <c>root.Frame</c>), the display list reaching the
/// backend contains only on-screen items (post-transform AABB), and content is
/// correctly translated by the viewport offset.
/// </summary>
[TestClass]
public sealed class ViewportClipTests
{
    private static BlockBox LayoutTallPage(int blocks, int blockHeightPx, double viewportWidth = 800)
    {
        // Each block is a fixed-height div with a unique background color so the
        // display list emits one FillRect per block at a known page-Y.
        var sb = new StringBuilder("<body style=\"margin:0\">");
        for (var i = 0; i < blocks; i++)
        {
            // Vary the color channel a little so blocks are distinguishable.
            var g = (byte)(i % 200 + 30);
            sb.Append($"<div style=\"margin:0;height:{blockHeightPx}px;background-color:rgb(10,{g},20)\"></div>");
        }
        sb.Append("</body>");

        var document = HtmlParser.Parse(sb.ToString());
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        return engine.LayoutDocument(document, new LayoutSize(viewportWidth, 600));
    }

    [TestMethod]
    public void Tall_page_renders_bitmap_sized_to_viewport_not_frame()
    {
        // ~200000 px tall: 1000 blocks of 200 px.
        var root = LayoutTallPage(blocks: 1000, blockHeightPx: 200);
        root.Frame.Height.Should().BeGreaterThan(190000,
            "the synthetic page must be far taller than any single texture");

        var viewport = new LayoutRect(0, 0, 800, 600);
        var dl = new DisplayListBuilder().Build(root, viewport);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(dl, viewport);

        bmp.Width.Should().Be(800);
        bmp.Height.Should().Be(600);
    }

    [TestMethod]
    public void Culled_display_list_is_O_on_screen_not_O_page()
    {
        var root = LayoutTallPage(blocks: 1000, blockHeightPx: 200);
        var viewport = new LayoutRect(0, 0, 800, 600);

        var full = new DisplayListBuilder().Build(root); // no culling
        var culled = new DisplayListBuilder().Build(root, viewport);

        // The full list has ~one fill per block; the culled list only the few
        // blocks intersecting a 600 px window (+128 px overdraw).
        full.Items.OfType<FillRect>().Should().HaveCountGreaterThan(900);
        culled.Items.Count.Should().BeLessThan(full.Items.Count / 10,
            "viewport culling must drop the off-screen blocks");

        // A block at page-Y ≈ 100000 (block index 500) is far off-screen and
        // must be absent; a block near the top is present.
        culled.Items.OfType<FillRect>()
            .Should().Contain(r => r.Bounds.Y < 600, "an on-screen block must survive");
        culled.Items.OfType<FillRect>()
            .Should().NotContain(r => r.Bounds.Y > 50000,
                "a block 50000 px down the page must be culled");
    }

    [TestMethod]
    public void Item_far_offscreen_in_local_space_is_kept_when_transformed_onto_viewport()
    {
        // A box laid out at page-Y ~5000 (off-screen for a 0..600 viewport) but
        // translated up by 5000 px lands on-screen. Culling must test the
        // POST-transform AABB, so the FillRect survives.
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"margin:0;height:5000px\"></div>" +
            "<div style=\"margin:0;height:100px;background-color:#ff0000;transform:translateY(-5000px)\"></div>" +
            "</body>";
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(800, 600));

        var viewport = new LayoutRect(0, 0, 800, 600);
        var dl = new DisplayListBuilder().Build(root, viewport);

        dl.Items.OfType<PushTransform>().Should().NotBeEmpty(
            "the transformed subtree must emit its bracket because it is now on-screen");
        dl.Items.OfType<FillRect>()
            .Should().Contain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0,
                "the red box is transformed onto the viewport and must survive culling");
    }

    [TestMethod]
    public void Transformed_subtree_offscreen_after_transform_is_culled_with_balanced_brackets()
    {
        // The red box is on-screen in local coords but translated 6000 px down,
        // off the viewport. Its whole transformed subtree (incl. the bracket)
        // must be skipped.
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"margin:0;height:100px;background-color:#ff0000;transform:translateY(6000px)\"></div>" +
            "</body>";
        var document = HtmlParser.Parse(html);
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(800, 600));

        var viewport = new LayoutRect(0, 0, 800, 600);
        var dl = new DisplayListBuilder().Build(root, viewport);

        dl.Items.OfType<FillRect>()
            .Should().NotContain(r => r.Color.R == 255 && r.Color.G == 0 && r.Color.B == 0,
                "a box transformed off the viewport must be culled");
        // Whatever the list contains, push/pop must stay balanced.
        dl.Items.OfType<PushTransform>().Count()
            .Should().Be(dl.Items.OfType<PopTransform>().Count(),
                "transform brackets must remain balanced after culling");
    }

    [TestMethod]
    public void Viewport_offset_translates_content_to_top_of_bitmap()
    {
        // A red rect spanning page-Y 10000..10100. Rendering a viewport that
        // starts at page-Y 10000 must place the red at the top of the bitmap.
        var list = new PaintList();
        list.Add(new FillRect(new LayoutRect(0, 10000, 800, 100), new CssColor(255, 0, 0, 255), FillRectPixelAlignment.Preserve));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var viewport = new LayoutRect(0, 10000, 800, 600);
        using var bmp = backend.Render(list, viewport);

        bmp.Width.Should().Be(800);
        bmp.Height.Should().Be(600);

        // Top rows should be red (the rect moved to device-Y 0); bottom rows
        // are the white background.
        bmp.GetPixel(400, 5).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        bmp.GetPixel(400, 500).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255));
    }

    [TestMethod]
    public void Fixed_positioned_box_is_translated_by_viewport_offset()
    {
        // A page taller than the viewport with a `position: fixed` bar pinned
        // to the top. Layout writes its frame in viewport-relative coords
        // (Y=0 against the initial containing block). When we render a
        // viewport scrolled down by 1000 px the painter must shift the fixed
        // subtree by the viewport origin so the bar stays at the top of the
        // visible region — i.e. its emitted page-Y is 1000, inside the cull
        // rect, instead of 0 (off-screen, culled).
        var html = """
            <body style="margin:0">
              <div style="position:fixed;top:0;left:0;width:800px;height:50px;background-color:rgb(255,0,0)"></div>
              <div style="height:3000px;background-color:rgb(200,200,200)"></div>
            </body>
            """;
        var document = HtmlParser.Parse(html);
        var styleEngine = new StyleEngine();
        var engine = new LayoutEngine(styleEngine, DefaultTextMeasurer.Instance);
        var root = engine.LayoutDocument(document, new LayoutSize(800, 600));

        var viewport = new LayoutRect(0, 1000, 800, 600);
        var dl = new DisplayListBuilder().Build(root, viewport);

        // The fixed bar's FillRect should land at page-Y 1000 (= viewport.Y),
        // not 0, since the painter rebased the fixed subtree onto the
        // viewport origin.
        var redRect = dl.Items.OfType<FillRect>().FirstOrDefault(f =>
            f.Color.R == 255 && f.Color.G == 0 && f.Color.B == 0);
        redRect.Should().NotBeNull("the fixed red bar must survive culling at the scrolled viewport");
        redRect!.Bounds.Y.Should().Be(1000, "fixed-position translation should rebase Y to the viewport origin");
        redRect.Bounds.Height.Should().Be(50);
    }

    [TestMethod]
    public void Null_viewport_emits_every_item_unchanged()
    {
        var root = LayoutTallPage(blocks: 20, blockHeightPx: 100);
        var withNull = new DisplayListBuilder().Build(root, viewport: null);
        var legacy = new DisplayListBuilder().Build(root);

        withNull.Items.Count.Should().Be(legacy.Items.Count,
            "passing a null viewport must reproduce the no-cull behavior exactly");
        withNull.Items.OfType<FillRect>().Should().HaveCountGreaterThanOrEqualTo(20);
    }
}
