namespace Tessera.Gui.Imaging;

/// <summary>
/// A MAUI <see cref="ImageSource"/> carrying raw RGBA8888 pixels with a known
/// pixel density. Use this for content rasterized at the device's physical
/// resolution (e.g. the Skia-rendered page bitmap) so it displays crisp on
/// Retina displays without going through MAUI's stream/PNG decode (which would
/// land at <c>UIImage.Scale = 1</c> and force a 1×→2× upsample at display time).
/// </summary>
public sealed class RgbaImageSource : ImageSource, IRgbaImageSource
{
    public int PixelWidth { get; init; }
    public int PixelHeight { get; init; }
    public float Density { get; init; } = 1f;
    public ReadOnlyMemory<byte> Pixels { get; init; }

    public override bool IsEmpty
        => Pixels.IsEmpty || PixelWidth <= 0 || PixelHeight <= 0;
}
