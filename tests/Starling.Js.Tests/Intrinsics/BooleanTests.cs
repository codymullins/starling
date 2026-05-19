using FluentAssertions;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Js.Tests.Intrinsics;

public class BooleanTests
{
    [Fact]
    public void Boolean_constructor_coerces_primitives()
    {
        Eval("Boolean();").AsBool.Should().BeFalse();
        Eval("Boolean(0);").AsBool.Should().BeFalse();
        Eval("Boolean(1);").AsBool.Should().BeTrue();
        Eval("Boolean('');").AsBool.Should().BeFalse();
        Eval("Boolean('false');").AsBool.Should().BeTrue();
        Eval("Boolean(null);").AsBool.Should().BeFalse();
        Eval("Boolean(undefined);").AsBool.Should().BeFalse();
        Eval("Boolean({});").AsBool.Should().BeTrue();
    }

    [Fact]
    public void New_Boolean_boxes_but_objects_are_truthy()
    {
        Eval("var b = new Boolean(false); b.valueOf();").AsBool.Should().BeFalse();
        Eval("var b = new Boolean(true); b.valueOf();").AsBool.Should().BeTrue();
        Eval("var b = new Boolean(false); b.toString();").AsString.Should().Be("false");
        Eval("var b = new Boolean(true); b.toString();").AsString.Should().Be("true");
        Eval("new Boolean(false) ? 1 : 2;").AsNumber.Should().Be(1);
    }

    [Fact]
    public void Boolean_prototype_methods_work_on_primitives_and_reject_wrong_receivers()
    {
        Eval("(true).toString();").AsString.Should().Be("true");
        Eval("(false).toString();").AsString.Should().Be("false");
        Eval("(true).valueOf();").AsBool.Should().BeTrue();
        Eval("(false).valueOf();").AsBool.Should().BeFalse();
        Action badValueOf = () => Eval("Boolean.prototype.valueOf();");
        Action badToString = () => Eval("Boolean.prototype.toString();");
        badValueOf.Should().Throw<JsThrow>();
        badToString.Should().Throw<JsThrow>();
    }

    [Fact]
    public void Boolean_global_and_prototype_slots_are_installed()
    {
        var rt = new JsRuntime();
        var Boolean = rt.GetGlobal("Boolean");
        Boolean.IsObject.Should().BeTrue();
        Boolean.AsObject.Get("prototype").AsObject.Should().BeSameAs(rt.Realm.BooleanPrototype);
        rt.Realm.BooleanConstructor.Should().BeSameAs(Boolean.AsObject);
        rt.Realm.BooleanPrototype.Get("constructor").AsObject.Should().BeSameAs(Boolean.AsObject);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
