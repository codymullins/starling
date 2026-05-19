---
id: "handoff:google-com-search"
milestone: "M3 → M7"
status: "in-progress"
created_at: "2026-05-18"
last_updated: "2026-05-19"
plan_refs:
  - "browser-plan/13_MILESTONES.md"
  - "browser-plan/09_JS_ENGINE.md"
  - "browser-plan/10_WEB_APIS.md"
---

# Handoff: load `https://google.com` and view search results

## Goal

Make `https://www.google.com` load fully (with JavaScript) and render usable
search results from `/search?q=…`. Per `browser-plan/13_MILESTONES.md:158`
this is the M7 deliverable; the engine is currently around mid-M3, so this
handoff spans roughly M3→M7 of work.

## Reality check (from A0 baseline)

A0 (2026-05-18) confirmed the home page already renders well even with no JS
execution — Google logo, search box, both buttons, top nav (Gmail/Images), and
footer are all visible at `/tmp/google_b0_b1.png` (800×600). What's missing
is the **search results page**:

- `https://www.google.com/search?q=hello` returns 90 KB of HTML, but the
  result list is **JS-only**. Without client-side `fetch`/XHR there is no
  result content in the body.
- With a Lynx/w3m UA, Google serves a 2.4 KB "Update your browser" page.
  No SSR fallback to harvest.
- The original plan's "Path A — form submission only" cannot reach search
  results for Google specifically. Everything below assumes **Path B**
  (real JS execution + DOM bindings + fetch).

ALPN already defaults to `["http/1.1"]` in `TesseraHttpClientOptions`
(src/Starling.Net/TesseraHttpClient.cs:428). A safety check added in A1
fails fast if a peer negotiates `h2` despite our list
(src/Starling.Net/TesseraHttpClient.cs:268-281). HTTP/2 itself is not a
blocker; defer to M6 wp:M3-09.

## What's done

| Item | Status | Files |
|---|---|---|
| A0 — Baseline render | ✅ | `/tmp/google_b0_b1.png`, browser-plan/03_NETWORKING.md unchanged |
| A1 — ALPN safety check | ✅ | `src/Starling.Net/TesseraHttpClient.cs:264-281` |
| B0 — JS runtime foundations | ✅ | `src/Starling.Js/Runtime/{PropertyDescriptor,JsObject,JsRealm,AbstractOperations,JsBoundFunction,JsRuntime,JsVm}.cs` |
| B1a — Lexer hardening | ✅ | `src/Starling.Js/Lex/{JsLexer,JsTokenKind,JsLexError}.cs` |
| B1b-1 — Modern syntax slice | ✅ | `src/Starling.Js/{Ast/Expressions.cs,Parse/JsParser.cs,Bytecode/{JsCompiler,Opcode}.cs,Runtime/JsVm.cs}` |
| B1b-2b — Destructuring | ✅ | `src/Starling.Js/{Ast/Expressions.cs,Parse/JsParser*.cs,Bytecode/{JsCompiler,Opcode}.cs,Runtime/JsVm.cs}`, `tests/Starling.Js.Tests/Runtime/JsDestructuringTests.cs` |
| B2-1 — Object intrinsic | ✅ | `src/Starling.Js/Intrinsics/ObjectCtor.cs`, `tests/Starling.Js.Tests/Intrinsics/ObjectTests.cs`, `src/Starling.Js/Runtime/JsRealm.cs` (+`ObjectConstructor`) |
| B2-5 — String intrinsic | ✅ | `src/Starling.Js/Intrinsics/StringCtor.cs`, `tests/Starling.Js.Tests/Intrinsics/StringTests.cs` |
| B2-6 — Number/Boolean/globals | ✅ | `src/Starling.Js/Intrinsics/{NumberCtor,BooleanCtor,Globals}.cs`, `tests/Starling.Js.Tests/Intrinsics/{NumberTests,BooleanTests,GlobalsTests}.cs` |
| B2-7 — Math intrinsic | ✅ | `src/Starling.Js/Intrinsics/MathObj.cs`, `tests/Starling.Js.Tests/Intrinsics/MathTests.cs` |
| B2-8 — JSON intrinsic | ✅ | `src/Starling.Js/Intrinsics/JsonObj.cs`, `tests/Starling.Js.Tests/Intrinsics/JsonTests.cs`, `src/Starling.Js/Runtime/{JsRealm,JsVm}.cs` (+`ActiveVm` + reentrancy-safe Run wrapper) |
| B2-9 — console | ✅ | `src/Starling.Js/Intrinsics/ConsoleObj.cs`, `src/Starling.Js/Runtime/{ConsoleSink,JsRealm,JsRuntime}.cs`, `tests/Starling.Js.Tests/Intrinsics/ConsoleTests.cs` |
| B6-1 — Flex layout | ✅ | `src/Starling.Layout/Flex/{FlexProperties,FlexParser,FlexLayout}.cs`, `src/Starling.Layout/Block/BlockLayout.cs` (dispatch), `tests/Starling.Layout.Tests/Flex/FlexLayoutTests.cs` |

### B0 surface delivered

- `PropertyDescriptor` (data/accessor + writable/enumerable/configurable + IsAccessor)
- `JsObject`: `Prototype` slot + chain-walking `Get`/`Has`, descriptor-aware
  `Set`, `DefineOwnProperty` with non-configurable validator,
  `SetPrototypeOf` with cycle check, `EnumerableKeys`, `Extensible`.
- `JsRealm`: 35+ intrinsic prototype/constructor slots pre-wired with the
  correct inheritance (Error→Object.prototype, TypeError→Error.prototype,
  Iterator→Object, ArrayIterator→Iterator, …). Helpers: `NewOrdinaryObject`,
  `NewError`, `NewTypeError`, `NewRangeError`, primitive boxing.
- `AbstractOperations`: `ToPrimitive`, `ToObject`, `ToPropertyKey`,
  `IsCallable`, `IsConstructor`, `Get` (with accessor invocation),
  `Set` (with chain walk + accessor support), `Call`, `Construct`,
  `SameValue`, `SameValueZero`.
- `JsBoundFunction` for `Function.prototype.bind`.
- `JsNativeFunction` now takes `(thisValue, args)` and has an `IsConstructor`
  flag so `new SomeNative()` works.
- VM's `Call`/`CallMethod`/`New` route through `AbstractOperations` for
  uniform dispatch.

### B1b-1 surface delivered (parser → compiler → VM, end-to-end)

- **Template literals**: `` `text ${expr} more` `` with arbitrary number of
  substitutions, nested expressions, literal newlines.
- **Arrow functions**: `x => x*2`, `() => 42`, `(a, b) => a + b`, block-body
  `(x) => { return x+1; }`. **Lexical `this` is not yet wired** — currently
  desugars to FunctionExpression. Tracked: B1b-2.
- **Object methods**: `{ foo(n) { return this.x + n; } }`.
- **Shorthand properties** `{ x, y }`, computed keys `{ [k]: v }` (already
  worked, kept), **object spread** `{ ...src, override: 1 }` via the new
  `Opcode.SpreadInto`.

### Tests

`tests/Starling.Js.Tests` — **242 passing** (started at 207; +35 across):
- `Runtime/JsRealmAndProtoTests.cs` (13)
- `Lex/JsLexerTemplateRegexTests.cs` (8)
- `Runtime/JsModernSyntaxTests.cs` (14)

Pre-existing failures on `main` (not regressions):
- `Tessera.Engine.Tests.EngineSnapshotRenderTests.Snapshot_nginx_org_renders_match_golden` (SSIM 0.39)
- `Tessera.Paint.Tests.DisplayListBuilderTests.Underlined_link_emits_text_and_underline_fill`

## Active assignments (2026-05-18)

These tasks are currently being worked on — do not pick them up in another
session. Other rows in the queue are free for other agents/sessions.

| ID | Owner | Status |
|---|---|---|
| **B2-1** Object intrinsic | claude-cody (agent) | complete (2026-05-18) |
| **B2-7** Math intrinsic | claude-cody (agent) | complete (2026-05-18) |
| **B2-8** JSON intrinsic | claude-cody (agent) | complete (2026-05-18) |
| **B6-1** Flex layout | claude-cody (agent) | complete (2026-05-18) |
| **B2-5** String prototype | claude-cody (agent, lane-C-1) | complete (2026-05-19) |
| **B2-6** Number/Boolean/globals | claude-cody (agent, lane-C-2) | complete (2026-05-19) |
| **B2-9** console | claude-cody (agent, lane-C-3) | complete (2026-05-18) |
| **B2-2** Function intrinsic | claude-cody (agent) | in progress (2026-05-18) |
| **B2-3** Error hierarchy | claude-cody (agent) | in progress (2026-05-18) |
| **B3-4** Promise + microtasks | claude-cody (agent) | in progress (2026-05-18) |
| **B6-2** position: absolute / fixed | claude-cody (agent) | in progress (2026-05-18) |
| **B1b-2b** Destructuring | claude-cody (agent, lane-A) | complete (2026-05-19) |
| **B3-1** Symbol + well-known symbols | claude-cody (agent, lane-D) | in progress (2026-05-18) |
| **B4-5** TypedArray/ArrayBuffer/DataView | claude-cody (agent, lane-E) | in progress (2026-05-18) |

## Work queue

Each row has a **Concurrency** column. Equal letters can be worked in parallel
without merge conflict; different letters have a hard ordering (the later one
depends on the earlier's surface).

| ID | Title | Concurrency | Depends on | Files (primary) |
|---|---|---|---|---|
| **B1b-2a** | Class declaration + expression, `extends`/`super`/methods/static/private `#fields` | **lane A** | B0, B1b-1 | `src/Starling.Js/{Ast/Expressions.cs,Parse/JsParser.cs,Bytecode/JsCompiler.cs,Runtime/JsVm.cs}` |
| **B1b-2b** | Destructuring (array + object, in let/const/var, params, assignment, defaults, rest) | **lane A** | B0 | `src/Starling.Js/{Ast/*,Parse/*,Bytecode/JsCompiler.cs}` (compiler desugars to existing locals + property loads) |
| **B1b-2c** | Async / await + generators (state-machine bytecode + new opcodes) | **lane A** | B3 (Promise + microtasks) | `src/Starling.Js/{Bytecode/Opcode.cs,Runtime/JsVm.cs}` + new `JsGenerator.cs` |
| **B2-1** | `Object` intrinsic (ctor + statics + prototype) | **lane B** | B0 | `src/Starling.Js/Intrinsics/ObjectCtor.cs` (new), `tests/Starling.Js.Tests/Intrinsics/ObjectTests.cs` (new) |
| **B2-2** | `Function` intrinsic + `call`/`apply`/`bind` | **lane B** | B0 | `src/Starling.Js/Intrinsics/FunctionCtor.cs` |
| **B2-3** | `Error` hierarchy (Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError, AggregateError) | **lane B** | B0 | `src/Starling.Js/Intrinsics/ErrorCtor.cs` |
| **B2-4** | `Array` intrinsic + full prototype (incl. immutable `toReversed`/`toSorted`/`toSpliced`/`with`) — uses dense `JsArray : JsObject` | **lane B** | B2-1, B2-2 | `src/Starling.Js/Intrinsics/ArrayCtor.cs`, `src/Starling.Js/Runtime/JsArray.cs` (new) |
| **B2-5** | `String` prototype (excl. RegExp paths; those land in B4-1) | **lane C** | B2-1 | `src/Starling.Js/Intrinsics/StringCtor.cs` |
| **B2-6** | `Number` / `Boolean` / global `parseInt`/`parseFloat`/`isNaN`/`isFinite`/`encodeURI*`/`decodeURI*` | **lane C** | B2-1 | `src/Starling.Js/Intrinsics/{NumberCtor,BooleanCtor,Globals}.cs` |
| **B2-7** | `Math` (every static + constants) | **lane C** | B0 | `src/Starling.Js/Intrinsics/MathObj.cs` |
| **B2-8** | `JSON.parse` + `JSON.stringify` (RFC 8259, with replacer/indent/cycles) | **lane C** | B0 | `src/Starling.Js/Intrinsics/JsonObj.cs` |
| **B2-9** | `console.{log,info,warn,error,debug,dir,table,time,timeEnd,count,group,groupEnd,trace}` | **lane C** | B0 | `src/Starling.Js/Intrinsics/ConsoleObj.cs` |
| **B3-1** | `Symbol` + well-known symbols + Symbol registry (`Symbol.for`/`keyFor`) | **lane D** | B2-1 | `src/Starling.Js/Intrinsics/SymbolCtor.cs` |
| **B3-2** | Iterator protocol + `for…of` retargeted to `@@iterator → .next`; Array/String iterators | **lane D** | B3-1, B2-4, B2-5 | `src/Starling.Js/{Bytecode/JsCompiler.cs,Intrinsics/*Iterator*.cs}` |
| **B3-3** | `Map` / `Set` (ordered-insertion) + `WeakMap`/`WeakSet` (`ConditionalWeakTable`-backed) | **lane D** | B3-2 | `src/Starling.Js/Intrinsics/{MapCtor,SetCtor,WeakMapCtor,WeakSetCtor}.cs` |
| **B3-4** | `Promise` + `MicrotaskQueue` (wire through `WebEventLoop`) | **lane D** | B0 | `src/Starling.Js/Runtime/MicrotaskQueue.cs` (new), `src/Starling.Js/Intrinsics/PromiseCtor.cs` |
| **B4-1** | `RegExp` — parser → Thompson NFA → Pike VM, full ES2024 grammar incl. unicode escapes; back-fill `String.prototype.{match,matchAll,replace,replaceAll,search,split}` | **lane E** | B2-5 | `src/Starling.Js/RegExp/*` (new subtree) + `src/Starling.Js/Intrinsics/RegExpCtor.cs` |
| **B4-2** | `Date` — invariant locale | **lane E** | B0 | `src/Starling.Js/Intrinsics/DateCtor.cs` |
| **B4-3** | `BigInt` (full operator set + `asIntN`/`asUintN`) | **lane E** | B2-6 | `src/Starling.Js/Intrinsics/BigIntCtor.cs` + `JsValue` BigInt arithmetic |
| **B4-4** | `Proxy` + `Reflect` (every trap) | **lane E** | B3-1 | `src/Starling.Js/{Intrinsics/{ProxyCtor,ReflectObj}.cs,Runtime/JsProxy.cs}` |
| **B4-5** | `TypedArray` + `ArrayBuffer` + `DataView` (Uint8Array … Float64Array) | **lane E** | B0 | `src/Starling.Js/Intrinsics/{ArrayBufferCtor,DataViewCtor,TypedArrayCtors}.cs` |
| **B4-6** | `WeakRef` + `FinalizationRegistry` | **lane E** | B3-3 | `src/Starling.Js/Intrinsics/{WeakRefCtor,FinalizationRegistryCtor}.cs` |
| **B5-1** | `Window` / `document` real bindings (replace `DomBindingHost`); `EventTarget` + `addEventListener` / `removeEventListener` / `dispatchEvent` | **lane F** | B2-1, B2-2 | `src/Starling.Bindings/{WindowBinding,DocumentBinding,EventTargetBinding}.cs` (new) |
| **B5-2** | Timers — `setTimeout` / `setInterval` / `clearTimeout` / `clearInterval` against `WebEventLoop` | **lane F** | B3-4 | `src/Starling.Bindings/TimersBinding.cs` |
| **B5-3** | `fetch` + `XMLHttpRequest` (against `TesseraHttpClient`) | **lane F** | B3-4, B5-1 | `src/Starling.Bindings/{FetchBinding,XhrBinding}.cs` |
| **B5-4** | `MutationObserver` / `IntersectionObserver` / `ResizeObserver` | **lane F** | B5-1 | `src/Starling.Bindings/Observers/*.cs` |
| **B5-5** | `history.pushState` / `popstate` + `localStorage` / `sessionStorage` + `document.cookie` getter/setter + `Performance.now` | **lane F** | B5-1 | `src/Starling.Bindings/{HistoryBinding,StorageBinding,CookieBinding,PerformanceBinding}.cs` |
| **B6-1** | Flex layout (direction, wrap=nowrap, justify-content, align-items, flex shorthand, gap) | **lane G** | (none) | `src/Starling.Layout/Flex/*` (new) |
| **B6-2** | `position: absolute` / `fixed` | **lane G** | (none) | `src/Starling.Layout/Position/*` (new) |
| **B6-3** | `position: sticky` | **lane G** | B6-1, B6-2 | `src/Starling.Layout/Position/Sticky.cs` |
| **B7** | End-to-end google.com search smoke test (offline fixtures + optional live gate) | **final** | everything | `tests/Starling.Engine.Tests/GoogleSearchTests.cs` (new), `testdata/sites/google-*.html` |

### Concurrency map

The lanes encode "can ship in parallel without merge conflict". A single agent
ploughing through any lane in order produces no rebases against the others.

```
lane A  B1b-2a → B1b-2b → B1b-2c        (parser + compiler surgery)
lane B  B2-1   → B2-2  → B2-3 → B2-4     (Object → Function → Error → Array)
lane C  B2-5 ∥ B2-6 ∥ B2-7 ∥ B2-8 ∥ B2-9 (String/Number/Math/JSON/console, each independent)
lane D  B3-1   → B3-2 → B3-3 → B3-4     (Symbol → iterators → collections → Promise)
lane E  B4-1 ∥ B4-2 ∥ B4-3 ∥ B4-4 ∥ B4-5 ∥ B4-6 (long-tail intrinsics, mutually independent)
lane F  B5-1   → B5-2 → B5-3 → B5-4 → B5-5 (Web APIs; ordered by dependency on B5-1)
lane G  B6-1 ∥ B6-2 → B6-3              (layout; flex + abs/fixed parallel, sticky last)
final   B7                              (after all)
```

Critical paths into B7:
- **For "page initialization doesn't throw"**: lane B (B2-1 .. B2-4) + lane C
  (any subset) gets minified JS to at least *evaluate*.
- **For "search results appear"**: lane F **B5-3** (fetch / XHR) is the
  keystone; it depends on B3-4 (Promise + microtasks) from lane D, and on
  B5-1 (document + addEventListener) from lane F itself.

### Parallel-launch suggestions

- **3 agents now**: lane B (B2-1), lane C (B2-7 Math or B2-8 JSON — both tiny),
  lane G (B6-1 flex). Zero overlap.
- **5 agents after B0 settles**: A/B/C/D/G can all run; lane E and lane F
  ride on top once their deps land.
- Lane A's class+destructuring work modifies the same files as lane B intrinsics
  only when an intrinsic registers via class syntax — but the intrinsic files
  are all *new*, so they don't collide with parser/compiler edits. Safe in
  parallel as long as the public surface of `JsCompiler` / `Opcode` doesn't
  drift mid-flight.

## Known gaps / footguns

- **Arrow lexical `this` is not wired** (B1b-1). Today arrow bodies behave
  like FunctionExpression bodies for `this`. Tests pass because none of them
  reference `this` inside an arrow. **Pin a test for this when B1b-2 starts**
  so we don't silently regress when class method bodies use arrows for
  callbacks.
- **`Function.prototype` is currently an empty `JsObject`** — `call`/`apply`/
  `bind` not installed yet. JS `JsFunction` instances inherit from it via
  `JsRealm.FunctionPrototype` but the chain is empty. Land B2-2 before
  anything that relies on `.bind()` (Google's bundles do).
- **No `Function.prototype` link on user-defined functions yet**. When a JS
  function is constructed via the `LoadFunction` opcode, its `Prototype`
  property isn't pointed at `realm.FunctionPrototype`. Fix in B2-2 with a
  helper that wires both the function object's `[[Prototype]]` and its own
  `prototype` property (the latter is the new-target prototype).
- **`new Array(...)` doesn't return an array yet**, just a plain object. Wire
  in B2-4 by making `Array` a `JsNativeFunction(isConstructor: true)` whose
  body returns a fresh `JsArray`.
- **Pre-existing test failures on `main`**: ignore them when running
  regressions for any task here. They're tracked under separate issues.
- **`Snapshot_nginx_org_renders_match_golden`**: SSIM 0.39 vs. 0.99 threshold.
  Snapshot golden is stale, not a regression. Re-vendor with
  `TESSERA_UPDATE_GOLDENS=1` when the underlying paint change is
  intentional.

## How to run / verify

```bash
# Build the lot
dotnet build Starling.sln -c Debug

# JS unit tests — all 242 should pass
dotnet test tests/Starling.Js.Tests --nologo

# Live baseline render (gives /tmp/google_*.png)
dotnet run --project src/Starling.Headless -c Debug --no-build -- \
  render https://www.google.com -o /tmp/google.png

# Per-intrinsic test scaffold (use this pattern for B2+ tasks)
dotnet test tests/Starling.Js.Tests --nologo \
  --filter "FullyQualifiedName~JsRealmAndProto|JsLexerTemplateRegex|JsModernSyntax"
```

The B0 + B1a + B1b-1 changes are JS-internal; the rendered home page is
byte-identical to the pre-change baseline. The first user-visible delta will
come from B5-1+B5-3 (a real `fetch` call against the offline `/search` fixture
returning result HTML).

## Pointers for the next agent

- Read `browser-plan/09_JS_ENGINE.md` end-to-end before starting any B2+ task
  — it pins the spec interpretation and naming for every intrinsic.
- Each intrinsic file follows the pattern: `public static void Install(JsRealm
  realm) { … }` — register the constructor on `realm.GlobalObject`, populate
  the corresponding `*Prototype` slot, ensure descriptors are
  `BuiltinMethod` (writable+non-enumerable+configurable).
- New opcodes go in `src/Starling.Js/Bytecode/Opcode.cs` (extend the enum at
  the bottom, before `Halt`) and `src/Starling.Js/Runtime/JsVm.cs` (add the
  `case` in dispatch order matching the enum).
- Tests: copy `tests/Starling.Js.Tests/Runtime/JsModernSyntaxTests.cs` as the
  template for end-to-end (parse → compile → run) tests; copy
  `tests/Starling.Js.Tests/Runtime/JsRealmAndProtoTests.cs` for unit-level
  runtime tests.
- The plan file at `/Users/cody/.claude/plans/let-s-get-started-implementing-virtual-dahl.md`
  has the full multi-phase plan in narrative form if more context is needed.

## Sizing (best-guess, single agent each)

| Lane | Approx. effort |
|---|---|
| Lane A (parser + compiler) | 2 – 3 weeks |
| Lane B (Object/Function/Error/Array core) | 2 – 3 weeks |
| Lane C (String/Number/Math/JSON/console) | 1 – 2 weeks |
| Lane D (Symbol/iterator/Map/Set/Promise) | 2 – 3 weeks |
| Lane E (RegExp/Date/BigInt/Proxy/TypedArrays) | 3 – 4 weeks (RegExp alone is ~2) |
| Lane F (Web APIs) | 3 – 4 weeks |
| Lane G (Layout) | 1 – 2 weeks |
| B7 smoke | 2 – 3 days |

Single-agent serial estimate: ~3 months. Five-agent parallel estimate
(lanes A/B/C/D/G concurrent then F gated on D): ~6 – 8 weeks.
