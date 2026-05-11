# 03 — Networking

## Scope

**In:** URL parsing, DNS, TCP, TLS 1.3 (pure managed), HTTP/1.1, HTTP/2 + HPACK, cookies, content decoding (gzip/brotli/deflate), HTTP cache, fetch primitives. Public seam for the engine.
**Out:** `fetch()` JS binding ([10_WEB_APIS.md](10_WEB_APIS.md)), service workers, WebSocket framing details (sketch only), HTTP/3 (deferred).

## Spec refs

- [SPEC: URL](https://url.spec.whatwg.org/) — WHATWG URL Living Standard
- [SPEC: DNS RFC 1035](https://www.rfc-editor.org/rfc/rfc1035)
- [SPEC: TLS 1.3 RFC 8446](https://www.rfc-editor.org/rfc/rfc8446)
- [SPEC: HTTP/1.1 RFC 9110/9112](https://www.rfc-editor.org/rfc/rfc9112) (semantics + syntax)
- [SPEC: HTTP/2 RFC 9113](https://www.rfc-editor.org/rfc/rfc9113)
- [SPEC: HPACK RFC 7541](https://www.rfc-editor.org/rfc/rfc7541)
- [SPEC: Cookies RFC 6265bis](https://www.ietf.org/archive/id/draft-ietf-httpbis-rfc6265bis-15.html)
- [SPEC: Fetch](https://fetch.spec.whatwg.org/)

## Rule 0 reminder

No P/Invoke. No `HttpClient`. No `SslStream`. (Yes, `SslStream` is "managed" in .NET, but it dispatches to OS TLS via Schannel/OpenSSL/Network.framework and therefore violates the user's requirement of a pure-managed implementation. **Do not use it.**)

What we *do* use:
- `System.Net.Sockets.Socket` (raw TCP / UDP, fully managed).
- `System.Buffers.ArrayPool<byte>` (zero-alloc receive paths).
- `System.IO.Pipelines` (back-pressured streaming).
- `System.IO.Compression` (DEFLATE, gzip — managed).
- `System.IO.Compression.BrotliStream` (managed since .NET 7).
- **BouncyCastle.Cryptography 2.5.0** for TLS 1.3 client (`Org.BouncyCastle.Tls.TlsClient`, fully managed C#).

## Project layout

```
src/Tessera.Net/
├── Tessera.Net.csproj
├── INetworkStack.cs                 # public seam
├── Url/                             # only struct/parsing helpers; full parser in Tessera.Url
│   └── UrlExtensions.cs
├── Dns/
│   ├── DnsResolver.cs
│   ├── DnsMessage.cs
│   ├── DnsCache.cs
│   └── HostsFile.cs
├── Tcp/
│   ├── TcpConnection.cs
│   └── ConnectionPool.cs
├── Tls/
│   ├── ITlsTransport.cs
│   ├── BcTlsClient.cs               # wraps BouncyCastle
│   ├── BcTlsAuthentication.cs       # cert validation
│   └── RootCertificates.cs          # bundled trust store
├── Http/
│   ├── HttpRequest.cs / HttpResponse.cs / ResponseChunk.cs
│   ├── H1/
│   │   ├── H1Connection.cs
│   │   ├── H1RequestWriter.cs
│   │   └── H1ResponseParser.cs
│   ├── H2/
│   │   ├── H2Connection.cs
│   │   ├── H2Stream.cs
│   │   ├── H2FrameReader.cs / H2FrameWriter.cs
│   │   ├── Hpack/
│   │   │   ├── HpackEncoder.cs
│   │   │   ├── HpackDecoder.cs
│   │   │   └── StaticTable.cs
│   │   └── FlowControl.cs
│   ├── Cookies/
│   │   ├── CookieJar.cs
│   │   ├── CookieParser.cs
│   │   └── PublicSuffixList.cs
│   ├── Decoding/
│   │   └── BodyDecoder.cs           # chunked + gzip/brotli/deflate
│   └── Cache/
│       └── HttpCache.cs             # RFC 9111 subset
└── HttpClient.cs                    # façade (NOT System.Net.Http.HttpClient — our own type)
```

## URL parsing

`Tessera.Url` implements the WHATWG URL parser ([SPEC: URL](https://url.spec.whatwg.org/)). Hand-roll. **Do not** use `System.Uri` — it predates the WHATWG spec and gets several edge cases wrong (percent-encoding, IDN, scheme parsing).

### Public API

```csharp
namespace Tessera.Url;

public readonly record struct Url
{
    public string Scheme    { get; init; }   // e.g. "https"
    public string Username  { get; init; }
    public string Password  { get; init; }
    public Host   Host      { get; init; }   // tagged: Domain | Ipv4 | Ipv6 | Opaque
    public ushort? Port     { get; init; }
    public Path   Path      { get; init; }   // list of segments (or single string for opaque)
    public string? Query    { get; init; }
    public string? Fragment { get; init; }

    public bool IsSpecial   { get; }         // http/https/ws/wss/ftp/file
    public Url Resolve(Url baseUrl);         // basic URL parser per spec
    public string Serialize(bool excludeFragment = false);
    public Origin Origin();
}

public abstract record Host;
public sealed record Domain(string Ascii)   : Host;  // already IDNA-encoded
public sealed record Ipv4(uint   Addr)      : Host;
public sealed record Ipv6(UInt128 Addr)     : Host;
public sealed record OpaqueHost(string Raw) : Host;
```

### Test fixture

Use the WPT URL test suite at `testdata/wpt/url/urltestdata.json`. ~700 test vectors. Must pass 100% as an acceptance gate. See [12_TESTING.md](12_TESTING.md#wpt-url-suite).

## DNS

Pure-managed recursive resolver on top of `Socket`. Per RFC 1035.

### Algorithm

1. Read `/etc/hosts` (Win: `%SystemRoot%\System32\drivers\etc\hosts`). Static map first.
2. Read system resolver config: `/etc/resolv.conf` on Unix, `GetAdaptersAddresses` equivalent on Windows via **managed-only fallback**: read the registry path `HKLM\System\CurrentControlSet\Services\Tcpip\Parameters\Interfaces\*` using `Microsoft.Win32.Registry` (pure managed — `System.Management` and `iphlpapi.dll` would be P/Invoke, **do not use**). If config unreadable, fall back to `1.1.1.1` and `8.8.8.8`.
3. Build a DNS query (`DnsMessage`), encode, send via UDP socket. 5s timeout.
4. If truncated bit set, retry over TCP (port 53).
5. Parse the answer section.
6. Cache for `min(record TTL, 300s)`.

### Wire format

Implement encoding/decoding manually. Names use the compression scheme (0xC0 pointer). A/AAAA/CNAME/NS/MX. Validate response ID matches query ID.

### Public API

```csharp
namespace Tessera.Net.Dns;

public interface IDnsResolver
{
    Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken ct);
}

public sealed class DnsResolver : IDnsResolver
{
    public DnsResolver(DnsOptions opts);
}

public record DnsOptions(IReadOnlyList<IPEndPoint> Servers, TimeSpan Timeout, bool UseDoH = false);
```

### DoH (later)

DNS-over-HTTPS uses our own HTTP stack circularly. Wire it after HTTP/2 is solid (M3+).

## TCP

Thin wrapper over `Socket`. Manages keep-alive pool keyed by `(scheme, host, port, alpn)`.

```csharp
public sealed class TcpConnection : IAsyncDisposable
{
    public static async Task<TcpConnection> ConnectAsync(EndPoint ep, CancellationToken ct);
    public Stream AsStream();          // for handing to TLS / H1 / H2
    public ValueTask DisposeAsync();
}

public sealed class ConnectionPool
{
    public ValueTask<TcpConnection> RentAsync(Url origin, CancellationToken ct);
    public void Return(Url origin, TcpConnection conn);
}
```

Pool: max 6 simultaneous H1 connections per origin (browser default), 1 H2 connection per origin (multiplexed). Idle timeout 60s.

## TLS 1.3 (pure managed)

The only managed-only path is **BouncyCastle.Cryptography 2.5.0**, namespace `Org.BouncyCastle.Tls`. It implements TLS 1.3 client-side in pure C# (verified by `bc-csharp` upstream). It runs on .NET 10 with `net6.0`/`netstandard2.0` TFMs.

**TLS 1.3 only.** By 2026, google.com, claude.ai, and every major CDN serve 1.3. We do not implement TLS 1.2 — it removes a downgrade-attack surface and halves the BouncyCastle code we have to validate. Sites that only offer ≤1.2 fail handshake with `NetworkException("TLS_VERSION_UNSUPPORTED")`. Revisit only if a target site breaks.

### Why not `SslStream`?

`SslStream` *appears* managed but delegates handshake/crypto to platform code (Schannel/OpenSSL/Network.framework). The user requirement is "from sockets up like Ladybird does"; that means **the bytes on the TLS record layer come out of our code**. BouncyCastle satisfies this.

### Wire-up sketch

```csharp
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

public sealed class BcTlsTransport : ITlsTransport, IDisposable
{
    private readonly TlsClientProtocol _proto;
    private readonly Stream _inner;

    public static async Task<BcTlsTransport> StartAsync(
        Stream tcp, string sni, IReadOnlyList<string> alpn, CancellationToken ct)
    {
        var crypto = new BcTlsCrypto(new SecureRandom());
        var client = new TesseraTlsClient(crypto, sni, alpn, RootCertificates.Default);
        var proto  = new TlsClientProtocol(tcp);
        proto.Connect(client);
        // BouncyCastle TlsClientProtocol blocks; wrap it in Task.Run for async use.
        return new BcTlsTransport(proto, tcp);
    }

    public Stream Stream => _proto.Stream;
    public string? NegotiatedAlpn => /* read from TlsClient callback */;
    public void Dispose() => _proto.Close();
}
```

`TesseraTlsClient` extends `DefaultTlsClient` and overrides:
- `GetSupportedVersions()` → TLS 1.3 only.
- `GetSupportedCipherSuites()` → TLS 1.3 AEAD suites only (`TLS_AES_128_GCM_SHA256`, `TLS_AES_256_GCM_SHA384`, `TLS_CHACHA20_POLY1305_SHA256`).
- `GetProtocolNames()` → ALPN: `h2`, then `http/1.1`.
- `NotifyServerCertificate(TlsServerCertificate)` → validate against `RootCertificates.Default` using bundled chain.
- `GetClientExtensions()` → SNI, ALPN, `signature_algorithms`, key share for `x25519` + `secp256r1`.

### Root store

Bundle Mozilla's CCADB at `testdata/roots/ccadb-roots.pem`. Build a `X509CertificateStructure[]` at static init. Refresh quarterly via a `tools/update-roots` script in repo. No OS dependency.

### Certificate validation rules

- Chain to a bundled root.
- `NotBefore ≤ now ≤ NotAfter`.
- SAN match for the requested host (RFC 6125). No fall-through to CN.
- AIA chasing optional in v1.
- Revocation: **OUT-OF-SCOPE-V1**. Plan: CRLite-style filter, fetched at startup.
- HSTS: maintain in-memory list from response headers; preload list from Chromium's `transport_security_state_static.json` is a v2 target.

### TLS 1.3 features required

- HKDF (BC implements).
- AEAD (AES-GCM, ChaCha20-Poly1305 — BC implements).
- 0-RTT: **OUT-OF-SCOPE-V1**.
- PSK / session resumption: v2.
- Encrypted ClientHello (ECH): v3.

## HTTP/1.1

### Request writer

```csharp
public sealed class H1RequestWriter
{
    public ValueTask WriteAsync(HttpRequest req, PipeWriter pipe, CancellationToken ct);
}
```

Format per RFC 9112 §3-§5. Mandatory headers: `Host`, `User-Agent`, `Accept`, `Accept-Encoding: gzip, br, deflate`, `Connection: keep-alive`, `Cookie` (if jar applies).

### Response parser

State machine, three states: `StatusLine` → `Headers` → `Body`.

Body kinds:
- `Content-Length: N` → fixed length.
- `Transfer-Encoding: chunked` → chunk framing per §7.1.
- HTTP/1.0 implicit close → read to EOF.

Streaming: emit `ResponseChunk` for the headers, then one `ResponseChunk` per chunk of decoded body.

### Connection reuse

`Connection: keep-alive` (default in 1.1). Pool returns connection on success. Discard on:
- Server sends `Connection: close`.
- Read/write error.
- Idle > 60s.

### Pipelining

OUT-OF-SCOPE-V1. Skip.

## HTTP/2

ALPN-negotiated. If ALPN says `h2`, we use this path; else fall back to H1.

### Frame layout

Implement readers/writers for the 10 frame types. Use `Span<byte>` everywhere. No allocations on the hot path.

```csharp
public enum H2FrameType : byte
{
    Data = 0x0, Headers = 0x1, Priority = 0x2, RstStream = 0x3,
    Settings = 0x4, PushPromise = 0x5, Ping = 0x6, GoAway = 0x7,
    WindowUpdate = 0x8, Continuation = 0x9,
}
```

### Connection lifecycle

1. Send connection preface `PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n` (24 bytes).
2. Send SETTINGS frame with `INITIAL_WINDOW_SIZE`, `MAX_HEADER_LIST_SIZE`, `ENABLE_PUSH=0`.
3. Read server SETTINGS, ACK.
4. Open streams.

### HPACK

Implement [RFC 7541](https://www.rfc-editor.org/rfc/rfc7541) by the book. Dynamic table sized per `SETTINGS_HEADER_TABLE_SIZE` (default 4096). Huffman code per Appendix B. Reference: there's a clean reference impl in Go's `golang.org/x/net/http2/hpack` — read for structure, don't copy.

### Flow control

Per-stream and per-connection windows. Update on send/receive of DATA frames. Send `WINDOW_UPDATE` when receive window dips below 50%.

### Stream multiplexing

`H2Stream` exposes the same `IAsyncEnumerable<ResponseChunk>` shape as H1. The `H2Connection` is shared. The pool keys by `(scheme, host, port, "h2")`.

### Server push

Refuse via `SETTINGS_ENABLE_PUSH=0`. (It's deprecated anyway.)

### Trailers

Read but ignore in v1 (no API surfaces them to the engine).

## HTTP/3

OUT-OF-SCOPE-V1. Requires QUIC, which requires UDP + custom congestion control + TLS 1.3 reused but reframed. Plan for M9+. Note: there is no pure-managed QUIC implementation in NuGet today. We will either fork `quiche` semantics or build from scratch. Decide when we get there.

## Compression

```csharp
public sealed class BodyDecoder
{
    public BodyDecoder(IReadOnlyList<string> contentEncoding);
    public ValueTask<int> DecodeAsync(ReadOnlyMemory<byte> input, Memory<byte> output, bool isLast);
}
```

Implementations:
- `gzip` → `System.IO.Compression.GZipStream` (managed).
- `deflate` → `DeflateStream`.
- `br` → `BrotliStream` (managed since .NET 7).
- Identity → passthrough.

Stack decoders if multiple encodings (rare; spec discourages).

## Cookies (RFC 6265bis)

```csharp
public sealed class CookieJar
{
    public void StoreFromHeaders(Url url, IReadOnlyList<string> setCookieHeaders);
    public string BuildCookieHeader(Url url);
    public void Clear();
}
```

Storage shape: `Dictionary<string /*domain*/, List<Cookie>>`. Insertion sort on `(path desc, creation asc)` for `BuildCookieHeader`.

Rules to enforce:
- `Path` attribute defaults to "default-path" algorithm.
- `Domain` attribute: leading `.` ignored. Subdomain matching.
- `Secure` cookies only on https.
- `HttpOnly` cookies hidden from JS bindings.
- `SameSite=Strict|Lax|None`. Default `Lax`. Reject `None` without `Secure`.
- `__Host-` and `__Secure-` prefix rules.

### Public Suffix List

Bundle [PSL](https://publicsuffix.org/list/public_suffix_list.dat) at `testdata/psl/effective_tld_names.dat`. Used to reject cookies for eTLDs. Refresh quarterly.

## HTTP cache (RFC 9111 subset)

```csharp
public interface IHttpCache
{
    bool TryGetFresh(Url url, HttpRequest req, out HttpResponse cached);
    void Store(HttpRequest req, HttpResponse resp);
    void Revalidate(HttpRequest req, HttpResponse new304);
}
```

v1 features: `Cache-Control: max-age`, `Expires`, `Last-Modified` + `If-Modified-Since`, `ETag` + `If-None-Match`. Storage: in-memory LRU, 64MB cap. Disk cache in M6+.

## Top-level façade

```csharp
public sealed class TesseraHttpClient
{
    public TesseraHttpClient(NetworkOptions opts);
    public IAsyncEnumerable<ResponseChunk> SendAsync(HttpRequest req, CancellationToken ct);
}

public sealed record NetworkOptions(
    IDnsResolver Dns,
    CookieJar Cookies,
    IHttpCache Cache,
    string UserAgent,
    TimeSpan ConnectTimeout,
    TimeSpan RequestTimeout);
```

Used by [10_WEB_APIS.md#fetch](10_WEB_APIS.md#fetch).

## Concurrency notes

- Reads off the socket happen on a `ThreadPool` worker (Socket async APIs use IOCP/epoll/kqueue, all managed).
- `H2Connection` has one reader loop; demuxes frames into `Channel<H2Frame>` per stream.
- Writes are serialized via a `SemaphoreSlim(1)`.

## Failure modes (table)

| Failure | Behavior |
|---|---|
| DNS resolution failure | Throw `NetworkException("DNS")`; engine shows `ERR_NAME_NOT_RESOLVED` page. |
| TCP connect timeout | `NetworkException("CONNECT_TIMEOUT")`. |
| TLS handshake failure | `NetworkException("TLS_HANDSHAKE")` with inner BC exception. |
| Cert chain invalid | Surface to user via shell prompt (later); fail-closed in v1. |
| HTTP 5xx | Pass through to engine; no automatic retry. |
| HTTP 3xx | Follow redirects up to 20 hops; abort with `ERR_TOO_MANY_REDIRECTS`. |
| Stream RST_STREAM | Treat as connection error in v1; pool drops the H2 connection. |
| Brotli decode failure | `NetworkException("DECODE")`. |

## Acceptance Tests

- [ ] WPT `url/urltestdata.json` passes 100%.
- [ ] DNS resolver returns A records for `example.com`, `www.example.com`, `localhost`.
- [ ] TLS 1.3 handshake completes against `tls13.akamai.io`, `cloudflare.com`, and a local test server using a self-signed cert added to a custom root store.
- [ ] HTTP/1.1 GET to `https://example.com` returns 200 and the body parses as HTML.
- [ ] HTTP/2 GET to `https://www.google.com/` upgrades via ALPN and returns 200 in one stream.
- [ ] Cookies set via `Set-Cookie` are echoed in subsequent requests, honoring `Secure`/`HttpOnly`/`SameSite`/`Path`/`Domain`.
- [ ] Gzip and Brotli-encoded bodies decode byte-identical to non-encoded servers.
- [ ] Connection pool reuses a TCP connection across two sequential HTTPS requests to the same origin.
- [ ] All of the above pass on Windows, macOS, Linux in CI.
- [ ] `grep -rn 'SslStream\|System.Net.Http' src/Tessera.Net/` is empty.
