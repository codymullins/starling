using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// LTF-02: per-layer content-hash cache key. The layer cache is keyed by a hash
/// of the slice's content (which excludes the composite-time transform/opacity),
/// so a transform/opacity-only frame hits cache (zero backend rasters) while a
/// real content change re-rasters exactly the changed layer — verified with a
/// backend wrapper that counts raster calls.
/// </summary>
[TestClass]
public sealed class LayerContentCacheTests
{
    private static BlockBox Layout(StyleEngine style, Starling.Dom.Document doc, int w, int h)
        => new LayoutEngine(style, DefaultTextMeasurer.Instance).LayoutDocument(doc, new Size(w, h));

    [TestMethod]
    public void Transform_only_change_does_not_re_raster_any_layer()
    {
        const int W = 160, H = 160;
        const float scale = 1f;
        const string baseStyle =
            "position:absolute;left:40px;top:40px;width:60px;height:60px;" +
            "background-color:#cc2222;will-change:transform;";

        var doc = HtmlParser.Parse(
            "<body style=\"margin:0;height:160px;background-color:#eef0ff\">" +
            $"<div id=box style=\"{baseStyle}transform:rotate(10deg)\"></div></body>");
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        using var inner = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var counting = new CountingBackend(inner);
        var store = new LayerCacheStore();
        var compositor = new CompositorEngine(counting);

        // Frame 1 seeds the caches: root + promoted div each raster once.
        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        var tree1 = new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root1);
        using (compositor.Render(tree1, new LayoutRect(0, 0, W, H), scale)) { }
        counting.RenderCount.Should().Be(2, "the first frame rasters the root and the promoted div");

        // Frame 2 changes ONLY the rotation — a composite-time property suppressed
        // from the slice, so both slices hash identically and serve from cache.
        doc.GetElementById("box")!.SetAttribute("style", baseStyle + "transform:rotate(70deg)");
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root2);
        using (compositor.Render(tree2, new LayoutRect(0, 0, W, H), scale)) { }
        counting.RenderCount.Should().Be(2, "a transform-only change re-blits every layer from cache — no new raster");
    }

    [TestMethod]
    public void Content_change_re_rasters_only_the_changed_layer()
    {
        const int W = 160, H = 160;
        const float scale = 1f;
        const string baseStyle =
            "position:absolute;left:40px;top:40px;width:60px;height:60px;will-change:transform;";

        var doc = HtmlParser.Parse(
            "<body style=\"margin:0;height:160px;background-color:#eef0ff\">" +
            $"<div id=box style=\"{baseStyle}background-color:#cc2222\"></div></body>");
        var style = new StyleEngine();
        var engine = new LayoutEngine(style, DefaultTextMeasurer.Instance);
        using var inner = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var counting = new CountingBackend(inner);
        var store = new LayerCacheStore();
        var compositor = new CompositorEngine(counting);

        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        var tree1 = new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root1);
        using (compositor.Render(tree1, new LayoutRect(0, 0, W, H), scale)) { }
        counting.RenderCount.Should().Be(2);

        // Change only the promoted div's background colour: its slice content
        // changes (new hash → re-raster) but the root slice is unchanged (hit).
        doc.GetElementById("box")!.SetAttribute("style", baseStyle + "background-color:#22cc22");
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root2);
        using (compositor.Render(tree2, new LayoutRect(0, 0, W, H), scale)) { }
        counting.RenderCount.Should().Be(3, "exactly one layer (the recoloured div) re-rasters; the root serves from cache");
    }

    [TestMethod]
    public void Cached_reblit_is_byte_identical_to_a_from_scratch_render()
    {
        const int W = 160, H = 160;
        const float scale = 1f;
        var html =
            "<body style=\"margin:0;height:160px;background-color:#eef0ff\">" +
            "<div style=\"opacity:0.7;position:absolute;left:20px;top:20px;width:80px;height:80px;background-color:#cc2222\">Hi</div>" +
            "</body>";

        var doc = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        // A persistent-store compositor that has already cached every layer.
        var store = new LayerCacheStore();
        var cached = new CompositorEngine(backend);
        var root = engine.LayoutDocument(doc, new Size(W, H));
        using (cached.Render(new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root), new LayoutRect(0, 0, W, H), scale)) { }
        using var reblit = cached.Render(new LayerTreeBuilder(null, null, null, store.CacheFor).Build(root), new LayoutRect(0, 0, W, H), scale);

        // A cold compositor with its own fresh per-layer caches (no persistence).
        var fresh = new CompositorEngine(backend);
        using var scratch = fresh.Render(new LayerTreeBuilder().Build(root), new LayoutRect(0, 0, W, H), scale);

        reblit.Width.Should().Be(scratch.Width);
        reblit.Height.Should().Be(scratch.Height);
        reblit.Rgba.AsSpan().SequenceEqual(scratch.Rgba).Should()
            .BeTrue("a content-cache HIT must reproduce the from-scratch raster byte-for-byte");
    }

    /// <summary>An <see cref="IPaintBackend"/> wrapper that forwards to an inner
    /// backend and counts how many slices it actually rasterized.</summary>
    private sealed class CountingBackend(IPaintBackend inner) : IPaintBackend
    {
        public int RenderCount { get; private set; }
        public string Name => inner.Name;

        public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale = 1.0f)
        {
            RenderCount++;
            return inner.Render(list, viewport, scale);
        }

        public RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale, bool opaqueBackground)
        {
            RenderCount++;
            return inner.Render(list, viewport, scale, opaqueBackground);
        }

        public void Dispose() => inner.Dispose();
    }
}
