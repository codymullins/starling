using Tessera.Common;
using Tessera.Net.Tcp;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;

namespace Tessera.Net.Tls;

/// <summary>
/// Pure-managed TLS transport built on BouncyCastle's TLS 1.3 implementation.
/// </summary>
public sealed class BcTlsTransport : ITlsTransport
{
    private readonly TlsClientProtocol _protocol;
    private readonly TcpConnectionStream _tcpStream;
    private bool _disposed;

    private BcTlsTransport(TlsClientProtocol protocol, TcpConnectionStream tcpStream, string? negotiatedApplicationProtocol)
    {
        _protocol = protocol;
        _tcpStream = tcpStream;
        NegotiatedApplicationProtocol = negotiatedApplicationProtocol;
    }

    public Stream Stream => _protocol.Stream;
    public string? NegotiatedApplicationProtocol { get; }

    public static async Task<Result<BcTlsTransport, TlsError>> ConnectAsync(
        ITcpConnection tcpConnection,
        TlsClientOptions options,
        CancellationToken ct = default)
    {
        if (tcpConnection is null) throw new ArgumentNullException(nameof(tcpConnection));
        if (options is null) throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ServerName) || options.ApplicationProtocols.Count == 0)
            return Result<BcTlsTransport, TlsError>.Err(TlsError.InvalidOptions);

        var tcpStream = new TcpConnectionStream(tcpConnection);
        var client = new TesseraTlsClient(
            new BcTlsCrypto(new SecureRandom()),
            options,
            RootCertificates.Default);
        var protocol = new TlsClientProtocol(tcpStream);

        try
        {
            await Task.Run(() => protocol.Connect(client), ct).ConfigureAwait(false);
            return Result<BcTlsTransport, TlsError>.Ok(
                new BcTlsTransport(protocol, tcpStream, client.NegotiatedApplicationProtocol));
        }
        catch (TlsFatalAlert alert) when (alert.AlertDescription is AlertDescription.bad_certificate
            or AlertDescription.certificate_expired
            or AlertDescription.certificate_revoked
            or AlertDescription.certificate_unknown
            or AlertDescription.unknown_ca)
        {
            protocol.Close();
            return Result<BcTlsTransport, TlsError>.Err(TlsError.CertificateRejected);
        }
        catch
        {
            protocol.Close();
            return Result<BcTlsTransport, TlsError>.Err(TlsError.HandshakeFailed);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _protocol.Close();
        _tcpStream.Dispose();
    }
}
