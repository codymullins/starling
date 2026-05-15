using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Tls;
using Org.BouncyCastle.Tls.Crypto.Impl.BC;
using Tessera.Net.Tcp;
using Tessera.Net.Tls;
using Xunit;
using BcCertificate = Org.BouncyCastle.Tls.Certificate;
using DotNetX509Certificate = System.Security.Cryptography.X509Certificates.X509Certificate2;

namespace Tessera.Net.Tests.Tls;

public class TlsClientTests
{
    [Fact]
    public void Default_root_store_loads_embedded_ccadb_bundle()
    {
        RootCertificates.Default.Certificates.Count.Should().BeGreaterThan(50);
    }

    [Fact]
    public void Client_extensions_advertise_sni_and_alpn()
    {
        var options = TlsClientOptions.ForHttps("example.com");
        var client = new TesseraTlsClient(
            new BcTlsCrypto(new SecureRandom()),
            options,
            RootCertificates.Default);

        var extensions = client.CreateClientExtensionsForTesting();

        var names = TlsExtensionsUtilities.GetServerNameExtensionClient(extensions);
        names.Should().ContainSingle();
        names[0].NameType.Should().Be(NameType.host_name);
        names[0].NameData.Should().Equal("example.com"u8.ToArray());

        var alpn = TlsExtensionsUtilities.GetAlpnExtensionClient(extensions)
            .Select(protocol => protocol.GetUtf8Decoding())
            .ToArray();
        alpn.Should().Equal("h2", "http/1.1");
    }

    [Theory]
    [InlineData("example.com", "example.com", true)]
    [InlineData("EXAMPLE.com.", "example.com", true)]
    [InlineData("*.example.com", "www.example.com", true)]
    [InlineData("*.example.com", "deep.www.example.com", false)]
    [InlineData("*.example.com", "example.com", false)]
    [InlineData("*.*.example.com", "www.example.com", false)]
    public void Dns_name_matching_handles_rfc6125_wildcard_shape(
        string pattern,
        string hostname,
        bool expected) =>
        CertificateHostNameMatcher.MatchDnsName(pattern, hostname).Should().Be(expected);

    [Fact]
    public async Task Invalid_options_return_error_before_handshake()
    {
        var result = await BcTlsTransport.ConnectAsync(
            new ClosedConnection(),
            new TlsClientOptions("", []),
            TestContext.Current.CancellationToken);

        result.IsErr.Should().BeTrue();
        result.Error.Should().Be(TlsError.InvalidOptions);
    }

    [Fact]
    public void Untrusted_self_signed_certificate_chain_is_rejected()
    {
        using var rsa = RSA.Create(2048);
        var request = new System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=bad.example",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("bad.example");
        request.CertificateExtensions.Add(san.Build());
        using DotNetX509Certificate certificate = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddDays(1));

        var tlsCertificate = new BcTlsCrypto(new SecureRandom())
            .CreateCertificate(certificate.Export(X509ContentType.Cert));
        var chain = new BcCertificate([tlsCertificate]);

        CertificateVerifier.Verify(chain, "bad.example", RootCertificates.Default)
            .Should().BeFalse();
    }

    [Theory]
    [InlineData("cloudflare.com")]
    [InlineData("tls13.akamai.io")]
    public async Task Live_tls13_handshake_when_enabled(string host)
    {
        if (Environment.GetEnvironmentVariable("TESSERA_LIVE_TLS_TESTS") != "1")
            return;

        var ct = TestContext.Current.CancellationToken;
        var addresses = await System.Net.Dns.GetHostAddressesAsync(host, ct);
        var endpoint = new IPEndPoint(addresses.First(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork), 443);
        var dialer = new TcpDialer(new Tessera.Net.Dns.DnsResolver(new NoopDnsTransport()))
        {
            ConnectTimeout = TimeSpan.FromSeconds(10),
        };
        var tcp = await dialer.DialDirectAsync(endpoint, TcpEndpoint.For(host, 443), ct);
        tcp.IsOk.Should().BeTrue();

        await using var connection = tcp.Value;
        var tls = await BcTlsTransport.ConnectAsync(
            connection,
            TlsClientOptions.ForHttps(host),
            ct);

        tls.IsOk.Should().BeTrue();
        tls.Value.NegotiatedApplicationProtocol.Should().BeOneOf("h2", "http/1.1", null);
        tls.Value.Dispose();
    }

    private sealed class ClosedConnection : ITcpConnection
    {
        public TcpEndpoint Endpoint => TcpEndpoint.For("closed.example", 443);
        public bool IsOpen => false;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct) => ValueTask.FromResult(0);
        public ValueTask ShutdownAsync(CancellationToken ct) => ValueTask.CompletedTask;
        public ValueTask WriteAsync(ReadOnlyMemory<byte> data, CancellationToken ct) =>
            throw new InvalidOperationException("closed");
    }

    private sealed class NoopDnsTransport : Tessera.Net.Dns.IDnsTransport
    {
        public Task<byte[]> SendAsync(byte[] queryPacket, CancellationToken ct) =>
            throw new InvalidOperationException("DNS is not exercised by this test");
    }
}
