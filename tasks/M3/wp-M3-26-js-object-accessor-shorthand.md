---
id: wp:M3-26-js-object-accessor-shorthand
title: "Object-literal accessor (getter/setter) shorthand — { get x(){…}, set x(v){…} } parse + compile"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

Object-literal getter/setter shorthand (ECMA-262 §13.2.5) failed to parse:

```js
var o = { get x(){ return 42; }, set x(v){ this._x = v; } };
// → expected '}' to close object literal (got Identifier 'x')
```

mcmaster.com's app bundle (Redux Toolkit `createSlice`) returns
`{ reducerPath:x, getSelectors:P, get selectors(){return P(w)}, selectSlice:w }`
and choked at `get selectors`. The parser treated `get` as a plain identifier
key, then expected `}`/`,` and saw the following property name.

## Root cause

`ParseObjectProperty` only handled data properties, method shorthand
(`{ m(){} }`), and binding/shorthand forms. There was no contextual `get`/`set`
detection. Classes already supported accessors (`ParseClassMember` +
`InstallMethodOrAccessor`), but object literals never reused that path.

## Design — reuse the class accessor machinery

### 1. AST — `Ast/Expressions.cs`
Added a `MethodKind Kind = MethodKind.Method` field to `ObjectProperty` (reuses
the existing class `MethodKind` enum). Default keeps every existing call site
back-compatible; `Get`/`Set` mark an accessor whose `Value` is the accessor fn.

### 2. Parse — `Parse/JsParser.cs`
In `ParseObjectProperty`, detect a contextual `get`/`set` followed by a property
name using the **same disambiguation as `ParseClassMember`**: `get`/`set` start
an accessor ONLY when the next token can begin a property name (identifier,
reserved word, string, number, or computed `[`). Extracted shared key parsing
into `ParsePropertyKey()` (computed/string/numeric/identifier) and added
`IsAccessorPropertyNameStart()`. Enforced §15.4.1 well-formedness: getter takes
0 params, setter exactly 1.

### 3. Compile — `Bytecode/JsCompiler.cs` (`EmitObjectLiteral`)
Accessor properties emit the accessor function then a define opcode. Added four
opcodes (`Bytecode/Opcode.cs`):
- `DefineGetter`/`DefineSetter [u16 nameIdx]` — string/numeric key.
- `DefineGetterComputed`/`DefineSetterComputed` — computed key on stack.

Each pops its inputs above the object and pushes the object back, keeping the
stack at `[obj]` for the next property (no Dup/Pop bracketing).

### 4. Runtime — `Runtime/JsVm.cs`
New `InstallObjectAccessor` mirrors the class `InstallMethodOrAccessor` but marks
the descriptor **enumerable** (object-literal accessors are enumerable per
§13.2.5; class accessors are not). It merges an existing accessor's
complementary half (paired `get x()/set x()` → one descriptor) and stamps the
§13.2.5.5 `name` ("get x"/"set x") via the existing `StampMethodName`.

### 5. CreateDataPropertyOrThrow for data props — overriding accessors
Object-literal **data** properties previously used `StoreProperty` (`[[Set]]`),
which would invoke an existing accessor's setter rather than overriding it — so
`{ get x(){…}, x: v }` failed "later definition wins" (§13.2.5.5 uses
CreateDataPropertyOrThrow, not Set). Added `DefineDataProperty [u16]` /
`DefineDataComputed` opcodes (own enumerable/writable/configurable data
descriptor via `DefineOwnProperty`) and switched `EmitObjectLiteral`'s data path
to them. `StoreProperty` is left untouched for member assignment / update exprs.
`__proto__` is unaffected (no proto accessor is registered, so both paths create
a plain own property — verified no regression across the full suite).

## Disambiguation handled (all tested)
- `{ get: 1 }` / `{ set: 2 }` — data property literally named "get"/"set".
- `{ get(){} }` / `{ set(){} }` — METHOD named "get"/"set".
- `{ get x(){}, set x(v){} }` — accessor pair sharing one descriptor (either order).
- `{ get 0(){} }`, `{ get "s"(){} }`, `{ get [k](){} }` — numeric/string/computed keys.
- `{ get }` shorthand binding (identifier in scope).
- data-after-accessor and accessor-after-data: later definition wins.

## Tests

`tests/Starling.Js.Tests/Runtime/JsObjectAccessorTests.cs` (24 tests, green):
getter returns / `this`; setter runs + receives value; setter-only reads
undefined; get/set pair both orders; the Redux mixed shape
`{a:1, get b(){return 2}, c:3}` reads all three; numeric/string/computed accessor
keys; computed setter; function `name` "get x"/"set x"; accessor enumerable +
in `Object.keys`; the four disambiguation cases; `{get}` shorthand;
data-after-getter and getter-after-data later-wins; getter-with-param and
setter-with-zero-params SyntaxErrors.

Full `Starling.Js.Tests`: **1384 passing, 1 skipped** (was 1360/1 — +24 new).

## Diagnostic result — next mcmaster blocker

The `get selectors` error is gone; the app bundle `mcm_cc73c91b…` now reaches a
NEW positioned parser error:

```
expected binding name or pattern (got Ellipsis '...') (at 117:67709)
```

Bundle line 117, col ~67709 (Chrome-UA fetch):

```
{[t.name](...n){return t(...n)}}[t.name]
```

The next gap is a **rest parameter in a computed-key method shorthand**
(`{ [t.name](...n){…} }`). Rest params in arrows / function declarations parse
(wp:M3-25), but object/method-shorthand parameter lists don't yet accept `...n`.
That is the next WP candidate (M3-27).
