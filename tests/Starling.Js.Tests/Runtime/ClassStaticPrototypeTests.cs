using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// §15.7.10 — a *static* class element whose (computed) key resolves to
/// "prototype" is a TypeError at class-definition time. An *instance* element
/// named "prototype" is fine (it lands on C.prototype, not C).
/// </summary>
[TestClass]
public class ClassStaticPrototypeTests
{
    [TestMethod]
    public void Static_computed_method_named_prototype_throws_type_error()
        => RunCatch("class C { static ['prototype']() {} }").Should().Be("TypeError");

    [TestMethod]
    public void Static_computed_getter_named_prototype_throws_type_error()
        => RunCatch("class C { static get ['prototype']() { return 1; } }").Should().Be("TypeError");

    [TestMethod]
    public void Static_computed_field_named_prototype_throws_type_error()
        => RunCatch("class C { static ['prototype'] = 1; }").Should().Be("TypeError");

    [TestMethod]
    public void Instance_computed_method_named_prototype_is_allowed()
        => RunCatch("class C { ['prototype']() {} }").Should().Be("no-throw");

    [TestMethod]
    public void Static_non_prototype_computed_method_is_allowed()
        => RunCatch("class C { static ['other']() {} }").Should().Be("no-throw");

    private static string RunCatch(string body)
    {
        var rt = new JsRuntime();
        var chunk = JsCompiler.CompileForEval(new JsParser(
            "try { " + body + " globalThis.r = 'no-throw'; } catch (e) { globalThis.r = e.name; }").ParseProgram());
        new JsVm(rt).Run(chunk);
        return rt.GetGlobal("r").AsString;
    }
}
