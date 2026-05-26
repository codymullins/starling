using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Tests;

/// <summary>
/// §10.4.4 / §10.4.4.6 — the <em>mapped</em> arguments exotic object produced for
/// a non-strict function with a simple parameter list. Each index <c>i</c> is
/// live-linked to the i-th formal parameter (writing <c>arguments[i]</c> updates
/// the parameter and reassigning the parameter updates <c>arguments[i]</c>), and
/// the mapping is removed (§10.4.4.2 / §10.4.4.5) when the index is deleted or
/// redefined as an accessor / non-writable data property. Strict mode and
/// non-simple parameter lists keep the unmapped ordinary form.
/// </summary>
[TestClass]
public class MappedArgumentsTests
{
    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }

    [TestMethod]
    public void Writing_arguments_index_updates_the_mapped_parameter()
    {
        Eval("function f(a){ arguments[0] = 9; return a; } f(1);")
            .AsNumber.Should().Be(9);
    }

    [TestMethod]
    public void Reassigning_the_parameter_updates_arguments_index()
    {
        Eval("function f(a){ a = 7; return arguments[0]; } f(1);")
            .AsNumber.Should().Be(7);
    }

    [TestMethod]
    public void Second_parameter_is_also_mapped()
    {
        Eval("function f(a, b){ arguments[1] = 20; return b; } f(1, 2);")
            .AsNumber.Should().Be(20);
    }

    [TestMethod]
    public void Mapped_index_data_descriptor_is_writable_enumerable_configurable()
    {
        Eval(@"
            function f(a){
                var d = Object.getOwnPropertyDescriptor(arguments, '0');
                return [d.value, d.writable, d.enumerable, d.configurable].join(',');
            }
            f(5);
        ").AsString.Should().Be("5,true,true,true");
    }

    [TestMethod]
    public void Length_is_writable_nonenumerable_configurable()
    {
        Eval(@"
            function f(a){
                var d = Object.getOwnPropertyDescriptor(arguments, 'length');
                return [d.value, d.writable, d.enumerable, d.configurable].join(',');
            }
            f(5);
        ").AsString.Should().Be("1,true,false,true");
    }

    [TestMethod]
    public void Callee_is_the_function_writable_nonenumerable_configurable()
    {
        Eval(@"
            function f(){
                var d = Object.getOwnPropertyDescriptor(arguments, 'callee');
                return [d.value === f, d.writable, d.enumerable, d.configurable].join(',');
            }
            f();
        ").AsString.Should().Be("true,true,false,true");
    }

    [TestMethod]
    public void Deleting_index_removes_the_mapping()
    {
        // After delete, a later parameter write must no longer sync.
        Eval("function f(a){ delete arguments[0]; a = 5; return arguments[0]; } f(1);")
            .IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Redefining_index_as_nonwritable_removes_the_mapping()
    {
        // §10.4.4.2 — {writable:false} unmaps; value stays at its pre-unmap value.
        Eval(@"
            function f(a){
                Object.defineProperty(arguments, '0', {writable: false});
                a = 2;
                return arguments[0];
            }
            f(1);
        ").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Defining_index_nonconfigurable_only_keeps_the_mapping()
    {
        // {configurable:false} alone leaves the index mapped and writable.
        Eval(@"
            function f(a){
                Object.defineProperty(arguments, '0', {configurable: false});
                arguments[0] = 2;
                return a;
            }
            f(1);
        ").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Redefining_index_as_accessor_removes_the_mapping()
    {
        // §10.4.4.2 — an accessor redefine unmaps; the parameter is untouched.
        Eval(@"
            function f(a){
                var calls = 0;
                Object.defineProperty(arguments, '0', {
                    set(_v){ calls++; }, enumerable: true, configurable: true
                });
                arguments[0] = 'x';
                // arguments[0] reads undefined (setter-only accessor, no getter);
                // Array#join renders undefined as the empty string.
                return [calls, a, String(arguments[0])].join(',');
            }
            f(0);
        ").AsString.Should().Be("1,0,undefined");
    }

    [TestMethod]
    public void Strict_function_arguments_is_unmapped()
    {
        Eval("function f(a){ 'use strict'; arguments[0] = 9; return a; } f(1);")
            .AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Default_parameter_list_keeps_unmapped_arguments()
    {
        Eval("function f(a = 1){ arguments[0] = 9; return a; } f(2);")
            .AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Rest_parameter_list_keeps_unmapped_arguments()
    {
        Eval("function f(a, ...r){ arguments[0] = 9; return a; } f(1);")
            .AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Index_without_a_passed_argument_is_not_mapped()
    {
        // b was not passed: arguments[1] is an ordinary property, not linked to b.
        Eval("function f(a, b){ arguments[1] = 5; return b; } f(1);")
            .IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Duplicate_parameter_names_map_only_the_last_index()
    {
        // Non-strict `function f(a, a)`: only arguments[1] is mapped to the live
        // binding `a`; writing arguments[0] does not affect it.
        Eval("function f(a, a){ arguments[0] = 10; arguments[1] = 20; return a; } f(1, 2);")
            .AsNumber.Should().Be(20);
    }

    [TestMethod]
    public void Captured_parameter_stays_in_sync_via_arguments()
    {
        // A parameter captured by a nested closure is boxed into a Cell; the
        // mapping must write through the same cell so the closure observes it.
        Eval(@"
            function f(a){
                var read = () => a;
                arguments[0] = 42;
                return read();
            }
            f(1);
        ").AsNumber.Should().Be(42);
    }

    [TestMethod]
    public void Mapped_arguments_object_still_iterates()
    {
        Eval("function f(a, b){ return [...arguments].join(','); } f(1, 2);")
            .AsString.Should().Be("1,2");
    }
}
