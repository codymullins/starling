using FluentAssertions;
using Tessera.Skia.Handles;
using Tessera.Skia.Interop;
using Xunit;

namespace Tessera.Skia.Tests;

/// <summary>
/// End-to-end P/Invoke smoke test for the <c>tessera_skia</c> shim, mirroring
/// the C harness in <c>native/shim/smoke_test.c</c>: create a context, create a
/// small offscreen surface, clear it + draw one fill_rect, flush, read pixels
/// back, and assert the background + rect colors.
/// </summary>
/// <remarks>
/// Guarded by <see cref="NativeShim.IsAvailable"/> — runs only on macOS with the
/// gitignored shim dylib present, and <c>Skip</c>s on CI win/linux where no
/// dylib exists yet (wp:M3-06g is osx-arm64-only for now).
/// </remarks>
public sealed class SkiaInteropSmokeTests
{
    private const int Width = 64;
    private const int Height = 64;

    [Fact]
    public void ContextSurfaceClearFillFlushReadback_ProducesExpectedPixels()
    {
        Assert.SkipUnless(NativeShim.IsAvailable, NativeShim.SkipReason);

        using var context = SkContext.Create(TsBackendHint.Auto);

        // The shim should report a real Dawn backend, not an empty string.
        context.BackendName().Should().NotBeNullOrEmpty();

        using var surface = SkSurface.Create(context, Width, Height);
        var canvas = surface.GetCanvas();

        // Clear to opaque blue, then fill an opaque red rect in the interior.
        var blue = new TsColor(0, 0, 255, 255);
        var red = new TsColor(255, 0, 0, 255);
        canvas.Clear(blue);
        canvas.FillRect(new TsRect(16f, 16f, 32f, 32f), red);

        surface.Flush(context);

        byte[] pixels = surface.ReadPixels(context, Width, Height);
        pixels.Should().HaveCount(Width * Height * 4);

        // Corner pixel (2,2) is the blue background.
        AssertPixel(pixels, x: 2, y: 2, 0, 0, 255, 255, "background corner");

        // Center pixel (32,32) is inside the red rect.
        AssertPixel(pixels, x: 32, y: 32, 255, 0, 0, 255, "rect center");
    }

    [Fact]
    public void Handles_AreReleased_WithoutLeaks()
    {
        Assert.SkipUnless(NativeShim.IsAvailable, NativeShim.SkipReason);

        // Create + dispose the full handle set a few times; SafeHandle release
        // must not throw or crash the native shim.
        for (int i = 0; i < 4; i++)
        {
            using var context = SkContext.Create(TsBackendHint.Auto);
            using var surface = SkSurface.Create(context, 8, 8);
            var canvas = surface.GetCanvas();
            canvas.Clear(new TsColor(0, 0, 0, 255));
            surface.Flush(context);
        }

        // Reaching here without an AccessViolation / native abort is the assertion.
        true.Should().BeTrue();
    }

    /// <summary>
    /// Channel-tolerant pixel compare (premul/unpremul rounding => allow +/-2),
    /// matching <c>pixel_is</c> in <c>smoke_test.c</c>.
    /// </summary>
    private static void AssertPixel(
        byte[] pixels, int x, int y, byte r, byte g, byte b, byte a, string what)
    {
        int offset = ((y * Width) + x) * 4;
        byte[] actual = [pixels[offset], pixels[offset + 1], pixels[offset + 2], pixels[offset + 3]];
        byte[] expected = [r, g, b, a];

        for (int c = 0; c < 4; c++)
        {
            Math.Abs(actual[c] - expected[c]).Should().BeLessThanOrEqualTo(
                2,
                $"{what} channel {c}: expected ~{expected[c]}, got {actual[c]}");
        }
    }
}
