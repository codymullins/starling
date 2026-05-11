using Tessera.Common;

namespace Tessera.Url;

/// <summary>
/// Tiny URL parser. Handles the three schemes M0 cares about. M2 replaces this
/// with the full WHATWG implementation per 03_NETWORKING.md.
/// </summary>
public static class UrlParser
{
    public enum ParseError
    {
        Empty,
        MissingScheme,
        UnsupportedScheme,
        MalformedAuthority,
    }

    public static Result<Url, ParseError> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return Result<Url, ParseError>.Err(ParseError.Empty);

        var s = input.Trim();
        var colon = s.IndexOf(':', StringComparison.Ordinal);
        if (colon <= 0)
            return Result<Url, ParseError>.Err(ParseError.MissingScheme);

        var scheme = s[..colon].ToLowerInvariant();
        var rest = s[(colon + 1)..];

        return scheme switch
        {
            "file" => ParseFile(rest),
            "http" or "https" => ParseHttp(scheme, rest),
            _ => Result<Url, ParseError>.Err(ParseError.UnsupportedScheme),
        };
    }

    private static Result<Url, ParseError> ParseFile(string rest)
    {
        // file:[//host]/path[?query][#fragment]
        string? host = null;
        var path = rest;
        if (rest.StartsWith("//", StringComparison.Ordinal))
        {
            var afterSlashes = rest[2..];
            var slash = afterSlashes.IndexOf('/', StringComparison.Ordinal);
            if (slash < 0)
            {
                host = afterSlashes;
                path = "/";
            }
            else
            {
                host = afterSlashes[..slash];
                path = afterSlashes[slash..];
            }
            if (host.Length == 0 || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                host = null;
        }

        var (cleanPath, query, fragment) = SplitPathQueryFragment(path);
        return Result<Url, ParseError>.Ok(new Url("file", host, null, cleanPath, query, fragment));
    }

    private static Result<Url, ParseError> ParseHttp(string scheme, string rest)
    {
        if (!rest.StartsWith("//", StringComparison.Ordinal))
            return Result<Url, ParseError>.Err(ParseError.MalformedAuthority);

        var afterSlashes = rest[2..];
        var slash = afterSlashes.IndexOf('/', StringComparison.Ordinal);
        string authority;
        string path;
        if (slash < 0)
        {
            authority = afterSlashes;
            path = "/";
        }
        else
        {
            authority = afterSlashes[..slash];
            path = afterSlashes[slash..];
        }

        // authority = [userinfo@]host[:port] — userinfo ignored for now
        var at = authority.IndexOf('@', StringComparison.Ordinal);
        if (at >= 0) authority = authority[(at + 1)..];

        int? port = null;
        var portColon = authority.LastIndexOf(':');
        // For IPv6 the host is bracketed; we don't bother in M0.
        if (portColon >= 0 && !authority.Contains(']', StringComparison.Ordinal))
        {
            if (int.TryParse(authority[(portColon + 1)..], out var p))
            {
                port = p;
                authority = authority[..portColon];
            }
        }

        if (authority.Length == 0)
            return Result<Url, ParseError>.Err(ParseError.MalformedAuthority);

        var (cleanPath, query, fragment) = SplitPathQueryFragment(path);
        return Result<Url, ParseError>.Ok(new Url(scheme, authority, port, cleanPath, query, fragment));
    }

    private static (string path, string? query, string? fragment) SplitPathQueryFragment(string s)
    {
        string? fragment = null;
        var hash = s.IndexOf('#', StringComparison.Ordinal);
        if (hash >= 0)
        {
            fragment = s[(hash + 1)..];
            s = s[..hash];
        }
        string? query = null;
        var q = s.IndexOf('?', StringComparison.Ordinal);
        if (q >= 0)
        {
            query = s[(q + 1)..];
            s = s[..q];
        }
        return (s, query, fragment);
    }
}
