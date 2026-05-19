using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>End-to-end coverage for <c>FinalizationRegistry</c> (B4-6).</summary>
/// <remarks>
/// As with <see cref="WeakRefTests"/>, we deliberately skip the GC-timing
/// assertion ("after dropping the target + forced GC, cleanup callback
/// fires with heldValue"). The cleanup pass + microtask scheduling are
/// covered indirectly via the API surface here; the actual reclamation
/// behavior needs a dedicated test fixture that can pin the GC schedule.
/// </remarks>
[TestClass]
public class FinalizationRegistryTests
{
    [TestMethod]
    public void FinalizationRegistry_constructor_wired()
    {
        var rt = new JsRuntime();
        rt.GetGlobal("FinalizationRegistry").IsObject.Should().BeTrue();
        rt.Realm.FinalizationRegistryConstructor.Should().NotBeNull();
    }

    [TestMethod]
    public void Constructing_with_non_callable_throws_TypeError()
    {
        Action act = () => Eval("new FinalizationRegistry({});");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Constructing_with_no_args_throws_TypeError()
    {
        Action act = () => Eval("new FinalizationRegistry();");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Register_with_primitive_target_throws_TypeError()
    {
        Action act = () => Eval(@"
            var fr = new FinalizationRegistry(function(){});
            fr.register(1, 'held');");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Register_target_equal_to_heldValue_throws_TypeError()
    {
        Action act = () => Eval(@"
            var fr = new FinalizationRegistry(function(){});
            var o = {};
            fr.register(o, o);");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void Register_returns_undefined_on_success()
    {
        Eval(@"
            var fr = new FinalizationRegistry(function(){});
            var o = {};
            typeof fr.register(o, 'held');").AsString.Should().Be("undefined");
    }

    [TestMethod]
    public void Register_with_unregister_token_then_unregister_returns_true()
    {
        Eval(@"
            var fr = new FinalizationRegistry(function(){});
            var o = {};
            var token = {};
            fr.register(o, 'held', token);
            fr.unregister(token);").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Unregister_with_unknown_token_returns_false()
    {
        Eval(@"
            var fr = new FinalizationRegistry(function(){});
            fr.unregister({});").AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Unregister_with_primitive_token_throws_TypeError()
    {
        Action act = () => Eval(@"
            var fr = new FinalizationRegistry(function(){});
            fr.unregister(1);");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    [TestMethod]
    public void ToStringTag_property_is_FinalizationRegistry()
    {
        // Object.prototype.toString doesn't yet consult @@toStringTag (B3-1
        // follow-up), so we just verify the symbol property is wired.
        Eval(@"
            var fr = new FinalizationRegistry(function(){});
            fr[Symbol.toStringTag];").AsString.Should().Be("FinalizationRegistry");
    }

    [TestMethod]
    public void Register_on_wrong_receiver_throws_TypeError()
    {
        Action act = () => Eval("FinalizationRegistry.prototype.register.call({}, {}, 'h');");
        act.Should().Throw<JsThrow>()
            .Which.Value.AsObject.Get("name").AsString.Should().Be("TypeError");
    }

    // TODO(B4-6 follow-up): GC-timing test that proves the cleanup callback
    // is invoked with the held value after the target is collected. Needs a
    // forced-GC fixture and deterministic microtask draining.

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
