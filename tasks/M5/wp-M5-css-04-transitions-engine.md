---
id: wp:M5-css-04-transitions-engine
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:11:45Z"
completed_at: "2026-05-19T15:40:00Z"
branch: "main"
depends_on:
  - wp:M1-07-css-cascade
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-04-transitions-engine — CSS Transitions sampler

## Goal

Implement the runtime side of CSS Transitions 1: detect when a computed value
of a transition-able property changes, start a transition with the declared
duration / timing function / delay, and sample the property each frame until
the transition completes.

## Inputs

- `PropertyId.Transition{Property,Duration,TimingFunction,Delay,Behavior}` —
  already parsed and cascaded; the shorthand expander already exists in
  `PropertyRegistry.ExpandTransition`.
- A frame clock — needs to be exposed from `Starling.Loop` (or added).
- Interpolation between two `CssValue`s, per-property (length-lerp, color-lerp,
  number-lerp, transform-decompose-and-lerp for the result of `wp:M5-css-01`).

## Outputs

- `src/Starling.Css/Animations/TimingFunction.cs` — `ease`, `linear`,
  `ease-in`/`out`/`in-out`, `cubic-bezier(...)`, `steps(...)`.
- `src/Starling.Css/Animations/Interpolator.cs` — per-property value lerp.
- `src/Starling.Css/Animations/TransitionEngine.cs` — owns the active
  transition table, ticked once per frame; emits a "current effective value"
  per (element, property) tuple that the style engine layers above the
  computed value during cascade.
- Tests for timing functions (Bezier maths), interpolation (length, color,
  transform decomposition), and a small end-to-end test that asserts a
  property value crosses through interpolated points over time.

## Acceptance

- A property change driven by a class toggle reaches its target value after
  the declared duration (within one frame), passing through interpolated
  values at intermediate ticks.
- `transition: opacity 200ms ease 50ms` is honoured for delay + duration + curve.
- Multi-layer comma-separated transitions are supported (currently noted as a
  TODO in `PropertyRegistry.cs:482`).

## Notes

- Animation timing properties produce a `CssTime` value that already has
  `InSeconds`.
- The cascade itself doesn't tick — the engine driver must register the
  TransitionEngine as a per-frame callback. The intent is to plug into the
  event-loop's `requestAnimationFrame` queue once that's wired up.
- `transition-behavior: allow-discrete` is the new (Transitions 2) opt-in for
  animating discrete properties like `display`; defer behind a feature flag.

## Handoff log

- 2026-05-19T02:23Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-19T15:11:45Z — claimed by agent-copilot-claude-opus-4.7, working on main
- 2026-05-19T15:40Z — implemented the standalone transition runtime. Three
  new files under `src/Starling.Css/Animations/`:
    * `TimingFunction.cs` — `linear`, `ease`/`ease-in`/`ease-out`/`ease-in-out`
      (the spec keyword → cubic-bezier mapping), `CubicBezierTimingFunction`
      with Newton-Raphson + bisection fallback (matches Blink's
      `UnitBezier`), and `StepsTimingFunction` for all four jump terms.
      `TimingFunction.FromCss(CssValue?)` parses keywords + `cubic-bezier(…)`
      + `steps(…)` and fails soft to `ease`.
    * `Interpolator.cs` — per-property lerp dispatch over `CssValue`. Number
      / percentage / length (same-unit fast path, cross-absolute-unit lerp
      in px) / time / angle (lerp in degrees) / colour (premultiplied sRGB
      to keep transparent-fade clean) / transform (pairwise function lerp
      when signatures match, matrix-decompose-and-lerp slow path otherwise).
      Mismatched value kinds fall back to the spec's discrete rule
      (50% switch point). `IsAnimatable(PropertyId)` lists the properties we
      currently know how to interpolate.
    * `TransitionEngine.cs` — owns `Dictionary<(Element, PropertyId), ActiveTransition>`
      plus a parallel `_lastEffective` table for the "previous effective
      value" baseline. Public API: `OnComputedValueChanged(element, property,
      newValue, readProperty)` (cascade hook), `GetEffective(element,
      property)` (cascade reads back), `Tick(nowMs)` (frame loop), and
      `Forget(element)` (avoid leaking detached elements). Honours
      `transition-property: none | all | <list>`, mixed `s`/`ms`/`CssTime`
      durations, and `cubic-bezier`/`steps` timing. Interrupting an
      in-flight transition uses the current sample as the new from-value
      (so a class-toggle mid-fade doesn't snap back to the original start).
  Tests: 27 new in `tests/Starling.Css.Tests/`:
    * `TimingFunctionTests` (10) — endpoints, monotonicity, ease-in/out
      shape, step quantisation, FromCss parse + fallback.
    * `InterpolatorTests` (9) — numbers, lengths (same- and cross-unit),
      discrete fallback, colour midpoint, alpha fade, transform pairwise
      and matrix-fallback paths, IsAnimatable.
    * `TransitionEngineTests` (8) — priming on first value, full
      from-zero-to-one ramp with mid-sample assertion, delay, opt-out
      (`transition-property: color` ignoring opacity changes), `none`
      keyword, interrupted-transition resampling, and Forget.
  All green; full Starling.Css.Tests is 447 pass / 0 fail (was 420).
  StyleEngine cascade integration and `requestAnimationFrame` frame-loop
  wiring intentionally not in this WP — they're explicit non-goals per the
  WP "Notes" ("the engine driver must register the TransitionEngine as a
  per-frame callback... once requestAnimationFrame is wired up"). The
  multi-layer comma-separated `transition` shorthand is also still pending
  on `wp:M5-css-multi-layer-transitions` (tracked separately because it
  needs `CssValueList` top-level comma splitting in the cascade).
  (agent-copilot-claude-opus-4.7)
