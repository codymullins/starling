# WPT pass-rate plan — scientific, data-driven

How we raise the web-platform-tests pass rate **methodically**, not ad hoc.
Companion to `tests/Starling.Wpt.Tests/README.md` (harness) and
`tasks/SPEC_COVERAGE.md` (CSS spec matrix).

Baseline (pinned-SHA `dom,css,url`, 569 files, 3 crashers skipped, 2026-05-24):
**14.54% (754/5185 subtests)**.

## Prime directive

> **Never fix a test. Fix a measured root cause, ranked by subtests-unblocked,
> and attribute every change to a re-measured delta.**

A change that isn't preceded by an impact estimate and followed by a confirming
re-measure doesn't count as progress — it's an unverified guess.

## The five rules

1. **Measure → rank → fix → re-measure → attribute.** Each work item names the
   cause it targets, the predicted Δsubtests (from `causes.txt`), and the
   observed Δ after. A miss means the model is wrong — investigate that, don't
   just move on.
2. **Root causes, not tests.** Fixes are ranked by how many subtests share the
   cause. One `createEvent` bind > one hand-fixed test.
3. **Re-baseline after every cluster.** Fixing X unblocks/【re】veals Y; the
   population shifts. Regenerate the taxonomy between clusters.
4. **Control confounders.** Exclude out-of-scope features from the denominator;
   separate harness artifacts from engine bugs; filter classifier noise (the
   "method hint" heuristic misfires on `length`/`e`/`alias`/`plural`/`undefined`
   — not real methods).
5. **Ratchet.** Lock each gain with a CI floor per area so it can't silently
   regress (same mechanism as `STARLING_TEST262_FLOOR`).

## Baseline failure taxonomy (4,431 non-passing subtests, 2026-05-24)

From `testdata/wpt/results/failures.txt`:

| Class | Count | Nature |
|---|--:|---|
| missing method (`not a function`) | 1,492 | mechanical |
| **timeout** | 918 | async never completes |
| `assert_equals` | 632 | semantic (wrong value) |
| `assert_throws_dom` | 255 | semantic (wrong/absent throw) |
| missing constructor | 173 | mechanical |
| other `assert_*` | ~420 | semantic |
| `notrun` | 715 | gated behind an earlier failure |

**Two findings that set the order:**

- **918 timeouts are not scattered** — 877 are in `dom/nodes`, and **772 come
  from three files** (`Document-createElementNS.html` 585, `Document-createElement.html`
  147, `-namespace` 40). "Test timed out", no missing API. ⇒ a likely *single
  systemic cause* (~15% of the whole corpus), possibly harness-side.
- **Missing methods cluster by feature**, so a few binds unblock hundreds:

  | Cluster | Methods (counts) | ~subtests |
  |---|---|--:|
  | Legacy events | `createEvent` 254 + `MouseEvent`/`UIEvent`/`KeyboardEvent` ctors | ~290 |
  | Namespaces | `setAttributeNS` 176, `getElementsByTagNameNS` 50, `createAttribute` 10, `lookupNamespaceURI`/`isDefaultNamespace` 28 | ~265 |
  | DOMImplementation | `createDocumentType` 93, `createDocument` 41, `createProcessingInstruction` 27 | ~160 |
  | CSSOM | `setProperty` 101 | ~100 |
  | Ranges | `createRange` 54, `Range` ctor 6, `createValueRange` 61 | ~120 |
  | ChildNode/ParentNode | `before`/`after`/`replaceWith`/`replaceChildren`/`moveBefore` | ~114 |

## Phases

### Phase 0 — Make measurement scientific (foundation; do first)
- **Cause classifier**: `WptTests` emits `testdata/wpt/results/causes.txt` — a
  ranked histogram (`missing-method:X`, `missing-ctor:X`, `assert:*`, `timeout`,
  `harness-error`, `notrun`, `other:*`) with counts + an example file per cause.
  This *is* the auto-generated backlog, regenerated every run.
- **A/B delta**: compare two runs' `causes.txt`/`summary.txt` so a fix reports as
  "+N subtests, −M cause:X". (Start: `diff` of two `causes.txt`.)
- **Scope policy**: an explicit out-of-scope feature list (workers, Shadow DOM?,
  SVG?) excluded from the denominator — the WPT analogue of Test262
  `OutOfScopeFeatures`. Decide & document before counting.
- Keep the pinned-SHA per-area snapshot as the control.

### Phase 1 — Crack the timeout concentration (highest leverage)
877 timeouts in `dom/nodes`, 772 in 3 files. **Hypothesis:** one systemic cause
(a shared hang, or our virtual-time `PumpFrame` tripping testharness's global
timeout on large generated files). **Method:** instrument `Document-createElementNS.html`;
decide harness-side (cheap; may convert ~770 subtests at once) vs genuine
async/API hang. Re-measure, attribute. Done *before* API work because the result
reshapes the population.

### Phase 2 — Mechanical clusters, in measured impact order
events → namespaces → DOMImplementation → CSSOM `setProperty` → Ranges →
ChildNode/ParentNode. Each: estimate from `causes.txt` → implement → re-run the
area → confirm predicted Δ → regression test → ratchet floor → re-baseline.

### Phase 3 — Semantic conformance (`assert_*`, ~1,300)
Only after mechanical gaps close (they change which assertions run). Regenerate
taxonomy; triage `assert_equals`/`assert_throws_dom` per area vs spec. Long tail.

### Phase 4 — Robustness & breadth
Crasher root-cause (3 found, likely one recursion bug; or per-test process
isolation) → widen `WPT_DIRS` beyond dom/css/url once they plateau → retire the
hand-rolled CSS `[Spec]` stub backlog where WPT now covers it.

## Guardrails

- **Predict-then-verify** every fix (stated Δ vs observed Δ).
- **Re-baseline** after each cluster.
- **CI ratchet floor** per area.
- **Confounder hygiene**: out-of-scope excluded; harness artifacts ≠ engine bugs;
  classifier noise filtered.

## Status log

- 2026-05-24: harness landed (`2b4b9f0`); baseline 14.54%; taxonomy above captured.
- 2026-05-25: **Phase 0 classifier landed** — `WptTests` emits
  `testdata/wpt/results/causes.txt` (impact-ranked root-cause histogram) + a
  top-25 section in `summary.txt`. Auto-generated backlog confirms the manual
  taxonomy and adds `other:Right-hand side of 'instanceof' is not an object` (43,
  likely missing interface objects). A/B delta = `diff` two `causes.txt`.
  Remaining Phase-0 item: scope policy (out-of-scope feature list).
- _Next: Phase 1 — timeout concentration probe on `Document-createElementNS.html`._
