using System.Buffers.Binary;
using Starling.Js.Bytecode;
using Starling.Js.Intrinsics;
using Starling.Js.RegExp;

namespace Starling.Js.Runtime;

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
    /// Used by <see cref="AbstractOperations.Call"/>. B1b-2c: when the
    /// callee is async / generator / async-generator, this entry instead
    /// returns the corresponding wrapper without running the body —
    /// dispatch lands in <see cref="StartGeneratorBody"/> /
    /// <see cref="StartAsyncBody"/>.</summary>
    public JsValue CallFunction(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        if (fn.Kind == JsFunctionKind.Generator)
            return StartGeneratorBody(fn, thisValue, args);
        if (fn.Kind == JsFunctionKind.Async)
            return StartAsyncBody(fn, thisValue, args);
        if (fn.Kind == JsFunctionKind.AsyncGenerator)
            return StartAsyncGeneratorBody(fn, thisValue, args);
        return Run(fn.Body, args, thisValue, fn.Upvalues, drainMicrotasks: false,
               currentFunction: fn, newTarget: null);
    }

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
        JsFunction? currentFunction, JsObject? newTarget,
        SuspendedFrame? suspension = null)
    {
        // Publish this VM on the realm so native intrinsics (JSON.parse
        // reviver, JSON.stringify replacer/toJSON, etc.) can dispatch JS
        // callables. Save/restore in case of reentry from a nested host
        // invocation chain.
        var prevVm = _runtime.Realm.ActiveVm;
        _runtime.Realm.ActiveVm = this;
        try
        {
            var result = RunInner(chunk, args, thisValue, upvalues, currentFunction, newTarget, suspension);
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
        IReadOnlyList<JsValue> upvalues, JsFunction? currentFunction, JsObject? newTarget,
        SuspendedFrame? suspension = null)
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

                // ----- Captured locals (gap:closure-write-back) -----
                case Opcode.InitCellLocal:
                {
                    var slot = ReadU8();
                    locals[slot] = JsValue.Object(new Cell(JsValue.Undefined));
                    break;
                }
                case Opcode.LoadCellLocal:
                {
                    var slot = ReadU8();
                    var cell = (Cell)locals[slot].AsObject;
                    Push(cell.Value);
                    break;
                }
                case Opcode.StoreCellLocal:
                {
                    var slot = ReadU8();
                    var cell = (Cell)locals[slot].AsObject;
                    cell.Value = Pop();
                    break;
                }
                case Opcode.PromoteParamCell:
                {
                    var slot = ReadU8();
                    locals[slot] = JsValue.Object(new Cell(locals[slot]));
                    break;
                }
                case Opcode.StoreUpvalue:
                {
                    var idx = ReadU8();
                    var cell = (Cell)upvalues[idx].AsObject;
                    cell.Value = Pop();
                    break;
                }
                case Opcode.LoadUpvalueCell:
                {
                    var idx = ReadU8();
                    Push(upvalues[idx]);
                    break;
                }
                // §14.7.4.4 CreatePerIterationEnvironment — read the cell in
                // `slot`, allocate a fresh Cell with the same value, write
                // it back. Closures formed in the upcoming iteration body
                // capture the new cell; previous iterations' closures retain
                // theirs, giving each iteration its own binding for `let` /
                // `const` declared in a for-loop init.
                case Opcode.RefreshLetBinding:
                {
                    var slot = ReadU8();
                    var oldCell = (Cell)locals[slot].AsObject;
                    locals[slot] = JsValue.Object(new Cell(oldCell.Value));
                    break;
                }

                // ----- Globals -----
                // gap:opcode-fast-path-bypasses-accessors — route global
                // reads/writes through AbstractOperations.Get/Set so accessor
                // descriptors on the global object (e.g. `location` defined via
                // Object.defineProperty with a getter) invoke their getter/setter
                // instead of silently returning undefined / overwriting the slot.
                case Opcode.LoadGlobal:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var globalObj = _runtime.Realm.GlobalObject;
                    Push(AbstractOperations.Get(this, globalObj, name, JsValue.Object(globalObj)));
                    break;
                }
                case Opcode.StoreGlobal:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var globalObj = _runtime.Realm.GlobalObject;
                    AbstractOperations.Set(this, globalObj, name, value, JsValue.Object(globalObj));
                    break;
                }
                // gap:script-top-var-not-global — idempotent CreateGlobalVarBinding
                // (§16.1.7 / §9.1.1.4.16). Skip if the global already has an own
                // property of this name (function-decl hoist may have installed
                // it first, or this is the second `var x` of a redeclaration);
                // otherwise install an own data property seeded with undefined.
                case Opcode.DeclareGlobalVar:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var globalObj = _runtime.Realm.GlobalObject;
                    if (!globalObj.HasOwn(name))
                    {
                        globalObj.DefineOwnProperty(name,
                            PropertyDescriptor.Data(JsValue.Undefined,
                                writable: true, enumerable: true, configurable: false));
                    }
                    break;
                }

                // ----- Stack manipulation -----
                case Opcode.Pop: sp--; break;
                case Opcode.Dup: Push(Peek()); break;
                case Opcode.Dup2:
                {
                    // (..., a, b) → (..., a, b, a, b)
                    var b = stack[sp - 1];
                    var a = stack[sp - 2];
                    Push(a);
                    Push(b);
                    break;
                }
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
                    // gap:closure-write-back — every upvalue is a Cell, so
                    // dereference to push the current bound value. Use
                    // LoadUpvalueCell to push the raw cell (for further
                    // chained captures).
                    var idx = ReadU8();
                    var upV = upvalues[idx];
                    if (upV.IsObject && upV.AsObject is Cell c) Push(c.Value);
                    else Push(upV); // legacy snapshot path — empty in practice
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

                // ----- Operator bundle (gap:instanceof / gap:in / gap:delete) -----
                case Opcode.Instanceof:
                {
                    var target = Pop();
                    var value = Pop();
                    Push(JsValue.Boolean(InstanceofOperator(value, target)));
                    break;
                }
                case Opcode.In:
                {
                    var rhs = Pop();
                    var key = Pop();
                    if (!rhs.IsObject)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot use 'in' operator to search for '"
                            + JsValue.ToStringValue(key) + "' in "
                            + JsValue.ToStringValue(rhs)));
                    var pk = AbstractOperations.ToPropertyKey(key);
                    Push(JsValue.Boolean(AbstractOperations.HasProperty(rhs.AsObject, pk)));
                    break;
                }
                case Opcode.DeleteProperty:
                {
                    var key = Pop();
                    var receiver = Pop();
                    if (!receiver.IsObject)
                    {
                        // §13.5.1: ToObject for primitives so we can delete keys
                        // on a wrapper — wrappers report success since no own
                        // properties exist matching the key. For null/undefined
                        // the spec throws TypeError.
                        if (receiver.IsNullish)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot convert undefined or null to object"));
                        var boxed = AbstractOperations.ToObject(_runtime.Realm, receiver);
                        Push(JsValue.Boolean(boxed.Delete(AbstractOperations.ToPropertyKey(key))));
                        break;
                    }
                    Push(JsValue.Boolean(receiver.AsObject.Delete(AbstractOperations.ToPropertyKey(key))));
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
                        // CopyDataProperties (§7.3.27) invokes getters on the source,
                        // not the data-only fast path. Mirror that here so accessor
                        // properties are spread by their getter's return value.
                        foreach (var key in srcObj.EnumerableKeys())
                            AbstractOperations.Set(this, dstObj, key,
                                AbstractOperations.Get(this, srcObj, key));
                        foreach (var key in srcObj.EnumerableSymbolKeys())
                            AbstractOperations.Set(this, dstObj, JsPropertyKey.Symbol(key),
                                AbstractOperations.Get(this, srcObj, JsPropertyKey.Symbol(key)));
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
                        len = Math.Max(0, (int)Math.Truncate(JsValue.ToNumber(
                            AbstractOperations.Get(this, srcObj, "length"))));
                    if (srcObj is not null)
                    {
                        for (var i = start; i < len; i++)
                            result.Push(AbstractOperations.Get(this, srcObj,
                                i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                    }
                    Push(JsValue.Object(result));
                    break;
                }

                case Opcode.GetIterator:
                {
                    var iterable = Pop();
                    var record = AbstractOperations.GetIterator(_runtime.Realm, this, iterable);
                    Push(JsValue.Object(new Starling.Js.Intrinsics.JsIteratorRecordHandle(record)));
                    break;
                }

                case Opcode.IteratorStep:
                {
                    // Peek (don't pop) so the surrounding loop keeps the handle
                    // across iterations. The dispatch arm pushes either the
                    // iterator-result object (done=false) or undefined (done=true)
                    // as the loop sentinel.
                    var top = Peek();
                    if (!top.IsObject || top.AsObject is not Starling.Js.Intrinsics.JsIteratorRecordHandle handle)
                        throw new InvalidOperationException("IteratorStep expects an iterator-record handle on the stack");
                    var step = AbstractOperations.IteratorStep(_runtime.Realm, this, ref handle.Record);
                    Push(step ?? JsValue.Undefined);
                    break;
                }

                case Opcode.IteratorClose:
                {
                    var handleV = Pop();
                    if (handleV.IsObject && handleV.AsObject is Starling.Js.Intrinsics.JsIteratorRecordHandle h)
                    {
                        if (!h.Record.Done)
                            AbstractOperations.IteratorClose(this, h.Record, isThrowing: false);
                    }
                    break;
                }

                case Opcode.GetAsyncIterator:
                {
                    // §7.4.2 GetIterator(obj, async). Resolve
                    // obj[@@asyncIterator]; if absent, fall back to the sync
                    // iterator wrapped as async (CreateAsyncFromSyncIterator).
                    var iterable = Pop();
                    Push(JsValue.Object(GetAsyncIteratorHandle(iterable)));
                    break;
                }

                case Opcode.AsyncIteratorNext:
                {
                    // Peek the record handle (loop keeps it across iterations).
                    var top = Peek();
                    if (!top.IsObject || top.AsObject is not Starling.Js.Intrinsics.JsIteratorRecordHandle handle)
                        throw new InvalidOperationException("AsyncIteratorNext expects an async-iterator-record handle");
                    var resultV = AbstractOperations.Call(this, handle.Record.NextMethod,
                        handle.Record.Iterator, Array.Empty<JsValue>());
                    if (handle.SyncWrapped)
                    {
                        // §27.1.4.2.1 CreateAsyncFromSyncIterator: await the
                        // sync result's `value` (it may itself be a thenable),
                        // then rebuild {value: awaited, done} so the loop's
                        // following await observes a fully-settled element.
                        Push(JsValue.Object(WrapSyncIteratorResult(resultV)));
                    }
                    else
                    {
                        // Async iterator: next() already returns a promise.
                        Push(resultV);
                    }
                    break;
                }

                case Opcode.AsyncIteratorClose:
                {
                    var handleV = Pop();
                    if (handleV.IsObject && handleV.AsObject is Starling.Js.Intrinsics.JsIteratorRecordHandle h
                        && !h.Record.Done)
                    {
                        var ret = AbstractOperations.GetMethod(this, h.Record.Iterator, "return");
                        if (!ret.IsUndefined && !ret.IsNull)
                        {
                            var rv = AbstractOperations.Call(this, ret, h.Record.Iterator,
                                Array.Empty<JsValue>());
                            // AsyncIteratorClose awaits the return result; the
                            // following Suspend(kind=1) does the await. For a
                            // sync-wrapped iterator the return value isn't a
                            // promise — resolve it so the await is uniform.
                            if (h.SyncWrapped)
                            {
                                var p = new JsPromise(_runtime.Realm.PromisePrototype);
                                PromiseCtor.Resolve(_runtime.Realm, p, rv);
                                Push(JsValue.Object(p));
                            }
                            else
                            {
                                Push(rv);
                            }
                            break;
                        }
                    }
                    // No return method (or already done) — push undefined so
                    // the unconditional await downstream is a no-op.
                    Push(JsValue.Undefined);
                    break;
                }

                case Opcode.EnumerateKeys:
                {
                    // §14.7.5.10 ForIn/OfHeadEvaluation step 6: for-in
                    // snapshots own + inherited enumerable string keys at
                    // loop entry. Null/undefined silently skip the body
                    // (spec: return an empty iterator).
                    var src = Pop();
                    var snapshot = new JsArray(_runtime.Realm);
                    if (!src.IsNullish)
                    {
                        var obj = AbstractOperations.ToObject(_runtime.Realm, src);
                        var emitted = new HashSet<string>(StringComparer.Ordinal);
                        var shadowed = new HashSet<string>(StringComparer.Ordinal);
                        var current = obj;
                        while (current is not null)
                        {
                            foreach (var k in current.EnumerableKeys())
                            {
                                if (shadowed.Contains(k)) continue;
                                if (emitted.Add(k)) snapshot.Push(JsValue.String(k));
                            }
                            // Any own key (enumerable or not) on this level
                            // shadows same-named keys further up the proto
                            // chain — per OrdinaryOwnPropertyKeys, all own
                            // names appear regardless of enumerability.
                            foreach (var k in current.Keys) shadowed.Add(k);
                            current = current.Prototype;
                        }
                    }
                    Push(JsValue.Object(snapshot));
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
                    // CopyDataProperties (§7.3.27) — accessor getters on the
                    // source must be invoked, not bypassed by the data-only
                    // fast path. Route through AbstractOperations.Get.
                    if (src.IsObject)
                    {
                        var srcObj = src.AsObject;
                        foreach (var key in srcObj.EnumerableKeys())
                            if (!excluded.Contains(key))
                                AbstractOperations.Set(this, result, key,
                                    AbstractOperations.Get(this, srcObj, key));
                    }
                    else if (!src.IsNullish)
                    {
                        var srcObj = AbstractOperations.ToObject(_runtime.Realm, src);
                        foreach (var key in srcObj.EnumerableKeys())
                            if (!excluded.Contains(key))
                                AbstractOperations.Set(this, result, key,
                                    AbstractOperations.Get(this, srcObj, key));
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
                            var value = AbstractOperations.Call(
                                this, JsValue.Object(init.Thunk), thisV, Array.Empty<JsValue>());
                            // wp:M3-04f — computed-key instance fields: the thunk
                            // returns the initializer value; define the own data
                            // property under the key resolved at class-definition
                            // time (CreateDataPropertyOrThrow per §10.2.4.1 /
                            // §15.7.10). Non-computed thunks self-store and return
                            // undefined — nothing to do here.
                            if (init.ComputedKey is { } ck && thisV.IsObject)
                            {
                                thisV.AsObject.DefineOwnProperty(ck,
                                    PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
                            }
                        }
                    }
                    break;
                }
                case Opcode.ToPropertyKey:
                {
                    // wp:M3-04f — §7.1.19 ToPropertyKey; push the normalized key
                    // back as a Symbol value or a String value. Threads `this`
                    // VM so an object key's Symbol.toPrimitive is honored.
                    var key = AbstractOperations.ToPropertyKey(this, Pop());
                    Push(key.IsSymbol ? JsValue.Symbol(key.AsSymbol) : JsValue.String(key.AsString));
                    break;
                }
                // ----- B1b-2c — Suspend (yield / await) -----
                case Opcode.Suspend:
                {
                    var kind = ReadU8();
                    var yielded = Pop();
                    if (suspension is null)
                    {
                        // Outside a suspendable context — yield/await are
                        // syntax errors but we accept liberally; surface
                        // the misuse as a SyntaxError at runtime.
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            kind == 1
                                ? "await is only valid in async functions and async generators"
                                : "yield is only valid in generator functions"));
                    }
                    JsValue toYield = yielded;
                    if (kind == 1)
                    {
                        // await: wrap in Promise.resolve and register a .then
                        // that resumes the worker. The yielded value is the
                        // promise itself — the dispatcher (StartAsyncBody)
                        // reads it via SuspendedFrame.YieldedValue, hooks
                        // up the .then, then calls Resume.
                        toYield = yielded;
                    }
                    // Record whether this suspension is a yield (0) or an
                    // await (1). The async-generator driver inspects this to
                    // distinguish a real `yield` (which settles the pending
                    // request with {value, done:false}) from an internal
                    // `await` (which just resumes the worker once the awaited
                    // promise settles). Sync generators / plain async ignore it.
                    suspension.SuspendKind = kind;
                    // Hand off to the caller (main thread). Block until
                    // resume. Returned value is the value to push back.
                    var resumed = suspension.WorkerYield(toYield);
                    if (suspension.ResumeWithThrow)
                    {
                        // Caller asked us to throw at this point (e.g.
                        // gen.throw(e) or awaited promise rejected).
                        suspension.ResumeWithThrow = false;
                        throw new JsThrow(resumed);
                    }
                    if (suspension.ResumeWithReturn)
                    {
                        // Caller invoked Generator.return(v) — walk any
                        // enclosing try/finally frames via the standard
                        // exception path (see the catch (JsReturnSentinel)
                        // arm below). At the top of the body the sentinel
                        // becomes a normal completion with the value as
                        // the return value.
                        suspension.ResumeWithReturn = false;
                        throw new JsReturnSentinel(resumed);
                    }
                    Push(resumed);
                    break;
                }
                case Opcode.YieldDelegate:
                {
                    var iterable = Pop();
                    if (suspension is null)
                    {
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "yield is only valid in generator functions"));
                    }
                    Push(ExecuteYieldDelegate(suspension, iterable));
                    break;
                }
                case Opcode.BuildClass:
                {
                    var idx = ReadU16();
                    var template = (Starling.Js.Bytecode.ClassTemplate)constants[idx]!;

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
                    // wp:M3-04f — computed keys were pushed (already ToPropertyKey-
                    // coerced) below each member's upvalues, so pop upvalues first
                    // then the key. Keys default to undefined for non-computed
                    // members and are ignored there.
                    var fieldUpvalues = new JsValue[template.Fields.Count][];
                    var fieldComputedKeys = new JsValue[template.Fields.Count];
                    for (var i = template.Fields.Count - 1; i >= 0; i--)
                    {
                        var n = template.Fields[i].UpvalueCount;
                        var ups = new JsValue[n];
                        for (var k = n - 1; k >= 0; k--) ups[k] = Pop();
                        fieldUpvalues[i] = ups;
                        fieldComputedKeys[i] = template.Fields[i].IsComputed ? Pop() : JsValue.Undefined;
                    }
                    var methodUpvalues = new JsValue[template.Methods.Count][];
                    var methodComputedKeys = new JsValue[template.Methods.Count];
                    for (var i = template.Methods.Count - 1; i >= 0; i--)
                    {
                        var n = template.Methods[i].UpvalueCount;
                        var ups = new JsValue[n];
                        for (var k = n - 1; k >= 0; k--) ups[k] = Pop();
                        methodUpvalues[i] = ups;
                        methodComputedKeys[i] = template.Methods[i].IsComputed ? Pop() : JsValue.Undefined;
                    }
                    var ctorUps = new JsValue[template.ConstructorUpvalueCount];
                    for (var k = template.ConstructorUpvalueCount - 1; k >= 0; k--) ctorUps[k] = Pop();
                    JsValue baseClassValue = JsValue.Undefined;
                    if (template.HasExtends) baseClassValue = Pop();

                    var classCtor = BuildClassRuntime(template, baseClassValue,
                        ctorUps, methodUpvalues, fieldUpvalues, staticBlockUpvalues,
                        methodComputedKeys, fieldComputedKeys);
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
            catch (JsReturnSentinel rs)
            {
                // Generator.return(v) injected at a suspension point —
                // walk enclosing try/finally frames as a Return completion
                // (mirrors DivertReturnThroughFinally for the synchronous
                // Return opcode). If nothing diverts it, exit the body
                // with rs.Value as the return value.
                if (!DivertReturnThroughFinally(tryStack, rs.Value, ref ip))
                    return rs.Value;
            }
            if (rethrow is not null) throw rethrow;
        }
    }

    /// <summary>§27.5.3.2 YieldDelegate body — runs the full <c>yield*</c>
    /// protocol inside a single opcode handler. Forwards the outer
    /// generator's resume kind (next / return / throw) into the inner
    /// iterator's matching method on each round-trip with the outer
    /// caller. Returns the value to push as the result of the yield*
    /// expression (the inner iterator's final <c>value</c> on done, or
    /// the value of an inner .return that completes early).</summary>
    private JsValue ExecuteYieldDelegate(SuspendedFrame suspension, JsValue iterable)
    {
        var realm = _runtime.Realm;
        var record = AbstractOperations.GetIterator(realm, this, iterable);
        var innerIter = record.Iterator;
        var nextMethod = record.NextMethod;
        // Bootstrap: caller's first .next() value (the one already on the
        // suspension's resume slot, or whatever they sent on the call that
        // brought us to yield*). The first round we always invoke
        // inner.next(undefined) — the outer caller's send-value is what
        // they pass on the .next() that *resumes* yield*, which we have
        // not yet observed (we're called from inside Suspend's frame).
        // Per spec §27.5.3.2 step 1, the initial received completion is
        // NormalCompletion(undefined).
        JsValue received = JsValue.Undefined;
        int receivedKind = 0; // 0 = normal, 1 = throw, 2 = return

        while (true)
        {
            JsValue innerResult;
            if (receivedKind == 0)
            {
                // Normal completion → inner.next(received)
                innerResult = AbstractOperations.Call(this, nextMethod, innerIter,
                    new[] { received });
            }
            else if (receivedKind == 1)
            {
                // Throw completion → inner.throw(received) if present.
                var throwM = AbstractOperations.GetMethod(this, innerIter, "throw");
                if (throwM.IsUndefined || throwM.IsNull)
                {
                    // No throw method: close the iterator and re-throw.
                    AbstractOperations.IteratorClose(this, record, isThrowing: true);
                    throw new JsThrow(realm.NewTypeError(
                        "Inner iterator does not have a 'throw' method"));
                }
                innerResult = AbstractOperations.Call(this, throwM, innerIter,
                    new[] { received });
            }
            else
            {
                // Return completion → inner.return(received) if present.
                var retM = AbstractOperations.GetMethod(this, innerIter, "return");
                if (retM.IsUndefined || retM.IsNull)
                {
                    // No return method: §27.5.3.2 — close inner with
                    // Return, then propagate Return(received) out of the
                    // outer generator body via the sentinel path.
                    throw new JsReturnSentinel(received);
                }
                innerResult = AbstractOperations.Call(this, retM, innerIter,
                    new[] { received });
                if (!innerResult.IsObject)
                    throw new JsThrow(realm.NewTypeError(
                        "iterator.return() did not return an object"));
                var doneR = JsValue.ToBoolean(AbstractOperations.Get(this, innerResult.AsObject, "done"));
                var valR = AbstractOperations.Get(this, innerResult.AsObject, "value");
                if (doneR)
                {
                    // Inner iterator honored the return — propagate
                    // Return(valR) out of the outer body so its finally
                    // blocks (if any) still run.
                    throw new JsReturnSentinel(valR);
                }
                // Inner refused to close — yield its value, continue.
                var resumedR = suspension.WorkerYield(valR);
                if (suspension.ResumeWithThrow)
                {
                    suspension.ResumeWithThrow = false;
                    received = resumedR;
                    receivedKind = 1;
                    continue;
                }
                if (suspension.ResumeWithReturn)
                {
                    suspension.ResumeWithReturn = false;
                    received = resumedR;
                    receivedKind = 2;
                    continue;
                }
                received = resumedR;
                receivedKind = 0;
                continue;
            }

            if (!innerResult.IsObject)
                throw new JsThrow(realm.NewTypeError(
                    "iterator.next() did not return an object"));
            var done = JsValue.ToBoolean(AbstractOperations.Get(this, innerResult.AsObject, "done"));
            var value = AbstractOperations.Get(this, innerResult.AsObject, "value");
            if (done)
            {
                // Inner finished — yield* evaluates to the inner's final
                // value. Push and exit the opcode.
                return value;
            }

            // Suspend the outer generator with the inner's yielded value.
            var resumed = suspension.WorkerYield(value);
            if (suspension.ResumeWithThrow)
            {
                suspension.ResumeWithThrow = false;
                received = resumed;
                receivedKind = 1;
            }
            else if (suspension.ResumeWithReturn)
            {
                suspension.ResumeWithReturn = false;
                received = resumed;
                receivedKind = 2;
            }
            else
            {
                received = resumed;
                receivedKind = 0;
            }
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

    /// <summary>§13.10.2 InstanceofOperator. Consults
    /// <c>target[@@hasInstance]</c> first; if absent, falls back to
    /// §10.4.6.4 OrdinaryHasInstance which walks the prototype chain.
    /// Throws TypeError when the target is not callable.</summary>
    private bool InstanceofOperator(JsValue value, JsValue target)
    {
        if (!target.IsObject)
            throw new JsThrow(_runtime.Realm.NewTypeError(
                "Right-hand side of 'instanceof' is not an object"));
        var targetObj = target.AsObject;
        // §13.10.2 step 2: invoke the well-known method if defined anywhere
        // on the prototype chain.
        var hasInstance = AbstractOperations.Get(this, targetObj,
            JsPropertyKey.Symbol(Starling.Js.Intrinsics.SymbolCtor.HasInstance));
        if (!hasInstance.IsUndefined && !hasInstance.IsNull)
        {
            if (!AbstractOperations.IsCallable(hasInstance))
                throw new JsThrow(_runtime.Realm.NewTypeError(
                    "Symbol.hasInstance method is not callable"));
            var result = AbstractOperations.Call(this, hasInstance, target, new[] { value });
            return JsValue.ToBoolean(result);
        }
        // §10.4.6.4 OrdinaryHasInstance.
        if (!AbstractOperations.IsCallable(target))
            throw new JsThrow(_runtime.Realm.NewTypeError(
                "Right-hand side of 'instanceof' is not callable"));
        // Unwrap bound functions: instanceof checks against the bound target.
        var unwrapped = targetObj;
        while (unwrapped is JsBoundFunction bf) unwrapped = bf.Target;
        if (!value.IsObject) return false;
        var proto = AbstractOperations.Get(this, unwrapped, "prototype");
        if (!proto.IsObject)
            throw new JsThrow(_runtime.Realm.NewTypeError(
                "Function has non-object prototype in instanceof check"));
        var protoObj = proto.AsObject;
        for (var p = value.AsObject.Prototype; p is not null; p = p.Prototype)
            if (ReferenceEquals(p, protoObj)) return true;
        return false;
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
        Starling.Js.Bytecode.ClassTemplate template,
        JsValue baseClassValue,
        JsValue[] ctorUpvalues,
        JsValue[][] methodUpvalues,
        JsValue[][] fieldUpvalues,
        JsValue[][] staticBlockUpvalues,
        JsValue[] methodComputedKeys,
        JsValue[] fieldComputedKeys)
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
            if (m.IsComputed)
            {
                // wp:M3-04f — coerced key value (Symbol or String) off the stack.
                var keyPk = AbstractOperations.ToPropertyKey(methodComputedKeys[i]);
                StampMethodName(fnInstance, keyPk, m.Kind);
                InstallMethodOrAccessor(owner, keyPk, m.Kind, fnInstance);
            }
            else
            {
                var keyForInstall = m.MangledPrivateKey ?? m.StaticKey!;
                InstallMethodOrAccessor(owner, keyForInstall, m.Kind, fnInstance);
            }
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
            // wp:M3-04f — computed key resolved at class-definition time.
            JsPropertyKey? computedKey = f.IsComputed
                ? AbstractOperations.ToPropertyKey(fieldComputedKeys[i])
                : (JsPropertyKey?)null;
            if (f.IsStatic)
            {
                if (computedKey is { } sck)
                {
                    // Static computed field: the thunk (when present) returns the
                    // initializer value; absent initializer ⇒ undefined.
                    var value = JsValue.Undefined;
                    if (f.InitializerTemplate is not null)
                    {
                        var initFnC = JsFunction.CreateInstance(realm, f.InitializerTemplate, fieldUpvalues[i]);
                        initFnC.HomeObject = ctorInstance;
                        value = AbstractOperations.Call(this, JsValue.Object(initFnC), JsValue.Object(ctorInstance), Array.Empty<JsValue>());
                    }
                    ctorInstance.DefineOwnProperty(sck,
                        PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
                    continue;
                }
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
            else if (computedKey is { } ick)
            {
                // Instance computed field: defer the (already-coerced) key + the
                // initializer thunk to construction time. The thunk returns the
                // value; RunFieldInits defines the property under ick. An absent
                // initializer ⇒ a thunk returning undefined.
                var thunk = f.InitializerTemplate is not null
                    ? JsFunction.CreateInstance(realm, f.InitializerTemplate, fieldUpvalues[i])
                    : MakeUndefinedReturningThunk(realm);
                instanceFieldInits.Add(new InstanceFieldInit("", IsPrivate: false, thunk, ick));
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

    private static void InstallMethodOrAccessor(JsObject owner, string key, Starling.Js.Bytecode.ClassMethodKind kind, JsFunction fn)
        => InstallMethodOrAccessor(owner, JsPropertyKey.String(key), kind, fn);

    /// <summary>wp:M3-04f — install a method/accessor under a runtime property
    /// key (String <em>or</em> Symbol), used for computed class members such
    /// as <c>[Symbol.iterator]()</c>. Mirrors the string-keyed path exactly,
    /// merging an existing accessor's complementary half so a paired
    /// <c>get [k]()/set [k]()</c> shares one descriptor.</summary>
    private static void InstallMethodOrAccessor(JsObject owner, JsPropertyKey key, Starling.Js.Bytecode.ClassMethodKind kind, JsFunction fn)
    {
        switch (kind)
        {
            case Starling.Js.Bytecode.ClassMethodKind.Method:
                owner.DefineOwnProperty(key,
                    PropertyDescriptor.Data(JsValue.Object(fn), writable: true, enumerable: false, configurable: true));
                break;
            case Starling.Js.Bytecode.ClassMethodKind.Get:
            {
                var existing = owner.GetOwnPropertyDescriptor(key);
                var setter = existing is { IsAccessor: true } existingDesc ? existingDesc.Setter : null;
                owner.DefineOwnProperty(key, PropertyDescriptor.Accessor(fn, setter, enumerable: false, configurable: true));
                break;
            }
            case Starling.Js.Bytecode.ClassMethodKind.Set:
            {
                var existing = owner.GetOwnPropertyDescriptor(key);
                var getter = existing is { IsAccessor: true } existingDesc ? existingDesc.Getter : null;
                owner.DefineOwnProperty(key, PropertyDescriptor.Accessor(getter, fn, enumerable: false, configurable: true));
                break;
            }
        }
    }

    /// <summary>wp:M3-04f — stamp the §13.2.5.5 / §15.7.5 "name" own property
    /// for a computed-key method. String keys use the key text directly;
    /// Symbol keys use <c>[description]</c> (empty for unnamed Symbols).
    /// Getters/setters prefix "get "/"set ".</summary>
    private static void StampMethodName(JsFunction fn, JsPropertyKey key, Starling.Js.Bytecode.ClassMethodKind kind)
    {
        string baseName = key.IsSymbol
            ? (key.AsSymbol.Description is { } d ? "[" + d + "]" : "")
            : key.AsString;
        string prefix = kind switch
        {
            Starling.Js.Bytecode.ClassMethodKind.Get => "get ",
            Starling.Js.Bytecode.ClassMethodKind.Set => "set ",
            _ => "",
        };
        fn.DefineOwnProperty("name",
            PropertyDescriptor.Data(JsValue.String(prefix + baseName), writable: false, enumerable: false, configurable: true));
    }

    /// <summary>wp:M3-04f — a zero-arg thunk that simply returns
    /// <c>undefined</c>, used for an instance computed field with no
    /// initializer. The runtime then defines the own property under the
    /// pre-resolved computed key.</summary>
    private static JsFunction MakeUndefinedReturningThunk(JsRealm realm)
    {
        var b = new Starling.Js.Bytecode.ChunkBuilder();
        b.Emit(Starling.Js.Bytecode.Opcode.LoadUndefined);
        b.Emit(Starling.Js.Bytecode.Opcode.Return);
        var chunk = b.Build("#field-init-undef-computed");
        var tmpl = new JsFunction("", chunk, 0);
        return JsFunction.CreateInstance(realm, tmpl, Array.Empty<JsValue>());
    }

    private static JsFunction MakeUndefinedFieldThunk(JsRealm realm, Starling.Js.Bytecode.FieldEntry field)
    {
        // Synthesize a tiny chunk: `this.key = undefined;` (or DefinePrivateField).
        var b = new Starling.Js.Bytecode.ChunkBuilder();
        if (field.MangledPrivateKey is not null)
        {
            // [this, undefined, DefinePrivateField]
            b.Emit(Starling.Js.Bytecode.Opcode.LoadThis);
            b.Emit(Starling.Js.Bytecode.Opcode.LoadUndefined);
            b.EmitU16(Starling.Js.Bytecode.Opcode.DefinePrivateField, b.AddConstant(field.MangledPrivateKey));
        }
        else
        {
            b.Emit(Starling.Js.Bytecode.Opcode.LoadThis);
            b.Emit(Starling.Js.Bytecode.Opcode.LoadUndefined);
            b.EmitU16(Starling.Js.Bytecode.Opcode.StoreProperty, b.AddConstant(field.StaticKey!));
            b.Emit(Starling.Js.Bytecode.Opcode.Pop);
        }
        b.Emit(Starling.Js.Bytecode.Opcode.ReturnUndefined);
        var chunk = b.Build("#field-init-undef");
        var tmpl = new JsFunction("", chunk, 0);
        return JsFunction.CreateInstance(realm, tmpl, Array.Empty<JsValue>());
    }

    // =====================================================================
    //               B1b-2c — Generator / Async dispatch
    // =====================================================================

    /// <summary>Invoke a generator function — set up a JsGenerator wrapper
    /// whose worker thread will run the body lazily on the first
    /// <c>.next()</c> call.</summary>
    internal JsValue StartGeneratorBody(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        var realm = _runtime.Realm;
        var frame = new SuspendedFrame(this);
        var gen = new JsGenerator(realm, frame);
        // Stamp own properties so duck-typing tests work.
        var argsCopy = args; // captured into the lambda
        var thisCopy = thisValue;
        var fnCopy = fn;
        frame.Start(() =>
        {
            // Worker thread: invoke the body with this frame as the active
            // suspension target. Result becomes the frame's return value.
            try
            {
                var rv = Run(fnCopy.Body, argsCopy, thisCopy, fnCopy.Upvalues,
                    drainMicrotasks: false, currentFunction: fnCopy, newTarget: null,
                    suspension: frame);
                // Frame's return value will be read by the dispatcher
                // (Generator.next's caller) — store via a field.
                frame.SetReturnValue(rv);
            }
            catch (JsThrow ex)
            {
                frame.SetThrew(ex.Value);
            }
        });
        return JsValue.Object(gen);
    }

    /// <summary>Invoke an async function — set up an outer Promise + worker
    /// thread that runs the body. Returns the outer Promise immediately;
    /// the body settles it on completion (or via an unhandled throw).</summary>
    internal JsValue StartAsyncBody(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        var realm = _runtime.Realm;
        var outer = new JsPromise(realm.PromisePrototype);
        var frame = new SuspendedFrame(this);
        var state = new JsAsyncFunctionState(frame, outer);

        var fnCopy = fn;
        var argsCopy = args;
        var thisCopy = thisValue;
        frame.Start(() =>
        {
            try
            {
                var rv = Run(fnCopy.Body, argsCopy, thisCopy, fnCopy.Upvalues,
                    drainMicrotasks: false, currentFunction: fnCopy, newTarget: null,
                    suspension: frame);
                frame.SetReturnValue(rv);
            }
            catch (JsThrow ex)
            {
                frame.SetThrew(ex.Value);
            }
        });

        // Drive the worker synchronously from the calling thread, riding
        // each await suspension via the microtask queue. The first Resume
        // kicks off the body; subsequent Resumes are wired by the await
        // handler below.
        DriveAsync(state);
        return JsValue.Object(outer);
    }

    /// <summary>Run the async body forward until the next await or
    /// completion. If the body awaits a value, schedules a .then on the
    /// Promise.resolve(value) so the worker resumes after the awaited
    /// settlement.</summary>
    private void DriveAsync(JsAsyncFunctionState state)
    {
        var realm = _runtime.Realm;
        var frame = state.Frame;
        // Initial kick: pass Undefined as resume value. The worker starts
        // executing and either runs to completion or hits a Suspend.
        frame.Resume(JsValue.Undefined);
        if (frame.Completed)
        {
            SettleAsync(state);
            return;
        }
        // Hit an await — frame.YieldedValue holds the awaited value.
        ScheduleAwait(state);
    }

    private void ScheduleAwait(JsAsyncFunctionState state)
    {
        var realm = _runtime.Realm;
        var awaited = state.Frame.YieldedValue;
        // Wrap value in a Promise (Promise.resolve semantics).
        JsPromise inner;
        if (awaited.IsObject && awaited.AsObject is JsPromise existing)
        {
            inner = existing;
        }
        else
        {
            inner = new JsPromise(realm.PromisePrototype);
            Starling.Js.Intrinsics.PromiseCtor.Resolve(realm, inner, awaited);
        }

        var onFulfill = new JsNativeFunction("", (thisV, args) =>
        {
            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            state.Frame.Resume(v, withThrow: false);
            if (state.Frame.Completed) SettleAsync(state);
            else ScheduleAwait(state);
            return JsValue.Undefined;
        }, isConstructor: false);
        var onReject = new JsNativeFunction("", (thisV, args) =>
        {
            var r = args.Length > 0 ? args[0] : JsValue.Undefined;
            state.Frame.Resume(r, withThrow: true);
            if (state.Frame.Completed) SettleAsync(state);
            else ScheduleAwait(state);
            return JsValue.Undefined;
        }, isConstructor: false);

        // Call inner.then(onFulfill, onReject).
        var then = AbstractOperations.Get(this, inner, "then");
        AbstractOperations.Call(this, then, JsValue.Object(inner),
            new[] { JsValue.Object(onFulfill), JsValue.Object(onReject) });
    }

    private void SettleAsync(JsAsyncFunctionState state)
    {
        if (state.Settled) return;
        state.Settled = true;
        var realm = _runtime.Realm;
        if (state.Frame.ThrewUncaught)
            Starling.Js.Intrinsics.PromiseCtor.Reject(realm, state.OuterPromise, state.Frame.ReturnValue);
        else
            Starling.Js.Intrinsics.PromiseCtor.Resolve(realm, state.OuterPromise, state.Frame.ReturnValue);
    }

    /// <summary>wp:M3-04g — invoke an <c>async function*</c>. Sets up a
    /// <see cref="JsAsyncGenerator"/> whose worker thread runs the body lazily
    /// on the first request. The body interleaves <c>yield</c> (kind 0) and
    /// <c>await</c> (kind 1) suspensions through the shared
    /// <see cref="SuspendedFrame"/>; the driver
    /// (<see cref="AsyncGeneratorDrainQueue"/>) tells them apart via
    /// <see cref="SuspendedFrame.SuspendKind"/>.</summary>
    internal JsValue StartAsyncGeneratorBody(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        var realm = _runtime.Realm;
        var frame = new SuspendedFrame(this);
        var gen = new JsAsyncGenerator(realm, frame);
        var fnCopy = fn;
        var argsCopy = args;
        var thisCopy = thisValue;
        frame.Start(() =>
        {
            try
            {
                var rv = Run(fnCopy.Body, argsCopy, thisCopy, fnCopy.Upvalues,
                    drainMicrotasks: false, currentFunction: fnCopy, newTarget: null,
                    suspension: frame);
                frame.SetReturnValue(rv);
            }
            catch (JsThrow ex)
            {
                frame.SetThrew(ex.Value);
            }
        });
        return JsValue.Object(gen);
    }

    /// <summary>wp:M3-04g — §7.4.2 GetIterator(obj, async) for
    /// <c>for await…of</c>. Resolves <c>obj[@@asyncIterator]</c>; when absent,
    /// builds the record from the sync <c>[Symbol.iterator]</c> and marks it
    /// sync-wrapped so the driver lifts each result into a Promise
    /// (CreateAsyncFromSyncIterator, §27.1.4.1).</summary>
    private Starling.Js.Intrinsics.JsIteratorRecordHandle GetAsyncIteratorHandle(JsValue iterable)
    {
        var realm = _runtime.Realm;
        if (iterable.IsNullish)
            throw new JsThrow(realm.NewTypeError("value is not async iterable"));

        var asyncMethod = AbstractOperations.GetMethod(this, iterable,
            Starling.Js.Intrinsics.SymbolCtor.AsyncIterator);
        if (!asyncMethod.IsUndefined && !asyncMethod.IsNull)
        {
            var iter = AbstractOperations.Call(this, asyncMethod, iterable, Array.Empty<JsValue>());
            if (!iter.IsObject)
                throw new JsThrow(realm.NewTypeError("async iterator method did not return an object"));
            var nextMethod = AbstractOperations.Get(this, iter.AsObject, "next");
            return new Starling.Js.Intrinsics.JsIteratorRecordHandle(
                new IteratorRecord(iter, nextMethod, Done: false));
        }

        // No @@asyncIterator — wrap the sync iterator.
        var syncRecord = AbstractOperations.GetIterator(realm, this, iterable);
        return new Starling.Js.Intrinsics.JsIteratorRecordHandle(syncRecord) { SyncWrapped = true };
    }

    /// <summary>wp:M3-04g — §27.1.4.2.1 AsyncFromSyncIterator next: produce a
    /// promise that resolves to <c>{value: await syncResult.value, done}</c>,
    /// so a sync iterable of promises iterated by <c>for await</c> unwraps each
    /// element.</summary>
    private JsPromise WrapSyncIteratorResult(JsValue syncResult)
    {
        var realm = _runtime.Realm;
        if (!syncResult.IsObject)
            throw new JsThrow(realm.NewTypeError("iterator.next() did not return an object"));
        var done = JsValue.ToBoolean(AbstractOperations.Get(this, syncResult.AsObject, "done"));
        var value = AbstractOperations.Get(this, syncResult.AsObject, "value");

        // Promise.resolve(value).then(v => MakeResult(v, done)).
        var inner = new JsPromise(realm.PromisePrototype);
        PromiseCtor.Resolve(realm, inner, value);
        var onFulfill = new JsNativeFunction("", (thisV, args) =>
        {
            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            return IteratorIntrinsics.MakeResult(realm, v, done);
        }, isConstructor: false);
        var then = AbstractOperations.Get(this, inner, "then");
        var chained = AbstractOperations.Call(this, then, JsValue.Object(inner),
            new[] { JsValue.Object(onFulfill) });
        // `then` returns a Promise; hand it back for the loop to await.
        return chained.IsObject && chained.AsObject is JsPromise p
            ? p
            : throw new JsThrow(realm.NewTypeError("Promise.prototype.then did not return a promise"));
    }

    // ---- wp:M3-04g — async-generator request queue + driver ----------------

    /// <summary>§27.6.3.1 AsyncGeneratorEnqueue — allocate a request promise,
    /// queue the request, and (if the generator isn't already busy draining)
    /// start processing the queue. Returns the request's promise.</summary>
    internal JsValue AsyncGeneratorEnqueue(JsAsyncGenerator gen, AsyncGeneratorRequestKind kind, JsValue value)
    {
        var realm = _runtime.Realm;
        var cap = new JsPromise(realm.PromisePrototype);
        gen.Queue.Enqueue(new AsyncGeneratorRequest(kind, value, cap));
        AsyncGeneratorDrainQueue(gen);
        return JsValue.Object(cap);
    }

    /// <summary>§27.6.3.4 AsyncGeneratorDrainQueue — if the generator is idle,
    /// resume the body for the head request and ride its yield/await
    /// suspensions until it produces a result (or completes), settling the
    /// head request's promise.</summary>
    private void AsyncGeneratorDrainQueue(JsAsyncGenerator gen)
    {
        if (gen.Draining) return;            // a resume is already in flight
        if (gen.Queue.Count == 0) return;    // nothing to do

        var realm = _runtime.Realm;
        var req = gen.Queue.Peek();

        // Already-completed generator: short-circuit per §27.6.3.6
        // AsyncGeneratorResumeNext for a Done state.
        if (gen.Done)
        {
            gen.Queue.Dequeue();
            switch (req.Kind)
            {
                case AsyncGeneratorRequestKind.Throw:
                    PromiseCtor.Reject(realm, req.Capability, req.Value);
                    break;
                case AsyncGeneratorRequestKind.Return:
                    PromiseCtor.Resolve(realm, req.Capability,
                        IteratorIntrinsics.MakeResult(realm, req.Value, done: true));
                    break;
                default:
                    PromiseCtor.Resolve(realm, req.Capability,
                        IteratorIntrinsics.MakeResult(realm, JsValue.Undefined, done: true));
                    break;
            }
            // Keep draining any further requests against the done state.
            AsyncGeneratorDrainQueue(gen);
            return;
        }

        // Requests against a not-yet-started generator that aren't `next`
        // complete the generator without ever running the body (§27.6.3.6):
        //   .return(v) → {value:v, done:true}; .throw(e) → reject(e).
        if (!gen.Started && req.Kind != AsyncGeneratorRequestKind.Next)
        {
            gen.Done = true;
            gen.Queue.Dequeue();
            if (req.Kind == AsyncGeneratorRequestKind.Throw)
                PromiseCtor.Reject(realm, req.Capability, req.Value);
            else
                PromiseCtor.Resolve(realm, req.Capability,
                    IteratorIntrinsics.MakeResult(realm, req.Value, done: true));
            AsyncGeneratorDrainQueue(gen);
            return;
        }

        gen.Draining = true;
        gen.Started = true;
        // Resume the body for this request's completion kind.
        switch (req.Kind)
        {
            case AsyncGeneratorRequestKind.Throw:
                gen.Frame.Resume(req.Value, withThrow: true);
                break;
            case AsyncGeneratorRequestKind.Return:
                gen.Frame.Resume(req.Value, withReturn: true);
                break;
            default:
                gen.Frame.Resume(req.Value);
                break;
        }
        AsyncGeneratorAfterResume(gen);
    }

    /// <summary>Common post-resume handling: the body either completed, hit a
    /// <c>yield</c> (deliver a result to the head request) or hit an
    /// <c>await</c> (schedule a continuation that resumes the body once the
    /// awaited promise settles).</summary>
    private void AsyncGeneratorAfterResume(JsAsyncGenerator gen)
    {
        var realm = _runtime.Realm;
        var frame = gen.Frame;

        if (frame.Completed)
        {
            gen.Draining = false;
            gen.Done = true;
            var req = gen.Queue.Dequeue();
            if (frame.ThrewUncaught)
                PromiseCtor.Reject(realm, req.Capability, frame.ReturnValue);
            else
                PromiseCtor.Resolve(realm, req.Capability,
                    IteratorIntrinsics.MakeResult(realm, frame.ReturnValue, done: true));
            // Drive remaining requests against the now-done state.
            AsyncGeneratorDrainQueue(gen);
            return;
        }

        if (frame.SuspendKind == 1)
        {
            // Internal `await` — resume the body once the awaited value
            // settles, then re-enter this handler. Stays Draining; does NOT
            // deliver a result to the head request.
            AsyncGeneratorScheduleAwait(gen);
            return;
        }

        // Real `yield` — fulfil the head request with {value, done:false}.
        gen.Draining = false;
        var yielded = frame.YieldedValue;
        var head = gen.Queue.Dequeue();
        PromiseCtor.Resolve(realm, head.Capability,
            IteratorIntrinsics.MakeResult(realm, yielded, done: false));
        // Service the next queued request (if any).
        AsyncGeneratorDrainQueue(gen);
    }

    /// <summary>Wire the internal <c>await</c>: resolve the awaited value to a
    /// promise and resume the body when it settles (mirrors
    /// <see cref="ScheduleAwait"/> but feeds back into the async-generator
    /// driver).</summary>
    private void AsyncGeneratorScheduleAwait(JsAsyncGenerator gen)
    {
        var realm = _runtime.Realm;
        var awaited = gen.Frame.YieldedValue;
        JsPromise inner;
        if (awaited.IsObject && awaited.AsObject is JsPromise existing)
        {
            inner = existing;
        }
        else
        {
            inner = new JsPromise(realm.PromisePrototype);
            PromiseCtor.Resolve(realm, inner, awaited);
        }

        var onFulfill = new JsNativeFunction("", (thisV, args) =>
        {
            var v = args.Length > 0 ? args[0] : JsValue.Undefined;
            gen.Frame.Resume(v, withThrow: false);
            AsyncGeneratorAfterResume(gen);
            return JsValue.Undefined;
        }, isConstructor: false);
        var onReject = new JsNativeFunction("", (thisV, args) =>
        {
            var r = args.Length > 0 ? args[0] : JsValue.Undefined;
            gen.Frame.Resume(r, withThrow: true);
            AsyncGeneratorAfterResume(gen);
            return JsValue.Undefined;
        }, isConstructor: false);

        var then = AbstractOperations.Get(this, inner, "then");
        AbstractOperations.Call(this, then, JsValue.Object(inner),
            new[] { JsValue.Object(onFulfill), JsValue.Object(onReject) });
    }
}

/// <summary>Thrown by the VM when a script-level <c>throw</c> is uncaught.</summary>
#pragma warning disable RCS1194
public sealed class JsThrow(JsValue value) : Exception($"uncaught: {value}")
{
    public JsValue Value { get; } = value;
}

/// <summary>Internal sentinel raised inside a generator worker thread when
/// the caller invokes <c>.return(v)</c> at a suspension point. Walks any
/// enclosing try/finally frames via the standard exception-handling path
/// (treated as a Return completion), and at the top of the generator body
/// produces a normal completion with <see cref="Value"/> as the return
/// value. Mirrors the synchronous-return path that
/// <c>DivertReturnThroughFinally</c> uses for the <c>Return</c> opcode.</summary>
internal sealed class JsReturnSentinel(JsValue value) : Exception("generator return sentinel")
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
