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
    MakeClosure,    // [u16 fnIdx][u16 nUpvalues] pop N values, build closure
    LoadUpvalue,    // [u16 idx] → push current frame's upvalue at idx
    LoadThis,       // → push the current frame's `this` binding
    NewObject,      // → push a fresh empty JsObject
    NewArray,       // → push a fresh empty JsArray (B2-4)
    LoadRegExp,     // [u16 srcIdx][u16 flagsIdx] → push a fresh JsRegExp built from constant-pool source/flags strings
    TemplateObject, // [u16 idx] → push the frozen, per-site-cached template strings array (cooked + .raw) for a tagged template
    LoadTrue,       // → push true
    LoadFalse,      // → push false
    LoadNull,       // → push null
    LoadUndefined,  // → push undefined
    LoadZero,       // → push 0 (peephole optimization)

    // ----- Locals -----
    LoadLocal,      // [u16 slot] → push local at slot
    StoreLocal,     // [u16 slot] → pop value, store to local slot
    DeclareLocal,   // [u16 slot] → make slot live (initialized to undefined)

    // ----- Lexical bindings / Temporal Dead Zone (let/const/class) -----
    /// <summary>[u16 slot] — instantiate a <c>let</c>/<c>const</c>/<c>class</c>
    /// binding in the uninitialized ("TDZ") state: store the realm's
    /// <see cref="Starling.Js.Runtime.JsRealm.TdzSentinel"/> in the slot. Emitted
    /// at scope entry in place of <see cref="DeclareLocal"/> for a non-captured
    /// lexical binding. A read or write before the initializer runs throws
    /// ReferenceError (§§9.1.1.1.4 / 13.3.1.1).</summary>
    DeclareLocalTdz,
    /// <summary>[u16 slot] — like <see cref="InitCellLocal"/> but the fresh
    /// <see cref="Starling.Js.Runtime.Cell"/> holds the TDZ sentinel rather than
    /// undefined. Emitted for a captured lexical binding at scope entry.</summary>
    InitCellLocalTdz,
    /// <summary>[u16 slot] — like <see cref="LoadLocal"/> but throws
    /// ReferenceError when the slot still holds the TDZ sentinel. Emitted for
    /// reads of a non-captured lexical binding.</summary>
    LoadLocalChecked,
    /// <summary>[u16 slot] — like <see cref="LoadCellLocal"/> but throws
    /// ReferenceError when the cell still holds the TDZ sentinel. Emitted for
    /// reads of a captured lexical binding owned by this function.</summary>
    LoadCellLocalChecked,
    /// <summary>[u16 slot] — like <see cref="StoreCellLocal"/> but throws
    /// ReferenceError when the cell still holds the TDZ sentinel (write before
    /// initialization to a lexical binding). Emitted for assignments (not the
    /// declaration's own initializer) to a captured lexical binding.</summary>
    StoreCellLocalChecked,
    /// <summary>[u16 idx] — like <see cref="LoadUpvalue"/> but throws
    /// ReferenceError when the captured cell still holds the TDZ sentinel.
    /// Emitted for reads of an outer-scope lexical binding from a nested
    /// function.</summary>
    LoadUpvalueChecked,
    /// <summary>[u16 idx] — like <see cref="StoreUpvalue"/> but throws
    /// ReferenceError when the captured cell still holds the TDZ sentinel
    /// (write before initialization through a closure).</summary>
    StoreUpvalueChecked,

    // ----- Captured locals (gap:closure-write-back / wp:M3-04c2) -----
    /// <summary>[u16 slot] — allocate a fresh <c>Cell { Value = Undefined }</c>
    /// and store it as the slot's value. Emitted in place of
    /// <see cref="DeclareLocal"/> for any local whose binding is referenced
    /// by a nested function.</summary>
    InitCellLocal,
    /// <summary>[u16 slot] — read the cell stored in the slot, push
    /// <c>cell.Value</c>. Replaces <see cref="LoadLocal"/> for captured
    /// reads in the function that owns the binding.</summary>
    LoadCellLocal,
    /// <summary>[u16 slot] — pop value, read the cell stored in the slot,
    /// set <c>cell.Value = value</c>. Replaces <see cref="StoreLocal"/> for
    /// captured writes in the function that owns the binding.</summary>
    StoreCellLocal,
    /// <summary>[u16 slot] — read the current slot value, wrap it in a fresh
    /// <see cref="Starling.Js.Runtime.Cell"/>, and store the cell back in
    /// the slot. Emitted at function entry for captured parameters whose
    /// values land in the slot by the VM's argument-copy step.</summary>
    PromoteParamCell,
    /// <summary>[u16 idx] — pop value, set <c>cell.Value = value</c> on the
    /// upvalue at <c>idx</c>. Used by inner functions to
    /// write back to a captured outer binding.</summary>
    StoreUpvalue,
    /// <summary>[u16 idx] — push the upvalue cell itself (as a JsValue) so
    /// it can be re-captured by a further-nested closure or class. The
    /// regular <see cref="LoadUpvalue"/> dereferences through the cell;
    /// this opcode hands the cell off intact.</summary>
    LoadUpvalueCell,
    /// <summary>[u16 slot] — §14.7.4.4 CreatePerIterationEnvironment. Read
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
    /// <summary>[u16 nameIdx] — like <see cref="LoadGlobal"/> but throws a
    /// ReferenceError when the global binding is unresolvable (the global
    /// object has no such property anywhere on its prototype chain), per
    /// §6.2.5.5 GetValue on an unresolvable Reference. Plain
    /// <see cref="LoadGlobal"/> returns <c>undefined</c> for missing globals
    /// (so host-binding probes / <c>typeof</c> don't throw); this checked
    /// variant is emitted where the spec requires GetValue to fire its
    /// ReferenceError — currently computed property keys whose key expression
    /// is a bare free identifier (e.g. <c>class { [unresolved]() {} }</c>),
    /// so the key's evaluation aborts the whole class/object definition.</summary>
    LoadGlobalChecked, // [u16 nameIdx] → push global by name, throw ReferenceError if absent
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

    /// <summary>wp:M3-71 — §19.2.1.1 PerformEval (direct path). Emitted only
    /// when the callee is a bare <c>eval</c> IdentifierReference that resolves
    /// to NO local/upvalue (i.e. the realm <c>%eval%</c> intrinsic). Operands are
    /// [u16 descriptorIdx][u8 argc]; the stack holds <c>[eval, arg0..argN]</c>.
    /// The VM checks the callee is still the realm eval intrinsic at runtime (a
    /// reassigned global <c>eval</c> falls back to an ordinary indirect call)
    /// and, if so, parses + compiles the source inheriting the CURRENT frame's
    /// lexical context (strict / <c>[[HomeObject]]</c> for <c>super</c> /
    /// <c>new.target</c> / <c>this</c> / derived-ctor-ness), then runs it on the
    /// same frame's function so <c>super.x</c> and <c>new.target</c> resolve.
    /// wp:M3-72 — <c>descriptorIdx</c> points at the <see cref="EvalScopeDescriptor"/>
    /// of the calling function's variable environment; the VM pairs it with the
    /// live frame (locals / upvalues) to build the caller scope the eval'd code
    /// reads/writes, and runs the §19.2.1.3 EvalDeclarationInstantiation
    /// early-error checks. A non-string argument is returned unchanged.</summary>
    DirectEval,     // [u16 descriptorIdx][u8 argc] eval-intrinsic + args on stack
    /// <summary>wp:M3-72 — [u16 nameIdx] — caller-scope-aware identifier load.
    /// Emitted only when compiling direct-eval source for a free identifier that
    /// matches one of the caller's in-scope binding names. Consults the frame's
    /// caller scope; on a hit pushes the caller binding's current value
    /// (throwing ReferenceError for a TDZ-uninitialized lexical caller binding);
    /// on a miss falls back to a checked global load.</summary>
    LoadEvalScope,  // [u16 nameIdx]
    /// <summary>wp:M3-72 — [u16 nameIdx] — caller-scope-aware identifier store.
    /// Pops the value; if the frame's caller scope has the name, writes through
    /// the caller binding's live storage; otherwise falls back to a global
    /// store.</summary>
    StoreEvalScope, // [u16 nameIdx]
    /// <summary>wp:M3-73 — [u16 nameIdx] — §19.2.1.3 EvalDeclarationInstantiation
    /// (non-global branch). Idempotent var/function pre-declaration into the
    /// caller frame's eval-introduced var store: create a binding (value
    /// <c>undefined</c>) for the name if one does not already exist, else no
    /// effect. Emitted only when compiling a NON-strict direct eval whose caller
    /// is a function, for the eval body's OWN top-level var/function names that
    /// are NOT already caller bindings.</summary>
    DeclareEvalVar, // [u16 nameIdx]
    /// <summary>wp:M3-73 — [u16 nameIdx] — pop the value and set the named
    /// binding in the caller frame's eval-introduced var store (the binding was
    /// created by <see cref="DeclareEvalVar"/>). Used for an eval-body var
    /// initializer and a hoisted function declaration's function object.</summary>
    StoreEvalVar,   // [u16 nameIdx]
    /// <summary>wp:M3-73 — [u16 nameIdx] — push true after removing the named
    /// binding from this frame's eval-introduced var store if present (an
    /// eval-introduced var/function binding is configurable, §19.2.1.3, so
    /// <c>delete</c> succeeds and removes it). When the name is not in the store
    /// this is the ordinary sloppy-mode identifier <c>delete</c> no-op (pushes
    /// true). Emitted for <c>delete name</c> in code that may carry such a
    /// store.</summary>
    DeleteEvalVar,  // [u16 nameIdx]

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
    /// <summary>[u16 nameConst] — pop the value being assigned, then throw a
    /// TypeError "Assignment to constant variable." Emitted for an assignment to
    /// an immutable binding (a module's imported binding, §16.2.1.6.2 — imported
    /// bindings are immutable and an assignment is a runtime TypeError). The
    /// operand names the binding for the message.</summary>
    ThrowConstAssignment,
    RequireObjectCoercible, // peek top-of-stack; throw a TypeError if it is null/undefined (§7.2.1). Used before object-destructuring property access.
    SetFunctionName, // [u16 nameConst] peek top-of-stack; if it is an anonymous function/class (name===""), set its `name` own property to the constant string. §named-evaluation.
    SpreadInto,     // pop src and dst objects, copy own enumerable props from src onto dst (object-literal spread)
    RestArray,      // [u16 start] pop src, push Array-like object with src[start..length)
    RestObject,     // [u16 excludedCount] pop N keys + src, push own-enumerable copy excluding keys

    // ----- Iterator protocol (B3-2) -----
    GetIterator,    // pop value, push an opaque iterator-record handle (a JsObject internal)
    IteratorStep,   // peek iterator-record; push iterator-result object, or push undefined on done. Sets the "done" slot when finished.
    IteratorClose,  // pop iterator-record; invoke .return() if present
    IteratorBindNext, // peek iterator-record; if already Done push undefined, else IteratorStep and push result.value (undefined on done, sets Done). §8.5.3 array-pattern element step.
    IteratorRest,   // peek iterator-record; collect remaining values into a fresh JsArray until Done, push the array. §8.5.3 BindingRestElement.
    IteratorCloseForThrow, // pop iterator-record; invoke .return() if present (swallowing any return()-error so the in-flight throw wins, §7.4.10).
    IteratorCloseFinally,  // pop iterator-record; close iff the enclosing try-frame's pending completion is abrupt (skip on Normal). Swallows return()-errors only for a pending Throw. Used by the for-of synthetic finally (§7.4.8 / §14.7.5.6).
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
    /// <summary>[u16 mangledNameIdx] — §13.10 ergonomic brand check
    /// (<c>#x in obj</c>): pop the operand; push <c>true</c> when it is an
    /// object carrying the named private element, else <c>false</c>. Never
    /// throws for a non-object operand (unlike PrivateGet).</summary>
    PrivateIn,
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

    /// <summary>§10.2.1.3 / §15.5.2 / §27 — marks the end of the
    /// parameter-binding prologue (FunctionDeclarationInstantiation) of a
    /// generator / async / async-generator body. The worker thread executes the
    /// prologue synchronously at call time and then hands off here, so a throw
    /// from parameter destructuring / defaults / RequireObjectCoercible /
    /// iterator-protocol surfaces to the caller BEFORE the generator object /
    /// promise is produced. Resuming past this point continues the body lazily.
    /// No operands; no stack effect. Only emitted for non-Normal function kinds
    /// and only meaningful when a suspension target is active.</summary>
    PrologueEnd,

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
    /// local slot named in the operand (u16). Emitted only at the top of a
    /// non-arrow function body that references the identifier <c>arguments</c>
    /// without binding it. Arrow functions never emit this; they inherit
    /// <c>arguments</c> lexically through the upvalue mechanism.</summary>
    MakeArguments,

    /// <summary>wp:M3-80 — §10.4.4.6 CreateMappedArgumentsObject: materialize the
    /// callee's <c>arguments</c> object as the <em>mapped</em> exotic form, where
    /// each index <c>i &lt; paramCount</c> is live-linked to the i-th formal
    /// parameter's local slot (writing <c>arguments[i]</c> updates the parameter
    /// and reassigning the parameter updates <c>arguments[i]</c>). Emitted instead
    /// of <see cref="MakeArguments"/> only for a NON-strict function with a simple
    /// parameter list (no defaults / rest / destructuring). Operand layout:
    /// <c>[u16 argsLocalSlot][u16 paramCount]</c> followed by <c>paramCount</c>
    /// <c>[u16 paramSlot]</c> entries (a paramSlot of 0xFFFF marks an index whose
    /// parameter name is shadowed by a later duplicate and so is left unmapped per
    /// the spec's last-wins mapping). The VM reads the parameter slots from the
    /// current frame's <c>locals</c>, holding the array by reference so the
    /// mapping stays live after the frame returns.</summary>
    MakeMappedArguments,

    /// <summary>wp:M3-21 — §15.2.5 InstantiateOrdinaryFunctionExpression: bind a
    /// named function <em>expression</em>'s own name inside its body to the
    /// function instance currently executing, so the body can refer to itself
    /// (recursion / self-reference) regardless of any outer binding. The operand
    /// (u16) is the local slot reserved for the name. Emitted only at the top of a
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

    // ----- with statement (§14.11 / §9.1.1.2 object Environment Records) -----
    /// <summary>§14.11.2 — pop a value, ToObject it, and push the resulting
    /// object as a new object Environment Record onto the running frame's
    /// with-stack. Unqualified name lookups in the body consult this object
    /// (respecting <c>@@unscopables</c>) before the lexical scope.</summary>
    PushWith,
    /// <summary>Pop the innermost object Environment Record off the frame's
    /// with-stack (normal completion of a <c>with</c> body). Abrupt completions
    /// are unwound by the try-frame mechanism the compiler wraps the body in.</summary>
    PopWith,
    /// <summary>[u16 nameIdx][i16 missOffset] — with-aware identifier load.
    /// Walk the frame's with-stack innermost-first; if an object Environment
    /// Record HasBinding(name) (HasProperty AND not blocked by
    /// <c>@@unscopables</c>), push its value and jump by missOffset. Otherwise
    /// fall through to the statically-compiled fallback load.</summary>
    WithLoadOrMiss,
    /// <summary>[u16 nameIdx][i16 missOffset] — with-aware method load for a
    /// call. Like <see cref="WithLoadOrMiss"/> but on a hit pushes
    /// [withObject, value] (so the call binds <c>this</c> to the binding
    /// object, §9.1.1.2 WithBaseObject) and jumps. On a miss falls through;
    /// the fallback pushes [undefined, value] for a normal call.</summary>
    WithLoadMethodOrMiss,
    /// <summary>[u16 nameIdx][i16 missOffset] — with-aware identifier store.
    /// Stack on entry: [value]. If an object Environment Record has the
    /// binding, pop value, Set it on that object, and jump by missOffset.
    /// Otherwise leave value on the stack and fall through to the static store.</summary>
    WithStoreOrMiss,
    /// <summary>[u16 nameIdx][i16 missOffset] — with-aware <c>delete name</c>.
    /// If an object Environment Record has the binding, delete the property,
    /// push the boolean result, and jump. Otherwise fall through to the static
    /// fallback (push true).</summary>
    WithDeleteOrMiss,

    /// <summary>§13.15.2 — with-aware read half of a compound assignment
    /// (<c>x op= y</c> inside a <c>with</c> body). Operand layout
    /// <c>[u16 nameIdx][u16 baseSlot][i32 missOffset]</c>. Resolves the LHS
    /// Reference's base EXACTLY ONCE: walk the frame's with-stack
    /// innermost-first; if an object Environment Record HasBinding(name) it is
    /// the Reference base. On a hit, stash that base object into
    /// <c>locals[baseSlot]</c>, push <c>Get(base, name)</c> (the current value),
    /// and jump by missOffset (past the statically-compiled fallback load). On a
    /// miss, stash <c>undefined</c> into <c>locals[baseSlot]</c> (marking "no
    /// with-base — use the static binding") and fall through to the fallback
    /// load. The captured base is reused by <see cref="WithCompoundStore"/> so
    /// the write lands on the SAME object even if the getter run during the read
    /// deleted the binding.</summary>
    WithCompoundLoad,
    /// <summary>§13.15.2 — with-aware write half of a compound assignment, paired
    /// with <see cref="WithCompoundLoad"/>. Operand layout
    /// <c>[u16 nameIdx][u16 baseSlot][i32 missOffset]</c>. Stack on entry holds
    /// the new value (a Dup'd copy on top, with the result copy beneath). Reads
    /// the base captured in <c>locals[baseSlot]</c>: if it is an object, pop the
    /// top value, <c>Set(base, name, value)</c> on that SAME object (the once-
    /// resolved Reference base, §13.15.2 PutValue(lref, v)), and jump by
    /// missOffset (past the static store fallback). If it is <c>undefined</c> (no
    /// with-base), fall through to the static store, which consumes the top
    /// value. Either way one value (the result) remains on the stack.</summary>
    WithCompoundStore,

    /// <summary>§13.15.2 / §13.3.3 — resolve a computed member's property key
    /// ONCE for a compound assignment <c>base[key] op= v</c>. Stack on entry:
    /// <c>[base, rawKey]</c>. Pops <c>rawKey</c>, then (in spec order) requires
    /// <c>base</c> to be coercible — a <c>null</c>/<c>undefined</c> base throws a
    /// TypeError BEFORE the key is coerced — and finally runs §7.1.19
    /// ToPropertyKey on <c>rawKey</c> (which may invoke a user <c>toString</c> /
    /// <c>@@toPrimitive</c> exactly once) and pushes the resolved key as a
    /// String/Symbol value: <c>[base, key]</c>. The following Dup2 + LoadComputed
    /// + StoreComputed then re-run ToPropertyKey on this already-primitive key,
    /// which is side-effect-free, so the user key coercion happens just once
    /// across the read and the write (§13.3.3 evaluates the key reference once).</summary>
    ResolveComputedKey,

    /// <summary>wp:M3-64 — §13.2.5 MakeMethod for object-literal methods. Stack
    /// on entry: [obj, fn]. Stamps <c>fn.[[HomeObject]] = obj</c> so a
    /// <c>super.x</c> inside the concise method / getter / setter resolves
    /// against <c>Object.getPrototypeOf(obj)</c>. Leaves the stack unchanged
    /// ([obj, fn]) for the following Define{Data,Getter,Setter}* opcode.
    /// No-op when the value on top is not a JsFunction (e.g. a bound/native).</summary>
    SetHomeObject,

    /// <summary>wp:M3-64 — like <see cref="SetHomeObject"/> but for a computed
    /// key still on the stack. Stack on entry: [obj, key, fn]. Stamps
    /// <c>fn.[[HomeObject]] = obj</c> and leaves the stack unchanged so the
    /// following DefineGetterComputed / DefineSetterComputed / DefineDataComputed
    /// can consume [obj, key, fn].</summary>
    SetHomeObjectComputed,

    Halt,           // end-of-program sentinel
}
