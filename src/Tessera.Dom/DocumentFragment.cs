namespace Tessera.Dom;

public sealed class DocumentFragment : Node
{
    public override NodeKind Kind => NodeKind.DocumentFragment;

    public override string NodeName => "#document-fragment";
}
