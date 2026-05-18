using Tessera.Common.Image;
using LayoutSize = Tessera.Layout.Size;
using PaintList = Tessera.Paint.DisplayList.DisplayList;

namespace Tessera.Paint.Backend;

/// <summary>Renderer-neutral seam consumed by <see cref="Painter"/> via <see cref="PaintBackendSelector"/>.</summary>
internal interface IPaintBackend : IDisposable
{
    string Name { get; }
    RenderedBitmap Render(PaintList list, LayoutSize viewport, float scale = 1.0f);
}
