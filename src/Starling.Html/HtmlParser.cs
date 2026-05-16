using Tessera.Common.Diagnostics;
using Tessera.Dom;
using Tessera.Html.TreeBuilder;

namespace Tessera.Html;

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
    public static Document Parse(string html, IDiagnostics? diagnostics = null)
        => HtmlTreeBuilder.Parse(html, diagnostics);
}
