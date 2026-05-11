namespace Tessera.Net.Http;

/// <summary>
/// Fully buffered HTTP response. Body is post-decoding: transfer-encoding and
/// content-encoding have already been removed.
/// </summary>
public sealed class HttpResponse
{
    public string HttpVersion { get; }
    public int StatusCode { get; }
    public string ReasonPhrase { get; }
    public HttpHeaders Headers { get; }
    public ReadOnlyMemory<byte> Body { get; }

    public HttpResponse(
        string httpVersion,
        int statusCode,
        string reasonPhrase,
        HttpHeaders headers,
        ReadOnlyMemory<byte> body)
    {
        HttpVersion = httpVersion ?? throw new ArgumentNullException(nameof(httpVersion));
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase ?? throw new ArgumentNullException(nameof(reasonPhrase));
        Headers = headers ?? throw new ArgumentNullException(nameof(headers));
        Body = body;
    }
}
