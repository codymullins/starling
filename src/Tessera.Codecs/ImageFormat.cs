namespace Tessera.Codecs;

/// <summary>The container formats <see cref="NativeImageDecoder"/> recognises.</summary>
internal enum ImageFormat
{
    Unknown = 0,
    Png,
    Jpeg,
    Webp,
    Gif,
    Bmp,
}

/// <summary>
/// Classifies an encoded image by its leading magic bytes. The platform
/// backends (especially Linux, which binds a separate library per codec) use
/// this to pick the right decode path without trial-and-error.
/// </summary>
internal static class ImageFormatSniffer
{
    /// <summary>
    /// Inspect the header of <paramref name="bytes"/> and return the detected
    /// <see cref="ImageFormat"/>, or <see cref="ImageFormat.Unknown"/> if no
    /// signature matches.
    /// </summary>
    public static ImageFormat Detect(ReadOnlySpan<byte> bytes)
    {
        // PNG: 89 50 4E 47 0D 0A 1A 0A
        if (bytes.Length >= 8 &&
            bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
            bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A)
        {
            return ImageFormat.Png;
        }

        // JPEG: FF D8 FF
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ImageFormat.Jpeg;
        }

        // WebP: "RIFF" .... "WEBP"
        if (bytes.Length >= 12 &&
            bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
            bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P')
        {
            return ImageFormat.Webp;
        }

        // GIF: "GIF87a" / "GIF89a"
        if (bytes.Length >= 6 &&
            bytes[0] == (byte)'G' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' &&
            bytes[3] == (byte)'8' && (bytes[4] == (byte)'7' || bytes[4] == (byte)'9') && bytes[5] == (byte)'a')
        {
            return ImageFormat.Gif;
        }

        // BMP: "BM"
        if (bytes.Length >= 2 && bytes[0] == (byte)'B' && bytes[1] == (byte)'M')
        {
            return ImageFormat.Bmp;
        }

        return ImageFormat.Unknown;
    }
}
