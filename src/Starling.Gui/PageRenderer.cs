using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tessera.Common.Image;
using Tessera.Gui.Imaging;
using Tessera.Layout.Box;
using Tessera.Paint.Backend;
using Tessera.Paint.DisplayList;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Gui;

/// <summary>
/// Paints a laid-out box tree through the unified Skia <see cref="DisplayList"/>
/// path — the exact same <c>DisplayListBuilder</c> + <c>SkiaGraphiteBackend</c>
/// pipeline the headless renderer uses — and hands the result back as a MAUI
/// <see cref="ImageSource"/>.
/// </summary>
/// <remarks>
/// This is the GUI's replacement for the retired <c>BoxTreeRenderer</c>: instead
/// of a native MAUI view tree (one Label/BoxView per primitive), the page is a
/// single flat bitmap surface. Interaction (hover / link / select / find) is
/// re-derived from the box tree by <see cref="BoxHitTester"/> rather than from
/// native sub-views.
/// <para>
/// v1 presentation is an offscreen GPU render followed by a GPU→CPU pixel
/// readback (<see cref="SkiaGraphiteBackend"/> already does the readback), then
/// a PNG re-encode so MAUI's image pipeline can display it. A future WP can
/// present the Graphite surface straight into a <c>CAMetalLayer</c>-backed
/// <c>UIView</c> (no readback, no re-encode) — see the WP handoff log.
/// </para>
/// <para>
/// One <see cref="SkiaGraphiteBackend"/> is held for the lifetime of the
/// renderer: native context creation (Dawn instance/adapter/device + Graphite
/// context) is the expensive step and is reused across every repaint, including
/// the per-pointer-move <c>:hover</c> repaints.
/// </para>
/// </remarks>
public sealed class PageRenderer : IDisposable
{
    private readonly SkiaGraphiteBackend _backend = new();
    private bool _disposed;

    /// <summary>
    /// Builds a display list from <paramref name="root"/> and rasterizes it
    /// through Skia Graphite. The surface is sized to the full document
    /// (<c>root.Frame</c>) — taller than the viewport — so the GUI's
    /// <c>ScrollView</c> scrolls the whole page.
    /// </summary>
    /// <param name="scale">
    /// Logical→physical pixel ratio. Pass the device's display density (1.0 on
    /// non-Retina, 2.0 on Retina) so glyphs are baked at native resolution.
    /// Defaults to 1.0 for the headless / test paths.
    /// </param>
    public RenderedBitmap Render(BlockBox root, float scale = 1.0f)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        PaintList displayList = new DisplayListBuilder().Build(root);
        var surfaceSize = new LayoutSize(
            Math.Max(1, root.Frame.Width),
            Math.Max(1, root.Frame.Height));
        return _backend.Render(displayList, surfaceSize, scale);
    }

    /// <summary>
    /// Wraps a <see cref="RenderedBitmap"/> (straight RGBA8888) as a MAUI
    /// <see cref="ImageSource"/> that preserves <paramref name="density"/> as
    /// the <c>UIImage.Scale</c> on Mac Catalyst. The bitmap is in physical
    /// pixels; the display target sizes itself in points, and the scale tells
    /// UIKit how many physical pixels back each point — so a Retina render
    /// reaches the screen without an intervening resample.
    /// </summary>
    public static ImageSource ToImageSource(RenderedBitmap bitmap, float density = 1f)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        return new RgbaImageSource
        {
            PixelWidth = bitmap.Width,
            PixelHeight = bitmap.Height,
            Density = density,
            Pixels = bitmap.Rgba,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
