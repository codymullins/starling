using Org.BouncyCastle.Tls;

namespace Tessera.Net.Tls;

internal sealed class TesseraTlsAuthentication : TlsAuthentication
{
    private readonly TlsClientOptions _options;
    private readonly RootCertificates _roots;

    public TesseraTlsAuthentication(TlsClientOptions options, RootCertificates roots)
    {
        _options = options;
        _roots = roots;
    }

    public void NotifyServerCertificate(TlsServerCertificate serverCertificate)
    {
        if (!CertificateVerifier.Verify(serverCertificate.Certificate, _options.ServerName, _roots, _options.ValidationTime))
            throw new TlsFatalAlert(AlertDescription.bad_certificate, "server certificate validation failed");
    }

    public TlsCredentials? GetClientCredentials(CertificateRequest certificateRequest) => null;
}
