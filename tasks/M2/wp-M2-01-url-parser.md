---
id: "wp:M2-01-url-parser"
milestone: "M2"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T16:25:00Z"
branch: "wp-M2-01-url-parser"
depends_on:
  - "wp:M0-02-common"
blocks:
  - "wp:M2-02-dns"
  - "wp:M2-03-tcp"
subsystem: "Tessera.Url"
plan_refs:
  - "browser-plan/03_NETWORKING.md#url-parsing"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-01-url-parser"
---

# wp:M2-01 — Full WHATWG URL parser

## Goal
Replace the M0 minimal `UrlParser` with the full WHATWG URL spec
implementation, plus a build-time fetch of `urltestdata.json` from WPT.

## Outputs
- `src/Tessera.Url/Url.cs` (expanded)
- `src/Tessera.Url/UrlParser.cs` (replaced)
- `src/Tessera.Url/Authority/*`, `IDNA/*`, `Percent/*`, etc.
- `testdata/spec/urltestdata.json`

## Acceptance
WPT `url/urltestdata.json`: 100%.

## Notes
The M0 `Url` record (Scheme/Host/Port/Path/Query/Fragment) is a structural
fit — replace internals, keep the public shape if practical (preserves the
existing `using TesseraUrl = global::Tessera.Url.Url;` alias in
`Tessera.Net`).

## Handoff log
- 2026-05-11T15:20Z — created.
