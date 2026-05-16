using Tessera.Codecs.Linux;
using Tessera.Codecs.Mac;
using Tessera.Codecs.Windows;
using Tessera.Common.Image;

namespace Tessera.Codecs;

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
    /// <summary>
    /// Decode a complete encoded image (PNG/JPEG/WebP) into a
    /// <see cref="DecodedImage"/>. The caller owns the result and must dispose
    /// it once the pixels have been consumed.
    /// </summary>
    /// <exception cref="ImageDecodeException">
    /// The bytes are empty, an unrecognised format, corrupt/truncated, or the
    /// host OS has no supported codec backend.
    /// </exception>
    public static DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
            throw new ImageDecodeException("Cannot decode an empty image buffer.");

        var format = ImageFormatSniffer.Detect(bytes);
        if (format == ImageFormat.Unknown)
            throw new ImageDecodeException(
                "Unrecognised image format: no PNG/JPEG/WebP/GIF/BMP signature in the leading bytes.");

        IImageDecoder backend = SelectBackend();
        return backend.Decode(bytes);
    }

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
