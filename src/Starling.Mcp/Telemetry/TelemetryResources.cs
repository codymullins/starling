using Starling.Telemetry;

namespace Starling.Mcp.Telemetry;

/// <summary>
/// MCP resource provider for telemetry snapshots. Exposes three read-only
/// resources — telemetry://traces, telemetry://logs, telemetry://metrics —
/// each returning the full current ring-buffer contents as
/// <c>application/json</c>. Reads share the same payload builders as
/// <see cref="TelemetryTools"/>, with no filters and the buffer-cap limit
/// (2000) applied. Clients that want filters call the tools instead.
/// </summary>
public sealed class TelemetryResources : IMcpResourceProvider
{
    public const string TracesUri = "telemetry://traces";
    public const string LogsUri = "telemetry://logs";
    public const string MetricsUri = "telemetry://metrics";

    private readonly TelemetryTools _tools;

    public TelemetryResources(TelemetryStream telemetry)
    {
        ArgumentNullException.ThrowIfNull(telemetry);
        _tools = new TelemetryTools(telemetry);
    }

    public string GetResourceDescriptorsJson() => ResourceDescriptorsJson;

    public bool HasResource(string uri) => uri switch
    {
        TracesUri or LogsUri or MetricsUri => true,
        _ => false,
    };

    public Task<McpResourceContent> ReadAsync(string uri, CancellationToken ct)
    {
        // Reuse the tool payload builders with empty arguments — that gives
        // the unfiltered, buffer-capped snapshot that MCP resources should
        // expose. The tools layer applies the same default limit clamp
        // (capped at the ring-buffer size).
        var emptyArgs = default(System.Text.Json.JsonElement);
        const int FullBuffer = 2000;
        var payload = uri switch
        {
            TracesUri => _tools.BuildTracesPayload(emptyArgs, FullBuffer),
            LogsUri => _tools.BuildLogsPayload(emptyArgs, FullBuffer),
            MetricsUri => _tools.BuildMetricsPayload(emptyArgs, FullBuffer),
            _ => throw new ArgumentException($"Unknown telemetry resource: {uri}", nameof(uri)),
        };

        return Task.FromResult(new McpResourceContent("application/json", payload.ToJsonString()));
    }

    private const string ResourceDescriptorsJson = """
        [
          {
            "uri": "telemetry://traces",
            "name": "Starling traces",
            "description": "Recent Starling engine spans (activity records) from the in-memory ring buffer. JSON payload matches browser_telemetry_traces with no filters.",
            "mimeType": "application/json"
          },
          {
            "uri": "telemetry://logs",
            "name": "Starling logs",
            "description": "Recent Starling engine log records from the in-memory ring buffer. JSON payload matches browser_telemetry_logs with no filters.",
            "mimeType": "application/json"
          },
          {
            "uri": "telemetry://metrics",
            "name": "Starling metrics",
            "description": "Recent metric measurements from Starling's Meter instruments. JSON payload matches browser_telemetry_metrics with no filters.",
            "mimeType": "application/json"
          }
        ]
        """;
}
