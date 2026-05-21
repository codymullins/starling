---
id: wp:M3-14-regexp-replace-delegation
title: "RegExp/String replace: spec-correct exec delegation + NPCG so core-js leaves native @@replace in place"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

mcmaster.com's first bundle is core-js 3.0.1 + webpack runtime. When it ran,
it recursed infinitely through `String.prototype.replace` ->
`RegExp.prototype[@@replace]` and exhausted the stack (surfaced as a catchable
`RangeError` after `wp:M3-13`).

Root cause: core-js feature-detects Starling's native RegExp/replace as
"broken" and installs its own `@@replace` polyfill, which recurses against
Starling's native `String.prototype.replace` (which delegates back to
`Symbol.replace`, now the polyfill). The fix is **not** to fix core-js -- it is
to make Starling's native behavior spec-correct so core-js's detects pass and
it no-ops (leaves the native methods in place).

## Which core-js detects were failing

From `core-js@3.0.1/internals/fix-regexp-well-known-symbol-logic.js` and
`internals/regexp-exec.js`. core-js redefines `String.prototype.replace` +
`RegExp.prototype[@@replace]` if **any** of these fail; it patches
`RegExp.prototype.exec` if `PATCH` is true:

1. **`NPCG_INCLUDED`** (`/()??/.exec('')[1] !== undefined`) -- Starling returned
   the empty string `""` for a non-participating capture group instead of
   `undefined`. -> `PATCH = true`, exec gets wrapped, cascade begins.
2. **`DELEGATES_TO_SYMBOL`** (`''.replace(O)` where `O[Symbol.replace]` is
   callable) -- Starling's `String.prototype.replace` only delegated to
   `@@replace` when the argument was a genuine `RegExp`, not any object.
3. **`DELEGATES_TO_EXEC`** (`re[Symbol.replace]('')` must call a monkeypatched
   `re.exec`) -- Starling's `RegExp.prototype[@@replace]` matched internally via
   `re.Compiled.Exec(...)` and never read/called the receiver's `exec`.
4. **`REPLACE_SUPPORTS_NAMED_GROUPS`** (`''.replace(re,'$<a>')` where `re.exec`
   returns `{groups:{a:'7'}}`) -- follows from (3): `$<name>` was resolved from
   the compiled pattern's named-capture table, not from the exec result's
   `groups` object.

## What was changed (Starling native behavior)

### 1. Pike VM capture slots start at -1 (`src/Starling.Js/RegExp/RegexPikeVm.cs`)
`ExecPike` allocated the initial slot array with `new int[slotCount]`, which
C# zero-initializes -- so a group that never participated read back as the span
`(0,0)` = `""`. The slow recursive matcher already initialized to `-1` (=
"not captured"). Fixed `ExecPike` to initialize all slots to `-1`. This makes
non-participating groups read as `undefined` per **section 22.2.7.2
RegExpBuiltinExec** (captures default to `undefined`). Fixes NPCG and several
alternation/optional cases (`/(a)?/`, `/(a)|(b)/`, etc.).

### 2. String.prototype.replace delegates to any @@replace (`StringCtor.cs`)
Per **section 22.1.3.19 / 22.1.3.20**, when `searchValue` is not undefined/null
and exposes a callable `[Symbol.replace]`, delegate to it -- for **any** object,
not just RegExp instances. The `replaceAll` non-global-RegExp guard is kept and
correctly scoped to genuine RegExps.

### 3. RegExp.prototype[@@replace] goes through RegExpExec (`RegExpCtor.cs`)
Rewrote `SymbolReplace` to follow **section 22.2.6.11** precisely:
- New `RegExpExec(realm, R, S)` helper implements **section 22.2.7.1**: read
  `R.exec`, call it if callable (requiring an Object-or-null result), else fall
  back to the built-in `exec`.
- `@@replace` now reads `global`, sets `lastIndex`, collects results via
  `RegExpExec`, and reads `result[0]`, `result.index`, `result.length`,
  `result[n]`, and `result.groups` off the returned array.
- New `GetSubstitution` overload implements **section 22.2.6.11.1** operating on
  the captures list + `groups` object from the exec result (so `$<name>`,
  `$1`..`$99`, `$&`, `` $` ``, `$'`, `$$` all resolve against the exec result).
- `@@replace` no longer requires `this` to be a genuine RegExp (it may be a
  user object with `exec`/`global`), matching the spec.
- Removed the now-orphaned `RegexMatch`-based `GetSubstitution`.

## Tests

`tests/Starling.Js.Tests/Runtime/RegExpReplaceDelegationTests.cs` -- 10
`[SpecFact]` tests, each citing the relevant section:
- `Nonparticipating_capture_group_is_undefined`,
  `Optional_group_that_did_not_match_is_undefined`,
  `Untaken_alternation_branch_capture_is_undefined` (22.2.7.2)
- `String_replace_delegates_to_searchValue_Symbol_replace` (22.1.3.19)
- `Symbol_replace_calls_the_regexps_own_exec`,
  `Symbol_replace_uses_overridden_exec_result_array` (22.2.7.1)
- `Named_group_substitution_reads_exec_result_groups`,
  `Native_named_group_substitution_still_works` (22.2.6.11.1)
- `Global_replace_with_numbered_captures`,
  `Functional_replace_receives_undefined_for_unmatched_group` (22.2.6.11)

`Starling.Js.Tests`: **1285 passed, 1 skipped** (baseline 1275 + 10 new, no
regressions). Test262 subset: green.

## End-to-end result

`dotnet run --project src/Starling.Headless -- render
"https://www.mcmaster.com/products/abrading-polishing/"`:

- **Before:** `Uncaught dynamic script error ... [object Object]` (the RangeError
  from core-js's recursing replace polyfill) on bundle
  `mcm_d78496ad...?files=EcEk`.
- **After:** that bundle executes cleanly (no recursion). Render completes and
  writes the PNG. The **next** blocker is a different bundle and a parser gap:
  `mcm_93043416...: expected property name after '?.' (got NumericLiteral) (at
  100:1823)` -- optional-chaining followed by a numeric literal / bracket index.
  That is a separate parser WP.
