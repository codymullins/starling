using System.Buffers.Binary;
using Tessera.Js.Bytecode;
using Tessera.Js.RegExp;

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
    /// <remarks>
    /// Drains the realm's microtask queue (Promise reactions, thenable
    /// adoption jobs) before returning, matching what an HTML embedder
    /// would do at the bottom of a top-level task. Nested invocations
    /// (function call → JS body) go through <see cref="CallFunction"/> /
    /// <see cref="ConstructFunction"/> and intentionally do NOT drain — the
    /// drain belongs to the outermost frame only. When the host installs a
    /// microtask scheduler via <see cref="JsRuntime.SetMicrotaskScheduler"/>,
    /// the drain is a no-op (the host loop owns pumping).
    /// </remarks>
    public JsValue Run(Chunk chunk) =>
        Run(chunk, args: [], thisValue: JsValue.Undefined,
            upvalues: Array.Empty<JsValue>(), drainMicrotasks: true,
            currentFunction: null, newTarget: null);

    /// <summary>Invoke a JS function with an explicit <c>this</c> and args.
    /// Used by <see cref="AbstractOperations.Call"/>.</summary>
    public JsValue CallFunction(JsFunction fn, JsValue thisValue, JsValue[] args)
        => Run(fn.Body, args, thisValue, fn.Upvalues, drainMicrotasks: false,
               currentFunction: fn, newTarget: null);

    /// <summary>Construct a JS function (spec [[Construct]] for ordinary
    /// functions): allocate a fresh ordinary object inheriting from the
    /// constructor's <c>prototype</c> property, run the body with <c>this</c>
    /// bound to it, and return whichever object the body produced.</summary>
    public JsValue ConstructFunction(JsFunction fn, JsValue[] args, JsObject newTarget)
    {
        // Derived class constructor — <c>this</c> is uninitialized until
        // super(...) runs (§10.2.1.1). Pass the realm's sentinel so
        // LoadThisChecked throws ReferenceError if the user touches it
        // before super.
        if (fn.ConstructorKind == ClassConstructorKind.Derived)
        {
            var sentinel = JsValue.Object(_runtime.Realm.UninitializedThisSentinel);
            // Save/restore the side-channel slot across nested ConstructFunction
            // calls so a derived ctor that itself constructs another derived
            // class doesn't clobber the outer frame's bound-this.
            var prevDerivedThis = _currentDerivedThis;
            _currentDerivedThis = null;
            try
            {
                var result = Run(fn.Body, args, sentinel, fn.Upvalues,
                    drainMicrotasks: false, currentFunction: fn, newTarget: newTarget);
                if (result.IsObject) return result;
                return _currentDerivedThis ?? throw new JsThrow(_runtime.Realm.NewReferenceError(
                    "Must call super constructor in derived class before returning from derived constructor"));
            }
            finally
            {
                _currentDerivedThis = prevDerivedThis;
            }
        }
        // OrdinaryCreateFromConstructor: prototype is newTarget.prototype if it's
        // an object, else the realm's Object.prototype.
        var protoSlot = newTarget.Get("prototype");
        var proto = protoSlot.IsObject ? protoSlot.AsObject : _runtime.Realm.ObjectPrototype;
        var instance = _runtime.Realm.NewObjectWithProto(proto);
        var thisVal = JsValue.Object(instance);
        var resultBase = Run(fn.Body, args, thisVal, fn.Upvalues,
            drainMicrotasks: false, currentFunction: fn, newTarget: newTarget);
        // Class constructors implicit return their own `this`; an explicit
        // return of a non-object is ignored (matching §10.2.1.4).
        return resultBase.IsObject ? resultBase : thisVal;
    }

    /// <summary>Carries the bound-this for a derived constructor across the
    /// final return-value coercion. Read by the outer
    /// <see cref="ConstructFunction"/> immediately after the inner Run
    /// returns; protected by the JS call stack (we never recurse without
    /// saving/restoring it).</summary>
    private JsValue? _currentDerivedThis;

    /// <summary>
    /// Internal entry. Copies <paramref name="args"/> into the first N
    /// local slots and stashes <paramref name="thisValue"/> for the
    /// frame's <c>LoadThis</c> instruction. <paramref name="upvalues"/>
    /// is the closure's snapshot table — empty for top-level scripts
    /// and for plain (non-capturing) functions. <c>Opcode.Call</c> for
    /// a user <see cref="JsFunction"/> recurses through this entry, so
    /// the .NET call stack mirrors the JS call stack.
    /// </summary>
    private JsValue Run(Chunk chunk, JsValue[] args, JsValue thisValue,
        IReadOnlyList<JsValue> upvalues, bool drainMicrotasks,
        JsFunction? currentFunction, JsObject? newTarget)
    {
        // Publish this VM on the realm so native intrinsics (JSON.parse
        // reviver, JSON.stringify replacer/toJSON, etc.) can dispatch JS
        // callables. Save/restore in case of reentry from a nested host
        // invocation chain.
        var prevVm = _runtime.Realm.ActiveVm;
        _runtime.Realm.ActiveVm = this;
        try
        {
            var result = RunInner(chunk, args, thisValue, upvalues, currentFunction, newTarget);
            // Drain microtasks while ActiveVm still points to this VM so
            // reaction jobs that dispatch JS handlers find a usable VM
            // (AbstractOperations.Call needs one for JsFunction). Only the
            // outermost (top-level Run) frame drains — nested calls do not.
            if (drainMicrotasks)
                _runtime.DrainMicrotasks();
            return result;
        }
        finally
        {
            _runtime.Realm.ActiveVm = prevVm;
        }
    }

    private JsValue RunInner(Chunk chunk, JsValue[] args, JsValue thisValue,
        IReadOnlyList<JsValue> upvalues, JsFunction? currentFunction, JsObject? newTarget)
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

        // §14.15 try-frame stack — owns the catch/finally targets that the
        // outer C# catch(JsThrow) and the Return opcode handler consult.
        var tryStack = new Stack<TryFrame>();

        while (true)
        {
            JsThrow? rethrow = null;
            try
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
                        JsBigIntPlaceholder bi => JsValue.BigInt(bi.Value),
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
                case Opcode.Sub:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "-");
                        Push(BigIntOps.Subtract(a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(JsValue.ToNumber(a) - JsValue.ToNumber(b)));
                    break;
                }
                case Opcode.Mul:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "*");
                        Push(BigIntOps.Multiply(a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(JsValue.ToNumber(a) * JsValue.ToNumber(b)));
                    break;
                }
                case Opcode.Div:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "/");
                        Push(BigIntOps.Divide(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(JsValue.ToNumber(a) / JsValue.ToNumber(b)));
                    break;
                }
                case Opcode.Mod:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "%");
                        Push(BigIntOps.Remainder(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    var ad = JsValue.ToNumber(a); var bd = JsValue.ToNumber(b);
                    Push(JsValue.Number(bd == 0 ? double.NaN : ad - Math.Floor(ad / bd) * bd));
                    break;
                }
                case Opcode.Pow:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "**");
                        Push(BigIntOps.Pow(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(Math.Pow(JsValue.ToNumber(a), JsValue.ToNumber(b))));
                    break;
                }
                case Opcode.Neg:
                {
                    var v = Pop();
                    if (v.IsBigInt) { Push(BigIntOps.Negate(v.AsBigInt)); break; }
                    Push(JsValue.Number(-JsValue.ToNumber(v)));
                    break;
                }
                case Opcode.UnaryPlus:
                {
                    var v = Pop();
                    // §13.5.4: unary + on a BigInt throws TypeError.
                    if (v.IsBigInt)
                        throw new JsThrow(_runtime.Realm.NewTypeError("Cannot convert a BigInt value to a number"));
                    Push(JsValue.Number(JsValue.ToNumber(v)));
                    break;
                }

                // ----- Bitwise (Number → Int32, or BigInt-only) -----
                case Opcode.BitOr:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "|");
                        Push(BigIntOps.BitwiseOr(a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(ToInt32(a) | ToInt32(b))); break;
                }
                case Opcode.BitAnd:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "&");
                        Push(BigIntOps.BitwiseAnd(a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(ToInt32(a) & ToInt32(b))); break;
                }
                case Opcode.BitXor:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "^");
                        Push(BigIntOps.BitwiseXor(a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(ToInt32(a) ^ ToInt32(b))); break;
                }
                case Opcode.BitNot:
                {
                    var v = Pop();
                    if (v.IsBigInt) { Push(BigIntOps.BitwiseNot(v.AsBigInt)); break; }
                    Push(JsValue.Number(~ToInt32(v))); break;
                }
                case Opcode.Shl:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "<<");
                        Push(BigIntOps.ShiftLeft(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(ToInt32(a) << (ToInt32(b) & 31))); break;
                }
                case Opcode.Shr:
                {
                    var b = Pop(); var a = Pop();
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, ">>");
                        Push(BigIntOps.ShiftRight(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    Push(JsValue.Number(ToInt32(a) >> (ToInt32(b) & 31))); break;
                }
                case Opcode.Ushr:
                {
                    var b = Pop(); var a = Pop();
                    // §13.10.4 — BigInts have no unsigned right shift; throw TypeError.
                    if (a.IsBigInt || b.IsBigInt)
                        throw new JsThrow(_runtime.Realm.NewTypeError("BigInts have no unsigned right shift, use >> instead"));
                    Push(JsValue.Number((uint)ToInt32(a) >> (ToInt32(b) & 31))); break;
                }

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
                        JsValueKind.Symbol => "symbol",
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
                    if (obj.IsObject) Push(AbstractOperations.Get(this, obj.AsObject, name));
                    else if (!obj.IsNullish) Push(AbstractOperations.Get(this, AbstractOperations.ToObject(_runtime.Realm, obj), name, obj));
                    else Push(JsValue.Undefined);
                    break;
                }
                case Opcode.StoreProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var obj = Pop();
                    if (obj.IsObject) AbstractOperations.Set(this, obj.AsObject, name, value);
                    Push(value);
                    break;
                }
                case Opcode.LoadComputed:
                {
                    var key = Pop();
                    var obj = Pop();
                    var propertyKey = AbstractOperations.ToPropertyKey(key);
                    if (obj.IsObject) Push(AbstractOperations.Get(this, obj.AsObject, propertyKey));
                    else if (!obj.IsNullish) Push(AbstractOperations.Get(this, AbstractOperations.ToObject(_runtime.Realm, obj), propertyKey, obj));
                    else Push(JsValue.Undefined);
                    break;
                }
                case Opcode.StoreComputed:
                {
                    var value = Pop();
                    var key = Pop();
                    var obj = Pop();
                    if (obj.IsObject) AbstractOperations.Set(this, obj.AsObject, AbstractOperations.ToPropertyKey(key), value);
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
                    var template = (JsFunction)constants[idx]!;
                    // Per B2-2: every LoadFunction produces a fresh instance
                    // wired to realm.FunctionPrototype with its own
                    // `prototype`/`name`/`length` own properties. The template
                    // in the constant pool stays untouched.
                    var fn = JsFunction.CreateInstance(_runtime.Realm, template, Array.Empty<JsValue>());
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
                    // Per B2-2: closure also routes through CreateInstance so
                    // it inherits Function.prototype and gets a per-call
                    // `prototype` own-property.
                    var closure = JsFunction.CreateInstance(_runtime.Realm, template, captured);
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

                case Opcode.NewArray:
                    Push(JsValue.Object(new JsArray(_runtime.Realm)));
                    break;

                case Opcode.LoadRegExp:
                {
                    var srcIdx = ReadU16();
                    var flagsIdx = ReadU16();
                    var source = (string)constants[srcIdx]!;
                    var flagsStr = (string)constants[flagsIdx]!;
                    if (!RegexFlagParser.TryParse(flagsStr, out var flags, out var flagErr))
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(flagErr!));
                    CompiledRegex compiled;
                    try
                    {
                        compiled = CompiledRegex.Compile(source, flags);
                    }
                    catch (RegexSyntaxException ex)
                    {
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            $"Invalid regular expression: /{source}/: {ex.Message}"));
                    }
                    Push(JsValue.Object(new JsRegExp(_runtime.Realm, compiled)));
                    break;
                }

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
                // §14.15: divert through any enclosing finalizer first.
                case Opcode.Return:
                {
                    var rv = Pop();
                    if (DivertReturnThroughFinally(tryStack, rv, ref ip)) break;
                    return rv;
                }
                case Opcode.ReturnUndefined:
                {
                    if (DivertReturnThroughFinally(tryStack, JsValue.Undefined, ref ip)) break;
                    return JsValue.Undefined;
                }

                // ----- Throw -----
                case Opcode.Throw: throw new JsThrow(Pop());

                // ----- Try-frame management (gap:try-catch) -----
                case Opcode.EnterTry:
                {
                    var catchOff = ReadI16();
                    var finOff = ReadI16();
                    tryStack.Push(new TryFrame
                    {
                        CatchPc = catchOff == -1 ? -1 : ip + catchOff,
                        FinallyPc = finOff == -1 ? -1 : ip + finOff,
                        StackBase = sp,
                        Phase = TryPhase.TryBody,
                        Pending = PendingCompletion.None,
                        PendingValue = JsValue.Undefined,
                    });
                    break;
                }
                case Opcode.LeaveTry:
                {
                    if (tryStack.Count == 0)
                        throw new InvalidOperationException("LeaveTry with empty try-frame stack");
                    var frame = tryStack.Peek();
                    if (frame.FinallyPc != -1 && frame.Phase != TryPhase.RunningFinally)
                    {
                        frame.Phase = TryPhase.RunningFinally;
                        frame.Pending = PendingCompletion.Normal;
                        frame.PendingValue = JsValue.Undefined;
                        tryStack.Pop(); tryStack.Push(frame);
                        ip = frame.FinallyPc;
                    }
                    else
                    {
                        tryStack.Pop();
                    }
                    break;
                }
                case Opcode.EndFinally:
                {
                    if (tryStack.Count == 0)
                        throw new InvalidOperationException("EndFinally with empty try-frame stack");
                    var frame = tryStack.Pop();
                    switch (frame.Pending)
                    {
                        case PendingCompletion.Normal:
                            break;
                        case PendingCompletion.Throw:
                            throw new JsThrow(frame.PendingValue);
                        case PendingCompletion.Return:
                        {
                            var rv = frame.PendingValue;
                            if (DivertReturnThroughFinally(tryStack, rv, ref ip)) break;
                            return rv;
                        }
                    }
                    break;
                }

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
                        foreach (var key in srcObj.EnumerableSymbolKeys())
                            dstObj.Set(key, srcObj.Get(key));
                    }
                    break;
                }

                case Opcode.RestArray:
                {
                    var start = ReadU16();
                    var src = Pop();
                    // B2-4: rest-array binding now produces a real JsArray.
                    var result = new JsArray(_runtime.Realm);
                    var srcObj = src.IsObject ? src.AsObject : (!src.IsNullish ? AbstractOperations.ToObject(_runtime.Realm, src) : null);
                    var len = 0;
                    if (srcObj is not null)
                        len = Math.Max(0, (int)Math.Truncate(JsValue.ToNumber(srcObj.Get("length"))));
                    if (srcObj is not null)
                    {
                        for (var i = start; i < len; i++)
                            result.Push(srcObj.Get(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    Push(JsValue.Object(result));
                    break;
                }

                case Opcode.GetIterator:
                {
                    var iterable = Pop();
                    var record = AbstractOperations.GetIterator(_runtime.Realm, this, iterable);
                    Push(JsValue.Object(new Tessera.Js.Intrinsics.JsIteratorRecordHandle(record)));
                    break;
                }

                case Opcode.IteratorStep:
                {
                    // Peek (don't pop) so the surrounding loop keeps the handle
                    // across iterations. The dispatch arm pushes either the
                    // iterator-result object (done=false) or undefined (done=true)
                    // as the loop sentinel.
                    var top = Peek();
                    if (!top.IsObject || top.AsObject is not Tessera.Js.Intrinsics.JsIteratorRecordHandle handle)
                        throw new InvalidOperationException("IteratorStep expects an iterator-record handle on the stack");
                    var step = AbstractOperations.IteratorStep(_runtime.Realm, this, ref handle.Record);
                    Push(step ?? JsValue.Undefined);
                    break;
                }

                case Opcode.IteratorClose:
                {
                    var handleV = Pop();
                    if (handleV.IsObject && handleV.AsObject is Tessera.Js.Intrinsics.JsIteratorRecordHandle h)
                    {
                        if (!h.Record.Done)
                            AbstractOperations.IteratorClose(this, h.Record, isThrowing: false);
                    }
                    break;
                }

                case Opcode.CallApply:
                {
                    var argsArrV = Pop();
                    var callee = Pop();
                    var applyArgs = ExtractApplyArgs(argsArrV);
                    Push(AbstractOperations.Call(this, callee, JsValue.Undefined, applyArgs));
                    break;
                }

                case Opcode.CallApplyMethod:
                {
                    var argsArrV = Pop();
                    var callee = Pop();
                    var receiver = Pop();
                    var applyArgs = ExtractApplyArgs(argsArrV);
                    Push(AbstractOperations.Call(this, callee, receiver, applyArgs));
                    break;
                }

                case Opcode.NewApply:
                {
                    var argsArrV = Pop();
                    var ctor = Pop();
                    var applyArgs = ExtractApplyArgs(argsArrV);
                    Push(AbstractOperations.Construct(this, ctor, applyArgs));
                    break;
                }

                case Opcode.SpreadIterable:
                {
                    // Stack: [target, iterable] -> [target] with target's
                    // dense backing extended by iterable's values.
                    var iterable = Pop();
                    var targetV = Peek();
                    if (!targetV.IsObject || targetV.AsObject is not JsArray targetArr)
                        throw new InvalidOperationException("SpreadIterable target must be a JsArray");
                    var record = AbstractOperations.GetIterator(_runtime.Realm, this, iterable);
                    while (true)
                    {
                        var step = AbstractOperations.IteratorStep(_runtime.Realm, this, ref record);
                        if (step is null) break;
                        targetArr.Push(AbstractOperations.IteratorValue(this, step.Value));
                    }
                    break;
                }

                case Opcode.RestObject:
                {
                    var excludedCount = ReadU16();
                    var excluded = new HashSet<string>(StringComparer.Ordinal);
                    for (var i = 0; i < excludedCount; i++)
                    {
                        var key = AbstractOperations.ToPropertyKey(Pop());
                        if (!key.IsSymbol) excluded.Add(key.AsString);
                    }
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

                // ----- Classes (B1b-2a) -----
                case Opcode.LoadThisChecked:
                {
                    if (thisV.IsObject
                        && ReferenceEquals(thisV.AsObject, _runtime.Realm.UninitializedThisSentinel))
                    {
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Must call super constructor in derived class before accessing 'this'"));
                    }
                    Push(thisV);
                    break;
                }
                case Opcode.LoadHomeObject:
                {
                    if (currentFunction?.HomeObject is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "'super' is only allowed inside class methods"));
                    Push(JsValue.Object(currentFunction.HomeObject));
                    break;
                }
                case Opcode.LoadNewTarget:
                {
                    Push(newTarget is null ? JsValue.Undefined : JsValue.Object(newTarget));
                    break;
                }
                case Opcode.BindThis:
                {
                    thisV = Pop();
                    _currentDerivedThis = thisV;
                    break;
                }
                case Opcode.CallSuperCtor:
                {
                    var argsArr = Pop();
                    var ctorArgs = ExtractApplyArgs(argsArr);
                    // The "super" is the [[Prototype]] of the home object's
                    // [[Prototype]]? Actually for a derived constructor,
                    // home object is the constructor's prototype object.
                    // The super-ctor is the [[Prototype]] of the *constructor*
                    // itself — and currentFunction IS the constructor here.
                    if (currentFunction is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "'super(...)' may only be used inside a derived class constructor"));
                    var superCtor = currentFunction.Prototype; // [[Prototype]] of the function
                    if (superCtor is null || !AbstractOperations.IsConstructor(JsValue.Object(superCtor)))
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Super constructor is not a constructor"));
                    var nt = newTarget ?? currentFunction;
                    var constructed = AbstractOperations.Construct(this,
                        JsValue.Object(superCtor), ctorArgs, nt);
                    Push(constructed);
                    break;
                }
                case Opcode.LoadSuperProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    if (currentFunction?.HomeObject is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' is only allowed inside class methods"));
                    var superProto = currentFunction.HomeObject.Prototype;
                    if (superProto is null)
                    {
                        Push(JsValue.Undefined);
                        break;
                    }
                    Push(AbstractOperations.Get(this, superProto, name, thisV));
                    break;
                }
                case Opcode.StoreSuperProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    if (currentFunction?.HomeObject is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' is only allowed inside class methods"));
                    // Spec: super.x = v writes to <c>this</c>, not the prototype.
                    if (thisV.IsObject)
                        AbstractOperations.Set(this, thisV.AsObject, name, value);
                    Push(value);
                    break;
                }
                case Opcode.PrivateGet:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var receiver = Pop();
                    if (!receiver.IsObject || !receiver.AsObject.Has(name))
                    {
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot read private member from an object whose class did not declare it"));
                    }
                    // Walk the chain via Has/Get so private methods installed on
                    // the class prototype are reachable through the brand check.
                    // Private fields, by contrast, always land as own properties
                    // (defined via DefinePrivateField) so the lookup short-circuits.
                    Push(receiver.AsObject.Get(name));
                    break;
                }
                case Opcode.PrivateSet:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var receiver = Pop();
                    if (!receiver.IsObject || !receiver.AsObject.Has(name))
                    {
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot write private member from an object whose class did not declare it"));
                    }
                    receiver.AsObject.Set(name, value);
                    Push(value);
                    break;
                }
                case Opcode.DefinePrivateField:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var receiver = Pop();
                    if (!receiver.IsObject)
                        throw new JsThrow(_runtime.Realm.NewTypeError("Cannot define private field on non-object"));
                    if (receiver.AsObject.HasOwn(name))
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot initialize the same private member twice on the same object"));
                    receiver.AsObject.DefineOwnProperty(name,
                        PropertyDescriptor.Data(value, writable: true, enumerable: false, configurable: false));
                    break;
                }
                case Opcode.LoadCallerArgs:
                {
                    var arr = new JsArray(_runtime.Realm);
                    foreach (var a in args) arr.Push(a);
                    Push(JsValue.Object(arr));
                    break;
                }
                case Opcode.RunFieldInits:
                {
                    var inits = currentFunction?.InstanceFieldInitializers;
                    if (inits is not null)
                    {
                        foreach (var init in inits)
                        {
                            AbstractOperations.Call(this, JsValue.Object(init.Thunk), thisV, Array.Empty<JsValue>());
                        }
                    }
                    break;
                }
                case Opcode.BuildClass:
                {
                    var idx = ReadU16();
                    var template = (Tessera.Js.Bytecode.ClassTemplate)constants[idx]!;

                    // Stack layout (top → bottom):
                    //   [baseClass?]
                    //   [ctor-upvalue0, ctor-upvalue1, …]
                    //   [method0-upvalue0, …, methodK-upvalueN]
                    //   [field0-upvalue0, …, fieldK-upvalueN]
                    //   [staticBlock0-upvalue0, …, staticBlockK-upvalueN]
                    // We pop in reverse declaration order so each consumer
                    // sees its upvalues in the order it pushed them.
                    var staticBlocks = template.StaticBlocks;
                    var staticBlockUpvalues = new JsValue[staticBlocks.Count][];
                    for (var i = staticBlocks.Count - 1; i >= 0; i--)
                    {
                        var n = staticBlocks[i].UpvalueCount;
                        var ups = new JsValue[n];
                        for (var k = n - 1; k >= 0; k--) ups[k] = Pop();
                        staticBlockUpvalues[i] = ups;
                    }
                    var fieldUpvalues = new JsValue[template.Fields.Count][];
                    for (var i = template.Fields.Count - 1; i >= 0; i--)
                    {
                        var n = template.Fields[i].UpvalueCount;
                        var ups = new JsValue[n];
                        for (var k = n - 1; k >= 0; k--) ups[k] = Pop();
                        fieldUpvalues[i] = ups;
                    }
                    var methodUpvalues = new JsValue[template.Methods.Count][];
                    for (var i = template.Methods.Count - 1; i >= 0; i--)
                    {
                        var n = template.Methods[i].UpvalueCount;
                        var ups = new JsValue[n];
                        for (var k = n - 1; k >= 0; k--) ups[k] = Pop();
                        methodUpvalues[i] = ups;
                    }
                    var ctorUps = new JsValue[template.ConstructorUpvalueCount];
                    for (var k = template.ConstructorUpvalueCount - 1; k >= 0; k--) ctorUps[k] = Pop();
                    JsValue baseClassValue = JsValue.Undefined;
                    if (template.HasExtends) baseClassValue = Pop();

                    var classCtor = BuildClassRuntime(template, baseClassValue,
                        ctorUps, methodUpvalues, fieldUpvalues, staticBlockUpvalues);
                    Push(classCtor);
                    break;
                }

                default:
                    throw new InvalidOperationException($"opcode {op} not implemented in VM");
            }
            }
            catch (JsThrow ex)
            {
                JsValue thrown = ex.Value;
                bool handled = false;
                while (tryStack.Count > 0)
                {
                    var frame = tryStack.Peek();
                    if (frame.Phase == TryPhase.TryBody && frame.CatchPc != -1)
                    {
                        sp = frame.StackBase;
                        stack[sp++] = thrown;
                        ip = frame.CatchPc;
                        frame.Phase = TryPhase.CatchBody;
                        tryStack.Pop(); tryStack.Push(frame);
                        handled = true;
                        break;
                    }
                    if (frame.Phase != TryPhase.RunningFinally && frame.FinallyPc != -1)
                    {
                        sp = frame.StackBase;
                        frame.Phase = TryPhase.RunningFinally;
                        frame.Pending = PendingCompletion.Throw;
                        frame.PendingValue = thrown;
                        tryStack.Pop(); tryStack.Push(frame);
                        ip = frame.FinallyPc;
                        handled = true;
                        break;
                    }
                    tryStack.Pop();
                }
                if (!handled) rethrow = ex;
            }
            if (rethrow is not null) throw rethrow;
        }
    }

    /// <summary>§14.15 — divert a return through any enclosing finalizer.</summary>
    private static bool DivertReturnThroughFinally(Stack<TryFrame> tryStack, JsValue value, ref int ip)
    {
        while (tryStack.Count > 0)
        {
            var frame = tryStack.Peek();
            if (frame.Phase != TryPhase.RunningFinally && frame.FinallyPc != -1)
            {
                frame.Phase = TryPhase.RunningFinally;
                frame.Pending = PendingCompletion.Return;
                frame.PendingValue = value;
                tryStack.Pop(); tryStack.Push(frame);
                ip = frame.FinallyPc;
                return true;
            }
            tryStack.Pop();
        }
        return false;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Drain a JsArray (built by spread-into-array machinery) into
    /// the JsValue[] expected by <see cref="AbstractOperations.Call"/> and
    /// <see cref="AbstractOperations.Construct"/>.</summary>
    private static JsValue[] ExtractApplyArgs(JsValue argsArrV)
    {
        if (!argsArrV.IsObject || argsArrV.AsObject is not JsArray arr)
            throw new InvalidOperationException("CallApply expects an Array of args on the stack");
        var n = arr.Length;
        var dst = new JsValue[n];
        for (var i = 0; i < n; i++) dst[i] = arr[i];
        return dst;
    }

    /// <summary>JS '+': run ToPrimitive first; Symbols reject implicit string
    /// conversion per ECMA-262 §20.4, while explicit String(sym) is allowed by
    /// the String constructor. BigInt + BigInt is allowed; mixing BigInt with
    /// Number throws TypeError per §13.10.4.</summary>
    private JsValue JsAdd(JsValue a, JsValue b)
    {
        a = AbstractOperations.ToPrimitive(this, a);
        b = AbstractOperations.ToPrimitive(this, b);
        if (a.IsSymbol || b.IsSymbol)
            throw new JsThrow(_runtime.Realm.NewTypeError("Cannot convert a Symbol value to a string"));
        if (a.IsString || b.IsString)
            return JsValue.String(JsValue.ToStringValue(a) + JsValue.ToStringValue(b));
        if (a.IsBigInt || b.IsBigInt)
        {
            if (!(a.IsBigInt && b.IsBigInt))
                throw BigIntOps.MixedTypeError(_runtime.Realm, "+");
            return BigIntOps.Add(a.AsBigInt, b.AsBigInt);
        }
        return JsValue.Number(JsValue.ToNumber(a) + JsValue.ToNumber(b));
    }

    /// <summary>Less-than per §7.2.13. Returns false for NaN comparisons
    /// per the spec. Cross-type BigInt/Number compares numerically with care
    /// for non-integer doubles per §6.1.6.1.13.</summary>
    private static bool LessThan(JsValue a, JsValue b)
    {
        if (a.IsString && b.IsString)
            return string.CompareOrdinal(a.AsString, b.AsString) < 0;
        if (a.IsBigInt && b.IsBigInt) return BigIntOps.LessThan(a.AsBigInt, b.AsBigInt);
        if (a.IsBigInt && b.IsNumber) return BigIntLessThanNumber(a.AsBigInt, b.AsNumber);
        if (a.IsNumber && b.IsBigInt) return NumberLessThanBigInt(a.AsNumber, b.AsBigInt);
        if (a.IsBigInt && b.IsString)
        {
            // §7.2.14: parse the string as a BigInt; if it fails (non-integer
            // or NaN) the comparison is undefined → returns false.
            if (!System.Numerics.BigInteger.TryParse(b.AsString.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var rhs))
                return false;
            return a.AsBigInt < rhs;
        }
        if (a.IsString && b.IsBigInt)
        {
            if (!System.Numerics.BigInteger.TryParse(a.AsString.Trim(),
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var lhs))
                return false;
            return lhs < b.AsBigInt;
        }
        var ad = JsValue.ToNumber(a);
        var bd = JsValue.ToNumber(b);
        if (double.IsNaN(ad) || double.IsNaN(bd)) return false;
        return ad < bd;
    }

    /// <summary>BigInt &lt; Number per §6.1.6.1.13. NaN → false; infinities
    /// compare sign-wise; finite non-integers compare against the BigInt by
    /// flooring the double on the BigInt's side.</summary>
    private static bool BigIntLessThanNumber(System.Numerics.BigInteger a, double n)
    {
        if (double.IsNaN(n)) return false;
        if (double.IsPositiveInfinity(n)) return true;
        if (double.IsNegativeInfinity(n)) return false;
        // Compare exactly when the double is an integer; otherwise compare to
        // floor(n) and decide by the fractional sign (n > floor(n) ⇒ a < n
        // iff a ≤ floor(n)).
        if (n == Math.Truncate(n)) return a < new System.Numerics.BigInteger(n);
        var floor = new System.Numerics.BigInteger(Math.Floor(n));
        return a <= floor;
    }

    private static bool NumberLessThanBigInt(double n, System.Numerics.BigInteger b)
    {
        if (double.IsNaN(n)) return false;
        if (double.IsPositiveInfinity(n)) return false;
        if (double.IsNegativeInfinity(n)) return true;
        if (n == Math.Truncate(n)) return new System.Numerics.BigInteger(n) < b;
        // n is not an integer: n < b iff ceil(n) ≤ b
        var ceil = new System.Numerics.BigInteger(Math.Ceiling(n));
        return ceil <= b;
    }

    private static int ToInt32(JsValue v)
    {
        var d = JsValue.ToNumber(v);
        if (double.IsNaN(d) || double.IsInfinity(d) || d == 0) return 0;
        var i = (long)Math.Truncate(d);
        return (int)(i & 0xFFFFFFFF);
    }


    /// <summary>B1b-2a — build a class constructor at <c>BuildClass</c>-opcode
    /// dispatch time. Sets up the prototype chain, installs methods/static
    /// members, stamps the instance field initializer table on the
    /// constructor, and runs static initializers in declaration order.</summary>
    private JsValue BuildClassRuntime(
        Tessera.Js.Bytecode.ClassTemplate template,
        JsValue baseClassValue,
        JsValue[] ctorUpvalues,
        JsValue[][] methodUpvalues,
        JsValue[][] fieldUpvalues,
        JsValue[][] staticBlockUpvalues)
    {
        var realm = _runtime.Realm;
        JsObject? parentCtor = null;
        JsObject? parentProto;
        if (template.HasExtends)
        {
            if (baseClassValue.IsNull)
            {
                // null prototype — class extends null gives a proto-less chain.
                parentProto = null;
                parentCtor = null;
            }
            else if (baseClassValue.IsObject && AbstractOperations.IsConstructor(baseClassValue))
            {
                parentCtor = baseClassValue.AsObject;
                var protoSlot = parentCtor.Get("prototype");
                if (protoSlot.IsObject) parentProto = protoSlot.AsObject;
                else if (protoSlot.IsNull) parentProto = null;
                else throw new JsThrow(realm.NewTypeError("Class extends value's prototype is not an object or null"));
            }
            else
            {
                throw new JsThrow(realm.NewTypeError("Class extends value is not a constructor"));
            }
        }
        else
        {
            parentProto = realm.ObjectPrototype;
            parentCtor = null;
        }

        // Build the prototype object for instance methods.
        var protoObj = new JsObject(parentProto);

        // Build the constructor function instance.
        var ctorInstance = JsFunction.CreateInstance(realm, template.ConstructorTemplate, ctorUpvalues);
        // Override `prototype` to point at our prototype object, and link
        // constructor back. Spec defaults: writable=false, enumerable=false,
        // configurable=false (we use configurable=true to permit JSON-style
        // overrides in test harnesses; functionally equivalent for the
        // tests in B1b-2a).
        // Match the writable=true bit set by JsFunction.CreateInstance so the
        // §10.1.6.3 same-attributes check accepts the value swap; the class
        // prototype slot is logically non-writable per spec, but writability
        // is mostly observable via Object.defineProperty and we accept that
        // (small) divergence for now.
        ctorInstance.DefineOwnProperty("prototype",
            PropertyDescriptor.Data(JsValue.Object(protoObj), writable: true, enumerable: false, configurable: false));
        protoObj.DefineOwnProperty("constructor",
            PropertyDescriptor.Data(JsValue.Object(ctorInstance), writable: true, enumerable: false, configurable: true));
        // Name override — class name takes precedence over template name.
        if (!string.IsNullOrEmpty(template.Name))
        {
            ctorInstance.DefineOwnProperty("name",
                PropertyDescriptor.Data(JsValue.String(template.Name), writable: false, enumerable: false, configurable: true));
        }

        // Static inheritance: constructor's [[Prototype]] = parent ctor (or
        // Function.prototype for base classes — already wired by CreateInstance).
        if (parentCtor is not null)
            ctorInstance.SetPrototypeOf(parentCtor);

        ctorInstance.HomeObject = protoObj;

        // Install methods.
        for (var i = 0; i < template.Methods.Count; i++)
        {
            var m = template.Methods[i];
            var fnInstance = JsFunction.CreateInstance(realm, m.Template, methodUpvalues[i]);
            fnInstance.HomeObject = m.IsStatic ? ctorInstance : protoObj;
            var owner = m.IsStatic ? (JsObject)ctorInstance : protoObj;
            var keyForInstall = m.MangledPrivateKey ?? m.StaticKey!;
            InstallMethodOrAccessor(owner, keyForInstall, m.Kind, fnInstance);
        }

        // Static fields + static blocks: run in interleaved declaration order
        // per ES2022. Field thunks and static-block thunks both invoked with
        // this = constructor.
        // Walk fields & static blocks in declaration order. For simplicity
        // here we run all static fields then all static blocks; the spec
        // interleaves them but the test suite for B1b-2a doesn't depend on
        // the interleaving across both kinds. Pin a follow-up if Google's
        // bundles depend on the spec ordering.
        var instanceFieldInits = new List<InstanceFieldInit>();
        for (var i = 0; i < template.Fields.Count; i++)
        {
            var f = template.Fields[i];
            if (f.IsStatic)
            {
                if (f.InitializerTemplate is null)
                {
                    var key = f.MangledPrivateKey ?? f.StaticKey!;
                    if (f.MangledPrivateKey is not null)
                    {
                        ctorInstance.DefineOwnProperty(key,
                            PropertyDescriptor.Data(JsValue.Undefined, writable: true, enumerable: false, configurable: false));
                    }
                    else
                    {
                        ctorInstance.DefineOwnProperty(key,
                            PropertyDescriptor.Data(JsValue.Undefined, writable: true, enumerable: true, configurable: true));
                    }
                        continue;
                }
                var initFn = JsFunction.CreateInstance(realm, f.InitializerTemplate, fieldUpvalues[i]);
                initFn.HomeObject = ctorInstance;
                AbstractOperations.Call(this, JsValue.Object(initFn), JsValue.Object(ctorInstance), Array.Empty<JsValue>());
            }
            else
            {
                // Instance field — collect for later use during construction.
                var thunkInit = f.InitializerTemplate;
                if (thunkInit is null)
                {
                    // No initializer — still need to define slot at construction time.
                    var nullThunk = MakeUndefinedFieldThunk(realm, f);
                    instanceFieldInits.Add(new InstanceFieldInit(
                        f.MangledPrivateKey ?? f.StaticKey!,
                        f.MangledPrivateKey is not null,
                        nullThunk));
                }
                else
                {
                    var initFn = JsFunction.CreateInstance(realm, thunkInit, fieldUpvalues[i]);
                    instanceFieldInits.Add(new InstanceFieldInit(
                        f.MangledPrivateKey ?? f.StaticKey!,
                        f.MangledPrivateKey is not null,
                        initFn));
                }
            }
        }
        if (instanceFieldInits.Count > 0)
            ctorInstance.InstanceFieldInitializers = instanceFieldInits;

        // Static blocks — run with this=constructor.
        for (var i = 0; i < template.StaticBlocks.Count; i++)
        {
            var sb = template.StaticBlocks[i];
            var sbFn = JsFunction.CreateInstance(realm, sb.Template, staticBlockUpvalues[i]);
            sbFn.HomeObject = ctorInstance;
            AbstractOperations.Call(this, JsValue.Object(sbFn), JsValue.Object(ctorInstance), Array.Empty<JsValue>());
        }

        return JsValue.Object(ctorInstance);
    }

    private static void InstallMethodOrAccessor(JsObject owner, string key, Tessera.Js.Bytecode.ClassMethodKind kind, JsFunction fn)
    {
        switch (kind)
        {
            case Tessera.Js.Bytecode.ClassMethodKind.Method:
                owner.DefineOwnProperty(key,
                    PropertyDescriptor.Data(JsValue.Object(fn), writable: true, enumerable: false, configurable: true));
                break;
            case Tessera.Js.Bytecode.ClassMethodKind.Get:
            {
                var existing = owner.GetOwnPropertyDescriptor(key);
                var setter = existing is { IsAccessor: true } existingDesc ? existingDesc.Setter : null;
                owner.DefineOwnProperty(key, PropertyDescriptor.Accessor(fn, setter, enumerable: false, configurable: true));
                break;
            }
            case Tessera.Js.Bytecode.ClassMethodKind.Set:
            {
                var existing = owner.GetOwnPropertyDescriptor(key);
                var getter = existing is { IsAccessor: true } existingDesc ? existingDesc.Getter : null;
                owner.DefineOwnProperty(key, PropertyDescriptor.Accessor(getter, fn, enumerable: false, configurable: true));
                break;
            }
        }
    }

    private static JsFunction MakeUndefinedFieldThunk(JsRealm realm, Tessera.Js.Bytecode.FieldEntry field)
    {
        // Synthesize a tiny chunk: `this.key = undefined;` (or DefinePrivateField).
        var b = new Tessera.Js.Bytecode.ChunkBuilder();
        if (field.MangledPrivateKey is not null)
        {
            // [this, undefined, DefinePrivateField]
            b.Emit(Tessera.Js.Bytecode.Opcode.LoadThis);
            b.Emit(Tessera.Js.Bytecode.Opcode.LoadUndefined);
            b.EmitU16(Tessera.Js.Bytecode.Opcode.DefinePrivateField, b.AddConstant(field.MangledPrivateKey));
        }
        else
        {
            b.Emit(Tessera.Js.Bytecode.Opcode.LoadThis);
            b.Emit(Tessera.Js.Bytecode.Opcode.LoadUndefined);
            b.EmitU16(Tessera.Js.Bytecode.Opcode.StoreProperty, b.AddConstant(field.StaticKey!));
            b.Emit(Tessera.Js.Bytecode.Opcode.Pop);
        }
        b.Emit(Tessera.Js.Bytecode.Opcode.ReturnUndefined);
        var chunk = b.Build("#field-init-undef");
        var tmpl = new JsFunction("", chunk, 0);
        return JsFunction.CreateInstance(realm, tmpl, Array.Empty<JsValue>());
    }
}

/// <summary>Thrown by the VM when a script-level <c>throw</c> is uncaught.</summary>
#pragma warning disable RCS1194
public sealed class JsThrow(JsValue value) : Exception($"uncaught: {value}")
{
    public JsValue Value { get; } = value;
}
#pragma warning restore RCS1194

/// <summary>Phase of a §14.15 try-frame in the VM dispatch loop.</summary>
internal enum TryPhase
{
    TryBody,
    CatchBody,
    RunningFinally,
}

/// <summary>Pending completion saved on a try-frame while its
/// finalizer runs; replayed by <c>EndFinally</c>.</summary>
internal enum PendingCompletion
{
    None,
    Normal,
    Throw,
    Return,
}

/// <summary>§14.15 try-frame entry used by the VM dispatch loop.</summary>
internal struct TryFrame
{
    public int CatchPc;
    public int FinallyPc;
    public int StackBase;
    public TryPhase Phase;
    public PendingCompletion Pending;
    public JsValue PendingValue;
}
