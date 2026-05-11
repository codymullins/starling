---
id: "wp:M2-02-dns"
milestone: "M2"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T17:10:00Z"
branch: "wp-M2-02-dns"
depends_on:
  - "wp:M2-01-url-parser"
blocks:
  - "wp:M2-05-http1"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#dns"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-02-dns"
---

# wp:M2-02 — DNS

## Goal
A/AAAA resolution against system resolver and explicit Do-not-care-about-DoH
v1; small local cache.

## Acceptance
Resolves `example.com`, `localhost`. 10 unit tests + 1 integration test.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T17:10Z — unblocked by wp:M2-01a completion (URL parser
  state machine merged to main). Claimed by agent-claude-cody.
  Branch `wp-M2-02-dns`. Claim posted as its own commit per AGENTS.md.
