---
id: wp:M5-css-05-animations-engine
milestone: M5
status: available
claimed_by: ""
claimed_at: ""
branch: main
depends_on:
  - wp:M5-css-03-keyframes-rule
  - wp:M5-css-04-transitions-engine
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-05-animations-engine — CSS Animations driver

## Goal

Implement the runtime that binds `animation-name` to a parsed `@keyframes`
rule and produces the per-frame effective value for each animated property.
Reuses the timing-function and interpolator infrastructure built in
`wp:M5-css-04-transitions-engine`.

## Inputs

- `KeyframesRule` (from `wp:M5-css-03`) — sorted-by-offset frames keyed by name.
- `PropertyId.Animation{Name,Duration,TimingFunction,Delay,IterationCount,Direction,FillMode,PlayState,Composition}`
  — already cascaded, with shorthand expansion in
  `PropertyRegistry.ExpandAnimation`.
- `TimingFunction` + `Interpolator` from `wp:M5-css-04`.

## Outputs

- `src/Starling.Css/Animations/AnimationEngine.cs` — per-(element, animation)
  state machine; ticked once per frame; queries the matching `KeyframesRule`
  by name in the active stylesheet registry.
- Iteration counting, direction (`normal`/`reverse`/`alternate`/`alternate-reverse`),
  fill-mode (`none`/`forwards`/`backwards`/`both`), play-state (`paused`/`running`).
- Multi-layer animation support (matches the `PropertyRegistry.cs:482` TODO).
- Tests for: linear progression, alternate direction flip, fill-mode forwards
  holding the final value, paused state freezing playback.

## Acceptance

- `animation: pulse 1s ease-in-out infinite alternate` on a box drives the
  property values through interpolated frames forever.
- A finite `animation-iteration-count` stops at the right offset given the
  `animation-fill-mode`.
- Pausing via `animation-play-state: paused` freezes the current sampled value.

## Notes

- Keyframes that omit `from` or `to` synthesise endpoints from the underlying
  cascaded computed value (CSS Animations 1 §4.2).
- Animation names are resolved against the stylesheets that contributed to
  the element — keyframes in shadow roots are not exposed to the light tree.
  For v1, a flat sheet registry is fine.
- The engine produces the same "effective value" stream shape as the
  TransitionEngine, so the style layer can compose both inputs.

## Handoff log

- 2026-05-19T02:23Z — created (agent-copilot-claude-opus-4.7)
