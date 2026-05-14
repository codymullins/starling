using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Tessera.Common.Image;
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
    public RenderedBitmap Render(BlockBox root)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        PaintList displayList = new DisplayListBuilder().Build(root);
        var surfaceSize = new LayoutSize(
            Math.Max(1, root.Frame.Width),
            Math.Max(1, root.Frame.Height));
        return _backend.Render(displayList, surfaceSize);
    }

    /// <summary>
    /// Encodes a <see cref="RenderedBitmap"/> (straight RGBA8888) to an in-memory
    /// PNG and wraps it as a MAUI <see cref="ImageSource"/>. MAUI's image
    /// pipeline wants encoded bytes; the Skia backend produces raw pixels, so we
    /// bridge through the existing ImageSharp PNG encoder. The encoded bytes are
    /// captured so the returned source can be re-streamed if MAUI re-reads it.
    /// </summary>
    public static ImageSource ToImageSource(RenderedBitmap bitmap)
    {
        ArgumentNullException.ThrowIfNull(bitmap);

        byte[] png;
        using (var image = SixLabors.ImageSharp.Image.LoadPixelData<Rgba32>(
                   bitmap.Rgba, bitmap.Width, bitmap.Height))
        using (var ms = new MemoryStream())
        {
            image.SaveAsPng(ms);
            png = ms.ToArray();
        }
        return ImageSource.FromStream(() => new MemoryStream(png));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
