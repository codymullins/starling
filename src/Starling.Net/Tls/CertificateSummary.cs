namespace Starling.Net.Tls;

/// <summary>
/// A human-facing summary of the leaf certificate presented by a TLS peer,
/// captured during the handshake. Only produced for certificates that passed
/// verification (Starling fails closed — see <see cref="CertificateVerifier"/>),
/// so a non-null summary always describes a chain that validated against the
/// bundled CCADB root store.
/// </summary>
public sealed record CertificateSummary(
    string Subject,
    string Issuer,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);
