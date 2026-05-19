using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

[TestClass]
public class ConsoleTests
{
    [TestMethod]
    public void Console_is_registered_as_non_enumerable_global_with_builtin_methods()
    {
        var rt = new JsRuntime();
        var console = rt.GetGlobal("console");

        console.IsObject.Should().BeTrue();
        var globalDesc = rt.Global.GetOwnPropertyDescriptor("console");
        globalDesc.Should().NotBeNull();
        globalDesc!.Value.Writable.Should().BeTrue();
        globalDesc.Value.Enumerable.Should().BeFalse();
        globalDesc.Value.Configurable.Should().BeTrue();

        foreach (var name in new[] { "log", "info", "warn", "error", "debug", "dir", "table", "time", "timeEnd", "count", "countReset", "group", "groupCollapsed", "groupEnd", "trace", "assert", "clear" })
        {
            var desc = console.AsObject.GetOwnPropertyDescriptor(name);
            desc.Should().NotBeNull(name);
            desc!.Value.Writable.Should().BeTrue(name);
            desc.Value.Enumerable.Should().BeFalse(name);
            desc.Value.Configurable.Should().BeTrue(name);
            desc.Value.Value.IsObject.Should().BeTrue(name);
        }
    }

    [TestMethod]
    public void Log_levels_route_to_sink_and_format_basic_arguments()
    {
        var capture = Run(@"
            console.log('hello', 1, true, null, undefined);
            console.info('info');
            console.warn('warn');
            console.error('error');
            console.debug('debug');
        ");

        capture.Should().HaveCount(5);
        capture[0].Should().Be((ConsoleLevel.Log, "hello 1 true null undefined"));
        capture[1].Should().Be((ConsoleLevel.Info, "info"));
        capture[2].Should().Be((ConsoleLevel.Warn, "warn"));
        capture[3].Should().Be((ConsoleLevel.Error, "error"));
        capture[4].Should().Be((ConsoleLevel.Debug, "debug"));
    }

    [TestMethod]
    public void Formatter_consumes_console_standard_substitutions()
    {
        var capture = Run(@"
            console.log('name=%s n=%d i=%i f=%f pct=%% obj=%o json=%j tail', 'star', 3.25, 4.75, 1.5, { a: 1 }, { b: 'x' }, 'extra');
        ");

        capture.Should().ContainSingle();
        capture[0].Level.Should().Be(ConsoleLevel.Log);
        capture[0].Message.Should().Be("name=star n=3.25 i=4 f=1.5 pct=% obj={ a: 1 } json={ b: \"x\" } tail extra");
    }

    [TestMethod]
    public void Objects_are_pretty_printed_for_log_dir_and_circular_references()
    {
        var capture = Run(@"
            var o = { a: 1, b: 'two' };
            o.self = o;
            console.log(o);
            console.dir({ nested: o });
        ");

        capture.Should().HaveCount(2);
        capture[0].Should().Be((ConsoleLevel.Log, "{ a: 1, b: \"two\", self: [Circular] }"));
        capture[1].Level.Should().Be(ConsoleLevel.Dir);
        capture[1].Message.Should().Contain("nested: { a: 1");
        capture[1].Message.Should().Contain("self: [Circular]");
    }

    [TestMethod]
    public void Table_renders_object_rows_as_text_columns()
    {
        var capture = Run("console.table({ first: 1, second: 'two' });");

        capture.Should().ContainSingle();
        capture[0].Level.Should().Be(ConsoleLevel.Table);
        capture[0].Message.Should().Contain("(index)");
        capture[0].Message.Should().Contain("Value");
        capture[0].Message.Should().Contain("first");
        capture[0].Message.Should().Contain("1");
        capture[0].Message.Should().Contain("second");
        capture[0].Message.Should().Contain("\"two\"");
    }

    [TestMethod]
    public void Count_and_countReset_track_counts_per_label()
    {
        var capture = Run(@"
            console.count('apples');
            console.count('apples');
            console.count();
            console.countReset('apples');
            console.count('apples');
        ");

        capture.Should().HaveCount(4);
        capture[0].Should().Be((ConsoleLevel.Info, "apples: 1"));
        capture[1].Should().Be((ConsoleLevel.Info, "apples: 2"));
        capture[2].Should().Be((ConsoleLevel.Info, "default: 1"));
        capture[3].Should().Be((ConsoleLevel.Info, "apples: 1"));
    }

    [TestMethod]
    public void Groups_indent_subsequent_output_and_groupEnd_restores_indent()
    {
        var capture = Run(@"
            console.group('outer');
            console.log('inside');
            console.groupCollapsed('inner');
            console.warn('deep');
            console.groupEnd();
            console.error('after inner');
            console.groupEnd();
            console.log('after all');
        ");

        capture.Should().HaveCount(6);
        capture[0].Should().Be((ConsoleLevel.Log, "outer"));
        capture[1].Should().Be((ConsoleLevel.Log, "  inside"));
        capture[2].Should().Be((ConsoleLevel.Log, "  inner"));
        capture[3].Should().Be((ConsoleLevel.Warn, "    deep"));
        capture[4].Should().Be((ConsoleLevel.Error, "  after inner"));
        capture[5].Should().Be((ConsoleLevel.Log, "after all"));
    }

    [TestMethod]
    public void TimeEnd_emits_elapsed_milliseconds_for_label()
    {
        var capture = Run(@"
            console.time('load');
            console.timeEnd('load');
        ");

        capture.Should().ContainSingle();
        capture[0].Level.Should().Be(ConsoleLevel.Info);
        capture[0].Message.Should().MatchRegex(@"^load: \d+(\.\d+)?ms$");
    }

    [TestMethod]
    public void Assert_writes_only_when_condition_is_falsy()
    {
        var capture = Run(@"
            console.assert(true, 'not printed');
            console.assert(0, 'bad %s', 'thing');
        ");

        capture.Should().ContainSingle();
        capture[0].Should().Be((ConsoleLevel.Error, "Assertion failed: bad thing"));
    }

    [TestMethod]
    public void Trace_emits_label_and_placeholder_js_frame()
    {
        var capture = Run("console.trace('here', 7);");

        capture.Should().ContainSingle();
        capture[0].Level.Should().Be(ConsoleLevel.Trace);
        capture[0].Message.Should().Be("here 7\n  at <js>");
    }

    [TestMethod]
    public void Clear_invokes_optional_host_hook()
    {
        var runtime = new JsRuntime();
        var cleared = false;
        runtime.Realm.ConsoleClear = () => cleared = true;

        Eval(runtime, "console.clear();");

        cleared.Should().BeTrue();
    }

    private static List<(ConsoleLevel Level, string Message)> Run(string src)
    {
        var runtime = new JsRuntime();
        var captured = new List<(ConsoleLevel Level, string Message)>();
        runtime.Realm.ConsoleSink = (level, message) => captured.Add((level, message));
        Eval(runtime, src);
        return captured;
    }

    private static JsValue Eval(JsRuntime runtime, string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(runtime).Run(chunk);
    }
}
