---
id: "wp:M3-04d-js-new-and-this"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T19:25:00Z"
branch: "wp-M3-04d-js-new-and-this"
depends_on:
  - "wp:M3-04b-js-functions"
blocks:
  - "wp:M3-05-js-intrinsics"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#vm"
---

# wp:M3-04d — `new` + `this`

## Goal
Make `new Foo(a, b)` work and give `this` a real per-frame binding.
After this lands, idiomatic constructor patterns work:

```js
function Point(x, y) { this.x = x; this.y = y; }
Point.prototype.add = function(o) { return new Point(this.x + o.x, this.y + o.y); };
// (prototype chain follows in M3-05.)
```

For this slice we cover the no-prototype version:

```js
function Point(x, y) { this.x = x; this.y = y; }
var p = new Point(3, 4);  // → { x: 3, y: 4 }
p.x + p.y;                  // → 7
```

## Outputs
- Compiler: `ThisExpression` compiles to `LoadThis` (was a placeholder
  `LoadGlobal "this"`).
- Opcode: `LoadThis`.
- VM: per-frame `this` value; `Call` accepts it (default `undefined`);
  `New` opcode creates a fresh object and binds it.
- Runtime: `JsFunction` callable as constructor.

## Out of scope (queued)

- `prototype` chain (wp:M3-05).
- Class declarations (wp:M3-02c).
- Arrow functions' lexical `this` (wp:M3-04c).

## Acceptance
- 8+ unit tests including: bare `this` in script context, `this` inside
  a regular function call (undefined in this slice — strict mode), `this`
  inside `new`-invoked function points to the new object, constructor
  with explicit non-object return uses `this`, constructor with explicit
  object return uses that object, property assignment via `this.x = …`,
  read via `this.x`, multi-arg constructor.

## Handoff log
- 2026-05-11T19:25Z — created and claimed atomically by agent-claude-cody.
