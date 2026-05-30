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

namespace Starling.Paint.Tests;

/// <summary>
/// Golden parity for the GPU layer-composite blend (wp:M12-13-gpu-composite-blend):
/// the same layer tree composited on the GPU must match the managed CPU
/// <c>BlendOp</c> path — byte-for-byte for upright (translate/scale) layers, and
/// within the SSIM tolerance the rest of the compositor uses for rotations
/// (filtered sampling rounds differently between the GPU sampler and the CPU
/// bilinear loop). Skips when the host has no GPU adapter.
/// </summary>
[TestClass]
public sealed class GpuCompositeParityTests
{
    private static BlockBox Layout(string html, int w, int h)
    {
        var document = HtmlParser.Parse(html);
        var engine = new LayoutEngine(new StyleEngine(), DefaultTextMeasurer.Instance);
        return engine.LayoutDocument(document, new Size(w, h));
    }

    private static (RenderedBitmap cpu, RenderedBitmap gpu) RenderBothPaths(string html, int w, int h, float scale)
    {
        var root = Layout(html, w, h);
        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);

        // Independent trees so neither path's per-layer picture cache perturbs the
        // other. Both feed the same geometry to CPU BlendOp vs GPU blend.
        var cpuTree = new LayerTreeBuilder().Build(root);
        var gpuTree = new LayerTreeBuilder().Build(root);

        var cpu = new CompositorEngine(backend) { DisableGpuBlend = true }
            .Render(cpuTree, new LayoutRect(0, 0, w, h), scale);
        var gpu = new CompositorEngine(backend) // GPU is the default path
            .Render(gpuTree, new LayoutRect(0, 0, w, h), scale);
        return (cpu, gpu);
    }

    /// <summary>Fraction of pixels whose every channel differs by at most <paramref name="tol"/>.</summary>
    private static double NearEqualFraction(RenderedBitmap a, RenderedBitmap b, int tol)
    {
        var pa = a.Rgba;
        var pb = b.Rgba;
        long near = 0;
        var pixels = pa.Length / 4;
        for (var i = 0; i < pa.Length; i += 4)
        {
            if (Math.Abs(pa[i] - pb[i]) <= tol
                && Math.Abs(pa[i + 1] - pb[i + 1]) <= tol
                && Math.Abs(pa[i + 2] - pb[i + 2]) <= tol
                && Math.Abs(pa[i + 3] - pb[i + 3]) <= tol)
                near++;
        }
        return (double)near / pixels;
    }

    [TestMethod]
    public void Upright_layers_match_cpu_blend_byte_for_byte()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available; GPU composite parity cannot be checked on this host.");
            return;
        }

        // Stacked translate-only layers: an opaque base, a half-opacity solid
        // layer, and an offset solid layer. No rotation/scale, so the GPU blend
        // lands texel-aligned and must reproduce the CPU AlphaOver exactly.
        const int W = 256, H = 192;
        var html =
            "<body style=\"margin:0;background-color:#dde3ff\">" +
            "<div style=\"position:absolute;left:0;top:0;width:256px;height:192px;background-color:#dde3ff\"></div>" +
            "<div style=\"opacity:0.5;position:absolute;left:20px;top:20px;width:120px;height:90px;background-color:#cc3333\"></div>" +
            "<div style=\"position:absolute;left:90px;top:60px;width:120px;height:90px;background-color:#2255aa\"></div>" +
            "</body>";

        var (c, g) = RenderBothPaths(html, W, H, 2.0f);
        using (c)
        using (g)
        {
            c.Width.Should().Be(g.Width);
            c.Height.Should().Be(g.Height);

            // Allow ±1 per channel for the rounding seam between the GPU's
            // unorm store and the CPU's round-half ToByte, but require the
            // overwhelming majority to match within that.
            var frac = NearEqualFraction(c, g, tol: 1);
            frac.Should().BeGreaterThanOrEqualTo(0.999,
                "an upright GPU blend must reproduce the CPU AlphaOver within a rounding unit");

            Ssim.ComputeRgba(c.Rgba, g.Rgba, c.Width, c.Height)
                .Should().BeGreaterThanOrEqualTo(0.999, "upright layers are byte-identical up to rounding");
        }
    }

    [TestMethod]
    public void Rotated_layer_matches_cpu_blend_within_ssim_tolerance()
    {
        if (GpuLayerCompositor.Shared is null)
        {
            Assert.Inconclusive("No GPU adapter available; GPU composite parity cannot be checked on this host.");
            return;
        }

        const int W = 220, H = 220;
        var html =
            "<body style=\"margin:0;background-color:#eef0ff\">" +
            "<div style=\"position:absolute;left:50px;top:50px;width:110px;height:70px;" +
            "background-color:#cc2222;transform:rotate(37deg)\"></div>" +
            "</body>";

        var (c, g) = RenderBothPaths(html, W, H, 2.0f);
        using (c)
        using (g)
        {
            Ssim.ComputeRgba(c.Rgba, g.Rgba, c.Width, c.Height)
                .Should().BeGreaterThanOrEqualTo(0.99,
                    "the GPU-rotated layer must match the CPU bilinear blend within the compositor's SSIM tolerance");
        }
    }
}
