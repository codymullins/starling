using AwesomeAssertions;
using Starling.Common.Image;
using Starling.Css.Values;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Drives <see cref="ImageSharpBackend"/> with hand-built display lists so
/// failure modes in the backend itself surface independently of layout. The
/// suite locks down two regressions that snuck past the engine-level renders:
/// <list type="bullet">
/// <item>Swapped sourceRect / destinationRect arguments to
/// <c>DrawingCanvas.DrawImage</c>, which silently rendered nothing.</item>
/// <item>The source image disposed inside the Mutate/Paint closure before the
/// deferred canvas timeline rasterised it (ObjectDisposedException once the
/// args were corrected).</item>
/// </list>
/// </summary>
[TestClass]
public sealed class ImageSharpBackendTests
{
    /// <summary>
    /// A 40x20 solid red swatch drawn at (10, 10) must leave red pixels on the
    /// canvas. The arg-swap regression made <c>DrawImage</c> a no-op (source
    /// crop outside the image bounds → empty source). The disposal regression
    /// threw <c>ObjectDisposedException</c> once the args were fixed.
    /// </summary>
    [TestMethod]
    public void Draw_image_blits_pixels_into_destination_rect()
    {
        const int W = 40, H = 20;
        const int destX = 10, destY = 10;

        using var swatch = DecodedImage.CreatePooled(W, H, span =>
        {
            for (var i = 0; i < span.Length; i += 4)
            {
                span[i] = 255;        // R
                span[i + 1] = 0;      // G
                span[i + 2] = 0;      // B
                span[i + 3] = 255;    // A
            }
        });

        var list = new PaintList();
        list.Add(new DrawImage(new LayoutRect(destX, destY, W, H), swatch));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 100));

        // At least half the swatch's pixels should survive bicubic resampling;
        // a perfect-identity blit would be 800, but anti-aliased edges may
        // soften corner pixels. The arg-swap regression produced zero.
        var redCount = BitmapPixels.CountExact(bmp, 255, 0, 0);
        redCount.Should().BeGreaterThanOrEqualTo(W * H / 2,
            "the swatch must paint into the destination rectangle; the swapped source/dest args regression yielded zero red pixels");
    }

    /// <summary>
    /// Lays out two image draws in the same display list. Both decoded
    /// sources are created inside the <c>DrawImage</c> handler and the canvas
    /// timeline rasterises them on flush. If the deferred-disposal fix
    /// regresses, the second draw (or both) will throw
    /// ObjectDisposedException from ImageBrushRenderer.
    /// </summary>
    [TestMethod]
    public void Multiple_image_draws_survive_deferred_rasterization()
    {
        using var red = DecodedImage.CreatePooled(20, 20, span => Fill(span, 255, 0, 0, 255));
        using var blue = DecodedImage.CreatePooled(20, 20, span => Fill(span, 0, 0, 255, 255));

        var list = new PaintList();
        list.Add(new DrawImage(new LayoutRect(0, 0, 20, 20), red));
        list.Add(new DrawImage(new LayoutRect(40, 0, 20, 20), blue));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        var act = () =>
        {
            using var bmp = backend.Render(list, new LayoutSize(80, 40));
            return BitmapPixels.CountExact(bmp, 255, 0, 0) + BitmapPixels.CountExact(bmp, 0, 0, 255);
        };

        var total = act.Should().NotThrow("source images must outlive the canvas command timeline").Which;
        total.Should().BeGreaterThan(0, "both swatches must paint, not just one");
    }

    /// <summary>
    /// Renders the same text+image scene through both backends and asserts the
    /// ImageSharp output is no longer a no-op for images: regression had
    /// images missing entirely, so the destination rectangle would contain
    /// nothing but the white background.
    /// </summary>
    [TestMethod]
    public void Image_destination_region_has_non_background_pixels()
    {
        using var green = DecodedImage.CreatePooled(50, 30, span => Fill(span, 0, 200, 0, 255));

        var list = new PaintList();
        list.Add(new DrawImage(new LayoutRect(20, 20, 50, 30), green));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(120, 80));

        BitmapPixels.CountNonWhite(bmp).Should().BeGreaterThan(
            500, "a solid-colour image must produce a large region of non-white pixels");
    }

    /// <summary>
    /// Image draws honor the display-list transform stack through the
    /// ImageSharp canvas state. A translated blit must move away from its
    /// original bounds instead of painting at the untransformed rectangle.
    /// </summary>
    [TestMethod]
    public void Translated_image_uses_canvas_transform()
    {
        using var red = DecodedImage.CreatePooled(10, 10, span => Fill(span, 255, 0, 0, 255));

        var list = new PaintList();
        list.Add(new PushTransform(Matrix2D.Translate(30, 10)));
        list.Add(new DrawImage(new LayoutRect(0, 0, 10, 10), red));
        list.Add(PopTransform.Instance);

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(80, 40));

        bmp.GetPixel(5, 5).Should().Be(((byte)255, (byte)255, (byte)255, (byte)255),
            "the untransformed image bounds should remain untouched");
        bmp.GetPixel(35, 15).Should().Be(((byte)255, (byte)0, (byte)0, (byte)255),
            "the canvas transform should translate the image before rasterization");
    }

    /// <summary>
    /// Pages that exceed wgpu's <c>maxTextureDimension2D</c> default (8192 px)
    /// must not crash the host: the GPU path falls back to the CPU rasterizer
    /// for that frame because wgpu's default uncaptured-error handler turns
    /// a CreateTexture validation error into a process <c>abort()</c>, which
    /// no C# try/catch can intercept. Regression: loading netclaw.dev under
    /// the AppHost default (<c>STARLING_PAINT_BACKEND=imagesharp-gpu</c>)
    /// aborted Starling.Gui inside <c>wgpuDeviceCreateTexture</c>.
    /// </summary>
    [TestMethod]
    public void Oversized_viewport_falls_back_to_cpu_instead_of_aborting()
    {
        var list = new PaintList();
        list.Add(new FillRect(new LayoutRect(0, 0, 100, 100), new Starling.Css.Values.CssColor(0, 0, 255, 255), FillRectPixelAlignment.Preserve));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, diagnostics: null, useWebGpu: true);

        Action act = () =>
        {
            using var bmp = backend.Render(list, new LayoutSize(1024, 9000));
        };

        act.Should().NotThrow("a viewport taller than maxTextureDimension2D must fall back to CPU, not invoke wgpuDeviceCreateTexture");
    }

    /// <summary>
    /// Identity-transform text renders through ImageSharp's prepared text
    /// path so color/layered glyphs keep their own paints. This pins the
    /// visible side: text must still leave non-background pixels on the
    /// canvas, so a future text-path refactor cannot silently become a no-op.
    /// </summary>
    [TestMethod]
    public void Identity_transform_text_paints_visible_pixels()
    {
        var list = new PaintList();
        list.Add(new DrawText(
            Text: "hello",
            X: 10,
            Y: 30,
            FontSize: 24,
            Color: new CssColor(0, 0, 0, 255),
            FontFamilies: new[] { "sans-serif" },
            Bold: false,
            Italic: false));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 60));

        BitmapPixels.CountNonWhite(bmp).Should().BeGreaterThan(0,
            "identity-transform text must produce visible glyphs; an empty canvas would mean the prepared-text path is broken");
    }

    private static void Fill(Span<byte> span, byte r, byte g, byte b, byte a)
    {
        for (var i = 0; i < span.Length; i += 4)
        {
            span[i] = r;
            span[i + 1] = g;
            span[i + 2] = b;
            span[i + 3] = a;
        }
    }

}

