using System.Text.Json.Serialization;

namespace Starling.Bench.Replay;

/// <summary>
/// A scope-labeled frame-replay result. Serialized to
/// <c>bench/results/&lt;date&gt;/&lt;page&gt;-&lt;scope&gt;.json</c> and read back by the
/// compare tool. <see cref="ScopeLabel"/> plus the per-phase keys in
/// <see cref="Phases"/> make the measurement scope unambiguous, so a number is
/// never reported without saying which phase it covers.
/// </summary>
public sealed record ReplayResult
{
    public required string SchemaVersion { get; init; }

    /// <summary>"incremental" or "full" — the layout path that produced this run.</summary>
    public required string ScopeLabel { get; init; }

    public required string Page { get; init; }
    public required int FrameCount { get; init; }
    public required int WarmupFrames { get; init; }
    public required double FrameDeltaMs { get; init; }
    public required string PaintBackend { get; init; }
    public required bool RasterEnabled { get; init; }

    /// <summary>Device pixel scale the frames were rasterized at (1.0 logical, 2.0 Retina).</summary>
    public float Scale { get; init; } = 1.0f;

    /// <summary>ISO-8601 capture time. Metadata only — read outside the measured loop.</summary>
    public required string CapturedUtc { get; init; }

    /// <summary>
    /// Per-phase distributions, keyed by scope:
    /// <c>frame</c> (whole), <c>style_anim</c>, <c>layout</c>, <c>display_list</c>, <c>raster</c>.
    /// </summary>
    public required Dictionary<string, PhaseStats> Phases { get; init; }

    public required GcStats Gc { get; init; }
    public required MeasureStats TextMeasure { get; init; }

    /// <summary>Layer-compositor counts (LTF-00), present only for a composite run.</summary>
    public CompositeStats? Composite { get; init; }
}

/// <summary>
/// Per-frame layer-compositor counts averaged over the measured frames: how many
/// layers the tree had, how many actually re-rastered (a backend Render call),
/// and how many re-blitted from cache. The win shows as RasteredPerFrame dropping
/// to the count of genuinely changed layers while the rest blit.
/// </summary>
public sealed record CompositeStats(
    double MeanLayersPerFrame,
    double MeanLayersRasteredPerFrame,
    double MeanLayersBlittedPerFrame);

/// <summary>Garbage-collection counts accumulated over the measured frames.</summary>
public sealed record GcStats(int Gen0, int Gen1, int Gen2);

/// <summary>Text-measure and node-walk counters, averaged per measured frame.</summary>
public sealed record MeasureStats(
    double MeanMeasureWidthCalls,
    double MeanShapeCalls,
    double MeanNodesVisited,
    double ShapeCacheHitRate);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ReplayResult))]
internal sealed partial class ReplayJsonContext : JsonSerializerContext;
