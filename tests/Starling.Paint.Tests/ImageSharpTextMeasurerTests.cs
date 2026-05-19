using FluentAssertions;
using SixLabors.Fonts;
using Starling.Layout.Text;
namespace Starling.Paint.Tests;

/// <summary>
/// Locks down the width-measurement regression: when ImageSharp's measurer
/// only knew about bundled OpenSans, CSS like
/// <c>font-family: "Helvetica Neue", Helvetica, Arial, sans-serif</c>
/// measured at OpenSans advances. That fed back into layout's max-width
/// shrink-to-fit pass and changed line-break points, causing visible width
/// drift on pages like justinjackson.ca/words.html.
/// </summary>
[TestClass]
public sealed class ImageSharpTextMeasurerTests
{
    private const string Probe = "The quick brown fox jumps over the lazy dog";
    private const double FontSize = 16d;

    /// <summary>
    /// Bold text must measure wider than Regular at the same font size for any
    /// system family that ships a real Bold face. If the measurer regressed
    /// to bundled-only OpenSans (no Bold face), Bold and Regular collapse to
    /// the same width.
    /// </summary>
    [TestMethod]
    public void Bold_advance_differs_from_regular_advance()
    {
        if (!(HostHasInstalledFontStyle(FontStyle.Bold))) { Assert.Inconclusive("no system Bold face on this host; can't differentiate Bold vs Regular"); return; }

        using var measurer = new ImageSharpTextMeasurer();
        var regular = measurer.MeasureWidth(Probe, FontSize,
            new FontSpec(["sans-serif"], bold: false, italic: false));
        var bold = measurer.MeasureWidth(Probe, FontSize,
            new FontSpec(["sans-serif"], bold: true, italic: false));

        regular.Should().BeGreaterThan(0);
        bold.Should().NotBeApproximately(regular, 0.5,
            "Bold should measure visibly different from Regular; identical advances indicate both resolved to the same face");
    }

    /// <summary>
    /// The system Helvetica/Arial/Segoe UI families have noticeably narrower
    /// glyphs than OpenSans, so resolving <c>sans-serif</c> against the
    /// system collection produces a different advance than the bundled
    /// OpenSans fallback. This is the regression's most direct fingerprint —
    /// if the measurer drops back to OpenSans-only the two measurements
    /// become identical.
    /// </summary>
    [TestMethod]
    public void System_sans_serif_measures_differently_than_bundled_open_sans()
    {
        if (!(HostHasInstalledSansSerif())) { Assert.Inconclusive("no system sans-serif on this host; nothing to compare against bundled OpenSans"); return; }

        using var measurer = new ImageSharpTextMeasurer();

        // "Open Sans" is the embedded family name; "sans-serif" is the CSS
        // generic that should expand to a system family on real hosts.
        var openSansSpec = new FontSpec(["Open Sans"], bold: false, italic: false);
        var sansSerifSpec = new FontSpec(["sans-serif"], bold: false, italic: false);

        var openSans = measurer.MeasureWidth(Probe, FontSize, openSansSpec);
        var sansSerif = measurer.MeasureWidth(Probe, FontSize, sansSerifSpec);

        sansSerif.Should().NotBeApproximately(openSans, 0.5,
            "CSS sans-serif must resolve to a system family on hosts that have one — the regression collapsed it back to bundled OpenSans");
    }

    [TestMethod]
    public void Empty_string_measures_zero()
    {
        using var measurer = new ImageSharpTextMeasurer();
        measurer.MeasureWidth("", FontSize, FontSpec.Default).Should().Be(0d);
    }

    [TestMethod]
    public void Line_height_is_positive_for_supported_specs()
    {
        using var measurer = new ImageSharpTextMeasurer();
        measurer.NormalLineHeight(FontSize, FontSpec.Default).Should().BeGreaterThan(0);
    }

    private static bool HostHasInstalledSansSerif()
    {
        try
        {
            var sys = SystemFonts.Collection;
            return sys.TryGet("Helvetica Neue", out _)
                || sys.TryGet("Helvetica", out _)
                || sys.TryGet("Arial", out _)
                || sys.TryGet("Segoe UI", out _)
                || sys.TryGet("DejaVu Sans", out _)
                || sys.TryGet("Liberation Sans", out _)
                || sys.TryGet("Inter", out _);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }
    }

    private static bool HostHasInstalledFontStyle(FontStyle style)
    {
        try
        {
            foreach (var family in SystemFonts.Collection.Families)
            {
                var styles = family.GetAvailableStyles().Span;
                for (var i = 0; i < styles.Length; i++)
                    if (styles[i] == style) return true;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // System fonts unavailable on this host.
        }
        return false;
    }
}

