namespace Tessera.Html.TreeBuilder;

/// <summary>
/// WHATWG HTML §13.2.4.1 insertion modes that this tree builder implements.
/// Frameset and complex table sub-modes are folded into <see cref="InBody"/>
/// in v1 — see HtmlTreeBuilder remarks for the simplification list.
/// </summary>
internal enum InsertionMode : byte
{
    Initial,
    BeforeHtml,
    BeforeHead,
    InHead,
    AfterHead,
    InBody,
    Text,
    AfterBody,
    AfterAfterBody,
}
