---
id: "wp:M3-15-js-break-continue-finally"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-breakfinally"
claimed_at: "2026-05-21T00:00:00Z"
completed_at: "2026-05-21T00:45:00Z"
branch: "main"
depends_on:
  - "wp:M3-03-js-compiler"
  - "wp:M3-04-js-vm"
  - "wp:M3-03e-js-switch-statement"
subsystem: "Starling.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#bytecode-ir"
---

# wp:M3-15 — JS: break/continue across an enclosing try…finally

## Goal

Implement abrupt `break` / `continue` completions that exit a loop or
switch across one or more enclosing `try…finally` blocks. Per ECMA-262
§14.15 (TryStatement) every intervening `finally` must run on the way
out — innermost first — before control reaches the loop/switch break or
continue site, and a `finally` that itself completes abruptly
(break/continue/return/throw) overrides the pending completion (§14.15.3
/ §14.7 / §14.13).

Before this WP the compiler hard-threw:
`'break' across an enclosing try/finally is not yet supported`. The
mcmaster.com jQuery bundle hits this and stops.

## Lowering chosen

Mirror the existing `return`-through-finally mechanism (`return` already
runs intervening finalizers via the VM's `DivertReturnThroughFinally`).

- New opcode `Opcode.BranchThroughFinally`, operand `[u8 unwindCount][i16 target]`.
  - `unwindCount = _tryDepth - targetFrame.TryDepthAtEntry` (compile-time:
    the number of open try-frames between the break/continue and its target
    loop/switch). All of those frames are being exited; the ones that carry
    a finalizer run code on the way out.
  - `target` is the i16 forward offset of the loop's break/continue PC,
    measured from the byte after the operand — identical to a plain `Jump`.
    The i16 slot is recorded on the same `Break/ContinuePatches` list as a
    normal `Jump`, so each loop's existing lowering pass patches it
    unchanged (forward via `PatchJump`, backward — continue targets — via
    `PatchBackwardJump`; both write at the i16 position with delta measured
    from `pos+2`, consistent with the VM read).
- Compiler (`EmitBreakOrContinue`): when `crossedTryFrames > 0`, emit
  `BranchThroughFinally` instead of throwing; else emit a plain `Jump`
  (unchanged fast path).
- VM (`DivertBranchThroughFinally`): pops up to `unwindCount` try-frames.
  The first popped frame still in try/catch phase with a finalizer suspends
  the unwind — its finalizer runs as a `PendingCompletion.Break` carrying
  the target PC and the remaining count; `EndFinally` re-enters the helper
  for the rest. Frames without a finalizer are discarded. When no further
  finalizer remains, `ip = targetPc`.
- Completion-value override (§14.15.3): handled for free — a finalizer that
  itself breaks/continues/returns/throws re-drives the try-stack via its
  own opcodes and never reaches the `EndFinally` that would resume the
  pending Break, so the new completion wins.

Byte/opcode flow for `for(...){ try{ break; } finally{...} }`:
`... BranchThroughFinally 01 <→loopExit> ...` → VM diverts to the
finalizer (frame.Pending=Break, target=loopExit, remaining=0) → finalizer
body → `EndFinally` → remaining 0, no more finalizers → `ip = loopExit`.

## Outputs

- `src/Starling.Js/Bytecode/Opcode.cs` — `BranchThroughFinally` opcode.
- `src/Starling.Js/Bytecode/JsCompiler.cs` — `EmitBreakOrContinue`
  emits `BranchThroughFinally` for the cross-finally case.
- `src/Starling.Js/Bytecode/Disassembler.cs` — decode the new opcode's
  `[u8][i16]` operand so disassembly stays correct.
- `src/Starling.Js/Runtime/JsVm.cs` — `PendingCompletion.Break`,
  `TryFrame.PendingTargetPc` / `PendingUnwindRemaining`,
  `BranchThroughFinally` handler, `DivertBranchThroughFinally` helper,
  `EndFinally` Break-resume case.
- `tests/Starling.Js.Tests/Runtime/JsBreakContinueFinallyTests.cs` — 9
  spec tests.

## Acceptance

- Repros 1–5 from the task brief pass (break/continue/labeled/nested/return).
- Finalizer-overrides-pending-completion cases pass.
- `break` across finally inside a switch.
- `dotnet build && dotnet test` green; full `Starling.Js.Tests` 1304
  passing / 1 skipped (was 1295/1; +9 new), Test262 subset green.
- mcmaster render no longer emits the try/finally compiler error.

## Handoff log

- 2026-05-21 — created + implemented by agent-claude-cody-breakfinally.
  9 tests green; full Js.Tests 1304/1 skipped; Test262 subset green.
  mcmaster render gets PAST the try/finally blocker; next `engine.js`
  error is an opaque runtime `JsThrow` (`[object Object]`) from the
  ScriptCombiner bundle — a separate WP (likely a missing browser/DOM API
  the now-executing jQuery code calls). The thrown value's `.message`
  isn't surfaced by `DynamicScriptRunner`'s log; improving that log to
  print Error `.name`/`.message` would make the next diagnosis faster.
