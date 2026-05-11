using Tessera.Dom;

namespace Tessera.Html;

/// <summary>
/// Stable façade. M0 implementation delegates to <see cref="MinimalHtmlParser"/>;
/// M1 replaces the body with the full WHATWG state-machine tokenizer + tree builder.
/// Public callers (Engine, Headless) bind here so they don't need to change in M1.
/// </summary>
public static class HtmlParser
{
    public static Document Parse(string html) => MinimalHtmlParser.Parse(html);
}
