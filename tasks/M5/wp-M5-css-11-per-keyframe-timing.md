---
id: wp:M5-css-11-per-keyframe-timing
milestone: M5
status: "claimed"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T15:39:22Z"
branch: "main"
depends_on: []
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
---

# wp:M5-css-11-per-keyframe-timing — animation-timing-function inside a @keyframes block

## Goal

Honour `animation-timing-function` declared *inside* an individual
`@keyframes` rule's selector block. The function is applied to the **segment
starting at that keyframe**, not to the whole animation
(CSS Animations 1 §7.1).

## Inputs

- `KeyframesParser.TryParse` (wp:M5-css-03).
- `AnimationInstance.SampleAtProgress` in
  `src/Starling.Css/Animations/AnimationEngine.cs`.

## Outputs

- Parse `animation-timing-function` out of `Keyframe.Declarations`; surface
  on a new `Keyframe.SegmentTimingFunction` (default linear).
- `SampleAtProgress` applies the *previous* keyframe's segment timing
  function to the segment progress before delegating to
  `Interpolator.Interpolate`.
- Tests:
  - `@keyframes k { 0% { opacity: 0; animation-timing-function: steps(2) }
    100% { opacity: 1 } }` — sample at progress 0.4 → 0 (first step),
    at 0.6 → 0.5.

## Acceptance

- Existing animation tests stay green.
- New step-function test passes.

## Notes

- The end-keyframe's timing function is ignored (CSS Animations 1 §7.1).
- This is a quality-of-life follow-up to wp:M5-css-05; not on the compositor
  critical path.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7).
- 2026-05-19T15:39:22Z — claimed by agent-copilot-claude-opus-4.7, working on main
