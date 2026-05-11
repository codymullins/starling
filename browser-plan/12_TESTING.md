# 12 — Testing

## Scope

**In:** Unit tests, integration tests, conformance suites (WPT, Test262, html5lib), golden-image rendering tests, fuzzing, benchmarks, CI integration.
**Out:** Manual QA scripts, accessibility audits (M9+).

## Tooling

| Concern | Tool | Version | Notes |
|---|---|---|---|
| Unit/integration | xUnit v3 | 1.0.x | Faster + better isolation than v2 |
| Assertions | FluentAssertions | 6.12.x | Optional; raw xUnit also fine |
| Coverage | `coverlet.collector` | 6.x | XPlat |
| Benchmarks | BenchmarkDotNet | 0.14.x | RyuJIT + Tiered |
| Fuzzing | SharpFuzz | 2.x | LibFuzzer-based via .NET Native |
| Golden images | xUnit + ImageSharp | n/a | PNG hash + SSIM diff |
| Property tests | FsCheck.Xunit | 3.x | Tokenizer + parser only |

## Test project layout

Every `src/Tessera.X` has a `tests/Tessera.X.Tests` mirror. Plus:

```
tests/
├── Tessera.E2E/                   # cross-subsystem flows (URL -> rendered PNG)
├── Tessera.Wpt/                   # Web Platform Tests harness
├── Tessera.Test262/               # ECMA Test262 harness
├── Tessera.Html5lib/              # html5lib-tests harness
└── Tessera.Fuzz/                  # fuzz targets (with SharpFuzz)
```

## Conventions

### Categories (xUnit `[Trait("Category", ...)]`)

| Category | Description | CI runs |
|---|---|---|
| `Unit` | Pure unit, sub-ms | Every PR |
| `Integration` | Sub-second | Every PR |
| `GoldenImage` | PNG rendering compare | Every PR |
| `Wpt` | WPT subset (~10k cases) | Nightly + on-demand |
| `Test262` | ECMA Test262 (~85k cases) | Nightly + on-demand |
| `Bench` | BDN runs | Weekly |
| `Fuzz` | Fuzz targets | Continuous on a CI runner |

### Naming

```csharp
public class HtmlTokenizerTests
{
    [Trait("Category", "Unit")]
    [Theory]
    [InlineData("<p>", "StartTag(p)")]
    [InlineData("</p>", "EndTag(p)")]
    public void DataState_BasicTags(string input, string expected) { ... }
}
```

## Golden image testing

```csharp
public sealed class GoldenImageTest : IClassFixture<EngineFixture>
{
    [Trait("Category", "GoldenImage")]
    [Theory]
    [GoldenSource("testdata/golden/**")]
    public async Task RenderMatchesGolden(GoldenCase c)
    {
        var page = await _engine.NewPageAsync();
        await page.NavigateAsync(new Url("file://" + c.HtmlPath));
        await page.WaitUntilIdle();
        using var img = await page.CaptureAsync(c.Viewport);
        GoldenAssert.Equal(c.PngPath, img, threshold: 0.005);
    }
}
```

`GoldenAssert.Equal`:
1. Loads expected PNG.
2. Computes SSIM (structural similarity) between expected and actual.
3. Fails if SSIM < `1 - threshold`.
4. On failure, writes `<actual>.png` and `<diff>.png` next to the expected. CI uploads as artifacts.

Fixture directory:
```
testdata/golden/
├── 001-hello-world.html
├── 001-hello-world.viewport.json   # { "width": 800, "height": 600 }
├── 001-hello-world.png             # the expected
├── 002-flexbox-basic.html
├── ...
```

Each milestone adds at least 20 golden cases targeting features delivered in that milestone.

## WPT subset

The full Web Platform Tests is 2M+ cases. We don't run all of them. Subset selection:

- `url/` (~700 URL parsing cases)
- `html/syntax/` (HTML parsing)
- `dom/nodes/` (DOM tree mutations + traversal)
- `css/css-syntax/`, `css/selectors/`, `css/css-cascade/`, `css/css-flexbox/`, `css/css-grid/`, `css/css-fonts/` (subsets)
- `fetch/` (subset — no service workers)
- `xhr/` (subset)
- `streams/` (subset)
- `encoding/`

Runner (`Tessera.Wpt/Runner.cs`):
```csharp
// loads testharness.js + testharnessreport.js into a Realm
// loads the test page via Engine
// listens for completion callback
// reports pass/fail counts per subdir
```

WPT consumes `wpt/` directory pulled in via `git submodule` at a pinned SHA. Update quarterly.

### Failure budgets per milestone

| Subsection | M3 | M5 | M7 | M11 |
|---|---|---|---|---|
| `url/` | 99% | 100% | 100% | 100% |
| `html/syntax/` | 99% | 100% | 100% | 100% |
| `dom/nodes/` | 80% | 90% | 95% | 99% |
| `css/css-syntax/` | 80% | 95% | 98% | 99% |
| `css/selectors/` | 50% | 80% | 90% | 95% |
| `css/css-cascade/` | 50% | 80% | 90% | 95% |
| `css/css-flexbox/` | 30% | 70% | 85% | 90% |
| `css/css-grid/` | 0% | 50% | 75% | 80% |
| `fetch/` | 30% | 70% | 85% | 90% |
| `encoding/` | 80% | 95% | 98% | 99% |

## Test262 (ECMA-262 conformance)

Pull as submodule. Harness in `Tessera.Test262/`. Per-test metadata in YAML frontmatter (`includes`, `flags`, `features`).

Selection in v1: skip `[Stage 3]` proposals, skip `non-strict + es5-only` legacy cases. Run the rest.

Concurrency: tests run in parallel xUnit workers, one `Realm` per test.

Targets (cumulative pass rate):
- M3: 80%
- M5: 90%
- M7: 95%
- M11: 98%

## html5lib-tests

[https://github.com/html5lib/html5lib-tests](https://github.com/html5lib/html5lib-tests)

Two formats:
- `tokenizer/*.test` — JSON files testing tokenizer state transitions.
- `tree-construction/*.dat` — text files testing tree builder against expected serialized tree.

Targets: 100% by end of M2 (HTML parsing is foundational).

## Fuzzing

`Tessera.Fuzz` defines targets:

```csharp
public class HtmlFuzz
{
    [FuzzerEntryPoint]
    public static void FuzzTokenizer(ReadOnlySpan<byte> input)
    {
        try {
            var parser = new HtmlParser(new Document(), Url.Empty, new());
            parser.FeedAsync(input.ToArray(), isLast:true, default).AsTask().Wait();
        } catch (HtmlParseException) { /* expected */ }
    }
}
```

Run continuously on a CI runner. Triage crashes via xUnit `[Theory]` cases generated from the corpus.

Subsystems with fuzz targets:
- HTML tokenizer + tree builder.
- CSS parser.
- URL parser.
- JS lexer + parser (catch StackOverflowException etc.).
- HTTP/1.1 response parser.
- HTTP/2 frame reader / HPACK decoder.
- TLS record layer (against malformed records).

## Benchmarks

`bench/Tessera.Bench` is a BenchmarkDotNet harness. Each subsystem owns a `.cs` file:

```csharp
[MemoryDiagnoser]
public class TokenizerBench
{
    private byte[] _bytes;
    [GlobalSetup] public void Setup() => _bytes = File.ReadAllBytes("testdata/bench/google.html");
    [Benchmark] public void Tokenize() { var t = new HtmlTokenizer(_bytes); while (t.MoveNext()) ; }
}
```

Targets per [01_ARCHITECTURE.md performance budget](01_ARCHITECTURE.md). Regressions trip a CI gate set at +15% over baseline.

## Memory and leak tests

Stress tests under `tests/Tessera.E2E/Memory/`:
- Open + close 1000 pages: working set must return within 10MB of baseline.
- Repeated navigations on the same page: no `JsObject` leak across `[GC.Collect; GC.WaitForPendingFinalizers]`.

## Rule 0 lint test

```csharp
public class RuleZeroTests
{
    [Fact] public void NoPInvoke_InAnyEngineProject()
    {
        var bad = Directory.EnumerateFiles("src", "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains("Tessera.Shell"))
            .Where(p => Regex.IsMatch(File.ReadAllText(p), @"\bDllImport\b|\bLibraryImport\b"));
        bad.Should().BeEmpty();
    }

    [Fact] public void NoSslStream_InNetProject()
    {
        var bad = Directory.EnumerateFiles("src/Tessera.Net", "*.cs", SearchOption.AllDirectories)
            .Where(p => File.ReadAllText(p).Contains("SslStream"));
        bad.Should().BeEmpty();
    }
}
```

## E2E harness

```csharp
public sealed class EndToEndTests
{
    [Trait("Category", "Integration")]
    [Fact] public async Task GoogleSearchPageRenders()
    {
        using var engine = new EngineFixture().Engine;
        var page = engine.NewBrowsingContext().ActivePage;
        await page.NavigateAsync(new Url("https://www.google.com/"));
        await page.WaitUntilIdleAsync(timeoutMs:10000);
        var rect = page.Document.QuerySelector("input[name=q]")
                       .GetBoundingClientRect();
        rect.Width.Should().BeGreaterThan(100);
    }

    [Trait("Category", "Integration")]
    [Fact] public async Task ClaudeWebsiteLoadsSignInForm()
    {
        // ...
    }
}
```

E2E tests run only in nightly CI (slow, network-dependent). Locally guarded by env var `TESSERA_E2E=1`.

## CI pipeline summary

| Job | Trigger | Duration | Required |
|---|---|---|---|
| `build` | PR + push | < 5 min | yes |
| `test-unit-integration-golden` | PR + push | < 15 min | yes |
| `test-html5lib` | PR + push | < 2 min | yes |
| `lint` (style + rule 0) | PR + push | < 2 min | yes |
| `test-wpt-subset` | nightly | < 60 min | no (advisory) |
| `test-test262-subset` | nightly | < 90 min | no (advisory) |
| `test-e2e` | nightly | < 30 min | no (advisory) |
| `fuzz` | continuous | always | no |

## Acceptance Tests

- [ ] `dotnet test --filter Category=Unit` exits 0 with > 0 tests passing on a fresh clone.
- [ ] `dotnet test --filter Category=GoldenImage` exits 0; at least 20 cases per active milestone.
- [ ] `dotnet test --filter Category=Wpt` produces a JSON results file; pass rate ≥ milestone target.
- [ ] Rule-0 lint test passes on every branch.
- [ ] BenchmarkDotNet results are emitted to `bench/results/<date>/` and uploaded as CI artifacts.
- [ ] At least one fuzz target has been running > 24h on a CI runner with no crashes.
