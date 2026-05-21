---
id: wp:M3-29-js-u16-local-slots
title: "JS bytecode: widen local-slot operands to u16 (fix >255-locals slot aliasing)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

Fix an engine bug where a function with more than 255 local slots produced
corrupted bytecode: local-slot opcodes encoded their slot index in a single
**u8** operand, so any slot index ≥ 256 silently wrapped modulo 256 and
aliased an unrelated low slot in the same function.

Surfaced as an internal, uncatchable-looking failure:

```
Script compile/run failure: Unable to cast object of type
'Starling.Js.Runtime.JsFunction' to type 'Starling.Js.Runtime.Cell'.
```

(at `JsVm.StoreCellLocal` — `var cell = (Cell)locals[slot].AsObject;`).

This breaks Google Tag Manager / gtag (whose top-level IIFE compiles to a
single chunk with **2126 locals**), and the same root cause affects TodoMVC,
webpack/bundler output, and other large minified scripts. The netclaw.dev
render loads gtag and hit this.

## Root cause (precisely)

`ChunkBuilder.ReserveLocal()` hands out monotonically increasing slot indices
with no upper bound, but every local-slot opcode used a u8 operand:

- `LoadLocal`, `StoreLocal`, `DeclareLocal`
- `InitCellLocal`, `LoadCellLocal`, `StoreCellLocal`, `PromoteParamCell`,
  `RefreshLetBinding`
- `MakeArguments`, `BindCallee`
- the captured-local upvalue push in `EmitFunctionConstructor`
  (`LoadLocal (byte)u.Index`, where `u.Index` is a *parent* local slot)

When a chunk has >255 locals, slot `N` (N ≥ 256) was emitted as `(byte)N`
= `N mod 256`, colliding with the real low slot `N mod 256`. In gtag's IIFE a
high **captured** slot (cell-backed) and a low **plain** slot collided on the
same byte operand: a plain `StoreLocal` wrote a raw `JsFunction` into the byte
slot that another path treated as a `Cell`, and the next `StoreCellLocal` on
that operand cast the raw function to `Cell` → `InvalidCastException`.

Diagnosis path: instrumented `StoreCellLocal`/`LoadCellLocal` to dump the
chunk disassembly + slot on the cast; ran gtag through a compile+VM harness;
saw plain `StoreLocal 37` clobbering a cell slot from
`HoistFunctionDeclarations → EmitStoreLocalSlot`; tracked the per-builder
plain-vs-cell op set to a conflict whose `arg` (a byte) collided across two
real slots; confirmed `LocalCount=2126 > 255` for the failing chunk.

## Fix

Widen all **local-slot** operands from u8 to u16 (little-endian), supporting up
to 65535 locals per function. Upvalue indices (`LoadUpvalue`, `StoreUpvalue`,
`LoadUpvalueCell`) and call arg counts (`Call`/`CallMethod`/`New`) stay u8 —
`MakeClosure` already caps captures at 255 with an explicit error.

- `src/Starling.Js/Bytecode/Chunk.cs` — new `ChunkBuilder.EmitSlot(op, slot)`
  that writes `op + u16 slot` and throws a clear
  `InvalidOperationException` if a function ever exceeds 65535 locals (instead
  of silently wrapping again).
- `src/Starling.Js/Bytecode/JsCompiler.cs` +
  `JsCompiler.Classes.cs` — every local-slot emit (`_b.Emit(Opcode.X, (byte)s)`)
  switched to `_b.EmitSlot(Opcode.X, s)` for the 10 slot opcodes above
  (66 sites). Non-slot emits (upvalue ops, argc, constant indices) unchanged.
- `src/Starling.Js/Runtime/JsVm.cs` — the 10 slot opcodes now `ReadU16()` their
  operand.
- `src/Starling.Js/Bytecode/Disassembler.cs` — moved the slot opcodes into a
  u16-operand decode group; upvalue/call opcodes stay u8.
- `src/Starling.Js/Bytecode/Opcode.cs` — updated the operand-width doc comments
  (`[u8 slot]` → `[u16 slot]`) for the affected opcodes.

The fix makes the slot operand correctly address every reserved slot, so a
captured slot is always a distinct byte-pair from any plain slot — no aliasing,
no clobbered `Cell`.

## Tests added

`tests/Starling.Js.Tests/Runtime/JsManyLocalsSlotTests.cs` — 3 tests:

| Test | Pins |
|---|---|
| `Captured_local_survives_when_function_has_over_256_slots` | the exact gtag failure: a captured var (cell) at a low slot read by a nested closure, with ~600 later locals crossing slot 256; closure observes the write (`141`), no cast |
| `Captured_param_survives_over_256_slots` | a captured parameter (`PromoteParamCell`) at a low slot survives >256 locals |
| `Plain_locals_beyond_slot_255_are_distinct` | high plain slots don't alias each other |

Reproduce-first verified: with the u8 operand (fix reverted) the first two
tests throw `InvalidCastException`; after the fix all pass.

## Results

- `Starling.Js.Tests`: **1430 passed, 1 skipped** (baseline 1427+1 → +3 new).
- `Starling.Js.Test262.Tests`, `Starling.Bindings.Tests` (189),
  `Starling.Engine.Tests` (128): all green.
- netclaw.dev render: the `Unable to cast … Cell` error from gtag is **gone**
  (0 occurrences). Remaining `engine.js` warnings are unrelated third-party
  JS-level errors (out of scope — one fix only):
  - `googletagmanager.com/gtag/js`: `not a function: undefined (callee hint:
    'Nf')` — a missing browser API stub, not the engine.
  - `static.cloudflareinsights.com/beacon.min.js`: `Uncaught script error
    [object Object]` (pre-existing).

## Handoff log

- **2026-05-21** agent-claude-cody: root-caused, fixed, tested; complete.
