---
id: "wp:M3-06f-docs-policy"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "complete"
claimed_by: "agent-claude-cody-docs"
claimed_at: "2026-05-14T14:43:47Z"
completed_at: "2026-05-14T14:43:47Z"
branch: "main"
depends_on: []
blocks: []
subsystem: "docs"
plan_refs:
  - "browser-plan/01_ARCHITECTURE.md#project-layout"
  - "browser-plan/02_PROJECT_SETUP.md#repo-hygiene"
  - "browser-plan/03_NETWORKING.md#tls-approach"
  - "browser-plan/08_FONTS_PAINT.md#raster-backend"
  - "browser-plan/10_WEB_APIS.md#crypto"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06f-docs-policy — rewrite docs for the interop seam policy

## Goal

Phase 10 (docs half): rewrite the project documentation to retire "Rule 0" and
describe the new **interop seam policy** — "managed-first, native at vetted
seams." Native `LibraryImport` is confined to `Tessera.Skia` and
`Tessera.Codecs`; every other engine project stays P/Invoke-free; `SslStream` is
pure-managed BCL so `Tessera.Net` keeps its clean bill. This package describes
the target state and can land early — it does not depend on any code being
written.

## Inputs

- No code dependencies; describes the target state of the whole pivot.
- The master plan's policy section and per-doc edit list (Phase 10).

## Outputs

- `README.md` — rewrite the "Rule 0" section to the interop seam policy.
- `AGENTS.md` (~lines 88–99) — update the purity rules to the seam policy.
- `browser-plan/03_NETWORKING.md` — rewrite the whole "## Rule 0 reminder"
  section into "## TLS approach: SslStream"; the `HttpClient` ban stays.
- `browser-plan/08_FONTS_PAINT.md` — update for OS-native codecs + Skia raster.
- `browser-plan/10_WEB_APIS.md` — crypto footnote: `crypto.subtle` →
  `System.Security.Cryptography`.
- `browser-plan/02_PROJECT_SETUP.md` — update the CI block + hygiene rules
  prose for the project allowlist.
- `browser-plan/13_MILESTONES.md` — update the M2 TLS/codec lines and add the
  M3 native-interop pivot.
- `browser-plan/09_JS_ENGINE.md` — prose-only touch (its grep stays valid).

## Acceptance

- "Rule 0" no longer appears as the governing policy in `README.md`,
  `AGENTS.md`, or the `browser-plan/*` files above — replaced by
  "managed-first, native at vetted seams."
- `browser-plan/03_NETWORKING.md` has a "## TLS approach: SslStream" section;
  the `HttpClient` ban is still documented.
- The two vetted interop projects (`Tessera.Skia`, `Tessera.Codecs`) are named
  as the only `LibraryImport`-allowed projects everywhere the policy is stated.
- `10_WEB_APIS.md` crypto footnote points at `System.Security.Cryptography`.
- No code, csproj, or CI-config changes in this package (CI config is `06l`).

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 10,
  "Docs rewrite").
- This is documentation only — the matching CI test/lint rewrites
  (`12_TESTING.md`, `ci.yml`) are owned by `06l-ci-policy`. Keep the two in sync
  conceptually but they merge separately.
- Safe to land before any native code exists; it describes where the project is
  going.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
- 2026-05-14T14:43:47Z — completed (agent-claude-cody-docs). Rewrote the
  Rule-0 framing across `README.md`, `AGENTS.md`,
  `browser-plan/{02,03,08,10,12,13}_*.md` to the interop seam policy
  ("managed-first, native at vetted seams"). `03_NETWORKING.md` "## Rule 0
  reminder" became "## TLS approach" (SslStream behind `ITlsTransport`, TLS 1.3
  only, bundled CCADB `CustomTrustStore`); the `HttpClient` ban is kept explicit.
  `09_JS_ENGINE.md` needed no edit — it has no "Rule 0" reference and its
  pure-managed claims + acceptance grep stay valid (`Tessera.Js` is not an
  interop project).
  Verification: `grep -rn "Rule 0\|Rule-0" README.md AGENTS.md browser-plan/`
  returns only (a) the `RuleZeroTests` class name in `12_TESTING.md` — that is
  test code, explicitly owned by `06l-ci-policy`, and the surrounding prose now
  notes it as a pre-pivot reference shape; and (b) two "Rule-0 lint passes"
  acceptance lines in `browser-plan/14_AGENT_TASKS.md` — out of this WP's scope
  (not in the per-doc edit list; that file is the package catalog). Both left
  intentionally for `06l` / a catalog-maintenance pass.
  No code, csproj, sln, CI workflow, or `tasks/INDEX.md` changes.
