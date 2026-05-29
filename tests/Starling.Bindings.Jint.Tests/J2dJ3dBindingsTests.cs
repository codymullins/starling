using AwesomeAssertions;
using Jint;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// J2d + J3d — Window/Storage/History/Performance + Observers/Crypto/Cookie
/// bindings on the Jint backend. Each test installs the full
/// <see cref="JintBindings.InstallAll(JintBackendContext)"/> pipeline against a
/// freshly-parsed document and exercises the binding from JS.
/// </summary>
[TestClass]
public sealed class J2dJ3dBindingsTests
{
    [TestMethod]
    public void Window_self_globalThis_and_document_are_wired()
    {
        var ctx = Setup();
        ctx.Engine.Evaluate("window === globalThis").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("self === window").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("typeof document").AsString().Should().Be("object");
        ctx.Engine.Evaluate("typeof navigator.userAgent").AsString().Should().Be("string");
        ctx.Engine.Evaluate("typeof location.href").AsString().Should().Be("string");
    }

    [TestMethod]
    public void Storage_setItem_getItem_length_and_removeItem_round_trip()
    {
        var ctx = Setup();
        StorageBinding.ResetForTests();
        ctx.Engine.Execute("""
            localStorage.setItem('k', 'v');
            globalThis.r1 = localStorage.getItem('k');
            globalThis.r2 = localStorage.length;
            localStorage.removeItem('k');
            globalThis.r3 = localStorage.getItem('k');
        """);
        ctx.Engine.Evaluate("r1").AsString().Should().Be("v");
        ctx.Engine.Evaluate("r2").AsNumber().Should().Be(1);
        ctx.Engine.Evaluate("r3").IsNull().Should().BeTrue();
    }

    [TestMethod]
    public void History_pushState_advances_length_and_back_fires_popstate()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.pops = 0;
            window.addEventListener('popstate', () => { pops++; });
            history.pushState({a:1}, '', '/one');
            history.pushState({a:2}, '', '/two');
            globalThis.len = history.length;
            history.back();
        """);
        ctx.Engine.Evaluate("len").AsNumber().Should().Be(3);
        ctx.Engine.Evaluate("pops").AsNumber().Should().Be(1);
    }

    [TestMethod]
    public void History_popstate_event_exposes_state()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.captured = null;
            window.addEventListener('popstate', e => { captured = e.state; });
            history.pushState({ tag: 'second' }, '', '/one');
            history.back();
            history.forward();
        """);
        ctx.Engine.Evaluate("captured.tag").AsString().Should().Be("second");
    }

    [TestMethod]
    public void Location_hash_assignment_updates_history()
    {
        var ctx = Setup(baseUrl: "https://example.test/page");
        ctx.Engine.Execute("""
            globalThis.hashes = 0;
            window.addEventListener('hashchange', () => { hashes++; });
            location.hash = 'section';
            globalThis.result = location.href + '|' + history.length + '|' + hashes;
        """);
        ctx.Engine.Evaluate("result").AsString().Should().Be("https://example.test/page#section|2|1");
    }

    [TestMethod]
    public void Performance_now_and_timeOrigin_return_numbers()
    {
        var ctx = Setup();
        ctx.Engine.Evaluate("typeof performance.now()").AsString().Should().Be("number");
        ctx.Engine.Evaluate("typeof performance.timeOrigin").AsString().Should().Be("number");
        ctx.Engine.Evaluate("performance.now() >= 0").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Crypto_randomUUID_returns_v4_uuid_string()
    {
        var ctx = Setup();
        var uuid = ctx.Engine.Evaluate("crypto.randomUUID()").AsString();
        uuid.Length.Should().Be(36);
        uuid[14].Should().Be('4'); // version nibble
        "89ab".Should().Contain(uuid[19].ToString()); // variant nibble (10xx)
    }

    [TestMethod]
    public void Crypto_getRandomValues_fills_uint8array_in_place()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.a = new Uint8Array(16);
            globalThis.r = crypto.getRandomValues(a);
            globalThis.same = (r === a);
            // odds of all zeros are ~1/2^128; treat any non-zero element as success.
            globalThis.anyNonZero = false;
            for (let i = 0; i < a.length; i++) if (a[i] !== 0) { anyNonZero = true; break; }
        """);
        ctx.Engine.Evaluate("same").AsBoolean().Should().BeTrue();
        ctx.Engine.Evaluate("anyNonZero").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Crypto_getRandomValues_rejects_float_typed_arrays()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.threw = false;
            try { crypto.getRandomValues(new Float32Array(4)); } catch (e) { threw = true; }
        """);
        ctx.Engine.Evaluate("threw").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Observers_construct_with_callback_and_expose_methods()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.m = new MutationObserver(() => {});
            globalThis.i = new IntersectionObserver(() => {});
            globalThis.r = new ResizeObserver(() => {});
            globalThis.types = [typeof m.observe, typeof m.disconnect,
                                typeof m.takeRecords, typeof r.observe];
        """);
        var types = ctx.Engine.Evaluate("types.join(',')").AsString();
        types.Should().Be("function,function,function,function");
        ctx.Engine.Evaluate("m.takeRecords().length").AsNumber().Should().Be(0);
    }

    [TestMethod]
    public void Observers_constructor_throws_when_callback_missing()
    {
        var ctx = Setup();
        ctx.Engine.Execute("""
            globalThis.threw = false;
            try { new MutationObserver(); } catch (e) { threw = true; }
        """);
        ctx.Engine.Evaluate("threw").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Document_cookie_accessor_is_present_and_returns_empty_string()
    {
        var ctx = Setup();
        // document.cookie getter/setter are installed on Document.prototype.
        ctx.Engine.Evaluate("typeof document.cookie").AsString().Should().Be("string");
        ctx.Engine.Evaluate("document.cookie").AsString().Should().Be("");
        // setter accepts strings without throwing
        ctx.Engine.Execute("document.cookie = 'a=b'");
        ctx.Engine.Evaluate("document.cookie").AsString().Should().Be("");
    }

    // ---- shared setup -------------------------------------------------------

    private static JintBackendContext Setup(string bodyHtml = "<div></div>", string baseUrl = "about:blank")
    {
        var doc = HtmlParser.Parse($"<!doctype html><html><body>{bodyHtml}</body></html>");
        var parsedBaseUrl = Starling.Url.UrlParser.Parse(baseUrl).Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: parsedBaseUrl,
            http: http,
            diag: Starling.Common.Diagnostics.NoopDiagnostics.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return ctx;
    }
}
