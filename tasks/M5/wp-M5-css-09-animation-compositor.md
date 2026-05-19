---
id: wp:M5-css-09-animation-compositor
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:32:47Z"
branch: "main"
depends_on:
  - wp:M5-css-06-keyframes-registry
  - wp:M5-css-07-animation-shorthand-multilayer
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-09-animation-compositor — Stitch animations/transitions into the cascade

## Goal

Introduce an `AnimationCompositor` that overlays
`AnimationEngine.GetEffective` and `TransitionEngine.GetEffective` onto a
statically-cascaded `ComputedStyle` so callers (layout/paint) see the
correct, time-varying values.

## Inputs

- `StyleEngine.Compute(Element)` → static `ComputedStyle`.
- `AnimationCompositor.BuildDeclarations(ComputedStyle)` from wp:M5-css-07.
- `AnimationEngine` + `TransitionEngine` from wp:M5-css-04 / wp:M5-css-05.

## Outputs

- `Tessera.Css.Animations.AnimationCompositor`:
  - `ComputedStyle Compose(Element, ComputedStyle staticStyle, double nowMs)`.
  - Per-element cache of "last cascaded snapshot" so transition triggering
    is correct on value changes.
  - Calls `AnimationEngine.OnAnimationsCascaded` only when the declaration
    list differs from last seen (by structural equality).
  - For each property listed in `transition-property`, diffs the static
    cascaded value vs the last snapshot and feeds
    `TransitionEngine.OnComputedValueChanged`.
  - Final value priority: **transition > animation > static cascade**
    (CSS Animations 1 §3.2).
- `StyleEngine.Compute(Element, double? nowMs = null)`: when `nowMs` is
  non-null, runs the compositor before returning.

## Acceptance

- `transition: opacity 1s; opacity: 1` then a second `Compute` with
  `opacity: 0` cascaded — at t=500ms, the result has `opacity: 0.5`.
- `animation: fade 1s linear` over `opacity: 1` — at t=500ms, returns the
  keyframe-interpolated value, not the underlying `1`.
- When both target the same property simultaneously, the transition value
  wins.
- `Forget(element)` clears compositor + both engines for the element on
  removal.

## Notes

- Don't touch `LayoutEngine` / `Painter` here — they keep consuming
  `ComputedStyle` and the compositor is invisible to them.
- Animations on shorthand properties: cascade already expands shorthands
  before compositor runs.
- Keep the compositor allocation-light on the hot path (per-frame, per
  styled element).

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7). Blocked on
  wp:M5-css-06 and wp:M5-css-07.
- 2026-05-19T17:30Z — unblocked (deps complete).
- 2026-05-19T17:32Z — claimed by agent-copilot-claude-opus-4.7.
- 2026-05-19T18:00Z — completed.
  * `AnimationCompositor` is now an instance class taking `AnimationEngine`
    and `TransitionEngine`. `Compose(element, staticStyle, nowMs)` returns
    a `ComputedStyle` with transition + animation samples overlaid
    (transition > animation > static per §3.2). Per-element snapshot of
    cascaded values gates `TransitionEngine.OnComputedValueChanged` so
    re-Compose calls don't restart in-flight transitions every frame.
  * `BuildDeclarations` stays a static helper (wp-07 API).
  * Added `AnimationEngine.ActiveProperties(element)` and
    `TransitionEngine.ActiveProperties(element)` for compositor enumeration.
  * `ComputedStyle.WithOverrides(...)` (internal) layers in samples.
  * `StyleEngine.Compositor` property lazily constructs the singleton and
    `StyleEngine.ComputeWithAnimations(element, nowMs, ...)` runs the
    static cascade then the compositor.
  * Fixed a second latent bug (mirrors wp-07): `ExpandTransition` only
    matched `CssDimension` for duration; parser emits `CssTime`. With the
    fix, `transition: opacity 1s linear` actually sets `transition-duration`.
  * 5 new tests in `AnimationCompositorTests`; full Css suite 476 green,
    full sln build green.
- 2026-05-19T15:32:47Z — claimed by agent-copilot-claude-opus-4.7, working on main
