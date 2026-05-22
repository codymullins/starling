using Starling.Net.Tcp;

namespace Starling.Net.Http;

/// <summary>
/// A live, in-use connection over which one HTTP/1.1 request/response cycle
/// can be conducted. May be plain TCP or TLS-wrapped TCP. Owned either by the
/// caller (when freshly dialed) or by a <see cref="ConnectionPool"/> (when
/// it's an idle, kept-alive transport awaiting reuse).
/// </summary>
/// <remarks>
/// The concrete implementations wrap an <see cref="ITcpConnection"/> plus
/// optionally a TLS transport, and expose the resulting byte
/// <see cref="System.IO.Stream"/> that the HTTP layer reads/writes through.
/// Disposing the transport tears down the TLS session (if any) and the
/// underlying TCP socket — there is no "soft" close that keeps the socket
/// alive.
/// </remarks>
public interface IHttpTransport : IAsyncDisposable
{
    /// <summary>The origin (scheme/host/port) this transport is bound to.</summary>
    OriginKey Origin { get; }

    /// <summary>Byte stream the HTTP request writer / response parser operate on.</summary>
    Stream Stream { get; }

    /// <summary>
    /// The ALPN protocol negotiated during the TLS handshake (e.g. <c>"h2"</c>
    /// or <c>"http/1.1"</c>), or null for plain HTTP / when no ALPN was agreed.
    /// Selects whether the HTTP/2 or HTTP/1.1 path drives this transport.
    /// </summary>
    string? Alpn { get; }

    /// <summary>True until either side closes the connection.</summary>
    bool IsOpen { get; }
}
