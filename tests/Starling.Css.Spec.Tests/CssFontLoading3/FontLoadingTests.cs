using AwesomeAssertions;
using Starling.Css.FontFace;
using Starling.Css.FontLoading;
using Starling.Css.Parser;

// The FontFace type in Starling.Css.FontLoading shadows the FontFace namespace
// in Starling.Css.FontFace, so we import it explicitly under its own name using
// a fully-qualified alias. C# resolves "FontFace" ambiguity in favour of the
// namespace, so we use "CssFontFace" as the alias for the loading type.
// See CSS Font Loading 3 §3 for the FontFace interface.
using CssFontFace = global::Starling.Css.FontLoading.FontFace;

namespace Starling.Css.Spec.Tests.CssFontLoading3;

/// <summary>
/// Conformance tests for
/// <see href="https://www.w3.org/TR/css-font-loading-3/">CSS Font Loading Level 3</see>.
/// </summary>
[TestClass]
[Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/")]
public sealed class FontLoadingTests
{
    // -------------------------------------------------------------------------
    // §3.1 — FontFaceLoadStatus / initial state
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontface-status"/>
    /// A newly-constructed FontFace starts in the
    /// <see cref="FontFaceLoadStatus.Unloaded"/> state.
    /// CSS Font Loading 3 §3.1.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontface-status")]
    [SpecFact]
    public void New_FontFace_status_is_Unloaded()
    {
        var face = new CssFontFace("Open Sans", "url(\"OpenSans.woff2\")");
        face.Status.Should().Be(FontFaceLoadStatus.Unloaded);
    }

    // -------------------------------------------------------------------------
    // §3.3 — FontFace.load()
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontface-load"/>
    /// Calling Load() on an unloaded face transitions status to
    /// <see cref="FontFaceLoadStatus.Loaded"/>.
    /// CSS Font Loading 3 §3.3.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontface-load")]
    [SpecFact]
    public void Load_transitions_Unloaded_to_Loaded()
    {
        var face = new CssFontFace("Roboto", "url(\"Roboto.woff2\")");
        face.Status.Should().Be(FontFaceLoadStatus.Unloaded);
        face.Load();
        face.Status.Should().Be(FontFaceLoadStatus.Loaded);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontface-load"/>
    /// Calling Load() on an already-loaded face is a no-op.
    /// CSS Font Loading 3 §3.3 step 1.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontface-load")]
    [SpecFact]
    public void Load_on_already_loaded_face_is_noop()
    {
        var face = new CssFontFace("Lato", "url(\"Lato.woff2\")");
        face.Load();
        face.Load(); // second call must not throw or regress status
        face.Status.Should().Be(FontFaceLoadStatus.Loaded);
    }

    // -------------------------------------------------------------------------
    // §3 — Descriptor properties
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#fontface-interface"/>
    /// Descriptor properties set via the constructor are readable.
    /// CSS Font Loading 3 §3.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#fontface-interface")]
    [SpecFact]
    public void Constructor_exposes_descriptor_properties()
    {
        var face = new CssFontFace(
            family: "My Font",
            source: "url(\"my-font.woff2\")",
            style: "italic",
            weight: "700",
            stretch: "condensed",
            unicodeRange: "U+0-FF");

        face.Family.Should().Be("My Font");
        face.Source.Should().Be("url(\"my-font.woff2\")");
        face.Style.Should().Be("italic");
        face.Weight.Should().Be("700");
        face.Stretch.Should().Be("condensed");
        face.UnicodeRange.Should().Be("U+0-FF");
    }

    // -------------------------------------------------------------------------
    // §3 — FromRule factory
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#fontface-interface"/>
    /// Building a FontFace from a parsed @font-face rule carries through the
    /// family name and style descriptors.
    /// CSS Font Loading 3 §3.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#fontface-interface")]
    [SpecFact]
    public void FromRule_copies_family_and_style_descriptors()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Open Sans";
                src: url("OpenSans-Bold-Italic.woff2") format("woff2");
                font-weight: bold;
                font-style: italic;
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;

        var face = CssFontFace.FromRule(rule);

        face.Family.Should().Be("Open Sans");
        face.Style.Should().Be("italic");
        face.Weight.Should().Be("bold");
        face.Status.Should().Be(FontFaceLoadStatus.Unloaded);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#fontface-interface"/>
    /// Building a FontFace from a parsed rule carries through the src descriptor
    /// as a serialised string.
    /// CSS Font Loading 3 §3.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#fontface-interface")]
    [SpecFact]
    public void FromRule_serialises_url_source()
    {
        var sheet = CssParser.ParseStyleSheet("""
            @font-face {
                font-family: "Inter";
                src: url("Inter.woff2") format("woff2");
            }
            """);
        var rule = FontFaceParser.ParseAll(sheet).Should().ContainSingle().Subject;

        var face = CssFontFace.FromRule(rule);

        face.Source.Should().Contain("Inter.woff2");
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#fontface-interface"/>
    /// A @font-face rule with no unicode-range descriptor produces a FontFace
    /// with the full-range default.
    /// CSS Font Loading 3 §3.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#fontface-interface")]
    [SpecFact]
    public void FromRule_without_unicode_range_defaults_to_full_range()
    {
        var rule = new FontFaceRule(
            "Latin Only",
            [new UrlFontSource("latin.woff2", null)],
            Bold: false,
            Italic: false,
            UnicodeRange: null);

        var face = CssFontFace.FromRule(rule);

        face.UnicodeRange.Should().Be("U+0-10FFFF");
    }

    // -------------------------------------------------------------------------
    // §4 — FontFaceSet Add / Has / Count / Delete
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-add"/>
    /// FontFaceSet.Add inserts a face; Has and Count reflect the addition.
    /// CSS Font Loading 3 §4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-add")]
    [SpecFact]
    public void FontFaceSet_Add_Has_Count()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Roboto", "url(\"Roboto.woff2\")");

        set.Count.Should().Be(0);
        set.Has(face).Should().BeFalse();

        set.Add(face);

        set.Count.Should().Be(1);
        set.Has(face).Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-add"/>
    /// Adding the same face twice does not increase the count.
    /// CSS Font Loading 3 §4 — set semantics.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-add")]
    [SpecFact]
    public void FontFaceSet_Add_duplicate_is_noop()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Roboto", "url(\"Roboto.woff2\")");
        set.Add(face);
        set.Add(face);
        set.Count.Should().Be(1);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-delete"/>
    /// FontFaceSet.Delete removes the face and returns true; a second delete
    /// returns false.
    /// CSS Font Loading 3 §4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-delete")]
    [SpecFact]
    public void FontFaceSet_Delete_removes_face()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Merriweather", "url(\"Merriweather.woff2\")");
        set.Add(face);
        set.Delete(face).Should().BeTrue();
        set.Has(face).Should().BeFalse();
        set.Delete(face).Should().BeFalse(); // already absent
    }

    // -------------------------------------------------------------------------
    // §4.2 — FontFaceSetLoadStatus
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status"/>
    /// An empty FontFaceSet has status Loaded.
    /// CSS Font Loading 3 §4.2.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status")]
    [SpecFact]
    public void FontFaceSet_empty_status_is_Loaded()
    {
        var set = new FontFaceSet();
        set.Status.Should().Be(FontFaceSetLoadStatus.Loaded);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status"/>
    /// A set whose only member is Loaded reports Loaded.
    /// CSS Font Loading 3 §4.2.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status")]
    [SpecFact]
    public void FontFaceSet_status_Loaded_when_all_members_loaded()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Nunito", "url(\"Nunito.woff2\")");
        face.Load();
        set.Add(face);
        set.Status.Should().Be(FontFaceSetLoadStatus.Loaded);
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status"/>
    /// A set with a member in the Unloaded state still reports Loaded because
    /// the set status is Loading only when a face is actively mid-load.
    /// CSS Font Loading 3 §4.2.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#dom-fontfaceset-status")]
    [SpecFact]
    public void FontFaceSet_status_Loaded_when_member_is_Unloaded()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Source Sans", "url(\"SourceSans.woff2\")");
        // face is Unloaded — Load() has not been called
        set.Add(face);
        set.Status.Should().Be(FontFaceSetLoadStatus.Loaded);
    }

    // -------------------------------------------------------------------------
    // §4.4 — FontFaceSet.Check()
    // -------------------------------------------------------------------------

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#font-face-set-check"/>
    /// FontFaceSet.Check returns true for a loaded face whose family appears in
    /// the font shorthand string.
    /// CSS Font Loading 3 §4.4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#font-face-set-check")]
    [SpecFact]
    public void Check_returns_true_for_present_loaded_family()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Open Sans", "url(\"OpenSans.woff2\")");
        face.Load();
        set.Add(face);

        set.Check("16px Open Sans").Should().BeTrue();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#font-face-set-check"/>
    /// FontFaceSet.Check returns false when no loaded face matches the family.
    /// CSS Font Loading 3 §4.4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#font-face-set-check")]
    [SpecFact]
    public void Check_returns_false_for_absent_family()
    {
        var set = new FontFaceSet();
        set.Check("16px Papyrus").Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#font-face-set-check"/>
    /// FontFaceSet.Check returns false for a face that is present but not loaded.
    /// CSS Font Loading 3 §4.4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#font-face-set-check")]
    [SpecFact]
    public void Check_returns_false_for_unloaded_family()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Inter", "url(\"Inter.woff2\")");
        set.Add(face); // not loaded
        set.Check("12px Inter").Should().BeFalse();
    }

    /// <summary>Spec: <see href="https://www.w3.org/TR/css-font-loading-3/#font-face-set-check"/>
    /// FontFaceSet.Check accepts a quoted family name in the font shorthand string.
    /// CSS Font Loading 3 §4.4.
    /// </summary>
    [Spec("css-font-loading-3", "https://www.w3.org/TR/css-font-loading-3/#font-face-set-check")]
    [SpecFact]
    public void Check_handles_quoted_family_name()
    {
        var set = new FontFaceSet();
        var face = new CssFontFace("Open Sans", "url(\"OpenSans.woff2\")");
        face.Load();
        set.Add(face);

        set.Check("italic 16px 'Open Sans'").Should().BeTrue();
    }
}
