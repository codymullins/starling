# Starling benchmarks

This folder holds the performance suite. There are two kinds of measurement, and
one generated overview.

- **Microbenchmarks** — [BenchmarkDotNet](https://github.com/dotnet/BenchmarkDotNet)
  classes in `Starling.Bench/`. Each one times a single layer (parse, cascade,
  layout, paint, raster). Output lands in `BenchmarkDotNet.Artifacts/`.
- **Frame replay** — drives a page through many synthetic frames and splits the
  cost by phase (style, layout, display list, raster). Output is dated JSON under
  `bench/results/<date>/`.
- **`benchmarks.md`** — a generated dashboard that merges both. Do not edit it by
  hand. See [Generate the dashboard](#generate-the-dashboard).

The split follows the pattern from
[dotnet/performance](https://github.com/dotnet/performance): microbenchmarks for
a single hot path, plus a comparison tool that diffs JSON results.

## Before you run

- Use `-c Release`. BenchmarkDotNet refuses to run in Debug.
- The paint and raster benchmarks need a Six Labors license key. Put
  `sixlabors.lic` at the repo root. `Directory.Build.props` finds it through the
  `SixLaborsLicenseFile` property.

## Run a microbenchmark

Run one benchmark class with a filter glob (matches `namespace.type.method`):

```bash
dotnet run -c Release --project bench/Starling.Bench -- --filter '*RasterBench*'
```

Run everything by passing `*`. List the available classes without running them
with `--list flat`.

The classes today:

| Class | Measures |
|---|---|
| `UrlBench` | URL parsing |
| `HtmlBench` | HTML parsing |
| `CssBench` / `StyleBench` | parse and cascade |
| `StyleInvalidationBench` | re-cascade after a DOM change |
| `LayoutBench` / `IncrementalLayoutBench` | box tree and incremental relayout |
| `PaintBench` | display-list build |
| `RasterBench` | draw the paint list to pixels (pure-CPU backend) |
| `SsimBench` | render similarity score |
| `H1ResponseBench` | HTTP/1 response parsing |
| `EndToEndBench` | parse through raster for a whole page |

### Where results land

BenchmarkDotNet writes to `BenchmarkDotNet.Artifacts/` next to wherever you ran
the command, which is the repo root in the example above. Each run leaves:

- `results/<Class>-report-github.md` — the readable table
- `results/<Class>-report.csv` — for diffing
- `results/<Class>-report.html`
- a timestamped `*.log` one level up

These files use a fixed name with no date. The next run of the same class
overwrites them. Copy a report aside before a comparison run.

## Frame replay

Drive one page through synthetic frames and print a per-phase report:

```bash
dotnet run -c Release --project bench/Starling.Bench -- replay flex-status
```

Pages: `flex-status`, `list`, `nginx`.

```
replay <page> [--frames N] [--warmup N] [--incremental | --full] [--no-raster]
```

The layout path defaults to the `STARLING_INCREMENTAL_LAYOUT` environment
variable (`=1` for incremental). Pass `--incremental` or `--full` to force one.

Each run saves a result to `bench/results/<date>/<page>-<scope>.json`. The scope
is `incremental` or `full`. Unlike the microbenchmark reports, these are dated,
so they do not overwrite each other.

## Compare two runs

The `compare` mode diffs two replay JSON files and flags any phase that got
slower past a threshold. It exits non-zero on a regression, so a continuous
integration job can gate on it.

```bash
dotnet run -c Release --project bench/Starling.Bench -- \
  compare <baseline.json> <candidate.json> [--threshold 0.10]
```

The default threshold is `0.10` (ten percent). It refuses to compare a different
page or a different scope, so an incremental run is never measured against a full
one by accident.

## Generate the dashboard

The `report` mode gathers the latest frame-replay JSON and the BenchmarkDotNet
markdown, then writes `bench/benchmarks.md`.

```bash
dotnet run -c Release --project bench/Starling.Bench -- report
```

```
report [--results <dir>] [--bdn <dir>] [--date yyyy-MM-dd] [--out <path>]
```

It picks the newest date folder under `bench/results/` for the replay half. To
refresh the dashboard with a fresh microbenchmark, run that benchmark first so
its artifact is current, then run `report`.

## Before and after a change

To measure a change that has no runtime switch (for example a dependency swap),
run the suite once on each version.

1. Run the benchmark on the baseline. Copy the report aside, because the next run
   overwrites it.
   ```bash
   dotnet run -c Release --project bench/Starling.Bench -- --filter '*RasterBench*'
   cp BenchmarkDotNet.Artifacts/results/Starling.Bench.RasterBench-report-github.md /tmp/before.md
   ```
2. Apply the change, then run the same benchmark again.
3. Compare the two reports. For frame replay, use the `compare` mode above on the
   two dated JSON files instead.
