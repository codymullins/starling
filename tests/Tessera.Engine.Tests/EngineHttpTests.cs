using System.Net;
using System.Net.Sockets;
using System.Text;
using FluentAssertions;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using Xunit;

namespace Tessera.Engine.Tests;

public class EngineHttpTests
{
    public static IEnumerable<object[]> SnapshotCases()
    {
        yield return [new HttpSnapshotCase("paragraph", "<body><p>Snapshot one.</p></body>", null, "Snapshot one.")];
        yield return [new HttpSnapshotCase("author style", "<head><style>.box{background-color:#008000;width:90px;height:35px}</style></head><body><div class=box>green</div></body>", new Rgba32(0, 128, 0), "green")];
        yield return [new HttpSnapshotCase("inline background", "<body><div style=\"background-color:#0000ff;width:80px;height:30px\">blue</div></body>", new Rgba32(0, 0, 255), "blue")];
        yield return [new HttpSnapshotCase("heading list", "<body><h1>Docs</h1><ul><li>Install</li><li>Run</li></ul></body>", null, "Docs Install Run")];
        yield return [new HttpSnapshotCase("centered text", "<body><p style=\"text-align:center;width:160px\">centered</p></body>", null, "centered")];
    }

    [Fact]
    public async Task RenderAsync_fetches_html_over_http_and_writes_png()
    {
        var bodyText = """
            <!doctype html>
            <head><style>.net { background-color: #008000; width: 100px; height: 40px; }</style></head>
            <body><div class="net">Hello over HTTP.</div></body>
            """;
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
            using var image = Image.Load<Rgba32>(output);
            CountExact(image, new Rgba32(0, 128, 0)).Should().BeGreaterThan(1_000);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task RenderAsync_follows_relative_redirect_and_renders_final_response()
    {
        using var server = await StubHttpServer.StartAsync(req =>
        {
            if (req.StartsWith("GET /start HTTP/1.1", StringComparison.Ordinal))
            {
                return Encoding.ASCII.GetBytes(
                    "HTTP/1.1 302 Found\r\n" +
                    "Location: /final\r\n" +
                    "Content-Length: 0\r\n" +
                    "Connection: close\r\n\r\n");
            }

            var body = Encoding.UTF8.GetBytes(
                "<head><style>.done{background-color:#008000;width:100px;height:40px}</style></head><body><div class=done>Redirected.</div></body>");
            return BuildResponse(body, "text/html; charset=utf-8");
        });

        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        try
        {
            var engine = new TesseraEngine();
            var result = await engine.RenderAsync(
                $"http://localhost:{server.Port}/start",
                new RenderOptions(new Size(300, 160), 16f),
                output,
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            result.Value.DisplayText.Should().Be("Redirected.");
            using var image = Image.Load<Rgba32>(output);
            CountExact(image, new Rgba32(0, 128, 0)).Should().BeGreaterThan(1_000);
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task RenderAsync_reports_redirect_loop_as_render_error()
    {
        using var server = await StubHttpServer.StartAsync(_ => Encoding.ASCII.GetBytes(
            "HTTP/1.1 302 Found\r\n" +
            "Location: /loop\r\n" +
            "Content-Length: 0\r\n" +
            "Connection: close\r\n\r\n"));

        var engine = new TesseraEngine();
        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        var result = await engine.RenderAsync(
            $"http://localhost:{server.Port}/loop",
            RenderOptions.Default,
            output,
            TestContext.Current.CancellationToken);

        result.IsErr.Should().BeTrue();
        result.Error.Message.Should().Contain("Too many redirects");
        File.Exists(output).Should().BeFalse();
    }

    [Theory]
    [MemberData(nameof(SnapshotCases))]
    public async Task RenderAsync_renders_snapshot_http_fixtures(HttpSnapshotCase snapshot)
    {
        var body = Encoding.UTF8.GetBytes("<!doctype html>" + snapshot.Html);
        using var server = await StubHttpServer.StartAsync(_ => BuildResponse(body, "text/html; charset=utf-8"));

        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        try
        {
            var engine = new TesseraEngine();
            var result = await engine.RenderAsync(
                $"http://localhost:{server.Port}/{snapshot.Name}",
                new RenderOptions(new Size(320, 180), 16f),
                output,
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            result.Value.DisplayText.Should().Be(snapshot.ExpectedText);
            File.Exists(output).Should().BeTrue();
            new FileInfo(output).Length.Should().BeGreaterThan(100);

            if (snapshot.RequiredColor is { } color)
            {
                using var image = Image.Load<Rgba32>(output);
                CountExact(image, color).Should().BeGreaterThan(500);
            }
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task RenderAsync_uses_html_meta_charset_when_http_header_omits_charset()
    {
        var body = Encoding.Latin1.GetBytes("""
            <!doctype html>
            <html><head><meta charset="iso-8859-1"></head><body><p>cafés</p></body></html>
            """);
        using var server = await StubHttpServer.StartAsync(_ => BuildResponse(body, "text/html"));

        var output = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        try
        {
            var engine = new TesseraEngine();
            var result = await engine.RenderAsync(
                $"http://localhost:{server.Port}/latin1",
                new RenderOptions(new Size(320, 180), 16f),
                output,
                TestContext.Current.CancellationToken);

            result.IsOk.Should().BeTrue(result.IsErr ? result.Error.Message : "");
            result.Value.DisplayText.Should().Be("cafés");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    [Fact]
    public async Task BrowserSession_keeps_cookies_across_navigations()
    {
        var sawCookieOnSecondRequest = false;
        using var server = await StubHttpServer.StartAsync(req =>
        {
            if (req.StartsWith("GET /login HTTP/1.1", StringComparison.Ordinal))
            {
                var body = Encoding.UTF8.GetBytes("<body><p>logged in</p></body>");
                var head = Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 OK\r\n" +
                    "Content-Type: text/html; charset=utf-8\r\n" +
                    "Set-Cookie: sid=abc; Path=/\r\n" +
                    $"Content-Length: {body.Length}\r\n" +
                    "Connection: close\r\n\r\n");
                var combined = new byte[head.Length + body.Length];
                Buffer.BlockCopy(head, 0, combined, 0, head.Length);
                Buffer.BlockCopy(body, 0, combined, head.Length, body.Length);
                return combined;
            }

            sawCookieOnSecondRequest = req.Contains("Cookie: sid=abc\r\n", StringComparison.Ordinal);
            return BuildResponse(Encoding.UTF8.GetBytes("<body><p>account</p></body>"), "text/html; charset=utf-8");
        });

        var first = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        var second = Path.Combine(Path.GetTempPath(), $"tessera-{Guid.NewGuid():N}.png");
        try
        {
            using var session = new BrowserSession();
            var login = await session.NavigateAsync(
                $"http://localhost:{server.Port}/login",
                new RenderOptions(new Size(320, 180), 16f),
                first,
                TestContext.Current.CancellationToken);
            login.IsOk.Should().BeTrue(login.IsErr ? login.Error.Message : "");

            var account = await session.NavigateAsync(
                $"http://localhost:{server.Port}/account",
                new RenderOptions(new Size(320, 180), 16f),
                second,
                TestContext.Current.CancellationToken);
            account.IsOk.Should().BeTrue(account.IsErr ? account.Error.Message : "");

            session.Cookies.Count.Should().Be(1);
            sawCookieOnSecondRequest.Should().BeTrue();
            session.History.Entries.Should().HaveCount(2);
        }
        finally
        {
            if (File.Exists(first)) File.Delete(first);
            if (File.Exists(second)) File.Delete(second);
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
    [InlineData("text/html", new byte[] { 0x3C, 0x6D, 0x65, 0x74, 0x61, 0x20, 0x63, 0x68, 0x61, 0x72, 0x73, 0x65, 0x74, 0x3D, 0x69, 0x73, 0x6F, 0x2D, 0x38, 0x38, 0x35, 0x39, 0x2D, 0x31, 0x3E, 0x63, 0x61, 0x66, 0xE9 }, "<meta charset=iso-8859-1>café")]
    [InlineData(null, new byte[] { 0x61, 0x62 }, "ab")]
    // WHATWG encoding-label aliases that map onto BCL Encoding singletons.
    [InlineData("text/html; charset=ANSI_X3.4-1968", new byte[] { 0x68, 0x69 }, "hi")]
    [InlineData("text/html; charset=ISO_8859-1", new byte[] { 0xE9 }, "é")]
    [InlineData("text/html; charset=iso-ir-100", new byte[] { 0xE9 }, "é")]
    [InlineData("text/html; charset=unicode-1-1-utf-8", new byte[] { 0x68, 0x69 }, "hi")]
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

    private static byte[] BuildResponse(byte[] body, string contentType)
    {
        var head = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            $"Content-Type: {contentType}\r\n" +
            $"Content-Length: {body.Length}\r\n" +
            "Connection: close\r\n\r\n");
        var combined = new byte[head.Length + body.Length];
        Buffer.BlockCopy(head, 0, combined, 0, head.Length);
        Buffer.BlockCopy(body, 0, combined, head.Length, body.Length);
        return combined;
    }

    private static int CountExact(Image<Rgba32> image, Rgba32 color)
    {
        var count = 0;
        image.ProcessPixelRows(rows =>
        {
            for (var y = 0; y < rows.Height; y++)
            {
                var row = rows.GetRowSpan(y);
                foreach (var px in row)
                    if (px.Equals(color))
                        count++;
            }
        });
        return count;
    }
}

public sealed record HttpSnapshotCase(
    string Name,
    string Html,
    Rgba32? RequiredColor,
    string ExpectedText)
{
    public override string ToString() => Name;
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
