using Starling.RegExp;

namespace Starling.Js.Runtime;

/// <summary>
/// ES2024 §22.2.7 RegExp instance — an exotic object holding a compiled regex
/// plus the mutable <c>lastIndex</c> slot. Inherits from
/// <see cref="JsRealm.RegExpPrototype"/>.
/// </summary>
public sealed class JsRegExp : JsObject
{
    public CompiledRegex Compiled { get; }
    public string Source => Compiled.Source;
    public RegexFlags Flags => Compiled.Flags;

    public JsRegExp(JsRealm realm, CompiledRegex compiled) : base(realm.RegExpPrototype)
    {
        Compiled = compiled;
        // lastIndex is writable, non-enumerable, non-configurable per §22.2.7.1.
        DefineOwnProperty("lastIndex",
            PropertyDescriptor.Data(JsValue.Number(0), writable: true, enumerable: false, configurable: false));
    }

    public double LastIndex
    {
        get
        {
            var v = Get("lastIndex");
            return v.IsNumber ? v.AsNumber : JsValue.ToNumber(v);
        }
        set => Set("lastIndex", JsValue.Number(value));
    }

    public override string ToString() => $"/{Source}/{RegexFlagParser.ToFlagString(Flags)}";
}
