using System.Collections.Concurrent;
using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Paint.Backend;
using Starling.Paint.Compositor;
using Starling.Paint.DisplayList;
using CompositorEngine = Starling.Paint.Compositor.Compositor;
using LayoutRect = Starling.Layout.Rect;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Acceptance tests for the M12-04 layer compositor: transform parity vs. the
/// flat M5 path, opacity correctness, and per-layer cache isolation.
/// </summary>
[TestClass]
public sealed class CompositorTests
{
    private static BlockBox Layout(string html, int w, int h)
    {
        var document = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        return engine.LayoutDocument(document, new Size(w, h));
    }

    private static RenderedBitmap FlatRender(ImageSharpBackend backend, BlockBox root, int w, int h, float scale)
    {
        PaintList list = new DisplayListBuilder().Build(root);
        return backend.Render(list, new LayoutRect(0, 0, w, h), scale);
    }

    [TestMethod]
    public void Promoted_rotation_matches_flat_path_within_ssim_tolerance()
    {
        const int W = 200, H = 200;
        const float scale = 1f;
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"position:absolute;left:50px;top:50px;width:100px;height:60px;" +
            "background-color:#cc2222;transform:rotate(45deg)\">Rotate</div>" +
            "</body>";

        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        // Flat M5 path bakes the rotation into the display list.
        using var flat = FlatRender(backend, root, W, H, scale);

        // New path: the div's layer holds the UPRIGHT content; the composite
        // applies the rotation.
        var tree = new LayerTreeBuilder().Build(root);
        using var layered = new CompositorEngine(backend).Render(tree, new LayoutRect(0, 0, W, H), scale);

        layered.Width.Should().Be(flat.Width);
        layered.Height.Should().Be(flat.Height);

        var ssim = Ssim.ComputeRgba(layered.Rgba, flat.Rgba, layered.Width, layered.Height);
        ssim.Should().BeGreaterThanOrEqualTo(0.99,
            "the composited rotation must match the flat pre-baked rotation");
    }

    [TestMethod]
    public void Opacity_layer_alpha_blends_over_the_background()
    {
        const int W = 120, H = 120;
        const float scale = 1f;
        // Solid blue page background; a 50%-opacity solid-red div on top. The
        // div is painted at FULL opacity into its cache, and the composite
        // applies 0.5 — the result over blue is (127.5, 0, 127.5)-ish.
        var html =
            "<body style=\"margin:0;background-color:#0000ff\">" +
            "<div style=\"opacity:0.5;width:120px;height:120px;background-color:#ff0000\"></div>" +
            "</body>";

        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        var tree = new LayerTreeBuilder().Build(root);
        using var output = new CompositorEngine(backend).Render(tree, new LayoutRect(0, 0, W, H), scale);

        // Sample the centre — well inside both the blue page and the red div.
        var (r, g, b, a) = output.GetPixel(W / 2, H / 2);
        a.Should().Be(255);
        // 0.5*red over blue → R≈128, B≈128, G≈0 (straight-alpha blend).
        ((int)r).Should().BeInRange(118, 138, "half-opacity red contributes ~128 R over blue");
        ((int)b).Should().BeInRange(118, 138, "the blue background shows through the 50% red");
        ((int)g).Should().BeLessThan(20, "neither layer contributes green");
    }

    [TestMethod]
    public void Untouched_sibling_layer_serves_from_cache_on_reblit()
    {
        const int W = 200, H = 200;
        const float scale = 1f;
        // Two promoted opacity divs (siblings). We render twice with the SAME
        // page version, so every layer's cache should serve from a HIT on the
        // second pass — the cache-keying invariant that makes per-layer
        // isolation possible: a layer whose (pageVersion, scale) is unchanged
        // re-blits from cache and is never re-rastered.
        var html =
            "<body style=\"margin:0\">" +
            "<div style=\"opacity:0.5;position:absolute;left:0;top:0;width:50px;height:50px;background-color:#ff0000\"></div>" +
            "<div style=\"opacity:0.5;position:absolute;left:100px;top:0;width:50px;height:50px;background-color:#0000ff\"></div>" +
            "</body>";

        var root = Layout(html, W, H);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var diag = new RecordingDiagnostics();

        // Build the tree ONCE so the layers (and their caches) persist across
        // both renders — the realistic scroll/re-blit scenario.
        var tree = new LayerTreeBuilder(styleOverride: null, images: null, diagnostics: diag).Build(root);
        var compositor = new CompositorEngine(backend, diag);

        const int pageVersion = 7;
        // First render seeds each layer's cache (no prior content → no HIT).
        using (compositor.Render(tree, new LayoutRect(0, 0, W, H), scale, pageVersion)) { }
        diag.CountOf("paint.cache.hit").Should().Be(0, "nothing is cached before the first render");

        // Second render at the SAME page version: each promoted layer must serve
        // from cache (HIT) instead of re-rasterizing — the per-layer isolation
        // invariant. There are two promoted divs, so two layers serve a HIT.
        using (compositor.Render(tree, new LayoutRect(0, 0, W, H), scale, pageVersion)) { }
        diag.CountOf("paint.cache.hit").Should().Be(2,
            "both untouched promoted layers re-blit from cache on the unchanged second render");

        // Bumping ONLY page version (the per-layer key) forces a re-raster of
        // every layer — the cache no longer matches, so no further HITs land.
        using (compositor.Render(tree, new LayoutRect(0, 0, W, H), scale, pageVersion + 1)) { }
        diag.CountOf("paint.cache.hit").Should().Be(2,
            "a changed key invalidates the serve — the re-keyed render is not a HIT");
    }

    [TestMethod]
    public void Transform_only_change_reblits_layer_content_from_persistent_cache()
    {
        // Phase 5 for transform (the opacity test's sibling): a promoted, rotated
        // element's transform is applied at composite time, not baked into the
        // slice, so changing only the rotation re-blits the cached upright content
        // and re-composites — no re-raster — while the output changes. (This is the
        // deterministic core; driving it from the animation clock end to end is the
        // live-GUI integration tracked in PHASES_1_5_STATUS.)
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
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var diag = new RecordingDiagnostics();
        var store = new LayerCacheStore(diag);
        var compositor = new CompositorEngine(backend, diag);
        const int pageVersion = 5; // content version — stable across the transform change

        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        FindByElement(root1, doc.GetElementById("box")!)!.Hints
            .Should().NotBe(Starling.Layout.Compositor.LayerHint.None, "a transformed div is its own layer");
        var tree1 = new LayerTreeBuilder(null, null, diag, store.CacheFor).Build(root1);
        byte[] first;
        using (var r1 = compositor.Render(tree1, new LayoutRect(0, 0, W, H), scale, pageVersion))
            first = (byte[])r1.Rgba.Clone();
        diag.CountOf("paint.cache.hit").Should().Be(0, "nothing is cached before the first render");

        // Change only the rotation — a composite-time property. Re-lay-out so the
        // new transform reaches the box style, then rebuild against the SAME store.
        doc.GetElementById("box")!.SetAttribute("style", baseStyle + "transform:rotate(70deg)");
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(null, null, diag, store.CacheFor).Build(root2);
        byte[] second;
        using (var r2 = compositor.Render(tree2, new LayoutRect(0, 0, W, H), scale, pageVersion))
            second = (byte[])r2.Rgba.Clone();

        diag.CountOf("paint.cache.hit").Should().Be(2,
            "the rotating layer's content (and the page background) re-blit from cache; only the composite transform changed");
        second.SequenceEqual(first).Should().BeFalse("the new rotation must change the composited output");
    }

    [TestMethod]
    public void Opacity_only_change_reblits_layer_content_from_persistent_cache()
    {
        // Phase 5: across frames the layer tree is rebuilt, but persistent
        // element-keyed caches let a layer whose only change is opacity (applied
        // at composite, not baked into the slice) serve its pixels from cache
        // rather than re-rasterize. We change opacity, rebuild the tree against
        // the SAME store at the SAME page version, and assert the content is a
        // cache HIT yet the composited output differs.
        const int W = 160, H = 160;
        const float scale = 1f;
        var html =
            "<body style=\"margin:0;height:160px;background-color:#eef0ff\">" +
            "<div id=fade style=\"opacity:0.9;position:absolute;left:10px;top:10px;" +
            "width:80px;height:80px;background-color:#cc2222\"></div>" +
            "</body>";

        var doc = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var diag = new RecordingDiagnostics();
        var store = new LayerCacheStore(diag);
        var compositor = new CompositorEngine(backend, diag);
        const int pageVersion = 3;

        var root1 = engine.LayoutDocument(doc, new Size(W, H));
        var tree1 = new LayerTreeBuilder(null, null, diag, store.CacheFor).Build(root1);
        byte[] first;
        using (var r1 = compositor.Render(tree1, new LayoutRect(0, 0, W, H), scale, pageVersion))
            first = (byte[])r1.Rgba.Clone();
        diag.CountOf("paint.cache.hit").Should().Be(0, "nothing is cached before the first render");

        // Animate opacity only — no layout-affecting change. Re-lay-out so the new
        // opacity reaches the box style, then rebuild the tree against the SAME store.
        doc.GetElementById("fade")!.SetAttribute(
            "style", "opacity:0.4;position:absolute;left:10px;top:10px;width:80px;height:80px;background-color:#cc2222");
        var root2 = engine.LayoutDocument(doc, new Size(W, H));
        var tree2 = new LayerTreeBuilder(null, null, diag, store.CacheFor).Build(root2);
        byte[] second;
        using (var r2 = compositor.Render(tree2, new LayoutRect(0, 0, W, H), scale, pageVersion))
            second = (byte[])r2.Rgba.Clone();

        // Both layers (root + the promoted div) re-blit their pixels from cache:
        // the slice content didn't change, only the composite-time opacity did.
        diag.CountOf("paint.cache.hit").Should().Be(2,
            "the layer content is reused from cache; only the composite-time opacity changed");
        // ...and the composited result actually changed (the div is more transparent).
        second.SequenceEqual(first).Should().BeFalse("the new opacity must change the composited output");
    }

    private static Box? FindByElement(Box box, Starling.Dom.Element el)
    {
        if (ReferenceEquals(box.Element, el)) return box;
        foreach (var c in box.Children)
            if (FindByElement(c, el) is { } found) return found;
        return null;
    }

    private sealed class RecordingDiagnostics : IDiagnostics
    {
        private readonly ConcurrentDictionary<string, double> _counters = new();

        public double CountOf(string name) => _counters.TryGetValue(name, out var v) ? v : 0d;

        public void Counter(string name, double value)
            => _counters.AddOrUpdate(name, value, (_, prev) => prev + value);

        public IDisposable Span(string area, string operation) => NoopSpan.Instance;
        public void Log(DiagLevel level, string area, string message) { }
        public void Snapshot(string label, ReadOnlySpan<byte> bytes) { }
        public void LogException(string area, Exception exception, string? message = null) { }

        private sealed class NoopSpan : IDisposable
        {
            public static readonly NoopSpan Instance = new();
            public void Dispose() { }
        }
    }
}
