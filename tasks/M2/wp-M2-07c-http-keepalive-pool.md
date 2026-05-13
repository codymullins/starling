---
id: "wp:M2-07c-http-keepalive-pool"
parent: "wp:M2-07-network-end-to-end"
milestone: "M2"
status: "claimed"
claimed_by: "agent-claude-cody"
claimed_at: "2026-05-13T14:40:21Z"
branch: "main"
depends_on:
  - "wp:M2-05-http1"
blocks: []
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#http11"
  - "browser-plan/13_MILESTONES.md#m2--networking-and-live-html"
  - "browser-plan/14_AGENT_TASKS.md#wpm2-07-network-end-to-end"
---

# wp:M2-07c — HTTP/1.1 connection pool + keep-alive

## Goal

Reuse TCP+TLS connections across sequential requests to the same origin.
M2 exit criteria require "connection pool reuses across 2 sequential
requests"; the current `TesseraHttpClient.Dispose()` is a no-op with a
stale comment that defers pooling to M3+. Pooling is also a real
multiplier — a page with 10 subresources currently pays 10× TCP+TLS
handshake cost.

## Inputs

- `TesseraHttpClient` request/response path (wp:M2-05 ✓).
- Existing transport abstractions: `TcpTransport`, `BcTlsTransport`.

## Outputs

- `src/Tessera.Net/Http/ConnectionPool.cs` — per-origin pool keyed on
  `(scheme, host, port)`. Holds a bounded queue of idle, kept-alive
  `IHttpTransport` instances with their last-used timestamps. Methods:
  `TryAcquire`, `Release`, `DrainExpired(TimeSpan idleTimeout)`,
  `DisposeAll`.
- `src/Tessera.Net/TesseraHttpClient.cs` —
  - On send: ask the pool for an existing connection before dialing a new
    one.
  - On response complete: if both sides advertise keep-alive (HTTP/1.1
    default unless `Connection: close`) and the body was fully consumed,
    return the transport to the pool; otherwise close it.
  - Implement `Dispose` to call `pool.DisposeAll()` (remove the stale
    "pooling is M3+" comment).
- `src/Tessera.Net/Http/H1/H1ResponseParser.cs` — surface whether the
  response indicated keep-alive (presence of `Connection: close` or
  HTTP/1.0 without `Connection: keep-alive` → close).
- `tests/Tessera.Net.Tests/ConnectionPoolTests.cs` — unit tests:
  - Acquire/release round-trip returns the same transport instance.
  - `Connection: close` does NOT return the transport to the pool.
  - Idle expiry drains a connection past `idleTimeout`.
  - Pool capacity bound respected (oldest evicted first).
- `tests/Tessera.Engine.Tests/EngineHttpTests.cs` — extend the local stub
  server to count distinct TCP accepts; assert that two sequential
  requests to the same origin result in exactly one TCP accept when both
  responses are keep-alive.

## Acceptance

- Two sequential GETs to a local stub server use one TCP connection.
- A request that receives `Connection: close` cleanly drops the
  connection without surfacing an error to the caller.
- Existing tests pass without modification (back-compat).
- Live HTTPS test added in wp:M2-07b passes a second consecutive request
  to the same host in measurably less wall-clock time (informational,
  not asserted).

## Notes

- Bound the pool conservatively for v1 (e.g., 6 per origin, matching the
  classic HTTP/1.1 browser cap). Eviction policy is LRU.
- Make the idle timeout configurable (default 60s).
- Be careful with body draining: a connection can only be returned to the
  pool if the response body was fully consumed AND the parser is in a
  clean state. If body length is unknown/streaming and the caller bails
  early, close the connection.
- Do NOT pool TLS connections that experienced a handshake or read error.
- HTTP/2 multiplexing is M6 work — explicitly out of scope.

## Handoff log

- 2026-05-13T00:00Z — agent-claude-cody, filed during MVP-path planning
  split-out of the catch-all wp:M2-07-network-end-to-end. Available to
  claim.
- 2026-05-13T14:40:21Z — claimed by agent-claude-cody, working on main
