namespace Starling.Mcp;

/// <summary>
/// One subsystem's contribution to the MCP server's resources/* surface. The
/// server composes any number of providers and dispatches resources/read to
/// whichever provider owns the URI. See <see cref="IMcpToolGroup"/> for the
/// parallel mechanism on the tool side.
/// </summary>
public interface IMcpResourceProvider
{
    /// <summary>
    /// JSON array literal of MCP resource descriptors —
    /// <c>[{"uri": "...", "name": "...", "description": "...", "mimeType": "..."}, ...]</c>.
    /// Returned as a string for the same reason as
    /// <see cref="IMcpToolGroup.GetToolDescriptorsJson"/>.
    /// </summary>
    string GetResourceDescriptorsJson();

    /// <summary>True if this provider owns the URI.</summary>
    bool HasResource(string uri);

    /// <summary>Read the resource at <paramref name="uri"/>. Implementations
    /// return the content body and the MIME type; the server wraps both into
    /// the MCP-spec resources/read contents array.</summary>
    Task<McpResourceContent> ReadAsync(string uri, CancellationToken ct);
}

/// <summary>
/// Resource read result. The server emits a single content entry —
/// <c>{uri, mimeType, text}</c> — per the MCP spec's resources/read shape.
/// </summary>
public sealed record McpResourceContent(string MimeType, string Text);
