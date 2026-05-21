---
id: wp:M3-23-js-runtime-error-source-positions
title: "Source positions in runtime JS errors — thread JsPosition through to VM throws so uncaught errors carry (at line:col)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

Runtime JS errors (uncaught throws) carried NO source location. A blocker like

```
engine.js: not a function: undefined (method hint: 'prototype')
```

in mcmaster.com's 295 KB minified jQuery/Backbone/YUI bundle was pure guesswork
to locate. The PARSER already has positions (compile errors show `(at line:col)`);
this WP threads positions through to the runtime so thrown TypeErrors include
`(at <line>:<col>)`.

This is a force-multiplier diagnostic, not a full debugger: a sparse,
low-overhead position table covering only the throw-prone opcodes.

## Design

Three layers, all cheap and opt-in (zero cost on the happy path):

1. **Compiler** (`Bytecode/JsCompiler.cs`) — a new `RecordPos(AstNode)` helper
   calls `ChunkBuilder.RecordPosition(line, col)` immediately BEFORE emitting a
   throw-prone opcode, so the recorded byte offset equals the opcode's start
   byte. Wired into:
   - `EmitCall` — `Call`, `CallMethod`, `CallApply`, `CallApplyMethod`
     (including the `super.method()` path); the member load inside a method call
     (`LoadProperty` / `LoadComputed` / `PrivateGet`) records the member
     expression's position so a bad receiver is pinned to `obj.fn`.
   - `EmitNew` — `New`, `NewApply`.
   - `EmitMemberLoad` — `LoadProperty`, `LoadComputed`, `PrivateGet`.
   The recorded position is the AST node's `Start` (1-based line/col).

2. **Chunk** (`Bytecode/Chunk.cs`) — a sparse, immutable
   `IReadOnlyList<(int Offset, int Line, int Col)> Positions`, kept sorted
   ascending by offset (emission order is monotonic). `PositionAt(int ip)` does a
   binary search for the greatest entry with `Offset <= ip` and returns
   `(Line, Col)?` — `null` when nothing was recorded. `ChunkBuilder` accumulates
   entries via `RecordPosition` (coalescing duplicate offsets, last-write-wins)
   and emits the array in `Build`. Empty unless positions were recorded, so
   non-recording chunks pay nothing.

3. **VM** (`Runtime/JsVm.cs`) — a local `AtPos(message)` helper inside
   `RunInner` appends `(at line:col)` using `chunk.PositionAt(ip)`. At a throw
   site `ip` has already advanced past the offending opcode's operand bytes, so
   the nearest preceding recorded entry (the opcode's start offset) is the right
   one. Applied to the three TypeErrors the VM constructs directly:
   `not a function` (Call), `not a function` (CallMethod), `not a constructor`
   (New). No-ops gracefully when no position was recorded.

## Tests

`tests/Starling.Js.Tests/Runtime/JsErrorPositionTests.cs` (6 tests, all green):

- `Method_call_on_missing_property_carries_position` — `var o={}; o.missing();`
  throws with `(at 1:`.
- `Bare_call_to_undefined_global_carries_position` — `nope();` → `(at 1:1)`.
- `New_on_undefined_carries_position` — `new undefined();` → `(at 1:1)`.
- `Call_deep_in_a_function_reports_inner_line` — the throw inside a function body
  reports the body line (3), not the call site (5); proves per-chunk tables.
- `Position_points_at_the_column_of_the_member_expression` — `o.g()` on a line
  with an earlier `o.f()` reports `(at 1:32)`, the failing member's column.
- `Successful_calls_do_not_get_a_position_suffix` — happy path unaffected.

Full `Starling.Js.Tests`: **1347 passing, 1 skipped** (was 1341/1 — +6 new).

## Nullish-member bonus — SKIPPED (noted)

The optional bonus (member access on `null`/`undefined` should throw
`TypeError: Cannot read properties of undefined` instead of returning
`undefined`) was deliberately skipped as too broad for this WP:

- The `Optional` flag on member/call AST nodes is NOT honored in codegen —
  `a?.b` compiles to a plain `LoadProperty`, relying on the current
  nullish-returns-undefined behavior for its short-circuit. There is an existing
  green test `JsOptionalChainingDigitTests.Optional_chain_short_circuits_on_nullish`
  (`var a=null; typeof (a?.b)` → `"undefined"`) that depends on it.
- Making `LoadProperty`/`LoadComputed` throw on nullish would break real optional
  chaining — which the mcmaster bundle uses heavily (`cond?.5:…`, `a?.b`).
- A correct fix needs its own WP implementing real optional-chain short-circuit
  codegen (guard the load with a `JumpIfNullish`) first.

The position-in-errors work (the priority) is complete and stands alone.

## Diagnostic result — positioned mcmaster blocker

With the fix, the render now reports the position:

```
engine.js: Uncaught dynamic script error (…/mcm_93043416e382333b8830bc7dd755e73f.js):
not a function: undefined (method hint: 'prototype') (at 176:202)
```

Bundle line 176, col 202 (Chrome-UA fetch), ~120 chars:

```
…this.add,this)};t.extend(i.prototype,{add:function(e,t){var i=e.cid;return this._views[i]=e,e.model&&(this._indexByM…
```

This is the **Backbone.BabySitter `ChildViewContainer`** module (line 176 is
`!function(e,t){var i=e.ChildViewContainer;…}(t,i)`, MIT, Derick Bailey 2016).
The failing callee at col 202 is the inner `t.extend` — `t` is the module's
second IIFE parameter, expected to be **underscore (`_`)**. Line 171's UMD header
declares `define(["backbone","underscore"],…)`; in the non-AMD global fallback
the module is invoked `}(t,i)` where `i` (underscore) is **undefined** because
underscore is not present as a global. So inner `t` is undefined →
`t.extend` is undefined → the `t.extend(i.prototype, {…})` CallMethod throws.

The `method hint: 'prototype'` is from `_lastLoadName` tracking the most-recent
property load (`i.prototype`, the argument evaluated after the callee); the
actually-undefined value is the `t.extend` callee. Next WP: ensure underscore
(`_`) is available/loaded before Backbone.BabySitter so its UMD fallback binds a
real underscore.
