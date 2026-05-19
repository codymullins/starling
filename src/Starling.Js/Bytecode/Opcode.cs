namespace Tessera.Js.Bytecode;

/// <summary>
/// Tessera JS bytecode opcodes. Stack-based VM model (operands flow through
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
    /// <see cref="Tessera.Js.Runtime.Cell"/>, and store the cell back in
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
    /// <summary>Push a fresh <see cref="Tessera.Js.Runtime.JsArray"/>
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

    // ----- Exception handling (gap:try-catch) -----
    EnterTry,
    LeaveTry,
    EndFinally,

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

    Halt,           // end-of-program sentinel
}
