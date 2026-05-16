using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.X509;

namespace Tessera.Net.Tls;

public static class CertificateVerifier
{
    public static bool Verify(
        Certificate certificate,
        string hostname,
        RootCertificates roots,
        DateTimeOffset? validationTime = null)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        if (roots is null) throw new ArgumentNullException(nameof(roots));
        if (string.IsNullOrWhiteSpace(hostname)) return false;

        var chain = DecodeChain(certificate);
        if (chain.Count == 0) return false;

        var now = (validationTime ?? DateTimeOffset.UtcNow).UtcDateTime;
        if (!chain.All(c => c.IsValid(now))) return false;
        if (!CertificateHostNameMatcher.Matches(chain[0], hostname)) return false;
        if (!VerifyPresentedChain(chain)) return false;

        return ChainsToTrustedRoot(chain[^1], roots.Certificates);
    }

    private static List<X509Certificate> DecodeChain(Certificate certificate)
    {
        var parser = new X509CertificateParser();
        return certificate.GetCertificateList()
            .Select(tlsCertificate => parser.ReadCertificate(tlsCertificate.GetEncoded()))
            .ToList();
    }

    private static bool VerifyPresentedChain(List<X509Certificate> chain)
    {
        for (var i = 0; i < chain.Count - 1; i++)
        {
            if (!chain[i].IssuerDN.Equivalent(chain[i + 1].SubjectDN))
                return false;
            try
            {
                chain[i].Verify(chain[i + 1].GetPublicKey());
            }
            catch
            {
                return false;
            }
        }

        return true;
    }

    private static bool ChainsToTrustedRoot(X509Certificate lastPresented, IReadOnlyList<X509Certificate> roots)
    {
        foreach (var root in roots)
        {
            if (lastPresented.SubjectDN.Equivalent(root.SubjectDN)
                && SamePublicKey(lastPresented, root))
                return true;

            if (!lastPresented.IssuerDN.Equivalent(root.SubjectDN))
                continue;

            try
            {
                lastPresented.Verify(root.GetPublicKey());
                return true;
            }
            catch
            {
                // Try the next root with the same subject; some stores have cross-signs.
            }
        }

        return false;
    }

    private static bool SamePublicKey(X509Certificate left, X509Certificate right)
    {
        var leftKey = SubjectPublicKeyInfoFactory
            .CreateSubjectPublicKeyInfo(left.GetPublicKey())
            .GetEncoded();
        var rightKey = SubjectPublicKeyInfoFactory
            .CreateSubjectPublicKeyInfo(right.GetPublicKey())
            .GetEncoded();
        return leftKey.AsSpan().SequenceEqual(rightKey);
    }
}

public static class CertificateHostNameMatcher
{
    public static bool Matches(X509Certificate certificate, string hostname)
    {
        if (certificate is null) throw new ArgumentNullException(nameof(certificate));
        if (string.IsNullOrWhiteSpace(hostname)) return false;

        var normalizedHost = hostname.Trim().TrimEnd('.').ToLowerInvariant();
        var names = certificate.GetSubjectAlternativeNames();
        if (names is null || names.Count == 0)
            return false;

        foreach (var name in names)
        {
            if (name.Count < 2 || name[0] is not int { } type || type != GeneralName.DnsName)
                continue;
            if (name[1] is string dnsName && MatchDnsName(dnsName, normalizedHost))
                return true;
        }

        return false;
    }

    public static bool MatchDnsName(string pattern, string hostname)
    {
        var normalizedPattern = pattern.Trim().TrimEnd('.').ToLowerInvariant();
        var normalizedHost = hostname.Trim().TrimEnd('.').ToLowerInvariant();
        if (normalizedPattern.Length == 0 || normalizedHost.Length == 0)
            return false;
        if (!normalizedPattern.Contains('*', StringComparison.Ordinal))
            return normalizedPattern == normalizedHost;

        if (!normalizedPattern.StartsWith("*.", StringComparison.Ordinal)
            || normalizedPattern.IndexOf('*', 1) >= 0)
            return false;

        var suffix = normalizedPattern[1..];
        if (!normalizedHost.EndsWith(suffix, StringComparison.Ordinal))
            return false;

        var unmatched = normalizedHost[..^suffix.Length];
        return unmatched.Length > 0 && !unmatched.Contains('.', StringComparison.Ordinal);
    }
}
