namespace Tessera.Net.Tls;

/// <summary>
/// TLS-protected byte stream established over a Tessera TCP connection.
/// </summary>
public interface ITlsTransport : IDisposable
{
    Stream Stream { get; }
    string? NegotiatedApplicationProtocol { get; }
}
