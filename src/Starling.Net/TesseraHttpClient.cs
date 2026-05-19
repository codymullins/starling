using System.Diagnostics;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Net.Dns;
using Tessera.Net.Http;
using Tessera.Net.Http.Cookies;
using Tessera.Net.Http.H1;
using Tessera.Net.Tcp;
using Tessera.Net.Tls;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Net;

/// <summary>
/// Top-level HTTP client. Resolves a URL to a TCP endpoint, opens a transport
/// (TLS for https, plain for http), writes one HTTP/1.1 request, parses the
/// response, and returns it. Sequential requests to the same origin reuse a
/// pooled TCP+TLS transport when both sides advertise keep-alive
/// (wp:M2-07c — HTTP/1.1 connection pool).
/// </summary>
/// <remarks>
/// For a single GET against <c>https://example.com</c> the data flow is:
/// <list type="bullet">
///   <item><see cref="DnsResolver"/> → A/AAAA records</item>
///   <item><see cref="TcpDialer"/> → <see cref="ITcpConnection"/></item>
///   <item><see cref="BcTlsTransport"/> → ALPN-negotiated TLS stream</item>
///   <item><see cref="H1RequestWriter"/> → wire bytes onto the stream</item>
///   <item><see cref="H1ResponseParser"/> → fully buffered <see cref="HttpResponse"/></item>
/// </list>
/// A <see cref="ConnectionPool"/> sits in front of the dialer: every send
/// asks the pool first, and clean responses with a definite body length and
/// keep-alive headers return the transport to the pool. HTTP/2 multiplexing
/// is M6 work; pooling here only covers HTTP/1.1.
/// </remarks>
public sealed class TesseraHttpClient : IDisposable
{
    private readonly TesseraHttpClientOptions _options;
    private readonly DnsResolver _dns;
    private readonly TcpDialer _dialer;
    private readonly ConnectionPool _pool;
    private readonly IDiagnostics _diag;
    private bool _disposed;

    public TesseraHttpClient() : this(new TesseraHttpClientOptions()) { }

    public TesseraHttpClient(TesseraHttpClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _diag = options.Diagnostics ?? NoopDiagnostics.Instance;
        _dns = options.DnsResolver ?? new DnsResolver(new UdpDnsTransport());
        _dialer = new TcpDialer(_dns) { ConnectTimeout = options.ConnectTimeout };
        _pool = options.ConnectionPool ?? new ConnectionPool();
    }

    /// <summary>Idle connection pool. Exposed mainly for tests asserting on
    /// reuse / capacity behaviour.</summary>
    public ConnectionPool ConnectionPool => _pool;

    public Task<Result<HttpResponse, NetworkError>> GetAsync(string url, CancellationToken ct = default)
    {
        var parsed = global::Tessera.Url.UrlParser.Parse(url);
        if (parsed.IsErr)
            return Task.FromResult(Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl));
        return SendAsync(HttpRequest.Get(parsed.Value), ct);
    }

    public Task<Result<HttpResponse, NetworkError>> GetAsync(TesseraUrl url, CancellationToken ct = default)
        => SendAsync(HttpRequest.Get(url), ct);

    public async Task<Result<HttpResponse, NetworkError>> SendAsync(
        HttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var url = request.Url;

        if (!url.IsHttp && !url.IsHttps)
            return Result<HttpResponse, NetworkError>.Err(NetworkError.UnsupportedScheme);

        if (string.IsNullOrEmpty(url.Host))
            return Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl);

        var port = url.Port ?? url.DefaultPort
            ?? (url.IsHttps ? 443 : url.IsHttp ? 80 : 0);
        if (port is < 1 or > 65535)
            return Result<HttpResponse, NetworkError>.Err(NetworkError.BadUrl);

        var origin = OriginKey.Create(url.IsHttps ? "https" : "http", url.Host, port);

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestCts.CancelAfter(_options.RequestTimeout);

        using var httpSpan = _diag.Span("net", "http");
        Activity.Current?.SetTag("http.method", request.Method);
        Activity.Current?.SetTag("http.url", url.ToString());
        Activity.Current?.SetTag("server.address", origin.Host);
        Activity.Current?.SetTag("server.port", origin.Port);
        _diag.Counter("net.http.requests", 1);

        try
        {
            // 1. Try the pool first. A pooled transport may turn out to have
            //    been closed by the peer between the prior response and this
            //    request; if our send raises an IO error — or reads back zero
            //    bytes when we were expecting a status line — we fall through
            //    to a fresh dial.
            var pooled = _pool.TryAcquire(origin);
            if (pooled is not null)
            {
                _diag.Counter("net.http.connection_reused", 1);
                Activity.Current?.SetTag("connection.reused", true);
                var pooledOutcome = await TrySendOnTransportAsync(
                    pooled, request, url, fromPool: true, requestCts.Token).ConfigureAwait(false);
                if (pooledOutcome.UsedTransport)
                {
                    RecordResponseTags(pooledOutcome.Result);
                    return pooledOutcome.Result;
                }
                // Otherwise the connection was unusable (closed/IO) — dispose
                // and retry with a fresh dial. Re-tag the span: this request
                // ended up paying for a fresh handshake despite the pool hit.
                Activity.Current?.SetTag("connection.reused", false);
                await SafeDisposeAsync(pooled).ConfigureAwait(false);
            }
            else
            {
                Activity.Current?.SetTag("connection.reused", false);
            }

            // 2. Dial + (optionally) TLS-handshake a new transport.
            var dialed = await DialAsync(url, origin, requestCts.Token).ConfigureAwait(false);
            if (dialed.IsErr)
            {
                RecordError(dialed.Error);
                return Result<HttpResponse, NetworkError>.Err(dialed.Error);
            }

            _diag.Counter("net.http.connection_opened", 1);

            var fresh = dialed.Value;
            var freshOutcome = await TrySendOnTransportAsync(
                fresh, request, url, fromPool: false, requestCts.Token).ConfigureAwait(false);
            if (!freshOutcome.UsedTransport)
            {
                // A brand-new transport that refused to talk: surface a
                // transport error and discard. We don't loop again to avoid
                // hammering.
                await SafeDisposeAsync(fresh).ConfigureAwait(false);
                RecordError(NetworkError.TransportFailure);
                return Result<HttpResponse, NetworkError>.Err(NetworkError.TransportFailure);
            }
            RecordResponseTags(freshOutcome.Result);
            return freshOutcome.Result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            RecordError(NetworkError.RequestTimeout);
            return Result<HttpResponse, NetworkError>.Err(NetworkError.RequestTimeout);
        }
    }

    private void RecordResponseTags(Result<HttpResponse, NetworkError> result)
    {
        if (result.IsErr)
        {
            RecordError(result.Error);
            return;
        }
        var response = result.Value;
        Activity.Current?.SetTag("http.status_code", response.StatusCode);
        Activity.Current?.SetTag("http.response.body.size", response.Body.Length);
        _diag.Counter("net.http.bytes_in", response.Body.Length);
        if (response.StatusCode >= 500)
        {
            Activity.Current?.SetStatus(ActivityStatusCode.Error, $"HTTP {response.StatusCode}");
        }
    }

    private void RecordError(NetworkError error)
    {
        var message = error.ToString();
        _diag.Counter("net.http.failures", 1);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, message);
        _diag.Log(DiagLevel.Warn, "net", message);
    }

    private async Task<Result<IHttpTransport, NetworkError>> DialAsync(
        TesseraUrl url, OriginKey origin, CancellationToken ct)
    {
        // DNS resolution span. The TcpDialer drives DNS internally; we mirror
        // the work with our own resolve call so the span/counter scope is
        // crisp and DNS-only failures are distinguishable from connect-only
        // failures. The result feeds DialDirectAsync below.
        Result<DnsResult, DnsError> dnsResult;
        using (var dnsSpan = _diag.Span("net", "dns"))
        {
            Activity.Current?.SetTag("dns.host", origin.Host);
            _diag.Counter("net.dns.resolutions", 1);
            dnsResult = await _dns.ResolveAsync(origin.Host, ct).ConfigureAwait(false);
            if (dnsResult.IsErr)
            {
                _diag.Counter("net.dns.failures", 1);
                Activity.Current?.SetStatus(ActivityStatusCode.Error, dnsResult.Error.ToString());
                return Result<IHttpTransport, NetworkError>.Err(NetworkError.DnsFailure);
            }
        }

        // TCP connect span. Try each resolved address; first success wins.
        ITcpConnection? tcp = null;
        TcpError? lastTcpError = null;
        using (var tcpSpan = _diag.Span("net", "tcp_connect"))
        {
            Activity.Current?.SetTag("server.address", origin.Host);
            Activity.Current?.SetTag("server.port", origin.Port);
            _diag.Counter("net.tcp.connects", 1);

            foreach (var ip in dnsResult.Value.Addresses)
            {
                var endpoint = new System.Net.IPEndPoint(ip, origin.Port);
                var dial = await _dialer.DialDirectAsync(
                    endpoint,
                    new TcpEndpoint(origin.Host, origin.Port),
                    ct).ConfigureAwait(false);
                if (dial.IsOk)
                {
                    tcp = dial.Value;
                    break;
                }
                lastTcpError = dial.Error;
            }

            if (tcp is null)
            {
                _diag.Counter("net.tcp.failures", 1);
                var err = lastTcpError == TcpError.Timeout
                    ? NetworkError.ConnectTimeout
                    : NetworkError.ConnectFailed;
                Activity.Current?.SetStatus(ActivityStatusCode.Error, err.ToString());
                return Result<IHttpTransport, NetworkError>.Err(err);
            }
        }

        if (url.IsHttps)
        {
            using var tlsSpan = _diag.Span("net", "tls_handshake");
            Activity.Current?.SetTag("server.address", origin.Host);
            _diag.Counter("net.tls.handshakes", 1);

            var tlsResult = await BcTlsTransport.ConnectAsync(
                tcp,
                new TlsClientOptions(origin.Host, _options.AlpnProtocols),
                ct).ConfigureAwait(false);
            if (tlsResult.IsErr)
            {
                _diag.Counter("net.tls.failures", 1);
                await tcp.DisposeAsync().ConfigureAwait(false);
                var err = tlsResult.Error == TlsError.CertificateRejected
                    ? NetworkError.TlsCertificateRejected
                    : NetworkError.TlsHandshakeFailed;
                Activity.Current?.SetStatus(ActivityStatusCode.Error, err.ToString());
                return Result<IHttpTransport, NetworkError>.Err(err);
            }
            // TesseraTlsClient pins TLS 1.3; tag it post-handshake.
            Activity.Current?.SetTag("tls.protocol", "TLSv1.3");
            if (tlsResult.Value.NegotiatedApplicationProtocol is { } alpn)
            {
                Activity.Current?.SetTag("tls.alpn", alpn);
                // We only speak HTTP/1.1 today; if a peer negotiates h2 despite
                // our ALPN list, fail fast rather than feed h2 frames into the
                // H1 parser. See browser-plan/03_NETWORKING.md (HTTP/2 = M6).
                if (alpn != "http/1.1" && alpn.Length > 0)
                {
                    _diag.Log(DiagLevel.Error, "net",
                        $"unsupported ALPN '{alpn}' negotiated for {origin.Host} (client speaks http/1.1 only)");
                    _diag.Counter("net.tls.failures", 1);
                    await tcp.DisposeAsync().ConfigureAwait(false);
                    return Result<IHttpTransport, NetworkError>.Err(NetworkError.TlsHandshakeFailed);
                }
            }

            return Result<IHttpTransport, NetworkError>.Ok(
                PooledHttpTransport.ForTls(origin, tcp, tlsResult.Value));
        }

        return Result<IHttpTransport, NetworkError>.Ok(
            PooledHttpTransport.ForPlainHttp(origin, tcp));
    }

    /// <summary>
    /// Write the request, parse the response, and decide whether to return
    /// the transport to the pool or close it. The returned
    /// <see cref="TransportSendOutcome.UsedTransport"/> flag is false when the
    /// caller should retry on a different transport (e.g. the pooled socket
    /// was closed between requests); the transport is still owned by the
    /// caller in that case and must be disposed.
    /// </summary>
    private async Task<TransportSendOutcome> TrySendOnTransportAsync(
        IHttpTransport transport,
        HttpRequest request,
        TesseraUrl url,
        bool fromPool,
        CancellationToken ct)
    {
        try
        {
            // Inject the cookie jar's view of the world into the request, but
            // only when the caller hasn't already supplied a Cookie header.
            if (_options.CookieJar is { } jar && !request.Headers.Contains("Cookie"))
            {
                var cookieHeader = jar.BuildCookieHeader(url);
                if (cookieHeader.Length > 0)
                    request.Headers.Set("Cookie", cookieHeader);
            }

            using (var writeSpan = _diag.Span("net", "h1_request"))
            {
                _diag.Counter("net.h1.requests_written", 1);
                await _options.RequestWriter
                    .WriteAsync(request, transport.Stream, ct)
                    .ConfigureAwait(false);
                if (!request.Body.IsEmpty)
                    _diag.Counter("net.http.bytes_out", request.Body.Length);
            }

            Result<HttpResponse, HttpError> parseResult;
            using (var parseSpan = _diag.Span("net", "h1_response"))
            {
                _diag.Counter("net.h1.responses_parsed", 1);
                parseResult = await _options.ResponseParser
                    .ParseAsync(transport.Stream, ct).ConfigureAwait(false);
                if (parseResult.IsOk)
                    Activity.Current?.SetTag("http.status_code", parseResult.Value.StatusCode);
            }

            if (parseResult.IsErr)
            {
                // A pooled connection that the peer closed since the prior
                // response will write OK (TCP buffers locally) but read 0
                // bytes — the parser surfaces that as UnexpectedEof. Retry
                // on a fresh dial in that case; for everything else (or on
                // a fresh transport) surface the protocol failure.
                if (fromPool && parseResult.Error == HttpError.UnexpectedEof)
                    return TransportSendOutcome.Unused();

                await SafeDisposeAsync(transport).ConfigureAwait(false);
                return TransportSendOutcome.Used(
                    Result<HttpResponse, NetworkError>.Err(MapParseError(parseResult.Error)));
            }

            var response = parseResult.Value;

            // Persist any Set-Cookie headers from the response.
            if (_options.CookieJar is { } jar2)
            {
                var setCookies = response.Headers.GetAll("Set-Cookie");
                if (setCookies.Count > 0)
                    jar2.StoreFromHeaders(url, setCookies);
            }

            // Decide pool fate. We can only safely reuse the transport if:
            //   - both sides agreed to keep-alive (RFC 9112 §9.3), AND
            //   - the body had a definite framing so we know we drained
            //     exactly to the end (no over-read into the next response).
            if (H1ResponseParser.IndicatesKeepAlive(response)
                && H1ResponseParser.HasDefiniteBodyFraming(response)
                && transport.IsOpen
                && !_disposed)
            {
                await _pool.ReleaseAsync(transport).ConfigureAwait(false);
            }
            else
            {
                await SafeDisposeAsync(transport).ConfigureAwait(false);
            }

            return TransportSendOutcome.Used(Result<HttpResponse, NetworkError>.Ok(response));
        }
        catch (OperationCanceledException)
        {
            await SafeDisposeAsync(transport).ConfigureAwait(false);
            // Cancellation includes our internal request-timeout CTS; let the
            // caller distinguish via its own token.
            throw;
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException)
        {
            // If this was a pooled connection on its first byte, treat the
            // failure as "socket died while idle" and let the caller retry.
            // We can't reliably tell whether anything was actually written;
            // for idempotent GETs (the only verb the engine uses today) a
            // re-dial is safe. For non-GETs the caller surfaces this as a
            // transport failure when the retry has nothing to fall back to.
            //
            // Sockets propagate ECONNRESET as raw SocketException (not wrapped
            // in IOException), so we accept both.
            return TransportSendOutcome.Unused();
        }
    }

    private static async ValueTask SafeDisposeAsync(IHttpTransport transport)
    {
        try { await transport.DisposeAsync().ConfigureAwait(false); }
        catch { /* a half-broken socket may throw on shutdown; we don't care */ }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Synchronously dispose the pool. The pool's DisposeAsync only awaits
        // socket teardowns which are non-blocking; .GetAwaiter().GetResult()
        // is fine on the disposal path.
        _pool.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private static NetworkError MapParseError(HttpError error) => error switch
    {
        HttpError.UnexpectedEof => NetworkError.TransportFailure,
        HttpError.TransportFailure => NetworkError.TransportFailure,
        HttpError.HeadersTooLarge => NetworkError.ProtocolError,
        HttpError.BodyTooLarge => NetworkError.ProtocolError,
        HttpError.BadStatusLine => NetworkError.ProtocolError,
        HttpError.BadHeader => NetworkError.ProtocolError,
        HttpError.BadChunkedFraming => NetworkError.ProtocolError,
        HttpError.UnsupportedEncoding => NetworkError.ProtocolError,
        HttpError.DecodeFailed => NetworkError.ProtocolError,
        _ => NetworkError.ProtocolError,
    };

    private readonly record struct TransportSendOutcome(bool UsedTransport, Result<HttpResponse, NetworkError> Result)
    {
        public static TransportSendOutcome Used(Result<HttpResponse, NetworkError> r) => new(true, r);
        public static TransportSendOutcome Unused() => new(false, default);
    }
}

public sealed class TesseraHttpClientOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<string> AlpnProtocols { get; init; } = ["http/1.1"];
    public DnsResolver? DnsResolver { get; init; }
    public CookieJar? CookieJar { get; init; }
    public H1RequestWriter RequestWriter { get; init; } = new();
    public H1ResponseParser ResponseParser { get; init; } = new();

    /// <summary>
    /// Optional injected connection pool. When null, the client owns a
    /// freshly-constructed pool with default sizing (6 idle per origin,
    /// 60s idle timeout).
    /// </summary>
    public ConnectionPool? ConnectionPool { get; init; }

    /// <summary>
    /// Optional diagnostics sink. When set, the client emits per-request
    /// spans (http / dns / tcp_connect / tls_handshake / h1_request /
    /// h1_response) and counters (net.http.*, net.dns.*, net.tcp.*,
    /// net.tls.*, net.h1.*) — feeding the Aspire dashboard. Defaults to
    /// <see cref="NoopDiagnostics.Instance"/>.
    /// </summary>
    public IDiagnostics? Diagnostics { get; init; }
}

public enum NetworkError
{
    BadUrl,
    UnsupportedScheme,
    DnsFailure,
    ConnectFailed,
    ConnectTimeout,
    RequestTimeout,
    TlsHandshakeFailed,
    TlsCertificateRejected,
    TransportFailure,
    ProtocolError,
}
