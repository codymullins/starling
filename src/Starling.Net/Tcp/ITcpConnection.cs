namespace Tessera.Net.Tcp;

/// <summary>
/// Async, byte-oriented TCP connection. Closed via <see cref="IAsyncDisposable"/>.
/// </summary>
/// <remarks>
/// Read returns 0 to signal a clean half-close from the peer (matches
/// <see cref="System.IO.Stream.ReadAsync(byte[], int, int, System.Threading.CancellationToken)"/>'s
/// semantics). Write may return fewer bytes than requested only when the
/// underlying socket is shutting down; otherwise it loops internally.
/// </remarks>
public interface ITcpConnection : IAsyncDisposable
{
    /// <summary>Remote endpoint we connected to (as written by the dialer).</summary>
    TcpEndpoint Endpoint { get; }

    /// <summary>True until either side closes the connection.</summary>
    bool IsOpen { get; }

    ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct);
    ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct);
    ValueTask ShutdownAsync(CancellationToken ct);
}
