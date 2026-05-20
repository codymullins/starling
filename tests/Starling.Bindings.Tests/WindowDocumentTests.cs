using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
namespace Starling.Bindings.Tests;

/// <summary>
/// B5-1 tests for the Window / document / EventTarget JS bindings.
/// </summary>
[TestClass]
public sealed class WindowDocumentTests
{
    [TestMethod]
    public void Window_equals_global_this()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = (window === globalThis) && (self === window);")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Document_is_stable_identity()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = window.document === document && document === document;")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Document_create_element_tag_name_is_uppercase()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = document.createElement('div').tagName;")
            .AsString.Should().Be("DIV");
    }

    [TestMethod]
    public void Element_id_attribute_round_trips()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "var e = document.createElement('div'); e.setAttribute('id', 'foo'); result = e.getAttribute('id');")
            .AsString.Should().Be("foo");
        Eval(runtime, "var e = document.createElement('div'); e.setAttribute('id', 'foo'); result = e.id;")
            .AsString.Should().Be("foo");
        Eval(runtime, "var e = document.createElement('div'); e.id = 'bar'; result = e.getAttribute('id');")
            .AsString.Should().Be("bar");
    }

    [TestMethod]
    public void Document_body_append_child_makes_node_part_of_children()
    {
        var (runtime, doc) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('span');
            e.id = 'hello';
            document.body.appendChild(e);
            result = document.body.children.length;
        """).AsNumber.Should().Be(1);
        doc.GetElementById("hello").Should().NotBeNull();
        // B5-1-followup: children is a real JsArray, not a plain "array-like".
        Eval(runtime, "result = Array.isArray(document.body.children);")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Add_event_listener_fires_with_this_equal_to_target()
    {
        var (runtime, _) = BuildEnv();
        var errors = new List<string>();
        runtime.Realm.ConsoleSink = (lvl, msg) => errors.Add($"{lvl} {msg}");
        Eval(runtime, """
            var btn = document.createElement('button');
            document.body.appendChild(btn);
            var fired = 0;
            var ttb = false; var ctb = false; var tgt = false;
            btn.addEventListener('click', function (e) {
                fired = fired + 1;
                ttb = (this === btn);
                ctb = (e.currentTarget === btn);
                tgt = (e.target === btn);
            });
            btn.dispatchEvent(new Event('click'));
        """);
        errors.Should().BeEmpty();
        runtime.GetGlobal("fired").AsNumber.Should().Be(1);
        runtime.GetGlobal("ttb").AsBool.Should().BeTrue("this should equal btn");
        runtime.GetGlobal("ctb").AsBool.Should().BeTrue("e.currentTarget should equal btn");
        runtime.GetGlobal("tgt").AsBool.Should().BeTrue("e.target should equal btn");
    }

    [TestMethod]
    public void Remove_event_listener_actually_unregisters()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var btn = document.createElement('button');
            document.body.appendChild(btn);
            var count = 0;
            var handler = function () { count = count + 1; };
            btn.addEventListener('click', handler);
            btn.removeEventListener('click', handler);
            btn.dispatchEvent(new Event('click'));
            result = count;
        """).AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void Once_option_fires_only_once()
    {
        var (runtime, _) = BuildEnv();
        // `count` is a global (no enclosing block scope captured here).
        Eval(runtime, """
            count = 0;
            var btn = document.createElement('button');
            document.body.appendChild(btn);
            btn.addEventListener('x', function () { count = count + 1; }, { once: true });
            btn.dispatchEvent(new Event('x'));
            btn.dispatchEvent(new Event('x'));
        """);
        runtime.GetGlobal("count").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Event_object_exposes_spec_fields()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            captured_type = '';
            captured_defaultPrevented = false;
            var btn = document.createElement('button');
            document.body.appendChild(btn);
            btn.addEventListener('boom', function (e) {
                e.preventDefault();
                captured_type = e.type;
                captured_defaultPrevented = e.defaultPrevented;
            });
            var ev = new Event('boom', { cancelable: true });
            btn.dispatchEvent(ev);
        """);
        runtime.GetGlobal("captured_type").AsString.Should().Be("boom");
        runtime.GetGlobal("captured_defaultPrevented").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Stop_propagation_halts_bubble()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var outer = document.createElement('div');
            var inner = document.createElement('span');
            outer.appendChild(inner);
            document.body.appendChild(outer);
            var outerCount = 0;
            outer.addEventListener('bubble', function () { outerCount = outerCount + 1; });
            inner.addEventListener('bubble', function (e) { e.stopPropagation(); });
            inner.dispatchEvent(new Event('bubble', { bubbles: true }));
            result = outerCount;
        """).AsNumber.Should().Be(0);
    }

    [TestMethod]
    public void Window_location_href_reflects_install_url()
    {
        var (runtime, _) = BuildEnv("https://example.com/path?q=1#frag");
        Eval(runtime, "result = window.location.href;")
            .AsString.Should().Be("https://example.com/path?q=1#frag");
        Eval(runtime, "result = window.location.protocol;")
            .AsString.Should().Be("https:");
        Eval(runtime, "result = window.location.pathname;")
            .AsString.Should().Be("/path");
        Eval(runtime, "result = window.location.search;")
            .AsString.Should().Be("?q=1");
        Eval(runtime, "result = window.location.hash;")
            .AsString.Should().Be("#frag");
    }

    [TestMethod]
    public void Document_get_element_by_id_returns_wrapper()
    {
        var (runtime, doc) = BuildEnv();
        var target = doc.CreateElement("div");
        target.SetAttribute("id", "needle");
        doc.Body!.AppendChild(target);
        Eval(runtime, "result = document.getElementById('needle').tagName;")
            .AsString.Should().Be("DIV");
        Eval(runtime, "result = document.getElementById('missing');")
            .IsNull.Should().BeTrue();
    }

    [TestMethod]
    public void Wrapper_identity_is_stable_via_caching()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var a = document.body;
            var b = document.body;
            result = a === b;
        """).AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Document_prototype_is_exposed_via_constructor()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = Object.getPrototypeOf(document) === Document.prototype;")
            .AsBool.Should().BeTrue();
        Eval(runtime, "result = Object.getPrototypeOf(document.body) === Element.prototype;")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Window_add_event_listener_fires_on_window_dispatch()
    {
        var (runtime, _) = BuildEnv();
        // Note: unqualified `addEventListener('x', fn)` doesn't fire here because
        // the VM passes `this = undefined` to plain calls (strict default), so
        // EventTarget.addEventListener can't resolve a host target. Real
        // browsers coerce `this` to globalThis in sloppy mode. Use `window.`.
        Eval(runtime, """
            fired = 0;
            window.addEventListener('hello', function () { fired = fired + 1; });
            window.dispatchEvent(new Event('hello'));
        """);
        runtime.GetGlobal("fired").AsNumber.Should().Be(1);
    }

    [TestMethod]
    public void Text_content_round_trips_through_js()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var p = document.createElement('p');
            p.textContent = 'hello world';
            document.body.appendChild(p);
            result = p.textContent;
        """).AsString.Should().Be("hello world");
    }

    [TestMethod]
    public void Class_name_setter_writes_class_attribute()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var e = document.createElement('div');
            e.className = 'one two';
            result = e.getAttribute('class');
        """).AsString.Should().Be("one two");
    }

    [TestMethod]
    public void Query_selector_supports_id_class_and_tag()
    {
        var (runtime, doc) = BuildEnv();
        var p = doc.CreateElement("p");
        p.SetAttribute("id", "intro");
        p.SetAttribute("class", "lead");
        doc.Body!.AppendChild(p);

        Eval(runtime, "result = document.querySelector('#intro').tagName;")
            .AsString.Should().Be("P");
        Eval(runtime, "result = document.querySelector('.lead').tagName;")
            .AsString.Should().Be("P");
        Eval(runtime, "result = document.querySelector('p').tagName;")
            .AsString.Should().Be("P");
        Eval(runtime, "result = document.querySelectorAll('p').length;")
            .AsNumber.Should().Be(1);
        // B5-1-followup: querySelectorAll returns a real JsArray.
        Eval(runtime, "result = Array.isArray(document.querySelectorAll('p'));")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Query_selector_supports_compound_combinator_and_attribute_selectors()
    {
        var (runtime, doc) = BuildEnv();
        BuildCard(doc);

        // Compound + child combinator.
        Eval(runtime, "result = document.querySelector('div.card > span.title').tagName;")
            .AsString.Should().Be("SPAN");
        // Descendant combinator.
        Eval(runtime, "result = document.querySelector('.card span').className;")
            .AsString.Should().Be("title");
        // Attribute presence + value.
        Eval(runtime, "result = document.querySelector('[data-x]').tagName;")
            .AsString.Should().Be("P");
        Eval(runtime, "result = document.querySelector('[data-role=\"panel\"]').tagName;")
            .AsString.Should().Be("DIV");
        // :nth-child (span is child 1, p is child 2).
        Eval(runtime, "result = document.querySelector('.card p:nth-child(2)').tagName;")
            .AsString.Should().Be("P");
        // Selector list, in tree order.
        Eval(runtime, "result = document.querySelectorAll('div.card span, div.card p').length;")
            .AsNumber.Should().Be(2);
    }

    [TestMethod]
    public void Element_matches_and_closest_use_full_selector_grammar()
    {
        var (runtime, doc) = BuildEnv();
        BuildCard(doc);

        Eval(runtime, "result = document.querySelector('span').matches('.title');")
            .AsBool.Should().BeTrue();
        Eval(runtime, "result = document.querySelector('span').matches('.nope');")
            .AsBool.Should().BeFalse();
        Eval(runtime, "result = document.querySelector('span.title').closest('div.card').tagName;")
            .AsString.Should().Be("DIV");
        Eval(runtime, "result = document.querySelector('span').closest('.nope') === null;")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Query_selector_throws_syntax_error_on_invalid_selector()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            function err(sel) {
                try { document.querySelector(sel); return ''; }
                catch (e) { return e.name; }
            }
            result = err('') + '|' + err('div::before.x');
        """).AsString.Should().Be("SyntaxError|SyntaxError");
    }

    [TestMethod]
    public void Navigator_user_agent_is_exposed()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, "result = typeof navigator.userAgent === 'string' && navigator.userAgent.length > 0;")
            .AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Event_target_is_set_during_dispatch()
    {
        var (runtime, _) = BuildEnv();
        var errors = new List<string>();
        runtime.Realm.ConsoleSink = (lvl, msg) => errors.Add($"{lvl} {msg}");
        Eval(runtime, """
            var btn = document.createElement('button');
            document.body.appendChild(btn);
            var fired = 0;
            var ok = false;
            btn.addEventListener('t', function (e) {
                fired = fired + 1;
                ok = (e.target === btn);
            });
            btn.dispatchEvent(new Event('t'));
        """);
        errors.Should().BeEmpty();
        runtime.GetGlobal("fired").AsNumber.Should().Be(1);
        runtime.GetGlobal("ok").AsBool.Should().BeTrue();
    }

    [TestMethod]
    public void Host_side_listener_added_in_csharp_fires_on_dispatch_from_js()
    {
        var (runtime, doc) = BuildEnv();
        var hit = 0;
        var elt = doc.CreateElement("p");
        elt.SetAttribute("id", "x");
        doc.Body!.AppendChild(elt);
        elt.AddEventListener("ping", _ => hit++);

        Eval(runtime, "document.getElementById('x').dispatchEvent(new Event('ping'));");
        hit.Should().Be(1);
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

    /// <summary>Appends <c>&lt;div class="card" data-role="panel"&gt;&lt;span class="title"/&gt;&lt;p data-x="1"/&gt;&lt;/div&gt;</c> to the body.</summary>
    private static void BuildCard(Document doc)
    {
        var card = doc.CreateElement("div");
        card.SetAttribute("class", "card");
        card.SetAttribute("data-role", "panel");
        var title = doc.CreateElement("span");
        title.SetAttribute("class", "title");
        var para = doc.CreateElement("p");
        para.SetAttribute("data-x", "1");
        card.AppendChild(title);
        card.AppendChild(para);
        doc.Body!.AppendChild(card);
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        new JsVm(runtime).Run(chunk);
        return runtime.GetGlobal("result");
    }
}
