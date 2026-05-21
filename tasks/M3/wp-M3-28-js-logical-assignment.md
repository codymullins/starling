---
id: wp:M3-28-js-logical-assignment
title: "JS compiler: logical assignment operators (??=, ||=, &&=)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

Support the three ES2021 logical assignment operators `||=`, `&&=`, and `??=`
(ECMAScript §13.15.2) in the JS bytecode compiler. Previously the compiler's
compound-assignment path routed them through `CompoundOpToBinaryOpcode`, which
only knows the arithmetic/bitwise compound ops, so any use threw
`NotSupportedException("compound op '??='")` (and `'||='` / `'&&='`).

This blocked netclaw.dev's own Astro module (`_astro/page.BIhdncjw.js`), which
uses `??=` and failed at compile time with `Module compile/run failure … compound op '??='`.

## Correct semantics (§13.15.2)

These are NOT `a = a op b`. They short-circuit, and the target reference is
evaluated **exactly once**:

- `a ||= b` — evaluate `a`; if truthy, result is `a`, `b` is NOT evaluated, NO
  assignment. Else assign `a = b`, result is `b`.
- `a &&= b` — if `a` is falsy, result is `a`, no eval/assign. Else `a = b`.
- `a ??= b` — if `a` is non-nullish (not null/undefined), result is `a`, no
  eval/assign. Else `a = b`.
- Result of the whole expression = the final value of the target.

## What was done

`src/Starling.Js/Bytecode/JsCompiler.cs`:

- Added `IsLogicalAssignOp(op)` (matches `||=`/`&&=`/`??=`) and
  `LogicalAssignShortCircuitJump(op)` mapping each op to the conditional jump
  that detects its short-circuit (no-assign) case:
  - `||=` → `JumpIfTrue` (short-circuit when truthy)
  - `&&=` → `JumpIfFalse` (short-circuit when falsy)
  - `??=` → `JumpIfNotNullish` (short-circuit when non-nullish)
  All three jumps POP their test operand, so the lowering Dups the current
  value first (same pattern as `EmitLogical`).
- In `EmitAssignment`, added a dispatch immediately after the destructuring
  (`IsPattern`) check and BEFORE the generic compound paths:
  `if (IsLogicalAssignOp(a.Op)) { EmitLogicalAssignment(a); return; }`.
- Added `EmitLogicalAssignment` handling all three target forms:
  **identifier** (local / upvalue / global), **member `obj.name`**, and
  **computed `obj[key]`**.
- `super.x ??= …`, computed super, and `this.#x ??= …` (private field) are
  deferred with clear `NotSupportedException` messages — netclaw doesn't need
  them, and the plain `=`/compound super paths use dedicated opcodes.

## Lowering

All forms share the shape: evaluate the reference once, read current value, Dup
+ conditional jump (short-circuit keeps current value as result), else Pop the
current value, evaluate RHS, store (re-using the existing store opcodes),
leaving the stored value. At the merge point exactly one value is on the stack.

### Identifier (local — upvalue/global mirror it with their store opcodes)
```
LoadLocal(slot)            ; [cur]
Dup                        ; [cur, cur]
<shortJump> ──────┐        ; pops one cur → short-circuit leaves [cur]
Pop               │        ; assign path: drop cur → []
<rhs>             │        ; [rhs]
Dup               │        ; [rhs, rhs]  (keep result after store pops)
StoreLocal(slot)  │        ; [rhs]
   ◄──────────────┘  PatchJump → merge: [cur] or [rhs]
```
Identifier writes route through `EmitStoreLocalSlot` (cell-aware),
`StoreUpvalue`, or `StoreGlobal` exactly like the plain `=` case, so captured
locals/upvalues write back through the shared `Cell`. The local/upvalue/global
store opcodes do NOT re-push, hence the `Dup` before the store.

### Member `obj.name`
```
<obj>                      ; [obj]
Dup                        ; [obj, obj]
LoadProperty(name)         ; [obj, cur]
Dup                        ; [obj, cur, cur]
<shortJump>                ; pops one cur
  ; assign path  [obj, cur]: Pop, <rhs>, StoreProperty(name) → [rhs]  (re-pushes)
  ; Jump end
  ; short path   [obj, cur]: store cur to temp local, Pop obj, reload temp → [cur]
```
`StoreProperty` pops `[obj, rhs]` and **re-pushes** rhs, so no extra Dup is
needed on the assign path. The base `obj` is evaluated once (Dup'd for the
read) and reused for the write. On the short-circuit path the dup'd base sits
under `cur`; a bump-allocated temp local (`ReserveLocal`) reorders the stack
since the VM has no swap-N opcode.

### Computed `obj[key]`
```
<obj>, <key>               ; [obj, key]
Dup2                       ; [obj, key, obj, key]   (eval base+key once)
LoadComputed               ; [obj, key, cur]
Dup                        ; [obj, key, cur, cur]
<shortJump>                ; pops one cur
  ; assign path [obj, key, cur]: Pop, <rhs>, StoreComputed → [rhs]  (re-pushes)
  ; Jump end
  ; short path  [obj, key, cur]: store cur→temp, Pop key, Pop obj, reload→[cur]
```
`Dup2` evaluates both base and key exactly once (matching the `obj[k]++`
lowering in `EmitUpdate`); `StoreComputed` pops `[obj, key, rhs]` and re-pushes
rhs.

## Tests added

`tests/Starling.Js.Tests/Runtime/JsLogicalAssignmentTests.cs` — 29 tests, all
tagged `[Spec("ecma262", "…#sec-assignment-operators", "13.15.2")]` + `[SpecFact]`:

| Area | Tests |
|---|---|
| `||=` identifier | falsy→assign, truthy→keep, result-is-final-value |
| `&&=` identifier | truthy→assign, falsy→keep |
| `??=` identifier | undefined→assign, null→assign, 0→keep, false→keep |
| short-circuit proof | `x ||= (n++,9)` etc. — RHS NOT run when short-circuited (`&&=`, `??=`, and `??=`-runs-when-nullish) |
| member `obj.x` | missing→assign, existing→keep, falsy→assign (`||=`), truthy→assign (`&&=`), result-is-final, base eval'd once on assign AND on short-circuit |
| computed `obj[k]` | missing→assign, falsy→assign, truthy→assign, key eval'd once (assign + short-circuit), RHS not run on short-circuit |
| global / chained | `g ||= 11`, right-assoc `a ??= b ??= 5` |
| closure | upvalue target writes through the shared cell |

## Results

- `Starling.Js.Tests`: **1427 passed, 1 skipped** (baseline 1398+1 → +29 new).
- netclaw.dev render (`dotnet run --project src/Starling.Headless -- render
  https://netclaw.dev …`): the `_astro/page.BIhdncjw.js` `compound op '??='`
  failure is **gone**; the page renders to a PNG.
- Remaining netclaw `engine.js` warnings are unrelated third-party scripts (out
  of scope — one feature only):
  - `googletagmanager.com/gtag/js`: `Unable to cast … JsFunction to … Cell`
    (a separate capture/cell compiler gap surfaced in GTM's own code; pre-fix
    this script failed earlier with `not a function … 'getName'`).
  - `static.cloudflareinsights.com/beacon.min.js`: `Uncaught script error
    [object Object]` (pre-existing before this fix).

## Handoff log

- **2026-05-21** agent-claude-cody: implemented and tested; complete.
