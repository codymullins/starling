using System.Text;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto;

namespace Tessera.Net.Tls;

internal sealed class TesseraTlsClient : DefaultTlsClient
{
    private readonly TlsClientOptions _options;
    private readonly RootCertificates _roots;
    private readonly IList<ProtocolName> _protocolNames;

    public TesseraTlsClient(TlsCrypto crypto, TlsClientOptions options, RootCertificates roots)
        : base(crypto)
    {
        _options = options;
        _roots = roots;
        _protocolNames = options.ApplicationProtocols
            .Select(ProtocolName.AsUtf8Encoding)
            .ToArray();
    }

    public string? NegotiatedApplicationProtocol { get; private set; }

    public override TlsAuthentication GetAuthentication() =>
        new TesseraTlsAuthentication(_options, _roots);

    public override IDictionary<int, byte[]> GetClientExtensions()
    {
        var extensions = TlsExtensionsUtilities.EnsureExtensionsInitialised(base.GetClientExtensions());
        return AddTesseraExtensions(extensions);
    }

    internal IDictionary<int, byte[]> CreateClientExtensionsForTesting() =>
        AddTesseraExtensions(new Dictionary<int, byte[]>());

    public override void ProcessServerExtensions(IDictionary<int, byte[]> serverExtensions)
    {
        base.ProcessServerExtensions(serverExtensions);
        var protocol = TlsExtensionsUtilities.GetAlpnExtensionServer(serverExtensions);
        NegotiatedApplicationProtocol = protocol?.GetUtf8Decoding();
    }

    protected override IList<ProtocolName> GetProtocolNames() => _protocolNames;

    protected override IList<ServerName> GetSniServerNames() =>
        [new ServerName(NameType.host_name, Encoding.ASCII.GetBytes(_options.ServerName))];

    protected override ProtocolVersion[] GetSupportedVersions() => [ProtocolVersion.TLSv13];

    protected override int[] GetSupportedCipherSuites() =>
    [
        CipherSuite.TLS_AES_128_GCM_SHA256,
        CipherSuite.TLS_AES_256_GCM_SHA384,
        CipherSuite.TLS_CHACHA20_POLY1305_SHA256,
    ];

    private IDictionary<int, byte[]> AddTesseraExtensions(IDictionary<int, byte[]> extensions)
    {
        TlsExtensionsUtilities.AddServerNameExtensionClient(extensions, GetSniServerNames());
        TlsExtensionsUtilities.AddAlpnExtensionClient(extensions, _protocolNames);
        return extensions;
    }
}
