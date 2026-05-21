using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html.TreeBuilder;

namespace Starling.Html;

/// <summary>
/// Stable façade over the active HTML parser implementation.
/// Public callers (Engine, Headless) bind here so they don't need to change in M1.
/// </summary>
/// <remarks>
/// The active parser is <see cref="HtmlTreeBuilder"/> — the spec-driven tree
/// construction stage (wp:M1-02). The earlier <c>TokenizingHtmlParser</c> is
/// retained on disk as the reference for the simplest token-to-DOM mapping;
/// it remains useful for diffing while we close the remaining edge-case gaps.
/// </remarks>
public static class HtmlParser
{
    /// <summary>Parses <paramref name="html"/> into a <see cref="Document"/>.</summary>
    /// <param name="html">The HTML source to parse.</param>
    /// <param name="diagnostics">Optional diagnostics sink.</param>
    /// <param name="scriptingEnabled">
    /// WHATWG HTML §13.2 scripting flag. The engine (which runs JS) passes
    /// <c>true</c> so <c>&lt;noscript&gt;</c> contents become inert raw text;
    /// the html5lib conformance harness leaves it <c>false</c>.
    /// </param>
    public static Document Parse(string html, IDiagnostics? diagnostics = null,
        bool scriptingEnabled = false)
        => HtmlTreeBuilder.Parse(html, diagnostics, scriptingEnabled);
}
