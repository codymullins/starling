using Tessera.Dom;
using Tessera.Dom.Events;
using Tessera.Js.Runtime;

namespace Tessera.Bindings;

/// <summary>
/// Minimal host bridge that exposes a tiny DOM surface to the current JS
/// runtime. Full WebIDL bindings land later; this gives simple fixtures a
/// tested path to read/mutate DOM text and dispatch click events.
/// </summary>
public sealed class DomBindingHost
{
    private readonly Document _document;

    public DomBindingHost(Document document)
    {
        _document = document ?? throw new ArgumentNullException(nameof(document));
    }

    public void Install(JsRuntime runtime)
    {
        ArgumentNullException.ThrowIfNull(runtime);

        runtime.RegisterGlobal("getElementTextById", args =>
        {
            var id = args.Length > 0 ? JsValue.ToStringValue(args[0]) : "";
            return _document.GetElementById(id) is { } element
                ? JsValue.String(element.TextContent)
                : JsValue.Undefined;
        });

        runtime.RegisterGlobal("setElementTextById", args =>
        {
            if (args.Length < 2) return JsValue.False;
            var id = JsValue.ToStringValue(args[0]);
            var text = JsValue.ToStringValue(args[1]);
            if (_document.GetElementById(id) is not { } element)
                return JsValue.False;
            element.TextContent = text;
            return JsValue.True;
        });

        runtime.RegisterGlobal("dispatchClickById", args =>
        {
            if (args.Length == 0) return JsValue.False;
            var id = JsValue.ToStringValue(args[0]);
            if (_document.GetElementById(id) is not { } element)
                return JsValue.False;
            return JsValue.Boolean(element.DispatchEvent(
                new MouseEvent("click", new EventInit(Bubbles: true, Cancelable: true))));
        });
    }
}
