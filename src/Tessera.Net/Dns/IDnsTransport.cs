namespace Tessera.Net.Dns;

/// <summary>
/// Transport seam for the DNS resolver. Sends a query packet, returns the
/// response. The production implementation is <see cref="UdpDnsTransport"/>;
/// tests substitute a fake to drive specific canned responses.
/// </summary>
public interface IDnsTransport
{
    Task<byte[]> SendAsync(byte[] queryPacket, CancellationToken ct);
}
