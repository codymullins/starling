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
| B4-5 — TypedArray/ArrayBuffer/DataView | ✅ | `src/Starling.Js/Intrinsics/{ArrayBufferCtor,DataViewCtor,TypedArrayCtors}.cs`, `src/Starling.Js/Runtime/{JsArrayBuffer,JsTypedArray}.cs`, `tests/Starling.Js.Tests/Intrinsics/TypedArrayTests.cs` |
| B2-2 — Function intrinsic | ✅ | `src/Starling.Js/Intrinsics/FunctionCtor.cs`, `tests/Starling.Js.Tests/Intrinsics/FunctionTests.cs`, `src/Starling.Js/Runtime/{JsObject,JsFunction,JsBoundFunction,JsVm,JsRealm}.cs`, `src/Starling.Js/Bytecode/JsCompiler.cs` (anon name = ""), `src/Starling.Js/Intrinsics/ObjectCtor.cs` (DefineMethod realm threading) |
| B2-3 — Error hierarchy | ✅ | `src/Starling.Js/Intrinsics/ErrorCtor.cs`, `tests/Starling.Js.Tests/Intrinsics/ErrorTests.cs`, `src/Starling.Js/Runtime/JsRealm.cs` (+NewEvalError/NewAggregateError + cause overloads) |
| B3-4 — Promise + microtasks | ✅ | `src/Starling.Js/Runtime/{MicrotaskQueue,JsPromise,JsRealm,JsRuntime,JsVm}.cs`, `src/Starling.Js/Intrinsics/PromiseCtor.cs`, `tests/Starling.Js.Tests/Intrinsics/PromiseTests.cs` |
| B6-1 — Flex layout | ✅ | `src/Starling.Layout/Flex/{FlexProperties,FlexParser,FlexLayout}.cs`, `src/Starling.Layout/Block/BlockLayout.cs` (dispatch), `tests/Starling.Layout.Tests/Flex/FlexLayoutTests.cs` |
| B6-2 — absolute/fixed positioning | ✅ | `src/Starling.Layout/Position/{PositionProperties,PositionParser,PositionLayout}.cs`, `src/Starling.Layout/{Block/BlockLayout,LayoutEngine}.cs`, `tests/Starling.Layout.Tests/Position/PositionLayoutTests.cs` |
| B2-2-followup — realm-aware intrinsics | ✅ | `src/Starling.Js/Intrinsics/{StringCtor,NumberCtor,BooleanCtor,MathObj,JsonObj,ConsoleObj,Globals,IntrinsicHelpers}.cs`, `src/Starling.Js/Runtime/JsRuntime.cs` (RegisterGlobal), `tests/Starling.Js.Tests/Intrinsics/IntrinsicChainTests.cs` |
| B2-4 — Array intrinsic + JsArray | ✅ | `src/Starling.Js/Runtime/JsArray.cs`, `src/Starling.Js/Intrinsics/ArrayCtor.cs`, `src/Starling.Js/{Bytecode/{Opcode,JsCompiler},Runtime/{JsObject,JsRealm,JsVm,JsRuntime},Intrinsics/ObjectCtor}.cs`, `tests/Starling.Js.Tests/Intrinsics/ArrayTests.cs` |
| B5-1 — Window / document / EventTarget | ✅ | `src/Starling.Bindings/{EventTargetBinding,DomWrappers,NodeBindings,QuerySelectorEngine,WindowBinding}.cs`, `src/Starling.Js/Runtime/{JsRealm,JsObject}.cs`, `tests/Starling.Bindings.Tests/WindowDocumentTests.cs` (DomBindingHost deleted) |
| B5-2 — Timers | ✅ | `src/Starling.Bindings/{TimersBinding,Starling.Bindings.csproj}`, `tests/Starling.Bindings.Tests/TimersTests.cs` |
| B5-5 — history / storage / cookie / performance | ✅ | `src/Starling.Bindings/{HistoryBinding,StorageBinding,CookieBinding,PerformanceBinding,WindowBinding,EventTargetBinding}.cs`, `tests/Starling.Bindings.Tests/{HistoryTests,StorageTests,CookieTests,PerformanceTests}.cs` |
| B3-2 — Iterator protocol | ✅ | `src/Starling.Js/Intrinsics/IteratorIntrinsics.cs` (+ ArrayIterator + StringIterator), `src/Starling.Js/{Bytecode/{Opcode,JsCompiler},Runtime/{JsVm,AbstractOperations,JsRuntime},Intrinsics/{ArrayCtor,StringCtor}}.cs` (+ GetIterator/IteratorStep/IteratorClose AO; for…of + spread retargeted), `tests/Starling.Js.Tests/Runtime/IteratorProtocolTests.cs` |
| B4-1 — RegExp | ✅ | `src/Starling.Js/RegExp/{RegexFlags,RegexCharClass,RegexAst,RegexParser,RegexInstruction,RegexCompiler,RegexProgram,RegexPikeVm,MatchResult,CompiledRegex}.cs`, `src/Starling.Js/Runtime/JsRegExp.cs`, `src/Starling.Js/Intrinsics/{RegExpCtor,StringCtor}.cs`, `tests/Starling.Js.Tests/{RegExp/RegexPikeVmTests,Intrinsics/RegExpTests}.cs` |
| B5-3 — fetch + XMLHttpRequest | ✅ | `src/Starling.Bindings/{FetchBinding,XhrBinding,WindowBinding}.cs`, `src/Starling.Js/Runtime/{MicrotaskQueue,JsRealm,JsVm}.cs` (thread-safe enqueue), `tests/Starling.Bindings.Tests/{FetchTests,XhrTests}.cs` |
| B3-4-followup-a/b — parser reserved words + Promise.any AggregateError | ✅ | `src/Starling.Js/Parse/JsParser.cs` (ExpectIdentifierName), `src/Starling.Js/Intrinsics/PromiseCtor.cs`, `tests/Starling.Js.Tests/{Parse/JsParserReservedMemberTests,Intrinsics/{PromiseTests,ArrayTests}}.cs` |
| B4-1-followup-a — regex literal parser | ✅ | `src/Starling.Js/Lex/JsLexer.cs` (PushBack), `src/Starling.Js/{Ast/Expressions,Parse/JsParser,Bytecode/{Opcode,JsCompiler,Disassembler},Runtime/JsVm}.cs` (LoadRegExp opcode), `tests/Starling.Js.Tests/Parse/JsParserRegExpLiteralTests.cs` |
| B5-3-followup-a — WithActiveVm helper | ✅ | `src/Starling.Js/Runtime/JsRuntime.cs` (helper + `_primaryVm`), `src/Starling.Bindings/{TimersBinding,FetchBinding,XhrBinding}.cs` (empty-chunk hack removed), `tests/Starling.Js.Tests/Runtime/WithActiveVmTests.cs` |
| B5-3-followup-b — NoWarn revert | ✅ | `Directory.Build.props`; underlying analyzer fires fixed across `src/Starling.Js/{RegExp/*,Intrinsics/{RegExpCtor,StringCtor},Runtime/IteratorProtocolTests}.cs` + tests |
| B5-1-followup — DOM array-likes | ✅ | `src/Starling.Bindings/{NodeBindings,FetchBinding}.cs` (real `JsArray` + Headers iterators), `tests/Starling.Bindings.Tests/{WindowDocumentTests,DomArrayLikeTests}.cs` |
| B3-1 — Symbol + well-known symbols | ✅ | `src/Starling.Js/Intrinsics/SymbolCtor.cs`, `src/Starling.Js/Runtime/{JsSymbol,JsRealm}.cs`, `tests/Starling.Js.Tests/Intrinsics/SymbolTests.cs` |
| B1b-2a — Class declarations | ✅ | `src/Starling.Js/Parse/JsParser.Classes.cs`, `src/Starling.Js/Bytecode/{JsCompiler.Classes,ClassTemplate}.cs`, `src/Starling.Js/{Ast/{Expressions,Statements},Bytecode/{Opcode,Disassembler,JsCompiler},Parse/{JsParser,JsParser.Statements},Runtime/{JsFunction,JsRealm,JsVm}}.cs`, `tests/Starling.Js.Tests/Runtime/JsClassTests.cs` |
| B3-3 — Map/Set/WeakMap/WeakSet | ✅ | `src/Starling.Js/Runtime/{JsMap,JsSet,JsWeakMap,JsWeakSet,JsMapIterator,JsSetIterator,SameValueZeroComparer}.cs`, `src/Starling.Js/Intrinsics/{MapCtor,SetCtor,WeakMapCtor,WeakSetCtor}.cs`, `tests/Starling.Js.Tests/Intrinsics/{MapTests,SetTests,WeakMapTests,WeakSetTests}.cs` |
| B4-4 — Proxy + Reflect | ✅ | `src/Starling.Js/Runtime/{JsProxy,JsObject,AbstractOperations,JsRealm}.cs`, `src/Starling.Js/Intrinsics/{ProxyCtor,ReflectObj,ObjectCtor}.cs`, `tests/Starling.Js.Tests/Intrinsics/{ProxyTests,ReflectTests}.cs` |
| B4-2 — Date | ✅ | `src/Starling.Js/Runtime/JsDate.cs`, `src/Starling.Js/Intrinsics/DateCtor.cs`, `src/Starling.Js/Runtime/{JsRealm,JsRuntime}.cs`, `tests/Starling.Js.Tests/Intrinsics/DateTests.cs` |
| B4-1-followup-b — matchAll iterator | ✅ | `src/Starling.Js/Intrinsics/{RegExpStringIterator,StringCtor,RegExpCtor}.cs`, `tests/Starling.Js.Tests/Intrinsics/RegExpTests.cs` (+ iterator-shape tests) |

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
| **B2-2** Function intrinsic | claude-cody (agent) | complete (2026-05-18) |
| **B2-3** Error hierarchy | claude-cody (agent) | complete (2026-05-18) |
| **B3-4** Promise + microtasks | claude-cody (agent) | complete (2026-05-18) |
| **B6-2** position: absolute / fixed | claude-cody (agent) | complete (2026-05-18) |
| **B1b-2b** Destructuring | claude-cody (agent, lane-A) | complete (2026-05-19) |
| **B3-1** Symbol + well-known symbols | claude-cody (agent, lane-D) | complete (2026-05-19) |
| **B4-5** TypedArray/ArrayBuffer/DataView | claude-cody (agent, lane-E) | complete (2026-05-19) |
| **B4-1-followup-a** regex literal parser | claude-cody (agent) | complete (2026-05-19) |
| **B5-3-followup-a** WithActiveVm helper | claude-cody (agent) | complete (2026-05-19) |
| **B5-3-followup-b** revert NoWarn | claude-cody (agent) | complete (2026-05-19) |
| **B5-1-followup** DOM array-likes | claude-cody (agent) | complete (2026-05-19) |
| **B1b-2a** Class declarations | claude-cody (agent) | complete (2026-05-19) |
| **B3-3** Map/Set/WeakMap/WeakSet | claude-cody (agent) | complete (2026-05-19) |
| **B4-4** Proxy + Reflect | claude-cody (agent) | complete (2026-05-19) |
| **B4-2 + B4-1-followup-b** Date + matchAll iterator | claude-cody (agent) | complete (2026-05-19) |
| **Fixups** Proxy/Reflect + B5-5 location + Map/Weak | claude-cody (agent) | complete (2026-05-19) |
| **B2-2-followup** realm-aware intrinsics | claude-cody (agent) | complete (2026-05-19) |
| **B2-4** Array + JsArray | claude-cody (agent) | complete (2026-05-19) |
| **B5-2** Timers | claude-cody (agent) | complete (2026-05-19) |
| **B5-1** Window/document/EventTarget | claude-cody (agent) | complete (2026-05-19) |
| **B5-3** fetch + XMLHttpRequest | claude-cody (agent) | in progress (2026-05-19) |
| **B3-2** Iterator protocol | claude-cody (agent) | in progress (2026-05-19) |
| **B4-1** RegExp | claude-cody (agent) | in progress (2026-05-19) |
| **B3-4-followup-a/b** Parser fix + AggregateError swap | claude-cody (agent) | in progress (2026-05-19) |
| **B5-5** history / storage / cookie / performance | claude-cody (agent, lane-F) | complete (2026-05-19) |

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
| **B2-2-followup** | Migrate remaining intrinsics to realm-aware `JsNativeFunction(realm, name, length, body, isConstructor)` so their methods inherit `Function.prototype` (today `Math.max.bind(...)` etc. is `undefined`). Mechanical sweep — pattern matches `ObjectCtor` migration in B2-2. **Files:** `Intrinsics/{StringCtor,NumberCtor,BooleanCtor,MathObj,JsonObj,ConsoleObj,Globals}.cs`, `Runtime/JsRuntime.cs` (`RegisterGlobal` overloads). Add a regression test asserting `Math.max.bind(null, 1) instanceof Function` and `typeof JSON.stringify.call === 'function'`. | **lane B** | B2-2 | (see Files) |
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
| **B3-4-followup-a** | Parser fix — accept reserved words as member identifiers in MemberExpression (`p.catch`, `obj.finally`, `x.default`, `o.class`, etc.). Today these only work via bracket form (`p["catch"]`), which breaks every real-world promise chain. Per ES §13.3.2: any `IdentifierName` (including reserved words) is valid after `.`. Pin a test that `Promise.resolve(1).catch(e => 2).then(v => v)` parses + evaluates. **Files:** `src/Starling.Js/Parse/JsParser.cs` (MemberExpression parser), `tests/Starling.Js.Tests/Parse/*` or `Intrinsics/PromiseTests.cs` (rewrite the bracket-form workarounds to dot form). | **lane A** | (none) | `src/Starling.Js/Parse/JsParser.cs` |
| **B3-4-followup-b** | Swap `Promise.any`'s ad-hoc aggregate-error object for `realm.NewAggregateError(reasons, "All promises were rejected")`. 5-line change in `PromiseCtor.cs`'s `PerformPromiseAny` rejection branch. B2-3 is done so the helper exists. Pin a test that `Promise.any([Promise.reject(1), Promise.reject(2)]).catch(e => e instanceof AggregateError)` is true. | **lane D** | B2-3 (done), B3-4 (done) | `src/Starling.Js/Intrinsics/PromiseCtor.cs` |
| **B3-4-followup-c** | `unhandledrejection` / `rejectionhandled` events — surface uncaught microtask exceptions as DOM events on `Window` instead of the current `ConsoleSink` route. Spec: §27.2.5 PromiseRejectionEvent. Tracked via `MicrotaskQueue.UncaughtHandler` (currently a `Console` writer). Roll into **B5-1** (Window/EventTarget) since the event surface lives there — but pin a TODO comment in `PromiseCtor.cs` so the B5-1 agent picks it up. | **lane F** | B5-1, B3-4 (done) | `src/Starling.Bindings/WindowBinding.cs` (when it lands), `src/Starling.Js/Runtime/MicrotaskQueue.cs` |
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
| **B4-1-followup-a** | Wire regex-literal syntax `/foo/g` into the parser. The lexer already emits `RegExpLiteral` tokens (B1a) but the parser never consumes them — B4-1 was scoped not to touch `JsParser.cs`. Without this, every real-world JS bundle that uses inline regex (most do) is broken. Pin a test: `/\d+/g.test("abc 123") === true` evaluates directly. **Files:** `src/Starling.Js/Parse/JsParser.cs` (PrimaryExpression), maybe a `Bytecode/Opcode.cs` LoadRegExp instruction or just lower to `new RegExp(source, flags)`. | **lane A** | B4-1 (done) | `src/Starling.Js/Parse/JsParser.cs` |
| **B4-1-followup-b** | `String.prototype.matchAll` currently returns a `JsArray` of all matches; the spec requires a real iterator. Now that B3-2 has shipped, swap to construct a `JsRegExpStringIterator` (new tiny class mirroring `JsArrayIterator`'s shape) inheriting from `%IteratorPrototype%`. Pin a test that `[..."abc abc".matchAll(/[a-z]+/g)].length === 2`. | **lane C** | B3-2 (done), B4-1 (done) | `src/Starling.Js/Intrinsics/StringCtor.cs`, new `Intrinsics/RegExpStringIterator.cs` |
| **B3-2-followup-a** | Implement `break` / `continue` in the bytecode compiler. The compiler today doesn't emit either (a pre-existing M3-03 gap surfaced by B3-2). `iterator.return()` on abrupt for…of completion is wired in opcodes (`IteratorClose`) but currently unreachable. Pin tests: `for (var x of [1,2,3]) { if (x===2) break; sum += x }` → sum === 1; same with `continue`; same with a user iterable that records `return()` being invoked. **Files:** `Bytecode/JsCompiler.cs` (break/continue label stacks), `Bytecode/Opcode.cs` (maybe new `Jmp`-with-label variants if not present), `Runtime/JsVm.cs` (if any dispatch tweaks). | **lane A** | B3-2 (done) | `src/Starling.Js/Bytecode/JsCompiler.cs` |
| **B5-3-followup-a** | Expose a public `JsRuntime.WithActiveVm(Action body)` helper so bindings stop using the empty-chunk-through-`JsVm.Run` hack (currently in `TimersBinding`, `FetchBinding`, `XhrBinding`). The helper sets `realm.ActiveVm` via the existing internal setter, runs `body`, drains microtasks, and restores the previous `ActiveVm`. Then migrate the three bindings to call it. Pin a test that timer + fetch microtask drain still settle promise reactions correctly after the migration. | **lane F** | B5-2 (done), B5-3 (done) | `src/Starling.Js/Runtime/{JsRuntime,JsRealm}.cs`, `src/Starling.Bindings/{TimersBinding,FetchBinding,XhrBinding}.cs` |
| **B5-3-followup-b** | Revert the three `Directory.Build.props` NoWarn entries (RCS1194, CA1859, IDE0005) that B5-3 added as a temporary workaround for the in-flight B4-1 RegExp branch. RegExp has landed and the suite builds clean; the NoWarn block now silently hides real analyzer signal across the solution. Verify the suite still builds + tests pass after the revert; if any analyzer fires, fix the underlying code rather than re-adding the suppression. | (cleanup) | B4-1 (done), B5-3 (done) | `Directory.Build.props` |
| **B5-1-followup** | Migrate the array-shaped DOM result surfaces (`Element.children`, `Element.childNodes`, `Document.querySelectorAll`, `getElementsByTagName`, `getElementsByClassName`, `Headers.entries`/`keys`/`values`) from the old `MakeArrayLike` plain-object pattern to real `JsArray` (and, where the spec wants live collections / iterators, route through B3-2 iterator objects). B5-1 was written before B2-4 + B3-2 shipped, so it used the placeholder shape. Pin tests asserting `Array.isArray(document.querySelectorAll('*'))` and that the result behaves like an array (has `.map`, `.filter`, etc.). | **lane F** | B2-4 (done), B3-2 (done), B5-1 (done) | `src/Starling.Bindings/{NodeBindings,FetchBinding}.cs` |
### Engine gaps (surfaced 2026-05-19 by fixup agents)

The Proxy/Reflect, B5-5 location, and Map/Weak fixup passes all routed around the same family of compiler/VM gaps: tests in production-correct intrinsics still failed because the engine doesn't yet compile common JS forms. The fixups dodged the gaps in tests; the real fixes live here. Each row is a real bug, not a feature — they will break real-world JS bundles.

| ID | Title | Concurrency | Depends on | Files (primary) |
|---|---|---|---|---|
| **gap:try-catch** | `try` / `catch` / `finally` not compiled. `JsCompiler.EmitStatement` throws `NotSupportedException` on `TryStatement`. Cited in wp:M3-03 notes but not done. Pin tests: `try { throw new TypeError('x') } catch (e) { e.name === 'TypeError' }`. Required for every realistic error-handling path. | **lane A** | (none) | `src/Starling.Js/Bytecode/{Opcode,JsCompiler}.cs`, `src/Starling.Js/Runtime/JsVm.cs` (new exception-handling opcodes + try-frame stack) |
| **gap:instanceof** | `instanceof` operator not wired in the VM (wp:M3-05). Tests across `ErrorTests`, `ProxyTests`, `PromiseTests` use `Object.getPrototypeOf(e) === Foo.prototype` as a workaround. Spec: §13.10.2 — call `target[Symbol.hasInstance](value)` if present, else walk the prototype chain. Pin: `new TypeError() instanceof Error`. | **lane A** | (none) | `src/Starling.Js/Bytecode/{Opcode,JsCompiler}.cs`, `src/Starling.Js/Runtime/JsVm.cs` |
| **gap:delete** | `delete obj.x` operator not lowered to a Delete opcode. ProxyTests had to rewrite to use `Reflect.deleteProperty` instead. Pin: `var o={x:1}; delete o.x; 'x' in o === false`. | **lane A** | gap:in | `src/Starling.Js/Bytecode/{Opcode,JsCompiler}.cs`, `src/Starling.Js/Runtime/JsVm.cs` |
| **gap:in** | `in` operator (`'x' in obj`) is a wp:M3-05 stub. ProxyTests use `Reflect.has` as a workaround. Pin: `'a' in {a:1} === true`. | **lane A** | (none) | `src/Starling.Js/Bytecode/{Opcode,JsCompiler}.cs`, `src/Starling.Js/Runtime/JsVm.cs` |
| **gap:closure-write-back** | Closures snapshot enclosing locals **read-only** (wp:M3-04c2). `function outer() { var x=0; function inner() { x = 5 } inner(); return x }` returns 0, not 5. MapTests had to mutate through a captured object instead of a free variable. Real-world JS depends on closure write-back; this is the single biggest user-visible compiler bug remaining. | **lane A** | (none) | `src/Starling.Js/{Bytecode/JsCompiler.cs,Runtime/JsVm.cs}` (upvalue boxing / shared cell representation) |
| **gap:compound-assign-property** | Compound assignment on object properties (`obj.x += y`, `obj[k] *= 2`, etc.) re-reads the base via a snapshot, so the write goes to the snapshotted base, not the live property. Plain `obj.x = obj.x + y` works. Same root as the closure read-only issue? Probably sibling. Pin: `var o={x:1}; o.x += 5; o.x === 6`. | **lane A** | gap:closure-write-back (maybe) | `src/Starling.Js/Bytecode/JsCompiler.cs` (EmitAssignment for compound ops on member targets) |
| **gap:script-top-var-not-global** | A script-top `var` is compiled as a local, not a global. Nested functions reading or writing that name resolve to a different binding. Breaks any inline `<script>` that declares globals via `var`. Pin: `var x = 1; function read() { return x }; read() === 1`. | **lane A** | (none) | `src/Starling.Js/Bytecode/JsCompiler.cs` (top-level scope emission) |
| **gap:opcode-fast-path-bypasses-accessors** | `Opcode.LoadGlobal` (and possibly other property fast paths) consults `JsObject.Get`'s data-only path and silently returns `Undefined` for accessor descriptors. Surfaced when bare `location` was undefined while `window.location` worked (B5-5 fixup). Likely siblings: `LoadProperty` short-circuit on missing data slot, `LoadName` on global. Audit every "fast path" Get in the VM and route through `AbstractOperations.Get` when the descriptor is an accessor. Pin: `Object.defineProperty(globalThis, 'foo', {get: () => 42}); foo === 42`. | **lane Bytecode** | (none) | `src/Starling.Js/Runtime/JsVm.cs` (Load* opcodes), `src/Starling.Js/Runtime/JsObject.cs` |
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
- **Bare-name accessor globals resolve to undefined** (surfaced by B5-5).
  `Opcode.LoadGlobal` reads through `JsObject.Get` which returns `Undefined`
  for accessor descriptors instead of invoking the getter — so unqualified
  `location.href` evaluates to `(undefined).href` and throws. `window.location.href`
  works because dotted member access routes through `AbstractOperations.Get`.
  Most real-world bundles use the bare form. Fix: switch `LoadGlobal` to
  `AbstractOperations.Get(_runtime.Realm.GlobalObject, name, JsValue.Object(global))`
  (or equivalent VM-aware path) so accessors fire. Same applies to `StoreGlobal`
  for accessor setters (`location.href = ...`, `history.scrollRestoration = ...`).
  File as a small standalone WP — touches `JsVm.cs` only, with regression tests
  in `WindowDocumentTests`.

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
