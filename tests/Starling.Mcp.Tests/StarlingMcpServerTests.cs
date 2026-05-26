using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Starling.Mcp.Telemetry;
using Starling.Telemetry;

namespace Starling.Mcp.Tests;

[TestClass]
public class StarlingMcpServerTests
{
    [TestMethod]
    public async Task Initialize_advertises_resources_capability_when_provider_present()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
        });

        response["result"]!["capabilities"]!["resources"].Should().NotBeNull();
        response["result"]!["capabilities"]!["tools"].Should().NotBeNull();
        response["result"]!["serverInfo"]!["name"]!.GetValue<string>().Should().Be("starling-test");
    }

    [TestMethod]
    public async Task Tools_list_includes_all_telemetry_tools()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("tools/list", null);

        var names = response["result"]!["tools"]!.AsArray()
            .Select(n => n!["name"]!.GetValue<string>())
            .ToArray();

        names.Should().BeEquivalentTo(
            "browser_telemetry_traces",
            "browser_telemetry_logs",
            "browser_telemetry_metrics",
            "browser_telemetry_describe");
    }

    [TestMethod]
    public async Task Tools_call_returns_structured_content()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("tools/call", new JsonObject
        {
            ["name"] = "browser_telemetry_describe",
            ["arguments"] = new JsonObject(),
        });

        var result = response["result"]!;
        result["isError"]!.GetValue<bool>().Should().BeFalse();
        result["structuredContent"]!["bufferCapacity"]!.GetValue<int>().Should().Be(2000);
        // Text content mirrors the structured payload.
        var textPayload = result["content"]!.AsArray()[0]!["text"]!.GetValue<string>();
        textPayload.Should().Contain("bufferCapacity");
    }

    [TestMethod]
    public async Task Tools_call_unknown_tool_returns_isError()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("tools/call", new JsonObject
        {
            ["name"] = "no_such_tool",
            ["arguments"] = new JsonObject(),
        });

        response["result"]!["isError"]!.GetValue<bool>().Should().BeTrue();
    }

    [TestMethod]
    public async Task Resources_list_returns_three_telemetry_resources()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("resources/list", null);

        var uris = response["result"]!["resources"]!.AsArray()
            .Select(n => n!["uri"]!.GetValue<string>())
            .ToArray();

        uris.Should().BeEquivalentTo(
            "telemetry://traces",
            "telemetry://logs",
            "telemetry://metrics");
    }

    [TestMethod]
    public async Task Resources_read_telemetry_traces_returns_application_json()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("resources/read", new JsonObject
        {
            ["uri"] = "telemetry://traces",
        });

        var entry = response["result"]!["contents"]!.AsArray()[0]!;
        entry["uri"]!.GetValue<string>().Should().Be("telemetry://traces");
        entry["mimeType"]!.GetValue<string>().Should().Be("application/json");
        var body = JsonNode.Parse(entry["text"]!.GetValue<string>())!.AsObject();
        body["traces"].Should().NotBeNull();
        body["count"].Should().NotBeNull();
    }

    [TestMethod]
    public async Task Resources_read_unknown_uri_returns_json_rpc_error()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("resources/read", new JsonObject
        {
            ["uri"] = "telemetry://does-not-exist",
        });

        response["error"]!["code"]!.GetValue<int>().Should().Be(-32602);
    }

    [TestMethod]
    public async Task Unknown_method_returns_method_not_found()
    {
        await using var fixture = await TestServerFixture.StartAsync();
        var response = await fixture.PostJsonRpcAsync("nonsense/method", null);
        response["error"]!["code"]!.GetValue<int>().Should().Be(-32601);
    }

    private sealed class TestServerFixture : IAsyncDisposable
    {
        public StarlingMcpServer Server { get; }
        public TelemetryStream Telemetry { get; }
        public HttpClient Http { get; }
        public Uri Endpoint { get; }

        private TestServerFixture(StarlingMcpServer server, TelemetryStream telemetry, HttpClient http, Uri endpoint)
        {
            Server = server;
            Telemetry = telemetry;
            Http = http;
            Endpoint = endpoint;
        }

        public static async Task<TestServerFixture> StartAsync()
        {
            var port = ReserveLoopbackPort();
            var endpoint = new Uri($"http://127.0.0.1:{port}/mcp");
            var logs = new InMemoryLogSink();
            var activities = new InMemoryActivitySink("Starling.Mcp.Tests.NoSource");
            var metrics = new InMemoryMeterSink("Starling.Mcp.Tests.NoMeter");
            var telemetry = new TelemetryStream(logs, activities, metrics);
            var server = new StarlingMcpServer(
                endpoint: endpoint,
                toolGroups: [new TelemetryTools(telemetry)],
                resourceProviders: [new TelemetryResources(telemetry)],
                serverName: "starling-test",
                serverTitle: "Starling Test Server");
            await server.StartAsync();
            var http = new HttpClient { BaseAddress = endpoint };
            return new TestServerFixture(server, telemetry, http, endpoint);
        }

        public async Task<JsonObject> PostJsonRpcAsync(string method, JsonNode? @params)
        {
            var request = new JsonObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = 1,
                ["method"] = method,
            };
            if (@params is not null)
                request["params"] = @params;
            using var response = await Http.PostAsJsonAsync(Endpoint.AbsolutePath, request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();
            return JsonNode.Parse(body)!.AsObject();
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            await Server.DisposeAsync();
            Telemetry.Dispose();
        }

        // Bind a TCP port, free it immediately, and reuse the number. The
        // window between Free + Server.StartAsync is small enough that the
        // kernel hasn't normally reassigned it; if a CI flake appears here
        // we'd add a retry loop.
        private static int ReserveLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
