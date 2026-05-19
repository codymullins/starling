namespace Tessera.Bench;

internal static class Fixtures
{
    private static readonly Lazy<string> _repoRoot = new(LocateRepoRoot);

    public static string RepoRoot => _repoRoot.Value;

    public static string NginxHtmlPath => Path.Combine(RepoRoot, "testdata", "snapshots", "nginx.org", "index.html");
    public static string NginxCssPath  => Path.Combine(RepoRoot, "testdata", "snapshots", "nginx.org", "css", "style_en.css");

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Starling.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Starling.sln walking up from the bench binary.");
        return dir.FullName;
    }

    public const string TinyHtml = "<!doctype html><html><body><p>Hello, world.</p></body></html>";

    public const string SmallHtml = """
        <!doctype html>
        <html>
          <head><title>small</title></head>
          <body>
            <h1>Heading</h1>
            <p>One paragraph with <em>inline</em> markup and a <a href="/x">link</a>.</p>
            <ul><li>a</li><li>b</li><li>c</li></ul>
          </body>
        </html>
        """;

    public const string TinyCss = "body { color: #333; font-size: 16px; margin: 0; }";

    public const string SmallCss = """
        body { margin: 0; padding: 0; color: #222; font-family: system-ui, sans-serif; }
        h1, h2, h3 { color: #111; }
        h1 { font-size: 2em; }
        a { color: rgb(0, 102, 204); text-decoration: underline; }
        ul { padding-left: 1.5em; }
        li + li { margin-top: 0.25em; }
        p > em { font-style: italic; }
        .container { max-width: 960px; margin: 0 auto; padding: 1em; }
        """;
}
