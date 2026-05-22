using Starling.Common;
using Starling.Net.Http.H2.Hpack;

namespace Starling.Net.Http.H2;

/// <summary>
/// State for a single client-initiated HTTP/2 stream: the response being
/// assembled from HEADERS/DATA frames, the send-side flow-control window for
/// any request body, and the completion source the caller awaits.
/// </summary>
/// <remarks>
/// Mutable fields are written by the connection's single reader loop or under
/// the connection lock; the caller only reads <see cref="Completion"/>.
/// </remarks>
internal sealed class H2Stream(int id)
{
    public int Id { get; } = id;

    /// <summary>Completed once the full response is assembled, or on stream/connection failure.</summary>
    public TaskCompletionSource<Result<HttpResponse, NetworkError>> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Final (non-1xx) response headers, set when the response HEADERS block decodes.</summary>
    public List<HpackHeaderField>? ResponseHeaders { get; set; }

    /// <summary>Accumulated, de-padded response body bytes (still content-encoded).</summary>
    public MemoryStream Body { get; } = new();

    /// <summary>Remaining peer flow-control credit for sending this stream's body.</summary>
    public int SendWindow { get; set; }

    /// <summary>True once the result has been published, to guard double-completion.</summary>
    public bool Finished { get; set; }
}
