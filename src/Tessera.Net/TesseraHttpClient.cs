using Tessera.Common;
using Tessera.Net.Dns;
using Tessera.Net.Http;
using Tessera.Net.Http.H1;
using Tessera.Net.Tcp;
using Tessera.Net.Tls;
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Net;

/// <summary>
/// Top-level HTTP client. Resolves a URL to a TCP endpoint, opens a transport
/// (TLS for https, plain for http), writes one HTTP/1.1 request, parses the
/// response, and returns it. Connection pooling, HTTP/2, redirects, and
/// cookies are all explicitly out of scope at this milestone.
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
/// Each transport is opened fresh and closed when the call returns.
/// </remarks>
public sealed class TesseraHttpClient : IDisposable
{
    private readonly TesseraHttpClientOptions _options;
    private readonly DnsResolver _dns;
    private readonly TcpDialer _dialer;

    public TesseraHttpClient() : this(new TesseraHttpClientOptions()) { }

    public TesseraHttpClient(TesseraHttpClientOptions options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _dns = options.DnsResolver ?? new DnsResolver(new UdpDnsTransport());
        _dialer = new TcpDialer(_dns) { ConnectTimeout = options.ConnectTimeout };
    }

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

        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        requestCts.CancelAfter(_options.RequestTimeout);

        var dial = await _dialer.DialAsync(
            new TcpEndpoint(url.Host, port), requestCts.Token).ConfigureAwait(false);
        if (dial.IsErr)
        {
            return Result<HttpResponse, NetworkError>.Err(dial.Error switch
            {
                TcpError.DnsFailed => NetworkError.DnsFailure,
                TcpError.Timeout => NetworkError.ConnectTimeout,
                _ => NetworkError.ConnectFailed,
            });
        }

        var tcp = dial.Value;
        Stream transportStream;
        BcTlsTransport? tls = null;

        try
        {
            if (url.IsHttps)
            {
                var tlsResult = await BcTlsTransport.ConnectAsync(
                    tcp,
                    new TlsClientOptions(url.Host, _options.AlpnProtocols),
                    requestCts.Token).ConfigureAwait(false);
                if (tlsResult.IsErr)
                {
                    return Result<HttpResponse, NetworkError>.Err(tlsResult.Error switch
                    {
                        TlsError.CertificateRejected => NetworkError.TlsCertificateRejected,
                        _ => NetworkError.TlsHandshakeFailed,
                    });
                }
                tls = tlsResult.Value;
                transportStream = tls.Stream;
            }
            else
            {
                transportStream = new Tls.TcpConnectionStream(tcp);
            }

            await _options.RequestWriter.WriteAsync(request, transportStream, requestCts.Token)
                .ConfigureAwait(false);

            var parseResult = await _options.ResponseParser
                .ParseAsync(transportStream, requestCts.Token).ConfigureAwait(false);
            if (parseResult.IsErr)
                return Result<HttpResponse, NetworkError>.Err(MapParseError(parseResult.Error));

            return Result<HttpResponse, NetworkError>.Ok(parseResult.Value);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return Result<HttpResponse, NetworkError>.Err(NetworkError.RequestTimeout);
        }
        catch (IOException)
        {
            return Result<HttpResponse, NetworkError>.Err(NetworkError.TransportFailure);
        }
        finally
        {
            tls?.Dispose();
            // Disposing the TLS transport already disposes the wrapped TCP
            // stream (which in turn disposes the connection). For plain HTTP
            // we created the wrapper ourselves and must dispose it.
            if (tls is null)
                await tcp.DisposeAsync().ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        // Nothing pooled yet — pooling is M3+ work per 03_NETWORKING.md.
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
}

public sealed class TesseraHttpClientOptions
{
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);
    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(30);
    public IReadOnlyList<string> AlpnProtocols { get; init; } = ["http/1.1"];
    public DnsResolver? DnsResolver { get; init; }
    public H1RequestWriter RequestWriter { get; init; } = new();
    public H1ResponseParser ResponseParser { get; init; } = new();
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
