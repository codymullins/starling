// `Tessera.Url` is both the namespace and the simple name of the URL type within
// it. From inside `Tessera.Net`, an unqualified `Url` binds to the sibling
// namespace, so we alias through `global::` to the type. Use a distinct alias
// name so the alias itself cannot collide with the sibling namespace.
using TesseraUrl = global::Tessera.Url.Url;

namespace Tessera.Net;

/// <summary>
/// Loud-failure stub. The full async, hand-written HTTP/1.1+HTTP/2 fetcher lives
/// in M2 — see 03_NETWORKING.md. Until then this exists so consumers can compile
/// against the seam.
/// </summary>
public interface IHttpFetcher
{
    ValueTask<HttpResponse> GetAsync(TesseraUrl url, CancellationToken ct = default);
}

public sealed record HttpResponse(
    int StatusCode,
    IReadOnlyDictionary<string, string> Headers,
    ReadOnlyMemory<byte> Body);

public sealed class NotImplementedFetcher : IHttpFetcher
{
    public ValueTask<HttpResponse> GetAsync(TesseraUrl url, CancellationToken ct = default)
        => throw new NotSupportedException(
            $"Tessera.Net is M2 work — see browser-plan/03_NETWORKING.md. Cannot GET {url}.");
}
