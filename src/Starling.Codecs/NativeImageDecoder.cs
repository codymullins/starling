using Starling.Codecs.Linux;
using Starling.Codecs.Mac;
using Starling.Codecs.Windows;
using Starling.Common.Image;

namespace Starling.Codecs;

/// <summary>
/// Public entry point for OS-native image decoding. Sniffs the format, then
/// dispatches to the platform backend — ImageIO on macOS, WIC on Windows,
/// libpng/libjpeg/libwebp on Linux — selected by <see cref="OperatingSystem"/>
/// runtime guards. Every backend returns the same backend-neutral
/// <see cref="DecodedImage"/> (straight RGBA8888, top-down, tightly packed).
/// </summary>
/// <remarks>
/// This is the decode half of the Phase-8 native-interop pivot: the engine no
/// longer decodes via ImageSharp. Encode stays elsewhere (the paint backend).
/// All failures — unknown format, corrupt data, unsupported OS — surface as
/// <see cref="ImageDecodeException"/> so callers have a single catch.
/// </remarks>
public static class NativeImageDecoder
{
    // Hard cap for decoded RGBA8888 surfaces to prevent memory-exhaustion
    // attacks from oversized/untrusted image dimensions.
    internal const int MaxDecodedImageBytes = 256 * 1024 * 1024; // 256 MiB

    /// <summary>
    /// Decode a complete encoded image (PNG/JPEG/WebP) into a
    /// <see cref="DecodedImage"/>. The caller owns the result and must dispose
    /// it once the pixels have been consumed.
    /// </summary>
    /// <exception cref="ImageDecodeException">
    /// The bytes are empty, an unrecognised format, corrupt/truncated, or the
    /// host OS has no supported codec backend, or decoded dimensions exceed
    /// the safety cap.
    /// </exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            throw new ImageDecodeException("Cannot decode an empty image buffer.");

        var format = ImageFormatSniffer.Detect(bytes);
        if (format == ImageFormat.Svg)
            throw new ImageDecodeException(
                "SVG is a vector format and cannot be decoded by the OS-native raster " +
                "codecs. Route it to the managed SVG rasterizer (Starling.Paint); the " +
                "engine does this automatically via ImageFormatSniffer.LooksLikeSvg.");

        if (format == ImageFormat.Unknown)
            throw new ImageDecodeException(
                "Unrecognised image format: no PNG/JPEG/WebP/GIF/BMP signature in the leading bytes.");

        IImageDecoder backend = SelectBackend();
        return backend.Decode(bytes);
    }

    /// <summary>
    /// Validates decoded bitmap dimensions and enforces a global safety cap on
    /// RGBA8888 output size.
    /// </summary>
    internal static (int Width, int Height, int ByteLength) ValidateDecodedDimensions(long width, long height)
    {
        if (width <= 0 || height <= 0)
            throw new ImageDecodeException($"Decoded image has invalid dimensions {width}x{height}.");
        if (width > int.MaxValue || height > int.MaxValue)
            throw new ImageDecodeException($"Decoded image dimensions {width}x{height} exceed supported integer range.");

        long bytes;
        try
        {
            bytes = checked(width * height * 4L);
        }
        catch (OverflowException)
        {
            throw new ImageDecodeException($"Decoded image dimensions {width}x{height} overflow RGBA byte size.");
        }

        if (bytes > MaxDecodedImageBytes)
            throw new ImageDecodeException(
                $"Decoded image dimensions {width}x{height} require {bytes} bytes, " +
                $"exceeding the safety cap of {MaxDecodedImageBytes} bytes.");

        return ((int)width, (int)height, (int)bytes);
    }

    /// <summary>
    /// True when <paramref name="bytes"/> sniff as an SVG document. The engine
    /// uses this to route SVG to the managed vector rasterizer
    /// (<c>Starling.Paint</c>) instead of this OS-native raster path, which can
    /// only decode bitmap containers. Exposed publicly because the
    /// <see cref="ImageFormat"/> enum and its sniffer are internal to this
    /// interop seam.
    /// </summary>
    public static bool IsSvg(ReadOnlySpan<byte> bytes) => ImageFormatSniffer.LooksLikeSvg(bytes);

    /// <summary>
    /// Pick the decoder backend for the current OS. Split out so tests can
    /// assert the dispatch decision.
    /// </summary>
    internal static IImageDecoder SelectBackend()
    {
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsMacCatalyst())
            return new ImageIODecoder();
        if (OperatingSystem.IsWindows())
            return new WicDecoder();
        if (OperatingSystem.IsLinux())
            return new LinuxImageDecoder();

        throw new ImageDecodeException(
            $"No native image decoder backend for this platform ({Environment.OSVersion.Platform}).");
    }
}
