---
id: wp:M3-08-js-member-update
title: "JS compiler: member-expression update targets (obj.x++, obj[k]++)"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

Support `obj.x++`, `++obj.x`, `obj[k]++`, `--obj[k]`, etc. in the JS bytecode
compiler. Previously `EmitUpdate` only handled `Identifier` targets and threw
`NotSupportedException("update target must be identifier in this slice")`.

This was blocking mcmaster.com's app bundle from executing (catalog fails to
render; the bundle uses `obj.x++` in its scroll/catalog code).

## What was done

- Added a `MemberExpression` branch to `EmitUpdate` in
  `src/Starling.Js/Bytecode/JsCompiler.cs` (~line 1765, after the existing
  `Identifier` arms).
- Handles both non-computed (`obj.name`) and computed (`obj[key]`) forms,
  for both prefix (`++`/`--`) and postfix (`++`/`--`).
- `super.x++` / `super[k]++` deferred with a clear `NotSupportedException`
  message referencing this WP.

## Lowering

### Non-computed prefix (`++obj.name`)
```
EmitObj, Dup, LoadProperty(name), UnaryPlus, LoadConst(1), Add/Sub, StoreProperty(name)
→ [newVal]
```

### Non-computed postfix (`obj.name++`)
```
EmitObj, Dup, LoadProperty(name), UnaryPlus, LoadConst(1), Add/Sub,
StoreProperty(name), LoadConst(1), Sub/Add
→ [oldNum]
```
`StoreProperty` re-pushes the stored value (newVal); `oldNum = newVal ∓ 1`.

### Computed prefix (`++obj[key]`)
```
EmitObj, EmitKey, Dup2, LoadComputed, UnaryPlus, LoadConst(1), Add/Sub, StoreComputed
→ [newVal]
```

### Computed postfix (`obj[key]++`)
```
EmitObj, EmitKey, Dup2, LoadComputed, UnaryPlus, LoadConst(1), Add/Sub,
StoreComputed, LoadConst(1), Sub/Add
→ [oldNum]
```

Key decisions:
- `Dup` (non-computed) / `Dup2` (computed) evaluates the reference parts
  exactly once, as required by §13.4.
- `UnaryPlus` before `Add`/`Sub` performs `ToNumber` (coerces strings/booleans
  to number, matching `"5"++ → 6`). The `Add` opcode uses JS `+` which would
  string-concat without this; `Sub` is numeric-only but `Add` is not.
- No temporary locals needed; `StoreProperty`/`StoreComputed` both re-push
  the stored value, enabling the postfix `oldNum = newVal ∓ 1` recovery.

## Tests added

`tests/Starling.Js.Tests/Runtime/JsMemberUpdateTests.cs` — 15 tests:

| Test | What it covers |
|---|---|
| `Postfix_increment_named_returns_old_value` | `r = p.n++` → r=5, p.n=6 |
| `Postfix_increment_named_mutates_property` | `o.x++` → o.x=2 |
| `Prefix_increment_named_returns_new_value` | `s = ++q.n` → s=6 |
| `Prefix_increment_named_mutates_property` | `++q.n` → q.n=6 |
| `Postfix_decrement_named_returns_old_value` | `r = o.x--` → r=3 |
| `Postfix_decrement_named_mutates_property` | `o.x--` → o.x=2 |
| `Prefix_decrement_named_returns_new_value` | `r = --o.x` → r=2 |
| `Postfix_increment_computed_returns_old_value` | `r = a[0]++` → r=5 |
| `Postfix_increment_computed_mutates_element` | `a[0]++` → a[0]=6 |
| `Prefix_increment_computed_returns_new_value` | `r = ++a[0]` → r=6 |
| `Prefix_decrement_computed_returns_new_value` | `r = --a[0]` → r=4 |
| `Computed_key_side_effect_executed_once` | key fn called once: i=1, a[0]=11 |
| `Combined_member_update_repro` | o.x=2, a[0]=6, r=5, p.n=6, s=6, q.n=6 |
| `Named_update_coerces_string_value_to_number` | `{x:"5"}.x++` → x=6 |
| `Named_update_as_statement_leaves_correct_final_value` | two consecutive updates |

All tagged `[Spec("ecma262", ..., "13.4 Update Expressions")]` + `[SpecFact]`.

## Results

- `Starling.Js.Tests`: 1263 passed, 1 skipped (baseline 1248+1 → +15 new)
- `/tmp/upd.html` repro: text rendered as "2,6,5,6" — no `engine.js` errors
- McMaster post-fix error: `not a function: undefined` (a different gap, not `update target`)

## Handoff log

- **2026-05-21** agent-claude-cody: implemented and tested; complete.
