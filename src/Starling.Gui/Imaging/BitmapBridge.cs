using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Starling.Common.Image;

namespace Starling.Gui.Imaging;

/// <summary>
/// Copies a <see cref="RenderedBitmap"/> (top-down, tightly-packed,
/// straight-alpha RGBA8888 — see RenderedBitmap.cs) into an Avalonia
/// <see cref="WriteableBitmap"/>. DPI is left at the default (96, 96):
/// the WebviewPanel uses Stretch.Uniform with explicit DIP-sized Width/Height,
/// so the bitmap's reported logical size is irrelevant and the renderer
/// downscales physical pixels 1:1 onto Retina.
/// </summary>
internal static class BitmapBridge
{
    private static readonly Vector DefaultDpi = new(96.0, 96.0);

    public static WriteableBitmap ToWriteableBitmap(RenderedBitmap source, double _ignoredScale)
    {
        ArgumentNullException.ThrowIfNull(source);

        var pixelSize = new PixelSize(source.Width, source.Height);
        var bitmap = new WriteableBitmap(pixelSize, DefaultDpi, PixelFormat.Rgba8888, AlphaFormat.Unpremul);

        using (var fb = bitmap.Lock())
        {
            // RenderedBitmap rows are stride = Width*4 (no padding). Avalonia's
            // FrameBuffer can report a wider RowBytes (Skia aligns to 4 anyway,
            // but be defensive in case a future backend pads). Copy row-by-row
            // when strides differ, single Marshal.Copy when they match.
            var srcStride = source.Width * 4;
            if (fb.RowBytes == srcStride)
            {
                Marshal.Copy(source.Rgba, 0, fb.Address, source.Rgba.Length);
            }
            else
            {
                for (var y = 0; y < source.Height; y++)
                {
                    var rowStart = y * srcStride;
                    var dst = fb.Address + (y * fb.RowBytes);
                    Marshal.Copy(source.Rgba, rowStart, dst, srcStride);
                }
            }
        }

        return bitmap;
    }
}
