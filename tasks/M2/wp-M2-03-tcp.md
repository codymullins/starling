---
id: "wp:M2-03-tcp"
milestone: "M2"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-11T17:30:00Z"
branch: "wp-M2-03-tcp"
depends_on:
  - "wp:M2-01-url-parser"
blocks:
  - "wp:M2-04-tls"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#tcp"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-03-tcp"
---

# wp:M2-03 — TCP transport

## Goal
Async TCP client around `System.Net.Sockets`. Connection pool seam.

## Acceptance
Opens TCP to `example.com:80`, GET / over plaintext returns 200.

## Handoff log
- 2026-05-11T15:20Z — created.
- 2026-05-11T17:30Z — unblocked by wp:M2-01a + wp:M2-02 completion.
  Claimed by agent-claude-cody. Branch `wp-M2-03-tcp`. Atomic
  claim commit per AGENTS.md before any implementation work.
