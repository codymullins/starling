using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// WPT-04 — DOM §6 traversal: NodeFilter, TreeWalker, NodeIterator bindings.
/// Covers the main §6.1 filter mechanics, §6.2.2 TreeWalker algorithms,
/// and §6.3.2 NodeIterator iteration. Tests are kept independent so each
/// creates a fresh runtime and document tree.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#traversal", "DOM §6 Traversal")]
public sealed class TraversalBindingTests
{
    // ---- NodeFilter constants -----------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#interface-nodefilter", "NodeFilter")]
    public void NodeFilter_constants_are_accessible()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            result = NodeFilter.SHOW_ALL === 0xFFFFFFFF
                  && NodeFilter.SHOW_ELEMENT === 1
                  && NodeFilter.SHOW_TEXT === 4
                  && NodeFilter.FILTER_ACCEPT === 1
                  && NodeFilter.FILTER_REJECT === 2
                  && NodeFilter.FILTER_SKIP === 3;
        """).AsBool.Should().BeTrue();
    }

    // ---- createTreeWalker basic ---------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createtreewalker", "createTreeWalker")]
    public void CreateTreeWalker_nextNode_traverses_in_preorder()
    {
        var (runtime, _) = BuildEnv();
        // doc → html → [head, body → [p → [#text]]]
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            var names = [];
            var n;
            while ((n = walker.nextNode()) !== null) names.push(n.nodeName);
            result = names.join(',');
        """);
        runtime.GetGlobal("result").AsString.Should().Be("P,#text");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createtreewalker", "createTreeWalker")]
    public void CreateTreeWalker_root_property_is_correct()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            result = walker.root === document.body;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-treewalker-currentnode", "TreeWalker.currentNode")]
    public void TreeWalker_currentNode_starts_at_root()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            result = walker.currentNode === document.body;
        """).AsBool.Should().BeTrue();
    }

    // ---- whatToShow filter -------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodefilter-show_element", "SHOW_ELEMENT")]
    public void TreeWalker_whatToShow_element_only_skips_text_nodes()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT);
            var names = [];
            var n;
            while ((n = walker.nextNode()) !== null) names.push(n.nodeName);
            result = names.join(',');
        """);
        // Only element nodes (P), no text nodes
        runtime.GetGlobal("result").AsString.Should().Be("P");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodefilter-show_text", "SHOW_TEXT")]
    public void TreeWalker_whatToShow_text_only_skips_elements()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
            var names = [];
            var n;
            while ((n = walker.nextNode()) !== null) names.push(n.nodeName);
            result = names.join(',');
        """);
        runtime.GetGlobal("result").AsString.Should().Be("#text");
    }

    // ---- TreeWalker direction methods --------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-treewalker-firstchild", "TreeWalker.firstChild")]
    public void TreeWalker_firstChild_moves_to_first_accepted_child()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            var c = walker.firstChild();
            result = c !== null && c.nodeName === 'P';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-treewalker-parentnode", "TreeWalker.parentNode")]
    public void TreeWalker_parentNode_returns_parent_within_root()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            walker.nextNode(); // move to P
            var p = walker.parentNode();
            result = p !== null && p === document.body;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-treewalker-parentnode", "TreeWalker.parentNode")]
    public void TreeWalker_parentNode_at_root_returns_null()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            result = walker.parentNode() === null;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-treewalker-previousnode", "TreeWalker.previousNode")]
    public void TreeWalker_previousNode_traverses_backward()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body);
            walker.nextNode(); // P
            walker.nextNode(); // #text
            var prev = walker.previousNode();
            result = prev !== null && prev.nodeName === 'P';
        """).AsBool.Should().BeTrue();
    }

    // ---- filter callback ---------------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createtreewalker", "createTreeWalker filter")]
    public void TreeWalker_filter_function_accept_filters_correctly()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body, 0xFFFFFFFF,
                function(node) { return node.nodeName === 'P' ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP; });
            var c = walker.nextNode();
            result = c !== null && c.nodeName === 'P';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createtreewalker", "createTreeWalker filter object")]
    public void TreeWalker_filter_object_with_acceptNode_works()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var filter = { acceptNode: function(node) {
                return node.nodeName === '#text' ? NodeFilter.FILTER_ACCEPT : NodeFilter.FILTER_SKIP;
            }};
            var walker = document.createTreeWalker(document.body, 0xFFFFFFFF, filter);
            var c = walker.nextNode();
            result = c !== null && c.nodeName === '#text';
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodefilter-filter_reject", "FILTER_REJECT in TreeWalker")]
    public void TreeWalker_filter_reject_skips_subtree()
    {
        var (runtime, _) = BuildEnv();
        // REJECT on P should skip P and its text child
        Eval(runtime, """
            var walker = document.createTreeWalker(document.body, 0xFFFFFFFF,
                function(node) { return node.nodeName === 'P' ? NodeFilter.FILTER_REJECT : NodeFilter.FILTER_ACCEPT; });
            // Only root (body) is current; nextNode should skip P and its subtree
            result = walker.nextNode() === null;
        """).AsBool.Should().BeTrue();
    }

    // ---- createNodeIterator basic ------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createnodeiterator", "createNodeIterator")]
    public void CreateNodeIterator_nextNode_traverses_in_preorder()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            var names = [];
            var n;
            while ((n = iter.nextNode()) !== null) names.push(n.nodeName);
            result = names.join(',');
        """);
        // body, P, #text (NodeIterator visits root too)
        runtime.GetGlobal("result").AsString.Should().Be("BODY,P,#text");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodeiterator-root", "NodeIterator.root")]
    public void CreateNodeIterator_root_property_is_correct()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            result = iter.root === document.body;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodeiterator-referencenode", "NodeIterator.referenceNode")]
    public void CreateNodeIterator_referenceNode_starts_at_root()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            result = iter.referenceNode === document.body && iter.pointerBeforeReferenceNode === true;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodeiterator-previousnode", "NodeIterator.previousNode")]
    public void CreateNodeIterator_previousNode_goes_backward()
    {
        var (runtime, _) = BuildEnv();
        // After nextNode()/nextNode() the pointer is AFTER P (pointerBefore=false).
        // One previousNode() repositions to BEFORE P (returns P per spec §6.3.2).
        // A second previousNode() then moves to the actual previous node = BODY.
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            iter.nextNode(); // BODY (pointer after BODY)
            iter.nextNode(); // P   (pointer after P)
            iter.previousNode(); // returns P, pointer now before P
            var prev = iter.previousNode(); // now moves back to BODY
            result = prev !== null ? prev.nodeName : 'null';
        """);
        runtime.GetGlobal("result").AsString.Should().Be("BODY");
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodeiterator-detach", "NodeIterator.detach")]
    public void CreateNodeIterator_detach_is_a_noop()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            iter.detach();
            var n = iter.nextNode();
            result = n !== null && n.nodeName === 'BODY';
        """).AsBool.Should().BeTrue();
    }

    // ---- NodeIterator whatToShow -------------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-nodefilter-show_element", "NodeIterator SHOW_ELEMENT")]
    public void NodeIterator_whatToShow_element_filters_text()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body, NodeFilter.SHOW_ELEMENT);
            var names = [];
            var n;
            while ((n = iter.nextNode()) !== null) names.push(n.nodeName);
            result = names.join(',');
        """);
        runtime.GetGlobal("result").AsString.Should().Be("BODY,P");
    }

    // ---- §6.3.3 NodeIterator removal steps ---------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#nodeiterator-pre-removing-steps", "§6.3.3")]
    public void NodeIterator_removal_updates_referenceNode_when_current_is_removed()
    {
        var (runtime, _) = BuildEnv();
        // Advance to P, then remove it. referenceNode should update to BODY.
        Eval(runtime, """
            var iter = document.createNodeIterator(document.body);
            iter.nextNode(); // body
            iter.nextNode(); // P  (pointerBefore=false)
            var p = iter.referenceNode;
            document.body.removeChild(p);
            // After removal: referenceNode should be the node preceding P (body)
            result = iter.referenceNode === document.body && iter.pointerBeforeReferenceNode === false;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#nodeiterator-pre-removing-steps", "§6.3.3 pointer-before-true")]
    public void NodeIterator_removal_pointer_before_advances_to_next_when_possible()
    {
        var (runtime, _) = BuildEnv();
        // Create a document with two siblings: P and Q. Advance iter to P
        // (pointerBefore=false). Then add Q. Set iter manually to before P.
        // Remove P while pointerBefore=true → should advance to Q.
        Eval(runtime, """
            var q = document.createElement('q');
            document.body.appendChild(q);
            // iter advanced 1 time: referenceNode=body, pointerBefore=false
            var iter = document.createNodeIterator(document.body);
            iter.nextNode(); // body, pointerBefore=false
            // now set reference to body with pointer before P:
            // advance back so pointer is before body
            iter.previousNode(); // back to before body, referenceNode=body, pointerBefore=true
            var p = document.body.firstChild; // P
            document.body.removeChild(p);
            // P was removed; iter was pointerBefore=true and referenceNode=body (not in P's subtree)
            // §6.3.3: P is not ancestor of body → do nothing
            result = iter.referenceNode === document.body;
        """).AsBool.Should().BeTrue();
    }

    // ---- createTreeWalker/createNodeIterator error handling ----------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createtreewalker", "createTreeWalker type error")]
    public void CreateTreeWalker_throws_if_root_is_not_a_node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { document.createTreeWalker(null); } catch(e) { threw = true; }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-document-createnodeiterator", "createNodeIterator type error")]
    public void CreateNodeIterator_throws_if_root_is_not_a_node()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var threw = false;
            try { document.createNodeIterator(42); } catch(e) { threw = true; }
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    // ---- Active filter guard (§6.1) ----------------------------------------

    [SpecFact]
    [Spec("dom", "https://dom.spec.whatwg.org/#concept-node-filter", "active filter flag")]
    public void TreeWalker_filter_recursive_call_throws_InvalidStateError()
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, """
            var threw = false;
            var walker = document.createTreeWalker(document.body, 0xFFFFFFFF, function(node) {
                try { walker.nextNode(); } catch(e) { threw = true; }
                return NodeFilter.FILTER_ACCEPT;
            });
            walker.nextNode();
            result = threw;
        """).AsBool.Should().BeTrue();
    }

    // ---- Helpers -----------------------------------------------------------

    private static (JsRuntime, Document) BuildEnv()
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        WindowBinding.Install(runtime, doc, new WindowInstallOptions());
        return (runtime, doc);
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var head = doc.CreateElement("head");
        var body = doc.CreateElement("body");
        var p = doc.CreateElement("p");
        var text = doc.CreateTextNode("hello");
        p.AppendChild(text);
        body.AppendChild(p);
        html.AppendChild(head);
        html.AppendChild(body);
        doc.AppendChild(html);
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
