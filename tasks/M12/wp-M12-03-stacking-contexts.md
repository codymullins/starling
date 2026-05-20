---
id: wp:M12-03-stacking-contexts
milestone: M12
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-20T02:21:12Z"
completed_at: ""
branch: "main"
depends_on: []
blocks:
  - wp:M12-04-layer-tree
subsystem: Starling.Layout
plan_refs:
  - browser-plan/07_LAYOUT.md
  - browser-plan/08_FONTS_PAINT.md
  - browser-plan/13_MILESTONES.md#m12-tiled-compositor-layer-tree
---

# wp:M12-03-stacking-contexts — Mark CSS stacking contexts as layer candidates

## Goal

Identify, during layout, every box that creates a CSS stacking context
(positioned with `z-index ≠ auto`, `opacity < 1`, non-identity `transform`,
`will-change: transform/opacity`, `filter`, `isolation: isolate`, `position:
fixed`, `position: sticky`, root element, etc. per CSS-Position 3 §9 and
CSS-Transforms 1 §5). Tag each such box with a `LayerHint` so the
display-list builder can later split paint into per-layer slices.

This WP does **not** create layers or change paint; it just makes the
layout tree carry the information the compositor work needs. Without it,
later WPs would have to re-derive "what should be its own layer" on every
frame.

## Inputs

- `Starling.Layout.Box.Box` hierarchy.
- `Starling.Css.Values` — `CssTransform`, opacity, position, z-index, etc.
- The CSS cascade already runs and exposes computed styles on each box.

## Outputs

- New `Starling.Layout.Compositor.LayerHint` flags enum:
  `None`, `Promoted`, `WillChange`, `Fixed`, `Sticky`, `Root`,
  `OpacityLessThanOne`, `Transform3D`, `Filter`, `Isolation`.
- `BlockBox` (and `InlineBox` where applicable) gains a `LayerHint Hints`
  property populated during the layout pass.
- A helper `StackingContextResolver` whose `Resolve(box, style) →
  LayerHint` encodes the spec's rules in one place, with unit tests per
  rule.
- A read-only debug walker `EnumerateLayerCandidates(root)` used by
  diagnostics + future WPs.

## Acceptance

- A test page with `<div style="position: relative; z-index: 1">` records
  exactly one `LayerHint.Promoted` candidate beyond the implicit root.
- A test page with `<div style="will-change: transform">` records a
  `LayerHint.WillChange` candidate.
- Nested promotions stack: an `opacity: 0.5` inside a `transform: rotate`
  produces two distinct candidates with `OpacityLessThanOne` and
  `Transform3D` respectively.
- WPT-style fixtures under `tests/Starling.Layout.Tests/StackingContexts/`
  cover each enum value with at least one passing case.
- `dotnet build && dotnet test` green.

## Notes

- The CSS spec rules are well-defined but numerous; copy them verbatim
  (with section refs) into the resolver's source so reviewers can audit.
- `position: sticky` is its own headache because the box's effective
  position depends on scroll. Tag as a candidate unconditionally — the
  compositor decides whether to actually promote at frame time.
- This does not need to subsume `wp:M5-css-02-transform-paint`'s existing
  transform handling; that WP already brackets paint with push/pop. Layers
  will eventually replace those brackets for transformed subtrees, but
  not in this WP.

## Handoff log

- 2026-05-19T17:46Z — created (agent-copilot-claude-opus-4.7)
- 2026-05-20T02:21:12Z — claimed by agent-claude-cody, working on main
