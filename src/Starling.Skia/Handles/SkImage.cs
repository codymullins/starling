using Tessera.Skia.Interop;

namespace Tessera.Skia.Handles;

/// <summary>
/// A decoded RGBA8888 image ready to be drawn onto a <see cref="SkCanvas"/>.
/// </summary>
/// <remarks>
/// HEADER DIVERGENCE: <c>tessera_skia.h</c> (as finalized by wp:M3-06g) has no
/// <c>TsImage</c> opaque handle and no <c>ts_image_create</c>/<c>ts_image_destroy</c>
/// pair — <c>ts_canvas_draw_image</c> takes raw tightly-packed RGBA8888 pixels
/// directly. So unlike the other Sk* wrappers there is no native handle to own
/// and therefore no <see cref="System.Runtime.InteropServices.SafeHandle"/>:
/// this is a managed-only value holder. It is kept as a distinct type for a
/// uniform call site (<see cref="SkCanvas.DrawImage(SkImage, TsRect)"/>) and so
/// a future ABI that does add a cached <c>TsImage</c> handle can slot in here
/// without churning callers.
/// </remarks>
internal sealed class SkImage
{
    private readonly byte[] _pixels;

    private SkImage(byte[] pixels, int width, int height)
    {
        _pixels = pixels;
        Width = width;
        Height = height;
    }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>The tightly-packed RGBA8888 pixel buffer (length = Width*Height*4).</summary>
    public ReadOnlySpan<byte> Pixels => _pixels;

    /// <summary>
    /// Wraps a tightly-packed RGBA8888 pixel buffer. The buffer is copied so the
    /// image owns an immutable snapshot.
    /// </summary>
    public static SkImage FromPixels(ReadOnlySpan<byte> pixels, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentException($"Image dimensions must be positive ({width}x{height}).");

        int expected = checked(width * height * 4);
        if (pixels.Length != expected)
            throw new ArgumentException(
                $"RGBA8888 buffer length {pixels.Length} != width*height*4 ({expected}).");

        return new SkImage(pixels.ToArray(), width, height);
    }
}
