namespace Tessera.Net.Http;

public enum HttpError
{
    /// <summary>Malformed status line.</summary>
    BadStatusLine,
    /// <summary>Malformed header (missing colon, invalid token, etc.).</summary>
    BadHeader,
    /// <summary>Chunked framing was syntactically broken.</summary>
    BadChunkedFraming,
    /// <summary>Connection closed before the response was complete.</summary>
    UnexpectedEof,
    /// <summary>Response header block exceeded the configured size cap.</summary>
    HeadersTooLarge,
    /// <summary>Response body exceeded the configured size cap.</summary>
    BodyTooLarge,
    /// <summary>Content-Encoding referenced an algorithm we don't support.</summary>
    UnsupportedEncoding,
    /// <summary>Content-Encoding payload failed to decode (truncated/corrupt).</summary>
    DecodeFailed,
    /// <summary>An IO error happened on the underlying transport.</summary>
    TransportFailure,
}
