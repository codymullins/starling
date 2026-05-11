namespace Tessera.Dom;

public abstract class CharacterData : Node
{
    private string _data;

    protected CharacterData(string data)
    {
        _data = data;
    }

    public string Data
    {
        get => _data;
        set
        {
            var normalized = value ?? string.Empty;
            if (_data == normalized)
                return;

            _data = normalized;
            OnTreeMutated();
        }
    }

    public override string? NodeValue
    {
        get => Data;
        set => Data = value ?? string.Empty;
    }

    public override string TextContent
    {
        get => Data;
        set => Data = value ?? string.Empty;
    }
}

public sealed class Text : CharacterData
{
    public Text(string data) : base(data)
    {
    }

    public override NodeKind Kind => NodeKind.Text;

    public override string NodeName => "#text";

    public override string ToString() => $"Text({Data.Length} chars)";
}

public sealed class Comment : CharacterData
{
    public Comment(string data) : base(data)
    {
    }

    public override NodeKind Kind => NodeKind.Comment;

    public override string NodeName => "#comment";
}

public sealed class CData : CharacterData
{
    public CData(string data) : base(data)
    {
    }

    public override NodeKind Kind => NodeKind.CDataSection;

    public override string NodeName => "#cdata-section";
}

public sealed class ProcessingInstruction : CharacterData
{
    public ProcessingInstruction(string target, string data) : base(data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        Target = target;
    }

    public string Target { get; }

    public override NodeKind Kind => NodeKind.ProcessingInstruction;

    public override string NodeName => Target;
}
