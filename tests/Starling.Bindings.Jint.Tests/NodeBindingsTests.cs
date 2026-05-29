using AwesomeAssertions;
using Jint;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// J2b coverage: parse a small HTML document into a real
/// <see cref="Starling.Dom.Document"/>, install the full Jint binding set
/// (<see cref="JintBindings.InstallAll(JintBackendContext)"/>), then run JS
/// through the Jint engine exercising the Node / Element / Document surface —
/// querySelector*, createElement/appendChild, textContent, classList,
/// setAttribute/dataset, and innerHTML — asserting both the results and wrapper
/// identity (<c>document.body === document.body</c>).
/// </summary>
[TestClass]
public sealed class NodeBindingsTests
{
    private const string Html =
        "<!doctype html><html><head><title>Hi</title></head>" +
        "<body><div id='main' class='a b'><p class='lead'>hello</p><p>two</p></div></body></html>";

    [TestMethod]
    public void QuerySelector_reads_tag_and_text()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.querySelector('#main').tagName").AsString().Should().Be("DIV");
        engine.Evaluate("document.querySelector('.lead').textContent").AsString().Should().Be("hello");
        engine.Evaluate("document.querySelectorAll('#main p').length").AsNumber().Should().Be(2);
        engine.Evaluate("document.getElementById('main').id").AsString().Should().Be("main");
    }

    [TestMethod]
    public void Invalid_selector_throws_SyntaxError()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("(function(){ try { document.querySelector('###'); return 'no'; } catch (e) { return e.name; } })()")
            .AsString().Should().Be("SyntaxError");
    }

    [TestMethod]
    public void CreateElement_appendChild_and_textContent_mutate_the_real_dom()
    {
        var (engine, doc) = NewSession(Html);
        engine.Evaluate(
            "var s = document.createElement('span'); s.textContent = 'world'; document.body.appendChild(s);");

        // Observable from JS …
        engine.Evaluate("document.querySelector('span').textContent").AsString().Should().Be("world");
        // … and reflected in the underlying Starling.Dom tree.
        var span = doc.GetElementsByTagName("span").Single();
        span.TextContent.Should().Be("world");
        span.ParentNode.Should().Be(doc.Body);
    }

    [TestMethod]
    public void ClassList_add_remove_toggle_contains()
    {
        var (engine, doc) = NewSession(Html);
        var main = doc.GetElementById("main")!;

        engine.Evaluate("document.querySelector('#main').classList.contains('a')").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelector('#main').classList.add('c')");
        engine.Evaluate("document.querySelector('#main').classList.toggle('a')"); // removes 'a'
        engine.Evaluate("document.querySelector('#main').classList.remove('b')");

        main.GetAttribute("class").Should().Be("c");
        engine.Evaluate("document.querySelector('#main').classList.contains('c')").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelector('#main').classList.contains('a')").AsBoolean().Should().BeFalse();
    }

    [TestMethod]
    public void SetAttribute_and_dataset_round_trip()
    {
        var (engine, doc) = NewSession(Html);
        engine.Evaluate("document.querySelector('#main').setAttribute('data-user-id', '42')");

        doc.GetElementById("main")!.GetAttribute("data-user-id").Should().Be("42");
        engine.Evaluate("document.querySelector('#main').getAttribute('data-user-id')").AsString().Should().Be("42");
        // dataset camelCase <-> data-kebab-case.
        engine.Evaluate("document.querySelector('#main').dataset.userId").AsString().Should().Be("42");
        engine.Evaluate("document.querySelector('#main').dataset.userId = '7'");
        doc.GetElementById("main")!.GetAttribute("data-user-id").Should().Be("7");
    }

    [TestMethod]
    public void InnerHTML_get_and_set_through_real_parser()
    {
        var (engine, doc) = NewSession(Html);

        engine.Evaluate("document.querySelector('#main').innerHTML = '<a href=\"/x\">link</a>'");
        engine.Evaluate("document.querySelector('#main a').tagName").AsString().Should().Be("A");
        engine.Evaluate("document.querySelector('#main').innerHTML").AsString().Should().Be("<a href=\"/x\">link</a>");

        // The DOM tree actually changed: previous <p> children are gone.
        doc.GetElementById("main")!.DescendantElements().Select(e => e.LocalName)
            .Should().ContainSingle().Which.Should().Be("a");
    }

    [TestMethod]
    public void Wrapper_identity_is_stable()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.body === document.body").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.querySelector('#main') === document.getElementById('main')").AsBoolean().Should().BeTrue();
        // A node created in JS keeps identity once inserted + re-queried.
        engine.Evaluate("var n = document.createElement('i'); document.body.appendChild(n); n === document.querySelector('i')")
            .AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Node_constants_and_instanceof_resolve()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("Node.ELEMENT_NODE").AsNumber().Should().Be(1);
        engine.Evaluate("document.body.nodeType").AsNumber().Should().Be(1);
        engine.Evaluate("document.body instanceof Element").AsBoolean().Should().BeTrue();
        engine.Evaluate("document.body instanceof Node").AsBoolean().Should().BeTrue();
    }

    [TestMethod]
    public void Document_title_get_and_set()
    {
        var (engine, _) = NewSession(Html);
        engine.Evaluate("document.title").AsString().Should().Be("Hi");
        engine.Evaluate("document.title = 'Bye'");
        engine.Evaluate("document.title").AsString().Should().Be("Bye");
    }

    [TestMethod]
    public void FormData_constructs_from_successful_form_controls()
    {
        var (engine, _) = NewSession("""
            <!doctype html><html><body>
              <form id="f">
                <input name="q" value="hello world">
                <input type="checkbox" name="agree" value="yes" checked>
                <input type="checkbox" name="skip" value="no">
                <select name="ship"><option value="ground">Ground</option><option value="air" selected>Air</option></select>
              </form>
            </body></html>
            """);

        engine.Evaluate("Array.from(new FormData(document.getElementById('f')).entries()).map(p => p[0] + '=' + p[1]).join('&')")
            .AsString().Should().Be("q=hello world&agree=yes&ship=air");
    }

    [TestMethod]
    public void Form_controls_expose_validation_selection_and_autocomplete()
    {
        var (engine, _) = NewSession("""
            <!doctype html><html><body>
              <form id="f">
                <input id="q" name="q" required list="terms">
                <datalist id="terms"><option value="alpha"></option><option value="beta"></option></datalist>
              </form>
            </body></html>
            """);

        engine.Evaluate("""
            var q = document.getElementById('q');
            var before = q.validity.valueMissing + '/' + q.checkValidity();
            q.value = 'alphabet';
            q.setSelectionRange(0, 5, 'forward');
            document.getElementById('f').submit();
            before + '|' + q.validity.valid + '|' + q.selectionStart + ':' + q.selectionEnd + ':' + q.selectionDirection
              + '|' + q.autocompleteSuggestions().join(',');
            """).AsString().Should().Be("true/false|true|0:5:forward|alpha,beta,alphabet");
    }

    private static (global::Jint.Engine Engine, Document Doc) NewSession(string html)
    {
        var doc = HtmlParser.Parse(html);
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        var http = new Starling.Net.StarlingHttpClient();
        var ctx = new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: baseUrl,
            http: http,
            diag: NoopDiagnostics.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
        JintBindings.InstallAll(ctx);
        return (engine, doc);
    }
}
