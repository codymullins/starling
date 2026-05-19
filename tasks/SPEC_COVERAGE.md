# CSS / Web Specification Coverage

**This file is the in-repo record of every CSS spec we know about, our
position on it, and where the conformance tests live.** Agents should read
this before starting any CSS work and update it when a spec moves between
columns.

Status legend:

| Symbol | Meaning |
|---|---|
| ✅ | Implemented — all spec tests `[SpecFact]`-tagged and passing |
| 🟢 | In progress — some `[SpecFact]`, some `[PendingFact]` |
| 🟡 | Scaffolded only — `_spec.md` exists; all tests `[PendingFact]` |
| 🔴 | Not started — no folder yet |
| 🚫 | Explicitly out of scope per `browser-plan/` |

Default CI gates on `dotnet test --filter "Status!=Pending"` (skipped tests
don't fail the build). A separate non-gating CI job runs
`STARLING_RUN_PENDING=true dotnet test --filter "Status=Pending"` to spot
tests that have started passing — when one does, promote it from
`[PendingFact]` to `[SpecFact]` and update the row here.

> Generated/updated by `dotnet run --project tools/Starling.SpecGen -- report`
> (the `catalog` and `generate-stubs` commands are implemented; the `report`
> command that rebuilds *this* file from trait data is not yet implemented —
> see `wp:spec-tooling-bootstrap`). Last manual sync: **2026-05-19**.
>
> The full machine catalog (`tasks/SPEC_CATALOG.md`, 123 specs, 1075
> properties, 59 at-rules, 169 selectors, 710 value types) is derived
> from `testdata/webref/css/*.json` and is the source of truth for which
> specs / properties exist.
>
> **Two test projects together feed the dashboard**:
>
> 1. `tests/Starling.Css.Spec.Tests/` — **enumeration of what the spec
>    requires**. `dotnet run --project tools/Starling.SpecGen -- generate-stubs`
>    produced **103 spec folders / 1185 stubs** (one per property / at-rule /
>    selector). As of the last sync **2 are promoted to `[SpecFact]` and
>    passing**; 1183 remain `[PendingFact]` (skipped). Promote stubs to
>    `[SpecFact]` (with a real assertion) as features are implemented — see
>    `tests/Starling.Css.Spec.Tests/CssColor/PropertyTests.cs` for the
>    worked-example template.
>
> 2. `tests/Starling.Css.Tests/` — **proof of what the engine does**. Every
>    legacy test class is now tagged `[Spec("<spec-id>", "<url>")]` so the
>    same `Spec=<id>` filter surfaces real, passing coverage:
>
>    ```bash
>    dotnet test tests/Starling.Css.Tests --filter "Spec=css-color-4"  # 37 passing
>    dotnet test tests/Starling.Css.Tests --filter "Spec=selectors-4"  # 50 passing
>    ```
>
> `STARLING_RUN_PENDING=true dotnet test tests/Starling.Css.Spec.Tests`
> currently fails 1183/1185 — that count is our conformance backlog and
> the denominator for progress tracking.

---

## Working with the generated stubs

* **Default test run is green** because every generated test is `[PendingFact]`
  (skipped). Run `dotnet test tests/Starling.Css.Spec.Tests` to verify.
* **See the backlog**: `STARLING_RUN_PENDING=true dotnet test tests/Starling.Css.Spec.Tests`.
  Today: 0/1185 conformance assertions implemented.
* **Filter by spec**: `dotnet test --filter "Spec=css-color-5"` runs only the
  `[Spec]`-trait-tagged tests for that spec.
* **Promoting a stub**: replace `[PendingFact(...)]` with `[SpecFact]` and
  write a real assertion. The skip flag flips automatically; `Status=Implemented`
  trait appears.
* **Regenerating**: re-running `generate-stubs` is idempotent — existing
  files are never overwritten, so promoted tests are safe. To pick up
  spec additions, `cd testdata/webref && (refresh per README)` then
  `dotnet run --project tools/Starling.SpecGen -- generate-stubs`.

## Core syntax & cascade

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS Syntax L3 | https://www.w3.org/TR/css-syntax-3/ | 🟢 | `Starling.Css.Tests/CssTokenizerTests`, `CssParserTests` (legacy unit) — needs `Starling.Css.Spec.Tests/CssSyntax3/` | `wp:spec-css-syntax-3` |
| CSS Values & Units L4 | https://www.w3.org/TR/css-values-4/ | 🟢 | legacy — needs `Starling.Css.Spec.Tests/CssValues4/` | `wp:spec-css-values-4` |
| CSS Cascade & Inheritance L5 | https://www.w3.org/TR/css-cascade-5/ | 🟢 | legacy — needs `Starling.Css.Spec.Tests/CssCascade5/` | `wp:spec-css-cascade-5` |
| CSS Custom Properties L1 | https://www.w3.org/TR/css-variables-1/ | 🟡 | `Starling.Css.Spec.Tests/CssVariables1/` | `wp:spec-css-variables-1` |
| CSS Conditional L5 (`@media`, `@supports`) | https://www.w3.org/TR/css-conditional-5/ | 🟢 | legacy | `wp:spec-css-conditional-5` |
| CSS Nesting L1 | https://www.w3.org/TR/css-nesting-1/ | 🟢 | `Starling.Css.Tests/NestingTests` (legacy) | `wp:spec-css-nesting-1` |
| Selectors L4 | https://www.w3.org/TR/selectors-4/ | 🟢 | legacy — needs `Selectors4/` | `wp:spec-selectors-4` |
| CSS Scoping (`@scope`) | https://www.w3.org/TR/css-cascade-6/#scoped-styles | 🔴 | — | `wp:spec-css-scope-1` |

## Color & typography

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS Color 4 | https://www.w3.org/TR/css-color-4/ | 🟢 | legacy (`ColorFunctionTests`, `GamutMappingTests`) | `wp:spec-css-color-4` |
| CSS Color 5 (color-mix, relative) | https://www.w3.org/TR/css-color-5/ | 🟡 | `CssColor5/` | `wp:spec-css-color-5-relative` |
| CSS Color HDR 6 | https://www.w3.org/TR/css-color-hdr/ | 🔴 | — | `wp:spec-css-color-6` |
| CSS Color Adjust 1 (`color-scheme`, `forced-colors`) | https://www.w3.org/TR/css-color-adjust-1/ | 🔴 | — | `wp:spec-css-color-adjust-1` |
| CSS Fonts 4 | https://www.w3.org/TR/css-fonts-4/ | 🟢 | legacy (`FontFaceParserTests`) — gaps in `font-variation-settings`, `font-feature-settings`, `size-adjust` | `wp:spec-css-fonts-4` |
| CSS Font Loading 3 (`document.fonts`) | https://www.w3.org/TR/css-font-loading-3/ | 🔴 | — | `wp:spec-css-font-loading-3` |
| CSS Text 3 | https://www.w3.org/TR/css-text-3/ | 🔴 | — | `wp:spec-css-text-3` |
| CSS Text 4 | https://www.w3.org/TR/css-text-4/ | 🚫 | deferred (drafts) | — |
| CSS Text Decoration 3 | https://www.w3.org/TR/css-text-decor-3/ | 🔴 | — | `wp:spec-css-text-decor-3` |
| CSS Text Decoration 4 | https://www.w3.org/TR/css-text-decor-4/ | 🔴 | — | `wp:spec-css-text-decor-4` |
| CSS Inline 3 | https://www.w3.org/TR/css-inline-3/ | 🔴 | — | `wp:spec-css-inline-3` |

## Layout

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS 2.2 (block/inline/floats/margin-collapse) | https://www.w3.org/TR/CSS22/ | 🟢 | `Starling.Layout.Tests` (partial) | `wp:spec-css-22-layout` |
| CSS Display 3 | https://www.w3.org/TR/css-display-3/ | 🟢 | legacy — gaps in `display: contents`, `flow-root` | `wp:spec-css-display-3` |
| CSS Box Model 3 | https://www.w3.org/TR/css-box-3/ | 🟢 | legacy | `wp:spec-css-box-3` |
| CSS Sizing 3 | https://www.w3.org/TR/css-sizing-3/ | 🟢 | legacy — intrinsic sizing untested | `wp:spec-css-sizing-3` |
| CSS Sizing 4 (`contain-intrinsic-size`) | https://www.w3.org/TR/css-sizing-4/ | 🔴 | — | `wp:spec-css-sizing-4` |
| CSS Position 3 | https://www.w3.org/TR/css-position-3/ | ✅ | `Starling.Layout.Tests/Position` | — |
| CSS Flexbox 1 | https://www.w3.org/TR/css-flexbox-1/ | ✅ | `Starling.Layout.Tests/Flex`, `FlexPropertyTests` | — |
| CSS Grid 2 | https://www.w3.org/TR/css-grid-2/ | 🟢 | `GridPropertyTests` parse-only; **no layout tests** | `wp:spec-css-grid-2-layout` |
| CSS Tables 3 | https://www.w3.org/TR/css-tables-3/ | 🟢 | `TableLayoutTests` (minimal) | `wp:spec-css-tables-3` |
| CSS Multicol 1 | https://www.w3.org/TR/css-multicol-1/ | 🔴 | — | `wp:spec-css-multicol-1` |
| CSS Logical Properties 1 | https://www.w3.org/TR/css-logical-1/ | 🟢 | `LogicalPropertyTests` | `wp:spec-css-logical-1` |
| CSS Writing Modes 4 | https://www.w3.org/TR/css-writing-modes-4/ | 🔴 | — | `wp:spec-css-writing-modes-4` |
| CSS Overflow 3 | https://www.w3.org/TR/css-overflow-3/ | 🔴 | — | `wp:spec-css-overflow-3` |
| CSS Containment 2 | https://www.w3.org/TR/css-contain-2/ | 🔴 | — | `wp:spec-css-contain-2` |
| CSS Container Queries 1 | https://www.w3.org/TR/css-contain-3/ | 🚫 | deferred per `browser-plan/06_CSS.md` | — |
| CSS Anchor Positioning 1 | https://www.w3.org/TR/css-anchor-position-1/ | 🚫 | not in scope | — |

## Visual / paint

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS Backgrounds & Borders 3 | https://www.w3.org/TR/css-backgrounds-3/ | 🟡 | `CssBackgrounds3/` | `wp:spec-css-backgrounds-3` |
| CSS Backgrounds & Borders 4 | https://www.w3.org/TR/css-backgrounds-4/ | 🔴 | — | `wp:spec-css-backgrounds-4` |
| CSS Images 3 | https://www.w3.org/TR/css-images-3/ | 🔴 | — | `wp:spec-css-images-3` |
| CSS Images 4 (`image-set()`, `cross-fade()`) | https://www.w3.org/TR/css-images-4/ | 🔴 | — | `wp:spec-css-images-4` |
| CSS Masking 1 | https://www.w3.org/TR/css-masking-1/ | 🔴 | — | `wp:spec-css-masking-1` |
| CSS Filter Effects 1 | https://www.w3.org/TR/filter-effects-1/ | 🔴 | — | `wp:spec-css-filter-effects-1` |
| CSS Compositing 1 | https://www.w3.org/TR/compositing-1/ | 🔴 | — | `wp:spec-css-compositing-1` |
| CSS Shapes 1 | https://www.w3.org/TR/css-shapes-1/ | 🔴 | — | `wp:spec-css-shapes-1` |
| CSS Transforms 1 | https://www.w3.org/TR/css-transforms-1/ | 🟢 | `CssTransformParserTests`, `TransformPropertyTests` | `wp:spec-css-transforms-1` |
| CSS Transforms 2 (3D, individual props) | https://www.w3.org/TR/css-transforms-2/ | 🔴 | — | `wp:spec-css-transforms-2` |
| CSS Scroll Snap 1 | https://www.w3.org/TR/css-scroll-snap-1/ | 🔴 | — | `wp:spec-css-scroll-snap-1` |
| CSS Scrollbars 1 | https://www.w3.org/TR/css-scrollbars-1/ | 🔴 | — | `wp:spec-css-scrollbars-1` |
| CSS Overscroll Behavior 1 | https://www.w3.org/TR/css-overscroll-1/ | 🔴 | — | `wp:spec-css-overscroll-1` |
| CSS View Transitions 1 | https://www.w3.org/TR/css-view-transitions-1/ | 🚫 | not in scope | — |
| CSS Scroll-Driven Animations 1 | https://www.w3.org/TR/scroll-animations-1/ | 🚫 | not in scope | — |

## UI / interaction

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS Basic UI 4 | https://www.w3.org/TR/css-ui-4/ | 🔴 | — | `wp:spec-css-ui-4` |
| CSS Pseudo 4 | https://www.w3.org/TR/css-pseudo-4/ | 🟢 | `PseudoElementTests` (legacy) — `::backdrop`, `::marker`, `::file-selector-button` untested | `wp:spec-css-pseudo-4` |
| CSS Lists 3 | https://www.w3.org/TR/css-lists-3/ | 🔴 | — | `wp:spec-css-lists-3` |
| CSS Counter Styles 3 (`@counter-style`) | https://www.w3.org/TR/css-counter-styles-3/ | 🔴 | — | `wp:spec-css-counter-styles-3` |
| CSS Generated Content 3 | https://www.w3.org/TR/css-content-3/ | 🔴 | — | `wp:spec-css-content-3` |
| CSS Speech 1 | https://www.w3.org/TR/css-speech-1/ | 🚫 | not in scope | — |
| CSS Ruby 1 | https://www.w3.org/TR/css-ruby-1/ | 🚫 | not in scope | — |

## Animation

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSS Animations 1 | https://www.w3.org/TR/css-animations-1/ | 🟢 | `AnimationEngineTests`, `KeyframesParserTests` | `wp:spec-css-animations-1` |
| CSS Animations 2 | https://www.w3.org/TR/css-animations-2/ | 🔴 | — | `wp:spec-css-animations-2` |
| CSS Transitions 1 | https://www.w3.org/TR/css-transitions-1/ | ✅ | `TransitionEngineTests`, `TransitionEngineSpecTests` | — |
| CSS Easing 1 | https://www.w3.org/TR/css-easing-1/ | 🟢 | `TimingFunctionTests` — `linear()` multi-stop untested | `wp:spec-css-easing-1` |
| Web Animations 1 | https://www.w3.org/TR/web-animations-1/ | 🔴 | belongs to `Starling.Cssom.Spec.Tests/` | `wp:spec-web-animations-1` |

## OM / scripting (`Starling.Cssom.Spec.Tests` family — to be created)

| Spec | URL | Status | Folder | Tracking WP |
|---|---|---|---|---|
| CSSOM | https://drafts.csswg.org/cssom/ | 🔴 | — | `wp:spec-cssom` |
| CSSOM View | https://drafts.csswg.org/cssom-view/ | 🔴 | — | `wp:spec-cssom-view` |
| CSS Properties & Values API L1 (`@property`) | https://www.w3.org/TR/css-properties-values-api-1/ | 🔴 | — | `wp:spec-css-properties-values-api-1` |
| CSS Typed OM 1 | https://www.w3.org/TR/css-typed-om-1/ | 🚫 | not in scope | — |
| CSS Houdini Paint API | https://www.w3.org/TR/css-paint-api-1/ | 🚫 | not in scope | — |

## `@`-rule cross-index

| At-rule | Spec | Status |
|---|---|---|
| `@import` | css-cascade-5 | ✅ `ImportConditionsTests` |
| `@media` | css-conditional-5 | ✅ `MediaQueryEvaluatorTests` |
| `@supports` | css-conditional-5 | ✅ `SupportsEvaluatorTests` |
| `@layer` | css-cascade-5 | ✅ `CascadeLayersTests` |
| `@font-face` | css-fonts-4 | ✅ `FontFaceParserTests` |
| `@keyframes` | css-animations-1 | ✅ `KeyframesParserTests` |
| `@property` | css-properties-values-api-1 | 🔴 |
| `@counter-style` | css-counter-styles-3 | 🔴 |
| `@page` | css-page-3 | 🔴 |
| `@scope` | css-cascade-6 | 🔴 |
| `@starting-style` | css-transitions-2 | 🔴 |
| `@container` | css-contain-3 | 🚫 deferred |

---

## Overall counters (manual until SpecGen lands)

| | Count |
|---|---|
| Specs catalogued | 54 |
| ✅ Implemented | 3 |
| 🟢 In progress | 16 |
| 🟡 Scaffolded only | 3 |
| 🔴 Not started | 25 |
| 🚫 Out of scope | 7 |
