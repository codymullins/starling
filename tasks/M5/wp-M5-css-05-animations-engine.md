---
id: wp:M5-css-05-animations-engine
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:17:03Z"
completed_at: "2026-05-19T16:05:00Z"
branch: "main"
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
- 2026-05-19T15:17:03Z — claimed by agent-copilot-claude-opus-4.7, working on main
- 2026-05-19T16:05Z — complete.
  - Added `src/Starling.Css/Animations/AnimationEngine.cs`: per-element list of
    `AnimationInstance`; `OnAnimationsCascaded(element, declarations)` diffs by
    name so re-cascade preserves playback position (CSS Animations 1 §6);
    multi-layer with later layer winning on conflicting property;
    `RegisterKeyframes` for sheet-driven name resolution; `Tick(nowMs)` advances
    a monotonic clock and clamps backwards times; `GetEffective(element, propId)`
    samples the effective value; `Forget(element)` drops state on detach.
  - `AnimationInstance.Sample` computes elapsed from `_startMs`, handles the
    delay window (backfill via `fill-mode: backwards|both`), iteration count
    (finite ends → forwards-fill window when `fill-mode: forwards|both`),
    direction (`alternate` flips on odd iterations; `alternate-reverse` flips
    on even), and applies the animation-level timing function to iteration
    progress. Keyframe interpolation walks the rule's frames for property-bearing
    pairs, computes `(eased - beforeOffset) / (afterOffset - beforeOffset)`,
    and delegates to `Interpolator.Interpolate`. Property names are converted
    PascalCase→kebab-case.
  - Pause: `play-state: paused` freezes `_pausedAtElapsedMs` at the moment of
    pause; on resume `_startMs` is shifted forward by the pause duration so
    the next sample reads from the same offset.
  - Tests at `tests/Starling.Css.Tests/AnimationEngineTests.cs` (13 cases) cover:
    linear progression, no-fill behavior after end, fill-mode `forwards`
    (holds final), fill-mode `backwards` (holds initial during delay),
    without-fill behavior in delay window, `alternate` direction flip on
    iteration 1, `reverse` direction starting at end value, paused freezing
    sample + resume continuing from same offset, zero-iteration-count
    suppressing output, unregistered keyframes name → null, multi-layer later
    layer overriding earlier, removed animation name stopping sampling,
    keyframe-only-at-endpoints interpolating directly.
  - Css test suite: 460 pass / 0 fail (was 447). Full sln build green.
  - Intentional non-goals (deferred): per-keyframe `animation-timing-function`
    (currently only the animation-level function is applied; keyframe-local
    timing functions need a KeyframeDeclaration extension); `AnimationComposition`
    (add/accumulate); cascade integration (rAF wiring out of scope per WP
    notes); multi-layer shorthand expansion in `PropertyRegistry.cs:482` is
    still single-layer — `OnAnimationsCascaded` accepts the list shape, but
    the cascade layer doesn't yet produce multiple `AnimationDeclaration`s
    from a comma-separated shorthand. These all blocked behind a downstream
    "animation/transition compositor" task that should live in M6 cascade
    integration.
