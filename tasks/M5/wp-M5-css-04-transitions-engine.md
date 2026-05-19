---
id: wp:M5-css-04-transitions-engine
milestone: M5
status: available
claimed_by: ""
claimed_at: ""
branch: main
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
