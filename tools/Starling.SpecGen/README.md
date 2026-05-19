# Starling.SpecGen — CSS spec catalog & coverage reporter

A small .NET CLI that ingests upstream spec definitions and reports our
coverage against them.

## Implemented today

- **`catalog`** — reads `testdata/webref/css/*.json` and emits a flat
  per-spec summary (counts of properties, at-rules, selectors, value
  types). Output is committed to `tasks/SPEC_CATALOG.md`.

```bash
dotnet run --project tools/Starling.SpecGen -- catalog \
  > tasks/SPEC_CATALOG.md
```

Current snapshot:

- **123 CSS specs** (`testdata/webref/css/*.json`)
- **41 IDL files** (`testdata/webref/idl/*.idl` — CSSOM, CSSOM View, Web
  Animations, Font Loading, etc.)
- **1075 properties, 59 at-rules, 169 selectors, 710 value types** in
  webref's parsed catalog.

- **`generate-stubs`** — for every spec in the catalog, emit a `_spec.md`
  manifest plus `PropertyTests.cs` / `AtRuleTests.cs` / `SelectorTests.cs`
  containing one `[PendingFact]` per definition under
  `tests/Starling.Css.Spec.Tests/{SpecId}/`. **Idempotent** — existing files
  are never overwritten, so hand-written tests and promoted `[SpecFact]`s are
  safe. Today's run produces **103 folders / 1185 stubs**.

  ```bash
  dotnet run --project tools/Starling.SpecGen -- generate-stubs
  ```

## Planned (not implemented yet — tracked by `wp:spec-tooling-bootstrap`)

- **`fetch`** — refresh `testdata/webref/` from upstream `w3c/webref` at a
  pinned commit. Today this is a manual `git sparse-checkout` + `cp`
  documented in `testdata/webref/README.md`.
- **`report`** — diff the catalog against test-assembly trait data
  (`[Spec]`, `[SpecFact]`, `[PendingFact]`) and rebuild
  `tasks/SPEC_COVERAGE.md` automatically, with per-spec percentages.
- **WPT integration** — pull a curated subset of `web-platform-tests` into
  `testdata/wpt/css/`, normalise the manifests into the catalog, run them
  through a thin xUnit harness so each WPT case is one xUnit test.

## Upstream sources

| Source | Purpose | License | Pinned at |
|---|---|---|---|
| `w3c/webref` (`ed/css/*.json`, `ed/idl/*.idl`) | property/at-rule/selector catalog, IDL | MIT | see `testdata/webref/README.md` |
| `mdn/data` (`css/*.json`) | curated property summaries — *not yet ingested* | CC0 | — |
| `web-platform-tests/wpt` (`css/*`) | executable conformance tests — *not yet ingested* | BSD-3 / W3C-test | — |

