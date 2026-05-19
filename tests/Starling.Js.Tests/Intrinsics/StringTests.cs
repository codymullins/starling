using FluentAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Intrinsics;

/// <summary>
/// End-to-end coverage for the ES2024 String constructor and String.prototype
/// surface installed by <c>StringCtor.Install</c>.
/// </summary>
[TestClass]
public class StringTests
{
    [TestMethod]
    public void Constructor_call_and_construct_coerce_values()
    {
        Eval("String();").AsString.Should().Be(string.Empty);
        Eval("String(123);").AsString.Should().Be("123");
        Eval("String(true);").AsString.Should().Be("true");
        Eval("var s = new String('abc'); s.toString();").AsString.Should().Be("abc");
        Eval("var s = new String('abc'); s.valueOf();").AsString.Should().Be("abc");
        Eval("var s = new String('abc'); s.length;").AsNumber.Should().Be(3);
        Eval("var s = new String('abc'); s[1];").AsString.Should().Be("b");
    }

    [TestMethod]
    public void Static_fromCharCode_and_fromCodePoint_handle_code_units_and_scalars()
    {
        Eval("String.fromCharCode(65, 66, 67);").AsString.Should().Be("ABC");
        Eval("String.fromCharCode(65537);").AsString.Should().Be("\u0001");
        Eval("String.fromCharCode(NaN);").AsString.Should().Be("\u0000");
        Eval("String.fromCodePoint(0x41, 0x1F600);").AsString.Should().Be("A😀");
        Eval("String.fromCodePoint();").AsString.Should().Be(string.Empty);
    }

    [TestMethod]
    public void Static_raw_interleaves_raw_segments_and_substitutions()
    {
        Eval("String.raw({ raw: { '0': 'a', '1': 'c', length: 2 } }, 'b');").AsString.Should().Be("abc");
        Eval("String.raw({ raw: { '0': 'x', length: 1 } });").AsString.Should().Be("x");
    }

    [TestMethod]
    public void Code_unit_and_code_point_accessors_cover_edges()
    {
        Eval("'abc'.at(1);").AsString.Should().Be("b");
        Eval("'abc'.at(-1);").AsString.Should().Be("c");
        Eval("'abc'.at(9);").IsUndefined.Should().BeTrue();
        Eval("'abc'.charAt(9);").AsString.Should().Be(string.Empty);
        Eval("'abc'.charCodeAt(1);").AsNumber.Should().Be(98);
        double.IsNaN(Eval("'abc'.charCodeAt(9);").AsNumber).Should().BeTrue();
        Eval("'😀'.codePointAt(0);").AsNumber.Should().Be(0x1F600);
        Eval("'😀'.codePointAt(1);").AsNumber.Should().Be(0xDE00);
        Eval("'😀'.codePointAt(2);").IsUndefined.Should().BeTrue();
    }

    [TestMethod]
    public void Search_and_position_methods_use_string_patterns()
    {
        Eval("'hello'.includes('ell');").AsBool.Should().BeTrue();
        Eval("'hello'.includes('ell', 2);").AsBool.Should().BeFalse();
        Eval("'hello'.startsWith('he');").AsBool.Should().BeTrue();
        Eval("'hello'.startsWith('el', 1);").AsBool.Should().BeTrue();
        Eval("'hello'.endsWith('lo');").AsBool.Should().BeTrue();
        Eval("'hello'.endsWith('l', 3);").AsBool.Should().BeTrue();
        Eval("'banana'.indexOf('na');").AsNumber.Should().Be(2);
        Eval("'banana'.indexOf('na', 3);").AsNumber.Should().Be(4);
        Eval("'banana'.lastIndexOf('na');").AsNumber.Should().Be(4);
        Eval("'banana'.lastIndexOf('na', 3);").AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Slicing_and_substring_methods_clamp_and_swap_indices()
    {
        Eval("'abcdef'.slice(1, 4);").AsString.Should().Be("bcd");
        Eval("'abcdef'.slice(-3, -1);").AsString.Should().Be("de");
        Eval("'abcdef'.slice(4, 1);").AsString.Should().Be(string.Empty);
        Eval("'abcdef'.substring(4, 1);").AsString.Should().Be("bcd");
        Eval("'abcdef'.substring(-2, 2);").AsString.Should().Be("ab");
    }

    [TestMethod]
    public void Concatenation_padding_repeat_and_case_mapping_work()
    {
        Eval("'a'.concat('b', 3);").AsString.Should().Be("ab3");
        Eval("'x'.padStart(4, 'ab');").AsString.Should().Be("abax");
        Eval("'x'.padEnd(4, 'ab');").AsString.Should().Be("xaba");
        Eval("'x'.padStart(2, '');").AsString.Should().Be("x");
        Eval("'ha'.repeat(3);").AsString.Should().Be("hahaha");
        Eval("'AbC'.toLowerCase();").AsString.Should().Be("abc");
        Eval("'AbC'.toUpperCase();").AsString.Should().Be("ABC");
        Eval("'AbC'.toLocaleLowerCase();").AsString.Should().Be("abc");
        Eval("'AbC'.toLocaleUpperCase();").AsString.Should().Be("ABC");
    }

    [TestMethod]
    public void Trimming_normalization_and_locale_compare_are_invariant()
    {
        Eval("'  hi  '.trim();").AsString.Should().Be("hi");
        Eval("'  hi  '.trimStart();").AsString.Should().Be("hi  ");
        Eval("'  hi  '.trimEnd();").AsString.Should().Be("  hi");
        Eval("'\uFEFFhi\uFEFF'.trim();").AsString.Should().Be("hi");
        Eval("'abc'.normalize();").AsString.Should().Be("abc");
        Eval("'abc'.normalize('NFD');").AsString.Should().Be("abc");
        Eval("'a'.localeCompare('a');").AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void Replace_and_replaceAll_support_string_patterns_and_substitutions()
    {
        Eval("'banana'.replace('na', 'NA');").AsString.Should().Be("baNAna");
        Eval("'banana'.replaceAll('na', 'NA');").AsString.Should().Be("baNANA");
        Eval("'abc'.replace('b', \"$&-$`-$'\");").AsString.Should().Be("ab-a-cc");
        Eval("'abc'.replace('b', function(m, i, s) { return m + i + s; });").AsString.Should().Be("ab1abcc");
        Eval("'ab'.replaceAll('', '.');").AsString.Should().Be(".a.b.");
    }

    [TestMethod]
    public void Split_supports_string_separators_limits_and_empty_separator()
    {
        Eval("var a = 'a,b,c'.split(','); a[0] + a[1] + a[2] + a.length;").AsString.Should().Be("abc3");
        Eval("var a = 'a,b,c'.split(',', 2); a[0] + a[1] + a.length;").AsString.Should().Be("ab2");
        Eval("var a = 'abc'.split(''); a[0] + a[1] + a[2] + a.length;").AsString.Should().Be("abc3");
        Eval("var a = 'abc'.split(); a[0] + a.length;").AsString.Should().Be("abc1");
        Eval("'abc'.split('', 0).length;").AsNumber.Should().Be(0);
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
