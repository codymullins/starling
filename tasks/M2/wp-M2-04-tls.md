---
id: "wp:M2-04-tls"
milestone: "M2"
status: "complete"
claimed_by: "agent-copilot-gpt-5.5"
claimed_at: "2026-05-11T19:24:07Z"
branch: "wp-M2-04-tls"
completed_at: "2026-05-11T19:30:00Z"
depends_on:
  - "wp:M2-03-tcp"
blocks:
  - "wp:M2-05-http1"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#tls-13-pure-managed"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-04-tls"
---

# wp:M2-04 — TLS 1.3 transport

## Goal

Pure-managed TLS 1.3 client transport over the M2 TCP seam using
BouncyCastle. This package establishes the encrypted stream used by HTTP/1.1
and later HTTP/2.

## Outputs

- `src/Tessera.Net/Tls/*`
- `src/Tessera.Net/Resources/Roots/ccadb.pem`

## Acceptance

- TLS 1.3 handshake succeeds against `cloudflare.com` and `tls13.akamai.io`.
- SNI and ALPN (`h2`, then `http/1.1`) are advertised correctly.
- Certificate validation fails closed on a known-bad chain.
- No `SslStream`, `HttpClient`, P/Invoke, or native TLS dependency is used.

## Handoff log

- 2026-05-11T19:20Z — created after wp:M2-03-tcp completion; available to claim.
- 2026-05-11T19:24:07Z — claimed by agent-copilot-gpt-5.5, branch `wp-M2-04-tls`
- 2026-05-11T19:30Z — landed pure-managed BouncyCastle TLS 1.3 transport over
  `ITcpConnection`, embedded Mozilla CCADB root bundle, SNI + ALPN extensions,
  fail-closed certificate validation, and TLS tests. Validation included live
  TLS 1.3 handshakes to `cloudflare.com` and `tls13.akamai.io`.
