using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Starling.Mcp;

/// <summary>
/// Loopback HTTP/1.1 + JSON-RPC 2.0 host for the MCP protocol. Generalised
/// from the original GUI-only server: composes any mix of tool groups and
/// resource providers, so the same code serves the GUI (browser-control +
/// telemetry tools) and the headless CLI (telemetry only). Connection-per-
/// request transport — push/streaming features (resources/subscribe, server-
/// initiated notifications) are out of scope here.
/// </summary>
public sealed class StarlingMcpServer : IAsyncDisposable
{
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;
    private const string DefaultProtocolVersion = "2025-11-25";

    private readonly IReadOnlyList<IMcpToolGroup> _toolGroups;
    private readonly IReadOnlyList<IMcpResourceProvider> _resourceProviders;
    private readonly string _serverName;
    private readonly string _serverTitle;
    private readonly string _serverVersion;
    private readonly bool _advertiseResources;
    private readonly string _toolsListJson;
    private readonly string _resourcesListJson;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TcpListener? _listener;
    private Task? _acceptLoop;

    public StarlingMcpServer(
        Uri endpoint,
        IEnumerable<IMcpToolGroup> toolGroups,
        IEnumerable<IMcpResourceProvider>? resourceProviders = null,
        string serverName = "starling",
        string serverTitle = "Starling",
        string serverVersion = "0.1.0")
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentNullException.ThrowIfNull(toolGroups);

        _toolGroups = toolGroups.ToArray();
        _resourceProviders = (resourceProviders ?? []).ToArray();
        _serverName = serverName;
        _serverTitle = serverTitle;
        _serverVersion = serverVersion;
        _advertiseResources = _resourceProviders.Count > 0;
        _toolsListJson = BuildListJson("tools", _toolGroups.Select(g => g.GetToolDescriptorsJson()));
        _resourcesListJson = BuildListJson("resources",
            _resourceProviders.Select(p => p.GetResourceDescriptorsJson()));
        Endpoint = endpoint;
        ValidateEndpoint(endpoint);
    }

    public Uri Endpoint { get; }

    public Task StartAsync(CancellationToken ct = default)
    {
        if (_listener is not null) return Task.CompletedTask;

        var ip = Endpoint.Host switch
        {
            "localhost" => IPAddress.Loopback,
            _ when IPAddress.TryParse(Endpoint.Host, out var parsed) => parsed,
            _ => throw new InvalidOperationException("The MCP endpoint must use a loopback host."),
        };
        if (!IPAddress.IsLoopback(ip))
            throw new InvalidOperationException("The MCP endpoint must use a loopback host.");

        var listener = new TcpListener(ip, Endpoint.Port);
        listener.Start();
        _listener = listener;
        _acceptLoop = Task.Run(() => AcceptLoopAsync(listener, _shutdown.Token), CancellationToken.None);
        ct.ThrowIfCancellationRequested();
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);
        _listener?.Stop();
        if (_acceptLoop is not null)
            await _stopped.Task.ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(TcpListener listener, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), CancellationToken.None);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (SocketException) when (ct.IsCancellationRequested)
        {
        }
        finally
        {
            _stopped.TrySetResult();
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken serverToken)
    {
        using var _ = client;
        await using var stream = client.GetStream();
        try
        {
            var request = await ReadRequestAsync(stream, serverToken).ConfigureAwait(false);
            if (request is null) return;

            if (!IsAllowedHost(request.Host) || request.Path != Endpoint.AbsolutePath)
            {
                await WritePlainResponseAsync(stream, 404, "Not Found", serverToken).ConfigureAwait(false);
                return;
            }

            if (!request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await WritePlainResponseAsync(stream, 405, "Method Not Allowed", serverToken).ConfigureAwait(false);
                return;
            }

            var response = await HandleJsonRpcAsync(request.Body, serverToken).ConfigureAwait(false);
            if (response is null)
            {
                await WritePlainResponseAsync(stream, 202, "Accepted", serverToken).ConfigureAwait(false);
                return;
            }

            await WriteJsonResponseAsync(stream, response, serverToken).ConfigureAwait(false);
        }
        catch (JsonException)
        {
            await WriteJsonResponseAsync(stream, JsonRpcError(null, -32700, "Parse error"), serverToken)
                .ConfigureAwait(false);
        }
        catch (InvalidDataException ex)
        {
            await WritePlainResponseAsync(stream, 400, ex.Message, serverToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Starling MCP] request failed: {ex}");
            await WritePlainResponseAsync(stream, 500, "Internal Server Error", serverToken)
                .ConfigureAwait(false);
        }
    }

    private async Task<JsonNode?> HandleJsonRpcAsync(byte[] body, CancellationToken ct)
    {
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        if (!root.TryGetProperty("method", out var methodElement))
            return JsonRpcError(TryCloneId(root), -32600, "Invalid request.");

        var id = TryCloneId(root);
        var method = methodElement.GetString();
        if (id is null && method == "notifications/initialized")
            return null;

        return method switch
        {
            "initialize" => JsonRpcResult(id, InitializeResult(root)),
            "ping" => JsonRpcResult(id, new JsonObject()),
            "tools/list" => JsonRpcResult(id, ParseListJson(_toolsListJson)),
            "tools/call" => JsonRpcResult(id, await CallToolAsync(root, ct).ConfigureAwait(false)),
            "resources/list" when _advertiseResources
                => JsonRpcResult(id, ParseListJson(_resourcesListJson)),
            "resources/read" when _advertiseResources
                => await ReadResourceAsync(id, root, ct).ConfigureAwait(false),
            _ => JsonRpcError(id, -32601, $"Method not found: {method}"),
        };
    }

    private async Task<JsonNode> CallToolAsync(JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var @params) ||
            !@params.TryGetProperty("name", out var nameElement))
        {
            return ToolErrorResult("tools/call requires params.name.");
        }

        var name = nameElement.GetString() ?? string.Empty;
        var arguments = @params.TryGetProperty("arguments", out var args) ? args : default;

        foreach (var group in _toolGroups)
        {
            if (!group.HasTool(name)) continue;
            try
            {
                var result = await group.InvokeAsync(name, arguments, ct).ConfigureAwait(false);
                return ToolResult(result);
            }
            catch (ArgumentException ex)
            {
                return ToolErrorResult(ex.Message);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Starling MCP] tool '{name}' failed: {ex}");
                return ToolErrorResult($"Tool '{name}' failed: {ex.Message}");
            }
        }

        return ToolErrorResult($"Unknown tool: {name}");
    }

    private async Task<JsonNode> ReadResourceAsync(JsonElement? id, JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var @params) ||
            !@params.TryGetProperty("uri", out var uriElement) ||
            uriElement.ValueKind != JsonValueKind.String)
        {
            return JsonRpcError(id, -32602, "resources/read requires params.uri.");
        }

        var uri = uriElement.GetString() ?? string.Empty;
        foreach (var provider in _resourceProviders)
        {
            if (!provider.HasResource(uri)) continue;
            try
            {
                var content = await provider.ReadAsync(uri, ct).ConfigureAwait(false);
                return JsonRpcResult(id, new JsonObject
                {
                    ["contents"] = new JsonArray(
                        new JsonObject
                        {
                            ["uri"] = uri,
                            ["mimeType"] = content.MimeType,
                            ["text"] = content.Text,
                        }),
                });
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[Starling MCP] resource '{uri}' read failed: {ex}");
                return JsonRpcError(id, -32603, $"Resource '{uri}' read failed: {ex.Message}");
            }
        }

        return JsonRpcError(id, -32602, $"Unknown resource: {uri}");
    }

    private static JsonNode ToolResult(McpToolResult result)
    {
        // Stringify once for the text-content channel, then parse a fresh copy
        // for structuredContent — a JsonNode can't be attached to two parents.
        var text = result.StructuredContent.ToJsonString();
        return new JsonObject
        {
            ["content"] = new JsonArray(
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text,
                }),
            ["structuredContent"] = JsonNode.Parse(text),
            ["isError"] = result.IsError,
        };
    }

    private static JsonNode ToolErrorResult(string message) => new JsonObject
    {
        ["content"] = new JsonArray(
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = message,
            }),
        ["isError"] = true,
    };

    private JsonNode InitializeResult(JsonElement request)
    {
        var protocolVersion = DefaultProtocolVersion;
        if (request.TryGetProperty("params", out var @params) &&
            @params.TryGetProperty("protocolVersion", out var requested) &&
            requested.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(requested.GetString()))
        {
            protocolVersion = requested.GetString()!;
        }

        var capabilities = new JsonObject
        {
            ["tools"] = new JsonObject { ["listChanged"] = false },
        };
        if (_advertiseResources)
            capabilities["resources"] = new JsonObject
            {
                ["listChanged"] = false,
                ["subscribe"] = false,
            };

        return new JsonObject
        {
            ["protocolVersion"] = protocolVersion,
            ["capabilities"] = capabilities,
            ["serverInfo"] = new JsonObject
            {
                ["name"] = _serverName,
                ["title"] = _serverTitle,
                ["version"] = _serverVersion,
            },
        };
    }

    // Compose the union of group/provider descriptors into one
    // <c>{"tools": [...]}</c> or <c>{"resources": [...]}</c> string at
    // construction time. The string is parsed fresh per request — JsonNode
    // trees can't be reattached to a new parent, but a string-cache keeps
    // the hot path allocation-light and AOT-safe (no JsonSerializer reflection).
    private static string BuildListJson(string key, IEnumerable<string> arrayLiterals)
    {
        var merged = new JsonArray();
        foreach (var literal in arrayLiterals)
        {
            if (string.IsNullOrWhiteSpace(literal)) continue;
            var parsed = JsonNode.Parse(literal) as JsonArray
                ?? throw new InvalidOperationException(
                    $"Tool/resource group returned a non-array JSON literal: {literal}");
            // Detach each element from its parent array so it can be re-parented.
            // Pre-materialise the entries since assigning into the new array
            // mutates the source's index → don't iterate the source directly.
            var entries = parsed.ToArray();
            foreach (var entry in entries)
            {
                if (entry is null) continue;
                parsed.Remove(entry);
                merged.Add(entry);
            }
        }
        return new JsonObject { [key] = merged }.ToJsonString();
    }

    private static JsonNode ParseListJson(string json) => JsonNode.Parse(json)!;

    private static JsonNode JsonRpcResult(JsonElement? id, JsonNode result) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = IdToNode(id),
        ["result"] = result,
    };

    private static JsonNode JsonRpcError(JsonElement? id, int code, string message) => new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = IdToNode(id),
        ["error"] = new JsonObject
        {
            ["code"] = code,
            ["message"] = message,
        },
    };

    private static JsonElement? TryCloneId(JsonElement root)
        => root.TryGetProperty("id", out var id) ? id.Clone() : null;

    // The request id echoes back verbatim (string, number, or null per JSON-RPC).
    // Round-trip through the raw text so any of those shapes is preserved without
    // reflection; a JSON null id yields a null node → "id": null.
    private static JsonNode? IdToNode(JsonElement? id)
        => id is { } element ? JsonNode.Parse(element.GetRawText()) : null;

    private static void ValidateEndpoint(Uri endpoint)
    {
        if (endpoint.Scheme != Uri.UriSchemeHttp)
            throw new ArgumentException("MCP endpoint scheme must be http.", nameof(endpoint));
        if (!IsLoopbackHost(endpoint.Host))
            throw new ArgumentException("MCP endpoint must be a loopback host.", nameof(endpoint));
        if (string.IsNullOrEmpty(endpoint.AbsolutePath) || endpoint.AbsolutePath == "/")
            throw new ArgumentException("MCP endpoint must include a path, e.g. /mcp.", nameof(endpoint));
    }

    private static bool IsLoopbackHost(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);

    private static async Task<HttpRequest?> ReadRequestAsync(Stream stream, CancellationToken ct)
    {
        var requestLine = await ReadLineAsync(stream, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(requestLine)) return null;

        var parts = requestLine.Split(' ', 3);
        if (parts.Length < 2) throw new InvalidDataException("Malformed request line.");

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headerBytes = requestLine.Length;
        while (true)
        {
            var line = await ReadLineAsync(stream, ct).ConfigureAwait(false);
            if (line is null) throw new InvalidDataException("Unexpected end of headers.");
            headerBytes += line.Length;
            if (headerBytes > MaxHeaderBytes) throw new InvalidDataException("Headers too large.");
            if (line.Length == 0) break;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0) continue;
            headers[line[..colon].Trim()] = line[(colon + 1)..].Trim();
        }

        byte[] body;
        if (headers.TryGetValue("Content-Length", out var contentLengthValue))
        {
            if (!int.TryParse(contentLengthValue, NumberStyles.None, CultureInfo.InvariantCulture, out var contentLength) ||
                contentLength < 0 ||
                contentLength > MaxBodyBytes)
            {
                throw new InvalidDataException("Invalid Content-Length.");
            }

            body = await ReadExactAsync(stream, contentLength, ct).ConfigureAwait(false);
        }
        else if (headers.TryGetValue("Transfer-Encoding", out var transferEncoding) &&
                 transferEncoding.Contains("chunked", StringComparison.OrdinalIgnoreCase))
        {
            body = await ReadChunkedBodyAsync(stream, ct).ConfigureAwait(false);
        }
        else
        {
            throw new InvalidDataException("Request body requires Content-Length or chunked Transfer-Encoding.");
        }

        headers.TryGetValue("Host", out var host);
        return new HttpRequest(parts[0], ExtractPath(parts[1]), host, body);
    }

    private static string ExtractPath(string requestTarget)
    {
        var queryStart = requestTarget.IndexOf('?', StringComparison.Ordinal);
        return queryStart >= 0 ? requestTarget[..queryStart] : requestTarget;
    }

    private static async Task<byte[]> ReadExactAsync(Stream stream, int length, CancellationToken ct)
    {
        var body = new byte[length];
        var read = 0;
        while (read < body.Length)
        {
            var n = await stream.ReadAsync(body.AsMemory(read), ct).ConfigureAwait(false);
            if (n == 0) throw new InvalidDataException("Unexpected end of body.");
            read += n;
        }

        return body;
    }

    private static async Task<byte[]> ReadChunkedBodyAsync(Stream stream, CancellationToken ct)
    {
        using var body = new MemoryStream();
        while (true)
        {
            var sizeLine = await ReadLineAsync(stream, ct).ConfigureAwait(false)
                ?? throw new InvalidDataException("Unexpected end of chunked body.");
            var extensionStart = sizeLine.IndexOf(';', StringComparison.Ordinal);
            var sizeText = extensionStart >= 0 ? sizeLine[..extensionStart] : sizeLine;
            if (!int.TryParse(sizeText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var size) ||
                size < 0)
            {
                throw new InvalidDataException("Invalid chunk size.");
            }

            if (size == 0)
            {
                while (true)
                {
                    var trailer = await ReadLineAsync(stream, ct).ConfigureAwait(false)
                        ?? throw new InvalidDataException("Unexpected end of chunked trailers.");
                    if (trailer.Length == 0) break;
                }

                return body.ToArray();
            }

            if (body.Length + size > MaxBodyBytes)
                throw new InvalidDataException("Request body too large.");

            var chunk = await ReadExactAsync(stream, size, ct).ConfigureAwait(false);
            body.Write(chunk, 0, chunk.Length);
            var terminator = await ReadLineAsync(stream, ct).ConfigureAwait(false);
            if (terminator is null or { Length: > 0 })
                throw new InvalidDataException("Invalid chunk terminator.");
        }
    }

    private static async Task<string?> ReadLineAsync(Stream stream, CancellationToken ct)
    {
        var bytes = new List<byte>(128);
        while (true)
        {
            var buffer = new byte[1];
            var n = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);
            if (n == 0) return bytes.Count == 0 ? null : Encoding.ASCII.GetString(bytes.ToArray());
            if (buffer[0] == (byte)'\n') break;
            if (buffer[0] != (byte)'\r') bytes.Add(buffer[0]);
            if (bytes.Count > MaxHeaderBytes) throw new InvalidDataException("Header line too long.");
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static async Task WriteJsonResponseAsync(Stream stream, JsonNode json, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetBytes(json.ToJsonString());
        await WriteResponseAsync(stream, 200, "OK", "application/json", body, ct).ConfigureAwait(false);
    }

    private static Task WritePlainResponseAsync(Stream stream, int code, string reason, CancellationToken ct)
        => WriteResponseAsync(stream, code, reason, "text/plain; charset=utf-8", Encoding.UTF8.GetBytes(reason), ct);

    private static async Task WriteResponseAsync(
        Stream stream,
        int code,
        string reason,
        string contentType,
        byte[] body,
        CancellationToken ct)
    {
        var header = Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {code} {reason}\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n" +
            "\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        if (body.Length > 0)
            await stream.WriteAsync(body, ct).ConfigureAwait(false);
    }

    private static bool IsAllowedHost(string? host)
    {
        if (string.IsNullOrWhiteSpace(host)) return false;
        var withoutPort = host;
        var portSeparator = host.LastIndexOf(':');
        if (portSeparator > -1 && !host.StartsWith("[", StringComparison.Ordinal))
            withoutPort = host[..portSeparator];
        if (withoutPort.StartsWith("[", StringComparison.Ordinal) &&
            withoutPort.EndsWith("]", StringComparison.Ordinal))
        {
            withoutPort = withoutPort[1..^1];
        }

        return withoutPort.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
               IPAddress.TryParse(withoutPort, out var ip) && IPAddress.IsLoopback(ip);
    }

    private sealed record HttpRequest(string Method, string Path, string? Host, byte[] Body);
}
