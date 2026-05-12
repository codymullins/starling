using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using SixLabors.ImageSharp;
using Xunit;

namespace Tessera.Engine.Tests;

public class EngineHttpTests
{
    [Fact]
    public async Task RenderAsync_fetches_html_over_http_and_writes_png()
    {
        var bodyText = "<!doctype html><body><p>Hello over HTTP.</p></body></html>";
        var bodyBytes = Encoding.UTF8.GetBytes(bodyText);
        using var server = await StubHttpServer.StartAsync(_ => Encoding.UTF8.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n" +
            bodyText));

        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        try
        {
            var engine = new TesseraEngine();
            var result = await engine.RenderAsync(
                $"http://localhost:{server.Port}/",
                new RenderOptions(new Size(400, 200), 28f),
                output,
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue($"render failed: {(result.IsErr ? result.Error.Message : "")}");
            File.Exists(output).Should().BeTrue();
            new FileInfo(output).Length.Should().BeGreaterThan(100);
            result.Value.DisplayText.Should().Be("Hello over HTTP.");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task RenderAsync_returns_render_error_on_http_failure_status()
    {
        using var server = await StubHttpServer.StartAsync(_ => Encoding.ASCII.GetBytes(
            "HTTP/1.1 404 Not Found\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n"));

        var engine = new TesseraEngine();
        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        var result = await engine.RenderAsync(
            $"http://localhost:{server.Port}/missing",
            RenderOptions.Default,
            output,
            TestContext.Current.CancellationToken);

        result.IsErr.Should().BeTrue();
        result.Error.Message.Should().Contain("404");
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [InlineData("text/html; charset=utf-8", new byte[] { 0x68, 0x69 }, "hi")]
    [InlineData("text/html", new byte[] { 0xEF, 0xBB, 0xBF, 0x68, 0x69 }, "hi")]
    [InlineData("text/html; charset=\"utf-8\"", new byte[] { 0x68 }, "h")]
    [InlineData(null, new byte[] { 0x61, 0x62 }, "ab")]
    public void ResolveEncoding_handles_common_inputs(string? contentType, byte[] body, string expectedDecoded)
    {
        var enc = TesseraEngine.ResolveEncoding(contentType, body);
        enc.GetString(body).TrimStart((char)0xFEFF).Should().Be(expectedDecoded);
    }

    [Fact]
    public void ResolveEncoding_falls_back_to_utf8_for_unknown_charset()
    {
        var enc = TesseraEngine.ResolveEncoding("text/html; charset=totally-fake", new byte[] { 0x61 });
        enc.WebName.Should().Be("utf-8");
    }
}

/// <summary>
/// One-shot stub HTTP server used by engine integration tests. Serves a
/// single response, then closes.
/// </summary>
internal sealed class StubHttpServer : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _accept;

    public int Port { get; }

    private StubHttpServer(TcpListener listener, Func<string, byte[]> handler)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _accept = Task.Run(() => AcceptLoop(handler));
    }

    public static Task<StubHttpServer> StartAsync(Func<string, byte[]> handler)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return Task.FromResult(new StubHttpServer(listener, handler));
    }

    private async Task AcceptLoop(Func<string, byte[]> handler)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                using var client = await _listener.AcceptTcpClientAsync(_cts.Token);
                using var stream = client.GetStream();

                var buffer = new byte[8192];
                var pos = 0;
                while (pos < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer.AsMemory(pos), _cts.Token);
                    if (n == 0) break;
                    pos += n;
                    if (ContainsCrLfCrLf(buffer.AsSpan(0, pos))) break;
                }

                var req = Encoding.ASCII.GetString(buffer, 0, pos);
                var response = handler(req);
                await stream.WriteAsync(response, _cts.Token);
                await stream.FlushAsync(_cts.Token);
            }
        }
        catch (OperationCanceledException) { /* shutting down */ }
        catch (ObjectDisposedException) { /* listener closed */ }
        catch (IOException) { /* peer disconnected */ }
    }

    private static bool ContainsCrLfCrLf(ReadOnlySpan<byte> data)
    {
        for (var i = 0; i + 3 < data.Length; i++)
        {
            if (data[i] == 0x0D && data[i + 1] == 0x0A && data[i + 2] == 0x0D && data[i + 3] == 0x0A)
                return true;
        }
        return false;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        try { _accept.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
