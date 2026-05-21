---
id: wp:M3-20-js-arguments-object
title: "The `arguments` exotic object (§10.4.4) — synthesize it for non-arrow function bodies that read it"
status: complete
claimed_by: agent-claude-cody
claimed_at: 2026-05-21T00:00:00Z
completed_at: 2026-05-21T00:00:00Z
depends_on: []
subsystem: Starling.Js
---

## Goal

mcmaster.com's second bundle (`mcm_93043416…` — jQuery + Backbone + YUI 2.6 +
Handlebars) threw, while initializing YUI:

```
engine.js: Uncaught dynamic script error (…mcm_93043416…):
  not a constructor: undefined (new hint: 'CustomEvent')
```

The failing constructor is `YAHOO.util.CustomEvent` (YUI's own event class,
defined at byte ~5768 of the bundle as a self-referential constructor). The
constructor *is* defined before any use, yet at construction time
`YAHOO.util.CustomEvent` evaluated to `undefined` → `new undefined()`.

## Diagnosis

Temporary instrumentation in the VM (`LoadProperty` / `StoreProperty` /
`LoadGlobal` / `StoreComputed`, tagging objects with stable identity ids;
removed before commit) showed:

- `YAHOO` was a stable single object (objId=1) the whole time — no reassignment.
- But **`YAHOO.util` was `undefined`** at the construction site, and **no
  `StoreComputed [util]` ever fired** — so `YAHOO.namespace("util",…)` never
  created `YAHOO.util`.

`YAHOO.namespace` builds the namespace by iterating its argument list:

```js
YAHOO.namespace = function () {
  var e, t, n, r = arguments, i = null;
  for (e = 0; e < r.length; e += 1)
    for (n = r[e].split("."), i = YAHOO, t = "YAHOO" == n[0] ? 1 : 0; t < n.length; t += 1)
      i[n[t]] = i[n[t]] || {}, i = i[n[t]];
  return i;
};
```

Reducing further: `function f(){ return arguments.length; } f(1,2,3)` returned
`undefined`, and `arguments[0]` was `undefined`. **Starling had no `arguments`
object at all.** A function body's reference to the identifier `arguments`
resolved as a free identifier → `LoadGlobal "arguments"` → `undefined`. So
`r = arguments` was `undefined`, `r.length` was `undefined`, the `for` loop
`0 < undefined` never iterated, `YAHOO.util` was never created, and the later
`new YAHOO.util.CustomEvent(...)` constructed `undefined`.

The root cause is therefore **not** YUI-specific and not a property-identity /
comma-expression / prototype bug — it is the missing `arguments` exotic object
(ECMAScript §10.4.4).

## Fix (Starling primitive)

Implement the unmapped `arguments` object (§10.4.4 / §10.4.4.6
CreateUnmappedArgumentsObject):

1. **Compiler** (`JsCompiler` + `JsCompiler.CaptureAnalysis` +
   `JsCompiler.Classes`): a new `CaptureAnalysis.ReferencesArguments(params, body)`
   detects whether a function body reads the identifier `arguments` free in its
   own arguments-scope — it descends into nested **arrow** functions (which
   inherit `arguments` lexically) but not into nested ordinary functions / class
   methods (which establish their own). When a non-arrow function (declaration,
   expression, class constructor, or method) references `arguments` and does not
   already bind the name (explicit param/var named `arguments` wins, §10.2.11),
   `MaybeBindArguments` reserves a local slot for `arguments` and emits a new
   `MakeArguments slot` opcode. Arrow bodies never emit it. If a nested arrow
   captures `arguments`, the slot is boxed into a `Cell` (`InitCellLocal`) so the
   closure and the frame share one object.

2. **VM** (`JsVm`): the `MakeArguments` opcode builds the object from the current
   frame's received `args` and writes it to the slot (through the cell when
   present).

3. **Runtime** (`JsRealm.CreateArgumentsObject`): an ordinary object inheriting
   from `Object.prototype` with the args as writable/enumerable/configurable
   indexed data properties, a non-enumerable `length`, and `@@iterator` aliased
   to `Array.prototype[@@iterator]` (so `[...arguments]` / `for…of` /
   destructuring work). Starling builds the unmapped form (no parameter
   aliasing) — sufficient for the legacy `arguments.length` / `arguments[i]` /
   `Array.prototype.slice.call(arguments)` idioms.

Allocation is on-demand: a function that never reads `arguments` pays nothing.

## Tests

`tests/Starling.Js.Tests/Runtime/JsArgumentsObjectTests.cs` (15 `[SpecFact]`),
reproduce-first (red before, green after):

- `length` / indexed access / out-of-range undefined / extra-args-beyond-params
- spread (`[...arguments]`) + `Array.prototype.slice.call(arguments)`
- the reduced `YAHOO.namespace` loop builds the namespace chain
- arrow inherits enclosing `arguments`; nested ordinary function gets its own
- explicit `arguments` param / `var` shadows the implicit object
- class method / function expression `arguments`
- `Array.isArray(arguments) === false` (unmapped form, Object.prototype proto)

## Verification

- `dotnet build src/Starling.Js` clean (0 warnings).
- Full `Starling.Js.Tests`: **1327 passing, 1 skipped, 0 failed** (baseline 1312
  + 15 new), no regressions.
- Render check: `starling render https://www.mcmaster.com` now executes the
  whole YUI bundle past the CustomEvent blocker. The `not a constructor:
  undefined (CustomEvent)` error is gone; the **next** mcmaster `engine.js`
  error is:
  `Uncaught (in XHR) not a function: undefined (method hint: 'handlePageReady')`
  (a separate XHR-callback blocker — out of scope here).
