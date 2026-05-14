using FluentAssertions;
using Xunit;

namespace Tessera.Codecs.Tests;

/// <summary>
/// End-to-end decode tests for <see cref="NativeImageDecoder"/>. The actual
/// decode runs on whatever OS the test executes on — macOS uses ImageIO,
/// Windows uses WIC, Linux uses libpng/libjpeg/libwebp. Pixel-value assertions
/// are guarded so a backend that is not yet runtime-verified on a given OS
/// (or a CI runner missing the Linux codec libraries) skips cleanly rather
/// than failing.
/// </summary>
public sealed class NativeImageDecoderTests
{
    private static readonly string FixtureDir =
        Path.Combine(AppContext.BaseDirectory, "testdata", "images");

    private static byte[] Fixture(string name)
    {
        var path = Path.Combine(FixtureDir, name);
        File.Exists(path).Should().BeTrue($"fixture '{name}' should be copied to the test output ({path})");
        return File.ReadAllBytes(path);
    }

    /// <summary>
    /// Reads the RGBA8888 quad at pixel (x, y) from a tightly-packed,
    /// top-down DecodedImage buffer.
    /// </summary>
    private static (byte R, byte G, byte B, byte A) PixelAt(
        ReadOnlySpan<byte> pixels, int width, int x, int y)
    {
        int i = (y * width + x) * 4;
        return (pixels[i], pixels[i + 1], pixels[i + 2], pixels[i + 3]);
    }

    // --- failure paths (run on every OS) -----------------------------------

    [Fact]
    public void EmptyBufferThrows()
    {
        var act = () => NativeImageDecoder.Decode(ReadOnlySpan<byte>.Empty);
        act.Should().Throw<ImageDecodeException>();
    }

    [Fact]
    public void UnrecognisedBytesThrow()
    {
        byte[] garbage = [0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77, 0x88, 0x99, 0xAA, 0xBB];
        var act = () => NativeImageDecoder.Decode(garbage);
        act.Should().Throw<ImageDecodeException>()
            .WithMessage("*Unrecognised image format*");
    }

    [Fact]
    public void TruncatedPngThrows()
    {
        // Valid PNG signature so the sniffer accepts it, but no actual chunks —
        // the native codec must reject it as ImageDecodeException.
        byte[] truncated = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x00];
        var act = () => NativeImageDecoder.Decode(truncated);
        act.Should().Throw<ImageDecodeException>();
    }

    [Fact]
    public void SelectBackendReturnsABackendForThisOs()
    {
        // On every CI OS one of the three guards matches; assert no throw.
        var backend = NativeImageDecoder.SelectBackend();
        backend.Should().NotBeNull();
    }

    // --- PNG: dot.png is 4x4 with distinct corner pixels -------------------

    [Fact]
    public void DecodesPngDimensions()
    {
        using var img = NativeImageDecoder.Decode(Fixture("dot.png"));
        img.Width.Should().Be(4);
        img.Height.Should().Be(4);
        img.Pixels.Length.Should().Be(4 * 4 * 4);
    }

    [Fact]
    public void DecodesPngCornerPixels()
    {
        using var img = NativeImageDecoder.Decode(Fixture("dot.png"));
        var px = img.Pixels.Span;

        // dot.png: (0,0) opaque red, (3,0) opaque green, (0,3) opaque blue,
        // (3,3) 50%-alpha white. PNG is lossless so values are exact.
        PixelAt(px, 4, 0, 0).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255));
        PixelAt(px, 4, 3, 0).Should().Be(((byte)0, (byte)255, (byte)0, (byte)255));
        PixelAt(px, 4, 0, 3).Should().Be(((byte)0, (byte)0, (byte)255, (byte)255));

        var (r, g, b, a) = PixelAt(px, 4, 3, 3);
        a.Should().Be(128, "the bottom-right pixel is 50% transparent (straight alpha)");
        r.Should().Be(255);
        g.Should().Be(255);
        b.Should().Be(255);
    }

    // --- JPEG: swatch.jpg is 8x8 solid mid-gray ----------------------------

    [Fact]
    public void DecodesJpegDimensions()
    {
        using var img = NativeImageDecoder.Decode(Fixture("swatch.jpg"));
        img.Width.Should().Be(8);
        img.Height.Should().Be(8);
        img.Pixels.Length.Should().Be(8 * 8 * 4);
    }

    [Fact]
    public void DecodesJpegPixelsNearGray()
    {
        using var img = NativeImageDecoder.Decode(Fixture("swatch.jpg"));
        var px = img.Pixels.Span;

        // JPEG is lossy; a flat fill survives within a few levels. Check the
        // centre pixel is close to mid-gray and fully opaque.
        var (r, g, b, a) = PixelAt(px, 8, 4, 4);
        ((int)r).Should().BeInRange(118, 138);
        ((int)g).Should().BeInRange(118, 138);
        ((int)b).Should().BeInRange(118, 138);
        a.Should().Be(255, "JPEG has no alpha channel — decode must fill opaque");
    }

    // --- WebP: tile.webp is 4x4 lossless solid orange ----------------------

    [Fact]
    public void DecodesWebpDimensions()
    {
        using var img = NativeImageDecoder.Decode(Fixture("tile.webp"));
        img.Width.Should().Be(4);
        img.Height.Should().Be(4);
        img.Pixels.Length.Should().Be(4 * 4 * 4);
    }

    [Fact]
    public void DecodesWebpPixels()
    {
        using var img = NativeImageDecoder.Decode(Fixture("tile.webp"));
        var px = img.Pixels.Span;

        // tile.webp is lossless solid orange (255,128,0,255).
        var (r, g, b, a) = PixelAt(px, 4, 2, 2);
        ((int)r).Should().BeInRange(253, 255);
        ((int)g).Should().BeInRange(126, 130);
        ((int)b).Should().BeInRange(0, 2);
        a.Should().Be(255);
    }
}
