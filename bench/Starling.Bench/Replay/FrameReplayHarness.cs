using System.Diagnostics;
using System.Globalization;
using Starling.Layout;
using Starling.Layout.Box;
using Starling.Layout.Incremental;
using Starling.Paint;
using Starling.Paint.Backend;
using Starling.Paint.DisplayList;

namespace Starling.Bench.Replay;

public sealed record ReplayOptions
{
    public int FrameCount { get; init; } = 600;
    public int WarmupFrames { get; init; } = 60;
    public double FrameDeltaMs { get; init; } = 1000.0 / 60.0;
    public bool Incremental { get; init; } = true;
    public bool RunRaster { get; init; } = true;
    // Device pixel scale handed to the backend each frame. 1.0 = logical pixels;
    // 2.0 is the Retina scale the GUI runs at, which quadruples the raster
    // surface (and its GPU readback) — a real per-frame cost worth measuring.
    public float Scale { get; init; } = 1.0f;
}

/// <summary>
/// Drives a <see cref="ReplayScenario"/> through N deterministic synthetic frames
/// and reports a per-phase, scope-labeled distribution. There is no GUI and no
/// real-clock <c>requestAnimationFrame</c>: the clock is a synthetic counter the
/// harness owns, and the per-frame DOM mutation stands in for the page's script.
/// Each frame mirrors the real per-frame phase order
/// (<c>style_anim → layout → display_list → raster</c>) but with a counting text
/// measurer the harness injects, so text-measure cost is visible.
/// </summary>
public sealed class FrameReplayHarness : IDisposable
{
    private readonly ReplayScenario _scenario;
    private readonly ReplayOptions _options;
    private readonly CountingTextMeasurer _measurer;
    private readonly ImageSharpBackend _backend;
    private readonly LayoutSession? _session;

    public FrameReplayHarness(ReplayScenario scenario, ReplayOptions options)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(options);
        _scenario = scenario;
        _options = options;
        _measurer = new CountingTextMeasurer(new ImageSharpTextMeasurer(FontResolver.Default));
        // WebGPU backend, constructed once and reused across frames: this is the
        // GPU paint path Starling ships, and reusing one target across frames
        // keeps per-frame backend-init noise out of the raster numbers. The host
        // must expose a working WebGPU adapter or Render throws.
        _backend = new ImageSharpBackend(FontResolver.Default, webFonts: null, diagnostics: null, useWebGpu: true);
        _session = options.Incremental ? new LayoutSession(scenario.Style) : null;
    }

    private struct PhaseTimes
    {
        public long StyleAnimTicks, StyleAnimAlloc;
        public long LayoutTicks, LayoutAlloc;
        public long DlTicks, DlAlloc;
        public long RasterTicks, RasterAlloc;
    }

    public ReplayResult Run()
    {
        var n = _options.FrameCount;
        long[] saT = new long[n], saA = new long[n];
        long[] loT = new long[n], loA = new long[n];
        long[] dlT = new long[n], dlA = new long[n];
        long[] raT = new long[n], raA = new long[n];
        long[] frT = new long[n], frA = new long[n];

        double mwSum = 0, shapeSum = 0, nodesSum = 0, hits = 0, total = 0;

        var sw = new Stopwatch();
        var clock = 0.0;
        var pt = default(PhaseTimes);

        // Warmup: identical work, all measurements discarded, caches left warm.
        for (var w = 0; w < _options.WarmupFrames; w++)
        {
            clock += _options.FrameDeltaMs;
            RunFrame(w, clock, sw, ref pt);
            _measurer.TakeFrameSnapshot();
        }

        var g0 = GC.CollectionCount(0);
        var g1 = GC.CollectionCount(1);
        var g2 = GC.CollectionCount(2);

        for (var f = 0; f < n; f++)
        {
            clock += _options.FrameDeltaMs;
            var root = RunFrame(_options.WarmupFrames + f, clock, sw, ref pt);

            saT[f] = pt.StyleAnimTicks; saA[f] = pt.StyleAnimAlloc;
            loT[f] = pt.LayoutTicks; loA[f] = pt.LayoutAlloc;
            dlT[f] = pt.DlTicks; dlA[f] = pt.DlAlloc;
            raT[f] = pt.RasterTicks; raA[f] = pt.RasterAlloc;
            frT[f] = pt.StyleAnimTicks + pt.LayoutTicks + pt.DlTicks + pt.RasterTicks;
            frA[f] = pt.StyleAnimAlloc + pt.LayoutAlloc + pt.DlAlloc + pt.RasterAlloc;

            var snap = _measurer.TakeFrameSnapshot();
            mwSum += snap.MeasureWidthCalls;
            shapeSum += snap.ShapeCalls;
            hits += snap.ShapeHits;
            total += snap.ShapeHits + snap.ShapeMisses;
            nodesSum += CountBoxes(root);
        }

        var gc = new GcStats(
            GC.CollectionCount(0) - g0,
            GC.CollectionCount(1) - g1,
            GC.CollectionCount(2) - g2);

        var phases = new Dictionary<string, PhaseStats>
        {
            ["frame"] = PhaseStats.From(frT, frA),
            ["style_anim"] = PhaseStats.From(saT, saA),
            ["layout"] = PhaseStats.From(loT, loA),
            ["display_list"] = PhaseStats.From(dlT, dlA),
        };
        if (_options.RunRaster)
            phases["raster"] = PhaseStats.From(raT, raA);

        var measure = new MeasureStats(
            MeanMeasureWidthCalls: mwSum / n,
            MeanShapeCalls: shapeSum / n,
            MeanNodesVisited: nodesSum / n,
            ShapeCacheHitRate: total > 0 ? hits / total : 0);

        return new ReplayResult
        {
            SchemaVersion = "1",
            ScopeLabel = _options.Incremental ? "incremental" : "full",
            Page = _scenario.PageName,
            FrameCount = n,
            WarmupFrames = _options.WarmupFrames,
            FrameDeltaMs = _options.FrameDeltaMs,
            PaintBackend = _options.RunRaster ? "imagesharp-webgpu" : "none",
            RasterEnabled = _options.RunRaster,
            Scale = _options.Scale,
            CapturedUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
            Phases = phases,
            Gc = gc,
            TextMeasure = measure,
        };
    }

    // One frame. Each phase is bracketed by an allocation read (outside the
    // Stopwatch window) so timing and per-phase allocations are both captured.
    private BlockBox RunFrame(int frameIndex, double nowMs, Stopwatch sw, ref PhaseTimes pt)
    {
        _scenario.MutateForFrame(frameIndex);

        var a0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        _scenario.Style.AnimationEngine.Tick(nowMs);
        _scenario.Style.TransitionEngine.Tick(nowMs);
        sw.Stop();
        pt.StyleAnimTicks = sw.ElapsedTicks;
        pt.StyleAnimAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;

        BlockBox root;
        a0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        if (_session is not null)
            root = _session.Layout(_scenario.Document, _scenario.Viewport, _measurer, nowMs);
        else
            root = new LayoutEngine(_scenario.Style, _measurer)
                .LayoutDocument(_scenario.Document, _scenario.Viewport, nowMs);
        sw.Stop();
        pt.LayoutTicks = sw.ElapsedTicks;
        pt.LayoutAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;

        a0 = GC.GetAllocatedBytesForCurrentThread();
        sw.Restart();
        var list = new DisplayListBuilder().Build(root);
        sw.Stop();
        pt.DlTicks = sw.ElapsedTicks;
        pt.DlAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;

        if (_options.RunRaster)
        {
            a0 = GC.GetAllocatedBytesForCurrentThread();
            sw.Restart();
            using var bmp = _backend.Render(list, _scenario.Viewport, _options.Scale);
            sw.Stop();
            pt.RasterTicks = sw.ElapsedTicks;
            pt.RasterAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        }
        else
        {
            pt.RasterTicks = 0;
            pt.RasterAlloc = 0;
        }

        return root;
    }

    private static int CountBoxes(Box box)
    {
        var count = 1;
        var children = box.Children;
        for (var i = 0; i < children.Count; i++)
            count += CountBoxes(children[i]);
        return count;
    }

    public void Dispose()
    {
        _measurer.Dispose();
        _backend.Dispose();
    }
}
