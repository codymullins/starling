---
id: "wp:spec-css-v1-om-subsystems"
milestone: "ongoing"
status: "available"
claimed_by: ""
claimed_at: ""
subsystem: "Starling.Css.Cssom / Starling.Bindings / Starling.Js.Hosting"
depends_on: []
plan_refs:
  - "tasks/SPEC_COVERAGE.md"
  - "browser-plan/06_CSS.md#cssom"
---

# wp:spec-css-v1-om-subsystems

The last four 🔴 CSS-V1 specs are JavaScript object-model subsystems. They
cannot be advanced with CSS parse tests — they need real engine/binding work in
the DOM + layout + JS-host stack and behavioral verification. These build on
this repo's current branch state, so they are main-tree / Engine.Tests /
Bindings.Tests work, **not** fresh-base worktree fan-out (see memory
`css-v1-parallelization`).

## The four specs + current state

All four now have a landed model/parse slice (🟢, audited + integrated this
session via sonnet subagents). What remains for each is the JS-binding /
behavioral layer.

1. **CSSOM View** (`cssom-view`) — 🟢 model: `DOMRectReadOnly`/`DOMRect`
   geometry (spec-correct edge derivation) in `src/Starling.Css/CssomView/`.
   Remaining: have `getBoundingClientRect` (`src/Starling.Bindings/NodeBindings.cs`,
   `Starling.Engine/BoxLayoutHost.cs`, `ILayoutHost.cs`) return this type; scroll
   offsets (`scrollX/Y`, `scrollTop/Left`, `scrollWidth/Height`,
   `clientWidth/Height`), `getClientRects`, `Element.scrollIntoView`,
   `Window.matchMedia` → `MediaQueryList` (the media evaluator exists).

2. **Web Animations 1** (`web-animations-1`) — 🟢 model: the §4 timing model
   (`EffectTiming`→`ComputeProgress`→`ComputedTiming`) in
   `src/Starling.Css/WebAnimations/`. Remaining: the JS WAAPI OM (`Animation`,
   `KeyframeEffect`, `Element.animate`, `getAnimations`) over the existing engine.

3. **CSS Typed OM 1** (`css-typed-om-1`) — 🟢 model: `CSSStyleValue`/
   `CSSNumericValue`/`CSSUnitValue`/`CSSKeywordValue`/`CSSUnparsedValue` +
   `CSSStyleValue.parse()` in `src/Starling.Css/TypedOm/`. Remaining: numeric
   math ops + JS `attributeStyleMap`/`computedStyleMap` bindings.

4. **CSS Font Loading 3** (`css-font-loading-3`) — 🟢 model: `FontFace`
   (incl. `FromRule(@font-face)`) + `FontFaceSet` (add/delete/has/count/status/
   `check()`) in `src/Starling.Css/FontLoading/`. Remaining: real async load +
   `ready`/events + `document.fonts` JS binding (async fetch already lives in
   `Starling.Engine.FontFaceFetcher`).

## Approach

Take them one at a time, behaviorally tested, in dependency order:
cssom-view (geometry already exists) → web-animations (engine exists) →
typed-om → font-loading. Each gets `[Spec(...)]` SpecFacts in the test project
with access to the engine/JS host, and matrix updates.

## Done when

Each spec's behavioral surface is implemented + `[Spec]`-tested green; matrix
rows move 🔴→🟢 (→✅ when their test surface is complete).
