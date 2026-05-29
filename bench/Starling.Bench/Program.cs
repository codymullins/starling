using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;
using Starling.Bench.Replay;

namespace Starling.Bench;

internal static class Program
{
    public static int Main(string[] args)
    {
        // Custom run-modes dispatched before the BenchmarkDotNet switcher: the
        // frame-replay harness and its baseline-compare tool.
        if (args.Length > 0 && args[0] == "replay")
            return ReplayProgram.Run(args[1..]);
        if (args.Length > 0 && args[0] == "compare")
            return ReplayCompare.Run(args[1..]);
        if (args.Length > 0 && args[0] == "report")
            return ReplayReport.Run(args[1..]);
        if (args.Length > 0 && args[0] == "animtrace")
            return AnimationTraceProgram.Run(args[1..]);

        // Add tail-latency columns so the BenchmarkDotNet tables carry p95/p90,
        // not just mean/median — the percentiles that predict frame budget misses.
        var config = DefaultConfig.Instance
            .AddColumn(StatisticColumn.P95)
            .AddColumn(StatisticColumn.P90);
        BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return 0;
    }
}
