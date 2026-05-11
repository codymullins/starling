using Tessera.Js.Bytecode;

namespace Tessera.Js.Runtime;

/// <summary>
/// User-defined JS function. Wraps a compiled <see cref="Chunk"/> plus the
/// declared parameter count. Called from the VM via the regular
/// <see cref="Opcode.Call"/> opcode — the dispatcher inspects the callee
/// object and pushes a new frame for <see cref="JsFunction"/>, vs. invoking
/// the native body for <see cref="JsNativeFunction"/>.
/// </summary>
/// <remarks>
/// <para>
/// Closures (wp:M3-04c) use <em>snapshot semantics</em>: at
/// <see cref="Opcode.MakeClosure"/> time, the parent frame pushes the
/// current values of each captured variable, and the VM constructs a
/// fresh <see cref="JsFunction"/> whose <see cref="Upvalues"/> array
/// holds those snapshots. Mutation through an upvalue (i.e. the inner
/// function reassigning a captured name and the parent observing it)
/// is deferred to wp:M3-04c2 with Cell-based slots.
/// </para>
/// <para>
/// A "template" JsFunction is what the compiler stuffs into the
/// constant pool — its <see cref="Upvalues"/> is empty and it is never
/// directly callable when the function has free captures. The runtime
/// instance with bound upvalues is built by <see cref="Opcode.MakeClosure"/>.
/// </para>
/// </remarks>
public sealed class JsFunction : JsObject
{
    public string Name { get; }
    public Chunk Body { get; }
    public int ArityDeclared { get; }
    /// <summary>Snapshotted captured values, in the order assigned by the
    /// compiler. Empty for plain (non-capturing) functions.</summary>
    public IReadOnlyList<JsValue> Upvalues { get; }

    public JsFunction(string name, Chunk body, int arityDeclared)
        : this(name, body, arityDeclared, Array.Empty<JsValue>())
    {
    }

    public JsFunction(string name, Chunk body, int arityDeclared, IReadOnlyList<JsValue> upvalues)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Body = body ?? throw new ArgumentNullException(nameof(body));
        ArityDeclared = arityDeclared;
        Upvalues = upvalues ?? throw new ArgumentNullException(nameof(upvalues));
    }

    public override string ToString()
        => $"function {Name}({ArityDeclared}) {{ [bytecode] }}";
}
