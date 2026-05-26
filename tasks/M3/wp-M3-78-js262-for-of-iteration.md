---
id: "wp:M3-78-js262-for-of-iteration"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-78 — JS: for-of / for-in iteration semantics

## Why (evidence)
`statements/for-of` 127 + `statements/for-in` 44 — the biggest remaining
coherent cluster. for-of signatures: 46 `SameValue` (iteration values/order/loop
var), 18 `Expected a TypeError` (iterator-protocol edge cases), 12
`Expected a ReferenceError` (per-iteration binding TDZ), 8 `TestError`, 6 niche
resizable-buffer (`method hint: 'resize'` — skip). Triage the cluster, find the
shared root causes, fix the biggest.

## Likely root causes to investigate (driven by the tests under
`/Users/cody/code/starling/testdata/test262/test/language/statements/for-of/` and
`.../for-in/`)
- **Per-iteration lexical binding**: `for (let x of it)` creates a FRESH binding
  each iteration (closures in the body capture distinct `x`); TDZ before the
  loop var is bound.
- **IteratorClose on abrupt completion** (break/throw/return inside the body
  calls the iterator's `return()`); value/order of the close.
- **Iterator protocol type checks** → TypeError (iterator result not an object,
  `next`/`return` not callable, etc.).
- **for-in** enumeration order / shadowed + deleted keys / prototype-chain dedup.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=for-of` (+ for-in) failures drop; report before/after;
  regression-scan every category.
- Unit tests for the root causes fixed (fresh per-iteration binding + closure
  capture; IteratorClose on break/throw; iterator-protocol TypeErrors).
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.
