using FluentAssertions;
using Tessera.Dom;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Bindings.Tests;

public sealed class PerformanceTests
{
    [Fact]
    public void Performance_now_returns_number()
    {
        var runtime = BuildEnv();
        Eval(runtime, "result = typeof performance.now();")
            .AsString.Should().Be("number");
    }

    [Fact]
    public void Performance_now_is_monotonic_nondecreasing()
    {
        var runtime = BuildEnv();
        Eval(runtime, """
            var a = performance.now();
            var b = performance.now();
            var c = performance.now();
            result = (b >= a) && (c >= b);
        """).AsBool.Should().BeTrue();
    }

    [Fact]
    public void TimeOrigin_is_pinned_at_install()
    {
        var origin = 1_700_000_000_000d;
        var runtime = BuildEnvWithClock(() => origin);
        Eval(runtime, "result = performance.timeOrigin;")
            .AsNumber.Should().Be(origin);
    }

    [Fact]
    public void Now_is_zero_or_more_relative_to_install()
    {
        var runtime = BuildEnv();
        Eval(runtime, "result = performance.now() >= 0;")
            .AsBool.Should().BeTrue();
    }

    private static JsRuntime BuildEnv()
    {
        var doc = new Document();
        doc.AppendChild(doc.CreateElement("html"));
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: "https://example.com/"));
        return runtime;
    }

    private static JsRuntime BuildEnvWithClock(Func<double> wallClock)
    {
        var doc = new Document();
        doc.AppendChild(doc.CreateElement("html"));
        var runtime = new JsRuntime();
        // Install Window first (it calls PerformanceBinding.Install with the
        // default wall clock); then re-install with the test seam. Re-install
        // is no-op when 'performance' already exists, so we install Performance
        // before Window to win the race for the property.
        PerformanceBinding.Install(runtime, wallClock);
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
