using System.Globalization;
using System.Text.Json;

namespace Starling.Bench.Replay;

/// <summary>
/// The <c>replay</c> run-mode: drive a page through N synthetic frames, print a
/// scope-labeled report, and persist the result to
/// <c>bench/results/&lt;date&gt;/&lt;page&gt;-&lt;scope&gt;.json</c>.
/// </summary>
internal static class ReplayProgram
{
    public static int Run(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 2;
        }

        var page = args[0];
        var frames = 600;
        var warmup = 60;
        bool? incremental = null;
        var raster = true;
        var scale = 1.0f;

        try
        {
            for (var i = 1; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--frames": frames = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--warmup": warmup = int.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    case "--incremental": incremental = true; break;
                    case "--full": incremental = false; break;
                    case "--no-raster": raster = false; break;
                    case "--scale": scale = float.Parse(args[++i], CultureInfo.InvariantCulture); break;
                    default:
                        Console.Error.WriteLine($"unknown option: {args[i]}");
                        PrintUsage();
                        return 2;
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException)
        {
            Console.Error.WriteLine("invalid option value.");
            PrintUsage();
            return 2;
        }

        ReplayScenario scenario;
        try
        {
            scenario = ReplayScenarios.Create(page);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }

        var options = new ReplayOptions
        {
            FrameCount = frames,
            WarmupFrames = warmup,
            RunRaster = raster,
            Incremental = incremental ?? true,
            Scale = scale,
        };

        using var harness = new FrameReplayHarness(scenario, options);
        var result = harness.Run();

        PrintReport(result);
        var path = WriteJson(result);
        Console.WriteLine();
        Console.WriteLine($"wrote {path}");
        return 0;
    }

    private static void PrintUsage()
    {
        Console.Error.WriteLine("usage: replay <page> [--frames N] [--warmup N] [--incremental | --full] [--no-raster] [--scale S]");
        Console.Error.WriteLine($"pages: {string.Join(", ", ReplayScenarios.Names)}");
        Console.Error.WriteLine("default layout path is incremental; pass --full to A/B the full-rebuild path.");
    }

    private static void PrintReport(ReplayResult r)
    {
        var rasterLabel = r.RasterEnabled ? "GPU raster" : "no raster";
        Console.WriteLine($"Benchmark: {r.Page}   Scope: full-frame ({r.ScopeLabel} layout, {rasterLabel})");
        Console.WriteLine($"Frames: {r.FrameCount} ({r.WarmupFrames} warmup)   Frame delta: {r.FrameDeltaMs.ToString("F3", CultureInfo.InvariantCulture)} ms   Backend: {r.PaintBackend}   Scale: {r.Scale.ToString("0.0", CultureInfo.InvariantCulture)}x");
        Console.WriteLine();
        Console.WriteLine($"{"phase",-13}{"mean",9}{"p50",9}{"p95",9}{"p99",9}{"max",9}{"drop>16.7",11}{"drop>8.3",10}{"alloc/f",12}");
        foreach (var key in new[] { "frame", "style_anim", "layout", "display_list", "raster" })
        {
            if (!r.Phases.TryGetValue(key, out var p)) continue;
            Console.WriteLine(
                $"{key,-13}{Ms(p.MeanMs),9}{Ms(p.P50Ms),9}{Ms(p.P95Ms),9}{Ms(p.P99Ms),9}{Ms(p.MaxMs),9}"
                + $"{p.DroppedOver16_67ms,11}{p.DroppedOver8_33ms,10}{Bytes(p.MeanAllocBytes),12}");
        }
        Console.WriteLine();
        Console.WriteLine($"GC g0/g1/g2: {r.Gc.Gen0}/{r.Gc.Gen1}/{r.Gc.Gen2}");
        Console.WriteLine(
            $"Text: measures/frame {F1(r.TextMeasure.MeanMeasureWidthCalls)}  shapes/frame {F1(r.TextMeasure.MeanShapeCalls)}  "
            + $"shape cache hit-rate {r.TextMeasure.ShapeCacheHitRate.ToString("P1", CultureInfo.InvariantCulture)}");
        Console.WriteLine($"Nodes visited/frame: {r.TextMeasure.MeanNodesVisited.ToString("F0", CultureInfo.InvariantCulture)}");
    }

    private static string WriteJson(ReplayResult r)
    {
        var date = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var dir = Path.Combine(Fixtures.RepoRoot, "bench", "results", date);
        Directory.CreateDirectory(dir);
        var suffix = r.RasterEnabled ? "" : "-noraster";
        // A non-1x scale gets its own filename so the Retina run doesn't clobber
        // the logical-pixel run for the same page+scope.
        var scaleSuffix = r.Scale == 1.0f ? "" : $"-{r.Scale.ToString("0.0", CultureInfo.InvariantCulture)}x";
        var file = Path.Combine(dir, $"{r.Page}-{r.ScopeLabel}{suffix}{scaleSuffix}.json");
        File.WriteAllText(file, JsonSerializer.Serialize(r, ReplayJsonContext.Default.ReplayResult));
        return file;
    }

    private static string Ms(double v) => v.ToString("F3", CultureInfo.InvariantCulture);
    private static string F1(double v) => v.ToString("F1", CultureInfo.InvariantCulture);

    private static string Bytes(double v) => v switch
    {
        >= 1024 * 1024 => (v / (1024 * 1024)).ToString("F1", CultureInfo.InvariantCulture) + " MB",
        >= 1024 => (v / 1024).ToString("F1", CultureInfo.InvariantCulture) + " KB",
        _ => v.ToString("F0", CultureInfo.InvariantCulture) + " B",
    };
}
