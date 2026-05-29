using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Compositor;
using Starling.Layout.Tree;
using Starling.Paint.DisplayList;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Compositor;

/// <summary>
/// Converts a laid-out box tree into a tree of <see cref="CompositorLayer"/>s.
/// A new layer opens at the implicit document root and at every box whose
/// <see cref="Box.Hints"/> is non-empty (a CSS stacking context / promotion
/// candidate tagged by <c>StackingContextResolver</c> in M12-03). Each layer
/// owns the display-list slice its subtree paints, with the items that belong to
/// descendant layers routed into those descendants instead.
/// </summary>
/// <remarks>
/// The implicit root layer is always present, even with zero promotions, so the
/// call site is uniform (WP note). Child layers are stored in PAINT ORDER — the
/// z-index sort (CSS-Position-3 §9) happens here, not at composite time.
/// </remarks>
internal sealed class LayerTreeBuilder
{
    private readonly DisplayListBuilder _builder = new();
    private readonly Func<Box, ComputedStyle?>? _styleOverride;
    private readonly IImageResolver? _images;
    private readonly IDiagnostics? _diag;
    // Supplies each layer's picture cache. When set (the live compositing
    // session), it returns a cache persisted across frames keyed by the layer's
    // element, so a transform/opacity-only change re-blits from cache (Phase 5).
    // Null for one-shot renders, where each layer owns a fresh cache.
    private readonly Func<Box, Cache.PictureCache>? _cacheFor;
    // Per-frame promotion predicate (LTF-01): an element that is actively
    // animating (or, via LTF-06, was just mutated) becomes a layer root even
    // with no static LayerHint. Evaluated every frame because animation
    // start/stop changes faster than layout runs, so it cannot be baked into the
    // layout-time Hints. Null for one-shot renders / tests with no live loop.
    private readonly Func<Box, bool>? _isAnimatingLayerRoot;

    public LayerTreeBuilder(
        Func<Box, ComputedStyle?>? styleOverride = null,
        IImageResolver? images = null,
        IDiagnostics? diagnostics = null,
        Func<Box, Cache.PictureCache>? cacheFor = null,
        Func<Box, bool>? isAnimatingLayerRoot = null)
    {
        _styleOverride = styleOverride;
        _images = images;
        _diag = diagnostics;
        _cacheFor = cacheFor;
        _isAnimatingLayerRoot = isAnimatingLayerRoot;
    }

    /// <summary>
    /// A box opens its own layer iff M12-03 tagged it with any static hint, or
    /// the per-frame predicate promotes it (an actively-animating or just-mutated
    /// element — LTF-01 / LTF-06). A composite-time transform/opacity on the
    /// promoted box is applied at composite (its slice stays upright); any other
    /// animated paint property simply re-rasters this box's own small slice.
    /// </summary>
    private bool IsLayerRoot(Box box)
        => box.Hints != LayerHint.None || (_isAnimatingLayerRoot?.Invoke(box) ?? false);

    /// <summary>
    /// Builds the layer tree. The returned layer is the implicit document root
    /// (always present); its <see cref="CompositorLayer.Children"/> are the
    /// next-level promoted boxes in paint order, recursively.
    /// </summary>
    public CompositorLayer Build(BlockBox root)
    {
        ArgumentNullException.ThrowIfNull(root);
        // The root's parent content origin is the document origin (0,0). The
        // box's own Frame.X/Y is folded in by BuildLayerSlice.
        return BuildLayer(root, parentOriginX: 0, parentOriginY: 0);
    }

    /// <summary>
    /// Builds the layer rooted at <paramref name="layerBox"/>. <paramref name="parentOriginX"/>/
    /// <paramref name="parentOriginY"/> are the page-coord content origin of the
    /// box that contains <paramref name="layerBox"/> — the same origin the flat
    /// builder's <c>Visit</c> would have received for it.
    /// </summary>
    private CompositorLayer BuildLayer(Box layerBox, double parentOriginX, double parentOriginY)
    {
        var frameX = parentOriginX + layerBox.Frame.X;
        var frameY = parentOriginY + layerBox.Frame.Y;

        // A layer carries its own CSS transform; the slice must be painted in
        // untransformed local space (the composite applies the matrix), so
        // suppress the slice root's transform bracket. Non-root transformed
        // descendants inside the slice keep their normal brackets.
        var transform = DisplayListBuilder.TryGetTransformMatrix(layerBox, _styleOverride, frameX, frameY);
        var hasTransform = transform is not null;

        PaintList slice = _builder.BuildLayerSlice(
            layerBox,
            parentOriginX,
            parentOriginY,
            IsLayerRoot,
            suppressRootTransform: hasTransform,
            _styleOverride,
            _images);

        var bounds = UnionBounds(slice);

        var opacity = EffectiveOpacity(layerBox);
        var clip = EffectiveClip(layerBox, frameX, frameY);

        // Descendant layer roots whose nearest enclosing layer is THIS one
        // become our child layers. They are found by descending into our
        // children's content boxes and stopping the descent at each layer root.
        var children = new List<(CompositorLayer Layer, int? ZIndex, int Order)>();
        var contentOriginX = frameX + layerBox.Border.Left + layerBox.Padding.Left;
        var contentOriginY = frameY + layerBox.Border.Top + layerBox.Padding.Top;
        foreach (var child in layerBox.Children)
            CollectChildLayers(child, contentOriginX, contentOriginY, children);

        // CSS-Position-3 §9 painting order: negative z-index below, then
        // auto/0 in tree order, then positive z-index — a stable sort by the
        // (effective z, tree order) key. `auto` is treated as 0 for ordering
        // among siblings within this stacking context.
        var ordered = children
            .OrderBy(c => c.ZIndex ?? 0)
            .ThenBy(c => c.Order)
            .Select(c => c.Layer)
            .ToList();

        // Content hash of the slice (LTF-02). The slice already excludes this
        // layer's own transform/opacity (suppressed above / applied at composite),
        // so a transform/opacity-only frame produces an identical hash and the
        // layer re-blits from cache; only a real content change re-rasters it.
        var contentHash = DisplayListContentHash.Compute(slice);

        return new CompositorLayer(slice, bounds, transform ?? Matrix2D.Identity, opacity, clip, ordered, _diag,
            cache: _cacheFor?.Invoke(layerBox), contentHash: contentHash);
    }

    /// <summary>
    /// Descends from <paramref name="box"/> looking for the next layer roots.
    /// When a layer root is found it is built (and its own subtree handled by the
    /// recursive <see cref="BuildLayer"/>); otherwise we keep descending into its
    /// children, so a layer root nested several plain boxes deep still attaches
    /// to the correct ancestor layer.
    /// </summary>
    private void CollectChildLayers(Box box, double parentOriginX, double parentOriginY, List<(CompositorLayer, int?, int)> sink)
    {
        if (IsLayerRoot(box))
        {
            var layer = BuildLayer(box, parentOriginX, parentOriginY);
            sink.Add((layer, ZIndexOf(box), sink.Count));
            return; // The nested layer owns everything below it.
        }

        var frameX = parentOriginX + box.Frame.X;
        var frameY = parentOriginY + box.Frame.Y;
        var contentOriginX = frameX + box.Border.Left + box.Padding.Left;
        var contentOriginY = frameY + box.Border.Top + box.Padding.Top;
        foreach (var child in box.Children)
            CollectChildLayers(child, contentOriginX, contentOriginY, sink);
    }

    private static Rect UnionBounds(PaintList slice)
    {
        var any = false;
        double minX = 0, minY = 0, maxX = 0, maxY = 0;
        var transform = Matrix2D.Identity;
        var stack = new Stack<Matrix2D>();
        stack.Push(Matrix2D.Identity);

        foreach (var item in slice.Items)
        {
            switch (item)
            {
                case PushTransform push:
                    transform = transform.Multiply(push.Matrix);
                    stack.Push(transform);
                    continue;
                case PopTransform:
                    if (stack.Count > 1) stack.Pop();
                    transform = stack.Peek();
                    continue;
            }

            if (!TryItemBounds(item, out var local)) continue;
            var aabb = TransformedAabb(local, transform);
            if (!any)
            {
                minX = aabb.X; minY = aabb.Y; maxX = aabb.Right; maxY = aabb.Bottom;
                any = true;
            }
            else
            {
                minX = Math.Min(minX, aabb.X);
                minY = Math.Min(minY, aabb.Y);
                maxX = Math.Max(maxX, aabb.Right);
                maxY = Math.Max(maxY, aabb.Bottom);
            }
        }

        return any ? new Rect(minX, minY, maxX - minX, maxY - minY) : Rect.Empty;
    }

    private static bool TryItemBounds(DisplayItem item, out Rect bounds)
    {
        switch (item)
        {
            case FillRect f: bounds = f.Bounds; return true;
            case FillGradient g: bounds = g.Bounds; return true;
            case StrokeRect s: bounds = s.Bounds; return true;
            case FillRoundedRect rf: bounds = rf.Bounds; return true;
            case StrokeRoundedRect rs: bounds = rs.Bounds; return true;
            case DrawBoxShadow sh:
                // The painted shadow is the box grown by spread+blur, offset.
                var pad = sh.Spread + sh.Blur;
                bounds = new Rect(
                    sh.Bounds.X + sh.OffsetX - pad,
                    sh.Bounds.Y + sh.OffsetY - pad,
                    sh.Bounds.Width + 2 * pad,
                    sh.Bounds.Height + 2 * pad);
                return true;
            case DrawImage i: bounds = i.Bounds; return true;
            case DrawText t:
                // Glyph run sits on the baseline; cover ascent above and a small
                // descent below so the AABB encloses the rasterized glyphs.
                bounds = new Rect(t.X, t.Y - t.FontSize, EstimateTextWidth(t), t.FontSize * 1.3);
                return true;
            case DrawTextDecoration d:
                // Decoration lines span the full glyph box (overline above,
                // line-through mid, underline below the baseline).
                bounds = new Rect(d.X, d.BaselineY - d.FontSize, d.Width, d.FontSize * 1.3);
                return true;
            case DrawTextShadow s:
                // Offset + blurred copy of the glyph run.
                bounds = new Rect(
                    s.X + s.OffsetX - s.Blur,
                    s.Y - s.FontSize + s.OffsetY - s.Blur,
                    EstimateShadowWidth(s) + 2 * s.Blur,
                    s.FontSize * 1.3 + 2 * s.Blur);
                return true;
            default:
                bounds = Rect.Empty;
                return false;
        }
    }

    private static double EstimateTextWidth(DrawText t)
        => t.Shaped is { } run && run.Advance > 0 ? run.Advance : t.Text.Length * t.FontSize * 0.6;

    private static double EstimateShadowWidth(DrawTextShadow s)
        => s.Shaped is { } run && run.Advance > 0 ? run.Advance : s.Text.Length * s.FontSize * 0.6;

    private static Rect TransformedAabb(Rect r, Matrix2D m)
    {
        if (m.IsIdentity) return r;
        var (x0, y0) = m.Transform(r.X, r.Y);
        var (x1, y1) = m.Transform(r.X + r.Width, r.Y);
        var (x2, y2) = m.Transform(r.X + r.Width, r.Y + r.Height);
        var (x3, y3) = m.Transform(r.X, r.Y + r.Height);
        var minX = Math.Min(Math.Min(x0, x1), Math.Min(x2, x3));
        var minY = Math.Min(Math.Min(y0, y1), Math.Min(y2, y3));
        var maxX = Math.Max(Math.Max(x0, x1), Math.Max(x2, x3));
        var maxY = Math.Max(Math.Max(y0, y1), Math.Max(y2, y3));
        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private float EffectiveOpacity(Box box)
    {
        var style = _styleOverride?.Invoke(box) ?? box.Style;
        return style?.Get(PropertyId.Opacity) switch
        {
            CssNumber n => (float)Math.Clamp(n.Value, 0d, 1d),
            CssPercentage p => (float)Math.Clamp(p.Value / 100d, 0d, 1d),
            _ => 1f,
        };
    }

    private Rect? EffectiveClip(Box box, double frameX, double frameY)
    {
        var style = _styleOverride?.Invoke(box) ?? box.Style;
        if (style is null) return null;
        // overflow:hidden clips the subtree to the box's frame (border box).
        // The `overflow` shorthand expands to overflow-x / overflow-y, so the
        // longhands are the carriers. Border-radius clipping is deferred (WP note).
        if (!IsOverflowHidden(style.Get(PropertyId.OverflowX))
            && !IsOverflowHidden(style.Get(PropertyId.OverflowY)))
            return null;
        return new Rect(frameX, frameY, box.Frame.Width, box.Frame.Height);
    }

    private static bool IsOverflowHidden(CssValue? value) => value switch
    {
        CssKeyword { Name: var n } => n.Equals("hidden", StringComparison.OrdinalIgnoreCase)
            || n.Equals("clip", StringComparison.OrdinalIgnoreCase),
        _ => false,
    };

    private int? ZIndexOf(Box box)
    {
        var style = _styleOverride?.Invoke(box) ?? box.Style;
        return style?.Get(PropertyId.ZIndex) switch
        {
            CssNumber n => (int)n.Value,
            CssLength len => (int)len.Value,
            _ => null, // auto → ordered as 0 among siblings (tree order tiebreak)
        };
    }
}
