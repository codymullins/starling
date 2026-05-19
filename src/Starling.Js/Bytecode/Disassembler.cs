using System.Buffers.Binary;
using System.Text;

namespace Tessera.Js.Bytecode;

/// <summary>
/// Renders a <see cref="Chunk"/> as human-readable text. Used by tests
/// (snapshot/diff) and by the future devtools bytecode viewer.
/// </summary>
public static class Disassembler
{
    public static string Disassemble(Chunk chunk)
    {
        var sb = new StringBuilder();
        if (chunk.Name is not null) sb.Append("# chunk: ").AppendLine(chunk.Name);
        if (chunk.Constants.Count > 0)
        {
            sb.AppendLine("# constants:");
            for (var k = 0; k < chunk.Constants.Count; k++)
                sb.Append("  ").Append(k).Append(": ").AppendLine(FormatConstant(chunk.Constants[k]));
        }
        sb.AppendLine("# code:");

        var code = chunk.Code;
        var i = 0;
        while (i < code.Length)
        {
            sb.Append("  ").Append(i.ToString("D4")).Append("  ");
            var op = (Opcode)code[i];
            i++;
            switch (op)
            {
                // u16 operand opcodes
                case Opcode.LoadConst:
                case Opcode.LoadFunction:
                case Opcode.LoadGlobal:
                case Opcode.StoreGlobal:
                case Opcode.LoadProperty:
                case Opcode.StoreProperty:
                case Opcode.RestArray:
                case Opcode.RestObject:
                case Opcode.LoadSuperProperty:
                case Opcode.StoreSuperProperty:
                case Opcode.PrivateGet:
                case Opcode.PrivateSet:
                case Opcode.DefinePrivateField:
                case Opcode.BuildClass:
                {
                    var idx = BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(i, 2));
                    i += 2;
                    sb.Append(op).Append(' ').Append(idx);
                    var c = chunk.Constants[idx];
                    sb.Append("  ; ").Append(FormatConstant(c));
                    break;
                }
                // u8 operand opcodes
                case Opcode.LoadLocal:
                case Opcode.StoreLocal:
                case Opcode.DeclareLocal:
                case Opcode.Call:
                case Opcode.CallMethod:
                case Opcode.New:
                case Opcode.LoadUpvalue:
                {
                    var slot = code[i];
                    i++;
                    sb.Append(op).Append(' ').Append(slot);
                    break;
                }
                // u16 + u8 — MakeClosure [fnIdx][nUpvalues]
                case Opcode.MakeClosure:
                {
                    var idx = BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(i, 2));
                    i += 2;
                    var n = code[i];
                    i++;
                    sb.Append(op).Append(' ').Append(idx).Append(' ').Append(n);
                    sb.Append("  ; template=").Append(FormatConstant(chunk.Constants[idx]))
                      .Append(" upvalues=").Append(n);
                    break;
                }
                // u16 + u16 — LoadRegExp [srcIdx][flagsIdx]
                case Opcode.LoadRegExp:
                {
                    var srcIdx = BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(i, 2));
                    i += 2;
                    var flagsIdx = BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(i, 2));
                    i += 2;
                    sb.Append(op).Append(' ').Append(srcIdx).Append(' ').Append(flagsIdx);
                    sb.Append("  ; /").Append(FormatConstant(chunk.Constants[srcIdx]))
                      .Append('/').Append(FormatConstant(chunk.Constants[flagsIdx]));
                    break;
                }
                // i16 jump-offset opcodes
                case Opcode.Jump:
                case Opcode.JumpIfTrue:
                case Opcode.JumpIfFalse:
                case Opcode.JumpIfNotNullish:
                case Opcode.LogAnd:
                case Opcode.LogOr:
                case Opcode.Coalesce:
                {
                    var offset = BinaryPrimitives.ReadInt16LittleEndian(code.AsSpan(i, 2));
                    i += 2;
                    sb.Append(op).Append(' ').Append(offset);
                    sb.Append("  ; → ").Append((i + offset).ToString("D4"));
                    break;
                }
                default:
                    sb.Append(op);
                    break;
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string FormatConstant(object? c) => c switch
    {
        null => "(null)",
        string s => $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"",
        double d => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
        bool b => b ? "true" : "false",
        JsBigIntPlaceholder bi => $"{bi.Digits}n",
        _ => c.ToString() ?? "(?)",
    };
}
