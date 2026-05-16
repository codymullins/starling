using System.Runtime.Versioning;
using Tessera.Common.Image;

namespace Tessera.Codecs.Linux;

/// <summary>
/// Linux image decoder. Linux ships no single system imaging framework, so this
/// backend binds one well-known shared library per format — <c>libpng16</c>,
/// <c>libjpeg</c>/<c>libjpeg-turbo</c>, <c>libwebp</c> — and uses the
/// magic-byte sniffer to pick which one to call. Each per-codec binding lives
/// in its own file (<see cref="LibPngDecoder"/>, <see cref="LibJpegDecoder"/>,
/// <see cref="LibWebpDecoder"/>).
/// </summary>
/// <remarks>
/// The libraries are referenced by soname (<c>libpng16.so.16</c> etc.) so the
/// runtime resolves whatever the distro packaged. CI installs
/// <c>libpng16-16 libjpeg-turbo8 libwebp7</c> (that apt step lands in
/// wp:M3-06l-ci-policy). Compile-checked on every OS; runtime-exercised only on
/// the Linux CI leg.
/// </remarks>
[SupportedOSPlatform("linux")]
internal sealed class LinuxImageDecoder : IImageDecoder
{
    public DecodedImage Decode(ReadOnlySpan<byte> bytes)
    {
        var format = ImageFormatSniffer.Detect(bytes);
        try
        {
            return format switch
            {
                ImageFormat.Png => LibPngDecoder.Decode(bytes),
                ImageFormat.Jpeg => LibJpegDecoder.Decode(bytes),
                ImageFormat.Webp => LibWebpDecoder.Decode(bytes),
                _ => throw new ImageDecodeException(
                    $"Linux backend has no codec for image format '{format}'. " +
                    "Supported: PNG (libpng16), JPEG (libjpeg-turbo), WebP (libwebp)."),
            };
        }
        catch (ImageDecodeException)
        {
            throw;
        }
        catch (DllNotFoundException ex)
        {
            throw new ImageDecodeException(
                $"Linux backend: native codec library for '{format}' is not installed " +
                "(need libpng16 / libjpeg-turbo / libwebp).", ex);
        }
        catch (Exception ex)
        {
            throw new ImageDecodeException($"Linux backend: native decode of '{format}' failed.", ex);
        }
    }
}
