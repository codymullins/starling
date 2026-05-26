using System.Buffers.Binary;
using Starling.Js.Bytecode;
using Starling.Js.Intrinsics;
using Starling.RegExp;

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

    /// <summary>Maximum nested JS call depth before a <c>RangeError</c> is
    /// thrown. Each JS call recurses through <c>Run</c> in C#, so an
    /// unbounded JS recursion would otherwise overflow the native stack and
    /// crash the process (uncatchable). The spec leaves the limit
    /// implementation-defined (§"so long as … the implementation … throws a
    /// RangeError"); this value is conservative to stay safe on a default
    /// (~1 MB) thread stack while still admitting normal recursion.
    ///
    /// Empirically the recursive interpreter (RunInner is a large method, ~4
    /// native frames per JS call) overflows the render path's thread stack near
    /// ~300 JS frames — well below the old 1000 limit, so an uncatchable
    /// StackOverflowException (process crash) won out over the guard. 150 keeps a
    /// safety margin so the guard reliably wins and throws a catchable RangeError.
    /// (Proper deep-recursion support — running the VM on a dedicated large-stack
    /// thread / trampolining — is a follow-up; see wp:M3-13.)</summary>
    private const int MaxCallDepth = 1000;
    private int _callDepth;

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
        // §16.1.7 / §9.4.1: a classic script's top-level `this` is the global
        // object (modules — evaluated via CallFunction, not this entry — keep
        // `this` = undefined). Real-world UMD wrappers do `}(this, factory)` and
        // read `this._` / `this.Backbone`, so top-level `this` MUST be global.
        Run(chunk, args: [], thisValue: JsValue.Object(_runtime.Realm.GlobalObject),
            upvalues: Array.Empty<JsValue>(), drainMicrotasks: true,
            currentFunction: null, newTarget: null);

    /// <summary>Re-entrant evaluation entry for the <c>eval</c> builtin and the
    /// <c>Function</c> constructor (§19.2.1 / §20.2.1). Runs a freshly compiled
    /// global-scope chunk against the current realm with <c>this</c> = the global
    /// object, WITHOUT draining microtasks (the outermost frame owns the drain).
    /// Returns the completion value the chunk left on the stack.</summary>
    public JsValue RunEval(Chunk chunk) =>
        Run(chunk, args: Array.Empty<JsValue>(),
            thisValue: JsValue.Object(_runtime.Realm.GlobalObject),
            upvalues: Array.Empty<JsValue>(), drainMicrotasks: false,
            currentFunction: null, newTarget: null);

    /// <summary>wp:M3-72 — run direct-eval'd code with <paramref name="evalScope"/>
    /// (the calling frame's live variable environment) installed so the eval
    /// body's caller-scope-aware opcodes resolve free identifiers to the
    /// caller's live bindings. <c>this</c> / <c>new.target</c> / the
    /// caller-as-currentFunction (for <c>super</c>) are inherited so the eval'd
    /// code's this / new.target / super resolve as in §19.2.1.1. Does not drain
    /// microtasks (the outermost frame owns the drain).</summary>
    internal JsValue RunDirectEval(Chunk chunk, EvalScope evalScope, JsFunction? caller,
        JsValue callerThis, JsObject? callerNewTarget, EvalVarStore? callerVarStore = null) =>
        Run(chunk, args: Array.Empty<JsValue>(),
            thisValue: callerThis,
            upvalues: Array.Empty<JsValue>(), drainMicrotasks: false,
            currentFunction: caller, newTarget: callerNewTarget,
            suspension: null, evalScope: evalScope,
            // wp:M3-73 — thread the caller frame's eval-introduced var store so
            // the eval body's DeclareEvalVar/StoreEvalVar write into it and its
            // own reads/writes of those names resolve through it.
            frameVarStore: callerVarStore);

    /// <summary>wp:M3-71/72 — §19.2.1.1 PerformEval (direct path). Parse + compile
    /// <paramref name="args"/>[0] inheriting the CALLER's lexical context, then
    /// run the resulting chunk on the caller's function so a SuperProperty
    /// resolves via <paramref name="caller"/>'s <c>[[HomeObject]]</c>,
    /// <c>new.target</c> sees <paramref name="callerNewTarget"/>, and <c>this</c>
    /// is <paramref name="callerThis"/>. A non-string argument is returned unchanged
    /// (§19.2.1 step 2).
    /// <para>wp:M3-72 — <paramref name="callerScope"/> is the caller frame's live
    /// variable environment (built from the call site's
    /// <see cref="Bytecode.EvalScopeDescriptor"/> + the live locals/upvalues). The
    /// eval body's free identifiers that name a caller binding read/write that
    /// live binding, and the §19.2.1.3 EvalDeclarationInstantiation early-error
    /// checks run against the caller's lexical bindings.</para>
    /// <para>wp:M3-73 — a NON-strict direct eval whose caller is a function injects
    /// the eval body's OWN top-level var/function declarations into the caller's
    /// variable environment so they persist after eval returns (§19.2.1.3
    /// non-global branch). Those new bindings live in the caller frame's
    /// <see cref="EvalVarStore"/> (<paramref name="callerVarStore"/>, created
    /// lazily here and propagated back to the caller frame via <c>ref</c>); a
    /// top-level var/function whose name IS an existing caller binding updates
    /// that live binding instead. A STRICT direct eval keeps its own
    /// var-environment (no injection — its top-level var/function bind globally),
    /// and a script-top (non-function) caller keeps the existing global-injection
    /// path.</para></summary>
    private JsValue PerformDirectEval(JsValue[] args, EvalScope callerScope,
        JsFunction? caller, JsValue callerThis, JsObject? callerNewTarget, bool callerStrict,
        bool inInitializer, ref EvalVarStore? callerVarStore)
    {
        var x = args.Length > 0 ? args[0] : JsValue.Undefined;
        if (!x.IsString) return x;

        // §19.2.1.1 steps 4-6: derive the early-error context from the caller.
        // A [[HomeObject]] ⇒ the caller is a method (inMethod). A class
        // constructor (HomeObject + [[Construct]]) ⇒ inConstructor (derived-ctor
        // gating for SuperCall). new.target is available iff the caller is
        // function code (inFunction); arrow callers inherit their enclosing
        // function's, so a caller at all (non-null) means inFunction.
        var inFunction = caller is not null;
        var inMethod = caller?.HomeObject is not null;
        var inDerivedCtor = caller is { ConstructorKind: ClassConstructorKind.Derived };

        Chunk chunk;
        bool injectVars;
        try
        {
            var ctx = new Parse.JsParser.DirectEvalContext(
                CallerStrict: callerStrict, InFunction: inFunction,
                InMethod: inMethod, InDerivedConstructor: inDerivedCtor);
            var program = new Parse.JsParser(x.AsString).ParseProgram(ctx);

            // wp:M3-81 — §sec-performeval-rules-in-initializer Additional Early
            // Error Rules for Eval Inside Initializer: when this direct eval occurs
            // inside a class field/static initializer or a (non-arrow) function
            // parameter default, "ScriptBody : StatementList — It is a Syntax Error
            // if ContainsArguments of StatementList is true." Throw before the body
            // runs (a pre-execution early error), so no side effect is observed.
            if (inInitializer && Bytecode.CaptureAnalysis.ContainsArgumentsInEvalBody(program.Body))
                throw new JsThrow(_runtime.Realm.NewSyntaxError(
                    "'arguments' is not allowed in an eval call inside an initializer"));

            var evalIsStrict = callerStrict || program.Strict;

            // §19.2.1.3 EvalDeclarationInstantiation — a NON-strict direct eval
            // hoists its top-level var/function names into the caller's variable
            // environment, and must not hoist over a like-named LEXICAL binding
            // (let/const/class) anywhere in the caller's environment chain. A
            // strict direct eval gets its own fresh variable environment, so no
            // collision is possible. The caller-scope entries carry the caller's
            // lexical names flagged IsLexical.
            if (!evalIsStrict)
            {
                foreach (var vn in Parse.JsParser.EvalVarDeclaredNames(program))
                {
                    if (callerScope.TryGet(vn, out var entry) && entry.IsLexical)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "Identifier '" + vn + "' has already been declared"));
                }
            }

            // wp:M3-73 — inject the eval body's own top-level var/function
            // declarations into the CALLER's var-environment iff the eval is
            // non-strict AND the caller is a function (a function var-env, not the
            // global one). A strict eval keeps its own var-env; a script-top
            // caller keeps the existing global-injection behaviour.
            injectVars = !evalIsStrict && inFunction;

            var callerNames = new HashSet<string>(callerScope.Names, StringComparer.Ordinal);
            // §19.2.1.1 — thread the caller's PrivateEnvironment so eval'd code
            // resolves private names (`this.#m`) against the enclosing class.
            chunk = JsCompiler.CompileForDirectEval(program, "<eval>", callerNames, injectVars,
                caller?.Body.PrivateNameScope);
        }
        catch (Parse.JsParseException ex)
        {
            throw new JsThrow(_runtime.Realm.NewSyntaxError(ex.Message));
        }

        // wp:M3-73 — when injecting, ensure the caller frame has a var store and
        // run the eval body against it so its DeclareEvalVar/StoreEvalVar land in
        // the caller's environment and its own reads/writes resolve there.
        if (injectVars)
            callerVarStore ??= new EvalVarStore();

        // Run on the caller's function (so super / this / new.target resolve)
        // with the caller scope installed so free identifiers reach the caller's
        // live bindings, and the caller's eval-introduced var store threaded in.
        return RunDirectEval(chunk, callerScope, caller, callerThis, callerNewTarget,
            callerVarStore);
    }

    /// <summary>Invoke a JS function with an explicit <c>this</c> and args.
    /// Used by <see cref="AbstractOperations.Call"/>. B1b-2c: when the
    /// callee is async / generator / async-generator, this entry instead
    /// returns the corresponding wrapper without running the body —
    /// dispatch lands in <see cref="StartGeneratorBody"/> /
    /// <see cref="StartAsyncBody"/>.</summary>
    public JsValue CallFunction(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        // wp:M3-83 — §9.3.1 cross-realm execution. If this function was created
        // in a DIFFERENT realm than the one this VM runs (the $262.createRealm
        // case: a foreign realm's function invoked from the host realm's VM),
        // dispatch through that realm's own VM with its realm published as the
        // running execution context. PrepareForOrdinaryCall pushes the callee's
        // [[Realm]] as the current realm; doing so makes the body resolve
        // globals, allocate intrinsics, and throw errors in the function's realm.
        if (fn.Realm is { } fnRealm && !ReferenceEquals(fnRealm, _runtime.Realm)
            && fnRealm.OwnerRuntime is { } owner)
            return owner.WithActiveVm(foreignVm =>
                ReferenceEquals(foreignVm, this) ? CallFunctionLocal(fn, thisValue, args)
                                                 : foreignVm.CallFunction(fn, thisValue, args));
        return CallFunctionLocal(fn, thisValue, args);
    }

    private JsValue CallFunctionLocal(JsFunction fn, JsValue thisValue, JsValue[] args)
    {
        // §10.2.1.2 OrdinaryCallBindThis: a SLOPPY function called with a nullish
        // `this` binds the global object, not undefined. This makes the
        // ubiquitous global-detection idiom `(function(){return this})()` and
        // legacy libs (jQuery/Backbone/YUI feature-detection) work. A STRICT
        // function leaves `this` as the passed value (undefined/null). Arrows
        // capture `this` lexically and ignore this argument; class constructors
        // only run via [[Construct]] with a real `this`, so neither is affected.
        if (thisValue.IsNullish && fn.ConstructorKind == ClassConstructorKind.None
            && !fn.Body.IsStrict)
            thisValue = JsValue.Object(_runtime.Realm.GlobalObject);

        if (fn.Kind == JsFunctionKind.Generator)
            return StartGeneratorBody(fn, thisValue, args);
        if (fn.Kind == JsFunctionKind.Async)
            return StartAsyncBody(fn, thisValue, args);
        if (fn.Kind == JsFunctionKind.AsyncGenerator)
            return StartAsyncGeneratorBody(fn, thisValue, args);
        return Run(fn.Body, args, thisValue, fn.Upvalues, drainMicrotasks: false,
               currentFunction: fn, newTarget: null,
               // wp:M3-73 — inherit the eval-introduced var store this closure
               // captured at creation so its free identifiers resolve through the
               // vars a direct eval injected into the enclosing var-environment.
               frameVarStore: fn.CapturedEvalVarStore);
    }

    /// <summary>Construct a JS function (spec [[Construct]] for ordinary
    /// functions): allocate a fresh ordinary object inheriting from the
    /// constructor's <c>prototype</c> property, run the body with <c>this</c>
    /// bound to it, and return whichever object the body produced.</summary>
    public JsValue ConstructFunction(JsFunction fn, JsValue[] args, JsObject newTarget)
    {
        // wp:M3-83 — §9.3.2 cross-realm construct. Mirror CallFunction: a foreign
        // realm's constructor (a class produced by another realm's eval) must run
        // with its realm active so its `this` instance, prototype, and any brand
        // / error it throws come from the constructor's realm, not the caller's.
        if (fn.Realm is { } fnRealm && !ReferenceEquals(fnRealm, _runtime.Realm)
            && fnRealm.OwnerRuntime is { } owner)
            return owner.WithActiveVm(foreignVm =>
                ReferenceEquals(foreignVm, this) ? ConstructFunctionLocal(fn, args, newTarget)
                                                 : foreignVm.ConstructFunction(fn, args, newTarget));
        return ConstructFunctionLocal(fn, args, newTarget);
    }

    private JsValue ConstructFunctionLocal(JsFunction fn, JsValue[] args, JsObject newTarget)
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
                    drainMicrotasks: false, currentFunction: fn, newTarget: newTarget,
                    frameVarStore: fn.CapturedEvalVarStore); // wp:M3-73
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
            drainMicrotasks: false, currentFunction: fn, newTarget: newTarget,
            frameVarStore: fn.CapturedEvalVarStore); // wp:M3-73
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

    /// <summary>DIAG: name of the most recent property/global load, used to
    /// enrich "not a function" errors with the callee identifier.</summary>
    private string? _lastLoadName;

    private static bool IsCallableValue(JsValue v)
        => v.IsObject && v.AsObject is JsNativeFunction or JsFunction or JsBoundFunction or JsProxy;

    /// <summary>wp:M3-72 — build the runtime <see cref="EvalScope"/> for a direct
    /// eval from the compile-time <see cref="Bytecode.EvalScopeDescriptor"/> and
    /// the caller frame's live storage (its <paramref name="locals"/> array and
    /// <paramref name="upvalues"/> cell table). Plain locals are referenced by
    /// (array, slot) so writes are observed by the caller after eval returns;
    /// captured locals/upvalues share the same <see cref="Cell"/> instance.</summary>
    private static EvalScope BuildEvalScope(Bytecode.EvalScopeDescriptor descriptor,
        JsValue[] locals, IReadOnlyList<JsValue> upvalues)
    {
        var entries = new List<EvalScope.Entry>(descriptor.Bindings.Count);
        foreach (var b in descriptor.Bindings)
        {
            switch (b.Kind)
            {
                case Bytecode.EvalScopeDescriptor.Kind.LocalSlot:
                    entries.Add(new EvalScope.Entry
                    {
                        Name = b.Name, Locals = locals, Slot = b.Index, IsLexical = b.IsLexical,
                    });
                    break;
                case Bytecode.EvalScopeDescriptor.Kind.LocalCell:
                {
                    // A captured local holds a Cell in its slot; share it so
                    // writes from the eval'd code are live for the caller and
                    // any other closures over the same binding.
                    var slotVal = locals[b.Index];
                    if (slotVal.IsObject && slotVal.AsObject is Cell cell)
                        entries.Add(new EvalScope.Entry { Name = b.Name, Cell = cell, IsLexical = b.IsLexical });
                    else
                        entries.Add(new EvalScope.Entry { Name = b.Name, Locals = locals, Slot = b.Index, IsLexical = b.IsLexical });
                    break;
                }
                case Bytecode.EvalScopeDescriptor.Kind.Upvalue:
                {
                    if (b.Index < upvalues.Count && upvalues[b.Index].IsObject
                        && upvalues[b.Index].AsObject is Cell upCell)
                        entries.Add(new EvalScope.Entry { Name = b.Name, Cell = upCell, IsLexical = b.IsLexical });
                    break;
                }
            }
        }
        return new EvalScope(entries);
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
    private JsValue Run(Chunk chunk, JsValue[] args, JsValue thisValue,
        IReadOnlyList<JsValue> upvalues, bool drainMicrotasks,
        JsFunction? currentFunction, JsObject? newTarget,
        SuspendedFrame? suspension = null, EvalScope? evalScope = null,
        EvalVarStore? frameVarStore = null)
    {
        // Publish this VM on the realm so native intrinsics (JSON.parse
        // reviver, JSON.stringify replacer/toJSON, etc.) can dispatch JS
        // callables. Save/restore in case of reentry from a nested host
        // invocation chain.
        // Guard the native stack: each nested JS call recurses through here, so
        // cap the depth and surface a catchable RangeError instead of a fatal
        // StackOverflowException.
        // Coarse logical cap, plus a probe of the *actual* remaining native
        // stack. The logical cap alone is unreliable: native frames-per-JS-call
        // vary (calls routed through native intrinsics like String.prototype.
        // replace / Function.prototype.call burn several extra native frames),
        // so a fixed depth can overflow the thread stack before the cap is hit.
        // TryEnsureSufficientExecutionStack() reports true while enough stack
        // remains for a typical call chain; when it goes false we surface a
        // catchable RangeError instead of an uncatchable StackOverflowException
        // that would crash the whole process.
        if (_callDepth >= MaxCallDepth ||
            !System.Runtime.CompilerServices.RuntimeHelpers.TryEnsureSufficientExecutionStack())
            throw new JsThrow(_runtime.Realm.NewRangeError("Maximum call stack size exceeded"));

        var prevVm = _runtime.Realm.ActiveVm;
        _runtime.Realm.ActiveVm = this;
        _callDepth++;
        try
        {
            var result = RunInner(chunk, args, thisValue, upvalues, currentFunction, newTarget, suspension, evalScope, frameVarStore);
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
            _callDepth--;
            _runtime.Realm.ActiveVm = prevVm;
        }
    }

    private JsValue RunInner(Chunk chunk, JsValue[] args, JsValue thisValue,
        IReadOnlyList<JsValue> upvalues, JsFunction? currentFunction, JsObject? newTarget,
        SuspendedFrame? suspension = null, EvalScope? evalScope = null,
        EvalVarStore? frameVarStore = null)
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
        // ES strict mode — whether this frame's code runs as strict mode code.
        // Drives strict StoreGlobal (assignment to undeclared global throws) and
        // strict Set/delete failures.
        var frameStrict = chunk.IsStrict;

        // wp:M3-73 — a non-strict function whose body/params contain a direct
        // eval eagerly allocates its var store at frame entry so closures it
        // creates (incl. ones created in a parameter initializer BEFORE the eval
        // runs) snapshot the same store and observe the bindings a later direct
        // eval injects into this function's variable environment. Any
        // already-captured parent store (frameVarStore, from
        // JsFunction.CapturedEvalVarStore) becomes this store's lookup parent so
        // a free identifier resolves own-env -> enclosing eval-env -> global.
        if (!frameStrict && chunk.HasDirectEval)
            frameVarStore = new EvalVarStore { Parent = frameVarStore };

        // wp:M3-81 — §sec-performeval-rules-in-initializer initializer depth. A
        // direct eval whose ScriptBody ContainsArguments is an early SyntaxError
        // while this is > 0. Seed it for class field / static-block thunks
        // (chunk.IsInitializer), and for an arrow closure that inherited the
        // initializer context lexically (chunk.IsArrow && the closure's
        // InInitializer flag). Parameter default regions toggle it at runtime via
        // the Enter/ExitInitializer opcodes the compiler brackets them with.
        var initDepth = (chunk.IsInitializer
            || (chunk.IsArrow && currentFunction is { InInitializer: true })) ? 1 : 0;

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
        int ReadI32()
        {
            var v = BinaryPrimitives.ReadInt32LittleEndian(code.AsSpan(ip, 4));
            ip += 4;
            return v;
        }

        // wp:M3-23 — append "(at line:col)" to a runtime-error message using
        // the chunk's sparse position table. At a throw site `ip` has already
        // advanced past the offending opcode's operands, so PositionAt finds
        // the nearest preceding recorded entry (the opcode's start offset).
        // No-ops gracefully (returns the bare message) when no position was
        // recorded for that opcode.
        string AtPos(string message)
        {
            var pos = chunk.PositionAt(ip);
            return pos is { } p ? $"{message} (at {p.Line}:{p.Col})" : message;
        }

        // §14.15 try-frame stack — owns the catch/finally targets that the
        // outer C# catch(JsThrow) and the Return opcode handler consult.
        var tryStack = new Stack<TryFrame>();

        // §14.11 / §9.1.1.2 — stack of object Environment Records installed by
        // the running `with` statements (innermost last). The with-aware
        // opcodes consult it for unqualified name resolution. Lazily created so
        // the common (no-`with`) path allocates nothing. Each `with` body wraps
        // its PopWith in a finally, so abrupt completions unwind it correctly.
        // §10.2.1 — a function whose body was compiled inside a `with` seeds its
        // frame's with-stack from the object Environment Records captured at
        // closure-creation time, so its free identifiers resolve against them.
        List<JsObject>? withStack = currentFunction?.CapturedWith is { Count: > 0 } cap
            ? new List<JsObject>(cap)
            : null;

        // §9.1.1.2.1 HasBinding — does object Environment Record `obj` have a
        // usable binding for `name`? True when HasProperty(obj, name) and the
        // name is not blocked by the object's @@unscopables list.
        bool WithHasBinding(JsObject obj, string name)
        {
            if (!AbstractOperations.HasProperty(obj, name)) return false;
            var unscopables = AbstractOperations.Get(this, obj, JsPropertyKey.Symbol(Intrinsics.SymbolCtor.Unscopables));
            if (unscopables.IsObject)
            {
                var blocked = AbstractOperations.Get(this, unscopables.AsObject, name);
                if (JsValue.ToBoolean(blocked)) return false;
            }
            return true;
        }

        // Find the innermost with-object that provides a binding for `name`,
        // or null if none does (so the static fallback applies).
        JsObject? FindWithBinding(string name)
        {
            if (withStack is null) return null;
            for (var i = withStack.Count - 1; i >= 0; i--)
                if (WithHasBinding(withStack[i], name)) return withStack[i];
            return null;
        }

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
                // Local-slot operands are u16 (see ChunkBuilder.EmitSlot):
                // large minified bundles routinely declare >255 locals in one
                // function, which a u8 operand would alias modulo 256.
                case Opcode.DeclareLocal:
                {
                    var slot = ReadU16();
                    locals[slot] = JsValue.Undefined;
                    break;
                }
                case Opcode.LoadLocal: Push(locals[ReadU16()]); break;
                case Opcode.StoreLocal: locals[ReadU16()] = Pop(); break;

                // ----- Lexical bindings / Temporal Dead Zone -----
                // A let/const/class slot is seeded with the TDZ sentinel at
                // scope entry; any read/write before the declaration's
                // initializer runs throws ReferenceError (§§9.1.1.1.4 /
                // 13.3.1.1). The plain DeclareLocal/StoreLocal opcodes are
                // unchanged so var/param fast paths take no extra branch.
                case Opcode.DeclareLocalTdz:
                {
                    var slot = ReadU16();
                    locals[slot] = JsValue.Object(_runtime.Realm.TdzSentinel);
                    break;
                }
                case Opcode.InitCellLocalTdz:
                {
                    var slot = ReadU16();
                    locals[slot] = JsValue.Object(
                        new Cell(JsValue.Object(_runtime.Realm.TdzSentinel)));
                    break;
                }
                case Opcode.LoadLocalChecked:
                {
                    var v = locals[ReadU16()];
                    if (v.IsObject && ReferenceEquals(v.AsObject, _runtime.Realm.TdzSentinel))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Cannot access a lexical binding before initialization"));
                    Push(v);
                    break;
                }
                case Opcode.LoadCellLocalChecked:
                {
                    var cell = (Cell)locals[ReadU16()].AsObject;
                    var v = cell.Value;
                    if (v.IsObject && ReferenceEquals(v.AsObject, _runtime.Realm.TdzSentinel))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Cannot access a lexical binding before initialization"));
                    Push(v);
                    break;
                }
                case Opcode.StoreCellLocalChecked:
                {
                    var cell = (Cell)locals[ReadU16()].AsObject;
                    if (cell.Value.IsObject
                        && ReferenceEquals(cell.Value.AsObject, _runtime.Realm.TdzSentinel))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Cannot access a lexical binding before initialization"));
                    cell.Value = Pop();
                    break;
                }
                case Opcode.LoadUpvalueChecked:
                {
                    var idx = ReadU16();
                    var upV = upvalues[idx];
                    JsValue v;
                    if (upV.IsObject && upV.AsObject is Cell c) v = c.Value;
                    else v = upV;
                    if (v.IsObject && ReferenceEquals(v.AsObject, _runtime.Realm.TdzSentinel))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Cannot access a lexical binding before initialization"));
                    Push(v);
                    break;
                }
                case Opcode.StoreUpvalueChecked:
                {
                    var idx = ReadU16();
                    var cell = (Cell)upvalues[idx].AsObject;
                    if (cell.Value.IsObject
                        && ReferenceEquals(cell.Value.AsObject, _runtime.Realm.TdzSentinel))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(
                            "Cannot access a lexical binding before initialization"));
                    cell.Value = Pop();
                    break;
                }

                // ----- Captured locals (gap:closure-write-back) -----
                case Opcode.InitCellLocal:
                {
                    var slot = ReadU16();
                    locals[slot] = JsValue.Object(new Cell(JsValue.Undefined));
                    break;
                }
                case Opcode.LoadCellLocal:
                {
                    var slot = ReadU16();
                    var cell = (Cell)locals[slot].AsObject;
                    Push(cell.Value);
                    break;
                }
                case Opcode.StoreCellLocal:
                {
                    var slot = ReadU16();
                    var cell = (Cell)locals[slot].AsObject;
                    cell.Value = Pop();
                    break;
                }
                case Opcode.PromoteParamCell:
                {
                    var slot = ReadU16();
                    locals[slot] = JsValue.Object(new Cell(locals[slot]));
                    break;
                }
                case Opcode.StoreUpvalue:
                {
                    var idx = ReadU16();
                    var cell = (Cell)upvalues[idx].AsObject;
                    cell.Value = Pop();
                    break;
                }
                case Opcode.LoadUpvalueCell:
                {
                    var idx = ReadU16();
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
                    var slot = ReadU16();
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
                    _lastLoadName = name;
                    // wp:M3-73 — a free identifier resolves through this frame's
                    // eval-introduced var store (a var/function a direct eval
                    // injected) before the global object (spec order: local ->
                    // upvalue -> var-env -> global).
                    if (frameVarStore is not null && frameVarStore.TryGet(name, out var evCell0))
                    {
                        Push(evCell0.Value);
                        break;
                    }
                    var globalObj = _runtime.Realm.GlobalObject;
                    Push(AbstractOperations.Get(this, globalObj, name, JsValue.Object(globalObj)));
                    break;
                }
                case Opcode.LoadGlobalChecked:
                {
                    // §6.2.5.5 GetValue — reading a free identifier that resolves
                    // to no binding is an unresolvable Reference and throws a
                    // ReferenceError. Emitted for all free-identifier reads
                    // (except the operand of typeof/delete). An embedder — e.g.
                    // the browser, for graceful degradation of host globals it
                    // hasn't implemented yet — can suppress the throw via the
                    // realm's ThrowOnUnresolvedGlobalRead flag / LenientGlobalNames
                    // allowlist (then the read yields undefined, the old behavior).
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    _lastLoadName = name;
                    // wp:M3-73 — resolve through the eval-introduced var store
                    // before the global object (see LoadGlobal).
                    if (frameVarStore is not null && frameVarStore.TryGet(name, out var evCell1))
                    {
                        Push(evCell1.Value);
                        break;
                    }
                    var realm = _runtime.Realm;
                    var globalObj = realm.GlobalObject;
                    if (!globalObj.Has(name))
                    {
                        if (realm.ThrowOnUnresolvedGlobalRead && !realm.LenientGlobalNames.Contains(name))
                            throw new JsThrow(realm.NewReferenceError(name + " is not defined"));
                        Push(JsValue.Undefined);
                        break;
                    }
                    Push(AbstractOperations.Get(this, globalObj, name, JsValue.Object(globalObj)));
                    break;
                }
                case Opcode.StoreGlobal:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    // wp:M3-73 — an assignment to a free identifier writes through
                    // this frame's eval-introduced var store when it owns the name
                    // (a var/function a direct eval injected), before the global
                    // object. This is how the eval body's own `x = 4` and the
                    // caller's post-eval writes hit the injected binding.
                    if (frameVarStore is not null && frameVarStore.TryGet(name, out var evCell2))
                    {
                        evCell2.Value = value;
                        break;
                    }
                    var globalObj = _runtime.Realm.GlobalObject;
                    // §9.1.1.4.16 / §13.15.2 — in strict code, assigning to an
                    // identifier that resolves to no existing binding is a
                    // ReferenceError (no implicit global creation). Walk the
                    // prototype chain since inherited accessors/props count.
                    if (frameStrict && !globalObj.Has(name))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(name + " is not defined"));
                    // §10.1.9 — a strict assignment that the [[Set]] rejects
                    // (non-writable prop, accessor without setter) throws TypeError.
                    var ok = AbstractOperations.Set(this, globalObj, name, value, JsValue.Object(globalObj));
                    if (!ok && frameStrict)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot assign to read-only property '" + name + "'"));
                    break;
                }

                // wp:M3-72 — direct-eval caller-scope read. Resolve a free
                // identifier (matching a caller binding name) against the live
                // caller frame, falling back to a checked global load on a miss.
                case Opcode.LoadEvalScope:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    _lastLoadName = name;
                    if (evalScope is not null && evalScope.TryGet(name, out var entry))
                    {
                        var v = entry.Read();
                        // §13.3.1.1 — reading a caller lexical binding still in
                        // its TDZ throws ReferenceError.
                        if (v.IsObject && ReferenceEquals(v.AsObject, _runtime.Realm.TdzSentinel))
                            throw new JsThrow(_runtime.Realm.NewReferenceError(
                                "Cannot access '" + name + "' before initialization"));
                        Push(v);
                        break;
                    }
                    var realm = _runtime.Realm;
                    var globalObj = realm.GlobalObject;
                    if (!globalObj.Has(name))
                    {
                        if (realm.ThrowOnUnresolvedGlobalRead && !realm.LenientGlobalNames.Contains(name))
                            throw new JsThrow(realm.NewReferenceError(name + " is not defined"));
                        Push(JsValue.Undefined);
                        break;
                    }
                    Push(AbstractOperations.Get(this, globalObj, name, JsValue.Object(globalObj)));
                    break;
                }
                // wp:M3-72 — direct-eval caller-scope write. Write through the
                // live caller binding, else fall back to a global store.
                case Opcode.StoreEvalScope:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    if (evalScope is not null && evalScope.TryGet(name, out var entry))
                    {
                        entry.Write(value);
                        break;
                    }
                    var globalObj = _runtime.Realm.GlobalObject;
                    if (frameStrict && !globalObj.Has(name))
                        throw new JsThrow(_runtime.Realm.NewReferenceError(name + " is not defined"));
                    var ok2 = AbstractOperations.Set(this, globalObj, name, value, JsValue.Object(globalObj));
                    if (!ok2 && frameStrict)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot assign to read-only property '" + name + "'"));
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

                // wp:M3-73 — §19.2.1.3 EvalDeclarationInstantiation (non-global
                // branch). Idempotent pre-declaration of an eval-body top-level
                // var/function name into the CALLER frame's eval-introduced var
                // store (frameVarStore is the caller's store while running eval'd
                // code). Re-declaring an existing binding has no effect.
                case Opcode.DeclareEvalVar:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    frameVarStore?.Declare(name);
                    break;
                }
                // wp:M3-73 — set an eval-introduced binding (created by
                // DeclareEvalVar): a var initializer's value or a hoisted
                // function declaration's function object.
                case Opcode.StoreEvalVar:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    frameVarStore?.Set(name, value);
                    break;
                }
                // wp:M3-73 — `delete name` where the name may be an
                // eval-introduced binding. Such bindings are configurable
                // (§19.2.1.3), so remove it from the store and push true; if the
                // name isn't there this is the ordinary sloppy identifier-delete
                // no-op (still true). Deletes only at the store's OWN level — an
                // enclosing function's binding (parent store) is not in scope to
                // delete from here.
                case Opcode.DeleteEvalVar:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    frameVarStore?.Delete(name);
                    Push(JsValue.Boolean(true));
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
                    if (a.IsBigInt || b.IsBigInt)
                    {
                        if (!(a.IsBigInt && b.IsBigInt)) throw BigIntOps.MixedTypeError(_runtime.Realm, "%");
                        Push(BigIntOps.Remainder(_runtime.Realm, a.AsBigInt, b.AsBigInt));
                        break;
                    }
                    var ad = JsValue.ToNumber(a); var bd = JsValue.ToNumber(b);
                    // §Number::remainder — truncated remainder (result carries the
                    // sign of the dividend), matching C `fmod`. The C# `%` operator
                    // on doubles implements exactly these IEEE semantics, including
                    // the NaN/Infinity/zero edge cases (`x % 0` → NaN, `x % ∞` → x,
                    // `∞ % y` → NaN), unlike the floored `a - floor(a/b)*b` form.
                    Push(JsValue.Number(ad % bd));
                    break;
                }
                case Opcode.Pow:
                {
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var v = ToNumericOperand(Pop());
                    if (v.IsBigInt) { Push(BigIntOps.Negate(v.AsBigInt)); break; }
                    Push(JsValue.Number(-JsValue.ToNumber(v)));
                    break;
                }
                case Opcode.UnaryPlus:
                {
                    var v = ToNumericOperand(Pop());
                    // §13.5.4: unary + on a BigInt throws TypeError.
                    if (v.IsBigInt)
                        throw new JsThrow(_runtime.Realm.NewTypeError("Cannot convert a BigInt value to a number"));
                    Push(JsValue.Number(JsValue.ToNumber(v)));
                    break;
                }

                // ----- Bitwise (Number → Int32, or BigInt-only) -----
                case Opcode.BitOr:
                {
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var v = ToNumericOperand(Pop());
                    if (v.IsBigInt) { Push(BigIntOps.BitwiseNot(v.AsBigInt)); break; }
                    Push(JsValue.Number(~ToInt32(v))); break;
                }
                case Opcode.Shl:
                {
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    var b = Pop(); var a = Pop(); a = ToNumericOperand(a); b = ToNumericOperand(b);
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
                    _lastLoadName = name;
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
                    if (obj.IsObject)
                    {
                        var ok = AbstractOperations.Set(this, obj.AsObject, name, value);
                        // §10.1.9 / §13.15.2 — a strict assignment the [[Set]]
                        // rejects (non-writable data prop, accessor without a
                        // setter, or add to a non-extensible object) throws.
                        if (!ok && frameStrict)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot assign to read-only property '" + name + "'"));
                    }
                    else if (frameStrict && obj.IsNullish)
                    {
                        // §13.15.2 PutValue on a nullish base is always a TypeError.
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot set property '" + name + "' of " + JsValue.ToStringValue(obj)));
                    }
                    Push(value);
                    break;
                }
                case Opcode.LoadComputed:
                {
                    var key = Pop();
                    var obj = Pop();
                    var propertyKey = AbstractOperations.ToPropertyKey(this, key);
                    if (obj.IsObject) Push(AbstractOperations.Get(this, obj.AsObject, propertyKey));
                    else if (!obj.IsNullish) Push(AbstractOperations.Get(this, AbstractOperations.ToObject(_runtime.Realm, obj), propertyKey, obj));
                    else Push(JsValue.Undefined);
                    break;
                }
                case Opcode.ResolveComputedKey:
                {
                    // §13.15.2 / §13.3.3 — resolve a compound-assignment computed
                    // key once. Spec order: the base's coercibility is checked
                    // BEFORE ToPropertyKey, so `null[obj] *= …` throws TypeError
                    // without ever invoking the key's toString.
                    var rawKey = Pop();
                    var baseV = Peek();
                    if (baseV.IsNullish)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot read properties of " + (baseV.IsNull ? "null" : "undefined")
                            + " (reading a computed property)"));
                    var resolved = AbstractOperations.ToPropertyKey(this, rawKey);
                    Push(resolved.IsSymbol ? JsValue.Symbol(resolved.AsSymbol)
                                           : JsValue.String(resolved.AsString));
                    break;
                }
                case Opcode.StoreComputed:
                {
                    var value = Pop();
                    var key = Pop();
                    var obj = Pop();
                    if (obj.IsObject)
                    {
                        var pk = AbstractOperations.ToPropertyKey(this, key);
                        var ok = AbstractOperations.Set(this, obj.AsObject, pk, value);
                        if (!ok && frameStrict)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot assign to read-only property '" + pk + "'"));
                    }
                    Push(value);
                    break;
                }
                // wp:M3-26 — object-literal accessor (getter/setter) shorthand.
                // Reuse the class-member accessor installer so paired get/set on
                // the same key share one descriptor. Object-literal accessors are
                // enumerable (§13.2.5), unlike class accessors.
                case Opcode.DefineGetter:
                case Opcode.DefineSetter:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var fnVal = Pop();
                    var obj = Pop();
                    InstallObjectAccessor(obj.AsObject, JsPropertyKey.String(name),
                        isGetter: op == Opcode.DefineGetter, (JsFunction)fnVal.AsObject);
                    Push(obj);
                    break;
                }
                case Opcode.DefineGetterComputed:
                case Opcode.DefineSetterComputed:
                {
                    var fnVal = Pop();
                    var key = Pop();
                    var obj = Pop();
                    InstallObjectAccessor(obj.AsObject, AbstractOperations.ToPropertyKey(this, key),
                        isGetter: op == Opcode.DefineGetterComputed, (JsFunction)fnVal.AsObject);
                    Push(obj);
                    break;
                }
                // wp:M3-26 — CreateDataPropertyOrThrow (§7.3.5): define an own
                // enumerable/writable/configurable data property, replacing any
                // existing accessor or data descriptor on the key.
                case Opcode.DefineDataProperty:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var obj = Pop();
                    obj.AsObject.DefineOwnProperty(name,
                        PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
                    Push(obj);
                    break;
                }
                case Opcode.DefineDataComputed:
                {
                    var value = Pop();
                    var key = Pop();
                    var obj = Pop();
                    obj.AsObject.DefineOwnProperty(AbstractOperations.ToPropertyKey(this, key),
                        PropertyDescriptor.Data(value, writable: true, enumerable: true, configurable: true));
                    Push(obj);
                    break;
                }
                // wp:M3-64 — §13.2.5 MakeMethod. Stack: [obj, fn]. Stamp the
                // method's [[HomeObject]] = the object literal being built so
                // `super.x` inside it resolves against the object's prototype.
                // Peek (do not pop) so the stack stays [obj, fn] for the Define
                // opcode that follows.
                case Opcode.SetHomeObject:
                {
                    var fnVal = stack[sp - 1];
                    var objVal = stack[sp - 2];
                    if (fnVal.IsObject && fnVal.AsObject is JsFunction methodFn
                        && objVal.IsObject)
                    {
                        methodFn.HomeObject = objVal.AsObject;
                    }
                    break;
                }
                // wp:M3-64 — computed-key variant: stack is [obj, key, fn].
                case Opcode.SetHomeObjectComputed:
                {
                    var fnVal = stack[sp - 1];
                    var objVal = stack[sp - 3];
                    if (fnVal.IsObject && fnVal.AsObject is JsFunction methodFn
                        && objVal.IsObject)
                    {
                        methodFn.HomeObject = objVal.AsObject;
                    }
                    break;
                }

                // wp:M3-81 — §sec-performeval-rules-in-initializer: open/close the
                // initializer region bracketing a non-arrow function's parameter
                // default prologue. While initDepth > 0, a direct eval whose
                // ScriptBody ContainsArguments throws a SyntaxError.
                case Opcode.EnterInitializer:
                    initDepth++;
                    break;
                case Opcode.ExitInitializer:
                    initDepth--;
                    break;

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
                    if (!IsCallableValue(callee))
                        throw new JsThrow(JsValue.String(AtPos($"not a function: {JsValue.ToStringValue(callee)} (callee hint: '{_lastLoadName}')")));
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
                    if (!IsCallableValue(callee))
                        throw new JsThrow(JsValue.String(AtPos($"not a function: {JsValue.ToStringValue(callee)} (method hint: '{_lastLoadName}')")));
                    Push(AbstractOperations.Call(this, callee, receiver, callArgs));
                    break;
                }
                case Opcode.DirectEval:
                {
                    // wp:M3-71/72 — §19.2.1.1 PerformEval (direct path). The
                    // compiler emitted this for a bare-`eval` call resolving to
                    // the global slot, with a u16 EvalScopeDescriptor index of the
                    // calling function's variable environment followed by the u8
                    // argc. Confirm the callee is STILL the realm intrinsic; if it
                    // was reassigned (or is otherwise not the intrinsic), fall back
                    // to an ordinary indirect call with this=undefined.
                    var descIdx = ReadU16();
                    var argc = ReadU8();
                    var callArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) callArgs[i] = Pop();
                    var callee = Pop();
                    var intrinsic = _runtime.Realm.EvalFunction;
                    if (intrinsic is not null && callee.IsObject
                        && ReferenceEquals(callee.AsObject, intrinsic))
                    {
                        // wp:M3-72 — pair the compile-time descriptor with the live
                        // frame storage so the eval'd code reads/writes the
                        // caller's actual bindings.
                        var descriptor = (Bytecode.EvalScopeDescriptor)constants[descIdx]!;
                        var callerScope = BuildEvalScope(descriptor, locals, upvalues);
                        // wp:M3-73 — a non-strict direct eval whose caller is a
                        // function injects its own top-level var/function bindings
                        // into the caller frame's eval-introduced var store. Pass
                        // it by ref so PerformDirectEval can create it lazily and
                        // the rest of THIS frame then resolves those names too.
                        Push(PerformDirectEval(callArgs, callerScope, currentFunction, thisV,
                            newTarget, frameStrict, inInitializer: initDepth > 0,
                            ref frameVarStore));
                        break;
                    }
                    if (!IsCallableValue(callee))
                        throw new JsThrow(JsValue.String(AtPos($"not a function: {JsValue.ToStringValue(callee)} (callee hint: 'eval')")));
                    Push(AbstractOperations.Call(this, callee, JsValue.Undefined, callArgs));
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
                    // §14.11 / §10.2.1 — capture the active with-objects so the
                    // function body resolves free identifiers against them.
                    if (template.Body.CapturesWith && withStack is { Count: > 0 })
                        fn.CapturedWith = withStack.ToArray();
                    // wp:M3-64 — §14.2 / §13.2.5: an arrow inherits the enclosing
                    // method's [[HomeObject]] lexically so `super.x` inside it
                    // resolves against the enclosing method's home object.
                    if (template.Body.IsArrow && currentFunction?.HomeObject is { } h1)
                        fn.HomeObject = h1;
                    // wp:M3-81 — §sec-performeval-rules-in-initializer: an arrow
                    // created while this frame is inside an initializer region (a
                    // parameter default or a field/static initializer) inherits the
                    // "inside-initializer" status lexically, so a deferred direct
                    // eval in its body still hits the ContainsArguments early error
                    // when the arrow is later invoked.
                    if (template.Body.IsArrow && initDepth > 0)
                        fn.InInitializer = true;
                    // wp:M3-73 — snapshot the creating frame's eval-introduced var
                    // store so this closure resolves free identifiers through the
                    // vars a direct eval injected into the enclosing function's
                    // variable environment (spec scope chain) before the global.
                    if (frameVarStore is not null)
                        fn.CapturedEvalVarStore = frameVarStore;
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
                    var nUpvalues = ReadU16();
                    var template = (JsFunction)constants[idx]!;
                    var captured = new JsValue[nUpvalues];
                    for (var i = nUpvalues - 1; i >= 0; i--) captured[i] = Pop();
                    // Per B2-2: closure also routes through CreateInstance so
                    // it inherits Function.prototype and gets a per-call
                    // `prototype` own-property.
                    var closure = JsFunction.CreateInstance(_runtime.Realm, template, captured);
                    // §14.11 / §10.2.1 — capture the active with-objects (see
                    // LoadFunction) so the closure body resolves free identifiers
                    // against the enclosing object Environment Records.
                    if (template.Body.CapturesWith && withStack is { Count: > 0 })
                        closure.CapturedWith = withStack.ToArray();
                    // wp:M3-64 — an arrow closure inherits the enclosing method's
                    // [[HomeObject]] lexically for `super.x` (see LoadFunction).
                    if (template.Body.IsArrow && currentFunction?.HomeObject is { } h2)
                        closure.HomeObject = h2;
                    // wp:M3-81 — an arrow closure created inside an initializer
                    // region inherits the inside-initializer status (see
                    // LoadFunction) for the eval ContainsArguments early error.
                    if (template.Body.IsArrow && initDepth > 0)
                        closure.InInitializer = true;
                    // wp:M3-73 — snapshot the creating frame's eval-introduced var
                    // store (see LoadFunction).
                    if (frameVarStore is not null)
                        closure.CapturedEvalVarStore = frameVarStore;
                    Push(JsValue.Object(closure));
                    break;
                }

                case Opcode.LoadUpvalue:
                {
                    // gap:closure-write-back — every upvalue is a Cell, so
                    // dereference to push the current bound value. Use
                    // LoadUpvalueCell to push the raw cell (for further
                    // chained captures).
                    var idx = ReadU16();
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

                case Opcode.TemplateObject:
                {
                    var tmpl = (TemplateObjectTemplate)constants[ReadU16()]!;
                    var cache = _runtime.Realm.TemplateObjectCache;
                    if (!cache.TryGetValue(tmpl, out var strings))
                    {
                        strings = BuildTemplateObject(tmpl);
                        cache[tmpl] = strings;
                    }
                    Push(JsValue.Object(strings));
                    break;
                }

                case Opcode.New:
                {
                    var argc = ReadU8();
                    var newArgs = new JsValue[argc];
                    for (var i = argc - 1; i >= 0; i--) newArgs[i] = Pop();
                    var ctor = Pop();
                    if (!ctor.IsObject)
                        throw new JsThrow(JsValue.String(AtPos($"not a constructor: {JsValue.ToStringValue(ctor)} (new hint: '{_lastLoadName}')")));
                    Push(AbstractOperations.Construct(this, ctor, newArgs));
                    break;
                }

                // ----- Control flow -----
                case Opcode.Jump: { var d = ReadI32(); ip += d; break; }
                case Opcode.JumpIfTrue:
                {
                    var d = ReadI32();
                    if (JsValue.ToBoolean(Pop())) ip += d;
                    break;
                }
                case Opcode.JumpIfFalse:
                {
                    var d = ReadI32();
                    if (!JsValue.ToBoolean(Pop())) ip += d;
                    break;
                }
                case Opcode.JumpIfNotNullish:
                {
                    var d = ReadI32();
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

                case Opcode.ThrowConstAssignment:
                {
                    // §16.2.1.6.2 — an assignment to an immutable binding (a
                    // module's imported binding) is a runtime TypeError. Discard
                    // the would-be assigned value, then throw.
                    var nameIdx = ReadU16();
                    var name = (string)constants[nameIdx]!;
                    Pop();
                    throw new JsThrow(_runtime.Realm.NewTypeError(
                        $"Assignment to constant variable '{name}'."));
                }

                // ----- Try-frame management (gap:try-catch) -----
                case Opcode.EnterTry:
                {
                    var catchOff = ReadI32();
                    var finOff = ReadI32();
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
                        case PendingCompletion.Break:
                        {
                            // wp:M3-15 — resume an in-flight break/continue: run
                            // any further intervening finalizers, then jump to
                            // the loop/switch site. The finalizer just executed
                            // completed normally, so the saved completion still
                            // governs control flow (§14.15.3).
                            DivertBranchThroughFinally(
                                tryStack, frame.PendingTargetPc,
                                frame.PendingUnwindRemaining, ref ip);
                            break;
                        }
                    }
                    break;
                }
                // wp:M3-15 — break/continue exiting a loop/switch across one or
                // more enclosing finalizers. Operand: [u8 unwindCount][i16 target].
                case Opcode.BranchThroughFinally:
                {
                    int unwindCount = ReadU8();
                    var delta = ReadI32();
                    var targetPc = ip + delta; // i16 measured from after the operand
                    DivertBranchThroughFinally(tryStack, targetPc, unwindCount, ref ip);
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
                    var pk = AbstractOperations.ToPropertyKey(this, key);
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
                        Push(JsValue.Boolean(boxed.Delete(AbstractOperations.ToPropertyKey(this, key))));
                        break;
                    }
                    var delKey = AbstractOperations.ToPropertyKey(this, key);
                    var deleted = receiver.AsObject.Delete(delKey);
                    // §13.5.1.2 — in strict code, `delete` of a non-configurable
                    // own property is a TypeError (sloppy returns false instead).
                    if (!deleted && frameStrict)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot delete property '" + delKey + "'"));
                    Push(JsValue.Boolean(deleted));
                    break;
                }

                case Opcode.RequireObjectCoercible:
                {
                    // §7.2.1 RequireObjectCoercible — object destructuring of a
                    // null/undefined value is a TypeError (the value stays on the
                    // stack for the following property loads).
                    var v = Peek();
                    if (v.IsNullish)
                        throw new JsThrow(_runtime.Realm.NewTypeError("Cannot destructure null or undefined"));
                    break;
                }

                case Opcode.SetFunctionName:
                {
                    // §named-evaluation — stamp an inferred name onto an anonymous
                    // function/class produced as the value of a binding/assignment
                    // initializer. The compiler only emits this when the RHS is a
                    // syntactically anonymous function definition, so we just guard
                    // on the current `name` still being "" (so a named expression
                    // or a non-function value is never clobbered).
                    var nameIdx = ReadU16();
                    var target = Peek();
                    if (target.IsObject && target.AsObject is JsFunction nfn)
                    {
                        var cur = nfn.GetOwnPropertyDescriptor("name");
                        var isAnon = cur is null
                            || (cur.Value.IsData && cur.Value.Value.IsString && cur.Value.Value.AsString.Length == 0);
                        if (isAnon)
                        {
                            var nm = (string)constants[nameIdx]!;
                            nfn.DefineOwnProperty("name",
                                PropertyDescriptor.Data(JsValue.String(nm), writable: false, enumerable: false, configurable: true));
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

                case Opcode.IteratorBindNext:
                {
                    // §8.5.3 IteratorBindingInitialization for a single array-
                    // pattern element. Peek the record (kept across elements).
                    // Once the record is Done, further elements bind undefined
                    // WITHOUT calling next() again.
                    var top = Peek();
                    if (!top.IsObject || top.AsObject is not Starling.Js.Intrinsics.JsIteratorRecordHandle handle)
                        throw new InvalidOperationException("IteratorBindNext expects an iterator-record handle on the stack");
                    if (handle.Record.Done)
                    {
                        Push(JsValue.Undefined);
                    }
                    else
                    {
                        var step = AbstractOperations.IteratorStep(_runtime.Realm, this, ref handle.Record);
                        if (step is null)
                        {
                            Push(JsValue.Undefined);
                        }
                        else
                        {
                            // §8.5.3 — if IteratorValue (reading .value) throws,
                            // the iterator is considered closed: mark Done so a
                            // surrounding IteratorClose skips return().
                            JsValue v;
                            try { v = AbstractOperations.IteratorValue(this, step.Value); }
                            catch { handle.Record = handle.Record with { Done = true }; throw; }
                            Push(v);
                        }
                    }
                    break;
                }

                case Opcode.IteratorRest:
                {
                    // §8.5.3 BindingRestElement — collect every remaining value
                    // into a fresh dense array, driving the iterator to Done.
                    var top = Peek();
                    if (!top.IsObject || top.AsObject is not Starling.Js.Intrinsics.JsIteratorRecordHandle handle)
                        throw new InvalidOperationException("IteratorRest expects an iterator-record handle on the stack");
                    var rest = new JsArray(_runtime.Realm);
                    var n = 0;
                    while (!handle.Record.Done)
                    {
                        var step = AbstractOperations.IteratorStep(_runtime.Realm, this, ref handle.Record);
                        if (step is null) break;
                        rest.Set(n.ToString(System.Globalization.CultureInfo.InvariantCulture),
                            AbstractOperations.IteratorValue(this, step.Value));
                        n++;
                    }
                    Push(JsValue.Object(rest));
                    break;
                }

                case Opcode.IteratorCloseForThrow:
                {
                    // §7.4.10 IteratorClose in a throwing completion: invoke
                    // return() but swallow any error it raises so the original
                    // (in-flight) throw is the one that propagates.
                    var handleV = Pop();
                    if (handleV.IsObject && handleV.AsObject is Starling.Js.Intrinsics.JsIteratorRecordHandle h
                        && !h.Record.Done)
                    {
                        AbstractOperations.IteratorClose(this, h.Record, isThrowing: true);
                    }
                    break;
                }

                case Opcode.IteratorCloseFinally:
                {
                    // §7.4.8 / §14.7.5.6 — the for-of synthetic finalizer. The
                    // for-of frame is on top of the try-stack with its pending
                    // completion set (LeaveTry/Divert pushed it back before
                    // jumping here). Close iff that completion is abrupt; on a
                    // Normal pending completion (ordinary body finish OR a
                    // `continue` to this loop) leave the iterator open so the
                    // next iteration re-steps it. Swallow return()-errors only
                    // for a pending Throw so the in-flight throw still wins.
                    var handleV = Pop();
                    var pending = tryStack.Count > 0 ? tryStack.Peek().Pending : PendingCompletion.Normal;
                    if (pending != PendingCompletion.Normal
                        && handleV.IsObject
                        && handleV.AsObject is Starling.Js.Intrinsics.JsIteratorRecordHandle hf
                        && !hf.Record.Done)
                    {
                        AbstractOperations.IteratorClose(
                            this, hf.Record, isThrowing: pending == PendingCompletion.Throw);
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
                        // §10.1.11.1 OrdinaryOwnPropertyKeys ordering: integer
                        // ("array index") keys first, in ascending numeric order,
                        // then the remaining string keys in insertion order.
                        // Integer keys are collected across the whole prototype
                        // chain (deduped) and sorted; string keys keep their
                        // per-level insertion order (own object first, then up
                        // the chain).
                        var intKeys = new SortedDictionary<uint, string>();
                        var strKeys = new List<string>();
                        var current = obj;
                        while (current is not null)
                        {
                            foreach (var k in current.EnumerableKeys())
                            {
                                if (shadowed.Contains(k)) continue;
                                if (!emitted.Add(k)) continue;
                                if (JsArray.IsArrayIndex(k, out var idx)) intKeys[idx] = k;
                                else strKeys.Add(k);
                            }
                            // Any own key (enumerable or not) on this level
                            // shadows same-named keys further up the proto
                            // chain — per OrdinaryOwnPropertyKeys, all own
                            // names appear regardless of enumerability.
                            foreach (var k in current.Keys) shadowed.Add(k);
                            current = current.Prototype;
                        }
                        foreach (var pair in intKeys) snapshot.Push(JsValue.String(pair.Value));
                        foreach (var k in strKeys) snapshot.Push(JsValue.String(k));
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
                        var key = AbstractOperations.ToPropertyKey(this, Pop());
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
                            "'super' keyword unexpected here"));
                    Push(JsValue.Object(currentFunction.HomeObject));
                    break;
                }
                case Opcode.LoadNewTarget:
                {
                    Push(newTarget is null ? JsValue.Undefined : JsValue.Object(newTarget));
                    break;
                }
                case Opcode.DynamicImport:
                {
                    // wp:M3-03c — §13.3.10 import(specifier). Hand the specifier
                    // value + this chunk's referrer URL to the active loader and
                    // push the resulting Promise. Every load/eval failure is a
                    // rejection inside ImportDynamic — the only synchronous throw
                    // is when no loader is wired into the realm.
                    // wp:M3-63 — the referrer is the ACTIVE script/module's source
                    // path (SourcePath), which is identical across all nested
                    // functions, NOT the running function's own chunk Name (which
                    // for a nested async/arrow/generator is its function name, not
                    // the script path — so a relative specifier would otherwise
                    // resolve against the cwd). Fall back to Name for the
                    // top-level chunk whose Name already is the path.
                    var spec = Pop();
                    var loader = _runtime.Realm.ModuleLoader
                        ?? throw new JsThrow(_runtime.Realm.NewTypeError(
                            "dynamic import() is not supported in this context (no module loader)"));
                    Push(JsValue.Object(loader.ImportDynamic(spec, chunk.SourcePath ?? chunk.Name)));
                    break;
                }
                case Opcode.LoadImportMeta:
                {
                    // wp:M3-03c — §13.3.12 import.meta. The active module's
                    // resolved URL is its SourcePath (identical across nested
                    // functions); ask the loader for that module's lazily-built
                    // meta object. wp:M3-63 — use SourcePath so import.meta.url
                    // stays consistent inside nested functions; fall back to Name.
                    var loader = _runtime.Realm.ModuleLoader;
                    var meta = loader?.ResolveMetaForUrl(chunk.SourcePath ?? chunk.Name);
                    if (meta is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "import.meta is only valid inside a module"));
                    Push(JsValue.Object(meta));
                    break;
                }
                case Opcode.RestParam:
                {
                    // §10.2.11 — collect the rest parameter's arguments
                    // (args[start..argc)) into a real dense array. Works in
                    // arrows too: reads the frame's `args` directly rather than
                    // the (arrow-absent) `arguments` object.
                    var start = ReadU16();
                    var rest = new JsArray(_runtime.Realm);
                    for (var i = start; i < args.Length; i++)
                        rest.Push(args[i]);
                    Push(JsValue.Object(rest));
                    break;
                }
                case Opcode.MakeArguments:
                {
                    // §10.4.4 — materialize the callee's `arguments` object from
                    // this frame's received args and bind it into `slot`. If the
                    // slot was pre-initialized to a Cell (because a nested arrow
                    // captures `arguments`), write through the cell so the
                    // closure observes the same object; otherwise store directly.
                    var slot = ReadU16();
                    var argObj = JsValue.Object(_runtime.Realm.CreateArgumentsObject(args, frameStrict));
                    if (locals[slot].IsObject && locals[slot].AsObject is Cell cell)
                        cell.Value = argObj;
                    else
                        locals[slot] = argObj;
                    break;
                }
                case Opcode.MakeMappedArguments:
                {
                    // §10.4.4.6 — build the mapped arguments object, live-linking
                    // each parameter index to its local slot in THIS frame so
                    // arguments[i] ⇄ parameter i. Must run after parameter binding
                    // (including PromoteParamCell) so a captured parameter's slot
                    // already holds its Cell and the map shares it.
                    var slot = ReadU16();
                    var paramCount = ReadU16();
                    var slotForIndex = new int[paramCount];
                    for (var i = 0; i < paramCount; i++)
                    {
                        var ps = ReadU16();
                        // §10.4.4.6 — index i is mapped only when its parameter is
                        // the last with that name (compiler marks shadowed dupes
                        // 0xFFFF) AND an argument was actually passed at i.
                        slotForIndex[i] = (ps == 0xFFFF || i >= args.Length) ? -1 : ps;
                    }
                    var argObj = JsValue.Object(_runtime.Realm.CreateMappedArgumentsObject(
                        args, locals, slotForIndex, currentFunction));
                    if (locals[slot].IsObject && locals[slot].AsObject is Cell cell)
                        cell.Value = argObj;
                    else
                        locals[slot] = argObj;
                    break;
                }
                case Opcode.BindCallee:
                {
                    // wp:M3-21 — §15.2.5. Bind a named function expression's own
                    // name to the executing function instance, so the body can
                    // refer to itself. `currentFunction` IS the callee. If the
                    // slot was pre-initialized to a Cell (a nested closure
                    // captures the name) write through the cell so the closure
                    // observes the same binding; otherwise store directly.
                    var slot = ReadU16();
                    var calleeVal = currentFunction is null
                        ? JsValue.Undefined
                        : JsValue.Object(currentFunction);
                    if (locals[slot].IsObject && locals[slot].AsObject is Cell cell)
                        cell.Value = calleeVal;
                    else
                        locals[slot] = calleeVal;
                    break;
                }
                case Opcode.BindThis:
                {
                    thisV = Pop();
                    _currentDerivedThis = thisV;
                    break;
                }

                // ----- with statement (§14.11 / §9.1.1.2) -----
                case Opcode.PushWith:
                {
                    // §14.11.2 — ToObject the head value and install it as an
                    // object Environment Record for the body.
                    var v = Pop();
                    var envObj = AbstractOperations.ToObject(_runtime.Realm, v);
                    (withStack ??= new List<JsObject>()).Add(envObj);
                    break;
                }
                case Opcode.PopWith:
                {
                    if (withStack is { Count: > 0 }) withStack.RemoveAt(withStack.Count - 1);
                    break;
                }
                case Opcode.WithLoadOrMiss:
                {
                    var name = (string)constants[ReadU16()]!;
                    var miss = ReadI32();
                    var obj = FindWithBinding(name);
                    if (obj is not null)
                    {
                        _lastLoadName = name;
                        Push(AbstractOperations.Get(this, obj, name, JsValue.Object(obj)));
                        ip += miss;
                    }
                    // miss: fall through to the static fallback the compiler emitted.
                    break;
                }
                case Opcode.WithLoadMethodOrMiss:
                {
                    var name = (string)constants[ReadU16()]!;
                    var miss = ReadI32();
                    var obj = FindWithBinding(name);
                    if (obj is not null)
                    {
                        // §9.1.1.2 WithBaseObject: the call's `this` is the
                        // binding object — push [withObj, fn] for CallMethod.
                        _lastLoadName = name;
                        Push(JsValue.Object(obj));
                        Push(AbstractOperations.Get(this, obj, name, JsValue.Object(obj)));
                        ip += miss;
                    }
                    break;
                }
                case Opcode.WithStoreOrMiss:
                {
                    var name = (string)constants[ReadU16()]!;
                    var miss = ReadI32();
                    var obj = FindWithBinding(name);
                    if (obj is not null)
                    {
                        var value = Pop();
                        var ok = AbstractOperations.Set(this, obj, name, value, JsValue.Object(obj));
                        if (!ok && frameStrict)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot assign to read-only property '" + name + "'"));
                        ip += miss;
                    }
                    // miss: leave the value on the stack for the static store.
                    break;
                }
                case Opcode.WithDeleteOrMiss:
                {
                    var name = (string)constants[ReadU16()]!;
                    var miss = ReadI32();
                    var obj = FindWithBinding(name);
                    if (obj is not null)
                    {
                        var ok = obj.Delete(name);
                        if (!ok && frameStrict)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot delete property '" + name + "'"));
                        Push(JsValue.Boolean(ok));
                        ip += miss;
                    }
                    break;
                }
                case Opcode.WithCompoundLoad:
                {
                    // §13.15.2 — resolve the compound-assignment LHS Reference base
                    // ONCE. On a with-binding hit, stash the base object so the
                    // paired WithCompoundStore writes to the SAME object even if
                    // the getter deletes the binding mid-evaluation.
                    var name = (string)constants[ReadU16()]!;
                    var baseSlot = ReadU16();
                    var miss = ReadI32();
                    var obj = FindWithBinding(name);
                    if (obj is not null)
                    {
                        locals[baseSlot] = JsValue.Object(obj);
                        _lastLoadName = name;
                        Push(AbstractOperations.Get(this, obj, name, JsValue.Object(obj)));
                        ip += miss;
                    }
                    else
                    {
                        // No with-base: mark the slot and fall through to the
                        // statically-compiled fallback load.
                        locals[baseSlot] = JsValue.Undefined;
                    }
                    break;
                }
                case Opcode.WithCompoundStore:
                {
                    var name = (string)constants[ReadU16()]!;
                    var baseSlot = ReadU16();
                    var miss = ReadI32();
                    var captured = locals[baseSlot];
                    if (captured.Kind == JsValueKind.Object)
                    {
                        // Write through the once-resolved Reference base. The
                        // result copy (Dup'd by the compiler) stays beneath.
                        var value = Pop();
                        var baseObj = captured.AsObject;
                        var ok = AbstractOperations.Set(this, baseObj, name, value, captured);
                        if (!ok && frameStrict)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                "Cannot assign to read-only property '" + name + "'"));
                        ip += miss;
                    }
                    // miss (captured is undefined): leave the value on the stack
                    // for the static store fallback.
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
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' keyword unexpected here"));
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
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' keyword unexpected here"));
                    // §13.3.4 / §9.1.1.4 PutValue with a Super Reference: the
                    // [[Set]] runs against the super base (GetPrototypeOf([[HomeObject]]))
                    // with the receiver = `this`. So a setter found on the super
                    // base runs with this=receiver; otherwise OrdinarySet creates
                    // the own data property on the receiver, not the prototype.
                    var superBase = currentFunction.HomeObject.Prototype;
                    if (superBase is not null)
                        AbstractOperations.Set(this, superBase, name, value, thisV);
                    Push(value);
                    break;
                }
                case Opcode.LoadSuperComputed:
                {
                    // wp:M3-04h — super[expr] read. Like LoadSuperProperty but the
                    // key is taken from the stack and coerced via ToPropertyKey
                    // (§13.3.7.2 GetSuperBase + §13.3.4 MakeSuperPropertyReference).
                    var key = Pop();
                    if (currentFunction?.HomeObject is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' keyword unexpected here"));
                    var propertyKey = AbstractOperations.ToPropertyKey(this, key);
                    var superProto = currentFunction.HomeObject.Prototype;
                    if (superProto is null)
                    {
                        Push(JsValue.Undefined);
                        break;
                    }
                    Push(AbstractOperations.Get(this, superProto, propertyKey, thisV));
                    break;
                }
                case Opcode.StoreSuperComputed:
                {
                    // wp:M3-04h — super[expr] = v. Mirrors StoreSuperProperty:
                    // §13.3.4 PutValue with a Super Reference runs [[Set]] against
                    // the super base with the receiver = `this`. Key is coerced
                    // via ToPropertyKey.
                    var value = Pop();
                    var key = Pop();
                    if (currentFunction?.HomeObject is null)
                        throw new JsThrow(_runtime.Realm.NewSyntaxError("'super' keyword unexpected here"));
                    var propertyKey = AbstractOperations.ToPropertyKey(this, key);
                    var superBase = currentFunction.HomeObject.Prototype;
                    if (superBase is not null)
                        AbstractOperations.Set(this, superBase, propertyKey, value, thisV);
                    Push(value);
                    break;
                }
                case Opcode.PrivateGet:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var receiver = Pop();
                    // §13.3.4.2 PrivateGet → §7.3.x PrivateElementFind: a TypeError
                    // unless the receiver ITSELF carries the brand. The brand is a
                    // per-object set (never prototype-walked), so a wrong receiver —
                    // a subclass constructor for a static private member, a Proxy
                    // wrapping an instance, or any object before its brand is
                    // installed (derived class, pre-super()) — throws here.
                    if (!receiver.IsObject || !receiver.AsObject.HasPrivateBrand(name))
                    {
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot read private member from an object whose class did not declare it"));
                    }
                    // §13.3.4.2 PrivateGet — a private accessor's getter must be
                    // invoked with `this` = receiver; a private method or field
                    // yields its value directly. Routing through the AO walks the
                    // chain (private methods/accessors live on the prototype) and
                    // invokes any getter; a §13.3.4 get on a set-only accessor is
                    // a TypeError.
                    var obj = receiver.AsObject;
                    var (getDesc, _) = FindPrivateDescriptor(obj, name);
                    if (getDesc is { IsAccessor: true } ga)
                    {
                        if (ga.Getter is null)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                $"'{name}' was defined without a getter"));
                        Push(AbstractOperations.Call(this, JsValue.Object(ga.Getter), receiver, Array.Empty<JsValue>()));
                    }
                    else
                    {
                        Push(getDesc?.Value ?? obj.Get(name));
                    }
                    break;
                }
                case Opcode.PrivateSet:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var value = Pop();
                    var receiver = Pop();
                    // §13.3.4.3 PrivateSet → PrivateElementFind: a TypeError unless
                    // the receiver ITSELF carries the brand (per-object set, never
                    // prototype-walked) — see PrivateGet above.
                    if (!receiver.IsObject || !receiver.AsObject.HasPrivateBrand(name))
                    {
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot write private member from an object whose class did not declare it"));
                    }
                    // §13.3.4.3 PrivateSet — invoke a private accessor's setter
                    // with `this` = receiver; writing a get-only accessor or a
                    // private method is a TypeError. A private field writes its
                    // own slot directly.
                    var sobj = receiver.AsObject;
                    var (setDesc, ownsField) = FindPrivateDescriptor(sobj, name);
                    if (setDesc is { IsAccessor: true } sa)
                    {
                        if (sa.Setter is null)
                            throw new JsThrow(_runtime.Realm.NewTypeError(
                                $"'{name}' was defined without a setter"));
                        AbstractOperations.Call(this, JsValue.Object(sa.Setter), receiver, new[] { value });
                    }
                    else if (setDesc is { IsAccessor: false } && !ownsField)
                    {
                        // A private *method* (data descriptor on the prototype) is
                        // not writable (§13.3.4.3 step "if entry.[[Kind]] is method").
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            $"private method '{name}' is not writable"));
                    }
                    else
                    {
                        sobj.Set(name, value);
                    }
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
                    // §7.3.28 PrivateFieldAdd installs the private element onto
                    // the receiver's [[PrivateElements]] — i.e. the brand. The
                    // brand check (PrivateGet/PrivateSet/PrivateIn/call) consults
                    // this per-object set, not the prototype chain.
                    receiver.AsObject.AddPrivateBrand(name);
                    break;
                }
                case Opcode.PrivateIn:
                {
                    var idx = ReadU16();
                    var name = (string)constants[idx]!;
                    var operand = Pop();
                    // §13.10.1 step 4 — a non-object right operand is a TypeError
                    // (not `false`).
                    if (!operand.IsObject)
                        throw new JsThrow(_runtime.Realm.NewTypeError(
                            "Cannot use 'in' operator to search for a private name in a non-object"));
                    // §13.10.1 / §7.3.x — `#x in obj` is true iff obj ITSELF carries
                    // the brand for #x (per-object set, never prototype-walked). A
                    // subclass constructor or a Proxy wrapping an instance does not
                    // carry the brand, so this yields false rather than true.
                    Push(JsValue.Boolean(operand.AsObject.HasPrivateBrand(name)));
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
                    // §10.2.1.3 InitializeInstanceElements — for a derived class
                    // this runs only after super() returns, so the instance's
                    // private brands appear at exactly the spec-correct moment;
                    // private access before this point throws a TypeError.
                    // Install instance private method/accessor brands first: their
                    // bodies live on the shared prototype, but each instance must
                    // carry the brand in its own [[PrivateElements]] set.
                    if (currentFunction?.InstancePrivateBrands is { } brands && thisV.IsObject)
                    {
                        var brandObj = thisV.AsObject;
                        foreach (var b in brands) brandObj.AddPrivateBrand(b);
                    }
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
                    else if (currentFunction?.Kind == JsFunctionKind.AsyncGenerator)
                    {
                        // §27.6.3.8 AsyncGeneratorYield step 1: a plain `yield x`
                        // inside an async generator must `Await(x)` before the
                        // result is delivered to the pending request. If the
                        // operand is a promise that rejects, AwaitOnWorker
                        // injects the rejection as a throw at this suspension
                        // point, so the body unwinds and the driver rejects the
                        // consumer's next() promise (rather than resolving it).
                        // On fulfilment we yield the awaited value, preserving
                        // the {value, done:false} result.
                        toYield = AwaitOnWorker(suspension, yielded);
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
                case Opcode.PrologueEnd:
                {
                    // §10.2.1.3 — the parameter-binding prologue has run
                    // synchronously on the worker thread. Hand off to the
                    // caller (Start{Generator,Async,AsyncGenerator}Body) so it
                    // can observe a prologue throw before producing the
                    // generator/promise. No value travels across this boundary;
                    // the body resumes here on the first real next()/drive.
                    // If suspension is null (defensive), it's a no-op.
                    if (suspension is not null)
                        suspension.WorkerYield(JsValue.Undefined);
                    break;
                }
                case Opcode.YieldDelegate:
                {
                    var isAsync = ReadU8() != 0;
                    var iterable = Pop();
                    if (suspension is null)
                    {
                        throw new JsThrow(_runtime.Realm.NewSyntaxError(
                            "yield is only valid in generator functions"));
                    }
                    Push(ExecuteYieldDelegate(suspension, iterable, isAsync));
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

    /// <summary>§27.5.3.2 / §27.6.3.7 YieldDelegate body — runs the full
    /// <c>yield*</c> protocol inside a single opcode handler. Forwards the outer
    /// generator's resume kind (next / return / throw) into the inner
    /// iterator's matching method on each round-trip with the outer
    /// caller. Returns the value to push as the result of the yield*
    /// expression (the inner iterator's final <c>value</c> on done, or
    /// the value of an inner .return that completes early).
    /// <para>When <paramref name="isAsync"/> the inner iterator is acquired via
    /// the async iteration protocol (<c>@@asyncIterator</c>, falling back to a
    /// sync iterator wrapped as async per §27.1.4.1) and every result returned
    /// by inner.next / .throw / .return is <c>await</c>ed before use.</para></summary>
    private JsValue ExecuteYieldDelegate(SuspendedFrame suspension, JsValue iterable, bool isAsync)
    {
        var realm = _runtime.Realm;
        IteratorRecord record;
        bool syncWrapped = false;
        if (isAsync)
        {
            var handle = GetAsyncIteratorHandle(iterable);
            record = handle.Record;
            syncWrapped = handle.SyncWrapped;
        }
        else
        {
            record = AbstractOperations.GetIterator(realm, this, iterable);
        }
        var innerIter = record.Iterator;
        var nextMethod = record.NextMethod;

        // §27.6.3.7 — in an async generator, `yield* x` awaits every result the
        // inner iterator produces before inspecting done/value. For a true
        // async iterator next/throw/return already return a promise; for a
        // sync iterable wrapped as async (§27.1.4) the result is a plain
        // iterator-result whose `value` must itself be awaited.
        JsValue MaybeAwait(JsValue v)
        {
            if (!isAsync) return v;
            if (syncWrapped)
                v = JsValue.Object(WrapSyncIteratorResult(v));
            return AwaitOnWorker(suspension, v);
        }
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
                innerResult = MaybeAwait(AbstractOperations.Call(this, nextMethod, innerIter,
                    new[] { received }));
            }
            else if (receivedKind == 1)
            {
                // Throw completion → inner.throw(received) if present.
                var throwM = AbstractOperations.GetMethod(this, innerIter, "throw");
                if (throwM.IsUndefined || throwM.IsNull)
                {
                    // No throw method: close the iterator and re-throw.
                    // §27.6.3.7 — for an async iterator the close is awaited.
                    if (isAsync)
                        AsyncIteratorCloseOnWorker(suspension, innerIter);
                    else
                        AbstractOperations.IteratorClose(this, record, isThrowing: true);
                    throw new JsThrow(realm.NewTypeError(
                        "Inner iterator does not have a 'throw' method"));
                }
                innerResult = MaybeAwait(AbstractOperations.Call(this, throwM, innerIter,
                    new[] { received }));
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
                innerResult = MaybeAwait(AbstractOperations.Call(this, retM, innerIter,
                    new[] { received }));
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
                suspension.SuspendKind = 0; // real yield (not an internal await)
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
            suspension.SuspendKind = 0; // real yield (not an internal await)
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

    /// <summary>Worker-side <c>await</c> used by <c>yield*</c> inside an async
    /// generator (§27.6.3.7). Suspends the worker with <see cref="SuspendedFrame.SuspendKind"/>
    /// = 1 so the async-generator driver settles <paramref name="value"/> as a
    /// promise and resumes us with the resolved value (or injects a throw on
    /// rejection). Mirrors the <c>Suspend</c> opcode's await arm.</summary>
    private static JsValue AwaitOnWorker(SuspendedFrame suspension, JsValue value)
    {
        suspension.SuspendKind = 1;
        var resumed = suspension.WorkerYield(value);
        if (suspension.ResumeWithThrow)
        {
            suspension.ResumeWithThrow = false;
            throw new JsThrow(resumed);
        }
        // A .return() injected while awaiting still has to unwind the body.
        if (suspension.ResumeWithReturn)
        {
            suspension.ResumeWithReturn = false;
            throw new JsReturnSentinel(resumed);
        }
        return resumed;
    }

    /// <summary>§7.4.11 AsyncIteratorClose used by <c>yield*</c> when the inner
    /// async iterator lacks a <c>throw</c> method — invoke its <c>return</c>
    /// (if any) and <c>await</c> the result on the worker thread before the
    /// outer TypeError propagates.</summary>
    private void AsyncIteratorCloseOnWorker(SuspendedFrame suspension, JsValue innerIter)
    {
        JsValue ret;
        try { ret = AbstractOperations.GetMethod(this, innerIter, "return"); }
        catch { return; }
        if (ret.IsUndefined || ret.IsNull) return;
        JsValue result;
        try { result = AbstractOperations.Call(this, ret, innerIter, Array.Empty<JsValue>()); }
        catch { return; }
        // Await the close result; swallow any rejection so the original throw
        // (the missing-throw-method TypeError) wins per §7.4.11.
        try { AwaitOnWorker(suspension, result); }
        catch (JsThrow) { /* original completion already throwing */ }
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

    /// <summary>wp:M3-15 — drive a <c>break</c>/<c>continue</c> abrupt completion
    /// out of <paramref name="unwindCount"/> enclosing try-frames, running each
    /// frame's finalizer (innermost first) on the way to
    /// <paramref name="targetPc"/> (the loop/switch break/continue site).
    ///
    /// <para>Pops up to <paramref name="unwindCount"/> frames. A popped frame
    /// that is still in its try/catch phase and carries a finalizer suspends
    /// the unwind: its finalizer runs as a <see cref="PendingCompletion.Break"/>
    /// carrying the target PC and the remaining count, and <c>EndFinally</c>
    /// re-enters this helper for the rest. Frames without a finalizer (or
    /// already running one) are simply discarded. When all intervening
    /// finalizers have run, control jumps to <paramref name="targetPc"/>.</para>
    ///
    /// <para>Per §14.15.3 a finalizer that itself completes abruptly overrides
    /// the pending Break — that is handled naturally because the finalizer's
    /// own break/continue/return/throw opcodes re-drive the try-stack and never
    /// reach the <c>EndFinally</c> that would resume this one.</para></summary>
    private static void DivertBranchThroughFinally(
        Stack<TryFrame> tryStack, int targetPc, int unwindCount, ref int ip)
    {
        while (unwindCount > 0 && tryStack.Count > 0)
        {
            var frame = tryStack.Pop();
            unwindCount--;
            if (frame.Phase != TryPhase.RunningFinally && frame.FinallyPc != -1)
            {
                frame.Phase = TryPhase.RunningFinally;
                frame.Pending = PendingCompletion.Break;
                frame.PendingValue = JsValue.Undefined;
                frame.PendingTargetPc = targetPc;
                frame.PendingUnwindRemaining = unwindCount;
                tryStack.Push(frame);
                ip = frame.FinallyPc;
                return;
            }
        }
        // No (further) finalizer to run — jump straight to the loop/switch site.
        ip = targetPc;
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

    /// <summary>§13.6 ApplyStringOrNumericBinaryOperator step 1 (and ToNumeric
    /// §7.1.22 step 1): coerce an operand of a numeric/bitwise binary operator to
    /// its primitive via ToPrimitive(number) so an object operand (e.g.
    /// <c>new Number(7)</c>, <c>new String("2")</c>, <c>new Boolean(true)</c>)
    /// converts through its <c>valueOf</c>/<c>toString</c> rather than yielding
    /// NaN. Primitives are returned unchanged so the existing fast paths
    /// (<see cref="JsValue.ToNumber"/>, <see cref="ToInt32"/>, the BigInt
    /// branches) are untouched. The static <see cref="JsValue.ToNumber"/> cannot
    /// do this itself — it has no VM/realm to dispatch the user method. Used by
    /// the arithmetic (<c>- * / % **</c>) and bitwise (<c>| &amp; ^ &lt;&lt;
    /// &gt;&gt; &gt;&gt;&gt;</c>) ops, which compound assignment reuses, so an
    /// object operand coerces identically whether written as <c>a * b</c> or
    /// <c>a *= b</c>. (<c>+</c> already runs ToPrimitive in <see cref="JsAdd"/>.)</summary>
    private JsValue ToNumericOperand(JsValue v)
        => v.IsObject ? AbstractOperations.ToPrimitive(this, v, "number") : v;


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

        // Install methods. Private instance methods/accessors live on the shared
        // prototype, but each instance must carry their brand in its own
        // [[PrivateElements]] set (collected here, applied at construction).
        // Static private members brand the constructor object directly.
        List<string>? instancePrivateBrands = null;
        for (var i = 0; i < template.Methods.Count; i++)
        {
            var m = template.Methods[i];
            var fnInstance = JsFunction.CreateInstance(realm, m.Template, methodUpvalues[i]);
            fnInstance.HomeObject = m.IsStatic ? ctorInstance : protoObj;
            var owner = m.IsStatic ? (JsObject)ctorInstance : protoObj;
            if (m.IsComputed)
            {
                // wp:M3-04f — coerced key value (Symbol or String) off the stack.
                var keyPk = AbstractOperations.ToPropertyKey(this, methodComputedKeys[i]);
                // §15.7.10 — a static class method/accessor may not be named
                // "prototype" (a computed key resolving to it → TypeError).
                if (m.IsStatic && keyPk.IsString && keyPk.AsString == "prototype")
                    throw new JsThrow(realm.NewTypeError(
                        "Classes may not have a static property named 'prototype'"));
                StampMethodName(fnInstance, keyPk, m.Kind);
                InstallMethodOrAccessor(owner, keyPk, m.Kind, fnInstance);
            }
            else
            {
                var keyForInstall = m.MangledPrivateKey ?? m.StaticKey!;
                InstallMethodOrAccessor(owner, keyForInstall, m.Kind, fnInstance);
                if (m.MangledPrivateKey is { } mangledMethod)
                {
                    if (m.IsStatic)
                    {
                        // §15.7.10 — only the constructor object carries the brand
                        // for a static private method/accessor.
                        ctorInstance.AddPrivateBrand(mangledMethod);
                    }
                    else
                    {
                        (instancePrivateBrands ??= new List<string>()).Add(mangledMethod);
                    }
                }
            }
        }
        if (instancePrivateBrands is not null)
            ctorInstance.InstancePrivateBrands = instancePrivateBrands;

        // §15.7.14 — for a class *declaration*, bind the class name to the
        // constructor BEFORE static elements run, so `static { … C … }` blocks
        // and `static p = C.q` field initializers can reference the class by
        // name. (Class expressions keep their inner name scoped to the body;
        // BindNameToGlobal is false for them, so this does not leak.)
        if (template.BindNameToGlobal && template.Name.Length > 0)
            AbstractOperations.Set(this, realm.GlobalObject, template.Name,
                JsValue.Object(ctorInstance), JsValue.Object(realm.GlobalObject));

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
                ? AbstractOperations.ToPropertyKey(this, fieldComputedKeys[i])
                : (JsPropertyKey?)null;
            if (f.IsStatic)
            {
                if (computedKey is { } sck)
                {
                    // §15.7.10 — a static field may not be named "prototype".
                    if (sck.IsString && sck.AsString == "prototype")
                        throw new JsThrow(realm.NewTypeError(
                            "Classes may not have a static property named 'prototype'"));
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
                        // §15.7.10 — only the constructor object carries the brand
                        // for a static private field.
                        ctorInstance.AddPrivateBrand(key);
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
                // wp:M3-71 — an instance field initializer has [[HomeObject]] =
                // the class prototype so `super.x` (incl. inside a direct eval in
                // the initializer) resolves against the parent prototype.
                thunk.HomeObject = protoObj;
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
                    // wp:M3-71 — [[HomeObject]] = class prototype so `super.x`
                    // (incl. inside a direct eval in the initializer) resolves.
                    initFn.HomeObject = protoObj;
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

    /// <summary>§13.2.8.4 — materialise a tagged template's frozen strings
    /// object: a cooked array carrying a frozen <c>.raw</c> sibling. Both arrays
    /// are integrity-frozen (own props non-writable/non-configurable, then
    /// non-extensible) so a tag function can't mutate the shared, cached object.</summary>
    private JsArray BuildTemplateObject(TemplateObjectTemplate tmpl)
    {
        var realm = _runtime.Realm;

        var rawItems = new JsValue[tmpl.Raw.Count];
        for (var i = 0; i < rawItems.Length; i++) rawItems[i] = JsValue.String(tmpl.Raw[i]);
        var rawArr = new JsArray(realm, rawItems);
        FreezeOwnProperties(rawArr);

        var cookedItems = new JsValue[tmpl.Cooked.Count];
        for (var i = 0; i < cookedItems.Length; i++)
            cookedItems[i] = tmpl.Cooked[i] is { } s ? JsValue.String(s) : JsValue.Undefined;
        var cooked = new JsArray(realm, cookedItems);
        cooked.DefineOwnProperty("raw",
            PropertyDescriptor.Data(JsValue.Object(rawArr), writable: false, enumerable: false, configurable: false));
        FreezeOwnProperties(cooked);

        return cooked;
    }

    private static void FreezeOwnProperties(JsObject obj)
    {
        foreach (var key in new List<JsPropertyKey>(obj.OwnPropertyKeys))
        {
            var d = obj.GetOwnPropertyDescriptor(key);
            if (d is null) continue;
            var desc = d.Value;
            obj.DefineOwnProperty(key, desc.IsAccessor
                ? PropertyDescriptor.Accessor(desc.Getter, desc.Setter, desc.Enumerable, configurable: false)
                : PropertyDescriptor.Data(desc.Value, writable: false, enumerable: desc.Enumerable, configurable: false));
        }
        obj.PreventExtensions();
    }

    /// <summary>Locate a private member's descriptor for a mangled name: a
    /// private field lives as an OWN property on the instance (defined via
    /// <see cref="Opcode.DefinePrivateField"/>); a private method/accessor lives
    /// on the class prototype (or the constructor, for statics). Returns the
    /// descriptor and whether it was found as an own property of
    /// <paramref name="obj"/> (i.e. a field, which the caller may write
    /// directly).</summary>
    private static (PropertyDescriptor? Desc, bool Own) FindPrivateDescriptor(JsObject obj, string name)
    {
        var own = obj.GetOwnPropertyDescriptor(name);
        if (own is not null) return (own, true);
        for (var o = obj.Prototype; o is not null; o = o.Prototype)
        {
            var d = o.GetOwnPropertyDescriptor(name);
            if (d is not null) return (d, false);
        }
        return (null, false);
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

    /// <summary>wp:M3-26 — install an object-literal accessor (getter/setter).
    /// Mirrors <see cref="InstallMethodOrAccessor(JsObject, JsPropertyKey, Starling.Js.Bytecode.ClassMethodKind, JsFunction)"/>
    /// but marks the descriptor <em>enumerable</em> per §13.2.5 (object-literal
    /// accessors are enumerable; class accessors are not). Merges an existing
    /// accessor's complementary half so a paired <c>get x()/set x()</c> on the
    /// same key shares one descriptor, and stamps the §13.2.5.5 "name"
    /// ("get x"/"set x").</summary>
    private static void InstallObjectAccessor(JsObject owner, JsPropertyKey key, bool isGetter, JsFunction fn)
    {
        StampMethodName(fn, key, isGetter
            ? Starling.Js.Bytecode.ClassMethodKind.Get
            : Starling.Js.Bytecode.ClassMethodKind.Set);
        var existing = owner.GetOwnPropertyDescriptor(key);
        var prevAccessor = existing is { IsAccessor: true } d ? d : (PropertyDescriptor?)null;
        var getter = isGetter ? fn : prevAccessor?.Getter;
        var setter = isGetter ? prevAccessor?.Setter : fn;
        owner.DefineOwnProperty(key,
            PropertyDescriptor.Accessor(getter, setter, enumerable: true, configurable: true));
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
        // §15.5.2 EvaluateGeneratorBody — FunctionDeclarationInstantiation (the
        // parameter-binding prologue) runs synchronously here, BEFORE the
        // generator object is returned. RunPrologue drives the worker to the
        // PrologueEnd marker; a throw from param destructuring / defaults /
        // RequireObjectCoercible / iterator protocol propagates to the caller
        // now (no generator object is produced).
        if (fn.Body.HasPrologue) RunPrologue(frame);
        return JsValue.Object(gen);
    }

    /// <summary>§10.2.1.3 — drive the worker through the synchronous
    /// parameter-binding prologue. Resumes once (which runs everything up to the
    /// body's <see cref="Opcode.PrologueEnd"/> marker). If the prologue threw,
    /// re-raises it on the calling thread; if it ran to completion (an
    /// empty/return-only body with no marker — defensive) the throw still
    /// surfaces. On success the worker is parked at PrologueEnd, ready for the
    /// first real resume.</summary>
    private static void RunPrologue(SuspendedFrame frame)
    {
        frame.Resume(JsValue.Undefined);
        if (frame.Completed && frame.ThrewUncaught)
            throw new JsThrow(frame.ReturnValue);
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

        // §27.7.5.2 AsyncFunctionStart — FunctionDeclarationInstantiation (the
        // parameter-binding prologue) runs synchronously at call time. Per
        // §27.7.5.1, an abrupt completion from the prologue REJECTS the returned
        // promise (it is NOT thrown synchronously — the function still returns a
        // promise). RunPrologueAsync runs the worker to the PrologueEnd marker;
        // if it threw, reject `outer` and skip driving the body. Synthetic async
        // bodies without a marker (top-level-await module wrappers) skip this.
        if (fn.Body.HasPrologue && RunPrologueAsync(state))
            return JsValue.Object(outer);

        // Drive the worker synchronously from the calling thread, riding
        // each await suspension via the microtask queue. The first Resume
        // kicks off the body; subsequent Resumes are wired by the await
        // handler below.
        DriveAsync(state);
        return JsValue.Object(outer);
    }

    /// <summary>§27.7.5.1 — run the async body's parameter-binding prologue
    /// synchronously. Returns true if the prologue threw (in which case the
    /// outer promise has been rejected and the body must not be driven); false
    /// when the worker parked cleanly at <see cref="Opcode.PrologueEnd"/>.</summary>
    private bool RunPrologueAsync(JsAsyncFunctionState state)
    {
        var frame = state.Frame;
        frame.Resume(JsValue.Undefined);
        if (frame.Completed && frame.ThrewUncaught)
        {
            state.Settled = true;
            PromiseCtor.Reject(_runtime.Realm, state.OuterPromise, frame.ReturnValue);
            return true;
        }
        return false;
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
        // §27.4 EvaluateAsyncGeneratorBody — like sync generators, the parameter-
        // binding prologue runs synchronously at call time and a throw propagates
        // to the caller before the async-generator object is produced.
        if (fn.Body.HasPrologue) RunPrologue(frame);
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
    /// <summary>wp:M3-15 — a <c>break</c>/<c>continue</c> exiting a loop or
    /// switch across this finalizer. <see cref="TryFrame.PendingTargetPc"/>
    /// holds the loop's break/continue PC and
    /// <see cref="TryFrame.PendingUnwindRemaining"/> the number of further
    /// try-frames to unwind before reaching it.</summary>
    Break,
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
    /// <summary>wp:M3-15 — for a pending <see cref="PendingCompletion.Break"/>,
    /// the absolute PC of the loop/switch break/continue site to jump to once
    /// every intervening finalizer has run.</summary>
    public int PendingTargetPc;
    /// <summary>wp:M3-15 — for a pending <see cref="PendingCompletion.Break"/>,
    /// the number of additional enclosing try-frames still to unwind (and run
    /// finalizers for) before reaching the target.</summary>
    public int PendingUnwindRemaining;
}
