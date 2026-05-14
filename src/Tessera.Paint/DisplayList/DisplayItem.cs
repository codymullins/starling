using Tessera.Common.Image;
using Tessera.Css.Values;
using Tessera.Layout;

namespace Tessera.Paint.DisplayList;

/// <summary>
/// Discriminated union of paint operations. Painters replay these in order to
/// produce the final raster. Decoupling paint from layout this way lets us
/// cache, diff, and serialize the paint stream.
/// </summary>
public abstract record DisplayItem;

public sealed record FillRect(Rect Bounds, CssColor Color) : DisplayItem;

public sealed record StrokeRect(Rect Bounds, CssColor Color, double Width) : DisplayItem;

public sealed record DrawText(
    string Text,
    double X,
    double Y,
    double FontSize,
    CssColor Color,
    string FontFamily,
    bool Bold,
    bool Italic) : DisplayItem;

/// <summary>
/// Blit a decoded image into <paramref name="Bounds"/>. <paramref name="Source"/>
/// is a backend-neutral <see cref="DecodedImage"/> (straight RGBA8888); the
/// paint backend reads its pixels directly. If <c>Bounds</c> differs from the
/// source's native size the backend resamples.
/// </summary>
public sealed record DrawImage(Rect Bounds, DecodedImage Source) : DisplayItem;
