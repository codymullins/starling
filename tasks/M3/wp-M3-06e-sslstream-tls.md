---
id: "wp:M3-06e-sslstream-tls"
parent: "wp:M3-06-native-interop-pivot"
milestone: "M3"
status: "claimed"
claimed_by: "agent-claude-cody-tls"
claimed_at: "2026-05-14T14:42:54Z"
branch: "main"
depends_on: []
blocks:
  - "wp:M3-06l-ci-policy"
subsystem: "Tessera.Net"
plan_refs:
  - "browser-plan/03_NETWORKING.md#tls-approach"
  - "browser-plan/12_TESTING.md#interop-seam-policy-test"
  - "browser-plan/13_MILESTONES.md#m3"
---

# wp:M3-06e-sslstream-tls — replace BouncyCastle TLS with `SslStream`

## Goal

Phase 9: swap the pure-managed BouncyCastle TLS engine for `SslStream` (OS TLS)
behind the **unchanged** `ITlsTransport` seam. `SslStream` is pure-managed BCL,
so `Tessera.Net` keeps its P/Invoke-free bill of health — no interop seam needed
here. Keep the bundled CCADB root store for cross-platform determinism, but
rebuild certificate verification on `System.Security.Cryptography.X509Certificates`.
Delete BouncyCastle entirely. Fully isolated to `Tessera.Net`; independently
mergeable to `main`.

## Inputs

- No code dependencies — fully isolated to `Tessera.Net`.
- Existing seam: `ITlsTransport.cs`, `TlsClientOptions.cs`, `TlsError.cs`,
  `TcpConnectionStream.cs`, embedded `ccadb.pem`.

## Outputs

- `src/Tessera.Net/Tls/SslStreamTlsTransport.cs` — implements `ITlsTransport`
  with `SslClientAuthenticationOptions`: ALPN (`Http2`, `Http11`), SNI
  (`TargetHost`), `EnabledSslProtocols = Tls13`.
- `src/Tessera.Net/Tls/CertificateVerifier.cs` — rewritten against
  `X509ChainPolicy.CustomTrustStore` + `TrustMode = CustomRootTrust` for chain
  building / expiry.
- `src/Tessera.Net/Tls/RootCertificates.cs` — rewritten to load the bundled
  CCADB roots as `X509Certificate2` for the custom trust store.
- Kept custom code: `CertificateHostNameMatcher` (SAN/wildcard matching only).
- **Deleted:** `src/Tessera.Net/Tls/BcTlsTransport.cs`,
  `TesseraTlsClient.cs`, `TesseraTlsAuthentication.cs`.
- **Kept:** `ITlsTransport.cs`, `TlsClientOptions.cs`, `TlsError.cs`,
  `TcpConnectionStream.cs`, embedded `ccadb.pem`.
- `src/Tessera.Net/TesseraHttpClient.cs` (~line 150) and
  `src/Tessera.Net/Http/PooledHttpTransport.cs` (type `_tls` as
  `ITlsTransport?`) — caller updates.
- `Directory.Packages.props` + `src/Tessera.Net/Tessera.Net.csproj` — remove
  `BouncyCastle.Cryptography`; regenerate affected `packages.lock.json`.

## Acceptance

- `SslStreamTlsTransport` implements the unchanged `ITlsTransport` seam; the
  `network-tests` job does a live TLS 1.3 + `h2` ALPN handshake to
  `example.com` / `nginx.org`.
- A bad certificate chain fails closed (custom trust store rejects it).
- `grep -rn BouncyCastle src/` is empty; `BouncyCastle.Cryptography` is gone
  from `Directory.Packages.props` and `Tessera.Net.csproj`; `packages.lock.json`
  regenerated.
- `BcTlsTransport.cs`, `TesseraTlsClient.cs`, `TesseraTlsAuthentication.cs` are
  deleted; the kept files remain.
- `Tessera.Net` stays P/Invoke-free (`SslStream` is BCL) — the interop-policy
  lint job still passes for this project with no allowlist entry.
- Full repo `dotnet test` green.

## Notes

- Master plan: `~/.claude/plans/make-a-plan-to-serialized-boole.md` (Phase 9).
- The `NoSslStream_InNetProject` test is **deleted** as part of `06l-ci-policy`
  — until then it will fail; coordinate the merge order via handoff log, or land
  the test deletion drive-by here if `06l` is not yet in flight.
- `Directory.Packages.props` is a merge-conflict hotspot — note the touch.

## Handoff log

- 2026-05-14T00:00:00Z — created (agent-claude-cody) during the native-interop pivot WP filing.
