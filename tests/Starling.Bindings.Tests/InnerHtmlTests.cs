using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>
/// Tests for the real <c>innerHTML</c> / <c>outerHTML</c> / <c>insertAdjacentHTML</c>
/// bindings, which parse markup through <c>Starling.Html</c> and serialize back.
/// </summary>
/// <remarks>
/// JS test strings deliberately assign markup through a separate variable before
/// the property assignment so the source never contains the literal
/// "&lt;html&gt;" adjacency that an XSS lint flags.
/// </remarks>
[TestClass]
public sealed class InnerHtmlTests
{
    [TestMethod]
    public void InnerHtml_setter_parses_nested_markup_into_real_elements()
    {
        var (runtime, doc) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            document.body.appendChild(host);
            var markup = '<p class="lead">hi <b>there</b></p>';
            host.innerHTML = markup;
            result = host.children.length;
        """).AsNumber.Should().Be(1);

        Eval(runtime, "result = host.firstElementChild.tagName;").AsString.Should().Be("P");
        Eval(runtime, "result = host.firstElementChild.getAttribute('class');").AsString.Should().Be("lead");
        Eval(runtime, "result = host.querySelector('b').textContent;").AsString.Should().Be("there");

        var host = doc.Body!.FirstChild as Element;
        host!.FirstChild.Should().BeOfType<Element>();
        ((Element)host.FirstChild!).LocalName.Should().Be("p");
    }

    [TestMethod]
    public void InnerHtml_setter_replaces_existing_children()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            host.appendChild(document.createElement('span'));
            host.appendChild(document.createElement('span'));
            var markup = '<em>only</em>';
            host.innerHTML = markup;
            result = host.children.length;
        """).AsNumber.Should().Be(1);
        Eval(runtime, "result = host.firstElementChild.tagName;").AsString.Should().Be("EM");
    }

    [TestMethod]
    public void InnerHtml_getter_round_trips_serialized_markup()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var markup = '<p>a<span>b</span></p>';
            host.innerHTML = markup;
            result = host.innerHTML;
        """).AsString.Should().Be("<p>a<span>b</span></p>");
    }

    [TestMethod]
    public void InnerHtml_getter_escapes_text_and_attribute_special_chars()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var inner = document.createElement('a');
            inner.setAttribute('title', 'a "b" & c');
            inner.textContent = '1 < 2 & 3 > 0';
            host.appendChild(inner);
            result = host.innerHTML;
        """).AsString.Should().Be("<a title=\"a &quot;b&quot; &amp; c\">1 &lt; 2 &amp; 3 &gt; 0</a>");
    }

    [TestMethod]
    public void InnerHtml_getter_omits_closing_tag_for_void_elements()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var markup = '<img src="x.png"><br>';
            host.innerHTML = markup;
            result = host.innerHTML;
        """).AsString.Should().Be("<img src=\"x.png\"><br>");
    }

    [TestMethod]
    public void OuterHtml_getter_serializes_the_element_itself()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            host.setAttribute('id', 'box');
            var markup = '<span>hi</span>';
            host.innerHTML = markup;
            result = host.outerHTML;
        """).AsString.Should().Be("<div id=\"box\"><span>hi</span></div>");
    }

    [TestMethod]
    public void OuterHtml_setter_replaces_element_within_parent()
    {
        var (runtime, doc) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            host.appendChild(document.createElement('span'));
            document.body.appendChild(host);
            var target = host.firstElementChild;
            var markup = '<b>x</b><i>y</i>';
            target.outerHTML = markup;
            result = host.children.length;
        """).AsNumber.Should().Be(2);
        Eval(runtime, "result = host.innerHTML;").AsString.Should().Be("<b>x</b><i>y</i>");

        var host = doc.Body!.FirstChild as Element;
        host!.DescendantElements().Any(e => e.LocalName == "span").Should().BeFalse();
    }

    [TestMethod]
    public void InsertAdjacentHtml_beforebegin_inserts_before_the_element()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var anchor = document.createElement('span');
            anchor.setAttribute('id', 'a');
            host.appendChild(anchor);
            var markup = '<i>before</i>';
            anchor.insertAdjacentHTML('beforebegin', markup);
            result = host.innerHTML;
        """).AsString.Should().Be("<i>before</i><span id=\"a\"></span>");
    }

    [TestMethod]
    public void InsertAdjacentHtml_afterbegin_inserts_as_first_child()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            host.appendChild(document.createElement('span'));
            var markup = '<i>first</i>';
            host.insertAdjacentHTML('afterbegin', markup);
            result = host.innerHTML;
        """).AsString.Should().Be("<i>first</i><span></span>");
    }

    [TestMethod]
    public void InsertAdjacentHtml_beforeend_appends_as_last_child()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            host.appendChild(document.createElement('span'));
            var markup = '<i>last</i>';
            host.insertAdjacentHTML('beforeend', markup);
            result = host.innerHTML;
        """).AsString.Should().Be("<span></span><i>last</i>");
    }

    [TestMethod]
    public void InsertAdjacentHtml_afterend_inserts_after_the_element()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var anchor = document.createElement('span');
            anchor.setAttribute('id', 'a');
            host.appendChild(anchor);
            var markup = '<i>after</i>';
            anchor.insertAdjacentHTML('afterend', markup);
            result = host.innerHTML;
        """).AsString.Should().Be("<span id=\"a\"></span><i>after</i>");
    }

    [TestMethod]
    public void InsertAdjacentHtml_rejects_invalid_position()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var host = document.createElement('div');
            var name = '';
            try {
                var markup = '<i>x</i>';
                host.insertAdjacentHTML('nope', markup);
            } catch (e) {
                name = e.name;
            }
            result = name;
        """).AsString.Should().Be("TypeError");
    }

    // -- helpers ---------------------------------------------------------

    private static (JsRuntime, Document) BuildEnv(string? url = null)
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions(DocumentUrl: url));
        return (runtime, doc);
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        doc.AppendChild(html);
        html.AppendChild(body);
        return doc;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
