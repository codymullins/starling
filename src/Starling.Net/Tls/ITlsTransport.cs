namespace Starling.Net.Tls;

/// <summary>
/// TLS-protected byte stream established over a Starling TCP connection.
/// </summary>
public interface ITlsTransport : IDisposable
{
    Stream Stream { get; }
    string? NegotiatedApplicationProtocol { get; }

    /// <summary>The verified leaf certificate presented by the peer, or null.</summary>
    CertificateSummary? PeerCertificate { get; }
}
