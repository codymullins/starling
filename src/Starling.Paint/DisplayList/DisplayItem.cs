using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Layout;
using Starling.Layout.Text;

namespace Starling.Paint.DisplayList;

/// <summary>
/// Discriminated union of paint operations. Painters replay these in order to
/// produce the final raster. Decoupling paint from layout this way lets us
/// cache, diff, and serialize the paint stream.
/// </summary>
public abstract record DisplayItem;

public enum FillRectPixelAlignment
{
    Preserve,
    SnapToDevicePixels,
}

public sealed record FillRect(Rect Bounds, CssColor Color, FillRectPixelAlignment PixelAlignment) : DisplayItem;

public sealed record StrokeRect(Rect Bounds, CssColor Color, double Width) : DisplayItem;

public sealed record DrawText(
    string Text,
    double X,
    double Y,
    double FontSize,
    CssColor Color,
    IReadOnlyList<string> FontFamilies,
    bool Bold,
    bool Italic,
    ShapedRun? Shaped = null) : DisplayItem;

/// <summary>
/// Blit a decoded image into <paramref name="Bounds"/>. <paramref name="Source"/>
/// is a backend-neutral <see cref="DecodedImage"/> (straight RGBA8888); the
/// paint backend reads its pixels directly. If <c>Bounds</c> differs from the
/// source's native size the backend resamples. When
/// <paramref name="SourceRect"/> is non-null, only that sub-rectangle of the
/// source pixels is blitted — used for CSS sprite-sheet painting where
/// <c>background-position</c> picks a slice out of a larger image.
/// </summary>
public sealed record DrawImage(Rect Bounds, DecodedImage Source, Rect? SourceRect = null) : DisplayItem;

/// <summary>
/// Pushes a 2D affine <paramref name="Matrix"/> onto the backend's transform
/// stack. Subsequent paint items between this <see cref="PushTransform"/> and
/// its matching <see cref="PopTransform"/> are rendered with <c>current ×
/// Matrix</c> applied — left-to-right composition matches CSS Transforms 1
/// §6.1 (the outer push applies last, so a nested transform sees its parent's
/// matrix as the surrounding coordinate frame).
/// <para>
/// The <see cref="DisplayListBuilder"/> pre-bakes the box's
/// <c>transform-origin</c> into the matrix
/// (<c>T(+origin) × M × T(-origin)</c>), so the backend just applies the
/// composed matrix verbatim — it does not need to know the box geometry.
/// </para>
/// </summary>
public sealed record PushTransform(Matrix2D Matrix) : DisplayItem;

/// <summary>Pops the most recent <see cref="PushTransform"/> off the backend stack.</summary>
public sealed record PopTransform : DisplayItem
{
    public static PopTransform Instance { get; } = new();
}

/// <summary>
/// Fills <paramref name="Bounds"/> with a CSS <c>&lt;gradient&gt;</c>
/// (<see href="https://www.w3.org/TR/css-images-3/#gradients">CSS Images 3 §3</see>).
/// The backend maps the gradient's color stops onto an ImageSharp gradient
/// brush (linear or radial) sized to <paramref name="Bounds"/>. Conic gradients
/// have no ImageSharp brush and are not emitted as this item.
/// </summary>
public sealed record FillGradient(Rect Bounds, CssGradient Gradient) : DisplayItem;
// ---------------------------------------------------------------------------
// wp:M5-css-14 — border-radius painting + box-shadow.
// Appended at the end per the shared-paint-file etiquette (DisplayItem.cs is
// shared with the gradients and text-decoration WPs).
// ---------------------------------------------------------------------------

/// <summary>
/// The four corner radii of a box, in CSS px, in
/// <c>top-left, top-right, bottom-right, bottom-left</c> order — the same order
/// the <c>border-*-radius</c> longhands and the <c>border-radius</c> shorthand
/// use. Each corner carries a horizontal (<c>Rx</c>) and vertical (<c>Ry</c>)
/// radius so elliptical corners are representable; the current builder fills
/// both from the single per-corner length, which is the common circular case.
/// </summary>
public readonly record struct CornerRadii(
    double TopLeftX, double TopLeftY,
    double TopRightX, double TopRightY,
    double BottomRightX, double BottomRightY,
    double BottomLeftX, double BottomLeftY)
{
    /// <summary>All corners square (no rounding).</summary>
    public static CornerRadii None { get; } = default;

    /// <summary>Builds uniform circular radii from one per-corner length each.</summary>
    public static CornerRadii Uniform(double topLeft, double topRight, double bottomRight, double bottomLeft)
        => new(topLeft, topLeft, topRight, topRight, bottomRight, bottomRight, bottomLeft, bottomLeft);

    /// <summary>True when every corner radius is zero (a plain rectangle).</summary>
    public bool IsZero =>
        TopLeftX <= 0 && TopLeftY <= 0 &&
        TopRightX <= 0 && TopRightY <= 0 &&
        BottomRightX <= 0 && BottomRightY <= 0 &&
        BottomLeftX <= 0 && BottomLeftY <= 0;
}

/// <summary>
/// Fills <paramref name="Bounds"/> with <paramref name="Color"/>, rounding the
/// corners by <paramref name="Radii"/> (CSS Backgrounds 3 §5). When the radii
/// are all zero this is equivalent to a <see cref="FillRect"/>; the painter may
/// short-circuit to a plain rectangle in that case.
/// </summary>
public sealed record FillRoundedRect(Rect Bounds, CornerRadii Radii, CssColor Color) : DisplayItem;

/// <summary>
/// Strokes the rounded-rect path described by <paramref name="Bounds"/> and
/// <paramref name="Radii"/> with a pen of <paramref name="Width"/> CSS px in
/// <paramref name="Color"/>. Used by the builder to paint a uniform rounded
/// border as a single centre-line ring; mixed per-side rounded borders are not
/// yet expressed through this primitive.
/// </summary>
public sealed record StrokeRoundedRect(Rect Bounds, CornerRadii Radii, CssColor Color, double Width) : DisplayItem;

/// <summary>
/// Casts a single outer drop shadow for a (possibly rounded) box per CSS
/// Backgrounds 3 §6. The shadow silhouette is <paramref name="Bounds"/> grown
/// by <paramref name="Spread"/> on every side (its corner radii grow with it),
/// translated by (<paramref name="OffsetX"/>, <paramref name="OffsetY"/>), and
/// softened by a Gaussian whose standard deviation is <paramref name="Blur"/>/2
/// (the spec defines the blur radius as one standard deviation × 2). When
/// <paramref name="Inset"/> is true the layer is an inner shadow; outer
/// painting is the supported path and the painter documents the inset gap.
/// </summary>
public sealed record DrawBoxShadow(
    Rect Bounds,
    CornerRadii Radii,
    double OffsetX,
    double OffsetY,
    double Blur,
    double Spread,
    CssColor Color,
    bool Inset) : DisplayItem;
