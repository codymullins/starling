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
    LoadTrue,       // → push true
    LoadFalse,      // → push false
    LoadNull,       // → push null
    LoadUndefined,  // → push undefined
    LoadZero,       // → push 0 (peephole optimization)

    // ----- Locals -----
    LoadLocal,      // [u8 slot] → push local at slot
    StoreLocal,     // [u8 slot] → pop value, store to local slot
    DeclareLocal,   // [u8 slot] → make slot live (initialized to undefined)

    // ----- Globals (for free identifiers / Test262 host bindings) -----
    LoadGlobal,     // [u16 nameIdx] → push global by name
    StoreGlobal,    // [u16 nameIdx] → pop value, store global

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
    Halt,           // end-of-program sentinel
}
