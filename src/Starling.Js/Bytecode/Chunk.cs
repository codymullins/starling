using System.Buffers.Binary;

namespace Starling.Js.Bytecode;

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
    /// <summary>gap:closure-write-back — the set of local-slot indices in
    /// this chunk that the compiler promoted to <c>Cell</c> storage because
    /// at least one nested function references the binding. Empty for
    /// functions with no captured locals (the common case).</summary>
    public IReadOnlySet<int> CapturedSlots { get; }

    /// <summary>wp:M3-23 — sparse source-position table, sorted ascending by
    /// bytecode offset. Each entry maps the start offset of a
    /// throw-prone opcode (Call / CallMethod / New / *Apply / member loads)
    /// to the 1-based line/column of the originating AST node. Empty unless
    /// the compiler recorded positions. Lookup via <see cref="PositionAt"/>.</summary>
    public IReadOnlyList<(int Offset, int Line, int Col)> Positions { get; }

    public Chunk(byte[] code, IReadOnlyList<object?> constants, int localCount, string? name = null)
        : this(code, constants, localCount, name, capturedSlots: null)
    {
    }

    public Chunk(byte[] code, IReadOnlyList<object?> constants, int localCount, string? name, IReadOnlySet<int>? capturedSlots)
        : this(code, constants, localCount, name, capturedSlots, positions: null)
    {
    }

    public Chunk(byte[] code, IReadOnlyList<object?> constants, int localCount, string? name,
        IReadOnlySet<int>? capturedSlots, IReadOnlyList<(int Offset, int Line, int Col)>? positions)
    {
        Code = code ?? throw new ArgumentNullException(nameof(code));
        Constants = constants ?? throw new ArgumentNullException(nameof(constants));
        LocalCount = localCount;
        Name = name;
        CapturedSlots = capturedSlots ?? EmptyCaptured;
        Positions = positions ?? EmptyPositions;
    }

    private static readonly IReadOnlySet<int> EmptyCaptured = new HashSet<int>();
    private static readonly IReadOnlyList<(int, int, int)> EmptyPositions = [];

    /// <summary>wp:M3-23 — find the nearest recorded source position at or
    /// before <paramref name="ip"/>. The VM's <c>ip</c> at a throw site has
    /// already advanced past the offending opcode's operand bytes, so the
    /// recorded entry (keyed by the opcode's start offset) is the greatest
    /// entry with <c>Offset &lt;= ip</c>. Returns <c>null</c> when no position
    /// was recorded (empty table or the throw precedes the first entry).</summary>
    public (int Line, int Col)? PositionAt(int ip)
    {
        var positions = Positions;
        if (positions.Count == 0) return null;
        // Binary search for the greatest Offset <= ip.
        int lo = 0, hi = positions.Count - 1, best = -1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (positions[mid].Offset <= ip) { best = mid; lo = mid + 1; }
            else hi = mid - 1;
        }
        if (best < 0) return null;
        var e = positions[best];
        return (e.Line, e.Col);
    }

    public override string ToString() => Disassembler.Disassemble(this);
}

/// <summary>
/// Constant-pool wrapper for a BigInt literal. The parser turns the lexeme
/// (decimal / 0x / 0b / 0o, with the trailing <c>n</c> stripped) into a
/// <see cref="System.Numerics.BigInteger"/> at AST-build time; the VM unboxes
/// it via <see cref="Starling.Js.Runtime.JsValue.BigInt(System.Numerics.BigInteger)"/>
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
    private HashSet<int>? _capturedSlots;
    private List<(int Offset, int Line, int Col)>? _positions;
    public int LocalCount { get; private set; }

    /// <summary>wp:M3-23 — record that the opcode about to be emitted at the
    /// current <see cref="Position"/> originates from the given 1-based source
    /// line/column. Call immediately BEFORE emitting a throw-prone opcode so
    /// the recorded offset equals the opcode's start byte. Entries are kept in
    /// emission order, which is monotonically non-decreasing in offset, so the
    /// table is already sorted for <see cref="Chunk.PositionAt"/>'s binary
    /// search. Duplicate offsets are coalesced (last write wins).</summary>
    public void RecordPosition(int line, int col)
    {
        _positions ??= [];
        var offset = _code.Count;
        if (_positions.Count > 0 && _positions[^1].Offset == offset)
            _positions[^1] = (offset, line, col);
        else
            _positions.Add((offset, line, col));
    }

    /// <summary>gap:closure-write-back — record that <paramref name="slot"/>
    /// is captured by a nested function and must use <see cref="Starling.Js.Runtime.Cell"/>
    /// storage. Reads and writes against the slot go through the cell-aware
    /// opcodes.</summary>
    public void MarkCaptured(int slot)
    {
        _capturedSlots ??= [];
        _capturedSlots.Add(slot);
    }

    public bool IsCaptured(int slot) => _capturedSlots is not null && _capturedSlots.Contains(slot);

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
        => new(_code.ToArray(), _constants.ToArray(), LocalCount, name, _capturedSlots,
            _positions is null ? null : _positions.ToArray());
}
