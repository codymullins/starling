---
id: "wp:M3-81-js262-eval-initializer-arguments-early-errors"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-81 — JS: direct-eval `arguments` early errors inside initializers

> Repurposed. The original WP-81 ("`arguments` in param defaults", ~42
> `ReferenceError: arguments is not defined`) was made obsolete by WP-80's
> mapped-arguments / MakeArguments ordering — that cluster is now **2** fails.
> Retarget the slot at the next-biggest coherent cluster.

## Why (evidence)
~160 coherent failures from one spec rule:
- **`eval-code/direct` — 136** "Expected a SyntaxError to be thrown but no
  exception was thrown". Generated tests where a **direct eval inside a
  parameter initializer** contains/declares `arguments`, e.g.
  `{ async *f(p = eval-call("var arguments = 'param'"), arguments) {} }` →
  `assert.throws(SyntaxError, o.f)`. Spans every callable form (func decl/expr,
  gen, async-gen, meth, gen-meth, arrow), preceding/following param named
  `arguments`, and declare-vs-declare-and-assign variants.
- **`statements|expressions/class/elements` — 24** "Expected a SyntaxError but
  got a ReferenceError": `*-direct-eval-err-contains-arguments*`, e.g.
  `class C { x = eval-call('() => arguments;'); }` → must `SyntaxError`.

Spec: **Additional Early Error Rules for Eval Inside Initializer**
(sec-performeval-rules-in-initializer). When a direct eval occurs inside a
**function parameter default initializer** or a **class field/static
initializer**, `PerformEval` applies: *ScriptBody : StatementList — It is a
Syntax Error if `ContainsArguments` of StatementList is true.* The engine
currently parses/runs the eval body normally (no early error), so it throws a
runtime `ReferenceError` or nothing instead of `SyntaxError`.

## Scope
- Plumb an "inside-initializer" flag into the direct-eval path so `PerformEval`
  knows it was called from (a) a parameter default initializer or (b) a class
  field/static-block/static initializer.
- When that flag is set, run the `ContainsArguments` static-semantics check over
  the parsed eval program and throw a **SyntaxError** (parse-time / pre-execution
  early error) if it is true. `ContainsArguments` returns true for any
  `IdentifierReference`/`BindingIdentifier` whose StringValue is `arguments`, and
  recurses per the grammar (do **not** descend into nested ordinary function
  bodies that introduce their own `arguments`, per the production rules).
- Do not change ordinary direct/indirect eval (no initializer context) or
  arrow-inherited `arguments` semantics outside eval.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=eval-code/direct` arguments-SyntaxError failures drop
  sharply (was 136) and `class/elements` `contains-arguments` (was 24) clear;
  report before/after; regression-scan every category (esp. that ordinary eval
  and `arguments`-in-eval *outside* initializers are unaffected).
- Unit tests: a direct eval of `"var arguments"` inside a param default →
  `SyntaxError`; `class C { x = <direct-eval of "arguments">; }; new C()` →
  `SyntaxError`; control: a top-level direct eval of `"arguments"` outside any
  initializer still throws `ReferenceError` (not SyntaxError); an arrow in the
  eval body inside an initializer containing `arguments` → `SyntaxError`.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.

## Done
Landed in `7f838f1 fix(js): direct-eval ContainsArguments early errors inside initializers`.
"Expected SyntaxError no throw" cluster 151 → 25 (−126), `class/elements`
contains-arguments 25 → 1 (−24). `eval-code` category 51.3% → 81.7%. No
adjacent regressions. Implementation: `EnterInitializer`/`ExitInitializer`
opcodes brace the region, a frame-local `initDepth` propagates through arrows
via a `JsFunction.InInitializer` closure flag, and `PerformDirectEval` runs a
new `ContainsArgumentsInEvalBody` (matches `BindingIdentifier` too, per spec)
that throws SyntaxError when the flag is set. Arrows only enter
initializer-context when the arrow's own param list binds `arguments`. Class
field/static initializers always enter it.
