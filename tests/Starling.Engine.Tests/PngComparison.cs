using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Tessera.Engine.Tests;

/// <summary>
/// Test helpers that lift ImageSharp PNGs into raw RGBA byte buffers
/// and dispatch to <see cref="Tessera.Common.Image.Ssim"/>. Kept here
/// because Tessera.Common deliberately has no image-library dependency
/// (see the SSIM source for why).
/// </summary>
internal static class PngComparison
{
    public static double Ssim(string actualPng, string goldenPng)
    {
        using var actual = Image.Load<Rgba32>(actualPng);
        using var golden = Image.Load<Rgba32>(goldenPng);
        if (actual.Width != golden.Width || actual.Height != golden.Height)
        {
            throw new InvalidOperationException(
                $"SSIM dimension mismatch: actual {actual.Width}x{actual.Height} vs golden {golden.Width}x{golden.Height}.");
        }

        var aBytes = ToRgba(actual);
        var bBytes = ToRgba(golden);
        return Tessera.Common.Image.Ssim.ComputeRgba(aBytes, bBytes, actual.Width, actual.Height);
    }

    public static bool BytesEqual(string a, string b)
    {
        var ba = File.ReadAllBytes(a);
        var bb = File.ReadAllBytes(b);
        return ba.AsSpan().SequenceEqual(bb);
    }

    private static byte[] ToRgba(Image<Rgba32> image)
    {
        var pixels = new byte[image.Width * image.Height * 4];
        image.CopyPixelDataTo(pixels);
        return pixels;
    }
}
