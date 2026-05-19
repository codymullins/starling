using BenchmarkDotNet.Attributes;
using TUrl = Tessera.Url.Url;
using UrlParser = Tessera.Url.UrlParser;

namespace Tessera.Bench;

// Backstops the M2 `url/` 100% WPT pass — UrlParser.Parse is hot on every
// fetch, redirect, and relative-href resolution. Targets a mix of input
// shapes so a regression in one branch (IDNA, percent-decode, IPv6 brackets)
// shows up without others masking it.
[MemoryDiagnoser]
public class UrlBench
{
    private const string Simple   = "https://example.com/";
    private const string Pathy    = "https://example.com/a/b/c/d?q=1&r=2#frag";
    private const string Idna     = "https://bücher.example/";
    private const string Ipv6     = "https://[2001:db8::1]:8443/path";
    private const string Percent  = "https://example.com/%E4%B8%AD%E6%96%87?k=%E5%80%BC";

    private static readonly TUrl _baseUrl = UrlParser.Parse("https://base.example/dir/sub/").Value;

    [Benchmark] public TUrl Simple_absolute()
        => UrlParser.Parse(Simple).Value;

    [Benchmark] public TUrl Pathy_with_query_and_fragment()
        => UrlParser.Parse(Pathy).Value;

    [Benchmark] public TUrl Idna_unicode_host()
        => UrlParser.Parse(Idna).Value;

    [Benchmark] public TUrl Ipv6_with_port()
        => UrlParser.Parse(Ipv6).Value;

    [Benchmark] public TUrl Percent_encoded_path_and_query()
        => UrlParser.Parse(Percent).Value;

    [Benchmark] public TUrl Relative_against_base()
        => UrlParser.Parse("../other/page.html?x=1", _baseUrl).Value;

    [Benchmark] public TUrl Relative_absolute_path()
        => UrlParser.Parse("/css/style.css", _baseUrl).Value;
}
