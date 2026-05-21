---
id: wp:M3-21-js-named-function-expression-self-binding
title: "Named function expression self-binding (§15.2.5) — bind the expression's own name inside its body"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

After M3-20 fixed `arguments`, mcmaster.com's combined jQuery + Backbone + YUI
bundle (`mcm_93043416…`) advanced into jQuery core and died:

```
engine.js: not a constructor: undefined (new hint: 'init')
```

jQuery's factory is a **named function expression** that refers to its own name:

```js
var b = function e(t,n){ return new e.fn.init(t,n); };   // `e` used INSIDE the body
b.fn = b.prototype = { init: function(s){ … } };
b.fn.init.prototype = b.fn;
```

`e` (the function-expression's own name) was undefined inside the body, so
`e.fn.init` evaluated to `undefined` and `new undefined()` threw.

## Diagnosis

Minimal repro:

```js
var f = function g(){ return typeof g; }; f();   // returned "undefined", MUST be "function"
```

Per §15.2.5 InstantiateOrdinaryFunctionExpression, a named function
*expression* `function BindingIdentifier (…) {…}` binds its own name (as an
immutable binding) in a dedicated function environment so the body can refer to
itself (recursion / self-reference). Starling never created that binding, so the
name resolved as a free identifier → `LoadGlobal` → `undefined`.

This is **not** jQuery-specific: every named-FE recursion / self-reference idiom
was broken. Function *declarations* were unaffected (they bind their name in the
enclosing scope via `HoistFunctionDeclarations`, which already worked).

## Fix (Starling primitive)

Mirrors the M3-20 `arguments` mechanism exactly.

1. **Opcode** (`Opcode.cs`): new `BindCallee slot` (u8). At a named-FE body's
   entry it binds the expression's own name to the executing function instance.

2. **VM** (`JsVm.cs`): the `BindCallee` handler writes `currentFunction` (the
   callee already threaded through `Run`/`RunInner`) into the slot — through its
   `Cell` when the slot was pre-boxed (a nested closure captures the name),
   otherwise directly. Because generators/async run their body via the same
   `Run(..., currentFunction: fnCopy, …)` path (`StartGeneratorBody` /
   `StartAsyncBody`), the binding works there too.

3. **Compiler** (`JsCompiler.cs`): `EmitFunctionExpression` calls a new
   `MaybeBindSelfName(name)` for a NON-arrow function expression with a name,
   AFTER `BindFunctionParameters`, `PreallocateCapturedVarBindings`,
   `HoistFunctionDeclarations`, and `MaybeBindArguments`. It reserves a local
   slot, registers the name in the body scope (so inner references + nested-
   closure capture resolve to it), and emits `BindCallee slot`. If the name is
   captured by a nested closure (incl. arrow), the slot is boxed into a `Cell`
   (`InitCellLocal`) first. The disassembler learns the new single-u8 op.

### Spec correctness

- **Only NON-arrow function expressions with a name** get the binding. Arrows
  never have an own name; function *declarations* bind in the enclosing scope
  and are not touched here (no double-bind).
- **Shadowing (§10.2.11):** `MaybeBindSelfName` first checks whether the name is
  already in any scope (a param, body `var`, or hoisted inner function of the
  same name). If so it does nothing — the user's binding wins. Because it runs
  *after* params / var pre-allocation / function hoisting, those always shadow.
- **Nested capture:** the captured-name set computed by `CaptureAnalysis.Compute`
  already includes the self-name when a nested function/arrow references it, so
  `IsNameCaptured(name)` is true and the slot becomes a shared `Cell`.
- **Identity:** the binding is the function instance actually executing
  (`currentFunction`), so `g === f` inside `var f = function g(){…}`.

## Tests

`tests/Starling.Js.Tests/Runtime/JsNamedFunctionExpressionTests.cs` (14
`[SpecFact]`, §15.2.5), reproduce-first (8 red before, all green after):

- `typeof g` repro → "function"; recursion (`fac` factorial → 120)
- the jQuery-core shape (`var j = function J(s){ return new J.fn.init(s); }; …`)
  constructs through the self-name → `j('hi').s === "hi"`
- self-name identity (`g === f`)
- param / body-`var` / inner-function-declaration shadowing (§10.2.11)
- nested arrow captures the self-name; nested arrow recursion; nested ordinary
  function captures it
- generator function expression binds its own name
- no regression: anonymous FE, arrow, and self-name invisible outside the FE

The 6 non-self-binding cases (shadowing + no-regression) pass with or without
the fix; the 8 self-binding cases were red before.

## Verification

- `dotnet build src/Starling.Js` clean (0 warnings).
- Full `Starling.Js.Tests`: **1341 passing, 1 skipped, 0 failed** (baseline 1327
  + 14 new), no regressions.
- Render check: `starling render https://www.mcmaster.com/products/abrading-polishing/`
  now executes past the jQuery-core blocker. The `not a constructor: undefined
  (new hint: 'init')` error is gone; the **next** mcmaster `engine.js` error is:
  `not a function: undefined (method hint: 'createHTMLDocument')`
  (`document.implementation.createHTMLDocument` — a DOM-API gap, out of scope).
