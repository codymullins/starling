using FluentAssertions;
namespace Starling.Gui.Tests;

/// <summary>
/// Regression coverage for in-page link activation. The earlier
/// implementation handed the href to <c>System.Uri.TryCreate</c> with
/// <c>UriKind.Absolute</c>; on Unix that misclassified path-only hrefs like
/// <c>/products/power-transmission/</c> as Unix file paths, returning a
/// <c>file://</c> URL and bypassing the page's base. These tests pin the
/// WHATWG resolution path so that regression cannot return.
/// </summary>
[TestClass]
public sealed class LinkResolverTests
{
    [TestMethod]
    public void Absolute_path_href_resolves_against_https_base()
    {
        // The exact failure mode the user reported: clicking
        // /products/power-transmission/ on the McMaster site navigated to
        // file:///products/power-transmission/ instead of staying on the host.
        var resolved = LinkResolver.Resolve(
            "/products/power-transmission/",
            "https://www.mcmaster.com/");

        resolved.Should().Be("https://www.mcmaster.com/products/power-transmission/");
    }

    [TestMethod]
    [DataRow("/products/x", "https://www.mcmaster.com/")]
    [DataRow("/a/b", "https://example.com/")]
    [DataRow("/", "https://example.com/page")]
    public void Absolute_path_hrefs_never_resolve_to_file_scheme(string href, string baseUrl)
    {
        var resolved = LinkResolver.Resolve(href, baseUrl);

        resolved.Should().NotBeNull();
        resolved.Should().NotStartWith("file:");
    }

    [TestMethod]
    public void Absolute_path_href_replaces_base_path()
    {
        // The base path /old/page must be discarded when the href is
        // path-absolute. The host has to come from the base; the new path
        // has to come from the href. (The base's query is preserved by the
        // current WHATWG parser; that's tangential to this regression so the
        // assertion targets the path and host only.)
        var resolved = LinkResolver.Resolve(
            "/foo/bar",
            "https://example.com/old/page");

        resolved.Should().StartWith("https://example.com/foo/bar");
        resolved.Should().NotContain("/old/");
    }

    [TestMethod]
    public void Absolute_https_href_is_passed_through()
    {
        var resolved = LinkResolver.Resolve(
            "https://other.example/x",
            "https://example.com/page");

        resolved.Should().Be("https://other.example/x");
    }

    [TestMethod]
    public void Dot_relative_href_resolves_against_directory_base()
    {
        var resolved = LinkResolver.Resolve(
            "./foo/bar",
            "https://example.com/a/b/");

        resolved.Should().Be("https://example.com/a/b/foo/bar");
    }

    [TestMethod]
    public void Parent_relative_href_pops_a_segment()
    {
        var resolved = LinkResolver.Resolve(
            "../sibling/page",
            "https://example.com/a/b/c");

        resolved.Should().Be("https://example.com/a/sibling/page");
    }

    [TestMethod]
    public void Query_only_href_replaces_base_query()
    {
        var resolved = LinkResolver.Resolve(
            "?page=2",
            "https://example.com/search?page=1");

        resolved.Should().Be("https://example.com/search?page=2");
    }

    [TestMethod]
    public void Absolute_href_resolves_without_a_base()
    {
        var resolved = LinkResolver.Resolve("https://example.com/x", baseUrl: null);

        resolved.Should().Be("https://example.com/x");
    }

    [TestMethod]
    public void Relative_href_without_base_returns_null()
    {
        var resolved = LinkResolver.Resolve("/foo", baseUrl: null);

        resolved.Should().BeNull();
    }

    [TestMethod]
    public void Empty_base_string_is_treated_as_missing_base()
    {
        var resolved = LinkResolver.Resolve("https://example.com/x", baseUrl: "");

        resolved.Should().Be("https://example.com/x");
    }
}
