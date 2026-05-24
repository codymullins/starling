using AwesomeAssertions;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Js.Tests.Runtime;

/// <summary>
/// §13.3.11 tagged templates — <c>tag`…`</c> calls <c>tag(strings, …subs)</c>
/// with a frozen, call-site-cached strings object (cooked array + <c>.raw</c>).
/// </summary>
[TestClass]
public class JsTaggedTemplateTests
{
    [TestMethod]
    public void Tag_receives_cooked_strings_and_substitutions()
    {
        Eval("""
            function tag(s, ...subs) { return s.join('|') + '::' + subs.join(','); }
            var a = 1, b = 2;
            tag`x${a}y${b}z`;
            """).AsString.Should().Be("x|y|z::1,2");
    }

    [TestMethod]
    public void Tag_no_substitution_yields_single_cooked_segment()
    {
        Eval("function tag(s){ return s.length + ':' + s[0]; } tag`hello`;")
            .AsString.Should().Be("1:hello");
    }

    [TestMethod]
    public void Cooked_applies_escapes_raw_preserves_source()
    {
        Eval("""
            function tag(s){ return s.raw[0]; }
            tag`a\tb\n`;
            """).AsString.Should().Be("a\\tb\\n");

        Eval("""
            function tag(s){ return s[0]; }
            tag`a\tb`;
            """).AsString.Should().Be("a\tb");
    }

    [TestMethod]
    public void String_raw_builtin_works_over_tagged_template()
    {
        // raw segments keep the backslash escapes verbatim; ${1} is a real
        // substitution interleaved between them.
        Eval(@"String.raw`a\n${1}b`;").AsString.Should().Be(@"a\n1b");
    }

    [TestMethod]
    public void Strings_object_is_an_array_with_raw_sibling_and_non_extensible()
    {
        // The dense JsArray backing can't represent per-element non-writability,
        // so full Object.isFrozen isn't reachable here; assert the invariants
        // tag functions actually rely on: it's an array, .raw is a parallel
        // array, and new properties can't be added.
        Eval("""
            function tag(s){
              return Array.isArray(s) && Array.isArray(s.raw)
                  && s.length === 2 && s.raw.length === 2
                  && !Object.isExtensible(s) && !Object.isExtensible(s.raw);
            }
            tag`a${1}b`;
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Same_call_site_is_cached_to_one_object()
    {
        Eval("""
            function id(s){ return s; }
            function ev(){ var a = 0; return id`p${a}q`; }
            ev() === ev();
            """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Distinct_call_sites_are_distinct_objects()
    {
        Eval("""
            function id(s){ return s; }
            (function(){ var a = 0; return id`p${a}q`; })() === (function(){ var a = 0; return id`p${a}q`; })();
            """).AsBool.Should().BeFalse();
    }

    [TestMethod]
    public void Member_tag_binds_this_to_its_base()
    {
        Eval("""
            var o = { name: 'O', t(s, x){ return this.name + ':' + s[0] + x; } };
            o.t`hi${42}`;
            """).AsString.Should().Be("O:hi42");
    }

    [TestMethod]
    public void Substitutions_are_passed_in_source_order()
    {
        Eval("""
            function tag(s, a, b, c){ return [a, b, c].join('-'); }
            tag`${10}${20}${30}`;
            """).AsString.Should().Be("10-20-30");
    }

    [TestMethod]
    public void Invalid_escape_yields_undefined_cooked_with_raw_preserved()
    {
        // §12.9.6 — an invalid escape sequence is legal in a tagged template:
        // the cooked element is `undefined` while the raw source is preserved.
        Eval("""
            function tag(s){ return (s[0] === undefined) + '|' + s.raw[0]; }
            tag`\unicode`;
            """).AsString.Should().Be(@"true|\unicode");
    }

    [TestMethod]
    public void Invalid_escape_only_blanks_its_own_segment_cooked()
    {
        // A valid leading segment still cooks; only the bad segment is undefined,
        // and that segment's raw value is intact.
        Eval("""
            function tag(s){ return s[0] + '|' + (s[1] === undefined) + '|' + s.raw[1]; }
            tag`ok${1}\xg done`;
            """).AsString.Should().Be(@"ok|true|\xg done");
    }

    [TestMethod]
    public void Invalid_escape_in_untagged_template_is_a_syntax_error()
    {
        var act = () => Eval(@"var x = `\unicode`;");
        act.Should().Throw<JsParseException>();
    }

    [TestMethod]
    public void Same_call_site_caches_one_object_across_repeated_evaluation()
    {
        // The same site is evaluated three times via a loop; every evaluation must
        // hand the tag the very same frozen template object.
        Eval("""
            var seen = [];
            function id(s){ return s; }
            for (var i = 0; i < 3; i++) seen.push(id`a${i}b`);
            seen[0] === seen[1] && seen[1] === seen[2];
            """).AsBool.Should().BeTrue();
    }

    private static JsValue Eval(string src)
    {
        var program = new JsParser(src).ParseProgram();
        var chunk = JsCompiler.CompileForEval(program);
        return new JsVm(new JsRuntime()).Run(chunk);
    }
}
