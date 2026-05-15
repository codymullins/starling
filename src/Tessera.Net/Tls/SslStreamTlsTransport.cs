using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Tessera.Common;
using Tessera.Common.Diagnostics;
using Tessera.Net.Tcp;

namespace Tessera.Net.Tls;

/// <summary>
/// TLS transport built on the BCL <see cref="SslStream"/> (OS TLS stack).
/// Pure-managed at the project level — <see cref="SslStream"/> is part of the
/// .NET BCL — so <c>Tessera.Net</c> keeps its P/Invoke-free bill of health.
/// </summary>
/// <remarks>
/// Certificate validation does not consult the OS trust store: the
/// <see cref="RemoteCertificateValidationCallback"/> routes the presented
/// chain through <see cref="CertificateVerifier"/>, which builds against the
/// bundled CCADB roots for cross-platform determinism.
/// </remarks>
public sealed class SslStreamTlsTransport : ITlsTransport
{
    // The macOS / Mac Catalyst native TLS + X509 stack (Secure Transport +
    // AppleCrypto SecTrust) is not safe under concurrent handshakes. A page
    // with several HTTPS origins fans out parallel TLS handshakes, and the
    // overlapping native certificate-chain work — SslStream's own internal
    // chain building plus our validation callback's Export / LoadCertificate /
    // X509Chain.Build — corrupts the managed heap, producing a hard SIGSEGV
    // (reproducible by navigating to https://google.com; see the WP M3-06 TLS
    // handoff). Serializing only our CertificateVerifier.Verify was not enough
    // because the racing X509 work lives outside it. This process-wide gate
    // serializes the whole handshake instead. Handshakes are CPU-cheap relative
    // to the page fetch they gate, so the lost parallelism is not the
    // page-load bottleneck.
    private static readonly SemaphoreSlim HandshakeGate = new(1, 1);

    private readonly SslStream _sslStream;
    private readonly TcpConnectionStream _tcpStream;
    private bool _disposed;

    private SslStreamTlsTransport(
        SslStream sslStream,
        TcpConnectionStream tcpStream,
        string? negotiatedApplicationProtocol)
    {
        _sslStream = sslStream;
        _tcpStream = tcpStream;
        NegotiatedApplicationProtocol = negotiatedApplicationProtocol;
    }

    public Stream Stream => _sslStream;
    public string? NegotiatedApplicationProtocol { get; }

    public static async Task<Result<SslStreamTlsTransport, TlsError>> ConnectAsync(
        ITcpConnection tcpConnection,
        TlsClientOptions options,
        CancellationToken ct = default)
    {
        if (tcpConnection is null) throw new ArgumentNullException(nameof(tcpConnection));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServerName) || options.ApplicationProtocols.Count == 0)
            return Result<SslStreamTlsTransport, TlsError>.Err(TlsError.InvalidOptions);

        NativeCallTrace.Mark("tls.connect.begin", options.ServerName);
        var tcpStream = new TcpConnectionStream(tcpConnection);

        // Tracks whether a handshake failure was caused by our custom
        // certificate verification rejecting the chain, so the caller can
        // surface CertificateRejected vs. a generic HandshakeFailed.
        var certificateRejected = false;

        var sslStream = new SslStream(
            tcpStream,
            leaveInnerStreamOpen: false,
            userCertificateValidationCallback: (_, certificate, chain, _) =>
            {
                NativeCallTrace.Mark("tls.callback.enter", certificate is null ? "null-cert" : "");
                if (certificate is null)
                {
                    certificateRejected = true;
                    return false;
                }

                NativeCallTrace.Mark("tls.callback.export.begin");
                var der = certificate.Export(X509ContentType.Cert);
                NativeCallTrace.Mark("tls.callback.export.end", $"der={der.Length}");

                NativeCallTrace.Mark("tls.callback.load.begin");
                using var leaf = X509CertificateLoader.LoadCertificate(der);
                NativeCallTrace.Mark("tls.callback.load.end");

                X509Certificate2Collection? extras = null;
                NativeCallTrace.Mark("tls.callback.chain.begin",
                    chain is null ? "null-chain" : $"elems={chain.ChainElements.Count}");
                if (chain is not null && chain.ChainElements.Count > 1)
                {
                    extras = new X509Certificate2Collection();
                    // Skip element 0 (the leaf); the rest are presented intermediates.
                    for (var i = 1; i < chain.ChainElements.Count; i++)
                        extras.Add(chain.ChainElements[i].Certificate);
                }
                NativeCallTrace.Mark("tls.callback.chain.end");

                NativeCallTrace.Mark("tls.callback.verify.begin");
                var ok = CertificateVerifier.Verify(
                    leaf,
                    extras,
                    options.ServerName,
                    RootCertificates.Default,
                    options.ValidationTime);
                NativeCallTrace.Mark("tls.callback.verify.end", ok ? "ok" : "rejected");
                if (!ok)
                    certificateRejected = true;
                return ok;
            });

        var authOptions = new SslClientAuthenticationOptions
        {
            TargetHost = options.ServerName,
            // Let the OS TLS stack negotiate the best mutually-supported
            // protocol. The predecessor BcTlsTransport spoke TLS 1.3 in pure
            // managed code on every platform, but SslStream delegates to the
            // OS: on macOS / Mac Catalyst it is backed by Secure Transport,
            // which has no TLS 1.3 support — pinning SslProtocols.Tls13 there
            // throws PlatformNotSupportedException and fails every handshake.
            // SslProtocols.None defers to the OS (and its crypto policy):
            // TLS 1.2 on Apple platforms, TLS 1.3 on Linux/Windows.
            EnabledSslProtocols = SslProtocols.None,
            ApplicationProtocols = options.ApplicationProtocols
                .Select(p => new SslApplicationProtocol(p))
                .ToList(),
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
        };

        // Serialize the whole handshake — AuthenticateAsClientAsync does not
        // return until the validation callback has run, so holding the gate
        // across it also serializes SslStream's internal chain building and
        // our callback. See HandshakeGate above.
        bool acquiredGate = false;
        try
        {
            await HandshakeGate.WaitAsync(ct).ConfigureAwait(false);
            acquiredGate = true;

            NativeCallTrace.Mark("tls.authenticate.begin", options.ServerName);
            await sslStream.AuthenticateAsClientAsync(authOptions, ct).ConfigureAwait(false);
            NativeCallTrace.Mark("tls.authenticate.end", options.ServerName);

            var negotiated = sslStream.NegotiatedApplicationProtocol;
            string? negotiatedName = negotiated.Protocol.IsEmpty
                ? null
                : System.Text.Encoding.ASCII.GetString(negotiated.Protocol.Span);

            return Result<SslStreamTlsTransport, TlsError>.Ok(
                new SslStreamTlsTransport(sslStream, tcpStream, negotiatedName));
        }
        catch (AuthenticationException) when (certificateRejected)
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return Result<SslStreamTlsTransport, TlsError>.Err(TlsError.CertificateRejected);
        }
        catch
        {
            await sslStream.DisposeAsync().ConfigureAwait(false);
            return certificateRejected
                ? Result<SslStreamTlsTransport, TlsError>.Err(TlsError.CertificateRejected)
                : Result<SslStreamTlsTransport, TlsError>.Err(TlsError.HandshakeFailed);
        }
        finally
        {
            if (acquiredGate)
                HandshakeGate.Release();
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sslStream.Dispose();
        _tcpStream.Dispose();
    }
}
