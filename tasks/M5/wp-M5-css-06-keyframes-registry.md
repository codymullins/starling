---
id: wp:M5-css-06-keyframes-registry
milestone: M5
status: "complete"
claimed_by: "agent-copilot-claude-opus-4.7"
claimed_at: "2026-05-19T16:30:00Z"
completed_at: "2026-05-19T16:45:00Z"
claimed_at: "2026-05-19T15:25:53Z"
branch: "main"
depends_on: []
subsystem: Starling.Css
plan_refs:
  - browser-plan/06_CSS.md
  - browser-plan/13_MILESTONES.md#m5-avalonia-shell-interactivity-polish
---

# wp:M5-css-06-keyframes-registry — Pipe @keyframes into AnimationEngine

## Goal

Make stylesheets with `@keyframes` rules automatically register them with the
running `AnimationEngine`, so an `animation-name` referencing them resolves to
a real keyframe set.

## Inputs

- `KeyframesParser.ParseAll(StyleSheet)` (wp:M5-css-03, done).
- `AnimationEngine.RegisterKeyframes(KeyframesRule)` (wp:M5-css-05, done).
- `StyleEngine` cache-invalidation hook around `StyleEngine.cs:113` that fires
  on stylesheet add.

## Outputs

- `StyleEngine.AnimationEngine` and `StyleEngine.TransitionEngine` properties
  (constructed in ctor or DI-injected so tests can stub).
- `StyleEngine.OnStyleSheetAdded` (or the existing cache-invalidation hook)
  calls `KeyframesParser.ParseAll(sheet)` and registers each rule.
- Re-registration replaces by name (latest sheet wins).

## Acceptance

- Adding a sheet `@keyframes fade { 0%{opacity:0} 100%{opacity:1} }` then
  `engine.AnimationEngine` has a `KeyframesRule` named `fade`.
- Removing/replacing a sheet drops or replaces the rule.
- Existing `StyleEngine` tests still pass.

## Notes

- Shadow-tree scoping is out of scope (flat registry is fine for v1).
- Don't touch sample/effective behavior — this WP is plumbing only.

## Handoff log

- 2026-05-19T16:25Z — created (agent-copilot-claude-opus-4.7) — see
  `plan.md` in session state for the broader compositor plan.
- 2026-05-19T16:45Z — complete.
  - `StyleEngine` now constructs `AnimationEngine` + `TransitionEngine` in
    its ctor and exposes them as public properties.
  - `AddStyleSheet` calls `KeyframesParser.ParseAll(sheet)` and registers
    each rule with the animation engine. `RemoveStyleSheet` rebuilds the
    registry from remaining sheets so last-wins ordering is preserved.
  - `AnimationEngine` gained `HasKeyframes(name)` / `GetKeyframes(name)`
    lookups for tests + compositor diagnostics. `ClearKeyframes()` already
    existed.
  - New `tests/Starling.Css.Tests/StyleEngineKeyframesRegistrationTests.cs`
    (5 cases): add-registers, plain-sheets-don't-register, remove-drops,
    remove-preserves-other-sheet, later-sheet-overrides-by-name.
  - Css suite: 465 pass / 0 fail (was 460).
- 2026-05-19T15:25:53Z — claimed by agent-copilot-claude-opus-4.7, working on main
