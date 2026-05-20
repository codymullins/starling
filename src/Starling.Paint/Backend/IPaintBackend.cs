using Starling.Common.Image;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Backend;

/// <summary>Renderer-neutral seam consumed by <see cref="Painter"/> via <see cref="PaintBackendSelector"/>.</summary>
internal interface IPaintBackend : IDisposable
{
    string Name { get; }

    /// <summary>
    /// Render <paramref name="list"/> into a bitmap sized to
    /// <paramref name="viewport"/>.Width × .Height. <paramref name="viewport"/>.X/Y
    /// is the page-coordinate scroll offset of the visible region: paint at
    /// page-coord (viewport.X, viewport.Y) lands at device-coord (0, 0). This is
    /// the viewport-clipped path used by interactive scroll.
    /// </summary>
    RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale = 1.0f);

    /// <summary>
    /// Render <paramref name="list"/> into a <paramref name="viewport"/>-sized
    /// bitmap, optionally over a transparent canvas
    /// (<paramref name="opaqueBackground"/> = false). The compositor uses the
    /// transparent path to rasterize a layer's slice so unpainted regions stay
    /// see-through for alpha-over compositing. Defaults to the opaque-white
    /// behavior of the primary overload.
    /// </summary>
    RenderedBitmap Render(PaintList list, LayoutRect viewport, float scale, bool opaqueBackground)
        => Render(list, viewport, scale);

    /// <summary>
    /// "Render everything" convenience: a <see cref="LayoutSize"/> with no
    /// offset (X=Y=0). Used by headless full-page screenshots and tests that
    /// pass an explicit surface size. Delegates to the rect overload.
    /// </summary>
    RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f)
        => Render(list, new LayoutRect(0, 0, viewport.Width, viewport.Height), scale);
}
