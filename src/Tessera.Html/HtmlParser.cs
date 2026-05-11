using Tessera.Dom;

namespace Tessera.Html;

/// <summary>
/// Stable façade over the active HTML parser implementation.
/// Public callers (Engine, Headless) bind here so they don't need to change in M1.
/// </summary>
public static class HtmlParser
{
    public static Document Parse(string html) => TokenizingHtmlParser.Parse(html);
}
