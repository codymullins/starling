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

    /// <summary>The realm this VM dispatches against.</summary>
    public JsRealm Realm => _runtime.Realm;

    /// <summary>The runtime that owns this VM (host bindings, console sink).</summary>
    public JsRuntime Runtime => _runtime;

    /// <summary>Run a chunk to completion. Returns the topmost value at Halt,
    /// or Undefined if the stack was empty.</summary>
    public JsValue Run(Chunk chunk) =>
        Run(chunk, args: [], thisValue: JsValue.Undefined, upvalues: Array.Empty<JsValue>());

    /// <summary>Invoke a JS function with an explicit <c>this</c> and args.
    /// Used by <see cref="AbstractOperations.Call"/>.</summary>
    public JsValue CallFunction(JsFunction fn, JsValue thisValue, JsValue[] args)
        => Run(fn.Body, args, thisValue, fn.Upvalues);

    /// <summary>Construct a JS function (spec [[Construct]] for ordinary
    /// functions): allocate a fresh ordinary object inheriting from the
    /// constructor's <c>prototype</c> property, run the body with <c>this</c>
    /// bound to it, and return whichever object the body produced.</summary>
    public JsValue ConstructFunction(JsFunction fn, JsValue[] args, JsObject newTarget)
    {
        // OrdinaryCreateFromConstructor: prototype is newTarget.prototype if it's
        // an object, else the realm's Object.prototype.
        var protoSlot = newTarget.Get("prototype");
        var proto = protoSlot.IsObject ? protoSlot.AsObject : _runtime.Realm.ObjectPrototype;
        var instance = _runtime.Realm.NewObjectWithProto(proto);
        var thisVal = JsValue.Object(instance);
        var result = Run(fn.Body, args, thisVal, fn.Upvalues);
        return result.IsObject ? result : thisVal;
    }

    /// <summary>
    /// Internal entry. Copies <paramref name="args"/> into the first N
    /// local slots and stashes <paramref name="thisValue"/> for the
    /// frame's <c>LoadThis</c> instruction. <paramref name="upvalues"/>
    /// is the closure's snapshot table — empty for top-level scripts
    /// and for plain (non-capturing) functions. <c>Opcode.Call</c> for
    /// a user <see cref="JsFunction"/> recurses through this entry, so
    /// the .NET call stack mirrors the JS call stack.
    /// </summary>
    private JsValue Run(Chunk chunk, JsValue[] args, JsValue thisValue, IReadOnlyList<JsValue> upvalues)
    {
        // Publish this VM on the realm so native intrinsics (JSON.parse
        // reviver, JSON.stringify replacer/toJSON, etc.) can dispatch JS
        // callables. Save/restore in case of reentry from a nested host
        // invocation chain.
        var prevVm = _runtime.Realm.ActiveVm;
        _runtime.Realm.ActiveVm = this;
        try
        {
            return RunInner(chunk, args, thisValue, upvalues);
        }
        finally
        {
            _runtime.Realm.ActiveVm = prevVm;
        }
    }

    private JsValue RunInner(Chunk chunk, JsValue[] args, JsValue thisValue, IReadOnlyList<JsValue> upvalues)
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
                        JsValueKind.Object => AbstractOperations.IsCallable(v) ? "function" : "object",
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
                    if (obj.IsObject) Push(obj.AsObject.Get(name));
                    else if (!obj.IsNullish) Push(AbstractOperations.ToObject(_runtime.Realm, obj).Get(name));
                    else Push(JsValue.Undefined);
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
                    var propertyKey = JsValue.ToStringValue(key);
                    if (obj.IsObject) Push(obj.AsObject.Get(propertyKey));
                    else if (!obj.IsNullish) Push(AbstractOperations.ToObject(_runtime.Realm, obj).Get(propertyKey));
                    else Push(JsValue.Undefined);
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
                // §10.2.1: plain Call binds this=Undefined (strict default);
                // CallMethod takes a receiver and binds this=receiver, used
                // by the compiler for obj.method() / obj[key]() syntax.
                case Opcode.Call:
                {
                    var argc = ReadU8();
                    var callArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) callArgs[i] = Pop();
                    var callee = Pop();
                    Push(AbstractOperations.Call(this, callee, JsValue.Undefined, callArgs));
                    break;
                }
                case Opcode.CallMethod:
                {
                    var argc = ReadU8();
                    var callArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) callArgs[i] = Pop();
                    var callee = Pop();
                    var receiver = Pop();
                    Push(AbstractOperations.Call(this, callee, receiver, callArgs));
                    break;
                }

                // LoadFunction — pull a pre-compiled JsFunction template
                // out of the constant pool (empty upvalues) and wrap as
                // an object value. Used only for non-capturing functions;
                // capturing ones come through MakeClosure.
                case Opcode.LoadFunction:
                {
                    var idx = ReadU16();
                    var fn = (JsFunction)constants[idx]!;
                    Push(JsValue.Object(fn));
                    break;
                }

                // MakeClosure — pop N captured values and wrap a fresh
                // JsFunction over the template, with those values bound
                // as snapshot upvalues. §10.2.1 (closure-of-environment),
                // adapted to our snapshot-only semantics for M3-04c.
                case Opcode.MakeClosure:
                {
                    var idx = ReadU16();
                    var nUpvalues = ReadU8();
                    var template = (JsFunction)constants[idx]!;
                    var captured = new JsValue[nUpvalues];
                    for (var i = nUpvalues - 1; i >= 0; i--) captured[i] = Pop();
                    var closure = new JsFunction(
                        template.Name, template.Body, template.ArityDeclared, captured);
                    Push(JsValue.Object(closure));
                    break;
                }

                case Opcode.LoadUpvalue:
                {
                    var idx = ReadU8();
                    Push(upvalues[idx]);
                    break;
                }

                case Opcode.LoadThis:
                    Push(thisV);
                    break;

                case Opcode.NewObject:
                    Push(JsValue.Object(_runtime.Realm.NewOrdinaryObject()));
                    break;

                case Opcode.New:
                {
                    var argc = ReadU8();
                    var newArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) newArgs[i] = Pop();
                    var ctor = Pop();
                    Push(AbstractOperations.Construct(this, ctor, newArgs));
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

                case Opcode.SpreadInto:
                {
                    var src = Pop();
                    var dst = Pop();
                    if (src.IsObject && dst.IsObject)
                    {
                        var srcObj = src.AsObject;
                        var dstObj = dst.AsObject;
                        foreach (var key in srcObj.EnumerableKeys())
                            dstObj.Set(key, srcObj.Get(key));
                    }
                    break;
                }

                case Opcode.RestArray:
                {
                    var start = ReadU16();
                    var src = Pop();
                    var result = _runtime.Realm.NewOrdinaryObject();
                    var srcObj = src.IsObject ? src.AsObject : (!src.IsNullish ? AbstractOperations.ToObject(_runtime.Realm, src) : null);
                    var len = 0;
                    if (srcObj is not null)
                        len = Math.Max(0, (int)Math.Truncate(JsValue.ToNumber(srcObj.Get("length"))));
                    var outIndex = 0;
                    if (srcObj is not null)
                    {
                        for (var i = start; i < len; i++)
                            result.Set((outIndex++).ToString(System.Globalization.CultureInfo.InvariantCulture), srcObj.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    result.Set("length", JsValue.Number(outIndex));
                    Push(JsValue.Object(result));
                    break;
                }

                case Opcode.RestObject:
                {
                    var excludedCount = ReadU16();
                    var excluded = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < excludedCount; i++)
                        excluded.Add(AbstractOperations.ToPropertyKey(Pop()));
                    var src = Pop();
                    var result = _runtime.Realm.NewOrdinaryObject();
                    if (src.IsObject)
                    {
                        var srcObj = src.AsObject;
                        foreach (var key in srcObj.EnumerableKeys())
                            if (!excluded.Contains(key)) result.Set(key, srcObj.Get(key));
                    }
                    else if (!src.IsNullish)
                    {
                        var srcObj = AbstractOperations.ToObject(_runtime.Realm, src);
                        foreach (var key in srcObj.EnumerableKeys())
                            if (!excluded.Contains(key)) result.Set(key, srcObj.Get(key));
                    }
                    Push(JsValue.Object(result));
                    break;
                }

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
