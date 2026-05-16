using Microsoft.Maui;

namespace Tessera.Gui.Imaging;

/// <summary>
/// A pre-rendered, raw straight-alpha RGBA8888 bitmap with an explicit pixel
/// density. Backed by <see cref="RgbaImageSource"/>; consumed by the
/// platform-specific <c>RgbaImageSourceService</c> which produces a
/// <c>UIImage</c> initialized with <see cref="Density"/> as its
/// <c>UIImage.Scale</c>, so a layout sized in points displays the bitmap
/// at native pixel resolution with no resampling.
/// </summary>
public interface IRgbaImageSource : IImageSource
{
    int PixelWidth { get; }
    int PixelHeight { get; }
    /// <summary>Logical→physical scale factor (e.g. 1.0 standard, 2.0 Retina).</summary>
    float Density { get; }
    /// <summary>
    /// Tightly-packed, top-down, straight-alpha RGBA8888. Length must equal
    /// <c>PixelWidth * PixelHeight * 4</c>.
    /// </summary>
    ReadOnlyMemory<byte> Pixels { get; }
}
