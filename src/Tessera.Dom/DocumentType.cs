namespace Tessera.Dom;

public sealed class DocumentType : Node
{
    public DocumentType(string name, string publicId = "", string systemId = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        PublicId = publicId;
        SystemId = systemId;
    }

    public override NodeKind Kind => NodeKind.DocumentType;

    public override string NodeName => Name;

    public string Name { get; }

    public string PublicId { get; }

    public string SystemId { get; }
}
