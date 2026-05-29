using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Layout.Box;
using Starling.Paint.Cache;

namespace Starling.Paint.Compositor;

/// <summary>
/// Holds one persistent <see cref="PictureCache"/> per compositor layer across
/// frames, keyed by the layer-root box's DOM <see cref="Element"/> (stable across
/// re-layouts). This is what makes Phase 5 work: the layer tree is rebuilt every
/// frame, but each layer reuses its element's cache, so a frame that only changed
/// a layer's <c>transform</c>/<c>opacity</c> (applied at composite time, not baked
/// into the cached slice) re-blits the layer's pixels from cache instead of
/// re-rasterizing them.
/// </summary>
/// <remarks>
/// Layers whose root box has no element (no stable cross-frame identity) fall back
/// to a fresh per-call cache — they simply don't get cross-frame reuse, which is
/// safe. The store is reset when the page's content changes (navigation /
/// re-layout that produces a new display-list version), the same trigger that
/// invalidates the flat picture cache.
/// </remarks>
internal sealed class LayerCacheStore
{
    private readonly IDiagnostics? _diag;
    private readonly Dictionary<Element, PictureCache> _caches = new();

    public LayerCacheStore(IDiagnostics? diagnostics = null) => _diag = diagnostics;

    /// <summary>The persistent cache for <paramref name="layerBox"/>'s element, or
    /// a fresh throwaway cache when the box has no element.</summary>
    public PictureCache CacheFor(Box layerBox)
    {
        if (layerBox.Element is not { } element)
            return new PictureCache(_diag);
        if (!_caches.TryGetValue(element, out var cache))
        {
            cache = new PictureCache(_diag);
            _caches[element] = cache;
        }
        return cache;
    }

    /// <summary>Drop every retained layer cache — called when the page content
    /// changes so the next frame re-rasterizes from scratch.</summary>
    public void Clear() => _caches.Clear();
}
