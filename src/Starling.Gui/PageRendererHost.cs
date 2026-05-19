using Starling.Common.Diagnostics;
using Starling.Common.Image;
using Starling.Css.Cascade;
using Starling.Layout.Box;
using Starling.Layout.Text;
using Starling.Layout.Tree;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Gui;

/// <summary>
/// Drives the same DisplayListBuilder + paint-backend pipeline that
/// src/Starling.Gui/PageRenderer.cs uses on the MAUI side, returning a raw
/// <see cref="RenderedBitmap"/> that <c>BitmapBridge</c> can hand to Avalonia.
/// The backend (Skia Graphite / ImageSharp CPU / ImageSharp WebGPU) is picked
/// by <see cref="PaintBackendSelector"/> from the <c>STARLING_PAINT_BACKEND</c>
/// env var. Avalonia takes the RGBA buffer directly — the MAUI-specific
/// <c>ToImageSource</c> tail isn't needed.
/// </summary>
internal sealed class PageRendererHost : IDisposable
{
    private readonly IDiagnostics _diag;
    private readonly IPaintBackend _backend;
    private bool _disposed;

    public PageRendererHost(IDiagnostics? diagnostics = null)
    {
        _diag = diagnostics ?? NoopDiagnostics.Instance;
        _backend = PaintBackendSelector.Create(FontResolver.Default, webFonts: null, _diag);
    }

    public RenderedBitmap Render(BlockBox root, float scale = 1.0f, Func<Box, ComputedStyle?>? styleOverride = null, IImageResolver? images = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(root);

        PaintList displayList = new DisplayListBuilder().Build(root, styleOverride, images);
        var surfaceSize = new LayoutSize(
            Math.Max(1, root.Frame.Width),
            Math.Max(1, root.Frame.Height));
        try
        {
            return _backend.Render(displayList, surfaceSize, scale);
        }
        catch (Exception ex)
        {
            // Surface backend failures (WebGPU init, native shim missing, etc.)
            // through diagnostics so Aspire's trace view shows the full
            // exception on the GUI span instead of just a failed activity.
            _diag.LogException("gui", ex, $"page render via '{_backend.Name}' failed");
            throw;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _backend.Dispose();
    }
}
