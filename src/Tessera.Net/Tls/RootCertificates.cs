using Org.BouncyCastle.X509;

namespace Tessera.Net.Tls;

public sealed class RootCertificates
{
    private const string ResourceSuffix = ".Resources.Roots.ccadb.pem";
    private readonly IReadOnlyList<X509Certificate> _certificates;

    private RootCertificates(IReadOnlyList<X509Certificate> certificates)
    {
        _certificates = certificates;
    }

    public static RootCertificates Default { get; } = LoadDefault();

    public IReadOnlyList<X509Certificate> Certificates => _certificates;

    public static RootCertificates FromPem(Stream pemStream)
    {
        if (pemStream is null) throw new ArgumentNullException(nameof(pemStream));
        var parser = new X509CertificateParser();
        var certificates = parser.ReadCertificates(pemStream).ToArray();
        if (certificates.Length == 0)
            throw new InvalidDataException("root certificate bundle is empty");
        return new RootCertificates(certificates);
    }

    private static RootCertificates LoadDefault()
    {
        var assembly = typeof(RootCertificates).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(ResourceSuffix, StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"Missing embedded root store resource: {resourceName}");
        return FromPem(stream);
    }
}
