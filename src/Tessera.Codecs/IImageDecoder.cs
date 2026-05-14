using Tessera.Common.Image;

namespace Tessera.Codecs;

/// <summary>
/// Decodes a compressed image byte stream into a backend-neutral
/// <see cref="DecodedImage"/> (straight RGBA8888, top-down, tightly packed).
/// </summary>
/// <remarks>
/// Each platform backend (macOS ImageIO, Windows WIC, Linux libpng/libjpeg/
/// libwebp) implements this. <see cref="NativeImageDecoder"/> is the public
/// entry point that dispatches to the right one via <see cref="OperatingSystem"/>
/// runtime guards.
/// </remarks>
internal interface IImageDecoder
{
    /// <summary>
    /// Decode <paramref name="bytes"/> — a complete encoded image file
    /// (PNG/JPEG/WebP) — into a <see cref="DecodedImage"/>. The caller owns the
    /// returned image and must dispose it.
    /// </summary>
    /// <exception cref="ImageDecodeException">
    /// The bytes are not a recognised image, are truncated/corrupt, or the
    /// platform codec rejected them.
    /// </exception>
    DecodedImage Decode(ReadOnlySpan<byte> bytes);
}
