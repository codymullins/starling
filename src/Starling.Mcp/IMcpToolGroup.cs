using System.Text.Json;
using System.Text.Json.Nodes;

namespace Starling.Mcp;

/// <summary>
/// One subsystem's contribution to the MCP server's tools/* surface. The
/// server composes any number of groups and dispatches tools/call to whichever
/// group owns the requested tool name. Implementations stay AOT-safe by
/// returning their tool descriptors as a JSON array literal (string) which
/// the server re-parses on demand — JsonNode instances cannot be re-parented,
/// so a single cached tree could not be reused across requests.
/// </summary>
public interface IMcpToolGroup
{
    /// <summary>
    /// JSON array literal of MCP tool descriptors —
    /// <c>[{"name": "...", "description": "...", "inputSchema": {...}}, ...]</c>.
    /// Must be valid JSON or the server's startup will throw. Returned as
    /// a string (not JsonNode) so the server can splice multiple groups into
    /// one tools/list array and re-parse a fresh tree per request.
    /// </summary>
    string GetToolDescriptorsJson();

    /// <summary>True if this group owns the named tool. The server consults
    /// each group in registration order until one claims the name.</summary>
    bool HasTool(string name);

    /// <summary>Run the named tool with the JSON-RPC params.arguments value
    /// (may be <c>default</c> when the call omitted arguments). Implementations
    /// must validate argument shape and throw <see cref="ArgumentException"/>
    /// for bad input; the server reports those as MCP tool errors.</summary>
    Task<McpToolResult> InvokeAsync(string name, JsonElement arguments, CancellationToken ct);
}

/// <summary>
/// Tool invocation result. The server wraps this into the MCP-spec response
/// shape (<c>{content: [{type: "text", text: ...}], structuredContent, isError}</c>),
/// stringifying <see cref="StructuredContent"/> for the text channel.
/// </summary>
public sealed record McpToolResult(JsonNode StructuredContent, bool IsError = false);
