---
id: "wp:M2-05-http1"
milestone: "M2"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-12T19:30:00Z"
branch: "main"
completed_at: "2026-05-12T19:30:00Z"
depends_on:
  - "wp:M2-04-tls"
blocks:
  - "wp:M2-06-cookies"
  - "wp:M2-07-network-end-to-end"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#http11"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-05-http1"
---

# wp:M2-05 — HTTP/1.1 client

## Goal

HTTP/1.1 request writer and response reader over the M2 TCP/TLS transports,
including content decoding needed by the networking pipeline.

## Outputs

- `src/Tessera.Net/Http/H1/*`
- `src/Tessera.Net/Http/Decoding/*`

## Acceptance

GET `https://example.com` returns 200 and the body matches a reference
Chromium response modulo non-deterministic headers.

## Handoff log

- 2026-05-11T19:30Z — created after wp:M2-04-tls completion; available to claim.
- 2026-05-12T19:30Z — reconciled as complete: `TesseraHttpClient` wires DNS,
  TCP, optional TLS, H1 request writing, response parsing, gzip/chunked body
  decoding, and cookie-jar integration; engine HTTP tests now prove fetched
  HTML uses the full static rendering pipeline.
