---
id: "wp:M3-69-js262-parser-early-errors-batch"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main 96bd81c (also js262-phase0-2; pushed origin/js-262)"
impact: "225 of 360 parse-error tests fixed; language 90.89%->91.44% (+0.55pp), ZERO category regressions. Added: return-outside-fn, coalesce-without-parens, optional-chain restrictions, await/yield in params, arrow ASI+dup-params, accessor arity, labelled-fn-in-iteration, for-of/in head+dstr, cover-init names, static-block restrictions, catch-binding conflicts. Skipped (noted): super-in-function-params, module-code early errors, arguments-in-static-block (each a distinct larger mechanism)."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-69 — JS parser: batch of missing parse-phase early errors

## Why (evidence)
~360 `expected parse error, parsed OK` failures — the diffuse parser-strictness
tail. No single dominant rule, but several related clusters worth a batch:
- `statements/return` (20) — `return` outside a function body.
- `expressions/async-arrow-function` (21) + `async-generator` (12) — `await` as a
  binding/param name in async contexts; async-arrow param rules.
- `expressions/optional-chaining` (12) — `new a?.b()`, `` a?.b`tpl` `` (tagged
  template on an optional chain) are SyntaxErrors.
- `expressions/coalesce` (8) — `??` mixed with `||`/`&&` without parens.
- `statements/for-in|for` labelled-fn-stmt; `for-of`/`for-in` `dstr` (18) LHS rules.
- `statements/class` / `object` (37) — assorted (e.g. setter must have exactly
  one non-rest param; static-init `arguments`).
- `module-code` (33) — module early errors (escaped `export` specifier, etc.) —
  attempt if tractable.

## Scope
Driven by the failing tests under
`/Users/cody/code/starling/testdata/test262/` (grep
`testdata/test262/results/failures.p68.txt` for `expected parse error, parsed OK`),
add the missing parse-phase early errors in `src/Starling.Js/Parse/` so each
invalid form throws a SyntaxError at parse time. Maximize coverage of the
clusters above; don't regress valid syntax. Skip anything that needs a runtime
or a large feature (note it).

## Acceptance
- Maintainer-run full `language` sweep: `expected parse error, parsed OK` drops
  meaningfully (was ~360); report before/after.
- Focused parse unit tests for each rule added (invalid → SyntaxError; a valid
  neighbor still parses).
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
