using FluentAssertions;
using Starling.Net.Dns;
namespace Starling.Net.Tests.Dns;

[TestClass]
public class DnsMessageTests
{
    [TestMethod]
    public void EncodeName_simple_hostname()
    {
        var bytes = DnsMessage.EncodeName("example.com");
        // 7,e,x,a,m,p,l,e, 3,c,o,m, 0
        bytes.Should().Equal(
            0x07, (byte)'e', (byte)'x', (byte)'a', (byte)'m', (byte)'p', (byte)'l', (byte)'e',
            0x03, (byte)'c', (byte)'o', (byte)'m',
            0x00);
    }

    [TestMethod]
    public void EncodeName_with_trailing_dot_is_same_as_without()
    {
        DnsMessage.EncodeName("example.com.").Should().Equal(DnsMessage.EncodeName("example.com"));
    }

    [TestMethod]
    public void EncodeName_empty_emits_root_only()
    {
        DnsMessage.EncodeName("").Should().Equal((byte)0);
    }

    [TestMethod]
    public void EncodeName_label_over_63_is_rejected()
    {
        var act = () => DnsMessage.EncodeName(new string('a', 64));
        act.Should().Throw<FormatException>();
    }

    [TestMethod]
    public void BuildQuery_writes_expected_header()
    {
        var pkt = DnsMessage.BuildQuery(0xABCD, "example.com", DnsMessage.QType.A);
        pkt.Length.Should().Be(12 + 13 + 4); // header + name + qtype/qclass
        pkt[0].Should().Be(0xAB);
        pkt[1].Should().Be(0xCD);
        pkt[2].Should().Be(0x01); // RD=1
        pkt[3].Should().Be(0x00);
        // qdcount=1
        pkt[4].Should().Be(0);
        pkt[5].Should().Be(1);
    }

    [TestMethod]
    public void DecodeName_roundtrips_simple_name()
    {
        var encoded = DnsMessage.EncodeName("a.b.c");
        // Place at offset 0 of a buffer.
        var (name, next) = DnsMessage.DecodeName(encoded, 0);
        name.Should().Be("a.b.c");
        next.Should().Be(encoded.Length);
    }

    [TestMethod]
    public void DecodeName_follows_compression_pointer()
    {
        // Build a packet that contains "example.com" at offset 12, and a
        // pointer at offset 30 referencing offset 12.
        var name = DnsMessage.EncodeName("example.com");
        var pkt = new byte[12 + name.Length + 2];
        Array.Copy(name, 0, pkt, 12, name.Length);
        pkt[12 + name.Length] = 0xC0;     // pointer high byte
        pkt[12 + name.Length + 1] = 12;   // → offset 12

        var (decoded, _) = DnsMessage.DecodeName(pkt, 12 + name.Length);
        decoded.Should().Be("example.com");
    }

    [TestMethod]
    public void Parse_response_with_one_A_answer()
    {
        // Build a tiny synthetic response: id, flags (QR=1, RA=1, RCODE=0),
        // QDCOUNT=1, ANCOUNT=1.
        var name = DnsMessage.EncodeName("example.com");
        var pkt = new byte[12 + name.Length + 4   // question
                            + name.Length + 10 + 4]; // answer
        // Header
        pkt[0] = 0xAB; pkt[1] = 0xCD;
        pkt[2] = 0x81; // QR=1, RD=1
        pkt[3] = 0x80; // RA=1, RCODE=0
        pkt[5] = 1; // QDCOUNT
        pkt[7] = 1; // ANCOUNT

        // Question
        Array.Copy(name, 0, pkt, 12, name.Length);
        var off = 12 + name.Length;
        pkt[off + 1] = (byte)DnsMessage.QType.A;
        pkt[off + 3] = (byte)DnsMessage.QClass.IN;

        // Answer (NAME, TYPE=A, CLASS=IN, TTL=300, RDLENGTH=4, RDATA=93.184.216.34)
        var aoff = off + 4;
        Array.Copy(name, 0, pkt, aoff, name.Length);
        var raoff = aoff + name.Length;
        pkt[raoff + 1] = (byte)DnsMessage.QType.A;
        pkt[raoff + 3] = (byte)DnsMessage.QClass.IN;
        pkt[raoff + 4] = 0; pkt[raoff + 5] = 0;
        pkt[raoff + 6] = 0x01; pkt[raoff + 7] = 0x2C; // TTL = 300
        pkt[raoff + 8] = 0; pkt[raoff + 9] = 4;
        pkt[raoff + 10] = 93; pkt[raoff + 11] = 184;
        pkt[raoff + 12] = 216; pkt[raoff + 13] = 34;

        var (header, questions, answers) = DnsMessage.Parse(pkt);
        header.Rcode.Should().Be(DnsMessage.RCode.NoError);
        header.AnCount.Should().Be(1);
        questions.Should().ContainSingle()
            .Which.Name.Should().Be("example.com");
        answers.Should().ContainSingle()
            .Which.Should().BeOfType<DnsMessage.AAnswer>()
            .Which.IPv4.Should().Equal(93, 184, 216, 34);
        ((DnsMessage.AAnswer)answers[0]).Ttl.Should().Be(300u);
    }
}
