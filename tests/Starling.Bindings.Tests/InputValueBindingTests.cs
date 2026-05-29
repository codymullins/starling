using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Bindings.Tests;

/// <summary>
/// Editable form-control JS surface: the <c>value</c> IDL attribute (live
/// value with content-attribute fallback), <c>document.activeElement</c>, and
/// <c>element.focus()</c>/<c>.blur()</c> — the script-visible half of
/// click-to-type.
/// </summary>
[TestClass]
public sealed class InputValueBindingTests
{
    [TestMethod]
    public void Input_value_reads_attribute_then_live_value()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            i.setAttribute('value', 'init');
            var before = i.value;          // content attribute is the initial value
            i.value = 'typed';             // assignment sets the live value
            result = before + '|' + i.value;
        """).AsString.Should().Be("init|typed");
    }

    [TestMethod]
    public void Setting_value_in_js_updates_the_dom_input_value()
    {
        var (runtime, doc) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            i.id = 'box';
            document.body.appendChild(i);
            i.value = 'hello';
        """);
        doc.GetElementById("box")!.InputValue.Should().Be("hello");
    }

    [TestMethod]
    public void ActiveElement_defaults_to_body_and_tracks_focus_and_blur()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var i = document.createElement('input');
            document.body.appendChild(i);
            var atStart = document.activeElement === document.body;
            i.focus();
            var afterFocus = document.activeElement === i;
            i.blur();
            var afterBlur = document.activeElement === document.body;
            result = atStart && afterFocus && afterBlur;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Form_controls_support_selection_validation_and_serialization()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var f = document.createElement('form');
            var q = document.createElement('input');
            q.name = 'q';
            q.required = true;
            f.appendChild(q);
            document.body.appendChild(f);

            var invalid = q.checkValidity();
            q.value = 'hello world';
            q.setSelectionRange(6, 11);
            var valid = q.checkValidity();
            result = invalid + '|' + valid + '|' + q.selectionStart + ':' + q.selectionEnd + '|' + f.serialize();
        """).AsString.Should().Be("false|true|6:11|q=hello+world");
    }

    [TestMethod]
    public void Checkbox_and_select_values_participate_in_form_serialization()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            document.body.innerHTML =
              '<form id="f">' +
              '<input type="checkbox" name="agree" value="yes">' +
              '<select name="ship"><option value="ground">Ground</option><option value="air">Air</option></select>' +
              '</form>';
            var agree = document.querySelector('input');
            var select = document.querySelector('select');
            agree.checked = true;
            select.value = 'air';
            result = document.getElementById('f').serialize();
        """).AsString.Should().Be("agree=yes&ship=air");
    }

    // ---------------------------------------------------------------- helpers

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
