using FluentAssertions;
using Tessera.Dom;
using Tessera.Js.Bytecode;
using Tessera.Js.Parse;
using Tessera.Js.Runtime;
using Xunit;

namespace Tessera.Bindings.Tests;

public sealed class DomBindingHostTests
{
    [Fact]
    public void Js_can_read_and_update_dom_text_by_id()
    {
        var doc = BuildDocument();
        var runtime = new JsRuntime();
        new DomBindingHost(doc).Install(runtime);

        Eval(runtime, "setElementTextById('message', getElementTextById('message') + ' world');");

        doc.GetElementById("message")!.TextContent.Should().Be("hello world");
    }

    [Fact]
    public void Js_can_dispatch_click_by_id()
    {
        var doc = BuildDocument();
        var clicked = false;
        doc.GetElementById("button")!.AddEventListener("click", _ => clicked = true);

        var runtime = new JsRuntime();
        new DomBindingHost(doc).Install(runtime);

        Eval(runtime, "clicked = dispatchClickById('button');");
        runtime.GetGlobal("clicked").AsBool.Should().BeTrue();
        clicked.Should().BeTrue();
    }

    private static Document BuildDocument()
    {
        var doc = new Document();
        var html = doc.CreateElement("html");
        var body = doc.CreateElement("body");
        var message = doc.CreateElement("p");
        message.SetAttribute("id", "message");
        message.TextContent = "hello";
        var button = doc.CreateElement("button");
        button.SetAttribute("id", "button");
        button.TextContent = "go";

        doc.AppendChild(html);
        html.AppendChild(body);
        body.AppendChild(message);
        body.AppendChild(button);
        return doc;
    }

    private static JsValue Eval(JsRuntime runtime, string source)
    {
        var program = new JsParser(source).ParseProgram();
        var chunk = JsCompiler.Compile(program);
        return new JsVm(runtime).Run(chunk);
    }
}
