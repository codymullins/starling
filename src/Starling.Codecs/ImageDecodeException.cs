namespace Tessera.Codecs;

/// <summary>
/// Thrown when an image cannot be decoded — unrecognised format, truncated or
/// corrupt data, an unsupported platform, or a failure inside the OS-native
/// codec. <c>Tessera.Engine</c>'s image fetcher catches this and degrades
/// the element to its <c>alt</c> text rather than crashing.
/// </summary>
public sealed class ImageDecodeException : Exception
{
    public ImageDecodeException()
    {
    }

    public ImageDecodeException(string message)
        : base(message)
    {
    }

    public ImageDecodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
