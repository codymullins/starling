using System.Net;
using System.Net.Sockets;

namespace Tessera.Net.Dns;

/// <summary>
/// Real UDP transport for DNS queries. Sends to a configured resolver IP:port
/// (default 8.8.8.8:53 — Google Public DNS). Pure managed via
/// <see cref="System.Net.Sockets"/> per Rule 0.
/// </summary>
/// <remarks>
/// Single round-trip; no retransmit, no fallback to TCP. Adequate for v1
/// hostnames whose responses fit in 512 bytes (typical A/AAAA records).
/// Larger responses (DNSSEC, many records) will require TC=1 handling +
/// TCP fallback per RFC 1035 §4.2.2 — deferred to a follow-up.
/// </remarks>
public sealed class UdpDnsTransport : IDnsTransport
{
    public IPEndPoint Resolver { get; }
    public TimeSpan Timeout { get; }

    public UdpDnsTransport()
        : this(new IPEndPoint(IPAddress.Parse("8.8.8.8"), 53), TimeSpan.FromSeconds(5)) { }

    public UdpDnsTransport(IPEndPoint resolver, TimeSpan timeout)
    {
        Resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        Timeout = timeout;
    }

    public async Task<byte[]> SendAsync(byte[] queryPacket, CancellationToken ct)
    {
        using var client = new UdpClient(AddressFamily.InterNetwork);
        // Bound timeout — if the resolver doesn't answer, we surface the
        // cancellation as OperationCanceledException to the caller.
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(Timeout);

        await client.SendAsync(queryPacket, queryPacket.Length, Resolver)
            .WaitAsync(cts.Token).ConfigureAwait(false);
        var result = await client.ReceiveAsync(cts.Token).ConfigureAwait(false);
        return result.Buffer;
    }
}
