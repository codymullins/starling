namespace Tessera.Dom;

/// <summary>
/// DOM node type discriminator. Matches the integer codes from the
/// <a href="https://dom.spec.whatwg.org/#dom-node-nodetype">DOM spec</a>
/// for source-grep compatibility, even though we use an enum instead of int.
/// </summary>
public enum NodeKind
{
    Element = 1,
    Attribute = 2,
    Text = 3,
    CDataSection = 4,
    ProcessingInstruction = 7,
    Comment = 8,
    Document = 9,
    DocumentType = 10,
    DocumentFragment = 11,
}
