using System.Text;

namespace Tessera.Url;

/// <summary>
/// Minimal URL representation. The full WHATWG URL parser is M2 work; for M0
/// we only need enough to recognize <c>file://</c>, <c>http://</c>, and
/// <c>https://</c> schemes and surface the path. See 03_NETWORKING.md.
/// </summary>
public sealed record Url(
    string Scheme,
    string? Host,
    int? Port,
    string Path,
    string? Query,
    string? Fragment)
{
    public bool IsFile => Scheme.Equals("file", StringComparison.OrdinalIgnoreCase);
    public bool IsHttp => Scheme.Equals("http", StringComparison.OrdinalIgnoreCase);
    public bool IsHttps => Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.Append(Scheme).Append(':');
        if (Host is not null || IsFile)
        {
            sb.Append("//");
            if (Host is not null) sb.Append(Host);
            if (Port is int p) sb.Append(':').Append(p);
        }
        sb.Append(Path);
        if (Query is not null) sb.Append('?').Append(Query);
        if (Fragment is not null) sb.Append('#').Append(Fragment);
        return sb.ToString();
    }

    /// <summary>
    /// Translate a <c>file://</c> URL to a local filesystem path. Throws if not
    /// a file URL.
    /// </summary>
    public string ToFileSystemPath()
    {
        if (!IsFile)
            throw new InvalidOperationException($"URL is not a file:// URL: {this}");

        // WHATWG: file URLs may have an empty host (file:///foo) or "localhost".
        // For relative file paths supplied as `file://./foo.html` we treat the
        // path verbatim — this is loose vs spec, fine for M0.
        var path = Path;
        if (path.StartsWith("//", StringComparison.Ordinal))
            path = path[1..];
        return path;
    }
}
