# 09 — JavaScript Engine

## Scope

**In:** Pure-managed ECMAScript engine — lexer, parser, AST, bytecode IR, register VM, value model, property storage, intrinsics, GC strategy, realms, modules. ECMAScript 2024 (ES15) is the target spec level.
**Out:** JIT compilation (deferred to vNext; rely on .NET tiered JIT under the bytecode interpreter for v1 perf), full WebAssembly engine (M9+), `SharedArrayBuffer`+`Atomics` (worker-only, M8+), Tail calls (deferred).

## Goal posture

**Hand-write everything.** Reference implementations to read (not copy):
- `Acornima` (C# port of Acorn) — clean ESTree-style parser.
- `Jint` (C#) — interpreter, env records, intrinsics.
- `Boa` (Rust) — modern register VM design.
- `LibJS` (Ladybird) — spec-faithful bytecode VM.

These are read for **structure** and **algorithm**, not copied. Our shape is closer to LibJS: AST → bytecode → register VM.

## Alternative engine backend (Jint)

The browser can run on a second, runtime-selectable JS engine: **[Jint](https://github.com/sebastienros/jint)**,
a pure-managed C# ECMAScript interpreter. It is a **temporary compatibility
crutch** — it lets Starling render real-world pages at near-full ECMAScript
conformance *today* while the in-house `Starling.Js` engine climbs toward its
own conformance target. It is meant to be removed once `Starling.Js` is good
enough; the architecture is built so removal is a one-step deletion.

- **Why Jint fits the interop policy.** Jint is pure-managed (its only dependency
  is the managed Acornima parser; no `runtimes/` native assets, no P/Invoke), so
  it satisfies the managed-first rule exactly like BouncyCastle. Both new
  projects are in the CI interop-seam allowlist.
- **Selection.** Set `STARLING_JS_ENGINE=jint` (default `starling`). The selector
  (`Starling.Engine/JsEngineSelector.cs`) mirrors `PaintBackendSelector`: lazy,
  default-on-unset, loud-fail on an unknown value.
- **Conformance delta (measured on tc39/test262 `language`, identical corpus).**
  Jint ≈ **99.6%** vs `Starling.Js` ≈ **81%** — about a 19-point web-compat gap
  that the crutch closes while the in-house engine catches up. Run both numbers
  yourself via the `Conformance_pass_rate` / `Jint_conformance_pass_rate` tests
  in `tests/Starling.Js.Test262.Tests` (corpus fetched by `tools/fetch-test262.sh`).

### Architecture — the narrow seam

The engine-neutral shared asset is `Starling.Dom` (the real DOM); both engines
wrap *the same* Dom nodes, only the marshalling differs. So the abstraction lives
at the `Starling.Engine` ↔ JS boundary, **not** at the `JsValue`/`JsObject`
level — the ~956 existing `Starling.Bindings` call sites are untouched.

- `src/Starling.Js.Hosting` — the seam: `IScriptEngineFactory`, `IScriptSession`,
  `ScriptSessionOptions`, `ScriptThrow`, and the shared `ILayoutHost`. Depends
  only on Dom/Net/Common/Url; references neither engine.
- `src/Starling.Bindings` — hosts the default **Starling.Js** backend
  (`StarlingScriptSession`) over the existing `JsRuntime` path.
- `src/Starling.Bindings.Jint` — the **Jint** backend: `JintScriptSession` plus a
  full, idiomatic re-exposure of the Web-API surface over Jint interop
  (Node/Element/Document, EventTarget/Event, Window/Storage/History/Performance,
  timers/rAF + event-loop pump, fetch, XMLHttpRequest, observers/crypto/cookies,
  ES modules). References Jint + the seam + Dom/Net/Css/Html/Common/Url — **not**
  `Starling.Js` or `Starling.Bindings`.

`Starling.Engine` keeps all orchestration (script ordering, DOMContentLoaded/load
timing, the async pump) and talks only to `IScriptSession`. Both backends pass
the same engine integration suite (`tests/Starling.Engine.Tests`, 151/151) and
CI runs that suite under both engines.

The full design contract and the work-package history are in
[`tasks/jint/DESIGN.md`](../tasks/jint/DESIGN.md) and
[`tasks/jint/TRACKER.md`](../tasks/jint/TRACKER.md).

### Removal checklist (when `Starling.Js` is good enough)

1. Delete `src/Starling.Bindings.Jint/` and `tests/Starling.Bindings.Jint.Tests/`.
2. Remove the `jint` arm from `Starling.Engine/JsEngineSelector.cs` (and its
   `Starling.Bindings.Jint` project reference); the env var then only accepts
   `starling`.
3. Remove the `Jint` `PackageVersion` from `Directory.Packages.props`.
4. Remove the Jint test262 harness (`Jint*Test262*` in
   `tests/Starling.Js.Test262.Tests`) and the Jint files from `Starling.slnx`,
   the CI interop allowlist, and the `STARLING_JS_ENGINE=jint` CI step.
5. **Keep** `src/Starling.Js.Hosting` (the seam) and `ILayoutHost` there — the
   seam is a clean abstraction worth retaining even with a single engine.

## Spec refs

- [SPEC: ECMA-262 (15th edition, 2024)](https://tc39.es/ecma262/)
- [SPEC: ECMA-402 (Intl)](https://tc39.es/ecma402/) — minimal subset in v1
- [SPEC: Test262](https://github.com/tc39/test262) — conformance harness

## Project layout

```
src/Starling.Js/
├── Starling.Js.csproj
├── Realm.cs                      # public entry
├── JsValue.cs                    # discriminated value type
├── Lex/
│   ├── Lexer.cs
│   ├── Token.cs
│   └── Punctuators.cs
├── Parse/
│   ├── Parser.cs
│   ├── Ast.cs                    # node records
│   ├── CoverGrammar.cs           # for arrow vs paren expr, etc.
│   └── EarlyErrors.cs
├── Bytecode/
│   ├── Compiler.cs               # AST -> bytecode
│   ├── Op.cs                     # 200-ish opcodes
│   ├── Chunk.cs
│   ├── Disassembler.cs
│   └── ScopeAnalyzer.cs
├── Runtime/
│   ├── Vm.cs                     # the interpreter loop
│   ├── CallFrame.cs
│   ├── EnvironmentRecord.cs
│   ├── DeclarativeEnv.cs / ObjectEnv.cs / FunctionEnv.cs / GlobalEnv.cs / ModuleEnv.cs
│   ├── JsObject.cs
│   ├── ObjectShape.cs            # hidden classes
│   ├── PropertyDescriptor.cs
│   └── Reference.cs
├── Intrinsics/
│   ├── ObjectCtor.cs
│   ├── FunctionCtor.cs
│   ├── ArrayCtor.cs
│   ├── StringCtor.cs
│   ├── NumberCtor.cs
│   ├── BooleanCtor.cs
│   ├── SymbolCtor.cs
│   ├── BigIntCtor.cs
│   ├── ErrorCtors.cs             # 7 native error types
│   ├── MathObj.cs
│   ├── JsonObj.cs
│   ├── MapCtor.cs / SetCtor.cs / WeakMapCtor.cs / WeakSetCtor.cs / WeakRefCtor.cs
│   ├── PromiseCtor.cs
│   ├── ProxyCtor.cs / ReflectObj.cs
│   ├── RegExpCtor.cs
│   ├── DateCtor.cs
│   ├── IteratorProto.cs / GeneratorProto.cs / AsyncProto.cs
│   ├── TypedArrayCtors.cs        # Uint8Array..Float64Array
│   └── ArrayBufferCtor.cs / DataViewCtor.cs
├── RegExp/
│   ├── RegExpCompiler.cs
│   ├── RegExpVm.cs               # NFA interpreter (Pike VM)
│   └── UnicodeCategories.cs      # generated from UCD
└── Async/
    ├── PromiseJob.cs             # microtask
    └── AsyncFunction.cs
```

## Value model

NaN-boxing is awkward in managed C# (we lack stable `*` pointers without pinning). Use a tagged 16-byte struct:

```csharp
[StructLayout(LayoutKind.Explicit)]
public readonly struct JsValue : IEquatable<JsValue>
{
    [FieldOffset(0)] private readonly long _bits;
    [FieldOffset(0)] private readonly double _double;
    [FieldOffset(8)] private readonly object? _ref;   // string, JsObject, BigInteger, Symbol

    public JsValueTag Tag => (JsValueTag)((_bits >> 48) & 0xFFFF);

    public static readonly JsValue Undefined = MakeTag(JsValueTag.Undefined);
    public static readonly JsValue Null      = MakeTag(JsValueTag.Null);
    public static readonly JsValue True      = MakeTag(JsValueTag.BoolTrue);
    public static readonly JsValue False     = MakeTag(JsValueTag.BoolFalse);

    public static JsValue Number(double d);
    public static JsValue Int32(int i);   // fast path; same physical layout as a small double
    public static JsValue String(string s);
    public static JsValue BigInt(BigInteger bi);
    public static JsValue Symbol(JsSymbol sym);
    public static JsValue Object(JsObject obj);
}

public enum JsValueTag : ushort {
    Number, Int32, Undefined, Null, BoolTrue, BoolFalse,
    String, BigInt, Symbol, Object
}
```

Notes:
- 16-byte struct keeps stack copies cheap. Passing by `in` for hot paths.
- `Number` stores in `_double` (untagged 64-bit IEEE 754). For non-number tags we use `_bits` high bits.
- `Int32` is a fast-path tag for small integers — many ops can stay in int domain.
- Strings are .NET `string`s. We pay the OOP cost for now.
- `BigInt` wraps `System.Numerics.BigInteger`.

## Strings

JS strings are sequences of UTF-16 code units. .NET strings are also UTF-16. Direct mapping. **Use `string`** for the canonical representation. Indexing is O(1).

For very long string concatenation (e.g. `''+='`-builders), back with a **rope**:

```csharp
public abstract class JsString {
    public static implicit operator JsString(string s) => new LeafString(s);
    public abstract int Length { get; }
    public abstract char this[int i] { get; }
    public abstract string Flatten();
}
public sealed class LeafString(string Value) : JsString { ... }
public sealed class ConcatString(JsString Left, JsString Right) : JsString { ... }
```

The `string`-typed JsValue field can hold either, accessed through helpers.

## Lexer

Per [SPEC: ECMA-262 §12 Lexical Grammar](https://tc39.es/ecma262/#sec-ecmascript-language-lexical-grammar).

Tokens:
```
NumericLiteral, BigIntLiteral, StringLiteral, RegExpLiteral, TemplateLiteralPart,
Identifier, PrivateIdentifier, Keyword (reserved + contextual),
Punctuator (~45),
LineTerminator, Whitespace, Comment (skipped),
EOF
```

Context-sensitive: `/` may start a regex or a divide op depending on prior token. Track this with a `prevSlashContext` flag.

Template literals: lexer is two-state — inside backticks vs inside `${ expr }`.

Goal symbol: lexer is parameterized by `Script` vs `Module` vs `RegExp` per spec.

## Parser

Recursive descent, full ES2024 grammar. Goal symbols: `Script`, `Module`. Output: AST in `Ast.cs`.

### AST shape (subset)

```csharp
public abstract record Node(SourceLocation Loc);
public sealed record ProgramNode(IReadOnlyList<Statement> Body, SourceType Source) : Node;
public abstract record Statement(SourceLocation Loc) : Node(Loc);
public sealed record BlockStmt(IReadOnlyList<Statement> Body) : Statement;
public sealed record ExprStmt(Expression Expr) : Statement;
public sealed record IfStmt(Expression Test, Statement Then, Statement? Else) : Statement;
public sealed record WhileStmt(Expression Test, Statement Body) : Statement;
public sealed record ForStmt(/* init/test/update/body */) : Statement;
public sealed record ForInStmt(...) : Statement;
public sealed record ForOfStmt(...) : Statement;
public sealed record SwitchStmt(Expression Disc, IReadOnlyList<SwitchCase> Cases) : Statement;
public sealed record TryStmt(BlockStmt Try, Catch? Catch, BlockStmt? Finally) : Statement;
public sealed record ThrowStmt(Expression Arg) : Statement;
public sealed record ReturnStmt(Expression? Arg) : Statement;
public sealed record FunctionDecl(...) : Statement;
public sealed record ClassDecl(...) : Statement;
public sealed record VarDecl(string Kind /* var|let|const */, IReadOnlyList<VarBinding> Decls) : Statement;
public sealed record ImportDecl(...) : Statement;
public sealed record ExportDecl(...) : Statement;

public abstract record Expression(SourceLocation Loc) : Node(Loc);
public sealed record Literal(object? Value, string Raw) : Expression;
public sealed record TemplateLiteral(IReadOnlyList<string> Quasis, IReadOnlyList<Expression> Expressions) : Expression;
public sealed record Identifier(string Name) : Expression;
public sealed record ThisExpr : Expression;
public sealed record SuperExpr : Expression;
public sealed record Member(Expression Object, Expression Prop, bool Computed, bool Optional) : Expression;
public sealed record Call(Expression Callee, IReadOnlyList<Expression> Args, bool Optional) : Expression;
public sealed record New(Expression Callee, IReadOnlyList<Expression> Args) : Expression;
public sealed record Assign(string Op, Expression Lhs, Expression Rhs) : Expression;
public sealed record Binary(string Op, Expression Left, Expression Right) : Expression;
public sealed record Logical(string Op, Expression Left, Expression Right) : Expression;   // &&, ||, ??
public sealed record Unary(string Op, Expression Arg, bool Prefix) : Expression;
public sealed record Update(string Op, Expression Arg, bool Prefix) : Expression;
public sealed record Conditional(Expression Test, Expression Then, Expression Else) : Expression;
public sealed record Sequence(IReadOnlyList<Expression> Expressions) : Expression;
public sealed record ArrowFn(...) : Expression;
public sealed record FunctionExpr(...) : Expression;
public sealed record ClassExpr(...) : Expression;
public sealed record ObjectExpr(IReadOnlyList<Property> Properties) : Expression;
public sealed record ArrayExpr(IReadOnlyList<Expression?> Elements) : Expression;
public sealed record SpreadElement(Expression Arg) : Expression;
public sealed record YieldExpr(Expression? Arg, bool Delegate) : Expression;
public sealed record AwaitExpr(Expression Arg) : Expression;
public sealed record ImportExpr(Expression Source) : Expression;
public sealed record TaggedTemplateExpr(Expression Tag, TemplateLiteral Quasi) : Expression;
public sealed record PrivateName(string Name) : Expression;
public sealed record MetaProperty(string Meta, string Property) : Expression;  // new.target, import.meta
```

### Cover grammars

Two notorious cases require cover-grammar parsing:
1. **Arrow function vs parenthesized expression**: `(a, b) => x` vs `(a, b)`. Parse as `CoverParenthesizedAndArrowParameterList`, decide when you see `=>` or its absence.
2. **Object literal vs object destructuring**: `({a} = obj)` vs `{a: 1}`. The pattern's grammar overlaps. Reinterpret after seeing `=`.

Both spec'd. Implement faithfully.

### Strict mode and modules

Modules and class bodies are strict. `"use strict";` directive in scripts enables strict per scope. Track on the parser; affects early errors.

### Regexp literals

Parser hand-tokenizes the regex body (cannot use general lexer due to `/` ambiguity). Forward the source string to `RegExpCompiler` at compile time.

## Bytecode IR

Stack-based with a register **accumulator** for the working value, plus a register file for locals and temporaries. Mirrors V8 Ignition. This is the sweet spot for managed interpreters.

### Encoding

`Op` enum + variable-length operands. Each instruction begins with a 1-byte opcode. Operands are 16-bit indices into the chunk's constant pool / register file.

```csharp
public enum Op : byte
{
    // Stack/register moves
    LoadConst, LoadUndefined, LoadNull, LoadTrue, LoadFalse, LoadThis, LoadGlobal,
    LoadLocal, StoreLocal, LoadFreeVar, StoreFreeVar,

    // Arithmetic
    Add, Sub, Mul, Div, Mod, Pow,
    BitAnd, BitOr, BitXor, BitNot, Shl, Shr, UShr,
    Neg, Pos, LogicalNot,

    // Comparison
    Eq, NeqEq, NotEq, NotNeqEq, Lt, Lte, Gt, Gte,
    In, InstanceOf,

    // Property
    GetProperty, GetPropertyByValue, SetProperty, SetPropertyByValue,
    DeleteProperty, DeletePropertyByValue,
    SetInitialProperty,                 // for object literals (faster than SetProperty)

    // Function
    Call, CallMethod, Construct, ConstructWithNewTarget,
    Return, Yield, YieldDelegate, Await, ThrowOp,

    // Control flow (offsets are int16)
    Jump, JumpIfTrue, JumpIfFalse, JumpIfTruthy, JumpIfFalsy, JumpIfNullOrUndef,
    EnterTry, LeaveTry, Throw,

    // Object / array creation
    CreateObject, CreateArray, CreateRegExp, CreateClass, CreateFunctionClosure,
    CreateRest, Spread,

    // Iteration
    GetIterator, GetAsyncIterator, IteratorNext, IteratorClose,
    ForOfNext, ForInNext,

    // Environment
    PushEnv, PopEnv,
    DefineVar, DefineLet, DefineConst, DefineFunction,

    // Misc
    TypeOf, Void, Debugger, Nop,
    GetSuperProperty, SetSuperProperty, GetSuperConstructor,
}
```

About 90 opcodes in the core; ~30 more for fast paths (e.g. `AddSmi` for small-integer adds, `CallIc0`/`CallIc1`/... for inline cache call shapes).

### Inline caches

Each `GetProperty` site keeps a tiny inline cache: `(lastShape, lastOffset)`. On dispatch, if `object.Shape == lastShape`, jump straight to `_slots[lastOffset]`. Miss → walk prototype chain + update cache. Common-case property access becomes ~5 lines of C#.

### Chunk

```csharp
public sealed class Chunk
{
    public byte[] Code;
    public JsValue[] Constants;
    public string[] Strings;
    public IReadOnlyList<RegExpSpec> RegExps;
    public IReadOnlyList<FunctionSpec> Functions;
    public IReadOnlyList<int> SourceMap;     // pc -> source line
    public int LocalCount;
    public string SourceFile;
    public string SourceText;                // for stack traces
}
```

### Compiler

`Compiler` walks the AST and emits `Op`s. Has a scope analyzer pass first that decides which names are stack locals vs. free vars.

Spec'd compile-time errors (duplicate `let` in same scope, `eval` shadowing, etc.) are emitted during compile and become `SyntaxError`s.

## VM

Threaded interpreter loop. Avoid `switch` over the byte; use a `delegate*<Vm, void>[]` jump table for each opcode, dispatched from a tight loop.

```csharp
public sealed class Vm
{
    private CallFrame _frame;
    private JsValue[] _stack;
    private int _sp;

    public JsValue Run()
    {
        while (true)
        {
            var op = (Op)_frame.Code[_frame.Pc++];
            switch (op) {
                case Op.LoadConst: { var i = ReadU16(); _accum = _frame.Constants[i]; break; }
                case Op.Add:       { _accum = AddOp(_stack[--_sp], _accum); break; }
                // ...
                case Op.Return:    return _accum;
            }
        }
    }
}
```

(Actual impl: precompiled into a per-frame method via DynamicMethod for hot frames in M5+; v1 stays interpreted.)

### Call frames

```csharp
public sealed class CallFrame
{
    public Chunk Chunk;
    public byte[] Code;
    public JsValue[] Constants;
    public int Pc;
    public JsValue[] Locals;            // register file
    public EnvironmentRecord Env;
    public JsObject? ThisBinding;
    public JsFunction? Function;
    public CallFrame? Caller;
    public TryHandler[]? TryHandlers;
}
```

### Exception handling

`ThrowOp` finds the nearest `EnterTry` handler. If frame has no handler, pop and continue search up the call chain. On exhaustion, the thrown value becomes a `JsException` and surfaces to the host.

## Object model

```csharp
public class JsObject
{
    public ObjectShape Shape { get; private set; }
    public JsValue[] Slots;            // indexed by shape
    public JsObject? Prototype;
    public bool Extensible = true;
    public Dictionary<JsSymbol, JsValue>? SymbolSlots;   // rarely used
    public Dictionary<string, PropertyDescriptor>? OverflowDescriptors;  // accessor + sparse

    public virtual JsValue Get(string key, JsValue receiver);
    public virtual void   Set(string key, JsValue value, JsValue receiver);
    public virtual bool   Delete(string key);
    public virtual IEnumerable<string> OwnKeys();
    public virtual void   DefineOwnProperty(string key, PropertyDescriptor pd);
}
```

### Shapes (hidden classes)

```csharp
public sealed class ObjectShape
{
    public ObjectShape? Parent;
    public string? AddedKey;
    public int AddedOffset;
    public IReadOnlyDictionary<string, int> Offsets;   // logical view; backed by chain
    public Dictionary<string, ObjectShape>? Transitions;   // for caching forward links
}
```

Transitions cache: `{a: 1}` and `{a: 2}` share a shape. Adding `b` advances both to the same daughter shape. This is what allows inline-cache hits across instances. **Critical for perf.**

### Property descriptors

```csharp
public readonly struct PropertyDescriptor
{
    public JsValue Value;
    public JsValue Getter;   // function or undefined
    public JsValue Setter;
    public bool Writable;
    public bool Enumerable;
    public bool Configurable;
    public DescriptorKind Kind;   // Data | Accessor
}
```

### Special objects

- **Arrays**: `JsArray : JsObject` with dense `JsValue[] Elements` (length 16, grows). Indexed access bypasses property lookups. Sparse arrays fall back to descriptor map.
- **Functions**: `JsFunction : JsObject` with `Chunk` (or native delegate), `Realm`, `EnvRecord` (closure scope), `[[HomeObject]]` for class methods.
- **Bound functions**, **Proxy**, **TypedArray**, **Map/Set**, **Promise**: each its own subclass with overridden internals.

## GC strategy

**Use the .NET GC.** Pure managed. JS objects are managed CLR objects; references between them are real CLR refs. The GC reclaims unreachable JS objects.

Trade-offs:
- **Pro**: no bespoke heap manager; rock-solid; concurrent GC; cross-platform.
- **Con**: latency spikes from gen 2 collections, large GC pauses on large heaps.

Mitigations:
- Pool `JsObject`, `JsArray`, `CallFrame`.
- Avoid boxed `JsValue` whenever possible (it's a `struct`).
- For RegExp NFA, recycle state arrays.

`WeakRef` and `FinalizationRegistry` map cleanly to `WeakReference<T>` and `ConditionalWeakTable<TKey, TValue>` in .NET.

## Realms and intrinsics

```csharp
public sealed class Realm
{
    public GlobalEnvironmentRecord GlobalEnv { get; }
    public JsObject GlobalObject { get; }
    public Intrinsics Intrinsics { get; }
    public ModuleRegistry Modules { get; }
    public Vm Vm { get; }
}

public sealed class Intrinsics
{
    public JsObject ObjectPrototype;
    public JsObject FunctionPrototype;
    public JsObject ArrayPrototype;
    public JsObject StringPrototype;
    public JsObject NumberPrototype;
    public JsObject BooleanPrototype;
    public JsObject ErrorPrototype;
    public JsObject IteratorPrototype;
    public JsObject GeneratorPrototype;
    public JsObject AsyncIteratorPrototype;
    public JsObject AsyncGeneratorPrototype;
    public JsObject PromisePrototype;
    public JsObject MapPrototype;
    public JsObject SetPrototype;
    public JsObject ArrayBufferPrototype;
    public JsObject TypedArrayPrototype;
    public JsObject DataViewPrototype;
    public JsObject RegExpPrototype;
    public JsObject DatePrototype;
    public JsObject SymbolPrototype;
    public JsObject BigIntPrototype;
    public JsObject ProxyConstructor;
    public JsObject ReflectObject;
    public JsObject ThrowTypeErrorFn;
    // ... ~60 in total
}
```

Each intrinsic is built imperatively at realm init: create object, link prototype, define properties via descriptors. Generated code is fine here if it stays readable.

## Built-ins to implement (v1 must-have)

| Family | Members | Notes |
|---|---|---|
| `Object` | `assign, create, defineProperty/ies, entries/keys/values, freeze/isFrozen, getOwnProperty*, getPrototypeOf, is, fromEntries, hasOwn, setPrototypeOf, preventExtensions, isExtensible, seal/isSealed, prototype.{hasOwnProperty, isPrototypeOf, propertyIsEnumerable, toString, valueOf}` | all |
| `Array` | `from, isArray, of, prototype.{at, concat, copyWithin, entries, every, fill, filter, find, findIndex, findLast, findLastIndex, flat, flatMap, forEach, includes, indexOf, join, keys, lastIndexOf, map, pop, push, reduce, reduceRight, reverse, shift, slice, some, sort, splice, toReversed, toSorted, toSpliced, unshift, values, with}` | all |
| `String` | `fromCharCode, fromCodePoint, raw, prototype.{at, charAt, charCodeAt, codePointAt, concat, endsWith, includes, indexOf, isWellFormed, lastIndexOf, match, matchAll, normalize, padEnd, padStart, repeat, replace, replaceAll, search, slice, split, startsWith, substring, toLowerCase, toUpperCase, toWellFormed, trim, trimEnd, trimStart}` | all |
| `Number` | constants + `isFinite/isInteger/isNaN/isSafeInteger, parseInt/parseFloat`, prototype.{toExponential, toFixed, toPrecision, toString} | all |
| `BigInt` | `asIntN, asUintN`, prototype.{toString, valueOf} | all |
| `Math` | every method | all |
| `JSON` | `parse, stringify` | rfc 8259 + es spec |
| `Date` | parsing + formatting + getters/setters | minimal locale support; ICU absent. Use invariant. |
| `RegExp` | full grammar including unicode (u, v flags) | NFA Pike VM |
| `Promise` | `resolve, reject, all, allSettled, any, race, withResolvers`, prototype.{then, catch, finally} | hooks into event loop microtask queue |
| `Symbol` | `for, keyFor, hasInstance, iterator, asyncIterator, isConcatSpreadable, ...` | well-known symbols |
| `Map / Set / WeakMap / WeakSet` | full surface | hashtable with siphash key |
| `WeakRef / FinalizationRegistry` | full | via .NET WeakReference |
| `Proxy / Reflect` | full | gotcha-heavy; test against test262 |
| Error types | `Error, TypeError, RangeError, ReferenceError, SyntaxError, URIError, EvalError, AggregateError` | with stack trace strings |
| `globalThis`, `parseInt`, `parseFloat`, `isNaN`, `isFinite`, `encodeURI*`, `decodeURI*` | | |
| `TypedArray, ArrayBuffer, DataView` | full | required by SPAs |
| `console` | `log, info, warn, error, debug, dir, table, time, timeEnd, count` | bind to host logger |
| `Intl` | OUT-OF-SCOPE-V1 except `Intl.Collator`, `Intl.DateTimeFormat` (no locale, falls back to invariant) | TC39-spec minimal |

## RegExp

Implement [SPEC: ECMA-262 §22.2](https://tc39.es/ecma262/#sec-regexp-regular-expression-objects) literally. Two-phase:
1. **Compile**: parser → AST → NFA via Thompson construction.
2. **Match**: Pike VM (parallel NFA threads).

Why not .NET `System.Text.RegularExpressions`? **Different semantics.** JS regex is closer to Perl/POSIX-extended; backrefs, lookbehind, unicode property escapes (`\p{L}`) differ. Implementing our own avoids spec-incompat surprises.

Unicode `\p{Script=Greek}` etc. requires bundling UCD tables. Generate at build time from [UCD 16.0](https://www.unicode.org/versions/Unicode16.0.0/).

## Modules

ES Module loader per [SPEC: §16.2](https://tc39.es/ecma262/#sec-modules).
- `import` statement parsed to `ImportDecl`.
- Module loader fetches source via `Starling.Net`, parses, links, evaluates.
- `import()` (dynamic) returns a Promise.
- `import.meta` provides `url`.
- Module records cached by `(realm, url)`.

CommonJS / UMD: out of scope. The web doesn't need it.

## async / await / generators

Compiled to a state-machine bytecode pattern. Yield-points checkpoint local register snapshots. Resume by jumping to the saved PC and restoring locals.

Async functions implicitly wrap return values in a Promise. `await` enqueues a continuation as a microtask on resolution.

## Performance

- Don't promise V8 speed. Aim for ~5-10x slower than V8 in M3, ~3x in M7.
- No JIT in v1. .NET tiered JIT under the interpreter buys back some perf.
- Inline-cache GetProperty / SetProperty hard.
- Avoid allocations in the hot loop. `JsValue` is a struct.
- Property storage: shape-based slots, dense.

Benchmark with Sunspider/Octane subset. Target M3: 60s SunSpider total ≤ 5s on a CI runner.

## Test conformance

Use [Test262](https://github.com/tc39/test262). Subset selection: language features in our v1 surface. Pass rate target by milestone (see [13_MILESTONES.md](13_MILESTONES.md)):
- M3 (lexer + parser + interpreter for ES5): 80%.
- M5 (most of ES2015+): 90%.
- M7 (claude.ai works): 95%.
- M11 (final v1 ship): 98%.

## Acceptance Tests

- [ ] Lexer accepts all valid tokens from the ECMA-262 test corpus.
- [ ] Parser produces correct AST for the full ES2024 grammar (test against fixtures from Test262's `parser/*.js`).
- [ ] Bytecode compiler emits expected sequences for hand-picked fixtures (`for (let i...)`, `try/catch`, `async/await`, class methods).
- [ ] VM passes 80% of Test262 by M3 entry.
- [ ] `Promise.resolve(1).then(v => v + 1)` returns 2 in the next microtask.
- [ ] `RegExp("a+b", "g").exec("aaab")` matches "aaab".
- [ ] `for-of` over a generator yields the expected sequence.
- [ ] `Proxy` traps for `get`/`set`/`has`/`deleteProperty`/`ownKeys` fire correctly.
- [ ] Stack overflow surfaces as a `RangeError` with a meaningful trace, not as a C# `StackOverflowException`.
- [ ] No `DllImport`, no `Jint` import, no `Microsoft.JScript`. `grep -rn 'DllImport\|Jint\|JScript' src/Starling.Js/` is empty.
