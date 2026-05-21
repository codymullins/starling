using Starling.Bindings;
using Starling.Css.Cascade;
using Starling.Dom;
using Starling.Layout.Box;

namespace Starling.Engine;

/// <summary>
/// <see cref="ILayoutHost"/> implementation backed by
/// <c>Painter.LayoutDocumentWithStyle</c>. The engine builds the host against
/// the parsed (pre-script) DOM, hands it to <c>WindowBinding</c>, and the
/// bindings answer rect / offset / computed-style queries from it for the
/// duration of script execution.
/// </summary>
/// <remarks>
/// <para><b>Incremental re-layout:</b> the host tracks the document's
/// <see cref="Document.MutationVersion"/>. Each readback first checks whether a
/// DOM mutation has bumped the version since the last layout; if so it lazily
/// re-runs the cascade + layout via the recompute delegate and rebuilds its
/// element→box index before answering. This makes mutate-then-measure idioms
/// (append sized content, then read <c>getBoundingClientRect</c> /
/// <c>offsetTop</c> on the new node or a following sibling) reflect the
/// post-mutation layout rather than the pre-script snapshot. Layout runs at
/// most once per quiescent batch of mutations because the version only advances
/// on the next mutation.</para>
/// </remarks>
internal sealed class BoxLayoutHost : ILayoutHost
{
    private readonly Dictionary<Element, Box> _boxByElement = new(ReferenceEqualityComparer.Instance);
    private readonly Document? _document;
    private readonly Func<(BlockBox Root, StyleEngine Style)>? _relayout;
    private StyleEngine? _style;
    private int _laidOutVersion;
    private bool _laidOut;

    /// <summary>
    /// Build a host over an already-computed layout. Without a
    /// <paramref name="document"/>/<paramref name="relayout"/> pair the host is
    /// a static snapshot — DOM mutations are not reflected.
    /// </summary>
    public BoxLayoutHost(BlockBox root, StyleEngine style,
        Document? document = null, Func<(BlockBox Root, StyleEngine Style)>? relayout = null)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        _document = document;
        _relayout = relayout;
        _laidOutVersion = document?.MutationVersion ?? 0;
        Index(root);
        _laidOut = true;
    }

    /// <summary>
    /// Build a host that <b>defers</b> its first layout until a script actually
    /// reads geometry or computed style. Pages whose scripts never touch layout
    /// (analytics beacons, feature-detection, etc.) skip the pre-script layout
    /// pass entirely — the only layout that runs is the final render pass. The
    /// first geometry/computed-style read triggers the <paramref name="relayout"/>
    /// delegate; subsequent reads reuse it until a DOM mutation bumps the
    /// document's <see cref="Document.MutationVersion"/>.
    /// </summary>
    public BoxLayoutHost(Document document, Func<(BlockBox Root, StyleEngine Style)> relayout)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(relayout);
        _document = document;
        _relayout = relayout;
        _laidOutVersion = document.MutationVersion;
        _laidOut = false;
    }

    /// <summary>
    /// If a DOM mutation has advanced the document's mutation version since the
    /// last layout, re-run layout and rebuild the element→box index. No-op for a
    /// static snapshot (no recompute delegate) or when nothing has mutated.
    /// </summary>
    private void EnsureFresh()
    {
        if (_relayout is null) return; // static snapshot — indexed at construction
        if (_laidOut && (_document is null || _document.MutationVersion == _laidOutVersion))
            return;

        var (root, style) = _relayout();
        _style = style;
        _boxByElement.Clear();
        Index(root);
        _laidOutVersion = _document?.MutationVersion ?? 0;
        _laidOut = true;
    }

    private void Index(Box box)
    {
        if (box.Element is { } e) _boxByElement[e] = box;
        foreach (var child in box.Children) Index(child);
    }

    public bool TryGetBoundingClientRect(Element element, out LayoutRect rect)
    {
        EnsureFresh();
        if (_boxByElement.TryGetValue(element, out var box))
        {
            var frame = box.Frame;
            rect = new LayoutRect(frame.X, frame.Y, frame.Width, frame.Height);
            return true;
        }
        rect = default;
        return false;
    }

    public bool TryGetOffsetMetrics(Element element, out OffsetMetrics metrics)
    {
        EnsureFresh();
        if (_boxByElement.TryGetValue(element, out var box))
        {
            var frame = box.Frame;
            var padding = box.Padding;
            // offsetWidth/Height = border box (frame). clientWidth/Height
            // = padding box (frame minus borders). Without borders the
            // numbers coincide, which is fine for our snapshot.
            var border = box.Border;
            metrics = new OffsetMetrics(
                OffsetWidth: frame.Width,
                OffsetHeight: frame.Height,
                OffsetTop: frame.Y,
                OffsetLeft: frame.X,
                ClientWidth: Math.Max(0, frame.Width - border.Horizontal),
                ClientHeight: Math.Max(0, frame.Height - border.Vertical));
            _ = padding; // reserved for scrollWidth/Height once content overflow is wired.
            return true;
        }
        metrics = default;
        return false;
    }

    public string GetComputedProperty(Element element, string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName)) return string.Empty;
        EnsureFresh();
        if (_style is null) return string.Empty;
        try
        {
            var computed = _style.Compute(element);
            return computed?.GetPropertyValue(propertyName) ?? string.Empty;
        }
        catch
        {
            // The cascade engine can throw for elements detached from the
            // document or for properties it doesn't recognise. The binding
            // surface is required not to throw, so fall back to the
            // unknown-property empty-string return.
            return string.Empty;
        }
    }
}
