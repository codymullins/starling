namespace Tessera.Dom;

public sealed class Text : Node
{
    public Text(string data)
    {
        Data = data;
    }

    public override NodeKind Kind => NodeKind.Text;

    public string Data { get; set; }

    public override string ToString() => $"Text({Data.Length} chars)";
}

public sealed class Comment : Node
{
    public Comment(string data)
    {
        Data = data;
    }

    public override NodeKind Kind => NodeKind.Comment;

    public string Data { get; set; }
}
