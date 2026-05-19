using AwesomeAssertions;

namespace Starling.Engine.Tests;

/// <summary>
/// Covers the <see cref="Srcset"/> source selector for the HTML
/// <c>srcset</c> + <c>sizes</c> attributes (HTML Living Standard,
/// "Images" → "Selecting an image source"). The regression these tests
/// pin: an <c>&lt;img sizes="400px" srcset="…400w,…1198w"&gt;</c> on a
/// 1280-px viewport must end up with an effective intrinsic width of
/// 400 CSS px (the source-size), not the source bitmap's pixel width.
/// </summary>
[TestClass]
public class SrcsetTests
{
    [TestMethod]
    public void Empty_srcset_returns_fallback_src_and_no_correction()
    {
        var (url, cw, ch) = Srcset.Select(null, null, "image.png", viewportWidthCssPx: 1280, fontSizeCssPx: 16);
        url.Should().Be("image.png");
        cw.Should().Be(0);
        ch.Should().Be(0);
    }

    [TestMethod]
    public void Parses_w_descriptor_candidates()
    {
        var list = Srcset.Parse("a.png 400w, b.png 800w, c.png 1200w");
        list.Should().HaveCount(3);
        list[0].Url.Should().Be("a.png"); list[0].Width.Should().Be(400);
        list[1].Url.Should().Be("b.png"); list[1].Width.Should().Be(800);
        list[2].Url.Should().Be("c.png"); list[2].Width.Should().Be(1200);
    }

    [TestMethod]
    public void Parses_x_descriptor_candidates()
    {
        var list = Srcset.Parse("a.png, b.png 2x, c.png 3x");
        list.Should().HaveCount(3);
        list[0].Density.Should().Be(1.0);
        list[1].Density.Should().Be(2.0);
        list[2].Density.Should().Be(3.0);
    }

    [TestMethod]
    public void Sizes_bare_px_is_the_source_size()
    {
        Srcset.ParseSourceSize("400px", viewportWidthCssPx: 1280, fontSizeCssPx: 16)
            .Should().Be(400);
    }

    [TestMethod]
    public void Sizes_evaluates_min_width_media_query()
    {
        // Picks 50vw (= 640px on 1280-wide viewport) once min-width:600px matches.
        Srcset.ParseSourceSize("(min-width: 600px) 50vw, 100vw",
            viewportWidthCssPx: 1280, fontSizeCssPx: 16)
            .Should().Be(640);
    }

    [TestMethod]
    public void Sizes_falls_through_to_bare_default_when_no_query_matches()
    {
        Srcset.ParseSourceSize("(min-width: 2000px) 50vw, 100vw",
            viewportWidthCssPx: 1280, fontSizeCssPx: 16)
            .Should().Be(1280);
    }

    [TestMethod]
    public void Density_corrected_width_matches_sizes_when_w_candidate_selected()
    {
        // The exact case from docs.htmlcsstoimage.com: sizes="400px" with
        // multiple w-described candidates. The engine must report a 400-px
        // intrinsic width, regardless of which candidate URL it fetches.
        var srcset = "u/400 400w, u/666 666w, u/932 932w, u/1198 1198w";
        var (url, cw, _) = Srcset.Select(srcset, "400px", "fallback.png",
            viewportWidthCssPx: 1280, fontSizeCssPx: 16);
        url.Should().Be("u/400");
        cw.Should().Be(400);
    }

    [TestMethod]
    public void Smallest_w_meeting_source_size_is_selected()
    {
        // With sourceSize=500, candidates 400/666/932/1198, the smallest >=500
        // is 666; that's the URL we pick (fewest pixels at acceptable density).
        var (url, cw, _) = Srcset.Select(
            "u/400 400w, u/666 666w, u/932 932w",
            "500px", "fallback.png",
            viewportWidthCssPx: 1280, fontSizeCssPx: 16);
        url.Should().Be("u/666");
        cw.Should().Be(500);
    }

    [TestMethod]
    public void No_sizes_picks_largest_w_candidate()
    {
        var (url, cw, _) = Srcset.Select("u/400 400w, u/800 800w", sizes: null,
            fallbackSrc: "fb", viewportWidthCssPx: 1280, fontSizeCssPx: 16);
        url.Should().Be("u/800");
        // No sizes → no density correction (use source pixel dims at layout time).
        cw.Should().Be(0);
    }

    [TestMethod]
    public void No_sizes_with_x_descriptors_picks_highest_density()
    {
        var (url, cw, _) = Srcset.Select("a.png, b.png 2x, c.png 3x", sizes: null,
            fallbackSrc: "fb", viewportWidthCssPx: 1280, fontSizeCssPx: 16);
        url.Should().Be("c.png");
        cw.Should().Be(0);
    }

    [TestMethod]
    public void Urls_containing_commas_are_handled()
    {
        // Cloudinary URLs include unescaped commas inside transformation
        // segments; we split on ", " (comma + whitespace) to keep them intact.
        var list = Srcset.Parse(
            "https://res.cloudinary.com/hcti/image/fetch/c_limit,f_auto,w_400/foo.png 400w, " +
            "https://res.cloudinary.com/hcti/image/fetch/c_limit,f_auto,w_800/foo.png 800w");
        list.Should().HaveCount(2);
        list[0].Url.Should().Contain("c_limit,f_auto,w_400");
        list[0].Width.Should().Be(400);
        list[1].Url.Should().Contain("c_limit,f_auto,w_800");
        list[1].Width.Should().Be(800);
    }

    [TestMethod]
    public void Em_units_in_sizes_resolve_against_font_size()
    {
        Srcset.ParseSourceSize("20em", viewportWidthCssPx: 1280, fontSizeCssPx: 16)
            .Should().Be(320);
    }

    [TestMethod]
    public void Vw_units_in_sizes_resolve_against_viewport()
    {
        Srcset.ParseSourceSize("25vw", viewportWidthCssPx: 1200, fontSizeCssPx: 16)
            .Should().Be(300);
    }
}
