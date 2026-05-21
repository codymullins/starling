using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// M3-31 — Tests for the Web Crypto minimal surface:
/// <c>crypto.getRandomValues</c> and <c>crypto.randomUUID</c>.
/// </summary>
[TestClass]
public sealed class CryptoBindingTests
{
    // ------------------------------------------------------------------ //
    // typeof crypto
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void Crypto_is_an_object()
    {
        var runtime = BuildEnv();
        Eval(runtime, "result = typeof crypto;")
            .AsString.Should().Be("object");
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — returns same array (identity)
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_returns_same_object()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = new Uint8Array(16);
            result = (crypto.getRandomValues(a) === a);
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — fills with valid byte values
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_fills_uint8_with_bytes_0_to_255()
    {
        var runtime = BuildEnv();
        // All values must be integers in [0, 255].
        Eval(runtime, """
            var a = new Uint8Array(16);
            crypto.getRandomValues(a);
            var allInRange = true;
            for (var i = 0; i < a.length; i++) {
                if (a[i] < 0 || a[i] > 255 || a[i] !== Math.floor(a[i])) {
                    allInRange = false;
                    break;
                }
            }
            result = allInRange;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void GetRandomValues_statistically_not_all_zero()
    {
        // 16 random bytes should not all be 0 (prob 2^-128).
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = new Uint8Array(16);
            crypto.getRandomValues(a);
            var anyNonZero = false;
            for (var i = 0; i < a.length; i++) {
                if (a[i] !== 0) { anyNonZero = true; break; }
            }
            result = anyNonZero;
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — Int32Array and Uint32Array also work
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_works_with_Int32Array()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = new Int32Array(4);
            result = (crypto.getRandomValues(a) === a);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void GetRandomValues_works_with_Uint32Array()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = new Uint32Array(4);
            result = (crypto.getRandomValues(a) === a);
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — error: Float typed arrays rejected
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_throws_for_Float32Array()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { crypto.getRandomValues(new Float32Array(4)); }
            catch (e) { threw = (e instanceof TypeError); }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void GetRandomValues_throws_for_Float64Array()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { crypto.getRandomValues(new Float64Array(4)); }
            catch (e) { threw = (e instanceof TypeError); }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — error: non-typed-array argument
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_throws_TypeError_for_plain_array()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { crypto.getRandomValues([1,2,3]); }
            catch (e) { threw = (e instanceof TypeError); }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // getRandomValues — error: byteLength > 65536 throws (QuotaExceeded)
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void GetRandomValues_throws_for_oversized_buffer()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { crypto.getRandomValues(new Uint8Array(65537)); }
            catch (e) { threw = true; }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void GetRandomValues_accepts_exactly_65536_bytes()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = new Uint8Array(65536);
            result = (crypto.getRandomValues(a) === a);
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // randomUUID — format
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void RandomUUID_matches_v4_format()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var uuid = crypto.randomUUID();
            result = /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/.test(uuid);
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void RandomUUID_returns_string()
    {
        var runtime = BuildEnv();
        Eval(runtime, "result = typeof crypto.randomUUID();")
            .AsString.Should().Be("string");
    }

    [TestMethod]
    public void RandomUUID_two_calls_differ()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = crypto.randomUUID();
            var b = crypto.randomUUID();
            result = (a !== b);
        """).AsBool.Should().BeTrue();
    }

    // ------------------------------------------------------------------ //
    // subtle is undefined (not implemented)
    // ------------------------------------------------------------------ //

    [TestMethod]
    public void Crypto_subtle_is_undefined()
    {
        var runtime = BuildEnv();
        Eval(runtime, "result = typeof crypto.subtle;")
            .AsString.Should().Be("undefined");
    }

    // ------------------------------------------------------------------ //
    // Helpers
    // ------------------------------------------------------------------ //

    private static JsRuntime BuildEnv()
    {
        var doc = new Document();
        doc.AppendChild(doc.CreateElement("html"));
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: "https://example.com/"));
        return runtime;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
