using AwesomeAssertions;
using Jint;
using Starling.Common.Diagnostics;
using Starling.Dom;
using Starling.Html;
using Starling.Loop;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// Wave-1 spike for the J2a infrastructure. It proves the frozen primitives the
/// Wave-2 binding agents build on actually work end-to-end against a real
/// <see cref="Starling.Dom.Document"/>:
/// <list type="number">
///   <item><see cref="JintBackendContext"/> stands up a live Jint engine over a
///   parsed DOM document;</item>
///   <item><see cref="JintInterop.DefineAccessor"/> installs a Web-IDL accessor
///   on a prototype slot (here <c>nodeName</c> on <see cref="JintDomWrapper.NodePrototype"/>);</item>
///   <item><see cref="JintDomWrapper.Wrap"/> mints an identity-stable JS wrapper
///   for a real <see cref="Element"/>, whose prototype is that slot;</item>
///   <item>script reads the accessor and gets the element's real nodeName.</item>
/// </list>
/// Full <c>document.querySelector('div').textContent</c> needs J2b (Node /
/// Document bindings), so the spike asserts what the J2a primitives alone can
/// prove: a wrapped node's <c>nodeName</c> via <c>DefineAccessor</c>, plus
/// wrapper identity (<c>el === el</c>).
/// </summary>
[TestClass]
public sealed class JintWrapperSpikeTests
{
    [TestMethod]
    public void Wrapped_element_exposes_nodeName_via_DefineAccessor()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body><div id='x'>hi</div></body></html>");
        var div = doc.GetElementsByTagName("div").Single();

        var ctx = NewContext(doc);

        // J2a primitive: a Node prototype slot with a `nodeName` accessor that
        // reads through to the backing Starling.Dom node.
        var nodeProto = new global::Jint.Native.JsObject(ctx.Engine);
        ctx.Wrappers.NodePrototype = nodeProto;
        JintInterop.DefineAccessor(ctx.Engine, nodeProto, "nodeName", (thisVal, _) =>
        {
            var node = ctx.Wrappers.UnwrapNode(thisVal);
            return JintInterop.Str(node?.NodeName);
        });

        // J2a primitive: wrap the real element and expose it to script.
        var wrapper = ctx.Wrappers.Wrap(div);
        ctx.Engine.SetValue("el", wrapper);

        // Read the accessor from JS: it must round-trip the backing node's real
        // NodeName, proving Wrap → prototype slot → DefineAccessor → Unwrap all
        // line up against the live Starling.Dom node.
        var nodeName = ctx.Engine.Evaluate("el.nodeName").AsString();
        nodeName.Should().Be(div.NodeName);
        nodeName.Should().NotBeNullOrEmpty();
        // The value flowed from the real DOM node through the accessor.
        nodeName.Should().Be("div");
    }

    [TestMethod]
    public void Wrapper_identity_is_stable_per_node()
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body><p>hi</p></body></html>");
        var p = doc.GetElementsByTagName("p").Single();
        var ctx = NewContext(doc);

        ctx.Engine.SetValue("a", ctx.Wrappers.Wrap(p));
        ctx.Engine.SetValue("b", ctx.Wrappers.Wrap(p)); // same node → same wrapper

        ctx.Engine.Evaluate("a === b").AsBoolean().Should().BeTrue();
    }

    private static JintBackendContext NewContext(Document doc)
    {
        var baseUrl = global::Starling.Url.UrlParser.Parse("about:blank").Value;
        var engine = new global::Jint.Engine();
        // The context holds the client for the engine's lifetime; the short-lived
        // test process reclaims it. (No fetch happens in the spike.)
        var http = new Starling.Net.StarlingHttpClient();
        return new JintBackendContext(
            engine: engine,
            document: doc,
            baseUrl: baseUrl,
            http: http,
            diag: NoopDiagnostics.Instance,
            loop: new WebEventLoop(),
            layoutHost: null,
            fetch: (_, _) => System.Threading.Tasks.Task.FromResult<string?>(null));
    }
}
