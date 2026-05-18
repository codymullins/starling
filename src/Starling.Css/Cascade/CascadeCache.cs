using Tessera.Css.Selectors;
using Tessera.Dom;

namespace Tessera.Css.Cascade;

/// <summary>
/// Per-tree-pass memoization for <see cref="StyleEngine.Compute(Element)"/>.
/// The cascade entry point recurses up the ancestor chain to compute each
/// parent's <see cref="ComputedStyle"/>; for a tree of N elements at depth
/// D that's O(N*D) work and the ancestor styles get recomputed N times.
/// Threading one of these into the tree-traversal call sites (e.g. the
/// box-tree builder) collapses repeated ancestor cascades to a single
/// computation per element per pass.
/// </summary>
/// <remarks>
/// Reference equality on <see cref="Element"/> is fine: <see cref="Element"/>
/// is a class and the DOM keeps a single instance per node within a pass.
/// The cache is only valid while the DOM and stylesheets are stable, so
/// callers should construct a fresh cache for each layout pass.
///
/// The cache deliberately does not handle the interactive
/// <see cref="SelectorMatchContext"/> path — when a non-null context is
/// passed to <see cref="StyleEngine.Compute(Element, SelectorMatchContext?, CascadeCache?)"/>
/// the engine bypasses the cache so pseudo-class state (<c>:hover</c>,
/// <c>:focus</c>, <c>:active</c>) is always honoured.
/// </remarks>
public sealed class CascadeCache
{
    private readonly Dictionary<Element, ComputedStyle> _styles = new(ReferenceEqualityComparer.Instance);

    public bool TryGet(Element element, out ComputedStyle style)
        => _styles.TryGetValue(element, out style!);

    public void Set(Element element, ComputedStyle style)
        => _styles[element] = style;

    public int Count => _styles.Count;

    public void Clear() => _styles.Clear();
}
