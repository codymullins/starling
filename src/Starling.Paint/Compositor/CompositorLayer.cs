using Starling.Common.Diagnostics;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Paint.Cache;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Compositor;

/// <summary>
/// One node of the compositor layer tree built by <see cref="LayerTreeBuilder"/>:
/// a promotable box (or the implicit document root) and the slice of the display
/// list its subtree paints, excluding anything inside descendant layers.
/// </summary>
/// <remarks>
/// Coordinate spaces: <see cref="Items"/> and <see cref="Bounds"/> are in
/// <em>page coords</em> (CSS px, document origin) — the same space the flat
/// <see cref="DisplayList.DisplayListBuilder"/> emits. The layer's
/// <see cref="Transform"/> / <see cref="Opacity"/> / <see cref="Clip"/> are the
/// effective cascade values for the layer-root box and are applied at
/// <em>composite</em> time (by <see cref="Compositor"/>), never baked into the
/// slice — that is why a promoted transformed box paints upright into its own
/// bitmap and the final composite rotates it.
/// </remarks>
internal sealed class CompositorLayer
{
    public CompositorLayer(
        PaintList items,
        Rect bounds,
        Matrix2D transform,
        float opacity,
        Rect? clip,
        IReadOnlyList<CompositorLayer> children,
        IDiagnostics? diagnostics = null,
        PictureCache? cache = null)
    {
        Items = items;
        Bounds = bounds;
        Transform = transform;
        Opacity = opacity;
        Clip = clip;
        Children = children;
        // A persistent cache (keyed by layer identity across frames) is supplied
        // by the compositing session so a transform/opacity-only change re-blits
        // the layer from cache instead of re-rasterizing it (plan Phase 5). When
        // none is supplied the layer owns a fresh per-call cache (the original
        // M12-04 behaviour, used by one-shot renders and tests).
        Cache = cache ?? new PictureCache(diagnostics);
    }

    /// <summary>Page-coord union of the painted items in this layer's slice.</summary>
    public Rect Bounds { get; }

    /// <summary>This layer's display-list slice (descendant-layer items excluded).</summary>
    public PaintList Items { get; }

    /// <summary>
    /// Effective document-space transform of the layer-root box
    /// (<c>T(+origin) × M × T(-origin)</c>, centre origin), or
    /// <see cref="Matrix2D.Identity"/> when the box has no transform.
    /// </summary>
    public Matrix2D Transform { get; }

    /// <summary>Effective opacity (1.0 when the box has no <c>opacity</c>).</summary>
    public float Opacity { get; }

    /// <summary>
    /// Page-coord clip rect from <c>overflow: hidden</c>, else null.
    /// Border-radius clipping is deferred (WP note).
    /// </summary>
    public Rect? Clip { get; }

    /// <summary>
    /// Child layers in PAINT ORDER. The z-index sort (CSS-Position-3 §9:
    /// negative z below, then auto/0 in tree order, then positive) happens in
    /// <see cref="LayerTreeBuilder"/>, so the compositor draws them as-is.
    /// </summary>
    public IReadOnlyList<CompositorLayer> Children { get; }

    /// <summary>One picture cache per layer (generalizes the M12-02 single cache).</summary>
    public PictureCache Cache { get; }
}
