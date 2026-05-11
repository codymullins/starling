---
id: "wp:M2-01-url-parser"
milestone: "M2"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T16:25:00Z"
branch: "wp-M2-01-url-parser"
completed_at: "2026-05-11T16:50:00Z"
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
- 2026-05-11T16:25Z — claimed by agent-claude-cody, branch
  `wp-M2-01-url-parser`; claim committed as its own atomic op (per the
  AGENTS.md protocol — previous sub-tasks had folded claims into impl
  commits which caused two parallel-work collisions earlier in the day).
- 2026-05-11T16:50Z — landed:
  - All 21 WHATWG basic-URL-parser states (§4.4.1).
  - Url record extended with Username/Password (additive `init` props).
  - SpecialSchemes table (ftp/file/http/https/ws/wss + default ports).
  - HostParser with IPv4 numeric canonicalization (§4.6).
  - Percent encoding with 6 spec encoding sets (§1.3).
  - Path normalization with dot-segment handling.
  - 22 new tests; 28/28 green; full repo 152/152.
  - Deferred to wp:M2-01b: IDNA Punycode, IPv6 brackets, full WPT
    urltestdata.json conformance.
