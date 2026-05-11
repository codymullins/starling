---
id: "wp:M3-03-js-compiler"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T18:00:00Z"
branch: "wp-M3-03-js-compiler"
completed_at: "2026-05-11T18:35:00Z"
depends_on:
  - "wp:M3-02b-js-parser-statements"
blocks:
  - "wp:M3-04-js-vm"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#bytecode-ir"
  - "browser-plan/14_AGENT_TASKS.md#wpm3-03-js-compiler"
---

# wp:M3-03 — JS bytecode compiler

## Goal
Walk a parsed JS `Program` AST and emit a `Chunk` of bytecode that the
M3-04 VM will execute. This slice covers the bytecode IR, the
compiler, and a disassembler used by tests.

## Outputs
- `src/Tessera.Js/Bytecode/Opcode.cs` — enum of opcodes.
- `src/Tessera.Js/Bytecode/Chunk.cs` — bytecode + constants + line table.
- `src/Tessera.Js/Bytecode/Disassembler.cs` — text dump for tests.
- `src/Tessera.Js/Bytecode/JsCompiler.cs` — walker emitting Chunk.

## Acceptance
- Plan §M3-03 calls for "snapshot tests on 30 hand-picked source files
  matching expected bytecode dumps". This slice lands ≥ 15 hand-picked
  cases covering: literals, binary ops, variable declarations, if/else,
  while, return, calls. The next slice (M3-04 VM) drives the bytecode
  through execution.

## Out of scope
- VM execution (wp:M3-04).
- Closures + upvalue capture (M3-04+).
- Inline caching, shape transitions (M7-01).

## Handoff log
- 2026-05-11T18:00Z — created and claimed atomically by agent-claude-cody.
  Sequenced after M3-02b lands (statements AST is the input).
