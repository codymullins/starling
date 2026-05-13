---
id: "wp:M2-07d-encoding-hardening"
parent: "wp:M2-07-network-end-to-end"
milestone: "M2"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-13T14:40:25Z"
branch: "main"
depends_on:
  - "wp:M2-05-http1"
blocks: []
subsystem: "Tessera.Engine"
plan_refs:
  - "browser-plan/03_NETWORKING.md#encoding-sniffing"
  - "browser-plan/04_HTML_PARSING.md#encoding"
  - "browser-plan/13_MILESTONES.md#m2--networking-and-live-html"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-07-network-end-to-end"
---

# wp:M2-07d — Encoding hardening

## Goal

Cover the WHATWG Encoding Standard label set well enough to clear the M2
exit criterion "WPT `encoding/` ≥ 95% on labels we support". The current
`TesseraEngine.TryResolveEncoding` handles the UTF + ASCII + Latin-1 family
but does NOT register `System.Text.Encoding.CodePages`, so any legacy
single-byte or East-Asian label (windows-1252, ISO-8859-2…16, shift_jis,
gbk, big5, euc-kr, …) silently falls back to UTF-8 with replacement chars.

## Inputs

- `TesseraEngine.TryResolveEncoding` and its existing test theory
  `ResolveEncoding_handles_common_inputs`.
- The WHATWG Encoding Standard label table:
  <https://encoding.spec.whatwg.org/#names-and-labels>.

## Outputs

- `src/Tessera.Engine/Engine.cs` — register
  `Encoding.RegisterProvider(CodePagesEncodingProvider.Instance)` once at
  engine bootstrap. Extend the label-to-Encoding mapping to cover, at
  minimum, the legacy labels enumerated by the WHATWG table that map to
  CodePages-backed encodings:
  - `windows-1250` … `windows-1258`
  - `iso-8859-2` … `iso-8859-16` (excluding 1 / 9 which BCL handles via
    Latin-1)
  - `shift_jis`, `shift-jis`, `sjis`, `ms_kanji`
  - `gbk`, `gb18030`, `gb2312`
  - `big5`, `big5-hkscs`
  - `euc-kr`
  - `koi8-r`, `koi8-u`
  - `macintosh`, `x-mac-cyrillic`
- `src/Tessera.Common/Encoding/WhatwgEncodingLabels.cs` (new) — a
  generated-style lookup table mapping every WHATWG label alias to its
  canonical encoding name. Single source of truth shared by HTTP header
  charset sniffing, meta charset sniffing, and BOM sniffing.
- `tests/Tessera.Engine.Tests/EngineHttpTests.cs` — extend
  `ResolveEncoding_handles_common_inputs` with the new label families.
- `tests/Tessera.Engine.Tests/EngineEncodingTests.cs` (new) — drive a
  curated subset of WPT `encoding/` (vendor under
  `testdata/wpt/encoding/`) and assert ≥ 95% pass on the supported label
  set. Document any specific subtests excluded and why.

## Acceptance

- `ResolveEncoding` returns the correct `System.Text.Encoding` for every
  WHATWG label we claim to support, validated by ≥ 30 theory cases.
- WPT `encoding/` subset: at least 95% of the subtests we run pass.
- A fixture HTML page declaring `<meta charset="windows-1252">` and
  containing a smart quote (`0x92`) renders the smart quote as `’`, not
  `?` or U+FFFD.
- Full repo `dotnet test` stays green.

## Notes

- `System.Text.Encoding.CodePages` is a separate NuGet package, but it is
  pure managed (.NET BCL) and Rule-0 clean. Verify the CI Rule-0 grep
  still passes after adding the reference.
- BOM detection already happens upstream; this package handles labels.
- The WHATWG table is the spec authority. Generate the lookup table
  programmatically (one-shot script or a comment in the source file
  pointing at the spec table revision) rather than hand-typing — it is
  large enough that hand-rolling will drift.
- Defer the legacy `replacement` encoding (a deliberately-failing decoder
  for known-broken labels) unless WPT requires it.

## Handoff log

- 2026-05-13T00:00Z — agent-claude-cody, filed during MVP-path planning
  split-out of the catch-all wp:M2-07-network-end-to-end. Available to
  claim. Picks up the open follow-up noted in the original wp:M2-07
  handoff log dated 2026-05-12T22:30Z.
- 2026-05-13T14:40:25Z — claimed by agent-claude-cody, working on main
