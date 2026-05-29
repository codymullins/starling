using Starling.RegExp;
using Starling.Js.Runtime;

namespace Starling.Js.Intrinsics;

/// <summary>
/// §22.2.9 RegExp String Iterator — the iterator returned by
/// <c>String.prototype.matchAll(re)</c>. Wraps a compiled regex + the input
/// string and walks one match per <c>.next()</c> call. Inherits from
/// %IteratorPrototype% so <c>iter[Symbol.iterator]()</c> returns the same
/// iterator (one-shot).
/// </summary>
/// <remarks>
/// We exec the underlying <see cref="CompiledRegex"/> directly rather than
/// re-routing through <c>RegExp.prototype.exec</c> so the iterator is
/// hermetic — a user-visible monkey-patch of <c>exec</c> would not change
/// matchAll's behavior (acceptable simplification; the spec's "let result be
/// ? RegExpExec(R, S)" route is the same observable in the common path).
/// </remarks>
internal sealed class JsRegExpStringIterator : JsObject
{
    private readonly JsRegExp _regex;
    private readonly string _input;
    private readonly bool _global;
    private readonly bool _unicode;
    private bool _done;
    private int _cursor;

    public JsRegExpStringIterator(JsRealm realm, JsRegExp regex, string input, bool global, bool unicode)
        : base(realm.IteratorPrototype)
    {
        _regex = regex;
        _input = input;
        _global = global;
        _unicode = unicode;
        _cursor = 0;

        // Install .next as an own property so we don't need a dedicated
        // %RegExpStringIteratorPrototype% slot in JsRealm. %IteratorPrototype%
        // already provides [@@iterator] returning this, satisfying the
        // it[Symbol.iterator]() === it invariant.
        var nextFn = new JsNativeFunction(realm, "next", 0,
            (thisV, _) => thisV.IsObject && thisV.AsObject is JsRegExpStringIterator it
                ? it.Next(realm)
                : throw new JsThrow(realm.NewTypeError("RegExp String Iterator.prototype.next called on incompatible receiver")),
            isConstructor: false);
        DefineOwnProperty("next",
            PropertyDescriptor.BuiltinMethod(JsValue.Object(nextFn)));
    }

    public JsValue Next(JsRealm realm)
    {
        if (_done) return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        var m = _regex.Compiled.Exec(_input, _cursor);
        if (m is null)
        {
            _done = true;
            return IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true);
        }
        // Advance cursor; guard against zero-width matches.
        _cursor = m.End;
        if (m.End == m.Start)
            _cursor = RegExpCtor.AdvanceStringIndex(_input, _cursor, _unicode);
        if (!_global) _done = true;
        var matchArr = RegExpCtor.BuildMatchArrayForIterator(realm, _regex, m);
        return IteratorIntrinsics.MakeResult(realm, JsValue.Object(matchArr), done: false);
    }
}
