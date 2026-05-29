using Starling.Common.Diagnostics;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout.Block;
using Starling.Layout.Box;
using Starling.Layout.Position;
using Starling.Layout.Text;
using Starling.Layout.Tree;

namespace Starling.Layout.Incremental;

/// <summary>
/// A persistent layout context that lays a document out once and then, on each
/// later frame, recomputes only the subtrees a mutation touched — the core of
/// incremental layout (see the incremental-layout plan §3, §5–6).
/// </summary>
/// <remarks>
/// <para>The session retains the box tree and an element/text-node → box map
/// across frames. Each frame it drains the document's
/// <see cref="Document.DrainLayoutMutations">layout-mutation batch</see>,
/// marks the changed subtrees (and any element with an in-flight
/// animation/transition, whose style changes off the clock) dirty along the
/// root-to-change path, then re-runs the block pass. Clean subtrees with an
/// unchanged constraint space are reused in place — repositioned in O(1)
/// because every <see cref="Box.Box.Frame"/> is parent-relative, never
/// recomputed or re-shaped.</para>
/// <para>Soundness comes from the reuse key: a box is reused only when it is
/// not on a dirty path <em>and</em> its constraint space is unchanged. Anything
/// the reconciler cannot prove safe — a structural change, a display-level flip,
/// a viewport resize, an unmapped target — falls back to a full rebuild, which
/// is always correct. The dual-run verifier (<c>LayoutVerifier</c>)
/// guards the whole thing: incremental output is checked against a full rebuild.</para>
/// </remarks>
public sealed class LayoutSession
{
    private readonly StyleEngine _style;
    private readonly IImageResolver _images;
    private readonly IDiagnostics _diag;

    // Persistent across frames: the retained tree and its lookup maps.
    private readonly Dictionary<Element, Box.Box> _elementMap = new();
    private readonly Dictionary<Starling.Dom.Text, TextBox> _textMap = new();
    private BlockBox? _root;
    private Size _viewport;

    /// <summary>Env switch that turns the incremental relayout path on. Off by
    /// default — the engine then uses the unchanged full-rebuild path.</summary>
    public const string EnvVar = "STARLING_INCREMENTAL_LAYOUT";

    /// <summary>Whether <see cref="EnvVar"/> requests incremental layout.</summary>
    public static bool Enabled =>
        string.Equals(Environment.GetEnvironmentVariable(EnvVar), "1", StringComparison.Ordinal);

    public LayoutSession(StyleEngine style, IImageResolver? images = null, IDiagnostics? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _images = images ?? NullImageResolver.Instance;
        _diag = diagnostics ?? NoopDiagnostics.Instance;
    }

    /// <summary>The retained box tree's root, or null before the first layout.</summary>
    public BlockBox? Root => _root;

    /// <summary>
    /// When set, every incremental relayout is checked against a full rebuild of
    /// the same document and the first geometry divergence is logged — the plan's
    /// incremental-vs-full harness flip (§2g). Doubles layout cost, so it is a
    /// debug/CI safety net; defaults to the <c>STARLING_LAYOUT_VERIFY</c> switch.
    /// </summary>
    public bool VerifyAgainstFullRebuild { get; init; } = Verification.LayoutVerifier.Enabled;

    /// <summary>
    /// Lay <paramref name="document"/> out at <paramref name="viewport"/>,
    /// reusing the retained tree where the mutation batch lets us. The first
    /// call (and any call the reconciler can't handle incrementally) does a full
    /// rebuild. Returns the laid-out root.
    /// </summary>
    public BlockBox Layout(
        Document document, Size viewport, ITextMeasurer measurer, double? nowMs, CancellationToken abort = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(measurer);

        var batch = document.DrainLayoutMutations();

        if (_root is not null && viewport == _viewport && TryReconcile(batch, nowMs))
        {
            using (_diag.Span("layout", "incremental.relayout"))
                RunLayout(measurer, viewport, abort, incremental: true);
            _diag.Counter("layout.incremental.relayout", 1);
            if (VerifyAgainstFullRebuild)
                Verify(document, viewport, measurer, nowMs, abort);
            return _root;
        }

        using (_diag.Span("layout", "incremental.full_rebuild"))
            FullBuild(document, viewport, measurer, nowMs, abort);
        _diag.Counter("layout.incremental.full_rebuild", 1);
        return _root!;
    }

    /// <summary>
    /// Dual-run check (plan §2g): rebuild <paramref name="document"/> from
    /// scratch the always-correct way and compare it to the just-produced
    /// incremental tree, logging the first geometry divergence. This is how a
    /// stale-but-plausible reuse — the worst incremental-layout failure — is
    /// caught before it reaches a user.
    /// </summary>
    private void Verify(Document document, Size viewport, ITextMeasurer measurer, double? nowMs, CancellationToken abort)
    {
        using var _ = _diag.Span("layout", "incremental.verify");
        var reference = new BoxTreeBuilder(_style, _images, nowMs).Build(document);
        var block = new BlockLayout(measurer, viewport, _diag, abort, incremental: false);
        block.Layout(reference);
        new PositionLayout(block, viewport).LayoutPositioned(reference);

        var divergence = Verification.LayoutVerifier.FindFirstDivergence(_root!, reference);
        if (divergence is { } d)
        {
            _diag.Counter("layout.incremental.divergent", 1);
            _diag.Log(DiagLevel.Error, "layout.incremental", $"incremental layout diverged from full rebuild: {d}");
        }
        else
        {
            _diag.Counter("layout.incremental.verify_ok", 1);
        }
    }

    private void FullBuild(Document document, Size viewport, ITextMeasurer measurer, double? nowMs, CancellationToken abort)
    {
        _elementMap.Clear();
        _textMap.Clear();
        _viewport = viewport;

        var builder = new BoxTreeBuilder(_style, _images, nowMs, _elementMap, _textMap);
        _root = builder.Build(document);
        RunLayout(measurer, viewport, abort, incremental: true);
    }

    private void RunLayout(ITextMeasurer measurer, Size viewport, CancellationToken abort, bool incremental)
    {
        var root = _root!;
        var block = new BlockLayout(measurer, viewport, _diag, abort, incremental);
        block.Layout(root);
        var positioning = new PositionLayout(block, viewport);
        positioning.LayoutPositioned(root);
    }

    /// <summary>
    /// Apply <paramref name="batch"/> to the retained tree, marking changed
    /// subtrees dirty. Returns false — asking the caller to fall back to a full
    /// rebuild — for any change the reconciler can't apply safely in place
    /// (structural change, display-level flip, unmapped target).
    /// </summary>
    private bool TryReconcile(IReadOnlyList<LayoutMutation> batch, double? nowMs)
    {
        foreach (var m in batch)
        {
            switch (m.Kind)
            {
                case LayoutChangeKind.TextChanged:
                    if (m.Target is not Starling.Dom.Text text
                        || !_textMap.TryGetValue(text, out var textBox))
                        return false;
                    textBox.Text = text.Data;
                    textBox.Fragments.Clear(); // force the inline pass to re-shape
                    MarkDirtyPath(textBox);
                    break;

                case LayoutChangeKind.LayoutRelevantAttr:
                    if (m.Target is not Element attrEl || !RebuildAndSplice(attrEl, nowMs))
                        return false;
                    break;

                // Structural changes need input-tree reconciliation + localized
                // anonymous re-wrapping (plan Phase 3). Until then, fall back.
                case LayoutChangeKind.ChildInserted:
                case LayoutChangeKind.ChildRemoved:
                    return false;
            }
        }

        // An animation or transition changes its target's computed style off the
        // clock, with no DOM mutation to record — so mark every active target
        // dirty and re-cascade it at the current frame time. (Both engines key on
        // Element; ones that produce no box, e.g. display:none, are skipped.)
        if (nowMs is not null)
        {
            foreach (var el in _style.AnimationEngine.ActiveElements)
                if (!MarkAnimated(el, nowMs)) return false;
            foreach (var el in _style.TransitionEngine.ActiveElements)
                if (!MarkAnimated(el, nowMs)) return false;
        }

        return true;
    }

    private bool MarkAnimated(Element el, double? nowMs)
    {
        if (!_elementMap.TryGetValue(el, out _)) return true; // no box (display:none) — nothing to do
        return RebuildAndSplice(el, nowMs);
    }

    /// <summary>
    /// Rebuild <paramref name="element"/>'s box subtree (re-cascading it at
    /// <paramref name="nowMs"/>) and splice it into the retained tree in place of
    /// its old box, then mark the path dirty. Returns false (forcing a full
    /// rebuild) if the element isn't mapped, is the root, changed box level, or
    /// no longer produces a single box — cases where the parent's structure
    /// would shift.
    /// </summary>
    private bool RebuildAndSplice(Element element, double? nowMs)
    {
        if (!_elementMap.TryGetValue(element, out var oldBox)) return false;
        var slot = oldBox.Parent;
        if (slot is null) return false; // the root element — full rebuild is simpler

        var index = slot.Children.IndexOf(oldBox);
        if (index < 0) return false;

        // Reproduce the old box's block/inline level so the parent's anonymous
        // wrapping is unchanged; a level flip would re-bucket siblings.
        var blockifyParent = oldBox.Kind != BoxKind.Inline;
        var builder = new BoxTreeBuilder(_style, _images, nowMs, _elementMap, _textMap);
        var newBox = builder.RebuildElementSubtree(element, slot.Style, blockifyParent);
        if (newBox is null || newBox.Kind != oldBox.Kind) return false;

        newBox.Parent = slot;
        slot.Children[index] = newBox;
        MarkDirtyPath(newBox);
        return true;
    }

    /// <summary>Mark <paramref name="box"/> and every ancestor up to the root as
    /// subtree-dirty, so the block pass recomputes the whole root-to-change path
    /// while reusing the clean subtrees that hang off it.</summary>
    private static void MarkDirtyPath(Box.Box box)
    {
        for (Box.Box? b = box; b is not null; b = b.Parent)
            b.SubtreeDirty = true;
    }
}
