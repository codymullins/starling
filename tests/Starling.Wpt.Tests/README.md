# Starling.Wpt.Tests

Runs a subset of [web-platform-tests](https://github.com/web-platform-tests/wpt)
(testharness.js tests) against the Starling engine and reports a pass rate — the
HTML/CSS/DOM analogue of `Starling.Js.Test262.Tests`.

## How it works (thin runner over the official harness)

We do **not** reimplement WPT. We reuse the official artifacts:

- `tools/fetch-wpt.sh` vendors the suite at a **pinned SHA** into
  `testdata/wpt/suite/` (gitignored; blobless partial + cone sparse checkout, so
  only the chosen subset's blobs download).
- `WptFileServer` serves that checkout over a loopback HTTP port — like the
  official `wpt serve`, minus its dynamic handlers — so server-absolute
  references (`/resources/testharness.js`, `/common/…`) resolve.
- Requests for `/resources/testharnessreport.js` are answered with our own
  capture script (the **official** extension point): it registers an
  `add_completion_callback` that serializes harness + subtest results to JSON on
  `<html data-wpt-results>`.
- `WptRunner` navigates the engine (`StarlingEngine.LayoutPageAsync`) to each
  test, drains the event loop, and reads that attribute back. No new engine API.

The pass rate is over **subtests that produced a result**. Files that emit no
testharness output (reftests, load errors, or `testharness.js` failing to run)
are reported as `no-result` and excluded from the denominator — the way Test262
skips out-of-scope files.

## Usage

```bash
tools/fetch-wpt.sh                       # vendor the suite (pinned SHA)
dotnet test tests/Starling.Wpt.Tests     # run; writes testdata/wpt/results/summary.txt
```

Config via env vars (mirrors `STARLING_TEST262_*`):

| Var | Default | Meaning |
|---|---|---|
| `STARLING_WPT_DIRS` | `dom,css,url` | comma list of suite subdirs |
| `STARLING_WPT_FILTER` | — | case-insensitive path substring |
| `STARLING_WPT_MAX` | `0` | cap on files (0 = no cap) |
| `STARLING_WPT_TIMEOUT_MS` | `10000` | per-file timeout |
| `STARLING_WPT_FLOOR` | `0` | min pass rate to require (0 = report only) |

When the suite is absent the test is **inconclusive** (skipped), so CI without
the corpus stays green. Results land in `testdata/wpt/results/`
(`summary.txt`, `failures.txt`, `progress.txt`).

## Status / triage log (2026-05-24)

The harness surfaces engine gaps one at a time. Bringing `testharness.js` from
"won't compile" to "results flow end-to-end" took five fixes, each found by
running this suite:

1. **JS compiler** — `let`/`const` at the top of a `catch` block weren't
   TDZ-hoisted, so `testharness.js` failed to compile (`missing declared lexical
   'required_props'`). Fixed in `JsCompiler.EmitTryBody`; regression test in
   `Starling.Js.Tests/CatchBlockLexicalTests`.
2. **DOM** — `document.createElementNS` wasn't bound on the native backend
   (Jint already had it). Added in `Starling.Bindings/NodeBindings`.
3. **Window identity** — `window.parent` / `top` / `frames` now return the
   window and `window.opener` is `null` for a top-level context, so
   testharness's `_forEach_windows` stops calling `postMessage` on a phantom
   window. Added in `Starling.Bindings/WindowBinding`.
4. **Harness config** — the report script calls `setup({ output: false })` to
   disable testharness's visual result rendering. That rendering builds a table
   via DOM APIs inside a completion callback; an unimplemented one throws and
   aborts the callback chain before ours. We read results programmatically, so
   it's pure overhead.
5. **`JSON.stringify`** — serialized real arrays as objects (`{"0":…}`) because
   it only recognized JSON.parse's internal array type, not the engine's `JsArray`
   exotic. Fixed in `JsonObj` (§25.5.2 IsArray); regression test in
   `Starling.Js.Tests/JsonStringifyArrayTests`. This one broke the runner's own
   result parsing (testharness reports results as an array).

**Result:** real per-subtest pass/fail now flows. Full `dom,css,url` subset
(569 files, 3 crashers skipped): **14.54% (754/5185)**. Strong areas
`dom/lists` 76.7%, `dom/historical` 61.2%; weak areas `dom/ranges` 0.5%,
`css/css-syntax` 3.9%. A large `timeout`/`notrun` bucket (~1.6k subtests) is
async tests that don't complete — headroom from event-loop tuning on top of the
per-API gaps (`namedItem`, `getElementsByTagNameNS`, `createAttribute`, …). Run
`tools/fetch-wpt.sh` then `dotnet test tests/Starling.Wpt.Tests` to reproduce;
see `testdata/wpt/results/summary.txt`.

**Skip-list / crashers.** A few files trigger an *uncatchable* crash
(stack-overflow class) that aborts the whole test host — `.NET` can't trap these
even with the per-file worker thread. They live in `testdata/wpt/skip.txt`
(committed; one path substring per line). This is triage backlog (likely a
shared recursion bug in event dispatch / DOM traversal); a full `dom` run needs
these resolved or skipped. The cleaner long-term fix is per-test process
isolation in the runner.
