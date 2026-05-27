using AwesomeAssertions;
using Starling.Dom;
using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;
using Starling.Spec;

namespace Starling.Bindings.Tests;

/// <summary>
/// Cross-cutting smoke tests for the document.create*() family. Every node
/// the spec says a Document creates must inherit from Node and report the
/// creating Document as its ownerDocument. Stale duplicate bindings that
/// build bare wrapper objects via `new Foo(...)` instead of delegating to
/// the Document fail these invariants — that's the regression class this
/// covers.
/// </summary>
[TestClass]
[Spec("dom", "https://dom.spec.whatwg.org/#interface-document", "4.5")]
public sealed class DocumentCreatorOwnershipTests
{
    [TestMethod]
    [Spec("dom", "https://dom.spec.whatwg.org/#dom-node-ownerdocument", "Node.ownerDocument")]
    [DataRow("createAttribute", "'x'")]
    [DataRow("createAttributeNS", "null, 'x'")]
    [DataRow("createElement", "'div'")]
    [DataRow("createElementNS", "null, 'div'")]
    [DataRow("createTextNode", "''")]
    [DataRow("createComment", "''")]
    [DataRow("createCDATASection", "''")]
    [DataRow("createProcessingInstruction", "'t', 'd'")]
    [DataRow("createDocumentFragment", "")]
    public void Document_create_returns_node_owned_by_document(string method, string args)
    {
        var (runtime, _) = BuildEnv();
        Eval(runtime, $$"""
            var n = document.{{method}}({{args}});
            result = (n instanceof Node) && n.ownerDocument === document;
        """).AsBool.Should().BeTrue($"document.{method}({args}) must return a Node owned by the document");
    }

    // ---- Helpers ------------------------------------------------------------

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
