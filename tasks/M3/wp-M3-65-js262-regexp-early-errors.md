---
id: "wp:M3-65-js262-regexp-early-errors"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
integrated: "main ea7ee9e (also js262-phase0-2; pushed origin/js-262)"
finding: "RegexParser was too permissive; added §22.2.1 early errors (dup named groups w/ ES2025 cross-alternative nuance, malformed group names, \\k dangling refs, min>max quantifiers, lookaround quantifiers, u/v strictness). 54 tests. Out of scope: inline flag modifiers (?i:), v-flag set ops, astral property-escapes (all pre-existing)."
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-65 — JS: RegExp pattern early-errors (invalid patterns must be SyntaxError)

## Why (evidence)
The biggest concentrated `expected parse error, parsed OK` sub-cluster:
**114** in `literals/regexp` (60 `named-groups`, 54 other). The engine's regex
parser accepts invalid patterns that the spec requires to be early SyntaxErrors
at parse time — e.g. duplicate group names `/(?<a>)(?<a>)/`, malformed group
names, invalid `\k<name>` backrefs, out-of-order quantifier bounds `/a{2,1}/`,
lone `\` etc.

## Scope
- In `src/Starling.Js/RegExp/RegexParser.cs` (+ where regex literals are
  validated during JS parsing), enforce the §22.2.1 early errors so an invalid
  pattern throws (surfaced as a JS SyntaxError at parse time, matching how the
  harness's negative `phase: parse` tests expect). Concentrate on: duplicate
  named-capture group names, invalid GroupName syntax, undefined `\k<name>`
  references, invalid quantifier ranges (min>max), and other pattern-level
  early errors the failing tests exercise.
- Read the failing tests under
  `/Users/cody/code/starling/testdata/test262/test/language/literals/regexp/`
  (esp. `named-groups/`) to enumerate the exact rules.
- Do NOT regress valid patterns (the existing RegExp tests must stay green).

## Acceptance
- Maintainer-run `STARLING_TEST262_FILTER=literals/regexp` failures drop sharply
  (was 114 parsed-OK); report before/after.
- Focused unit tests: each early-error class rejected; representative valid
  patterns still compile.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- NOTE: worktree base may predate merged work; keep isolated; integrator reconciles.
