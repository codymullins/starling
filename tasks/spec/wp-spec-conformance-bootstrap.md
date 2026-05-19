---
id: "wp:spec-conformance-bootstrap"
milestone: "ongoing"
status: "in_progress"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T16:20:00Z"
subsystem: "Starling.Css / spec-conformance"
plan_refs:
  - "browser-plan/06_CSS.md#acceptance-tests"
  - "browser-plan/12_TESTING.md"
  - "tasks/SPEC_COVERAGE.md"
---

# wp:spec-conformance-bootstrap

Bootstrap the **CSS/web spec conformance** test infrastructure so every
spec we care about has a dedicated, traceable, machine-discoverable home
for tests — including specs we haven't started implementing yet.

## Goals

1. **One test per spec requirement** — even unimplemented ones — so the
   suite is the in-repo, executable record of what we still owe.
2. **Default-green CI**: unimplemented tests are skipped, not failing.
3. **Easy promotion**: when implementation lands, flipping a test from
   `[PendingFact]` → `[SpecFact]` is a one-line change.
4. **Filterable** by spec id, section, status, milestone.
5. **Reportable**: `tasks/SPEC_COVERAGE.md` is the single source of truth
   for "where are we against the web platform?"

## What landed in this bootstrap

- `tests/Starling.Spec.Common/`
  - `SpecAttribute` — adds `Spec`, `SpecUrl`, `Section` traits.
  - `SpecFactAttribute` — runs; adds `Status=Implemented`.
  - `PendingFactAttribute` — skips by default; adds `Status=Pending` and
    optional `Wp` trait. `STARLING_RUN_PENDING=true` actually runs them.
- `tests/Starling.Css.Spec.Tests/` — first family project, with three
  exemplar spec folders (`CssVariables1`, `CssColor5`, `CssBackgrounds3`),
  each containing a `_spec.md` (frontmatter manifest) and stub test
  classes tagged with `[Spec]` + `[PendingFact]`.
- `testdata/webref/` — **pinned snapshot of `w3c/webref`** (commit
  `589264b15ca2172f2592c5578a857b4d0e2840c1`): 123 CSS spec JSON files
  (`ed/css/*.json`) + 41 IDL files for CSSOM/Web Animations/Font Loading.
  Source of truth for what specs exist and what they define.
- `tools/Starling.SpecGen/` — working .NET CLI:
  - `catalog` command reads `testdata/webref/css/*.json` and emits
    `tasks/SPEC_CATALOG.md` (1075 properties, 59 at-rules, 169 selectors,
    710 value types across 123 specs).
  - `report` / `generate-stubs` / `fetch` commands are still stubbed.
- `tasks/SPEC_CATALOG.md` — machine-generated, committed.
- `tasks/SPEC_COVERAGE.md` — hand-maintained coverage matrix until
  `report` is implemented.

## Follow-up work (open)

Each spec listed in `tasks/SPEC_COVERAGE.md` with status 🔴/🟡/🟢 should
get its own `wp:spec-<spec-id>` package; the table already references the
intended wp ids. Highest-impact next:

1. `wp:spec-cssom` and `wp:spec-cssom-view` — entire JS-visible CSS layer
   has zero tests today.
2. `wp:spec-css-variables-1` — flesh out the scaffolded folder; cycle
   detection + IACVT.
3. `wp:spec-css-backgrounds-3` — `background` shorthand, multi-layer,
   `border-radius`, `box-shadow` parsing + paint.
4. `wp:spec-css-images-3` — gradient parsers.
5. `wp:spec-css-grid-2-layout` — layout-side tests for grid (parsing
   exists, algorithm tests don't).
6. `wp:spec-tooling-bootstrap` — implement `Starling.SpecGen` so the
   catalog stops being maintained by hand.

## Conventions for follow-up agents

- Add `[Spec]` to every test class and method that maps to a spec section.
- Default to `[PendingFact]` for anything you haven't proven works; this
  keeps CI green while still documenting the requirement.
- When you make a pending test pass, change it to `[SpecFact]` and tick
  the row in `tasks/SPEC_COVERAGE.md` in the same commit.
- Spec folders live under
  `tests/Starling.{Css,Layout,Paint,Cssom}.Spec.Tests/{SpecId}/`. Create
  the family project if it doesn't yet exist; copy `Starling.Css.Spec.Tests`
  as a template.
