namespace Tessera.Net.Tls;

public sealed record TlsClientOptions(
    string ServerName,
    IReadOnlyList<string> ApplicationProtocols,
    DateTimeOffset? ValidationTime = null)
{
    public static TlsClientOptions ForHttps(string serverName) =>
        new(serverName, ["h2", "http/1.1"]);
}
