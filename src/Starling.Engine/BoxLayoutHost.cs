using Tessera.Bindings;
using Tessera.Css.Cascade;
using Tessera.Dom;
using Tessera.Layout.Box;

namespace Tessera.Engine;

/// <summary>
/// <see cref="ILayoutHost"/> implementation backed by a single
/// <c>Painter.LayoutDocumentWithStyle</c> pass. The engine snapshots layout against the parsed (pre-script) DOM,
/// hands the host to <c>WindowBinding</c>, and the bindings answer rect /
/// offset / computed-style queries from this snapshot for the duration of
/// script execution.
/// </summary>
/// <remarks>
/// Pre-script snapshot is a deliberate simplification — scripts that mutate
/// then measure get stale numbers. Replacing this with a true on-demand
/// "flush layout" path (re-run cascade + layout from inside a binding) is
/// the follow-up that lets bundlers' measure-after-mutate idioms work.
/// </remarks>
internal sealed class BoxLayoutHost : ILayoutHost
{
    private readonly Dictionary<Element, Box> _boxByElement = new(ReferenceEqualityComparer.Instance);
    private readonly StyleEngine _style;

    public BoxLayoutHost(BlockBox root, StyleEngine style)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentNullException.ThrowIfNull(style);
        _style = style;
        Index(root);
    }

    private void Index(Box box)
    {
        if (box.Element is { } e) _boxByElement[e] = box;
        foreach (var child in box.Children) Index(child);
    }

    public bool TryGetBoundingClientRect(Element element, out LayoutRect rect)
    {
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
