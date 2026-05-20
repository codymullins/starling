namespace Starling.Layout.Compositor;

/// <summary>
/// Why a box was tagged as a layer candidate during layout. Each set bit names
/// a CSS condition that establishes a stacking context (or otherwise forces the
/// box onto its own compositor layer). The bits OR together: a single box can
/// satisfy several conditions at once (e.g. a <c>position: fixed</c> box with
/// <c>opacity: 0.5</c> carries both <see cref="Fixed"/> and
/// <see cref="OpacityLessThanOne"/>).
/// <para>
/// This enum is pure layout-tree metadata. It does not create layers or change
/// paint; a later compositor work package consumes it to split paint into
/// per-layer slices.
/// </para>
/// </summary>
[System.Flags]
public enum LayerHint
{
    None = 0,

    /// <summary>Positioned box with an explicit <c>z-index</c> (≠ <c>auto</c>).</summary>
    Promoted = 1 << 0,

    /// <summary><c>will-change</c> names a compositing-triggering property.</summary>
    WillChange = 1 << 1,

    /// <summary><c>position: fixed</c>.</summary>
    Fixed = 1 << 2,

    /// <summary><c>position: sticky</c> (tagged unconditionally).</summary>
    Sticky = 1 << 3,

    /// <summary>The document's root element box.</summary>
    Root = 1 << 4,

    /// <summary><c>opacity</c> &lt; 1.</summary>
    OpacityLessThanOne = 1 << 5,

    /// <summary>A non-identity <c>transform</c> (name is a slight misnomer:
    /// any 2D or 3D transform sets it).</summary>
    Transform3D = 1 << 6,

    /// <summary><c>filter</c> is not <c>none</c>.</summary>
    Filter = 1 << 7,

    /// <summary><c>isolation: isolate</c>.</summary>
    Isolation = 1 << 8,
}
