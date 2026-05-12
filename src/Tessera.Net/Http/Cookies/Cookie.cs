namespace Tessera.Net.Http.Cookies;

public enum SameSiteMode
{
    /// <summary>Default per RFC 6265bis when no SameSite attribute is present.</summary>
    Lax,
    Strict,
    /// <summary>Cross-site usage. Only valid when <c>Secure</c> is also set.</summary>
    None,
}

/// <summary>
/// A stored HTTP cookie. Mutable fields like <see cref="LastAccessUtc"/> are
/// updated by the jar; identity is the (Name, Domain, Path) tuple.
/// </summary>
internal sealed class Cookie
{
    public required string Name { get; init; }
    public required string Value { get; init; }
    public required string Domain { get; init; }    // canonical lowercase, no leading dot
    public required string Path { get; init; }
    public required DateTimeOffset CreationUtc { get; init; }

    public DateTimeOffset LastAccessUtc { get; set; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public bool Persistent { get; init; }
    public bool HostOnly { get; init; }
    public bool Secure { get; init; }
    public bool HttpOnly { get; init; }
    public SameSiteMode SameSite { get; init; } = SameSiteMode.Lax;

    public bool IsExpired(DateTimeOffset now) =>
        ExpiresUtc is { } e && e <= now;
}
