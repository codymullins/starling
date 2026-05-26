---
id: "wp:M3-82-js262-ownkeys-integer-ordering"
parent: "wp:M3-02e-js-test262"
milestone: "M3"
status: "complete"
phase: "3"
subsystem: "Starling.Js"
depends_on: []
blocks: []
---

# wp:M3-82 — JS: own-property-key ordering (integer indices first, ascending)

## Why (evidence)
~32 coherent failures, all the same symptom — property keys enumerated in
insertion order instead of spec order:
`Actual [a, 1, c, 2] and expected [1, 2, a, c] should have the same contents`.
Concentrated in `computed-property-names/*` (number.js, class/static
method-*-order, to-name-side-effects/numbers-*) and
`statements/for-in/order-simple-object.js` / `order-after-define-property.js`,
plus stragglers anywhere a test does `Object.getOwnPropertyNames` / `for-in` /
`Object.keys` over numeric keys.

Spec: **OrdinaryOwnPropertyKeys** (§10.1.11). Own keys are returned as:
1. every **array-index** key (canonical numeric string in `[0, 2^32-1)`), in
   **ascending numeric order**; then
2. every other String key, in **property creation order**; then
3. every Symbol key, in property creation order.
The engine returns keys in pure insertion order, so integer keys aren't hoisted
ahead of (or sorted among) themselves.

## Scope
- Implement the integer-index-first ordering in the ordinary object's
  own-key enumeration so it flows through `OwnPropertyKeys`,
  `Object.keys/values/entries`, `Object.getOwnPropertyNames`,
  `JSON.stringify`, `for-in`, spread, and `Object.assign`.
- "Array index" = the canonical-numeric-string test (`ToString(ToUint32(k)) === k`
  and `< 2^32-1`), NOT arbitrary numeric-looking strings — e.g. `"1e+55"` and
  `"-1"` stay in the String bucket (see
  `computed-property-names/object/property/number-duplicates.js`).
- Symbols last. Don't disturb existing insertion order *within* the string and
  symbol buckets.

## MUST build on current main
First `git fetch origin && git reset --hard origin/js-262`, then extend.

## Acceptance
- `STARLING_TEST262_FILTER=computed-property-names` failures drop sharply (was
  ~18 there) and `for-in/order-*` clears; report before/after; regression-scan
  every category.
- Unit tests: `Object.keys({b:0, 2:0, a:0, 1:0})` → `["1","2","b","a"]`;
  array-index vs non-index split (`"1e+55"`, `"-1"`, `"4294967295"` stay
  string-bucket; `"0".."4294967294"` are indices); symbols enumerate after
  strings in `Reflect.ownKeys`.
- Existing `Starling.Js.Tests` green except the known pre-existing failure.
- Commit author `Cody Mullins <1738479+codymullins@users.noreply.github.com>`.

## Done
Landed in `48ae377 fix(js): own-property-key ordering — integer indices first ascending (§10.1.11)`.
`computed-property-names` 78/96 → 94/96, `for-in/order` 6/10 → 10/10. Key
"should have the same contents" signature 32 → 14 (the remaining 14 are mostly
the `Number.toString` `1e55` upper/lower-case formatting, out of scope).
Implementation: `JsObject` now holds a parallel `_stringKeyOrder` list and an
`OrderedStringKeys()` helper that yields integer-index keys first in ascending
numeric order then strings in insertion order, with `PutString`/`RemoveString`
maintaining order across delete + reinsert. Partial-define merge centralized as
virtual `DefineOwnPropertyPartial` so JsProxy, JsModuleNamespace, JsArray, and
JsMappedArguments keep their exotic semantics. `JsFunction.CreateInstance`
installs own keys as `length, name, prototype`.
