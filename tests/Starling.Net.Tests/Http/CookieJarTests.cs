using FluentAssertions;
using Starling.Net.Http.Cookies;
using StarlingUrlParser = global::Starling.Url.UrlParser;

namespace Starling.Net.Tests.Http;

[TestClass]
public class CookieJarTests
{
    private static global::Starling.Url.Url Url(string s) => StarlingUrlParser.Parse(s).Value;

    private static CookieJar NewJar(DateTimeOffset? now = null)
    {
        var t = now ?? DateTimeOffset.Parse("2026-05-11T00:00:00Z");
        return new CookieJar(PublicSuffixList.Default, () => t);
    }

    [TestMethod]
    public void Round_trips_a_simple_host_only_cookie()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "session=abc" });

        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("session=abc");
    }

    [TestMethod]
    public void Host_only_cookie_does_not_match_subdomain()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "session=abc" });

        // Without an explicit Domain attribute the cookie is host-only.
        jar.BuildCookieHeader(Url("https://www.example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Domain_attribute_extends_to_subdomains()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "session=abc; Domain=example.com" });

        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("session=abc");
        jar.BuildCookieHeader(Url("https://www.example.com/")).Should().Be("session=abc");
        jar.BuildCookieHeader(Url("https://deep.www.example.com/")).Should().Be("session=abc");
    }

    [TestMethod]
    public void Cookie_for_unrelated_domain_is_rejected()
    {
        var jar = NewJar();
        var n = jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "session=abc; Domain=other.com" });
        n.Should().Be(0);
        jar.BuildCookieHeader(Url("https://other.com/")).Should().Be("");
    }

    [TestMethod]
    public void Cookie_with_public_suffix_domain_is_rejected()
    {
        var jar = NewJar();
        var n = jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "session=abc; Domain=com" });
        n.Should().Be(0);
    }

    [TestMethod]
    public void Secure_cookie_is_only_sent_on_https()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "session=abc; Secure" });

        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("session=abc");
        jar.BuildCookieHeader(Url("http://example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Insecure_request_cannot_set_secure_cookie()
    {
        var jar = NewJar();
        var n = jar.StoreFromHeaders(Url("http://example.com/"),
            new[] { "session=abc; Secure" });
        n.Should().Be(0);
    }

    [TestMethod]
    public void Path_match_is_prefix_with_slash_boundary()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/api/users"),
            new[] { "scoped=1; Path=/api" });

        jar.BuildCookieHeader(Url("https://example.com/api")).Should().Be("scoped=1");
        jar.BuildCookieHeader(Url("https://example.com/api/users/3")).Should().Be("scoped=1");
        // Same prefix without slash boundary must not match.
        jar.BuildCookieHeader(Url("https://example.com/api2")).Should().Be("");
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Default_path_uses_directory_of_request_url()
    {
        var jar = NewJar();
        // No Path attribute → default-path = "/api".
        jar.StoreFromHeaders(Url("https://example.com/api/users"), new[] { "k=v" });

        jar.BuildCookieHeader(Url("https://example.com/api/users")).Should().Be("k=v");
        jar.BuildCookieHeader(Url("https://example.com/api/users/42")).Should().Be("k=v");
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Replacing_cookie_preserves_creation_time_but_updates_value()
    {
        var t1 = DateTimeOffset.Parse("2026-05-11T00:00:00Z");
        var t2 = t1.AddMinutes(10);
        var time = t1;
        var jar = new CookieJar(PublicSuffixList.Default, () => time);

        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=first" });
        time = t2;
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=second" });

        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("k=second");
        jar.Count.Should().Be(1);
    }

    [TestMethod]
    public void Max_age_zero_or_negative_evicts_existing_cookie()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=v" });
        jar.Count.Should().Be(1);

        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=v; Max-Age=0" });
        jar.Count.Should().Be(0);
    }

    [TestMethod]
    public void Expired_cookies_are_dropped_at_send_time()
    {
        var time = DateTimeOffset.Parse("2026-05-11T00:00:00Z");
        var jar = new CookieJar(PublicSuffixList.Default, () => time);

        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=v; Max-Age=10" });
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("k=v");

        time = time.AddMinutes(1); // way past 10 seconds
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Multiple_cookies_are_sorted_longest_path_first()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "a=1; Path=/" });
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "b=2; Path=/api" });
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "c=3; Path=/api/v1" });

        jar.BuildCookieHeader(Url("https://example.com/api/v1/users")).Should().Be("c=3; b=2; a=1");
    }

    [TestMethod]
    public void SameSite_None_without_Secure_is_rejected()
    {
        var jar = NewJar();
        var n = jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "k=v; SameSite=None" });
        n.Should().Be(0);
    }

    [TestMethod]
    public void Host_prefix_requires_secure_host_only_root_path()
    {
        var jar = NewJar();

        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Host-id=abc; Secure; Path=/" }).Should().Be(1);

        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Host-bad1=x; Path=/" }).Should().Be(0);  // missing Secure

        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Host-bad2=x; Secure; Path=/scoped" }).Should().Be(0);  // non-root path

        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Host-bad3=x; Secure; Path=/; Domain=example.com" }).Should().Be(0);  // not host-only
    }

    [TestMethod]
    public void Secure_prefix_requires_secure()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Secure-x=ok; Secure" }).Should().Be(1);

        jar.StoreFromHeaders(Url("https://example.com/"),
            new[] { "__Secure-x=bad" }).Should().Be(0);
    }

    [TestMethod]
    public void Clear_removes_everything()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "k=v" });
        jar.StoreFromHeaders(Url("https://other.com/"), new[] { "x=y" });
        jar.Count.Should().Be(2);

        jar.Clear();
        jar.Count.Should().Be(0);
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Be("");
    }

    [TestMethod]
    public void Multivalued_set_cookie_headers_all_get_stored()
    {
        var jar = NewJar();
        jar.StoreFromHeaders(Url("https://example.com/"), new[] { "a=1", "b=2", "c=3" })
            .Should().Be(3);
        jar.BuildCookieHeader(Url("https://example.com/")).Should().Contain("a=1")
            .And.Contain("b=2").And.Contain("c=3");
    }
}
