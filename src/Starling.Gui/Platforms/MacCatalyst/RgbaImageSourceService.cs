using CoreGraphics;
using Foundation;
using Microsoft.Maui;
using Tessera.Gui.Imaging;
using UIKit;

namespace Tessera.Gui.Platforms.MacCatalyst;

/// <summary>
/// MAUI image-source service that materialises an <see cref="IRgbaImageSource"/>
/// as a <see cref="UIImage"/> initialised with the source's
/// <see cref="IRgbaImageSource.Density"/> as its <c>UIImage.Scale</c>. Result:
/// a `(W,H)` point-sized layout shows a `W·Density × H·Density` pixel image at
/// native resolution — no resampling between the Skia raster and the screen.
/// </summary>
internal sealed class RgbaImageSourceService : ImageSourceService, IImageSourceService<IRgbaImageSource>
{
    public Task<IImageSourceServiceResult<UIImage>?> GetImageAsync(
        IRgbaImageSource imageSource, float scale = 1f, CancellationToken cancellationToken = default)
    {
        var ui = CreateUIImage(imageSource);
        return Task.FromResult<IImageSourceServiceResult<UIImage>?>(
            ui is null ? null : new ImageSourceServiceResult(ui));
    }

    public override Task<IImageSourceServiceResult<UIImage>?> GetImageAsync(
        IImageSource imageSource, float scale = 1f, CancellationToken cancellationToken = default)
        => GetImageAsync((IRgbaImageSource)imageSource, scale, cancellationToken);

    private static UIImage? CreateUIImage(IRgbaImageSource src)
    {
        if (src.IsEmpty) return null;

        // NSData.FromArray copies the bytes, so the managed buffer's lifetime
        // ends here — the data is owned by NSData → CGDataProvider → CGImage →
        // UIImage downstream.
        var bytes = src.Pixels.ToArray();
        using var data = NSData.FromArray(bytes);
        using var provider = new CGDataProvider(data);
        using var colorSpace = CGColorSpace.CreateSrgb();

        // RenderedBitmap is straight (non-premultiplied) RGBA8888 in memory order
        // R,G,B,A — i.e. CGBitmapFlags.Last on a big-endian byte order. The
        // ByteOrder32Big modifier locks that interpretation on little-endian
        // hardware.
        var flags = CGBitmapFlags.Last | CGBitmapFlags.ByteOrder32Big;
        var bytesPerRow = src.PixelWidth * 4;
        var cgImage = new CGImage(
            width: src.PixelWidth,
            height: src.PixelHeight,
            bitsPerComponent: 8,
            bitsPerPixel: 32,
            bytesPerRow: bytesPerRow,
            colorSpace: colorSpace,
            bitmapFlags: flags,
            provider: provider,
            decode: null,
            shouldInterpolate: true,
            intent: CGColorRenderingIntent.Default);

        return UIImage.FromImage(cgImage, scale: (nfloat)src.Density, orientation: UIImageOrientation.Up);
    }
}
