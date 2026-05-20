using AwesomeAssertions;
using Starling.Css.Cascade;
using Starling.Html;
using Starling.Layout.Box;
using Starling.Layout.Compositor;
using Starling.Spec;

namespace Starling.Layout.Tests.StackingContexts;

/// <summary>
/// Integration-level tests: lay out a real document and walk it with
/// <see cref="LayerCandidates.EnumerateLayerCandidates"/> to confirm the
/// box-tree carries the expected <see cref="LayerHint"/> bits.
/// </summary>
[TestClass]
public sealed class LayerCandidatesTests
{
    private static BlockBox Layout(string html)
        => new LayoutEngine(new StyleEngine())
            .LayoutDocument(HtmlParser.Parse(html), new Size(800, 600));

    private static List<LayerCandidate> Candidates(string html)
        => LayerCandidates.EnumerateLayerCandidates(Layout(html)).ToList();

    /// <summary>Candidates that are not the implicit root element box.</summary>
    private static List<LayerCandidate> NonRootCandidates(string html)
        => Candidates(html).Where(c => !c.Hints.HasFlag(LayerHint.Root)).ToList();

    [TestMethod]
    public void Root_box_is_a_candidate_with_root_bit()
    {
        var candidates = Candidates("<body><div>plain</div></body>");
        candidates.Should().ContainSingle(c => c.Hints.HasFlag(LayerHint.Root));
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Relative_z_index_records_exactly_one_promoted_candidate_beyond_root()
    {
        var nonRoot = NonRootCandidates(
            """<body><div style="position:relative; z-index:1">x</div></body>""");

        nonRoot.Should().ContainSingle();
        nonRoot[0].Hints.Should().HaveFlag(LayerHint.Promoted);
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Will_change_transform_records_a_will_change_candidate()
    {
        var nonRoot = NonRootCandidates(
            """<body><div style="will-change:transform">x</div></body>""");

        nonRoot.Should().Contain(c => c.Hints.HasFlag(LayerHint.WillChange));
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-rendering", section: "5")]
    public void Nested_opacity_inside_transform_records_two_distinct_candidates()
    {
        var nonRoot = NonRootCandidates(
            """
            <body>
              <div id="outer" style="transform:rotate(15deg)">
                <div id="inner" style="opacity:0.5">x</div>
              </div>
            </body>
            """);

        nonRoot.Should().HaveCount(2);
        nonRoot.Should().ContainSingle(c => c.Hints.HasFlag(LayerHint.Transform3D)
            && !c.Hints.HasFlag(LayerHint.OpacityLessThanOne));
        nonRoot.Should().ContainSingle(c => c.Hints.HasFlag(LayerHint.OpacityLessThanOne)
            && !c.Hints.HasFlag(LayerHint.Transform3D));

        // The two candidates are distinct boxes.
        var transformBox = nonRoot.Single(c => c.Hints.HasFlag(LayerHint.Transform3D)).Box;
        var opacityBox = nonRoot.Single(c => c.Hints.HasFlag(LayerHint.OpacityLessThanOne)).Box;
        transformBox.Should().NotBeSameAs(opacityBox);
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Fixed_box_records_a_fixed_candidate()
    {
        NonRootCandidates("""<body><div style="position:fixed">x</div></body>""")
            .Should().Contain(c => c.Hints.HasFlag(LayerHint.Fixed));
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Sticky_box_records_a_sticky_candidate()
    {
        NonRootCandidates("""<body><div style="position:sticky">x</div></body>""")
            .Should().Contain(c => c.Hints.HasFlag(LayerHint.Sticky));
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Filter_box_records_a_filter_candidate()
    {
        NonRootCandidates("""<body><div style="filter:blur(2px)">x</div></body>""")
            .Should().Contain(c => c.Hints.HasFlag(LayerHint.Filter));
    }

    [TestMethod]
    [Spec("css-position-3", "https://www.w3.org/TR/css-position-3/#stacking-context", section: "9")]
    public void Isolation_box_records_an_isolation_candidate()
    {
        NonRootCandidates("""<body><div style="isolation:isolate">x</div></body>""")
            .Should().Contain(c => c.Hints.HasFlag(LayerHint.Isolation));
    }

    [TestMethod]
    [Spec("css-transforms-1", "https://www.w3.org/TR/css-transforms-1/#transform-rendering", section: "5")]
    public void Transform_box_records_a_transform_candidate()
    {
        NonRootCandidates("""<body><div style="transform:scale(2)">x</div></body>""")
            .Should().Contain(c => c.Hints.HasFlag(LayerHint.Transform3D));
    }

    [TestMethod]
    public void Plain_document_has_no_candidates_beyond_root()
    {
        NonRootCandidates("<body><div>plain</div><p>text</p></body>")
            .Should().BeEmpty();
    }
}
