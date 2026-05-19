using System.Buffers.Binary;

namespace Tessera.Js.Bytecode;

/// <summary>
/// A bytecode chunk — bytes + constant pool + local-slot count.
/// </summary>
/// <remarks>
/// <para>
/// One chunk per compilation unit (script top-level body, function body,
/// etc.). The chunk is immutable from the VM's perspective; the compiler
/// builds it via <see cref="ChunkBuilder"/> and then freezes it.
/// </para>
/// <para>
/// Constants are boxed as object: doubles for NumericLiteral, strings for
/// StringLiteral, the singleton <see cref="JsBigIntPlaceholder"/> for
/// BigIntLiteral (decoded by the runtime when the type is needed).
/// </para>
/// </remarks>
public sealed class Chunk
{
    public byte[] Code { get; }
    public IReadOnlyList<object?> Constants { get; }
    public int LocalCount { get; }
    public string? Name { get; }

    public Chunk(byte[] code, IReadOnlyList<object?> constants, int localCount, string? name = null)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Constants = constants ?? throw new ArgumentNullException(nameof(constants));
        LocalCount = localCount;
        Name = name;
    }

    public override string ToString() => Disassembler.Disassemble(this);
}

/// <summary>
/// Constant-pool wrapper for a BigInt literal. The parser turns the lexeme
/// (decimal / 0x / 0b / 0o, with the trailing <c>n</c> stripped) into a
/// <see cref="System.Numerics.BigInteger"/> at AST-build time; the VM unboxes
/// it via <see cref="Tessera.Js.Runtime.JsValue.BigInt(System.Numerics.BigInteger)"/>
/// when <see cref="Opcode.LoadConst"/> dispatches on this record.
/// </summary>
public sealed record JsBigIntPlaceholder(System.Numerics.BigInteger Value)
{
    /// <summary>Back-compat ctor — parse a decimal digits string.</summary>
    public JsBigIntPlaceholder(string digits)
        : this(System.Numerics.BigInteger.Parse(digits,
            System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture)) { }

    /// <summary>Back-compat decimal-string view of <see cref="Value"/>.
    /// Used by the disassembler.</summary>
    public string Digits => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>
/// Mutable builder used by <c>JsCompiler</c>. Tracks the constant pool,
/// the byte stream, and the active local-slot count.
/// </summary>
public sealed class ChunkBuilder
{
    private readonly List<byte> _code = [];
    private readonly List<object?> _constants = [];
    private readonly Dictionary<string, int> _stringPool = new(StringComparer.Ordinal);
    public int LocalCount { get; private set; }

    public int Position => _code.Count;

    public int AddConstant(object? value)
    {
        if (value is string s && _stringPool.TryGetValue(s, out var idx)) return idx;
        var pos = _constants.Count;
        _constants.Add(value);
        if (value is string ss) _stringPool[ss] = pos;
        return pos;
    }

    public int ReserveLocal()
    {
        var slot = LocalCount;
        LocalCount++;
        return slot;
    }

    public void Emit(Opcode op) => _code.Add((byte)op);

    public void Emit(Opcode op, byte arg)
    {
        _code.Add((byte)op);
        _code.Add(arg);
    }

    public void EmitU16(Opcode op, int arg)
    {
        if (arg is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(arg), arg, "operand exceeds u16 range");
        _code.Add((byte)op);
        var b1 = (byte)(arg & 0xFF);
        var b2 = (byte)((arg >> 8) & 0xFF);
        _code.Add(b1);
        _code.Add(b2);
    }

    /// <summary>
    /// Emit a jump opcode with a placeholder offset; returns the position of
    /// the offset bytes for later patching via <see cref="PatchJump"/>.
    /// </summary>
    public int EmitJump(Opcode op)
    {
        _code.Add((byte)op);
        var pos = _code.Count;
        _code.Add(0);
        _code.Add(0);
        return pos;
    }

    /// <summary>Patch a previously-emitted jump to land at the current position.</summary>
    public void PatchJump(int operandPos)
    {
        var jumpFrom = operandPos + 2; // jump is measured from end-of-operand
        var target = _code.Count;
        var delta = target - jumpFrom;
        if (delta is < short.MinValue or > short.MaxValue)
            throw new InvalidOperationException("jump distance overflows i16");
        BinaryPrimitives.WriteInt16LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_code).Slice(operandPos, 2),
            (short)delta);
    }

    /// <summary>Write a signed 16-bit value at an arbitrary previously-reserved
    /// position (used for backward jumps).</summary>
    public void PatchI16(int pos, short value)
        => BinaryPrimitives.WriteInt16LittleEndian(
            System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_code).Slice(pos, 2),
            value);

    /// <summary>Emit two raw bytes for an unsigned 16-bit operand. Useful
    /// after a manually-emitted opcode that doesn't have a typed
    /// <see cref="EmitU16"/> overload.</summary>
    public void EmitU16Raw(int value)
    {
        if (value is < 0 or > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(value));
        _code.Add((byte)(value & 0xFF));
        _code.Add((byte)((value >> 8) & 0xFF));
    }

    /// <summary>Emit a raw unsigned-8-bit byte. Used for opcodes with
    /// composite operand layouts (e.g. <see cref="Opcode.MakeClosure"/>
    /// = <c>[u16 idx][u8 count]</c>) where the typed
    /// <see cref="Emit(Opcode, byte)"/> doesn't apply.</summary>
    public void EmitU8Raw(int value)
    {
        if (value is < 0 or > 0xFF)
            throw new ArgumentOutOfRangeException(nameof(value));
        _code.Add((byte)value);
    }

    public Chunk Build(string? name = null)
        => new(_code.ToArray(), _constants.ToArray(), LocalCount, name);
}
