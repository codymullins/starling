using FluentAssertions;
using Starling.Net.Http.Cookies;
namespace Starling.Net.Tests.Http;

[TestClass]
public class PublicSuffixListTests
{
    [TestMethod]
    public void Default_psl_loads_a_substantial_rule_count()
    {
        // The bundled PSL has thousands of rules; sanity-check it's the real
        // file and not an empty stub.
        PublicSuffixList.Default.RuleCount.Should().BeGreaterThan(5_000);
    }

    [TestMethod]
    [DataRow("com", true)]
    [DataRow("co.uk", true)]
    [DataRow("github.io", true)]
    [DataRow("example.com", false)]
    [DataRow("example.co.uk", false)]
    [DataRow("foo.bar.example.co.uk", false)]
    public void Default_psl_classifies_common_hosts(string domain, bool isPs)
    {
        PublicSuffixList.Default.IsPublicSuffix(domain).Should().Be(isPs);
    }

    [TestMethod]
    public void Single_label_unknown_tld_is_treated_as_public_suffix_via_default_rule()
    {
        // No rule exists for "totallyfakeldfknv2"; the default "*" rule applies.
        PublicSuffixList.Default.IsPublicSuffix("totallyfakeldfknv2").Should().BeTrue();
        PublicSuffixList.Default.IsPublicSuffix("example.totallyfakeldfknv2").Should().BeFalse();
    }

    [TestMethod]
    public void Parse_handles_inline_comments_and_blank_lines()
    {
        var psl = PublicSuffixList.Parse("""
            // header comment
            com
            // second comment
            co.uk
            // wildcard
            *.kawasaki.jp
            // exception
            !city.kawasaki.jp
            """);
        psl.IsPublicSuffix("com").Should().BeTrue();
        psl.IsPublicSuffix("co.uk").Should().BeTrue();

        // *.kawasaki.jp matches any one label under kawasaki.jp.
        psl.IsPublicSuffix("foo.kawasaki.jp").Should().BeTrue();

        // !city.kawasaki.jp is an exception → city.kawasaki.jp is NOT a public suffix
        // even though *.kawasaki.jp would otherwise match it.
        psl.IsPublicSuffix("city.kawasaki.jp").Should().BeFalse();
    }

    [TestMethod]
    [DataRow("EXAMPLE.com", false)]
    [DataRow("example.com.", false)]
    [DataRow("CO.UK", true)]
    public void Lookup_is_case_and_trailing_dot_insensitive(string host, bool isPs)
    {
        PublicSuffixList.Default.IsPublicSuffix(host).Should().Be(isPs);
    }
}
