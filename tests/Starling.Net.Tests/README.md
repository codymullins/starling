# Starling.Net.Tests

Tests for `src/Starling.Net` — the stack from sockets up to HTTP.

## What it covers

DNS, TCP, TLS 1.3 (using BouncyCastle), HTTP/1.1 with keep-alive, gzip and
brotli decoding, redirects, cookies, and the Public Suffix List. The
HTTP/2 client is in but not yet wired into every path.

URL parsing tests live in
[`Starling.Url.Tests`](../Starling.Url.Tests). That suite runs the Web
Platform Tests `url/` set (about 700 cases) for the "URL 100%" claim.

## How to run

```bash
dotnet test tests/Starling.Net.Tests
dotnet test tests/Starling.Url.Tests
```

## What the badge means

HTTP/1.1 and TLS 1.3 are in production use and render real sites end to
end. URL parsing meets the 100% target at milestone 5. HTTP/2 is in but
not everywhere. Disk cache and other hardening are pushed to milestone 9
and later.

Design: [`03_NETWORKING.md`](../../browser-plan/03_NETWORKING.md).
