using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// wp:M3-68 — §6.2.5.5 GetValue: reading a bare free identifier that resolves to
/// no binding is an unresolvable Reference and throws a ReferenceError (was
/// silently yielding undefined). `typeof`/`delete` of an undeclared identifier
/// still do not throw. An embedder can opt a realm back to lenient via
/// ThrowOnUnresolvedGlobalRead / LenientGlobalNames (the browser page realm does).
/// </summary>
[TestClass]
public class UnresolvedGlobalReadTests
{
    [TestMethod]
    public void Bare_undeclared_read_throws_reference_error()
    {
        var rt = new JsRuntime();
        RunOn(rt, "try { undeclaredX; globalThis.r='no-throw'; } catch (e) { globalThis.r = e.name + ':' + e.message; }");
        rt.GetGlobal("r").AsString.Should().Be("ReferenceError:undeclaredX is not defined");
    }

    [TestMethod]
    public void Typeof_undeclared_is_undefined_not_throw()
    {
        var rt = new JsRuntime();
        RunOn(rt, "globalThis.t = typeof undeclaredY;");
        rt.GetGlobal("t").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Delete_undeclared_does_not_throw()
    {
        var rt = new JsRuntime();
        RunOn(rt, "globalThis.d = delete undeclaredZ;");
        rt.GetGlobal("d").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Declared_var_read_still_works()
    {
        var rt = new JsRuntime();
        RunOn(rt, "var a = 5; globalThis.b = a + 1;");
        rt.GetGlobal("b").AsNumber.Should().Be(6);
    }

    [TestMethod]
    public void Lenient_realm_makes_undeclared_read_undefined()
    {
        var rt = new JsRuntime();
        rt.Realm.ThrowOnUnresolvedGlobalRead = false;
        RunOn(rt, "globalThis.t = typeof missingHost; globalThis.eq = (missingHost === undefined);");
        rt.GetGlobal("t").AsString.Should().Be("undefined");
        rt.GetGlobal("eq").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Allowlisted_name_is_lenient_others_still_throw()
    {
        var rt = new JsRuntime();
        rt.Realm.LenientGlobalNames.Add("allowedHost");
        RunOn(rt, "globalThis.ok = (allowedHost === undefined);" +
            "try { otherUndeclared; globalThis.threw = false; } catch (e) { globalThis.threw = (e.name === 'ReferenceError'); }");
        rt.GetGlobal("ok").AsBool.Should().BeTrue();
        rt.GetGlobal("threw").AsBool.Should().BeTrue();
    }

    private static JsValue RunOn(JsRuntime rt, string src)
    {
        var chunk = JsCompiler.CompileForEval(new JsParser(src).ParseProgram());
        return new JsVm(rt).Run(chunk);
    }
}
