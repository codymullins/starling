using System.Text;
using FluentAssertions;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>B5-3 XHR tests. Shares <see cref="LocalServer"/> fixture with
/// FetchTests.</summary>
[TestClass]
public sealed class XhrTests
{
    [TestMethod]
    public async Task Xhr_get_round_trip_via_onreadystatechange()
    {
        await using var server = await LocalServer.Start(ctx =>
        {
            ctx.Response.StatusCode = 200;
            using var w = new StreamWriter(ctx.Response.OutputStream, new UTF8Encoding(false));
            w.Write("hello");
        });
        var env = FetchTests.NewEnv(server.BaseUrl);
        FetchTests.Eval(env.Runtime, $@"
            globalThis.body = null;
            var x = new XMLHttpRequest();
            x.open('GET', '{server.BaseUrl}/foo');
            x.onreadystatechange = function() {{ if (x.readyState === 4) globalThis.body = x.responseText; }};
            x.send();
        ");
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("body").IsString);
        env.Runtime.GetGlobal("body").AsString.Should().Be("hello");
    }

    [TestMethod]
    public async Task Xhr_exposes_status_and_headers()
    {
        await using var server = await LocalServer.Start(ctx =>
        {
            ctx.Response.StatusCode = 201;
            ctx.Response.StatusDescription = "Created";
            ctx.Response.Headers["X-Token"] = "abc";
        });
        var env = FetchTests.NewEnv(server.BaseUrl);
        FetchTests.Eval(env.Runtime, $@"
            globalThis.status = null; globalThis.token = null; globalThis.all = null;
            var x = new XMLHttpRequest();
            x.open('GET', '{server.BaseUrl}/created');
            x.onload = function() {{
                globalThis.status = x.status;
                globalThis.token = x.getResponseHeader('x-token');
                globalThis.all = x.getAllResponseHeaders();
            }};
            x.send();
        ");
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("status").IsNumber);
        env.Runtime.GetGlobal("status").AsNumber.Should().Be(201);
        env.Runtime.GetGlobal("token").AsString.Should().Be("abc");
        env.Runtime.GetGlobal("all").AsString.Should().Contain("X-Token: abc");
    }

    [TestMethod]
    public async Task Xhr_abort_fires_abort_event_and_sets_readyState_done()
    {
        var hold = new TaskCompletionSource();
        await using var server = await LocalServer.Start(async ctx =>
        {
            await hold.Task.ConfigureAwait(false);
            ctx.Response.StatusCode = 200;
        });
        var env = FetchTests.NewEnv(server.BaseUrl);
        FetchTests.Eval(env.Runtime, $@"
            globalThis.aborted = false; globalThis.finalState = null;
            var x = new XMLHttpRequest();
            x.open('GET', '{server.BaseUrl}/slow');
            x.onabort = function() {{ globalThis.aborted = true; globalThis.finalState = x.readyState; }};
            x.send();
            x.abort();
        ");
        await FetchTests.PumpUntil(env.Runtime, () => env.Runtime.GetGlobal("aborted").AsBool);
        hold.TrySetResult();
        env.Runtime.GetGlobal("aborted").AsBool.Should().BeTrue();
        env.Runtime.GetGlobal("finalState").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Xhr_constants_present_on_constructor_and_instance()
    {
        var env = FetchTests.NewEnv("http://127.0.0.1/");
        FetchTests.Eval(env.Runtime, @"
            globalThis.cd = XMLHttpRequest.DONE;
            globalThis.id = (new XMLHttpRequest()).DONE;
        ");
        env.Runtime.GetGlobal("cd").AsNumber.Should().Be(4);
        env.Runtime.GetGlobal("id").AsNumber.Should().Be(4);
    }

    [TestMethod]
    public void Xhr_sync_open_throws()
    {
        var env = FetchTests.NewEnv("http://127.0.0.1/");
        // try/catch isn't compiled yet (wp:M3-03) — assert at the host level.
        var ex = Assert.ThrowsExactly<JsThrow>(() => FetchTests.Eval(env.Runtime, @"
            var x = new XMLHttpRequest();
            x.open('GET', '/foo', false);
        "));
        var msg = JsValue.ToStringValue(ex.Value.AsObject.Get("message"));
        msg.Should().Contain("Synchronous");
    }
}
