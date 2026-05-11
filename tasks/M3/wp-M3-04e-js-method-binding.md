---
id: "wp:M3-04e-js-method-binding"
parent: "wp:M3-04-js-vm"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T19:50:00Z"
branch: "wp-M3-04e-js-method-binding"
completed_at: "2026-05-11T20:05:00Z"
depends_on:
  - "wp:M3-04d-js-new-and-this"
blocks:
  - "wp:M3-05-js-intrinsics"
subsystem: "Tessera.Js"
plan_refs:
  - "browser-plan/09_JS_ENGINE.md#vm"
---

# wp:M3-04e — Method `this` binding

## Goal
Make `obj.method()` and `obj[key]()` bind `this` to `obj` inside the
callee. Without this, methods stored on instances can't read their own
fields:

```js
function C() { this.n = 5; this.get = function() { return this.n; }; }
new C().get();  // currently undefined; should be 5
```

## Outputs
- New opcode `CallMethod [u8 argc]`: pops `argc` args, pops the
  function value, pops the receiver, binds `this = receiver`.
- Compiler: when a `CallExpression`'s callee is a `MemberExpression`,
  emit `obj → Dup → LoadProperty → args → CallMethod`. The
  non-member-call path keeps using plain `Opcode.Call`.

## Out of scope
- Optional chaining (`obj?.method()`): the member is optional, so this
  needs a guard. Queued for M3-04f.
- Bound functions (`Function.prototype.bind`): M3-05 intrinsics.

## Acceptance
- 6+ tests including: dot-method binds this; bracket-method binds this;
  method returning this; chained calls (`a.b().c()` where b returns
  another object); plain function call still has this=Undefined; the
  receiver in dot-call is evaluated once.

## Handoff log
- 2026-05-11T19:50Z — created and claimed atomically by agent-claude-cody.
