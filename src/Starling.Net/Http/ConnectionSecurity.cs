using Starling.Net.Tls;

namespace Starling.Net.Http;

/// <summary>
/// Security context a response was fetched over: the negotiated HTTP version,
/// whether the transport was encrypted (TLS), and — for HTTPS — the verified
/// leaf certificate. Surfaced to the shell's "lock" affordance.
/// </summary>
/// <remarks>
/// Starling fails closed on certificate errors: an invalid chain aborts the
/// connection before any response is produced, so a populated
/// <see cref="Certificate"/> always means a chain that validated against the
/// bundled root store. There is no "proceed anyway" path in v1.
/// </remarks>
public sealed record ConnectionSecurity(
    string Protocol,
    bool IsEncrypted,
    CertificateSummary? Certificate)
{
    /// <summary>True when the response came over TLS with a verified certificate.</summary>
    public bool IsSecure => IsEncrypted && Certificate is not null;
}
