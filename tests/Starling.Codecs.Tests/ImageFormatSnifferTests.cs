using FluentAssertions;
using Xunit;

namespace Tessera.Codecs.Tests;

/// <summary>
/// Magic-byte sniffer tests. These are pure logic — no native codec — so they
/// run identically on every OS.
/// </summary>
public sealed class ImageFormatSnifferTests
{
    [Fact]
    public void DetectsPng()
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00];
        ImageFormatSniffer.Detect(header).Should().Be(ImageFormat.Png);
    }

    [Fact]
    public void DetectsJpeg()
    {
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10];
        ImageFormatSniffer.Detect(header).Should().Be(ImageFormat.Jpeg);
    }

    [Fact]
    public void DetectsWebp()
    {
        byte[] header =
        [
            (byte)'R', (byte)'I', (byte)'F', (byte)'F',
            0x00, 0x00, 0x00, 0x00,
            (byte)'W', (byte)'E', (byte)'B', (byte)'P',
        ];
        ImageFormatSniffer.Detect(header).Should().Be(ImageFormat.Webp);
    }

    [Fact]
    public void DetectsGif()
    {
        ImageFormatSniffer.Detect("GIF89a"u8).Should().Be(ImageFormat.Gif);
        ImageFormatSniffer.Detect("GIF87a"u8).Should().Be(ImageFormat.Gif);
    }

    [Fact]
    public void DetectsBmp()
    {
        ImageFormatSniffer.Detect("BM\x00\x00"u8).Should().Be(ImageFormat.Bmp);
    }

    [Fact]
    public void UnknownForGarbage()
    {
        byte[] garbage = [0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08, 0x09, 0x0A, 0x0B];
        ImageFormatSniffer.Detect(garbage).Should().Be(ImageFormat.Unknown);
    }

    [Fact]
    public void UnknownForEmpty()
    {
        ImageFormatSniffer.Detect(ReadOnlySpan<byte>.Empty).Should().Be(ImageFormat.Unknown);
    }

    [Fact]
    public void UnknownForTruncatedHeader()
    {
        // PNG signature is 8 bytes; 4 is not enough to match.
        byte[] partial = [0x89, 0x50, 0x4E, 0x47];
        ImageFormatSniffer.Detect(partial).Should().Be(ImageFormat.Unknown);
    }
}
