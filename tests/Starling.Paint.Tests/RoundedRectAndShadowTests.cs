using AwesomeAssertions;
using Starling.Css.Values;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;
using LayoutRect = Starling.Layout.Rect;
using LayoutSize = Starling.Layout.Size;
using PaintList = Starling.Paint.DisplayList.DisplayList;

namespace Starling.Paint.Tests;

/// <summary>
/// Pixel probes for wp:M5-css-14 — <c>border-radius</c> rounded fills and
/// <c>box-shadow</c> outer drop shadows, driven through
/// <see cref="ImageSharpBackend"/> with hand-built display lists so the paint
/// behaviour is exercised independently of layout.
/// </summary>
[TestClass]
public sealed class RoundedRectAndShadowTests
{
    private static readonly CssColor Red = new(255, 0, 0, 255);

    [TestMethod]
    public void Rounded_fill_leaves_extreme_corner_unpainted_but_fills_centre()
    {
        // A 100x100 red box at (10,10) with a 24px corner radius. The extreme
        // top-left pixel (10,10) sits outside the rounded silhouette, so it must
        // stay white; the centre must be solid red.
        const int boxX = 10, boxY = 10, boxW = 100, boxH = 100;
        var radii = CornerRadii.Uniform(24, 24, 24, 24);

        var list = new PaintList();
        list.Add(new FillRoundedRect(new LayoutRect(boxX, boxY, boxW, boxH), radii, Red));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(160, 160));

        // Extreme corner pixel — well inside the 24px corner quadrant — is cut.
        bmp.GetPixel(boxX + 1, boxY + 1).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the rounded corner must not be painted by the fill");

        // Centre is solidly filled.
        bmp.GetPixel(boxX + boxW / 2, boxY + boxH / 2).Should().Be(
            ((byte)255, (byte)0, (byte)0, (byte)255),
            "the interior of a rounded fill stays the fill colour");

        // Sanity: the rounded fill still covers most of the box.
        var redCount = BitmapPixels.CountExact(bmp, 255, 0, 0);
        redCount.Should().BeGreaterThan(boxW * boxH / 2,
            "a rounded fill clips only the corners, not the bulk of the box");
        redCount.Should().BeLessThan(boxW * boxH,
            "the four rounded corners remove some area from the square");
    }

    [TestMethod]
    public void Outer_drop_shadow_darkens_pixels_offset_from_the_box()
    {
        // A 60x60 box at (40,40); shadow offset 12px down-right, soft blur. The
        // shadow must darken canvas pixels just past the box's bottom-right
        // edge — a region that is pure white without the shadow.
        var box = new LayoutRect(40, 40, 60, 60);
        var shadowColor = new CssColor(0, 0, 0, 200);

        var list = new PaintList();
        list.Add(new DrawBoxShadow(box, CornerRadii.None, OffsetX: 12, OffsetY: 12, Blur: 8, Spread: 0, shadowColor, Inset: false));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(200, 200));

        // A point just below-right of the box, inside the offset shadow.
        var (r, g, b, _) = bmp.GetPixel((int)(box.Right + 6), (int)(box.Bottom + 6));
        (r < 250 && g < 250 && b < 250).Should().BeTrue(
            "the drop shadow must darken pixels offset toward the bottom-right of the box");

        // The opposite (top-left) corner, away from the offset, stays white.
        bmp.GetPixel((int)box.X - 6, (int)box.Y - 6).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "no shadow is cast on the side opposite the positive offset");
    }

    [TestMethod]
    public void Shadow_is_clipped_to_the_rounded_silhouette()
    {
        // With a large corner radius, the shadow's own corners are rounded too,
        // so the area immediately diagonally outside a box corner (where a
        // square shadow silhouette would reach) stays lighter than the centre of
        // the shadow edge.
        var box = new LayoutRect(60, 60, 80, 80);
        var radii = CornerRadii.Uniform(30, 30, 30, 30);
        var shadowColor = new CssColor(0, 0, 0, 255);

        var list = new PaintList();
        // No offset, no blur, no spread: the shadow silhouette equals the box
        // silhouette, so corner rounding is directly observable.
        list.Add(new DrawBoxShadow(box, radii, OffsetX: 0, OffsetY: 0, Blur: 0, Spread: 0, shadowColor, Inset: false));

        using var backend = new ImageSharpBackend(FontResolver.Default, webFonts: null);
        using var bmp = backend.Render(list, new LayoutSize(220, 220));

        // The extreme corner of the box's bounding square is outside the rounded
        // shadow, so it remains white.
        bmp.GetPixel((int)box.X + 1, (int)box.Y + 1).Should().Be(
            ((byte)255, (byte)255, (byte)255, (byte)255),
            "the shadow follows the rounded silhouette, leaving the square corner clear");

        // The middle of the shadow is fully painted black.
        bmp.GetPixel((int)(box.X + box.Width / 2), (int)(box.Y + box.Height / 2)).Should().Be(
            ((byte)0, (byte)0, (byte)0, (byte)255),
            "the body of a 0-blur shadow is solid");
    }
}
