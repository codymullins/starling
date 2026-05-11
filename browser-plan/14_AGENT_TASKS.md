# 14 — Agent Tasks

## Purpose

This is the work-package backlog. Each package is a **self-contained unit of work** an implementation agent can pick up. Each package:

- Has a single owner at a time.
- Has explicit inputs (files/PRs that must exist), outputs (files/types/PRs), and acceptance criteria.
- States its dependencies on other packages.
- Maps to one or more milestone exit criteria from [13_MILESTONES.md](13_MILESTONES.md).

## How agents work this list

1. Read [00_INDEX.md](00_INDEX.md) → [01_ARCHITECTURE.md](01_ARCHITECTURE.md) → [13_MILESTONES.md](13_MILESTONES.md).
2. Find the current milestone in [13_MILESTONES.md](13_MILESTONES.md).
3. Pick an unblocked package in that milestone here.
4. Claim it by creating a tracking issue or PR with `wp:<id>` in the title.
5. Implement, write tests, open PR, mark package complete.

## Conventions

- `wp:` = "work package".
- IDs: `wp:M<milestone>-<seq>-<short-name>`, e.g. `wp:M0-01-scaffold`.
- "Inputs" lists package IDs and/or file paths.
- "Outputs" lists files/types you must create.
- "Acceptance" mirrors the per-doc acceptance tests.

## M0 — Walking skeleton

### wp:M0-01-scaffold

- **Inputs**: none.
- **Subsystem**: [02_PROJECT_SETUP.md](02_PROJECT_SETUP.md).
- **Outputs**: `Tessera.sln`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `.github/workflows/ci.yml`, all 13 source + 13 test + 1 e2e + 1 bench + 1 headless projects.
- **Acceptance**: `dotnet build` + `dotnet test` exit 0 on all three OS matrix. Rule-0 lint passes.

### wp:M0-02-common

- **Inputs**: wp:M0-01-scaffold.
- **Subsystem**: [01_ARCHITECTURE.md](01_ARCHITECTURE.md) (Common module shape).
- **Outputs**: `src/Tessera.Common/{Logger,Result,Maybe,CodePoint,Rope}.cs`.
- **Acceptance**: 10 unit tests covering `Result<T>`, `Maybe<T>`, and `Rope`.

### wp:M0-03-paint-stub

- **Inputs**: wp:M0-02-common.
- **Subsystem**: [08_FONTS_PAINT.md](08_FONTS_PAINT.md) — minimal slice only.
- **Outputs**:
  - `src/Tessera.Paint/Painter.cs` with a `RenderHelloWorld(string text, Size viewport) → Image<Rgba32>` method.
  - Bundle 1 font (Inter) at `src/Tessera.Paint/Resources/Fonts/Inter.ttf`.
- **Acceptance**: Headless CLI renders a fixed string into a PNG. PNG hash matches golden `testdata/golden/000-hello.png`.

### wp:M0-04-headless-cli

- **Inputs**: wp:M0-03-paint-stub.
- **Subsystem**: [02_PROJECT_SETUP.md headless CLI](02_PROJECT_SETUP.md#headless-cli-shape).
- **Outputs**: `src/Tessera.Headless/Program.cs` implementing `render` subcommand only.
- **Acceptance**: `tessera render --hello -o out.png` exits 0 and writes a PNG.

## M1 — HTML+CSS static rendering

### wp:M1-01-html-tokenizer

- **Inputs**: wp:M0-01-scaffold.
- **Subsystem**: [04_HTML_PARSING.md tokenizer](04_HTML_PARSING.md#tokenizer).
- **Outputs**: `src/Tessera.Html/Tokenizer/*` + a build-time tool `tools/gen-entities/Program.cs` that generates `NamedCharacterReferences.cs` from `testdata/spec/html-entities.json`.
- **Acceptance**: html5lib tokenizer suite **100%**.

### wp:M1-02-html-tree-builder

- **Inputs**: wp:M1-01-html-tokenizer, wp:M1-03-dom-core.
- **Subsystem**: [04_HTML_PARSING.md tree builder](04_HTML_PARSING.md#tree-builder).
- **Outputs**: `src/Tessera.Html/TreeBuilder/*`.
- **Acceptance**: html5lib tree-construction suite ≥ 95%; adoption-agency tests in `tests1.dat` 100%.

### wp:M1-03-dom-core

- **Inputs**: wp:M0-02-common.
- **Subsystem**: [05_DOM.md](05_DOM.md) (Node/Element/Document only).
- **Outputs**: `src/Tessera.Dom/{Node,NodeKind,Element,Document,Text,Comment,NamedNodeMap,Attr}.cs` plus mutation primitives.
- **Acceptance**: WPT `dom/nodes/Node-*` ≥ 90% (excluding events).

### wp:M1-04-dom-events

- **Inputs**: wp:M1-03-dom-core.
- **Subsystem**: [05_DOM.md events](05_DOM.md#events).
- **Outputs**: `src/Tessera.Dom/Events/*`.
- **Acceptance**: WPT `dom/events/**` (core dispatch) ≥ 95%.

### wp:M1-05-css-tokenizer-parser

- **Inputs**: wp:M0-02-common.
- **Subsystem**: [06_CSS.md tokenizer + parser](06_CSS.md#tokenizer).
- **Outputs**: `src/Tessera.Css/Tokenizer/*`, `src/Tessera.Css/Parser/*`.
- **Acceptance**: WPT `css/css-syntax/**` ≥ 80%.

### wp:M1-06-css-selectors

- **Inputs**: wp:M1-05-css-tokenizer-parser, wp:M1-03-dom-core.
- **Subsystem**: [06_CSS.md selectors](06_CSS.md#selectors).
- **Outputs**: `src/Tessera.Css/Selectors/*`.
- **Acceptance**: WPT `css/selectors/**` ≥ 80% (v1 subset).

### wp:M1-07-css-cascade

- **Inputs**: wp:M1-06-css-selectors.
- **Subsystem**: [06_CSS.md cascade](06_CSS.md#cascade).
- **Outputs**: `src/Tessera.Css/Cascade/*`, `src/Tessera.Css/Properties/*`, `src/Tessera.Css/Values/*`, `src/Tessera.Css/UserAgent/UaStyleSheet.cs`.
- **Acceptance**: WPT `css/css-cascade/**` ≥ 80%.

### wp:M1-08-layout-block-inline

- **Inputs**: wp:M1-07-css-cascade, wp:M0-03-paint-stub.
- **Subsystem**: [07_LAYOUT.md block + inline](07_LAYOUT.md#block-formatting-context-bfc).
- **Outputs**: `src/Tessera.Layout/{LayoutEngine,Box,Tree/BoxTreeBuilder,Block/*,Inline/*,Sizing/*}.cs`.
- **Acceptance**: 20 layout golden tests pass (block + inline + margin collapse + simple text wrap).

### wp:M1-09-paint-display-list

- **Inputs**: wp:M1-08-layout-block-inline.
- **Subsystem**: [08_FONTS_PAINT.md display list](08_FONTS_PAINT.md#display-list).
- **Outputs**: `src/Tessera.Paint/DisplayList/*`, `src/Tessera.Paint/Backend/ImageSharpBackend.cs`, `src/Tessera.Paint/Text/*`.
- **Acceptance**: Render the M1 golden suite (≥20 cases) within tolerance.

## M2 — Networking

### wp:M2-01-url-parser

- **Inputs**: wp:M0-02-common.
- **Subsystem**: [03_NETWORKING.md URL](03_NETWORKING.md#url-parsing).
- **Outputs**: `src/Tessera.Url/Url.cs` and friends. Build-time fetch of `testdata/spec/urltestdata.json` from WPT.
- **Acceptance**: WPT `url/urltestdata.json` 100%.

### wp:M2-02-dns

- **Inputs**: wp:M2-01-url-parser.
- **Subsystem**: [03_NETWORKING.md DNS](03_NETWORKING.md#dns).
- **Outputs**: `src/Tessera.Net/Dns/*`.
- **Acceptance**: Resolves `example.com`, `localhost`. 10 unit tests + 1 integration test.

### wp:M2-03-tcp

- **Inputs**: wp:M2-01-url-parser.
- **Subsystem**: [03_NETWORKING.md TCP](03_NETWORKING.md#tcp).
- **Outputs**: `src/Tessera.Net/Tcp/*`.
- **Acceptance**: Opens TCP to `example.com:80`, GET / over plaintext returns 200.

### wp:M2-04-tls

- **Inputs**: wp:M2-03-tcp.
- **Subsystem**: [03_NETWORKING.md TLS](03_NETWORKING.md#tls-13-pure-managed).
- **Outputs**: `src/Tessera.Net/Tls/*` including bundled root store at `src/Tessera.Net/Resources/Roots/ccadb.pem`.
- **Acceptance**: TLS 1.3 handshake to `cloudflare.com`, `tls13.akamai.io`. SNI + ALPN advertisement correct. Cert validation fails on a known-bad chain.

### wp:M2-05-http1

- **Inputs**: wp:M2-04-tls.
- **Subsystem**: [03_NETWORKING.md HTTP/1.1](03_NETWORKING.md#http11).
- **Outputs**: `src/Tessera.Net/Http/H1/*`, `src/Tessera.Net/Http/Decoding/*`.
- **Acceptance**: GET `https://example.com` returns 200 + body matches reference Chromium response (modulo non-deterministic headers).

### wp:M2-06-cookies

- **Inputs**: wp:M2-05-http1.
- **Subsystem**: [03_NETWORKING.md cookies](03_NETWORKING.md#cookies-rfc-6265bis).
- **Outputs**: `src/Tessera.Net/Http/Cookies/*` including bundled PSL at `Resources/psl/effective_tld_names.dat`.
- **Acceptance**: WPT `cookies/` ≥ 80%.

### wp:M2-07-network-end-to-end

- **Inputs**: wp:M2-05-http1, wp:M1-09-paint-display-list.
- **Subsystem**: [01_ARCHITECTURE.md data flow](01_ARCHITECTURE.md#data-flow-url--pixels).
- **Outputs**: `src/Tessera.Engine/*` wiring; `tessera render https://...` works.
- **Acceptance**: `tessera render https://example.com` renders a recognizable example.com page. 5 golden tests against live (snapshot-vendored) responses.

## M3 — JavaScript engine

### wp:M3-01-js-lexer

- **Inputs**: wp:M0-02-common.
- **Subsystem**: [09_JS_ENGINE.md lexer](09_JS_ENGINE.md#lexer).
- **Outputs**: `src/Tessera.Js/Lex/*`.
- **Acceptance**: Test262 lexer category 100%; FsCheck property tests on random valid sources.

### wp:M3-02-js-parser

- **Inputs**: wp:M3-01-js-lexer.
- **Subsystem**: [09_JS_ENGINE.md parser](09_JS_ENGINE.md#parser).
- **Outputs**: `src/Tessera.Js/Parse/*`.
- **Acceptance**: Parses 100% of Test262 valid sources; rejects 100% of invalid; early-errors set correct.

### wp:M3-03-js-compiler

- **Inputs**: wp:M3-02-js-parser.
- **Subsystem**: [09_JS_ENGINE.md bytecode](09_JS_ENGINE.md#bytecode-ir).
- **Outputs**: `src/Tessera.Js/Bytecode/*`.
- **Acceptance**: Snapshot tests on 30 hand-picked source files matching expected bytecode dumps.

### wp:M3-04-js-vm

- **Inputs**: wp:M3-03-js-compiler.
- **Subsystem**: [09_JS_ENGINE.md VM](09_JS_ENGINE.md#vm).
- **Outputs**: `src/Tessera.Js/Runtime/*`.
- **Acceptance**: Sub-Test262 (language/expressions, language/statements) ≥ 80%.

### wp:M3-05-js-intrinsics

- **Inputs**: wp:M3-04-js-vm.
- **Subsystem**: [09_JS_ENGINE.md intrinsics](09_JS_ENGINE.md#realms-and-intrinsics).
- **Outputs**: `src/Tessera.Js/Intrinsics/*`.
- **Acceptance**: Test262 built-ins ≥ 80%.

### wp:M3-06-js-regexp

- **Inputs**: wp:M3-05-js-intrinsics.
- **Subsystem**: [09_JS_ENGINE.md RegExp](09_JS_ENGINE.md#regexp).
- **Outputs**: `src/Tessera.Js/RegExp/*`. Build-time UCD generator at `tools/gen-ucd/`.
- **Acceptance**: Test262 `built-ins/RegExp/**` ≥ 80%.

### wp:M3-07-js-async

- **Inputs**: wp:M3-05-js-intrinsics.
- **Subsystem**: [09_JS_ENGINE.md async/await](09_JS_ENGINE.md#async--await--generators).
- **Outputs**: `src/Tessera.Js/Async/*`, microtask queue plumbing.
- **Acceptance**: `await`/`async` tests in Test262 ≥ 80%.

### wp:M3-08-js-modules

- **Inputs**: wp:M3-07-js-async, wp:M2-07-network-end-to-end.
- **Subsystem**: [09_JS_ENGINE.md modules](09_JS_ENGINE.md#modules).
- **Outputs**: Module loader in `src/Tessera.Js/Runtime/ModuleRegistry.cs` and wiring to fetch sources.
- **Acceptance**: 20 hand-picked module test cases pass; circular import returns correct partial bindings.

## M4 — DOM bindings + Web APIs

### wp:M4-01-event-loop

- **Inputs**: wp:M3-07-js-async, wp:M0-02-common.
- **Subsystem**: [10_WEB_APIS.md event loop](10_WEB_APIS.md#event-loop).
- **Outputs**: `src/Tessera.Loop/*`.
- **Acceptance**: Microtask ordering matches Chrome on 20 hand-picked fixtures.

### wp:M4-02-dom-bindings-core

- **Inputs**: wp:M3-08-js-modules, wp:M1-03-dom-core, wp:M4-01-event-loop.
- **Subsystem**: [10_WEB_APIS.md DOM bindings](10_WEB_APIS.md#dom-bindings--the-bridge).
- **Outputs**: `src/Tessera.Bindings/Bindings/{Window,Document,Node,Element,...}.cs`.
- **Acceptance**: A hand-picked set of DOM APIs (querySelector, addEventListener, dispatchEvent, innerHTML, classList, dataset) work via JS in `tessera js`.

### wp:M4-03-fetch

- **Inputs**: wp:M4-02-dom-bindings-core, wp:M2-07-network-end-to-end.
- **Subsystem**: [10_WEB_APIS.md fetch](10_WEB_APIS.md#fetch).
- **Outputs**: `src/Tessera.Loop/Fetch/*`, bindings.
- **Acceptance**: WPT `fetch/api/**` ≥ 70%.

### wp:M4-04-observers

- **Inputs**: wp:M4-02-dom-bindings-core.
- **Subsystem**: [10_WEB_APIS.md observers](10_WEB_APIS.md#observers).
- **Outputs**: MutationObserver, IntersectionObserver, ResizeObserver bindings + integration with the event loop's render step.
- **Acceptance**: WPT `mutation-observer/`, `intersection-observer/`, `resize-observer/` ≥ 80%.

### wp:M4-05-history-and-storage

- **Inputs**: wp:M4-02-dom-bindings-core.
- **Subsystem**: [10_WEB_APIS.md history + storage](10_WEB_APIS.md#history-api).
- **Outputs**: HistoryBinding, LocationBinding, StorageBinding.
- **Acceptance**: `history.pushState` + `popstate` round-trip; `localStorage` persists across process restarts.

## M5 — Avalonia shell

### wp:M5-01-shell-skeleton

- **Inputs**: wp:M4-02-dom-bindings-core.
- **Subsystem**: [11_AVALONIA_SHELL.md](11_AVALONIA_SHELL.md).
- **Outputs**: `src/Tessera.Shell/Program.cs`, MainWindow, UrlBar, TabStrip + the EngineSurface bitmap pipeline.
- **Acceptance**: Launch on win/mac/linux. Type a URL, press Enter, page renders.

### wp:M5-02-shell-input

- **Inputs**: wp:M5-01-shell-skeleton.
- **Subsystem**: [11_AVALONIA_SHELL.md input translation](11_AVALONIA_SHELL.md#input-translation).
- **Outputs**: `EngineHost`, `InputTranslator`. Click → DOM `click` event chain.
- **Acceptance**: A counter fixture page works interactively via clicks.

### wp:M5-03-shell-tabs

- **Inputs**: wp:M5-02-shell-input.
- **Subsystem**: [11_AVALONIA_SHELL.md tabs](11_AVALONIA_SHELL.md#tabs).
- **Outputs**: Multi-tab state + Avalonia bindings.
- **Acceptance**: 5 tabs open simultaneously, switching is < 200ms.

## M6, M7, M8 packages

Refer back to the milestone exit criteria; agents will plan these packages as they enter each milestone. The shape mirrors what's above.

Sketch of M6:
- wp:M6-01-http2-framing
- wp:M6-02-hpack
- wp:M6-03-flow-control
- wp:M6-04-websocket
- wp:M6-05-cookies-final

Sketch of M7:
- wp:M7-01-js-perf-pass-1 (inline caches, shape transitions)
- wp:M7-02-css-flex-final
- wp:M7-03-css-grid
- wp:M7-04-intersection-observer-final
- wp:M7-05-history-state
- wp:M7-06-crypto-subtle-min
- wp:M7-07-google-search-fixture

Sketch of M8:
- wp:M8-01-workers
- wp:M8-02-wasm-v0
- wp:M8-03-dynamic-import
- wp:M8-04-claude-ai-fixture
- wp:M8-05-perf-pass-2

## Parallelization map

Independent at start (can all begin once wp:M0-01 and wp:M0-02 are done):

```
M1-01 (HTML tokenizer)  M1-05 (CSS tokenizer/parser)
M2-01 (URL)             M3-01 (JS lexer)
M0-03 (Paint stub)
```

Critical path:
```
M0-01 → M0-02 → M1-03 (DOM) → M1-02 (tree builder) → M1-07 (cascade) →
M1-08 (layout) → M1-09 (paint) → M2-07 → M3-04 → M4-02 → M4-03 → M5-01 → M6 → M7 → M8
```

With 4 agents in parallel, expected throughput at any time:
1. Critical-path agent driving the next milestone.
2. Backfill agent on the deepest subsystem (JS).
3. Test/conformance agent expanding the WPT/Test262 harness.
4. Tooling/infra agent on CI, golden suites, fuzzing.

## Definition of "done" for any package

1. PR opened, linked to the package ID.
2. New tests added that exercise the new code (unit + golden if applicable).
3. `dotnet test` green locally.
4. CI green on all three OS matrix.
5. Rule-0 lint passes.
6. Doc updates if the package's design changed from the plan (file a delta in the doc).
7. Owner marks the package complete and lists the next unblocked work in their PR body.
