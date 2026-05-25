namespace Starling.Js.Bytecode;

/// <summary>
/// wp:M3-72 — compile-time description of the caller's variable environment at
/// a direct-eval call site. Emitted into the constant pool and referenced by
/// the <see cref="Opcode.DirectEval"/> instruction; the VM consults it together
/// with the live frame state (locals array + upvalue cells) to build the
/// runtime <c>EvalScope</c> that the eval'd code resolves caller bindings
/// against.
/// </summary>
public sealed class EvalScopeDescriptor
{
    /// <summary>How a caller binding is stored in the caller frame.</summary>
    public enum Kind
    {
        /// <summary>Plain local: live in the caller's locals[Slot] as a raw value.</summary>
        LocalSlot,
        /// <summary>Captured local: the caller's locals[Slot] holds a shared Cell.</summary>
        LocalCell,
        /// <summary>Captured outer binding: the caller's upvalues[Index] is a Cell.</summary>
        Upvalue,
    }

    public readonly record struct Binding(string Name, Kind Kind, int Index, bool IsLexical);

    public IReadOnlyList<Binding> Bindings { get; }

    public EvalScopeDescriptor(IReadOnlyList<Binding> bindings) => Bindings = bindings;
}
