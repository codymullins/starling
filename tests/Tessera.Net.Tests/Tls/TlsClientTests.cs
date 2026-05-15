using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using FluentAssertions;
using Tessera.Net.Tcp;
using Tessera.Net.Tls;
using Xunit;

namespace Tessera.Net.Tests.Tls;

public class TlsClientTests
{
    [Fact]
    public void Default_root_store_loads_embedded_ccadb_bundle()
    {
        RootCertificates.Default.Certificates.Count.Should().BeGreaterThan(50);
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
    public void Certificate_host_name_matcher_reads_dns_sans()
    {
        using var certificate = CreateSelfSigned("CN=host.example", "host.example", "*.alt.example");

        CertificateHostNameMatcher.Matches(certificate, "host.example").Should().BeTrue();
        CertificateHostNameMatcher.Matches(certificate, "any.alt.example").Should().BeTrue();
        CertificateHostNameMatcher.Matches(certificate, "other.example").Should().BeFalse();
    }

    [Fact]
    public async Task Invalid_options_return_error_before_handshake()
    {
        var result = await SslStreamTlsTransport.ConnectAsync(
            new ClosedConnection(),
            new TlsClientOptions("", []),
            TestContext.Current.CancellationToken);

        result.IsErr.Should().BeTrue();
        result.Error.Should().Be(TlsError.InvalidOptions);
    }

    [Fact]
    public void Untrusted_self_signed_certificate_chain_is_rejected()
    {
        using var certificate = CreateSelfSigned("CN=bad.example", "bad.example");

        CertificateVerifier.Verify(certificate, extraCertificates: null, "bad.example", RootCertificates.Default)
            .Should().BeFalse();
    }

    [Fact]
    public void Certificate_not_matching_hostname_is_rejected()
    {
        using var certificate = CreateSelfSigned("CN=bad.example", "bad.example");

        CertificateVerifier.Verify(certificate, extraCertificates: null, "other.example", RootCertificates.Default)
            .Should().BeFalse();
    }

    [Fact]
    public void Certificate_chaining_to_custom_trust_anchor_is_accepted()
    {
        using var rootKey = RSA.Create(2048);
        var rootRequest = new CertificateRequest(
            "CN=Tessera Test Root", rootKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        rootRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: true, hasPathLengthConstraint: false, 0, critical: true));
        using var root = rootRequest.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var leafKey = RSA.Create(2048);
        var leafRequest = new CertificateRequest(
            "CN=leaf.example", leafKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("leaf.example");
        leafRequest.CertificateExtensions.Add(san.Build());
        leafRequest.CertificateExtensions.Add(
            new X509BasicConstraintsExtension(certificateAuthority: false, hasPathLengthConstraint: false, 0, critical: true));

        var serial = new byte[8];
        RandomNumberGenerator.Fill(serial);
        using var signedLeaf = leafRequest.Create(
            root, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), serial);
        using var leaf = signedLeaf.CopyWithPrivateKey(leafKey);

        // Default trust store does not include our test root.
        CertificateVerifier.Verify(leaf, extraCertificates: null, "leaf.example", RootCertificates.Default)
            .Should().BeFalse();

        var customRoots = LoadRootsWith(root);
        CertificateVerifier.Verify(leaf, extraCertificates: null, "leaf.example", customRoots)
            .Should().BeTrue();
    }

    [Fact]
    public void Concurrent_verification_against_shared_root_store_is_stable()
    {
        // Regression: RootCertificates.Default is a process-wide singleton whose
        // X509Certificate2 instances are not thread-safe. Concurrent chain
        // builds used to race on their native handles and corrupt the heap
        // (SIGSEGV). Hammer the shared store from many threads and assert a
        // consistent result with no crash.
        using var certificate = CreateSelfSigned("CN=bad.example", "bad.example");

        var results = new System.Collections.Concurrent.ConcurrentBag<bool>();
        Parallel.For(0, 256, new ParallelOptions { MaxDegreeOfParallelism = 16 }, _ =>
            results.Add(CertificateVerifier.Verify(
                certificate, extraCertificates: null, "bad.example", RootCertificates.Default)));

        results.Should().HaveCount(256).And.OnlyContain(ok => ok == false);
    }

    [Theory]
    [InlineData("example.com")]
    [InlineData("nginx.org")]
    public async Task Live_handshake_when_enabled(string host)
    {
        // Env-gated: the CI `network-tests` job sets TESSERA_ALLOW_NETWORK=1.
        // SslStreamTlsTransport lets the OS negotiate the protocol version
        // (SslProtocols.None): TLS 1.2 on macOS/Mac Catalyst (Secure Transport
        // has no 1.3), TLS 1.3 on the Linux/Windows runners. So this exercises
        // a real handshake on every platform, not just the Linux runner.
        if (Environment.GetEnvironmentVariable("TESSERA_ALLOW_NETWORK") != "1")
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
        var tls = await SslStreamTlsTransport.ConnectAsync(
            connection,
            TlsClientOptions.ForHttps(host),
            ct);

        tls.IsOk.Should().BeTrue();
        tls.Value.NegotiatedApplicationProtocol.Should().BeOneOf("h2", "http/1.1", null);
        tls.Value.Dispose();
    }

    private static X509Certificate2 CreateSelfSigned(string subject, params string[] dnsNames)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            subject, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var san = new SubjectAlternativeNameBuilder();
        foreach (var dnsName in dnsNames)
            san.AddDnsName(dnsName);
        request.CertificateExtensions.Add(san.Build());
        return request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
    }

    private static RootCertificates LoadRootsWith(X509Certificate2 extraRoot)
    {
        var assembly = typeof(RootCertificates).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".Resources.Roots.ccadb.pem", StringComparison.Ordinal));
        using var bundle = assembly.GetManifestResourceStream(resourceName)!;
        using var reader = new StreamReader(bundle);
        var pem = reader.ReadToEnd()
            + Environment.NewLine
            + extraRoot.ExportCertificatePem();

        using var ms = new MemoryStream(System.Text.Encoding.ASCII.GetBytes(pem));
        return RootCertificates.FromPem(ms);
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
