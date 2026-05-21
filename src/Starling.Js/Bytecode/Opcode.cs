namespace Starling.Js.Bytecode;

/// <summary>
/// Starling JS bytecode opcodes. Stack-based VM model (operands flow through
/// an evaluation stack). Each opcode is followed by 0–2 immediate operand
/// bytes encoding constant-pool indexes, local-slot indexes, or jump
/// offsets.
/// </summary>
/// <remarks>
/// Naming + layout patterned on V8 Ignition + Wren — close enough to
/// existing literature that contributors can cross-reference without
/// re-deriving choices. Multi-byte operands are little-endian.
/// </remarks>
public enum Opcode : byte
{
    // ----- Constants + literals -----
    Nop,
    LoadConst,      // [u16 idx] → push constant pool entry
    LoadFunction,   // [u16 idx] → push JsFunction template (no upvalues) from pool
    MakeClosure,    // [u16 fnIdx][u8 nUpvalues] pop N values, build closure
    LoadUpvalue,    // [u8 idx] → push current frame's upvalue at idx
    LoadThis,       // → push the current frame's `this` binding
    NewObject,      // → push a fresh empty JsObject
    NewArray,       // → push a fresh empty JsArray (B2-4)
    LoadRegExp,     // [u16 srcIdx][u16 flagsIdx] → push a fresh JsRegExp built from constant-pool source/flags strings
    LoadTrue,       // → push true
    LoadFalse,      // → push false
    LoadNull,       // → push null
    LoadUndefined,  // → push undefined
    LoadZero,       // → push 0 (peephole optimization)

    // ----- Locals -----
    LoadLocal,      // [u8 slot] → push local at slot
    StoreLocal,     // [u8 slot] → pop value, store to local slot
    DeclareLocal,   // [u8 slot] → make slot live (initialized to undefined)

    // ----- Captured locals (gap:closure-write-back / wp:M3-04c2) -----
    /// <summary>[u8 slot] — allocate a fresh <c>Cell { Value = Undefined }</c>
    /// and store it as the slot's value. Emitted in place of
    /// <see cref="DeclareLocal"/> for any local whose binding is referenced
    /// by a nested function.</summary>
    InitCellLocal,
    /// <summary>[u8 slot] — read the cell stored in the slot, push
    /// <c>cell.Value</c>. Replaces <see cref="LoadLocal"/> for captured
    /// reads in the function that owns the binding.</summary>
    LoadCellLocal,
    /// <summary>[u8 slot] — pop value, read the cell stored in the slot,
    /// set <c>cell.Value = value</c>. Replaces <see cref="StoreLocal"/> for
    /// captured writes in the function that owns the binding.</summary>
    StoreCellLocal,
    /// <summary>[u8 slot] — read the current slot value, wrap it in a fresh
    /// <see cref="Starling.Js.Runtime.Cell"/>, and store the cell back in
    /// the slot. Emitted at function entry for captured parameters whose
    /// values land in the slot by the VM's argument-copy step.</summary>
    PromoteParamCell,
    /// <summary>[u8 idx] — pop value, set <c>cell.Value = value</c> on the
    /// upvalue at <c>idx</c>. Used by inner functions to
    /// write back to a captured outer binding.</summary>
    StoreUpvalue,
    /// <summary>[u8 idx] — push the upvalue cell itself (as a JsValue) so
    /// it can be re-captured by a further-nested closure or class. The
    /// regular <see cref="LoadUpvalue"/> dereferences through the cell;
    /// this opcode hands the cell off intact.</summary>
    LoadUpvalueCell,
    /// <summary>[u8 slot] — §14.7.4.4 CreatePerIterationEnvironment. Read
    /// the current <see cref="Starling.Js.Runtime.Cell"/> stored in
    /// <c>slot</c>, snapshot its <c>Value</c>, allocate a fresh Cell with
    /// the same value, and write the fresh cell back into the slot. Emitted
    /// at the top of each iteration of <c>for (let|const x = ...; ...; ...)</c>
    /// (and the corresponding for-in / for-of forms) so closures created
    /// inside the iteration body capture an iteration-specific binding
    /// rather than sharing a single slot across iterations.</summary>
    RefreshLetBinding,

    // ----- Globals (for free identifiers / Test262 host bindings) -----
    LoadGlobal,     // [u16 nameIdx] → push global by name
    StoreGlobal,    // [u16 nameIdx] → pop value, store global
    /// <summary>[u16 nameIdx] — gap:script-top-var-not-global. Idempotent
    /// CreateGlobalVarBinding (§16.1.7 / §9.1.1.4.16): if the global object
    /// already has an own property with this name, do nothing; otherwise
    /// install a fresh own data property initialized to <c>undefined</c>
    /// with <c>writable=true</c>, <c>enumerable=true</c>, and
    /// <c>configurable=false</c> (per spec for <c>var</c> declarations in
    /// non-eval Script code). Emitted by the compiler at every script-top
    /// <c>var</c> declarator so the binding exists as a global property
    /// before its initializer runs, and so a later redeclaration without
    /// an initializer doesn't reset its value.</summary>
    DeclareGlobalVar,

    // ----- Stack manipulation -----
    Pop,
    Dup,
    Swap,

    // ----- Arithmetic -----
    Add, Sub, Mul, Div, Mod, Pow,
    Neg, UnaryPlus,

    // ----- Bitwise -----
    BitOr, BitAnd, BitXor, BitNot, Shl, Shr, Ushr,

    // ----- Logical (return boolean) -----
    Not,
    LogAnd,         // [u16 endOffset] short-circuit jump
    LogOr,
    Coalesce,

    // ----- Comparison -----
    Eq, NEq, StrictEq, StrictNEq,
    Lt, LtEq, Gt, GtEq,

    // ----- Property access -----
    LoadProperty,   // [u16 nameIdx] — obj.name
    StoreProperty,  // [u16 nameIdx]
    LoadComputed,   // obj[key] (key + obj on stack)
    StoreComputed,
    // wp:M3-26 — object-literal accessor (getter/setter) shorthand (§13.2.5).
    // Stack: [obj, fn] → [obj]. Defines an enumerable accessor descriptor,
    // merging an existing accessor's complementary half (paired get/set).
    DefineGetter,         // [u16 nameIdx] — { get name(){…} }
    DefineSetter,         // [u16 nameIdx] — { set name(v){…} }
    DefineGetterComputed, // stack: [obj, key, fn] → [obj] — { get [k](){…} }
    DefineSetterComputed, // stack: [obj, key, fn] → [obj] — { set [k](v){…} }
    // wp:M3-26 — object-literal data property via CreateDataPropertyOrThrow
    // (§13.2.5.5): define an own enumerable/writable/configurable data prop,
    // OVERRIDING any existing accessor (vs StoreProperty's [[Set]], which would
    // invoke an inherited/own setter). Stack: [obj, value] → [obj].
    DefineDataProperty,   // [u16 nameIdx]
    DefineDataComputed,   // stack: [obj, key, value] → [obj]

    // ----- Calls -----
    Call,           // [u8 argc] callee + args on stack; this=Undefined
    CallMethod,     // [u8 argc] receiver + callee + args on stack; this=receiver
    New,            // [u8 argc]
    Return,         // pop and return
    ReturnUndefined,

    // ----- Control flow -----
    Jump,           // [u16 offset]
    JumpIfTrue,
    JumpIfFalse,
    JumpIfNotNullish,

    // ----- Misc -----
    TypeOf,
    Throw,
    SpreadInto,     // pop src and dst objects, copy own enumerable props from src onto dst (object-literal spread)
    RestArray,      // [u16 start] pop src, push Array-like object with src[start..length)
    RestObject,     // [u16 excludedCount] pop N keys + src, push own-enumerable copy excluding keys

    // ----- Iterator protocol (B3-2) -----
    GetIterator,    // pop value, push an opaque iterator-record handle (a JsObject internal)
    IteratorStep,   // peek iterator-record; push iterator-result object, or push undefined on done. Sets the "done" slot when finished.
    IteratorClose,  // pop iterator-record; invoke .return() if present
    SpreadIterable, // pop iterable + peek target JsArray; append all iterable's values to target via @@iterator

    // ----- Apply-style calls (B3-2: call with materialized args array) -----
    CallApply,        // pop args-array + callee; this=Undefined
    CallApplyMethod,  // pop args-array + callee + receiver; this=receiver
    NewApply,         // pop args-array + ctor

    // ----- Classes (B1b-2a) -----
    /// <summary>Push the current frame's [[HomeObject]] (the prototype that
    /// owns the method currently executing). Used by <c>super.x</c> and
    /// <c>super.x()</c> to compute the base prototype. Throws TypeError when
    /// the current function has no home object (not a class method).</summary>
    LoadHomeObject,
    /// <summary>Push the current frame's [[NewTarget]] — the constructor
    /// invoked by <c>new</c>, threaded through derived <c>super(...)</c>
    /// calls per §10.2.1.</summary>
    LoadNewTarget,
    /// <summary>Pop a value and bind it as the current frame's <c>this</c>.
    /// Used by derived constructors after <c>super(...)</c> returns. Subsequent
    /// <c>LoadThis</c> instructions in the same frame see the new binding.</summary>
    BindThis,
    /// <summary>Like <see cref="LoadThis"/> but throws ReferenceError when
    /// <c>this</c> is uninitialized (i.e. inside a derived constructor before
    /// <c>super(...)</c> has been called). The compiler emits this for any
    /// <c>this</c> access inside a class body.</summary>
    LoadThisChecked,
    /// <summary>Pop an args-array. Invoke the parent constructor (via
    /// [[HomeObject]].[[Prototype]].constructor) with the current
    /// [[NewTarget]] and push the constructed object — the caller is
    /// expected to immediately <see cref="BindThis"/>.</summary>
    CallSuperCtor,
    /// <summary>[u16 nameIdx] — push the property looked up on
    /// <c>[[HomeObject]].[[Prototype]]</c> with the current frame's
    /// <c>this</c> as the receiver for accessor getters. Lowering for
    /// <c>super.name</c> reads. The frame's <c>this</c> is consulted via
    /// the implicit <c>thisV</c> slot — no operand on the eval stack.</summary>
    LoadSuperProperty,
    /// <summary>[u16 nameIdx] — pop value, write to <c>this[name]</c>
    /// (per spec, <c>super.name = v</c> sets the property on the receiver,
    /// not the prototype). Pushes the assigned value back.</summary>
    StoreSuperProperty,
    /// <summary>[no operand] — pop key; push the property looked up on
    /// <c>[[HomeObject]].[[Prototype]]</c> with the current frame's
    /// <c>this</c> as the receiver for accessor getters. Lowering for
    /// <c>super[expr]</c> reads (wp:M3-04h). The key is run through
    /// <c>ToPropertyKey</c> at runtime (supports string and Symbol keys).
    /// Stack: [..., key] → [..., value].</summary>
    LoadSuperComputed,
    /// <summary>[no operand] — pop value, pop key; write to
    /// <c>this[ToPropertyKey(key)]</c> (per spec, <c>super[expr] = v</c> sets
    /// the property on the receiver <c>this</c>, not the prototype) — wp:M3-04h.
    /// Pushes the assigned value back. Stack: [..., key, value] → [..., value].</summary>
    StoreSuperComputed,
    /// <summary>[u16 mangledNameIdx] — pop receiver, push the value of the
    /// private slot. Throws TypeError if the receiver lacks the slot
    /// (handled in VM). The mangled name is a class-unique constant pool
    /// entry that identifies the slot.</summary>
    PrivateGet,
    /// <summary>[u16 mangledNameIdx] — pop receiver + value, write to the
    /// private slot on the receiver. Pushes the assigned value.</summary>
    PrivateSet,
    /// <summary>[u16 mangledNameIdx] — pop receiver + value, define a fresh
    /// private slot on the receiver. Throws TypeError if the slot already
    /// exists. Pushes nothing (consumes both operands).</summary>
    DefinePrivateField,
    /// <summary>[u16 templateIdx] — consult the <see cref="ClassTemplate"/>
    /// constant; if the template has <c>HasExtends</c>, pop the base-class
    /// value from the stack. Allocate the class constructor + prototype,
    /// install methods/static fields/static blocks, then push the
    /// constructor as a JsValue. Field initializers for instance fields
    /// are stamped onto the constructor's internal slot and run during
    /// later <c>new</c> invocations.</summary>
    BuildClass,
    /// <summary>Push a fresh <see cref="Starling.Js.Runtime.JsArray"/>
    /// containing every argument the current frame received (in argument
    /// order). Used to forward all arguments to <c>super(...args)</c> in
    /// a synthesized default derived constructor.</summary>
    LoadCallerArgs,
    /// <summary>Run each thunk in
    /// <c>currentFunction.InstanceFieldInitializers</c> with <c>this</c>
    /// bound to the current frame's <c>this</c>. Emitted by the compiler
    /// at the top of a base-class constructor body, and immediately after
    /// <see cref="BindThis"/> in a derived-class constructor body. No-op
    /// when the current function carries no field initializers.</summary>
    RunFieldInits,
    /// <summary>wp:M3-04f — §7.1.19 ToPropertyKey. Pop a value, coerce it to a
    /// property key (Symbols pass through; everything else stringifies via
    /// ToPrimitive("string")), and push the normalized key back as a JsValue
    /// (a Symbol value, or a String value). Emitted by the class compiler
    /// immediately after evaluating a computed member key (<c>[expr]</c>) so
    /// the ToPropertyKey coercion — and any observable side effects or throws
    /// it triggers — happen once, in source order, at class-definition time.</summary>
    ToPropertyKey,

    // ----- Exception handling (gap:try-catch) -----
    EnterTry,
    LeaveTry,
    EndFinally,
    /// <summary>wp:M3-15 — §14.15 abrupt <c>break</c>/<c>continue</c> that exits
    /// a loop or switch across one or more enclosing <c>try…finally</c> blocks.
    /// Operand layout <c>[u8 unwindCount][i16 target]</c>: <c>unwindCount</c> is
    /// the number of open try-frames between this instruction and the target
    /// loop/switch site that must be unwound (running each frame's finalizer,
    /// innermost first); <c>target</c> is the i16 forward offset (measured from
    /// the byte after the operand, identical to <see cref="Jump"/>) of the
    /// loop's break/continue PC. The VM diverts through the intervening
    /// finalizers as a Break completion carrying the target PC and remaining
    /// unwind count, then jumps to <c>target</c> once they have all run. A
    /// finalizer that itself performs an abrupt completion (break/continue/
    /// return/throw) overrides the pending Break per §14.15.3.</summary>
    BranchThroughFinally,

    // ----- Operator bundle (gap:instanceof / gap:in / gap:delete) -----
    /// <summary>Pop right, pop left, push <c>left instanceof right</c>.
    /// Implements §13.10.2 InstanceofOperator: consults
    /// <c>target[@@hasInstance]</c> when present, else walks the prototype
    /// chain for OrdinaryHasInstance.</summary>
    Instanceof,
    /// <summary>Pop right, pop key, push <c>key in right</c>. Implements
    /// §13.10.1 RelationalExpression: throws TypeError when the right-hand
    /// side is not an Object; otherwise returns
    /// <c>HasProperty(ToPropertyKey(key))</c>.</summary>
    In,
    /// <summary>Pop key, pop receiver, push <c>delete receiver[key]</c>.
    /// Implements §13.5.1 [[Delete]] dispatch. Receivers that are not
    /// objects (after spec-typing) return <c>true</c> with no effect.</summary>
    DeleteProperty,
    /// <summary>Duplicate the top two stack values (a, b → a, b, a, b).
    /// Used by compound assignment on member targets to keep the
    /// receiver/key live across the read.</summary>
    Dup2,

    // ----- for…in (B7-followup-b) -----
    /// <summary>Pop a value; if it's null or undefined, push an empty
    /// <see cref="Starling.Js.Runtime.JsArray"/>. Otherwise coerce to object
    /// and push a JsArray snapshot of enumerable string keys (own + inherited,
    /// dedup'd so shadowed names appear once with the most-derived shadowing
    /// rule, in insertion order per §14.7.5.10 ForIn/OfHeadEvaluation). The
    /// for…in loop iterates this snapshot, ignoring any later mutation per
    /// spec.</summary>
    EnumerateKeys,

    // ----- Generators / async (B1b-2c) -----
    /// <summary>[u8 kind] — Suspend the current frame. <c>kind</c>:
    /// 0 = yield (sync generator), 1 = await (async). Pops the yielded /
    /// awaited value, hands it to the suspension scheduler (the worker
    /// thread blocks until the caller resumes), then pushes the
    /// resume-value back onto the stack. For <c>await</c>, the
    /// resume-value is the resolved value (or, if the awaited promise
    /// rejected, a <c>JsThrow</c> is raised at this point).</summary>
    Suspend,

    /// <summary>§27.5.3.2 YieldDelegate (<c>yield* expr</c>). Pops an
    /// iterable, builds an iterator-record, and runs the full delegate
    /// protocol entirely inside the opcode handler: forwarding the outer
    /// generator's resume kind (next/return/throw) into the inner
    /// iterator's matching method on each round-trip. Exits only when the
    /// inner iterator signals <c>done: true</c>, at which point the
    /// inner's <c>value</c> is pushed onto the outer stack as the result
    /// of the <c>yield*</c> expression. Throws SyntaxError if invoked
    /// outside a generator context (no <see cref="Suspend"/>-style
    /// suspension target).</summary>
    YieldDelegate,

    /// <summary>wp:M3-04g — pop an iterable, resolve its async iterator via
    /// <c>[Symbol.asyncIterator]</c> (falling back to a sync iterator wrapped
    /// as async, §7.4.2 GetIterator(obj, async)), and push an opaque
    /// async-iterator-record handle. Used by <c>for await (… of …)</c>.</summary>
    GetAsyncIterator,

    /// <summary>wp:M3-04g — peek the async-iterator-record handle and call its
    /// <c>next()</c>; push the returned promise (to be <c>await</c>ed by a
    /// following <see cref="Suspend"/> kind=1). The awaited result object's
    /// <c>done</c>/<c>value</c> are then read by the loop.</summary>
    AsyncIteratorNext,

    /// <summary>wp:M3-04g — pop an async-iterator-record handle; if not already
    /// done, call its <c>return()</c>. Push the returned value (a promise or
    /// undefined) to be <c>await</c>ed for AsyncIteratorClose (§7.4.11).</summary>
    AsyncIteratorClose,

    // ----- Modules (wp:M3-03c — dynamic import + import.meta) -----
    /// <summary>wp:M3-03c — §13.3.10 ImportCall. Pop the specifier value,
    /// string-coerce it, and hand it to the active
    /// <see cref="Starling.Js.Modules.ModuleLoader"/> (reached via
    /// <c>Realm.ModuleLoader</c>) along with the running chunk's name as the
    /// referrer URL. Push the Promise the loader returns — it resolves to the
    /// imported module's namespace object once the subtree (including any
    /// top-level await) settles, and rejects on resolve/fetch/eval failure.
    /// Throws TypeError synchronously only when no loader is wired into the
    /// realm.</summary>
    DynamicImport,
    /// <summary>wp:M3-03c — §13.3.12 ImportMeta. Push the running module's
    /// host-populated <c>import.meta</c> object (lazily built, carries at least
    /// <c>url</c>). Resolved by looking the current chunk's name (the module URL)
    /// up in the active loader's registry. Throws SyntaxError when used outside
    /// module code (no matching module record).</summary>
    LoadImportMeta,

    /// <summary>wp:M3-20 — §10.4.4 CreateMappedArgumentsObject (Starling builds
    /// the simpler unmapped form, §10.4.4 / §10.4.4.6 CreateUnmappedArgumentsObject):
    /// materialize the current frame's <c>arguments</c> object — an
    /// array-like exotic object carrying every argument the callee received in
    /// index order plus a <c>length</c> own property — and store it into the
    /// local slot named in the operand (u8). Emitted only at the top of a
    /// non-arrow function body that references the identifier <c>arguments</c>
    /// without binding it. Arrow functions never emit this; they inherit
    /// <c>arguments</c> lexically through the upvalue mechanism.</summary>
    MakeArguments,

    /// <summary>wp:M3-21 — §15.2.5 InstantiateOrdinaryFunctionExpression: bind a
    /// named function <em>expression</em>'s own name inside its body to the
    /// function instance currently executing, so the body can refer to itself
    /// (recursion / self-reference) regardless of any outer binding. The operand
    /// (u8) is the local slot reserved for the name. Emitted only at the top of a
    /// NON-arrow function-expression body whose name is not shadowed by a param
    /// or body var/function declaration. The VM writes <c>currentFunction</c>
    /// (the callee) into the slot — through its Cell when a nested closure
    /// captures the name. Function <em>declarations</em> bind their name in the
    /// enclosing scope and never emit this; arrows never have an own name.</summary>
    BindCallee,

    /// <summary>§15.1.5 / §10.2.11 IteratorBindingInitialization for a function
    /// rest parameter (<c>function f(a, ...rest)</c>, <c>(...rest) =&gt; …</c>):
    /// gather every received argument from index <c>start</c> (u16 operand)
    /// onward into a fresh dense <see cref="Starling.Js.Runtime.JsArray"/> and
    /// push it. Reads the frame's received args directly — works in arrow
    /// functions, which have no <c>arguments</c> object. Emitted once at the
    /// rest parameter's binding site; the array is then bound like any other
    /// parameter target (identifier or destructuring pattern).</summary>
    RestParam,      // [u16 start] push Array of received args[start..argc)

    Halt,           // end-of-program sentinel
}
