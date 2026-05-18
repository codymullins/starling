using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Starling.Gui.Avalonia.Mcp;

public sealed class GuiMcpServer : IAsyncDisposable
{
    private const string DefaultUrl = "http://127.0.0.1:3077/mcp";
    private const int MaxHeaderBytes = 32 * 1024;
    private const int MaxBodyBytes = 1024 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly BrowserTools _tools;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TaskCompletionSource _stopped =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private TcpListener? _listener;
    private Task? _acceptLoop;

    public GuiMcpServer(BrowserControlBridge browser)
    {
        _tools = new BrowserTools(browser);
        Endpoint = ResolveEndpoint();
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

            await WriteJsonResponseAsync(stream, response.Value, serverToken).ConfigureAwait(false);
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

    private async Task<JsonElement?> HandleJsonRpcAsync(byte[] body, CancellationToken ct)
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
            "ping" => JsonRpcResult(id, new { }),
            "tools/list" => JsonRpcResult(id, ToolsList()),
            "tools/call" => JsonRpcResult(id, await CallToolAsync(root, ct).ConfigureAwait(false)),
            _ => JsonRpcError(id, -32601, $"Method not found: {method}"),
        };
    }

    private async Task<object> CallToolAsync(JsonElement request, CancellationToken ct)
    {
        if (!request.TryGetProperty("params", out var @params) ||
            !@params.TryGetProperty("name", out var nameElement))
        {
            return ToolResult(
                BrowserControlResult.Failure(
                    "tools/call requires params.name.",
                    null,
                    null,
                    canGoBack: false,
                    canGoForward: false,
                    isBusy: false));
        }

        var arguments = @params.TryGetProperty("arguments", out var args)
            ? args
            : default;
        var name = nameElement.GetString();
        BrowserControlResult result = name switch
        {
            "browser_navigate" => await _tools.BrowserNavigate(ReadUrlArgument(arguments), ct).ConfigureAwait(false),
            "browser_back" => await _tools.BrowserBack(ct).ConfigureAwait(false),
            "browser_forward" => await _tools.BrowserForward(ct).ConfigureAwait(false),
            "browser_refresh" => await _tools.BrowserRefresh(ct).ConfigureAwait(false),
            _ => BrowserControlResult.Failure(
                $"Unknown browser tool: {name}",
                null,
                null,
                canGoBack: false,
                canGoForward: false,
                isBusy: false),
        };
        return ToolResult(result);
    }

    private static string ReadUrlArgument(JsonElement arguments)
    {
        if (arguments.ValueKind == JsonValueKind.Object &&
            arguments.TryGetProperty("url", out var url) &&
            url.ValueKind == JsonValueKind.String)
        {
            return url.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    private static object InitializeResult(JsonElement request)
    {
        var protocolVersion = "2025-11-25";
        if (request.TryGetProperty("params", out var @params) &&
            @params.TryGetProperty("protocolVersion", out var requested) &&
            requested.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(requested.GetString()))
        {
            protocolVersion = requested.GetString()!;
        }

        return new
        {
            protocolVersion,
            capabilities = new
            {
                tools = new
                {
                    listChanged = false,
                },
            },
            serverInfo = new
            {
                name = "starling-gui-avalonia",
                title = "Starling GUI",
                version = "0.1.0",
            },
        };
    }

    private static object ToolsList() => new
    {
        tools = new object[]
        {
            new
            {
                name = "browser_navigate",
                description = "Navigate the visible Starling browser window to a URL.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        url = new
                        {
                            type = "string",
                            description = "The absolute URL to load, for example https://example.com.",
                        },
                    },
                    required = new[] { "url" },
                },
            },
            new
            {
                name = "browser_back",
                description = "Navigate the visible Starling browser window back in history.",
                inputSchema = EmptyInputSchema(),
            },
            new
            {
                name = "browser_forward",
                description = "Navigate the visible Starling browser window forward in history.",
                inputSchema = EmptyInputSchema(),
            },
            new
            {
                name = "browser_refresh",
                description = "Reload the current page in the visible Starling browser window.",
                inputSchema = EmptyInputSchema(),
            },
        },
    };

    private static object EmptyInputSchema() => new
    {
        type = "object",
        properties = new { },
    };

    private static object ToolResult(BrowserControlResult result)
    {
        var json = JsonSerializer.Serialize(result, JsonOptions);
        var structured = JsonSerializer.Deserialize<JsonElement>(json, JsonOptions);
        return new
        {
            content = new[]
            {
                new
                {
                    type = "text",
                    text = json,
                },
            },
            structuredContent = structured,
            isError = !result.Ok,
        };
    }

    private static JsonElement JsonRpcResult(JsonElement? id, object result)
        => JsonSerializer.SerializeToElement(
            new
            {
                jsonrpc = "2.0",
                id,
                result,
            },
            JsonOptions);

    private static JsonElement JsonRpcError(JsonElement? id, int code, string message)
        => JsonSerializer.SerializeToElement(
            new
            {
                jsonrpc = "2.0",
                id,
                error = new
                {
                    code,
                    message,
                },
            },
            JsonOptions);

    private static JsonElement? TryCloneId(JsonElement root)
        => root.TryGetProperty("id", out var id) ? id.Clone() : null;

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

    private async Task WriteJsonResponseAsync(Stream stream, JsonElement json, CancellationToken ct)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(json, JsonOptions);
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

    private bool IsAllowedHost(string? host)
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

    private static Uri ResolveEndpoint()
    {
        var configured = Environment.GetEnvironmentVariable("TESSERA_MCP_URL");
        if (string.IsNullOrWhiteSpace(configured))
            return new Uri(DefaultUrl);

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttp ||
            !IPAddressIsLoopback(uri.Host) ||
            uri.AbsolutePath is "" or "/")
        {
            throw new InvalidOperationException(
                "TESSERA_MCP_URL must be an absolute loopback HTTP URL with a path, for example http://127.0.0.1:3077/mcp.");
        }

        return uri;
    }

    private static bool IPAddressIsLoopback(string host)
        => host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
           IPAddress.TryParse(host, out var ip) && IPAddress.IsLoopback(ip);

    private sealed record HttpRequest(string Method, string Path, string? Host, byte[] Body);
}
