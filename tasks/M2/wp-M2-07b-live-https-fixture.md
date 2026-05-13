---
id: "wp:M2-07b-live-https-fixture"
parent: "wp:M2-07-network-end-to-end"
milestone: "M2"
status: "complete"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-13T14:40:55Z"
branch: "main"
depends_on:
  - "wp:M2-07a-img-fetch-decode-paint"
blocks: []
subsystem: "Tessera.Engine"
plan_refs:
  - "browser-plan/13_MILESTONES.md#m2--networking-and-live-html"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-07-network-end-to-end"
  - "browser-plan/12_TESTING.md"
completed_at: "2026-05-13T14:51:47Z"
---

# wp:M2-07b — Live HTTPS render fixture + SSIM gate

## Goal

Lock the M2 exit demo (`tessera render https://example.com -o out.png`)
behind a CI signal, plus capture one snapshot-vendored fixture of a richer
real-world page so we can keep regressing it offline.

## Inputs

- TLS 1.3 + HTTP/1.1 client fully wired (wp:M2-04, wp:M2-05 ✓).
- `<img>` paint (wp:M2-07a) — required for the snapshot fixture to be
  visually faithful.

## Outputs

- `tests/Tessera.Engine.Tests/EngineLiveHttpsTests.cs` — one xUnit test
  attribute-gated on a `TESSERA_ALLOW_NETWORK` env var (skipped by default
  locally, enabled in a dedicated CI job). It calls
  `engine.RenderAsync("https://example.com")`, writes a PNG, and asserts
  SSIM ≥ 0.99 against `testdata/golden/live/example.com.png`.
- `testdata/golden/live/example.com.png` — checked-in baseline.
- `testdata/snapshots/anthropic.com/` — vendored HTML + every subresource
  (CSS, images, fonts) needed to render the marketing front page through
  the local fixture server pattern already used in
  `EngineHttpTests`. Include a `manifest.json` capturing each URL,
  timestamp, content-type, and SHA-256 so re-vendoring is auditable.
- `tests/Tessera.Engine.Tests/EngineSnapshotRenderTests.cs` — renders the
  vendored anthropic.com snapshot through a local stub server and asserts
  byte-exact PNG against
  `testdata/golden/snapshots/anthropic.com.png` (or SSIM ≥ 0.99 if AA noise
  is unavoidable across platforms).
- `src/Tessera.Common/Image/Ssim.cs` (or similar) — small pure-managed SSIM
  implementation (window-based; ImageSharp gives us pixel access). If
  byte-exact comparison suffices for the snapshot variant, keep SSIM scoped
  to the live test.
- `.github/workflows/ci.yml` — opt-in `network-tests` job that exports
  `TESSERA_ALLOW_NETWORK=1` so the live test runs there but not on the
  default matrix.

## Acceptance

- Local `dotnet test` (no env var) skips the live test, runs the snapshot
  test, and stays green.
- CI `network-tests` job runs the live test and passes against
  `example.com`.
- The snapshot test fails if any rendered byte differs more than the SSIM
  threshold from its golden — catching regressions in HTML/CSS/layout/paint
  changes.
- Manifest contents reproducibly identify the captured page version (a
  re-vendor can diff against the prior manifest).

## Notes

- Use a per-snapshot stub HTTP server that serves files from
  `testdata/snapshots/<host>/` keyed by the captured paths. The harness
  already has this shape in `EngineHttpTests`.
- SSIM threshold 0.99 is the milestone spec; if AA fonts produce wider drift
  on a given platform, widen the window or pin platform-specific goldens.
- Anthropic.com is one example pick; if it ships heavy JS that breaks the
  snapshot test, swap to a simpler marketing page (e.g., the
  `nginx.org` index) and note in handoff. Defer to whoever claims this.
- This package does NOT cover connection reuse — wp:M2-07c owns that.

## Handoff log

- 2026-05-13T00:00Z — agent-claude-cody, filed during MVP-path planning
  split-out of the catch-all wp:M2-07-network-end-to-end. Available to
  claim after wp:M2-07a lands.
- 2026-05-13T14:40:55Z — claimed by agent-claude-cody, working on main
- 2026-05-13T14:51:47Z — merged; complete
