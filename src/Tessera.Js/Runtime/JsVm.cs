using System.Buffers.Binary;
using Tessera.Js.Bytecode;

namespace Tessera.Js.Runtime;

/// <summary>
/// Stack-machine VM. Executes a <see cref="Chunk"/> against a
/// <see cref="JsRuntime"/> and returns the last-evaluated value.
/// </summary>
/// <remarks>
/// <para>
/// First-cut VM (wp:M3-04). Single-frame execution — function calls are
/// limited to host-native callables; user-defined function bodies live
/// in sub-chunks (deferred to the follow-up wp that wires
/// FunctionDeclaration through the compiler too).
/// </para>
/// <para>
/// Throws <see cref="JsThrow"/> for uncaught script-level throws; the host
/// wraps it appropriately. Stack overflows surface as
/// <see cref="StackOverflowException"/>.
/// </para>
/// </remarks>
public sealed class JsVm
{
    private readonly JsRuntime _runtime;
    private const int MaxStack = 1024;

    public JsVm(JsRuntime runtime)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
    }

    /// <summary>Run a chunk to completion. Returns the topmost value at Halt,
    /// or Undefined if the stack was empty.</summary>
    public JsValue Run(Chunk chunk) => Run(chunk, args: [], thisValue: JsValue.Undefined);

    /// <summary>
    /// Internal entry. Copies <paramref name="args"/> into the first N
    /// local slots and stashes <paramref name="thisValue"/> for the
    /// frame's <c>LoadThis</c> instruction. Top-level scripts pass
    /// empty args and Undefined this; <c>Opcode.Call</c> for a user
    /// <see cref="JsFunction"/> recurses through this entry, so the .NET
    /// call stack mirrors the JS call stack.
    /// </summary>
    private JsValue Run(Chunk chunk, JsValue[] args, JsValue thisValue)
    {
        var stack = new JsValue[MaxStack];
        var sp = 0;
        var locals = new JsValue[Math.Max(chunk.LocalCount, 1)];
        for (var k = 0; k < args.Length && k < locals.Length; k++)
            locals[k] = args[k];
        var code = chunk.Code;
        var constants = chunk.Constants;
        var ip = 0;
        var thisV = thisValue;

        void Push(JsValue v)
        {
            if (sp >= MaxStack) throw new StackOverflowException("JS stack overflow");
            stack[sp++] = v;
        }
        JsValue Pop() => stack[--sp];
        JsValue Peek() => stack[sp - 1];

        int ReadU8() => code[ip++];
        int ReadU16()
        {
            var v = BinaryPrimitives.ReadUInt16LittleEndian(code.AsSpan(ip, 2));
            ip += 2;
            return v;
        }
        int ReadI16()
        {
            var v = BinaryPrimitives.ReadInt16LittleEndian(code.AsSpan(ip, 2));
            ip += 2;
            return v;
        }

        while (true)
        {
            var op = (Opcode)code[ip++];
            switch (op)
            {
                case Opcode.Halt:
                    return sp > 0 ? stack[sp - 1] : JsValue.Undefined;
                case Opcode.Nop: break;

                // ----- Constants -----
                case Opcode.LoadConst:
                {
                    var idx = ReadU16();
                    var c = constants[idx];
                    Push(c switch
                    {
                        double d => JsValue.Number(d),
                        string s => JsValue.String(s),
                        JsBigIntPlaceholder bi => JsValue.BigInt(bi.Digits),
                        _ => JsValue.Undefined,
                    });
                    break;
                }
                case Opcode.LoadTrue: Push(JsValue.True); break;
                case Opcode.LoadFalse: Push(JsValue.False); break;
                case Opcode.LoadNull: Push(JsValue.Null); break;
                case Opcode.LoadUndefined: Push(JsValue.Undefined); break;
                case Opcode.LoadZero: Push(JsValue.Zero); break;

                // ----- Locals -----
                case Opcode.DeclareLocal:
                {
                    var slot = ReadU8();
                    locals[slot] = JsValue.Undefined;
                    break;
                }
                case Opcode.LoadLocal: Push(locals[ReadU8()]); break;
                case Opcode.StoreLocal: locals[ReadU8()] = Pop(); break;

                // ----- Globals -----
                case Opcode.LoadGlobal:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    Push(_runtime.GetGlobal(name));
                    break;
                }
                case Opcode.StoreGlobal:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    _runtime.SetGlobal(name, Pop());
                    break;
                }

                // ----- Stack manipulation -----
                case Opcode.Pop: sp--; break;
                case Opcode.Dup: Push(Peek()); break;
                case Opcode.Swap:
                {
                    var b = stack[sp - 1];
                    stack[sp - 1] = stack[sp - 2];
                    stack[sp - 2] = b;
                    break;
                }

                // ----- Arithmetic -----
                case Opcode.Add:
                {
                    var b = Pop();
                    var a = Pop();
                    Push(JsAdd(a, b));
                    break;
                }
                case Opcode.Sub: Push(JsValue.Number(NumPop2(out var b1, ref sp, stack) - b1)); break;
                case Opcode.Mul: { var b = Pop(); var a = Pop(); Push(JsValue.Number(JsValue.ToNumber(a) * JsValue.ToNumber(b))); break; }
                case Opcode.Div: { var b = Pop(); var a = Pop(); Push(JsValue.Number(JsValue.ToNumber(a) / JsValue.ToNumber(b))); break; }
                case Opcode.Mod:
                {
                    var b = Pop(); var a = Pop();
                    var ad = JsValue.ToNumber(a); var bd = JsValue.ToNumber(b);
                    Push(JsValue.Number(bd == 0 ? double.NaN : ad - Math.Floor(ad / bd) * bd));
                    break;
                }
                case Opcode.Pow:
                {
                    var b = Pop(); var a = Pop();
                    Push(JsValue.Number(Math.Pow(JsValue.ToNumber(a), JsValue.ToNumber(b))));
                    break;
                }
                case Opcode.Neg: Push(JsValue.Number(-JsValue.ToNumber(Pop()))); break;
                case Opcode.UnaryPlus: Push(JsValue.Number(JsValue.ToNumber(Pop()))); break;

                // ----- Bitwise (operate on Int32) -----
                case Opcode.BitOr:  { var b = Pop(); var a = Pop(); Push(JsValue.Number(ToInt32(a) | ToInt32(b))); break; }
                case Opcode.BitAnd: { var b = Pop(); var a = Pop(); Push(JsValue.Number(ToInt32(a) & ToInt32(b))); break; }
                case Opcode.BitXor: { var b = Pop(); var a = Pop(); Push(JsValue.Number(ToInt32(a) ^ ToInt32(b))); break; }
                case Opcode.BitNot: Push(JsValue.Number(~ToInt32(Pop()))); break;
                case Opcode.Shl: { var b = Pop(); var a = Pop(); Push(JsValue.Number(ToInt32(a) << (ToInt32(b) & 31))); break; }
                case Opcode.Shr: { var b = Pop(); var a = Pop(); Push(JsValue.Number(ToInt32(a) >> (ToInt32(b) & 31))); break; }
                case Opcode.Ushr: { var b = Pop(); var a = Pop(); Push(JsValue.Number((uint)ToInt32(a) >> (ToInt32(b) & 31))); break; }

                // ----- Comparison -----
                case Opcode.Eq:        { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(JsValue.AbstractEquals(a, b))); break; }
                case Opcode.NEq:       { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(!JsValue.AbstractEquals(a, b))); break; }
                case Opcode.StrictEq:  { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(JsValue.StrictEquals(a, b))); break; }
                case Opcode.StrictNEq: { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(!JsValue.StrictEquals(a, b))); break; }
                case Opcode.Lt:   { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(LessThan(a, b))); break; }
                case Opcode.LtEq: { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(LessThan(a, b) || JsValue.AbstractEquals(a, b))); break; }
                case Opcode.Gt:   { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(LessThan(b, a))); break; }
                case Opcode.GtEq: { var b = Pop(); var a = Pop(); Push(JsValue.Boolean(LessThan(b, a) || JsValue.AbstractEquals(a, b))); break; }

                // ----- Logical / typeof -----
                case Opcode.Not: Push(JsValue.Boolean(!JsValue.ToBoolean(Pop()))); break;
                case Opcode.TypeOf:
                {
                    var v = Pop();
                    Push(JsValue.String(v.Kind switch
                    {
                        JsValueKind.Undefined => "undefined",
                        JsValueKind.Null => "object",
                        JsValueKind.Boolean => "boolean",
                        JsValueKind.Number => "number",
                        JsValueKind.String => "string",
                        JsValueKind.Object => v.AsObject is JsNativeFunction ? "function" : "object",
                        JsValueKind.BigInt => "bigint",
                        _ => "undefined",
                    }));
                    break;
                }

                // ----- Property access -----
                case Opcode.LoadProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var obj = Pop();
                    Push(obj.IsObject ? obj.AsObject.Get(name) : JsValue.Undefined);
                    break;
                }
                case Opcode.StoreProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var obj = Pop();
                    if (obj.IsObject) obj.AsObject.Set(name, value);
                    Push(value);
                    break;
                }
                case Opcode.LoadComputed:
                {
                    var key = Pop();
                    var obj = Pop();
                    Push(obj.IsObject ? obj.AsObject.Get(JsValue.ToStringValue(key)) : JsValue.Undefined);
                    break;
                }
                case Opcode.StoreComputed:
                {
                    var value = Pop();
                    var key = Pop();
                    var obj = Pop();
                    if (obj.IsObject) obj.AsObject.Set(JsValue.ToStringValue(key), value);
                    Push(value);
                    break;
                }

                // ----- Calls -----
                case Opcode.Call:
                {
                    var argc = ReadU8();
                    var callArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) callArgs[i] = Pop();
                    var callee = Pop();
                    if (callee.IsObject && callee.AsObject is JsNativeFunction native)
                    {
                        Push(native.Body(callArgs));
                    }
                    else if (callee.IsObject && callee.AsObject is JsFunction jsFn)
                    {
                        // Reentrant call — mirrors JS call stack on .NET's.
                        // §10.2.1 strict-mode default: `this` is Undefined
                        // for plain calls. Member-method invocation will
                        // need separate bytecode (LoadProperty+Call doesn't
                        // bind the object); that's queued for M3-04e.
                        var ret = Run(jsFn.Body, callArgs, JsValue.Undefined);
                        Push(ret);
                    }
                    else
                    {
                        throw new JsThrow(JsValue.String($"not a function: {callee}"));
                    }
                    break;
                }

                // LoadFunction — pull a pre-compiled JsFunction out of the
                // constant pool and wrap as an object value.
                case Opcode.LoadFunction:
                {
                    var idx = ReadU16();
                    var fn = (JsFunction)constants[idx]!;
                    Push(JsValue.Object(fn));
                    break;
                }

                case Opcode.LoadThis:
                    Push(thisV);
                    break;

                case Opcode.NewObject:
                    Push(JsValue.Object(new JsObject()));
                    break;

                case Opcode.New:
                {
                    var argc = ReadU8();
                    var newArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) newArgs[i] = Pop();
                    var ctor = Pop();
                    if (ctor.IsObject && ctor.AsObject is JsFunction jsFn)
                    {
                        // §13.3.5.1 [[Construct]]: allocate a fresh ordinary
                        // object, run the body with `this` bound to it. If
                        // the body returns an object, use that; otherwise
                        // use the freshly-allocated `this`.
                        var instance = new JsObject();
                        var thisVal = JsValue.Object(instance);
                        var result = Run(jsFn.Body, newArgs, thisVal);
                        Push(result.IsObject ? result : thisVal);
                    }
                    else if (ctor.IsObject && ctor.AsObject is JsNativeFunction)
                    {
                        // Native constructors land in M3-05 when intrinsics
                        // arrive (e.g. `new Array(...)`). For now this is
                        // an error.
                        throw new JsThrow(JsValue.String(
                            "new on native function not yet supported (wp:M3-05)"));
                    }
                    else
                    {
                        throw new JsThrow(JsValue.String($"not a constructor: {ctor}"));
                    }
                    break;
                }

                // ----- Control flow -----
                case Opcode.Jump: { var d = ReadI16(); ip += d; break; }
                case Opcode.JumpIfTrue:
                {
                    var d = ReadI16();
                    if (JsValue.ToBoolean(Pop())) ip += d;
                    break;
                }
                case Opcode.JumpIfFalse:
                {
                    var d = ReadI16();
                    if (!JsValue.ToBoolean(Pop())) ip += d;
                    break;
                }
                case Opcode.JumpIfNotNullish:
                {
                    var d = ReadI16();
                    if (!Pop().IsNullish) ip += d;
                    break;
                }

                // ----- Returns -----
                case Opcode.Return: return Pop();
                case Opcode.ReturnUndefined: return JsValue.Undefined;

                // ----- Throw -----
                case Opcode.Throw: throw new JsThrow(Pop());

                default:
                    throw new InvalidOperationException($"opcode {op} not implemented in VM");
            }
        }
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>JS '+': string-concat if either operand is a string after
    /// ToPrimitive (which we approximate as ToString for non-primitives),
    /// otherwise numeric add.</summary>
    private static JsValue JsAdd(JsValue a, JsValue b)
    {
        if (a.IsString || b.IsString)
            return JsValue.String(JsValue.ToStringValue(a) + JsValue.ToStringValue(b));
        return JsValue.Number(JsValue.ToNumber(a) + JsValue.ToNumber(b));
    }

    /// <summary>Less-than per §7.2.13. Returns false for NaN comparisons
    /// per the spec.</summary>
    private static bool LessThan(JsValue a, JsValue b)
    {
        if (a.IsString && b.IsString)
            return string.CompareOrdinal(a.AsString, b.AsString) < 0;
        var ad = JsValue.ToNumber(a);
        var bd = JsValue.ToNumber(b);
        if (double.IsNaN(ad) || double.IsNaN(bd)) return false;
        return ad < bd;
    }

    private static int ToInt32(JsValue v)
    {
        var d = JsValue.ToNumber(v);
        if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) return 0;
        var i = (long)Math.Truncate(d);
        return (int)(i & 0xFFFFFFFF);
    }

    // Helper to satisfy Opcode.Sub's pattern — keeps stack frame clean.
    private static double NumPop2(out double b, ref int sp, JsValue[] stack)
    {
        var bv = stack[--sp];
        var av = stack[--sp];
        b = JsValue.ToNumber(bv);
        return JsValue.ToNumber(av);
    }
}

/// <summary>Thrown by the VM when a script-level <c>throw</c> is uncaught.</summary>
#pragma warning disable RCS1194
public sealed class JsThrow(JsValue value) : Exception($"uncaught: {value}")
{
    public JsValue Value { get; } = value;
}
#pragma warning restore RCS1194
