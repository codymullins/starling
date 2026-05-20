# Starling.SpecGen — CSS spec catalog & coverage reporter

A small .NET CLI that ingests upstream spec definitions and reports our
coverage against them.

> **Conformance tests are hand-written, not generated.** The old
> `generate-stubs` command (one `[PendingFact]` per webref definition) was
> removed — it produced ~1185 empty stubs that implied coverage we didn't
> have. Tests now live wherever they naturally belong (`tests/*.Tests/`),
> are hand-written, and carry `[Spec]` + `[SpecFact]`/`[PendingFact]`
> traits. This tool's job is only to tell you **what the spec defines**
> (the catalog) so you know what's still missing.

## Implemented today

- **`catalog`** — reads `testdata/webref/css/*.json` and emits a flat
  per-spec summary (counts of properties, at-rules, selectors, value
  types). Output is committed to `tasks/SPEC_CATALOG.md`. This is the
  upstream "what exists" map you diff against your `[Spec]`-tagged tests to
  find gaps.

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

