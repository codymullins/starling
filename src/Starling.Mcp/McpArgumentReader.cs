using System.Text.Json;

namespace Starling.Mcp;

/// <summary>
/// JSON-RPC params.arguments shape helpers shared by tool groups. The MCP
/// protocol passes arguments as a free-form JSON object; these helpers read
/// individual fields with sensible default-on-missing behaviour, matching
/// the original BrowserTools dispatch path.
/// </summary>
public static class McpArgumentReader
{
    public static string ReadString(JsonElement arguments, string name)
        => ReadOptionalString(arguments, name) ?? string.Empty;

    public static string? ReadOptionalString(JsonElement arguments, string name)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    public static bool ReadBool(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.True;

    public static double ReadDouble(JsonElement arguments, string name)
        => arguments.ValueKind == JsonValueKind.Object &&
           arguments.TryGetProperty(name, out var value) &&
           value.ValueKind == JsonValueKind.Number &&
           value.TryGetDouble(out var d)
               ? d
               : 0;

    public static long? ReadOptionalLong(JsonElement arguments, string name)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt64(out var l))
        {
            return l;
        }

        return null;
    }

    public static double? ReadOptionalDouble(JsonElement arguments, string name)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetDouble(out var d))
        {
            return d;
        }

        return null;
    }

    public static int ReadIntOr(JsonElement arguments, string name, int fallback)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty(name, out var value) &&
            value.ValueKind == JsonValueKind.Number &&
            value.TryGetInt32(out var i))
        {
            return i;
        }

        return fallback;
    }
}
