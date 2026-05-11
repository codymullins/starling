using System.Net.Sockets;

namespace Tessera.Net.Tcp;

/// <summary>
/// <see cref="ITcpConnection"/> implementation backed by a real
/// <see cref="System.Net.Sockets.Socket"/>. Pure managed per Rule 0.
/// </summary>
internal sealed class SocketTcpConnection : ITcpConnection
{
    private readonly Socket _socket;
    private bool _open = true;

    public TcpEndpoint Endpoint { get; }

    public SocketTcpConnection(Socket socket, TcpEndpoint endpoint)
    {
        _socket = socket ?? throw new ArgumentNullException(nameof(socket));
        Endpoint = endpoint;
    }

    public bool IsOpen => _open && _socket.Connected;

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct)
    {
        if (!_open) return 0;
        var n = await _socket.ReceiveAsync(buffer, SocketFlags.None, ct)
            .ConfigureAwait(false);
        if (n == 0) _open = false; // peer closed
        return n;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        if (!_open) throw new InvalidOperationException("connection is closed");
        var sent = 0;
        while (sent < data.Length)
        {
            var n = await _socket.SendAsync(data[sent..], SocketFlags.None, ct)
                .ConfigureAwait(false);
            if (n == 0) { _open = false; break; }
            sent += n;
        }
    }

    public async ValueTask ShutdownAsync(CancellationToken ct)
    {
        if (!_open) return;
        _open = false;
        try { _socket.Shutdown(SocketShutdown.Both); } catch (SocketException) { }
        await Task.Yield();
        _ = ct;
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync(CancellationToken.None).ConfigureAwait(false);
        _socket.Dispose();
    }
}
