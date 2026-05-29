using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Css.Properties;
using Starling.Css.Values;
using Starling.Layout.Box;
using Starling.Layout.Incremental;
using Starling.Layout.Inline;
using Starling.Layout.Text;

namespace Starling.Layout.Block;

/// <summary>
/// Block formatting context layout. Children stack vertically; inline children
/// are folded into anonymous blocks before this pass so we only ever see block
/// children here.
/// </summary>
/// <remarks>
/// Coordinate convention: every box's <see cref="Box.Box.Frame"/> is in its
/// <em>parent's content-box</em> coordinate space. To get a document-space
/// position, the painter walks the tree adding parent content origins.
/// </remarks>
internal sealed class BlockLayout
{
    private readonly ITextMeasurer _measurer;
    private readonly Size _viewport;
    private readonly InlineLayout _inline;
    private readonly CancellationToken _abort;

    // When true, the block pass records each box's constraint space and reuses a
    // clean subtree (not dirty + same constraint) by repositioning it in O(1)
    // instead of recomputing it. Off by default, so the full-rebuild path is
    // byte-for-byte unchanged. See Starling.Layout.Incremental.
    private readonly bool _incremental;

    public BlockLayout(ITextMeasurer measurer, Size viewport, IDiagnostics? diagnostics = null, CancellationToken abort = default, bool incremental = false)
    {
        _measurer = measurer;
        _viewport = viewport;
        _inline = new InlineLayout(measurer, viewport, diagnostics);
        _abort = abort;
        _incremental = incremental;
    }

    public void Layout(Box.Box root)
    {
        ResolveBoxModel(root, _viewport.Width);
        var contentWidth = _viewport.Width - root.Margin.Horizontal - root.Border.Horizontal - root.Padding.Horizontal;
        // The root's containing block is the viewport (CSS 2.1 §10.1). When the
        // root has `height: auto` we still hand the viewport height down as the
        // percentage basis for descendants — that matches the long-standing
        // html/body special case browsers use so `body { height: 100% }` reaches
        // the viewport instead of collapsing to 0.
        var explicitHeight = ResolveLength(root.Style, PropertyId.Height, _viewport.Height, _viewport, allowAuto: true);
        var consumedHeight = LayoutChildren(root, contentWidth, explicitHeight ?? _viewport.Height);
        var contentHeight = explicitHeight ?? consumedHeight;
        root.Frame = new Rect(
            root.Margin.Left,
            root.Margin.Top,
            contentWidth + root.Padding.Horizontal + root.Border.Horizontal,
            contentHeight + root.Padding.Vertical + root.Border.Vertical);
    }

    /// <summary>
    /// Lay out <paramref name="parent"/>'s children as a block formatting
    /// context using <paramref name="containerWidth"/> as the available
    /// content width. Returns the total consumed height. The parent's own
    /// frame is not touched — callers (e.g. <see cref="Inline.InlineLayout"/>
    /// for an inline-block) compose the result into their own box model.
    /// </summary>
    internal double LayoutChildren(Box.Box parent, double containerWidth)
        => LayoutChildren(parent, containerWidth, containerHeight: null, measure: false);

    /// <summary>
    /// Lay out a flex/grid item's contents. Identical to <c>LayoutChildren</c>
    /// for a normal block-level item, but a bare-text "anonymous flex item"
    /// (CSS Flexbox §4) is itself an <see cref="BoxKind.AnonymousBlock"/> whose
    /// children are raw inline boxes with no enclosing anonymous block — so it
    /// must establish the inline formatting context directly instead of trying
    /// to block-stack its text (which silently drops it).
    /// </summary>
    internal double LayoutItem(Box.Box item, double containerWidth, double? containerHeight, bool measure = false, bool reuseHeight = false)
    {
        if (item.Kind == BoxKind.AnonymousBlock)
            return _inline.Layout(item, containerWidth, measure);
        // A flex/grid item can itself be a flex container (nested flex — e.g. a
        // navbar's <ul> that is both an item of the nav row and a flex row of
        // its own <li>s). LayoutChildren would block-stack the inner items;
        // route back through the flex formatting context so the inner row lays
        // out as a row. (Normal block flow reaches this via LayoutBlock; items
        // bypass that path, so the dispatch must be repeated here.)
        if (IsFlexContainer(item.Style))
        {
            var flex = new Starling.Layout.Flex.FlexLayout(this, _viewport);
            return flex.Layout(item, containerWidth, containerHeight);
        }
        if (IsGridContainer(item.Style))
        {
            var grid = new Starling.Layout.Grid.GridLayout(this, _viewport);
            return grid.Layout(item, containerWidth, containerHeight);
        }
        return LayoutChildren(item, containerWidth, containerHeight, measure, reuseHeight);
    }

    /// <summary>
    /// Lay out children with the option of running in measurement mode, used
    /// by the inline-block shrink-to-fit pass. In measurement mode the inline
    /// formatting context skips its post-layout alignment shifts so the
    /// caller's <c>MeasureUsedWidth</c> walk sees natural pre-shift positions.
    /// </summary>
    internal double LayoutChildren(Box.Box parent, double containerWidth, bool measure)
        => LayoutChildren(parent, containerWidth, containerHeight: null, measure);

    internal double LayoutChildren(Box.Box parent, double containerWidth, double? containerHeight)
        => LayoutChildren(parent, containerWidth, containerHeight, measure: false);

    /// <summary>
    /// Lay out children with an explicit containing-block height for resolving
    /// percentage heights on descendants (CSS 2.1 §10.5). Pass <c>null</c> when
    /// the containing block has indefinite height — percentage heights on
    /// direct children then collapse to <c>auto</c> per spec.
    /// <para><paramref name="reuseHeight"/>, set by a flex/grid item's auto
    /// cross-size (height) measurement, lets a clean child subtree replay its
    /// cached measured height instead of re-laying. Only valid when the caller
    /// consumes the returned height and never reads descendant fragments — the
    /// width (min/max-content) measure passes leave it false.</para>
    /// </summary>
    internal double LayoutChildren(Box.Box parent, double containerWidth, double? containerHeight, bool measure, bool reuseHeight = false)
    {
        var cursorY = 0d;
        var prevBottomMargin = 0d;
        var first = true;
        // Every LayoutChildren call gets its own float context — every block
        // is treated as a new BFC for floats, which is a simplification of
        // CSS 2.1 §9.4.1 (BFCs are actually only established by floats,
        // overflow!=visible, position:absolute|fixed, display:inline-block,
        // and the root). The simplification is fine while inline floats and
        // float-escape-across-non-BFC layouts aren't exercised.
        var floats = new FloatContext(containerWidth);
        foreach (var child in parent.Children)
        {
            // Host abort (Stop button, navigation supersede). One check per
            // child keeps the cancellation latency bounded — a deeply nested
            // subtree pays at every level — without polluting the per-property
            // inner work below.
            _abort.ThrowIfCancellationRequested();

            // CSS 2.1 §9.3.1: `position: absolute` and `position: fixed`
            // remove the element from normal flow. We skip them here so the
            // cursor doesn't advance — they're placed in a second pass by
            // PositionLayout. The child's Frame is left at default (zero)
            // until that pass writes it.
            if (IsOutOfFlow(child.Style))
                continue;

            var floatSide = GetFloatSide(child.Style);
            if (floatSide is not null)
            {
                LayoutFloat(child, containerWidth, containerHeight, floats, floatSide, cursorY);
                continue;
            }

            // CSS 2.1 §9.5.2 — clear on a non-float pushes its top edge past
            // every active float of the requested side(s) before normal flow
            // resumes. Apply before margin-collapse so the cleared block
            // starts in the right band.
            var clearSide = GetClearSide(child.Style);
            if (clearSide is not null)
            {
                var clearY = floats.ClearY(clearSide);
                if (clearY > cursorY)
                {
                    cursorY = clearY;
                    prevBottomMargin = 0;
                }
            }

            LayoutBlock(child, containerWidth, containerHeight, ref cursorY, ref prevBottomMargin, ref first, measure, reuseHeight);
        }
        // Grow the consumed height to enclose any floats that stick past the
        // last in-flow block (§10.6.7). Without this, a float-only container
        // would report zero height even though its floats are visible.
        var floatBottom = floats.MaxFloatBottom();
        if (floatBottom > cursorY) cursorY = floatBottom;
        return cursorY;
    }

    private static string? GetFloatSide(ComputedStyle? style)
    {
        if (style is null) return null;
        if (style.Get(PropertyId.Float) is not CssKeyword k) return null;
        return k.Name.Equals("left", StringComparison.OrdinalIgnoreCase) ? "left"
            : k.Name.Equals("right", StringComparison.OrdinalIgnoreCase) ? "right"
            : null;
    }

    private static string? GetClearSide(ComputedStyle? style)
    {
        if (style is null) return null;
        if (style.Get(PropertyId.Clear) is not CssKeyword k) return null;
        var n = k.Name;
        return n.Equals("left", StringComparison.OrdinalIgnoreCase) ? "left"
            : n.Equals("right", StringComparison.OrdinalIgnoreCase) ? "right"
            : n.Equals("both", StringComparison.OrdinalIgnoreCase) ? "both"
            : null;
    }

    private void LayoutFloat(Box.Box child, double containerWidth, double? containerHeight, FloatContext floats, string side, double startY)
    {
        // Floats use the regular block sizing path (specified width, intrinsic
        // height from laid-out children) but are placed by the float context
        // instead of advancing the cursor. They establish their own BFC so the
        // recursive LayoutChildren call gets its own FloatContext.
        if (child.Kind == BoxKind.AnonymousBlock) return;

        ResolveBoxModel(child, containerWidth);
        var width = ContentWidth(child, containerWidth);

        var explicitHeight = ResolveHeight(child.Style, PropertyId.Height, containerHeight, _viewport, allowAuto: true);
        double childContentHeight;
        if (IsFlexContainer(child.Style))
        {
            var flex = new Starling.Layout.Flex.FlexLayout(this, _viewport);
            childContentHeight = flex.Layout(child, width, explicitHeight);
        }
        else if (IsGridContainer(child.Style))
        {
            var grid = new Starling.Layout.Grid.GridLayout(this, _viewport);
            childContentHeight = grid.Layout(child, width, explicitHeight);
        }
        else
        {
            childContentHeight = LayoutChildren(child, width, explicitHeight, measure: false);
        }

        var resolvedHeight = explicitHeight ?? childContentHeight;
        var fullHeight = resolvedHeight + child.Padding.Vertical + child.Border.Vertical;
        var fullWidth = width + child.Padding.Horizontal + child.Border.Horizontal;
        var outerWidth = fullWidth + child.Margin.Horizontal;
        var outerHeight = fullHeight + child.Margin.Vertical;

        var placement = side == "left"
            ? floats.PlaceLeft(startY, outerWidth, outerHeight)
            : floats.PlaceRight(startY, outerWidth, outerHeight);

        child.Frame = new Rect(
            placement.X + child.Margin.Left,
            placement.Y + child.Margin.Top,
            fullWidth,
            fullHeight);
    }

    /// <summary>
    /// True for <c>position: absolute</c> / <c>fixed</c> boxes, which are
    /// removed from normal flow (CSS 2.1 §9.3.1) and placed in a later pass by
    /// <see cref="Position.PositionLayout"/>. Flex (CSS Flexbox §4) and Grid
    /// (CSS Grid §9) likewise exclude such children from item layout, so this
    /// is shared with <see cref="Flex.FlexLayout"/> / <see cref="Grid.GridLayout"/>.
    /// </summary>
    internal static bool IsOutOfFlow(ComputedStyle? style)
    {
        if (style is null) return false;
        if (style.Get(PropertyId.Position) is not CssKeyword k) return false;
        var name = k.Name;
        return string.Equals(name, "absolute", StringComparison.OrdinalIgnoreCase)
            || string.Equals(name, "fixed", StringComparison.OrdinalIgnoreCase);
    }

    private void LayoutBlock(Box.Box child, double containerWidth, double? containerHeight, ref double cursorY, ref double prevBottomMargin, ref bool first, bool measure = false, bool reuseHeight = false)
    {
        // Incremental reuse: a clean subtree laid out under the same constraint
        // space keeps its size and its descendants' (parent-relative) geometry,
        // so we only re-place its root in the parent's block flow. The placement
        // math below mirrors the full path exactly, so the dual-run harness sees
        // no divergence. Measurement mode (inline-block shrink-to-fit) skips
        // reuse — its alignment behaviour differs from the normal pass.
        var cs = new ConstraintSpace(containerWidth, containerHeight, _viewport.Width, _viewport.Height);

        // Measurement-mode reuse: an intrinsic-sizing pass over a clean subtree
        // (e.g. an auto-height flex item measuring its content height) only
        // consumes the subtree's height — it never reads the descendants' text
        // fragments. So a clean box measured before under the same measurement
        // constraint replays its cached height without re-shaping a single line.
        // This is what stops one deep DOM change inside a flex root from
        // re-measuring the whole page every frame: the dirty item re-measures,
        // its clean siblings/descendants replay. Width (min/max-content) measures
        // do read fragments, but those short-circuit at the flex layer before
        // reaching here, so this height-only replay is sound.
        if (_incremental && measure && reuseHeight && !child.SubtreeDirty
            && child.LaidConstraintMeasure == cs && child.Kind != BoxKind.AnonymousBlock)
        {
            var mTop = child.Margin.Top;
            var mCollapse = first ? mTop : Math.Max(0, Math.Max(mTop, prevBottomMargin) - prevBottomMargin);
            cursorY += mCollapse;
            first = false;
            child.Frame = new Rect(child.Margin.Left, cursorY, child.Frame.Width, child.MeasuredHeight);
            cursorY += child.MeasuredHeight + child.Margin.Bottom;
            prevBottomMargin = child.Margin.Bottom;
            return;
        }

        if (_incremental && !measure && !child.SubtreeDirty && child.LaidConstraint == cs)
        {
            if (child.Kind == BoxKind.AnonymousBlock)
            {
                child.Frame = new Rect(0, cursorY, containerWidth, child.Frame.Height);
                cursorY = child.Frame.Bottom;
                prevBottomMargin = 0;
                first = false;
                return;
            }

            var reuseTop = child.Margin.Top;
            var reuseCollapse = first ? reuseTop : Math.Max(0, Math.Max(reuseTop, prevBottomMargin) - prevBottomMargin);
            cursorY += reuseCollapse;
            first = false;
            child.Frame = new Rect(child.Margin.Left, cursorY, child.Frame.Width, child.Frame.Height);
            cursorY += child.Frame.Height + child.Margin.Bottom;
            prevBottomMargin = child.Margin.Bottom;
            return;
        }

        if (child.Kind == BoxKind.AnonymousBlock)
        {
            // Anonymous blocks take the initial value for box-model properties
            // (margin/padding/border = 0); they only inherit text-formatting for
            // the inline pass.
            child.Margin = Edges.Zero;
            child.Padding = Edges.Zero;
            child.Border = Edges.Zero;

            var inlineHeight = _inline.Layout(child, containerWidth, measure);
            child.Frame = new Rect(0, cursorY, containerWidth, inlineHeight);
            cursorY = child.Frame.Bottom;
            prevBottomMargin = 0;
            first = false;
            Stamp(child, cs, measure);
            return;
        }

        ResolveBoxModel(child, containerWidth);

        // Margin-collapse against previous sibling.
        var topMargin = child.Margin.Top;
        var collapseTop = first ? topMargin : Math.Max(0, Math.Max(topMargin, prevBottomMargin) - prevBottomMargin);
        cursorY += collapseTop;
        first = false;

        var width = ContentWidth(child, containerWidth);

        // CSS 2.1 §10.3.3 — resolve `auto` horizontal margins for non-replaced
        // block-level elements in normal flow. Only when `width` is not `auto`
        // do auto margins absorb slack; otherwise they resolve to 0 (already
        // handled by ResolveBoxModel).
        ResolveAutoHorizontalMargins(child, containerWidth, width);

        // Flex container: hand off to the flex formatting context. Flex sizes
        // its items inside the container's content box; the result replaces
        // what block stacking would have produced.
        var explicitHeight = ResolveHeight(child.Style, PropertyId.Height, containerHeight, _viewport, allowAuto: true);
        double childContentHeight;
        if (IsFlexContainer(child.Style))
        {
            var flex = new Starling.Layout.Flex.FlexLayout(this, _viewport);
            childContentHeight = flex.Layout(child, width, explicitHeight);
        }
        else if (IsGridContainer(child.Style))
        {
            var grid = new Starling.Layout.Grid.GridLayout(this, _viewport);
            childContentHeight = grid.Layout(child, width, explicitHeight);
        }
        else
        {
            childContentHeight = LayoutChildren(child, width, explicitHeight, measure, reuseHeight);
        }

        var resolvedHeight = explicitHeight ?? childContentHeight;
        var fullHeight = resolvedHeight + child.Padding.Vertical + child.Border.Vertical;

        child.Frame = new Rect(
            child.Margin.Left,
            cursorY,
            width + child.Padding.Horizontal + child.Border.Horizontal,
            fullHeight);

        cursorY += fullHeight + child.Margin.Bottom;
        prevBottomMargin = child.Margin.Bottom;
        Stamp(child, cs, measure);
    }

    /// <summary>Record the constraint space a freshly laid-out box was computed
    /// under so a later frame can reuse it. The normal pass also clears the dirty
    /// mark; the measurement pass records only its own (height-only) reuse key and
    /// leaves the dirty mark for the normal pass to clear. No-op off the
    /// incremental path.</summary>
    private void Stamp(Box.Box box, ConstraintSpace cs, bool measure)
    {
        if (!_incremental) return;
        if (measure)
        {
            box.LaidConstraintMeasure = cs;
            box.MeasuredHeight = box.Frame.Height;
            return;
        }
        box.LaidConstraint = cs;
        box.SubtreeDirty = false;
    }

    /// <summary>
    /// CSS 2.1 §10.3.3 — for a block-level non-replaced element in normal flow,
    /// `auto` values for `margin-left` / `margin-right` absorb the slack between
    /// the used width and the containing block's content width:
    /// <list type="bullet">
    ///   <item>Both auto → center the box (each margin gets half the slack; if
    ///   negative, both go to 0).</item>
    ///   <item>One auto → that one absorbs all slack.</item>
    ///   <item>Neither auto → leave margins as computed (over-constrained;
    ///   spec says ignore margin-right in LTR but we keep the user's values).</item>
    /// </list>
    /// This is only meaningful when `width` is not `auto`; if width filled the
    /// available space, slack is zero (or negative) and there is nothing to
    /// distribute.
    /// </summary>
    private static void ResolveAutoHorizontalMargins(Box.Box child, double containerWidth, double usedWidth)
    {
        var leftAuto = IsAutoMargin(child.Style, PropertyId.MarginLeft);
        var rightAuto = IsAutoMargin(child.Style, PropertyId.MarginRight);
        if (!leftAuto && !rightAuto) return;

        // CSS 2.1 §10.3.3 says auto margins absorb slack when width is not
        // auto. §10.4 extends this to the max-width / min-width clamp: after
        // the tentative width is constrained, the leftover horizontal space
        // is redistributed to auto margins, even though the *computed* width
        // value is `auto`. Without this, `body { max-width: 35em; margin: auto }`
        // (the words.html / Justin Jackson layout, MDN article body, etc.)
        // pins to the left instead of centering. Compute the slack from the
        // used width — if it's zero (true auto width, no clamp) the autos
        // collapse to 0 the same way the §10.3.3 path produces.
        var outerWidth = usedWidth + child.Padding.Horizontal + child.Border.Horizontal;
        var slack = containerWidth - outerWidth - child.Margin.Left - child.Margin.Right;
        if (slack <= 0) return;

        double newLeft = child.Margin.Left;
        double newRight = child.Margin.Right;
        if (leftAuto && rightAuto)
        {
            if (slack < 0)
            {
                // Negative slack: both autos go to 0 (left edge).
                newLeft = 0;
                newRight = 0;
            }
            else
            {
                newLeft = slack / 2d;
                newRight = slack - newLeft;
            }
        }
        else if (leftAuto)
        {
            newLeft = Math.Max(0, slack);
        }
        else // rightAuto
        {
            newRight = Math.Max(0, slack);
        }

        child.Margin = new Edges(child.Margin.Top, newRight, child.Margin.Bottom, newLeft);
    }

    private static bool IsAutoMargin(ComputedStyle? style, PropertyId property)
        => style is not null && style.Get(property) is CssKeyword k && k.Name == "auto";

    internal static bool IsFlexContainer(ComputedStyle? style)
    {
        if (style is null) return false;
        if (style.Get(PropertyId.Display) is not CssKeyword k) return false;
        return k.Name.Equals("flex", StringComparison.OrdinalIgnoreCase)
            || k.Name.Equals("inline-flex", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsGridContainer(ComputedStyle? style)
    {
        if (style is null) return false;
        if (style.Get(PropertyId.Display) is not CssKeyword k) return false;
        return k.Name.Equals("grid", StringComparison.OrdinalIgnoreCase)
            || k.Name.Equals("inline-grid", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasExplicitWidth(ComputedStyle? style)
    {
        if (style is null) return false;
        var value = style.Get(PropertyId.Width);
        return value is not CssKeyword k || k.Name != "auto";
    }

    private void ResolveBoxModel(Box.Box box, double containerWidth)
    {
        box.Margin = new Edges(
            ResolveLength(box.Style, PropertyId.MarginTop, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginRight, containerWidth, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginBottom, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.MarginLeft, containerWidth, _viewport) ?? 0);

        box.Padding = new Edges(
            ResolveLength(box.Style, PropertyId.PaddingTop, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingRight, containerWidth, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingBottom, _viewport.Height, _viewport) ?? 0,
            ResolveLength(box.Style, PropertyId.PaddingLeft, containerWidth, _viewport) ?? 0);

        box.Border = new Edges(
            ResolveBorderWidth(box.Style, PropertyId.BorderTopWidth, PropertyId.BorderTopStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderRightWidth, PropertyId.BorderRightStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderBottomWidth, PropertyId.BorderBottomStyle, _viewport),
            ResolveBorderWidth(box.Style, PropertyId.BorderLeftWidth, PropertyId.BorderLeftStyle, _viewport));
    }

    private double ContentWidth(Box.Box box, double containerWidth)
    {
        // CSS 2.1 §10.4: tentative width is `width` if specified, otherwise the
        // available space within the parent. The tentative width is then
        // clamped by `max-width` (upper bound) and `min-width` (lower bound).
        // Honouring max-width is what makes the classic "max-width: 35em"
        // narrow-column layout work (justinjackson.ca/words.html, MDN, etc).
        var explicitWidth = ResolveLength(box.Style, PropertyId.Width, containerWidth, _viewport, allowAuto: true);
        double tentative;
        if (explicitWidth is { } w)
        {
            tentative = w;
        }
        else
        {
            var available = containerWidth - box.Margin.Horizontal - box.Border.Horizontal - box.Padding.Horizontal;
            tentative = Math.Max(0, available);
        }

        // max-width: `none` (the initial value) is a no-op; any concrete length
        // or percentage clamps the tentative width down.
        var maxWidth = ResolveMaxLength(box.Style, PropertyId.MaxWidth, containerWidth, _viewport);
        if (maxWidth is { } mx && tentative > mx)
            tentative = mx;

        // min-width: initial value `0` is a no-op; a concrete length/percentage
        // expands the box back up when it's narrower than the floor.
        var minWidth = ResolveLength(box.Style, PropertyId.MinWidth, containerWidth, _viewport) ?? 0;
        if (tentative < minWidth)
            tentative = minWidth;

        return Math.Max(0, tentative);
    }

    // max-* properties accept the keyword `none` (the initial value) to mean
    // "no upper bound". ResolveLength maps `none` to 0, which would collapse
    // the box; intercept it here so callers can skip the clamp.
    private static double? ResolveMaxLength(ComputedStyle? style, PropertyId property, double percentageBasis, Size? viewport)
    {
        if (style is null) return null;
        var value = style.Get(property);
        if (value is CssKeyword k && k.Name == "none") return null;
        return ResolveLength(style, property, percentageBasis, viewport);
    }

    internal static double ResolveBorderWidth(ComputedStyle? style, PropertyId widthId, PropertyId styleId, Size? viewport = null)
    {
        if (style is null) return 0;
        var styleValue = style.Get(styleId);
        if (styleValue is CssKeyword k && k.Name == "none") return 0;
        return style.Get(widthId) is CssLength len ? ToPx(len, viewport) : 0;
    }

    /// <summary>
    /// Height-axis variant of <see cref="ResolveLength(ComputedStyle?, PropertyId, double, Size?, bool)"/>.
    /// CSS 2.1 §10.5: if the containing block's height is not specified
    /// explicitly (the basis here is <c>null</c>), a percentage height
    /// resolves as <c>auto</c> — returned as <c>null</c> when
    /// <paramref name="allowAuto"/> is true, otherwise 0.
    /// </summary>
    internal static double? ResolveHeight(ComputedStyle? style, PropertyId property, double? containerHeight, Size? viewport, bool allowAuto = false)
    {
        if (style is null) return null;
        var value = style.Get(property);
        return value switch
        {
            CssLength len => ToPx(len, viewport),
            CssPercentage when !containerHeight.HasValue => allowAuto ? null : 0,
            CssPercentage pct => containerHeight!.Value * pct.Value / 100d,
            CssNumber n => n.Value,
            CssKeyword k when k.Name == "auto" => allowAuto ? null : 0,
            CssKeyword k when k.Name == "none" => 0,
            _ => null,
        };
    }

    internal static double? ResolveLength(ComputedStyle? style, PropertyId property, double percentageBasis, bool allowAuto = false)
        => ResolveLength(style, property, percentageBasis, viewport: null, allowAuto);

    internal static double? ResolveLength(ComputedStyle? style, PropertyId property, double percentageBasis, Size? viewport, bool allowAuto = false)
    {
        if (style is null) return null;
        var value = style.Get(property);
        return value switch
        {
            CssLength len => ToPx(len, viewport),
            CssPercentage pct => percentageBasis * pct.Value / 100d,
            CssNumber n => n.Value,
            CssKeyword k when k.Name == "auto" => allowAuto ? null : 0,
            CssKeyword k when k.Name == "none" => 0,
            _ => null,
        };
    }

    internal static double ToPx(CssLength length) => ToPx(length, viewport: null);

    internal static double ToPx(CssLength length, Size? viewport) => length.Unit switch
    {
        CssLengthUnit.Px => length.Value,
        CssLengthUnit.Pt => length.Value * 4d / 3d,
        CssLengthUnit.Pc => length.Value * 16d,
        CssLengthUnit.In => length.Value * 96d,
        CssLengthUnit.Cm => length.Value * 96d / 2.54d,
        CssLengthUnit.Mm => length.Value * 96d / 25.4d,
        CssLengthUnit.Q => length.Value * 96d / 101.6d,
        CssLengthUnit.Em => length.Value * 16d,
        CssLengthUnit.Rem => length.Value * 16d,
        CssLengthUnit.Vh => length.Value * (viewport?.Height ?? 100d) / 100d,
        CssLengthUnit.Vw => length.Value * (viewport?.Width ?? 100d) / 100d,
        _ => length.Value,
    };
}
