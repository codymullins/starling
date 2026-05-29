using BenchmarkDotNet.Attributes;
using Starling.Css.Cascade;
using Starling.Css.Parser;
using Starling.Dom;
using Starling.Html;
using Starling.Layout;
using Starling.Layout.Incremental;
using Starling.Layout.Text;
using Starling.Paint.DisplayList;

namespace Starling.Bench;

// Per-frame cost of driving a CSS @keyframes timeline — the work the live frame
// loop repeats every animation frame (Engine.PrepareAnimationFrame): advance
// the animation clock, re-sample the animated properties through the cascade,
// relayout whatever the samples dirtied, and rebuild the display list.
//
// Two property shapes, because they take very different paths:
//   * [Kind] = Transform — transform + opacity. Composite-only: the animated
//     value never affects box geometry, so incremental layout leaves the tree
//     alone and only the paint (display-list) stage re-runs.
//   * [Kind] = Layout — width + margin. Layout-affecting: each frame marks the
//     animated elements dirty and relays them out, so the box-tree pass re-runs.
//
// [Boxes] scales the number of simultaneously-animating elements. Each frame
// advances the clock by one 60fps step; the animation iterates forever, so the
// playback head keeps sweeping the keyframes and never settles.
//
// Frame measures the whole pipeline (tick + layout + paint). SampleOnly isolates
// the interpolation: tick the clock, then read every animated property's sampled
// value, with no layout or paint — so the gap between the two attributes the
// per-frame cost to interpolation vs. the layout/paint it triggers.
[MemoryDiagnoser]
public class AnimationBench
{
    public enum AnimKind { Transform, Layout }

    [Params(AnimKind.Transform, AnimKind.Layout)]
    public AnimKind Kind;

    [Params(10, 100)]
    public int Boxes;

    private const double FrameMs = 1000.0 / 60.0;
    private static readonly Size Viewport = new(1024, 768);
    private readonly ITextMeasurer _measurer = DefaultTextMeasurer.Instance;

    private Document _doc = null!;
    private StyleEngine _style = null!;
    private LayoutSession _session = null!;
    private double _clock;

    [GlobalSetup]
    public void Setup()
    {
        var css = Kind == AnimKind.Transform ? Fixtures.TransformAnimCss : Fixtures.LayoutAnimCss;
        _doc = HtmlParser.Parse(Fixtures.AnimatedBoxesHtml(Boxes));
        _doc.RecordLayoutMutations = true;
        _style = new StyleEngine();
        _style.AddStyleSheet(CssParser.ParseStyleSheet(css));
        _session = new LayoutSession(_style) { VerifyAgainstFullRebuild = false };
        // Warm: full build + cascade. The cascade's AnimationCompositor.Compose
        // starts the animations (OnAnimationsCascaded), so later frames just tick
        // and re-sample rather than re-discovering the animation declarations.
        _session.Layout(_doc, Viewport, _measurer, nowMs: 0);
        _clock = 0;
    }

    [Benchmark(Baseline = true)]
    public int Frame()
    {
        _clock += FrameMs;
        _style.AnimationEngine.Tick(_clock);
        var root = _session.Layout(_doc, Viewport, _measurer, _clock);
        return new DisplayListBuilder().Build(root).Items.Count;
    }

    [Benchmark]
    public int SampleOnly()
    {
        _clock += FrameMs;
        var engine = _style.AnimationEngine;
        engine.Tick(_clock);
        var samples = 0;
        foreach (var el in engine.ActiveElements)
            foreach (var prop in engine.ActiveProperties(el))
                if (engine.GetEffective(el, prop) is not null)
                    samples++;
        return samples;
    }
}
