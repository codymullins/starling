using Starling.Js.Ast;
using Starling.Js.Runtime;

namespace Starling.Js.Bytecode;

/// <summary>
/// Walks a parsed JS <see cref="Program"/> and emits a <see cref="Chunk"/>
/// of bytecode for the M3-04 VM to execute.
/// </summary>
/// <remarks>
/// <para>
/// First-cut scope (wp:M3-03):
/// </para>
/// <list type="bullet">
///   <item>Literals (numeric / string / boolean / null).</item>
///   <item>Identifier references — locals if declared in current scope,
///         else global by name.</item>
///   <item>Binary, unary, update, logical (with short-circuit jumps).</item>
///   <item>Conditional (ternary) via JumpIfFalse + Jump.</item>
///   <item>Assignment to identifier or member.</item>
///   <item>Property access (dot + computed).</item>
///   <item>Call expressions.</item>
///   <item>Statements: ExpressionStatement, BlockStatement, VariableDeclaration,
///         IfStatement, WhileStatement, ReturnStatement (function compiles to
///         a separate sub-chunk in M3-04; for now Return at top-level halts).</item>
/// </list>
/// <para>
/// Out of scope (queued as follow-ups):
/// </para>
/// <list type="bullet">
///   <item>FunctionDeclaration sub-chunks + closures (M3-04 VM territory).</item>
///   <item>TryStatement (M3-05).</item>
///   <item>SwitchStatement (M3-05).</item>
///   <item>ForIn/ForOf iteration protocol (M3-04 VM needs iterator support).</item>
/// </list>
/// </remarks>
public sealed partial class JsCompiler
{
    private readonly ChunkBuilder _b = new();
    private readonly List<Dictionary<string, int>> _scopes = [new()];

    /// <summary>Stack of class private-name scopes — each frame maps the
    /// source-level name (e.g. <c>#x</c>) to its mangled own-property key
    /// for the active class. Resolution walks outer-to-inner so nested
    /// classes shadow correctly.</summary>
    private readonly Stack<Dictionary<string, string>> _privateScopes = new();

    /// <summary>Enclosing compiler — null for the script-top compiler. Used
    /// by lazy upvalue resolution to walk lexically-enclosing scopes when
    /// a free identifier inside a nested function isn't local here.</summary>
    private readonly JsCompiler? _parent;

    /// <summary>
    /// Description of a captured name as recorded in this function's
    /// upvalue table. <see cref="IsLocalCapture"/> distinguishes the two
    /// cases: when true, <see cref="Index"/> is a parent local slot;
    /// when false, it's an index into the parent's upvalue table (i.e.
    /// the capture chains through an intermediate function).
    /// </summary>
    private readonly record struct UpvalueRef(bool IsLocalCapture, int Index);

    /// <summary>Upvalue descriptors for the function this compiler is
    /// producing. Index in this list is the operand passed to
    /// <see cref="Opcode.LoadUpvalue"/> inside the body, and is the order
    /// in which the parent must push captured values before
    /// <see cref="Opcode.MakeClosure"/>.</summary>
    private readonly List<UpvalueRef> _upvalues = [];

    /// <summary>Name → index in <see cref="_upvalues"/>, so repeated reads
    /// of the same captured name reuse one upvalue slot.</summary>
    private readonly Dictionary<string, int> _upvalueByName = new(StringComparer.Ordinal);

    /// <summary>gap:closure-write-back — names declared in this compiler's
    /// function that are referenced from inside one or more nested
    /// functions. Computed by <see cref="CaptureAnalysis.Compute"/> before
    /// any bytecode is emitted; consulted at every declaration site to
    /// decide whether the slot uses <see cref="Starling.Js.Runtime.Cell"/>
    /// storage.</summary>
    private HashSet<string> _capturedNames = new(StringComparer.Ordinal);

    /// <summary>B7-followup-b — open-loop tracking for <c>break</c> /
    /// <c>continue</c>. Each entry holds the patch lists for jumps targeting
    /// the loop's continue / break sites; the loop body emits forward
    /// <see cref="Opcode.Jump"/> instructions and records the operand
    /// position so the loop's lowering pass can patch them once the
    /// continue/break PCs are known. <see cref="TryDepthAtEntry"/> records
    /// the try-frame depth at the loop's entry; a <c>break</c> /
    /// <c>continue</c> whose enclosing try-depth is greater than this would
    /// need to run intervening <c>finally</c> blocks (currently
    /// unsupported — the compiler throws).</summary>
    private sealed class LoopFrame
    {
        public List<int> BreakPatches { get; } = [];
        public List<int> ContinuePatches { get; } = [];
        public int TryDepthAtEntry { get; init; }
    }

    private readonly Stack<LoopFrame> _loops = new();

    /// <summary>B7-followup-b — depth of currently open try-frames in the
    /// emitted bytecode. Incremented on <see cref="Opcode.EnterTry"/>,
    /// decremented after the matching cleanup. Used by <c>break</c> /
    /// <c>continue</c> to detect the (currently-unsupported) cross-finally
    /// case.</summary>
    private int _tryDepth;

    public JsCompiler() : this(parent: null) { }

    private JsCompiler(JsCompiler? parent)
    {
        _parent = parent;
    }

    /// <summary>gap:closure-write-back — is this name marked for shared-cell
    /// storage in the current function?</summary>
    private bool IsNameCaptured(string name) => _capturedNames.Contains(name);

    /// <summary>gap:closure-write-back — convenience: did the compiler box
    /// the local at this slot? Used by load/store emission sites.</summary>
    private bool IsSlotCaptured(int slot) => _b.IsCaptured(slot);

    /// <summary>gap:script-top-var-not-global — true when this compiler is
    /// emitting the top-level script chunk (i.e. not inside any function or
    /// arrow body). Script-top <c>var</c> / <c>let</c> / <c>const</c>
    /// declarations become properties on the global object per §16.1.7
    /// ScriptEvaluation, so they're addressed by name (LoadGlobal /
    /// StoreGlobal / DeclareGlobalVar) rather than allocated to local
    /// slots.</summary>
    private bool IsScriptTop => _parent is null;

    public static Chunk Compile(Program program, string? name = "<script>")
    {
        var c = new JsCompiler();
        c.RunCaptureAnalysisForScript(program.Body);
        c.EmitProgram(program, keepLastExpression: false);
        return c._b.Build(name);
    }

    /// <summary>
    /// Compile in eval / REPL mode: if the last statement is an
    /// ExpressionStatement, leave its value on the stack so the caller of
    /// the VM can observe it. Used by <c>starling js</c> and by tests.
    /// </summary>
    public static Chunk CompileForEval(Program program, string? name = "<eval>")
    {
        var c = new JsCompiler();
        c.RunCaptureAnalysisForScript(program.Body);
        c.EmitProgram(program, keepLastExpression: true);
        return c._b.Build(name);
    }

    /// <summary>gap:script-top-var-not-global — script-top declarations
    /// (<c>var</c>, <c>let</c>, <c>const</c>, function-declarations, classes)
    /// all bind on the global object now, so nested functions resolve any
    /// free identifier through <see cref="Opcode.LoadGlobal"/> /
    /// <see cref="Opcode.StoreGlobal"/>. There are no script-top "locals"
    /// to capture, so the captured-name set is empty.</summary>
    private void RunCaptureAnalysisForScript(IReadOnlyList<Statement> body)
    {
        _ = body;
        _capturedNames = new HashSet<string>(StringComparer.Ordinal);
    }

    /// <summary>gap:closure-write-back — populate <see cref="_capturedNames"/>
    /// for a function body. Run by the sub-compiler before any bytecode is
    /// emitted, so declaration sites can pick the right opcode.</summary>
    private void RunCaptureAnalysisForFunction(IReadOnlyList<Expression> parameters, IReadOnlyList<Statement> body)
    {
        _capturedNames = CaptureAnalysis.Compute(parameters, body);
    }

    private void EmitProgram(Program p, bool keepLastExpression)
    {
        // gap:closure-write-back — pre-allocate captured top-level vars
        // BEFORE we hoist function declarations, so a hoisted function's
        // body resolves a captured name to the parent's local (cell) slot
        // instead of falling through to LoadGlobal/StoreGlobal.
        PreallocateCapturedVarBindings(p.Body);
        // Hoist FunctionDeclarations: compile bodies, allocate locals,
        // emit StoreLocal in declaration order so they're callable before
        // their textual position (matches §10.2.11 / §13.2.1 var hoisting
        // for function declarations).
        HoistFunctionDeclarations(p.Body);

        for (var i = 0; i < p.Body.Count; i++)
        {
            var s = p.Body[i];
            var isLast = i == p.Body.Count - 1;
            if (s is FunctionDeclaration)
            {
                // Already hoisted; nothing to emit at the textual position.
                continue;
            }
            if (isLast && keepLastExpression && s is ExpressionStatement es)
            {
                EmitExpression(es.Expression); // skip trailing Pop
            }
            else
            {
                EmitStatement(s);
            }
        }
        _b.Emit(Opcode.Halt);
    }

    private void HoistFunctionDeclarations(IReadOnlyList<Statement> body)
    {
        // At the script top there is no enclosing function, so hoisted
        // function-declarations install themselves as globals (mirroring
        // §10.2.11 host-defined global object behavior). Inside a function,
        // they bind a fresh local slot in the function's variable scope.
        var isScriptTop = _parent is null;
        foreach (var s in body)
        {
            if (s is not FunctionDeclaration fd) continue;
            // Reserve the slot / register the binding BEFORE compiling the
            // body, so the body can resolve `fd.Name` to itself (recursion)
            // and so nested function bodies that capture the name go through
            // the same upvalue.
            int? slot = null;
            if (!isScriptTop)
            {
                if (_scopes[^1].ContainsKey(fd.Name.Name))
                {
                    slot = _scopes[^1][fd.Name.Name];
                }
                else
                {
                    var fresh = _b.ReserveLocal();
                    _scopes[^1][fd.Name.Name] = fresh;
                    if (IsNameCaptured(fd.Name.Name)) _b.MarkCaptured(fresh);
                    if (_b.IsCaptured(fresh)) _b.Emit(Opcode.InitCellLocal, (byte)fresh);
                    slot = fresh;
                }
            }
            // Compile the body in a fresh sub-compiler parented to this
            // one so the body can resolve free identifiers as upvalues
            // captured from this scope.
            var sub = new JsCompiler(parent: this);
            sub.RunCaptureAnalysisForFunction(fd.Params, fd.Body.Body);
            sub.EmitFunctionBody(fd);
            var chunk = sub._b.Build(fd.Name.Name);

            // Emit either LoadFunction (no captures) or push upvalues +
            // MakeClosure. Either way, leave the function value on the
            // stack, then store under the function's name as a global so
            // recursive references inside the body resolve to the same
            // closure instance.
            EmitFunctionConstructor(fd.Name.Name, chunk,
                CountSimpleParams(fd.Params), sub._upvalues,
                ResolveFunctionKind(fd.Async, fd.Generator));
            if (isScriptTop)
            {
                var nameIdx = _b.AddConstant(fd.Name.Name);
                _b.EmitU16(Opcode.StoreGlobal, nameIdx);
            }
            else
            {
                EmitStoreLocalSlot(slot!.Value);
            }
            // StoreGlobal / StoreLocal-flavored ops do not re-push, so the
            // stack is balanced after the hoist.
        }
    }

    /// <summary>gap:closure-write-back — emit the correct store opcode for
    /// a local slot, accounting for whether the slot was promoted to a
    /// cell.</summary>
    private void EmitStoreLocalSlot(int slot)
    {
        if (_b.IsCaptured(slot)) _b.Emit(Opcode.StoreCellLocal, (byte)slot);
        else _b.Emit(Opcode.StoreLocal, (byte)slot);
    }

    /// <summary>gap:closure-write-back — emit the correct load opcode for
    /// a local slot, accounting for whether the slot was promoted to a
    /// cell.</summary>
    private void EmitLoadLocalSlot(int slot)
    {
        if (_b.IsCaptured(slot)) _b.Emit(Opcode.LoadCellLocal, (byte)slot);
        else _b.Emit(Opcode.LoadLocal, (byte)slot);
    }

    /// <summary>Materialize a function as either a plain template
    /// reference (no upvalues — emits <see cref="Opcode.LoadFunction"/>)
    /// or as a closure-construction sequence: push each upvalue's
    /// snapshot, then emit <see cref="Opcode.MakeClosure"/>. Leaves the
    /// resulting function value on the top of the stack.</summary>
    private void EmitFunctionConstructor(
        string name, Chunk body, int arity, IReadOnlyList<UpvalueRef> upvalues)
        => EmitFunctionConstructor(name, body, arity, upvalues, Runtime.JsFunctionKind.Normal);

    private void EmitFunctionConstructor(
        string name, Chunk body, int arity, IReadOnlyList<UpvalueRef> upvalues,
        Runtime.JsFunctionKind kind)
    {
        var fn = new Runtime.JsFunction(name, body, arity) { Kind = kind };
        var fnIdx = _b.AddConstant(fn);

        if (upvalues.Count == 0)
        {
            _b.EmitU16(Opcode.LoadFunction, fnIdx);
            return;
        }

        if (upvalues.Count > 255)
            throw new NotSupportedException("more than 255 captured variables not supported");

        // gap:closure-write-back — push the captured cell (not its value)
        // so that the new closure aliases the same shared cell that the
        // owning function reads and writes. Parent local slots already
        // hold the cell as a JsValue (allocated by InitCellLocal /
        // PromoteParamCell), so a plain LoadLocal pushes the cell. Parent
        // upvalues are dereferenced by the default LoadUpvalue, so we
        // need LoadUpvalueCell to push the cell intact.
        foreach (var u in upvalues)
        {
            if (u.IsLocalCapture)
            {
                // The parent's slot must already be a cell here (the
                // static CaptureAnalysis seeded the parent's
                // _capturedNames before any bytecode was emitted, and
                // every declaration site honored that). Plain LoadLocal
                // pushes the slot value — which is the cell.
                _b.Emit(Opcode.LoadLocal, (byte)u.Index);
            }
            else
            {
                _b.Emit(Opcode.LoadUpvalueCell, (byte)u.Index);
            }
        }
        _b.EmitU16(Opcode.MakeClosure, fnIdx);
        _b.EmitU8Raw((byte)upvalues.Count);
    }

    private static int CountSimpleParams(IReadOnlyList<Expression> ps)
    {
        var n = 0;
        foreach (var p in ps)
        {
            if (p is SpreadElement) break;
            if (p is AssignmentExpression or AssignmentPattern) break;
            n++;
        }
        return n;
    }

    /// <summary>Compile a function body. Parameters get the first N local
    /// slots; the body's own var declarations follow.</summary>
    private void EmitFunctionBody(FunctionDeclaration fd)
    {
        // Reserve a local slot per simple-identifier parameter so the
        // callee sees args in slots 0..N-1.
        BindFunctionParameters(fd.Params);
        // gap:closure-write-back — captured `var` bindings must exist as
        // locals BEFORE we compile any nested function body that might
        // resolve them as upvalues. Pre-allocate slots for every captured
        // var in this function's body, and emit InitCellLocal so the slot
        // already holds a Cell when an inner closure is constructed.
        PreallocateCapturedVarBindings(fd.Body.Body);
        // gap:closure-write-back — function-declarations declared inside a
        // function body are hoisted to the top of the function per §13.2.1
        // and §14.1.18 (function-scoped). Without this, an inner
        // `function inner() {...}` would be silently dropped, breaking
        // closure tests like `function outer(){ var x=0; function inner(){x=5} inner(); return x }`.
        HoistFunctionDeclarations(fd.Body.Body);
        foreach (var inner in fd.Body.Body) EmitStatement(inner);
        // Implicit `return undefined` if the body didn't return.
        _b.Emit(Opcode.ReturnUndefined);
    }

    /// <summary>gap:closure-write-back — walk this function's body and
    /// pre-allocate local slots for every <c>var</c>/<c>let</c>/<c>const</c>
    /// binding whose name is captured by a nested function. Captured slots
    /// are also initialized to a Cell (with undefined) immediately, so a
    /// nested closure constructed during hoisting can capture the cell.
    /// </summary>
    /// <remarks>
    /// Non-captured locals stay unallocated here — they're declared at
    /// their textual site by <see cref="DeclarePatternBindings"/>, which
    /// keeps the fast path unchanged for the overwhelming majority of
    /// bindings.
    /// </remarks>
    private void PreallocateCapturedVarBindings(IReadOnlyList<Statement> body)
    {
        foreach (var s in body) PreallocateCapturedInStatement(s);
    }

    private void PreallocateCapturedInStatement(Statement? s)
    {
        if (s is null) return;
        switch (s)
        {
            case VariableDeclaration vd:
                foreach (var d in vd.Declarations) PreallocateCapturedInPattern(d.Id);
                return;
            case BlockStatement b: foreach (var x in b.Body) PreallocateCapturedInStatement(x); return;
            case IfStatement i:
                PreallocateCapturedInStatement(i.Consequent);
                PreallocateCapturedInStatement(i.Alternate);
                return;
            case WhileStatement w: PreallocateCapturedInStatement(w.Body); return;
            case DoWhileStatement dw: PreallocateCapturedInStatement(dw.Body); return;
            case ForStatement f:
                if (f.Init is VariableDeclaration fvd)
                    foreach (var d in fvd.Declarations) PreallocateCapturedInPattern(d.Id);
                PreallocateCapturedInStatement(f.Body);
                return;
            case ForInStatement fi:
                if (fi.Left is VariableDeclaration vdi)
                    foreach (var d in vdi.Declarations) PreallocateCapturedInPattern(d.Id);
                PreallocateCapturedInStatement(fi.Body);
                return;
            case ForOfStatement fo:
                if (fo.Left is VariableDeclaration vdo)
                    foreach (var d in vdo.Declarations) PreallocateCapturedInPattern(d.Id);
                PreallocateCapturedInStatement(fo.Body);
                return;
            case SwitchStatement sw:
                foreach (var c in sw.Cases)
                    foreach (var s2 in c.Consequent) PreallocateCapturedInStatement(s2);
                return;
            case TryStatement tr:
                PreallocateCapturedInStatement(tr.Block);
                if (tr.Handler is not null)
                    foreach (var s2 in tr.Handler.Body.Body) PreallocateCapturedInStatement(s2);
                if (tr.Finalizer is not null) PreallocateCapturedInStatement(tr.Finalizer);
                return;
            case LabeledStatement ls: PreallocateCapturedInStatement(ls.Body); return;
        }
    }

    private void PreallocateCapturedInPattern(Expression? pattern)
    {
        if (pattern is null) return;
        switch (pattern)
        {
            case Identifier id:
                if (IsNameCaptured(id.Name) && !_scopes[^1].ContainsKey(id.Name))
                {
                    var slot = _b.ReserveLocal();
                    _scopes[^1][id.Name] = slot;
                    _b.MarkCaptured(slot);
                    _b.Emit(Opcode.InitCellLocal, (byte)slot);
                }
                return;
            case AssignmentExpression a when a.Op == "=": PreallocateCapturedInPattern(a.Target); return;
            case AssignmentPattern a: PreallocateCapturedInPattern(a.Target); return;
            case ArrayPattern arr:
                foreach (var el in arr.Elements)
                {
                    switch (el)
                    {
                        case ArrayPatternBindingElement binding: PreallocateCapturedInPattern(binding.Target); break;
                        case ArrayPatternRestElement rest: PreallocateCapturedInPattern(rest.Target); break;
                    }
                }
                return;
            case ObjectPattern obj:
                foreach (var prop in obj.Properties) PreallocateCapturedInPattern(prop.Target);
                if (obj.Rest is not null) PreallocateCapturedInPattern(obj.Rest.Argument);
                return;
            case ArrayExpression arr:
                foreach (var el in arr.Elements)
                {
                    if (el is null) continue;
                    PreallocateCapturedInPattern(el is SpreadElement sp ? sp.Argument : el);
                }
                return;
            case ObjectExpression obj:
                foreach (var prop in obj.Properties)
                {
                    if (prop.Value is SpreadElement sp) PreallocateCapturedInPattern(sp.Argument);
                    else PreallocateCapturedInPattern(prop.Value);
                }
                return;
            case SpreadElement spread: PreallocateCapturedInPattern(spread.Argument); return;
            case RestElement rest: PreallocateCapturedInPattern(rest.Argument); return;
        }
    }

    // -----------------------------------------------------------------------
    // Statements
    // -----------------------------------------------------------------------

    private void EmitStatement(Statement s)
    {
        switch (s)
        {
            case EmptyStatement: return;
            case ExpressionStatement es:
                EmitExpression(es.Expression);
                _b.Emit(Opcode.Pop);
                return;
            case BlockStatement bs:
                _scopes.Add(new());
                foreach (var inner in bs.Body) EmitStatement(inner);
                _scopes.RemoveAt(_scopes.Count - 1);
                return;
            case VariableDeclaration vd:
                EmitVarDecl(vd);
                return;
            case IfStatement i:
                EmitExpression(i.Test);
                var jz = _b.EmitJump(Opcode.JumpIfFalse);
                EmitStatement(i.Consequent);
                if (i.Alternate is not null)
                {
                    var jend = _b.EmitJump(Opcode.Jump);
                    _b.PatchJump(jz);
                    EmitStatement(i.Alternate);
                    _b.PatchJump(jend);
                }
                else
                {
                    _b.PatchJump(jz);
                }
                return;
            case WhileStatement w:
                EmitWhile(w);
                return;
            case DoWhileStatement dw:
                EmitDoWhile(dw);
                return;
            case ForStatement f:
                EmitFor(f);
                return;
            case ForOfStatement fo:
                EmitForOf(fo);
                return;
            case ForInStatement fi:
                EmitForIn(fi);
                return;
            case BreakStatement bs:
                EmitBreakOrContinue(bs.Start, isBreak: true, label: bs.Label);
                return;
            case ContinueStatement cs:
                EmitBreakOrContinue(cs.Start, isBreak: false, label: cs.Label);
                return;
            case ReturnStatement r:
                if (r.Argument is null) _b.Emit(Opcode.ReturnUndefined);
                else
                {
                    EmitExpression(r.Argument);
                    _b.Emit(Opcode.Return);
                }
                return;
            case ThrowStatement t:
                EmitExpression(t.Argument);
                _b.Emit(Opcode.Throw);
                return;
            case DebuggerStatement:
                _b.Emit(Opcode.Nop); // M3-05+ may emit a real debugger trap
                return;
            case FunctionDeclaration:
                // Hoisted at scope entry by HoistFunctionDeclarations;
                // textual position emits nothing.
                return;
            case ClassDeclaration cd:
                EmitClassDeclaration(cd);
                return;
            case TryStatement ts:
                EmitTry(ts);
                return;
        }
        throw new NotSupportedException(
            $"compiler: statement kind '{s.GetType().Name}' not yet supported (see wp:M3-03 notes).");
    }

    /// <summary>
    /// §14.15 — emit <c>try</c>/<c>catch</c>/<c>finally</c>. Lowering relies on a per-VM
    /// try-frame stack: <see cref="Opcode.EnterTry"/> records both branch targets and
    /// the eval-stack height, the dispatch loop's outer C# <c>catch (JsThrow)</c>
    /// routes thrown values to the active frame's catch (or finally, if no handler),
    /// and <see cref="Opcode.LeaveTry"/> / <see cref="Opcode.EndFinally"/> drive the
    /// normal-completion / pending-completion flow.
    /// </summary>
    private void EmitTry(TryStatement ts)
    {
        var hasHandler = ts.Handler is not null;
        var hasFinalizer = ts.Finalizer is not null;
        if (!hasHandler && !hasFinalizer)
        {
            EmitStatement(ts.Block);
            return;
        }

        _b.Emit(Opcode.EnterTry);
        var catchOperandPos = _b.Position;
        _b.EmitU16Raw(0xFFFF);
        var finallyOperandPos = _b.Position;
        _b.EmitU16Raw(0xFFFF);

        _tryDepth++;
        try
        {
            EmitTryBody(ts, catchOperandPos, finallyOperandPos);
        }
        finally
        {
            _tryDepth--;
        }
    }

    private void EmitTryBody(TryStatement ts, int catchOperandPos, int finallyOperandPos)
    {
        var hasHandler = ts.Handler is not null;
        var hasFinalizer = ts.Finalizer is not null;
        EmitStatement(ts.Block);
        _b.Emit(Opcode.LeaveTry);
        int jumpPastHandler = -1;
        if (hasHandler && !hasFinalizer)
        {
            jumpPastHandler = _b.EmitJump(Opcode.Jump);
        }

        if (hasHandler)
        {
            var catchTargetDelta = _b.Position - (catchOperandPos + 4);
            if (catchTargetDelta is < short.MinValue or > short.MaxValue)
                throw new InvalidOperationException("try/catch catch offset overflows i16");
            _b.PatchI16(catchOperandPos, (short)catchTargetDelta);

            _scopes.Add(new());
            var handler = ts.Handler!;
            if (handler.Param is Identifier idParam)
            {
                var slot = _b.ReserveLocal();
                _scopes[^1][idParam.Name] = slot;
                // gap:closure-write-back — catch bindings can be captured too.
                if (IsNameCaptured(idParam.Name))
                {
                    _b.MarkCaptured(slot);
                    _b.Emit(Opcode.InitCellLocal, (byte)slot);
                    _b.Emit(Opcode.StoreCellLocal, (byte)slot);
                }
                else
                {
                    _b.Emit(Opcode.DeclareLocal, (byte)slot);
                    _b.Emit(Opcode.StoreLocal, (byte)slot);
                }
            }
            else if (handler.Param is null)
            {
                _b.Emit(Opcode.Pop);
            }
            else
            {
                var srcSlot = _b.ReserveLocal();
                _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
                DeclarePatternBindings(handler.Param);
                EmitPatternFromLocal(handler.Param, srcSlot, isDeclaration: true);
            }
            foreach (var inner in handler.Body.Body) EmitStatement(inner);
            _scopes.RemoveAt(_scopes.Count - 1);
            _b.Emit(Opcode.LeaveTry);
        }

        if (hasFinalizer)
        {
            var finallyTargetDelta = _b.Position - (finallyOperandPos + 2);
            if (finallyTargetDelta is < short.MinValue or > short.MaxValue)
                throw new InvalidOperationException("try/catch finally offset overflows i16");
            _b.PatchI16(finallyOperandPos, (short)finallyTargetDelta);

            EmitStatement(ts.Finalizer!);
            _b.Emit(Opcode.EndFinally);
        }

        if (jumpPastHandler >= 0) _b.PatchJump(jumpPastHandler);
    }

    private void EmitVarDecl(VariableDeclaration vd)
    {
        foreach (var d in vd.Declarations)
        {
            // ECMA-262 §14.3.3 BindingPattern: declarations reserve all
            // binding names first, then initialize by walking the pattern.
            DeclarePatternBindings(d.Id);
            if (d.Init is not null)
            {
                if (d.Id is Identifier id)
                {
                    EmitExpression(d.Init);
                    // gap:script-top-var-not-global — at script top, the
                    // binding is a global property (not a local slot), so
                    // the initializer write routes through StoreGlobal.
                    if (IsScriptTop)
                    {
                        _b.EmitU16(Opcode.StoreGlobal, _b.AddConstant(id.Name));
                    }
                    else
                    {
                        if (!TryResolveLocal(id.Name, out var slot))
                            throw new InvalidOperationException($"missing declared local '{id.Name}'");
                        EmitStoreLocalSlot(slot);
                    }
                }
                else
                {
                    var srcSlot = _b.ReserveLocal();
                    EmitExpression(d.Init);
                    _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
                    EmitPatternFromLocal(d.Id, srcSlot, isDeclaration: true);
                }
            }
        }
    }

    /// <summary>§14.7.3 WhileStatement — emits a label-top / test / body /
    /// jump-back pattern, routing <c>break</c> and <c>continue</c> through
    /// the loop frame so nested jumps land at the right targets.</summary>
    private void EmitWhile(WhileStatement w)
    {
        var loop = new LoopFrame { TryDepthAtEntry = _tryDepth };
        _loops.Push(loop);
        var loopStart = _b.Position;
        EmitExpression(w.Test);
        var jzWhile = _b.EmitJump(Opcode.JumpIfFalse);
        EmitStatement(w.Body);
        // continue → loopStart.
        foreach (var p in loop.ContinuePatches) PatchBackwardJump(p, loopStart);
        var jBack = _b.EmitJump(Opcode.Jump);
        PatchBackwardJump(jBack, loopStart);
        _b.PatchJump(jzWhile);
        // break-target lands here.
        foreach (var p in loop.BreakPatches) _b.PatchJump(p);
        _loops.Pop();
    }

    /// <summary>§14.7.2 DoWhileStatement — body runs once before the test.
    /// <c>continue</c> jumps to the test; <c>break</c> jumps past the loop.</summary>
    private void EmitDoWhile(DoWhileStatement dw)
    {
        var loop = new LoopFrame { TryDepthAtEntry = _tryDepth };
        _loops.Push(loop);
        var loopStart = _b.Position;
        EmitStatement(dw.Body);
        // continue → test.
        var testPos = _b.Position;
        foreach (var p in loop.ContinuePatches) PatchBackwardJump(p, testPos);
        EmitExpression(dw.Test);
        var jBack = _b.EmitJump(Opcode.JumpIfTrue);
        PatchBackwardJump(jBack, loopStart);
        // break-target.
        foreach (var p in loop.BreakPatches) _b.PatchJump(p);
        _loops.Pop();
    }

    /// <summary>§14.7.4 ForStatement — C-style <c>for (init; test; update) body</c>.
    /// <c>continue</c> jumps to the update; <c>break</c> jumps past the loop.
    /// Init may be a VariableDeclaration (var/let/const), an Expression
    /// (evaluated for side effects), or null. Test and update are both
    /// optional; an absent test is treated as truthy.</summary>
    /// <remarks>
    /// Per §14.7.4.4 ForBodyEvaluation, <c>for (let|const i = ...; ...; ...)</c>
    /// creates a fresh binding per iteration (CreatePerIterationEnvironment).
    /// The compiler allocates a Cell-backed local slot for each let/const
    /// binding declared in the init, emits the init normally, and then
    /// emits <see cref="Opcode.RefreshLetBinding"/> at the top of every
    /// iteration (after init, before test) so closures formed inside the
    /// iteration body capture the iteration's own cell. <c>for (var i = ...)</c>
    /// is unchanged: a single binding for the whole loop.
    /// </remarks>
    private void EmitFor(ForStatement f)
    {
        _scopes.Add(new());

        // Per-iteration let/const bindings: route the init through a
        // Cell-backed local slot, then emit RefreshLetBinding at loopStart.
        List<int>? perIterSlots = null;
        if (f.Init is VariableDeclaration vdLet && (vdLet.Kind == "let" || vdLet.Kind == "const"))
        {
            perIterSlots = EmitForLetInit(vdLet);
        }
        else
        {
            switch (f.Init)
            {
                case null: break;
                case VariableDeclaration vd: EmitVarDecl(vd); break;
                case ExpressionStatement es:
                    EmitExpression(es.Expression);
                    _b.Emit(Opcode.Pop);
                    break;
                case Expression ie:
                    EmitExpression(ie);
                    _b.Emit(Opcode.Pop);
                    break;
                default:
                    throw new NotSupportedException(
                        $"for-loop init '{f.Init.GetType().Name}' not supported");
            }
        }

        // §14.7.4.4 step 2 — refresh once after init, before the first test.
        // The init's value is copied into a fresh cell so closures formed
        // in iteration 1's body see an iteration-local binding.
        if (perIterSlots is not null)
        {
            foreach (var slot in perIterSlots)
                _b.Emit(Opcode.RefreshLetBinding, (byte)slot);
        }

        var loop = new LoopFrame { TryDepthAtEntry = _tryDepth };
        _loops.Push(loop);

        var loopStart = _b.Position;
        int jExit = -1;
        if (f.Test is not null)
        {
            EmitExpression(f.Test);
            jExit = _b.EmitJump(Opcode.JumpIfFalse);
        }
        EmitStatement(f.Body);
        // continue → update site (which also re-refreshes the bindings, so a
        // `continue` from inside the body still gets per-iteration semantics
        // for the next round).
        var updatePos = _b.Position;
        foreach (var p in loop.ContinuePatches) PatchBackwardJump(p, updatePos);
        // §14.7.4.4 step 3c — refresh after body, before update. The current
        // iteration's mutations land in this iteration's cell (captured by
        // any closures formed in the body); a fresh cell is allocated for
        // the next iteration with the post-body value, and the update
        // operates on that fresh cell.
        if (perIterSlots is not null)
        {
            foreach (var slot in perIterSlots)
                _b.Emit(Opcode.RefreshLetBinding, (byte)slot);
        }
        if (f.Update is not null)
        {
            EmitExpression(f.Update);
            _b.Emit(Opcode.Pop);
        }
        var jBack = _b.EmitJump(Opcode.Jump);
        PatchBackwardJump(jBack, loopStart);
        if (jExit >= 0) _b.PatchJump(jExit);
        // break-target.
        foreach (var p in loop.BreakPatches) _b.PatchJump(p);
        _loops.Pop();
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>Allocate Cell-backed local slots for each identifier declared
    /// in a <c>for (let|const ... ; ... ; ...)</c> init, emit the init
    /// expressions, and return the slot list so the caller can emit
    /// <see cref="Opcode.RefreshLetBinding"/> at the top of each iteration.
    /// Unlike <see cref="EmitVarDecl"/>, this path bypasses the
    /// script-top-as-global routing: spec requires <c>let</c> in a for-loop
    /// init to be lexically scoped to the loop, never a global. Each
    /// binding's slot is unconditionally marked captured (cell-backed) so
    /// the refresh opcode can swap it per iteration without checking.
    /// Destructuring patterns in the init currently fall back to the regular
    /// path (no per-iteration semantics for the destructured names — rare
    /// in practice, tracked as a follow-up).</summary>
    private List<int> EmitForLetInit(VariableDeclaration vd)
    {
        var slots = new List<int>(vd.Declarations.Count);
        foreach (var d in vd.Declarations)
        {
            if (d.Id is not Identifier id)
            {
                // Destructuring init: route through the regular pattern path
                // and skip per-iteration semantics for now.
                DeclarePatternBindings(d.Id);
                if (d.Init is not null)
                {
                    var srcSlot = _b.ReserveLocal();
                    EmitExpression(d.Init);
                    _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
                    EmitPatternFromLocal(d.Id, srcSlot, isDeclaration: true);
                }
                continue;
            }

            // Allocate a Cell-backed slot in the for-loop's scope. Even at
            // script-top the binding is loop-local (not a global), per spec.
            var slot = _b.ReserveLocal();
            _scopes[^1][id.Name] = slot;
            _b.MarkCaptured(slot);
            _b.Emit(Opcode.InitCellLocal, (byte)slot);
            slots.Add(slot);

            if (d.Init is not null)
            {
                EmitExpression(d.Init);
                _b.Emit(Opcode.StoreCellLocal, (byte)slot);
            }
        }
        return slots;
    }

    /// <summary>Per-iteration let/const binding for for-in / for-of: allocate
    /// a Cell-backed slot for each identifier, declare it in the loop's
    /// scope (overriding any preallocation from <see cref="DeclarePatternBindings"/>),
    /// and return the slot list. Identifier-only LHS — destructuring on the
    /// for-of LHS is bound by <see cref="EmitForOfBinding"/> directly into
    /// these slots via the regular pattern path.</summary>
    private List<int> ReserveForOfLetSlots(VariableDeclaration vd)
    {
        var slots = new List<int>(vd.Declarations.Count);
        foreach (var d in vd.Declarations)
        {
            if (d.Id is not Identifier id) continue;
            var slot = _b.ReserveLocal();
            _scopes[^1][id.Name] = slot;
            _b.MarkCaptured(slot);
            _b.Emit(Opcode.InitCellLocal, (byte)slot);
            slots.Add(slot);
        }
        return slots;
    }

    /// <summary>
    /// §14.7.5 ForIn/OfBodyEvaluation — desugared to the spec iterator loop:
    /// <code>
    ///   handle = GetIterator(rhs)
    ///   loop:
    ///     step = IteratorStep(handle)   // pushed: iterator-result or undefined
    ///     if step === undefined goto done
    ///     value = step.value
    ///     &lt;bind value to LHS pattern/identifier&gt;
    ///     &lt;body&gt;
    ///     goto loop
    ///   done:
    ///     pop handle
    ///     IteratorClose(handle)         // no-op when record.Done is true
    /// </code>
    /// <c>break</c> jumps to the cleanup path so <c>IteratorClose</c> still
    /// fires (invoking the iterator's <c>return()</c> if defined, per
    /// §7.4.10). <c>continue</c> jumps to the next IteratorStep.
    /// </summary>
    private void EmitForOf(ForOfStatement fo)
    {
        // Open a fresh scope so loop-var bindings don't leak.
        _scopes.Add(new());

        // Step 1: evaluate the iterable + materialise an iterator-record handle.
        EmitExpression(fo.Right);
        _b.Emit(Opcode.GetIterator);
        var handleSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)handleSlot);

        // Pre-declare any identifiers introduced by the LHS so the body sees
        // them as locals. For VariableDeclaration we declare each binding;
        // for a bare AssignmentTarget we leave the resolver to fall through
        // to the parent scope. Per §14.7.4.4, let/const bindings get per-
        // iteration semantics: allocate Cell-backed slots and refresh each
        // iteration so closures in the body capture an iteration-local cell.
        List<int>? perIterSlots = null;
        if (fo.Left is VariableDeclaration vd0)
        {
            if (vd0.Kind == "let" || vd0.Kind == "const")
            {
                perIterSlots = ReserveForOfLetSlots(vd0);
                // Destructuring patterns and any non-identifier LHS still
                // need the regular declare path; ReserveForOfLetSlots skips
                // those, so call DeclarePatternBindings as a fallback.
                foreach (var d in vd0.Declarations)
                    if (d.Id is not Identifier) DeclarePatternBindings(d.Id);
            }
            else
            {
                foreach (var d in vd0.Declarations) DeclarePatternBindings(d.Id);
            }
        }

        var loop = new LoopFrame { TryDepthAtEntry = _tryDepth };
        _loops.Push(loop);

        var loopStart = _b.Position;
        _b.Emit(Opcode.LoadLocal, (byte)handleSlot);
        _b.Emit(Opcode.IteratorStep);
        // Stack top is iterator-result-object or undefined. Compare with
        // undefined; jump to done when so.
        _b.Emit(Opcode.Dup);
        _b.Emit(Opcode.LoadUndefined);
        _b.Emit(Opcode.StrictEq);
        var jExit = _b.EmitJump(Opcode.JumpIfTrue);
        // CreatePerIterationEnvironment — refresh let/const bindings before
        // the iteration's value is stored into them.
        if (perIterSlots is not null)
        {
            foreach (var slot in perIterSlots)
                _b.Emit(Opcode.RefreshLetBinding, (byte)slot);
        }
        // Stack: [iterResult]. Extract .value.
        _b.EmitU16(Opcode.LoadProperty, _b.AddConstant("value"));
        // Stack: [value]. Bind to LHS.
        EmitForOfBinding(fo.Left);
        // Body.
        EmitStatement(fo.Body);
        // continue → loopStart (re-fetch via IteratorStep).
        foreach (var p in loop.ContinuePatches) PatchBackwardJump(p, loopStart);
        var jBack = _b.EmitJump(Opcode.Jump);
        PatchBackwardJump(jBack, loopStart);

        // break-target: enters the cleanup path WITHOUT the stale sentinel
        // (break jumps from inside the body where the stack is balanced).
        // Cleanup is duplicated from the normal-exit path below but with no
        // initial Pop.
        foreach (var p in loop.BreakPatches) _b.PatchJump(p);
        _b.Emit(Opcode.LoadLocal, (byte)handleSlot);
        _b.Emit(Opcode.IteratorClose);
        var jPastNormal = _b.EmitJump(Opcode.Jump);

        _b.PatchJump(jExit);
        // Stack on exit: [undefined-sentinel]. Pop it.
        _b.Emit(Opcode.Pop);
        // IteratorClose on normal completion is a no-op (record.Done=true)
        // but we emit the opcode so abrupt completions stack into the
        // cleanup path uniformly.
        _b.Emit(Opcode.LoadLocal, (byte)handleSlot);
        _b.Emit(Opcode.IteratorClose);

        _b.PatchJump(jPastNormal);
        _loops.Pop();
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>§14.7.5 ForIn — iterate enumerable string keys of the
    /// right-hand side (own + inherited, dedup'd). The key set is snapshotted
    /// at loop entry per spec, so mutations during iteration don't appear.</summary>
    private void EmitForIn(ForInStatement fi)
    {
        _scopes.Add(new());

        // Materialize the key snapshot. EnumerateKeys handles null/undefined
        // by yielding an empty array (spec: silently skip the loop body).
        EmitExpression(fi.Right);
        _b.Emit(Opcode.EnumerateKeys);
        var keysSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)keysSlot);

        // Iteration counter.
        var iSlot = _b.ReserveLocal();
        _b.Emit(Opcode.LoadZero);
        _b.Emit(Opcode.StoreLocal, (byte)iSlot);

        // Pre-declare LHS bindings. Per §14.7.4.4 CreatePerIterationEnvironment,
        // let/const bindings get a fresh slot per iteration.
        List<int>? perIterSlots = null;
        if (fi.Left is VariableDeclaration vd0)
        {
            if (vd0.Kind == "let" || vd0.Kind == "const")
            {
                perIterSlots = ReserveForOfLetSlots(vd0);
                foreach (var d in vd0.Declarations)
                    if (d.Id is not Identifier) DeclarePatternBindings(d.Id);
            }
            else
            {
                foreach (var d in vd0.Declarations) DeclarePatternBindings(d.Id);
            }
        }

        var loop = new LoopFrame { TryDepthAtEntry = _tryDepth };
        _loops.Push(loop);

        var loopStart = _b.Position;
        // if (i >= keys.length) break.
        _b.Emit(Opcode.LoadLocal, (byte)iSlot);
        _b.Emit(Opcode.LoadLocal, (byte)keysSlot);
        _b.EmitU16(Opcode.LoadProperty, _b.AddConstant("length"));
        _b.Emit(Opcode.GtEq);
        var jExit = _b.EmitJump(Opcode.JumpIfTrue);

        // CreatePerIterationEnvironment — refresh let/const bindings before
        // the iteration's key is stored into them.
        if (perIterSlots is not null)
        {
            foreach (var slot in perIterSlots)
                _b.Emit(Opcode.RefreshLetBinding, (byte)slot);
        }

        // key = keys[i].
        _b.Emit(Opcode.LoadLocal, (byte)keysSlot);
        _b.Emit(Opcode.LoadLocal, (byte)iSlot);
        _b.Emit(Opcode.LoadComputed);
        // Bind to LHS.
        EmitForOfBinding(fi.Left);
        // Body.
        EmitStatement(fi.Body);
        // continue → increment.
        var incPos = _b.Position;
        foreach (var p in loop.ContinuePatches) PatchBackwardJump(p, incPos);
        // i++ (use load/add/store; the Inc-update path is not exposed here).
        _b.Emit(Opcode.LoadLocal, (byte)iSlot);
        _b.EmitU16(Opcode.LoadConst, _b.AddConstant((double)1));
        _b.Emit(Opcode.Add);
        _b.Emit(Opcode.StoreLocal, (byte)iSlot);
        var jBack = _b.EmitJump(Opcode.Jump);
        PatchBackwardJump(jBack, loopStart);

        _b.PatchJump(jExit);
        foreach (var p in loop.BreakPatches) _b.PatchJump(p);
        _loops.Pop();
        _scopes.RemoveAt(_scopes.Count - 1);
    }

    /// <summary>B7-followup-b — emit a forward <see cref="Opcode.Jump"/> for
    /// <c>break</c> or <c>continue</c> and record the patch position on the
    /// innermost open loop frame. Labeled break/continue is a follow-up
    /// (queued under the M3-03 labeled-statement work).</summary>
    /// <remarks>
    /// The cross-finally case (a break/continue whose target loop sits
    /// outside an enclosing try/finally) is NOT yet supported — spec wants
    /// the intervening finally blocks to run first, mirroring the
    /// DivertReturnThroughFinally path. For now the compiler throws so
    /// callers see a clear error instead of silently skipping a finally.
    /// </remarks>
    private void EmitBreakOrContinue(Starling.Js.Lex.JsPosition where, bool isBreak, string? label)
    {
        if (label is not null)
            throw new NotSupportedException(
                $"labeled '{(isBreak ? "break" : "continue")} {label}' is not yet supported (compiler at {where.Line}:{where.Column}).");
        if (_loops.Count == 0)
            throw new InvalidOperationException(
                $"SyntaxError: Illegal {(isBreak ? "break" : "continue")} statement at {where.Line}:{where.Column} (must be inside a loop).");
        var loop = _loops.Peek();
        if (_tryDepth > loop.TryDepthAtEntry)
            throw new NotSupportedException(
                $"'{(isBreak ? "break" : "continue")}' across an enclosing try/finally is not yet supported (compiler at {where.Line}:{where.Column}).");
        var patch = _b.EmitJump(Opcode.Jump);
        (isBreak ? loop.BreakPatches : loop.ContinuePatches).Add(patch);
    }

    private void EmitForOfBinding(AstNode left)
    {
        // Stack top: [value]. Consume it into LHS.
        switch (left)
        {
            case VariableDeclaration vd:
                if (vd.Declarations.Count != 1)
                    throw new NotSupportedException("for…of binding requires exactly one declarator");
                var d = vd.Declarations[0];
                if (d.Id is Identifier id)
                {
                    StoreBindingIdentifier(id.Name);
                }
                else
                {
                    EmitPatternFromStack(d.Id, isDeclaration: true);
                }
                return;
            case Identifier id2:
                StoreBindingIdentifier(id2.Name);
                return;
            case MemberExpression me:
                StoreMemberTarget(me);
                return;
            case Expression pattern:
                EmitPatternFromStack(pattern, isDeclaration: false);
                return;
            default:
                throw new NotSupportedException($"for…of LHS '{left.GetType().Name}' not supported");
        }
    }

    private void PatchBackwardJump(int operandPos, int target)
    {
        var jumpFrom = operandPos + 2;
        var delta = target - jumpFrom;
        if (delta is < short.MinValue or > short.MaxValue)
            throw new InvalidOperationException("backward jump overflows i16");
        // ChunkBuilder doesn't expose direct patch for arbitrary positions
        // except via PatchJump (which patches to _current_ position). Reuse
        // the same arithmetic but go through a small helper that writes
        // bytes directly.
        WriteI16(operandPos, (short)delta);
    }

    private void WriteI16(int pos, short value)
    {
        // ChunkBuilder hides its byte list; expose patching by going through
        // PatchJump-style logic. For backward jumps we need raw access, so
        // add a tiny accessor.
        _b.PatchI16(pos, value);
    }

    // -----------------------------------------------------------------------
    // Expressions
    // -----------------------------------------------------------------------

    private void EmitExpression(Expression e)
    {
        switch (e)
        {
            case NumericLiteral n:
                if (n.Value == 0) _b.Emit(Opcode.LoadZero);
                else
                {
                    var idx = _b.AddConstant(n.Value);
                    _b.EmitU16(Opcode.LoadConst, idx);
                }
                return;
            case StringLiteral s:
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(s.Value));
                return;
            case BooleanLiteral b:
                _b.Emit(b.Value ? Opcode.LoadTrue : Opcode.LoadFalse);
                return;
            case NullLiteral:
                _b.Emit(Opcode.LoadNull);
                return;
            case BigIntLiteral bi:
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(new JsBigIntPlaceholder(bi.Value)));
                return;
            case RegExpLiteral rx:
                // Source + flags live as string constants; the VM compiles
                // and wraps a fresh JsRegExp per execution so each evaluation
                // gets an independent `lastIndex` slot (§13.2.7.3).
                _b.EmitU16(Opcode.LoadRegExp, _b.AddConstant(rx.Source));
                _b.EmitU16Raw(_b.AddConstant(rx.Flags));
                return;
            case Identifier id:
                EmitIdLoad(id.Name);
                return;
            case ThisExpression:
                _b.Emit(_classMethodDepth > 0 ? Opcode.LoadThisChecked : Opcode.LoadThis);
                return;
            case BinaryExpression bin:
                EmitExpression(bin.Left);
                EmitExpression(bin.Right);
                _b.Emit(BinaryOpToOpcode(bin.Op));
                return;
            case LogicalExpression log:
                EmitLogical(log);
                return;
            case UnaryExpression u:
                if (u.Op == "delete")
                {
                    EmitDelete(u);
                    return;
                }
                if (u.Op == "void")
                {
                    EmitExpression(u.Argument);
                    _b.Emit(Opcode.Pop);
                    _b.Emit(Opcode.LoadUndefined);
                    return;
                }
                EmitExpression(u.Argument);
                _b.Emit(UnaryOpToOpcode(u.Op));
                return;
            case UpdateExpression up:
                EmitUpdate(up);
                return;
            case AssignmentExpression a:
                EmitAssignment(a);
                return;
            case ConditionalExpression c:
                EmitExpression(c.Test);
                var jzCond = _b.EmitJump(Opcode.JumpIfFalse);
                EmitExpression(c.Consequent);
                var jend = _b.EmitJump(Opcode.Jump);
                _b.PatchJump(jzCond);
                EmitExpression(c.Alternate);
                _b.PatchJump(jend);
                return;
            case MemberExpression m:
                EmitMemberLoad(m);
                return;
            case CallExpression call:
                EmitCall(call);
                return;
            case NewExpression ne:
                EmitNew(ne);
                return;
            case ObjectExpression oe:
                EmitObjectLiteral(oe);
                return;
            case ArrayExpression ae:
                EmitArrayLiteral(ae);
                return;
            case FunctionExpression fe:
                EmitFunctionExpression(fe);
                return;
            case SequenceExpression seq:
                for (var i = 0; i < seq.Expressions.Count - 1; i++)
                {
                    EmitExpression(seq.Expressions[i]);
                    _b.Emit(Opcode.Pop);
                }
                EmitExpression(seq.Expressions[^1]);
                return;
            case TemplateLiteral tpl:
                EmitTemplateLiteral(tpl);
                return;
            case ArrowFunctionExpression arrow:
                EmitArrowFunction(arrow);
                return;
            case YieldExpression yld:
                EmitYield(yld);
                return;
            case AwaitExpression aw:
                EmitAwait(aw);
                return;
            case ClassExpression cls:
                EmitClassExpression(cls);
                return;
            case SuperPropertyExpression sp:
                EmitSuperProperty(sp);
                return;
            case SuperCallExpression sc:
                EmitSuperCall(sc);
                return;
            case PrivateNameExpression:
                throw new NotSupportedException(
                    "private-name reference used outside a member-expression context");
        }
        throw new NotSupportedException(
            $"compiler: expression kind '{e.GetType().Name}' not yet supported.");
    }

    /// <summary>
    /// Desugar template literal to left-fold string concat:
    /// `q0${e0}q1${e1}q2` → q0 + e0 + q1 + e1 + q2 (with explicit ToString
    /// coercion of substitution values implicit in JS '+'). Empty quasi
    /// segments are skipped.
    /// </summary>
    private void EmitTemplateLiteral(TemplateLiteral tpl)
    {
        // Always start with the first quasi (which can be "") to anchor as a string —
        // ensures arithmetic '+' on subsequent operands becomes string concat.
        _b.EmitU16(Opcode.LoadConst, _b.AddConstant(tpl.Quasis[0]));
        for (var i = 0; i < tpl.Expressions.Count; i++)
        {
            EmitExpression(tpl.Expressions[i]);
            _b.Emit(Opcode.Add);
            if (tpl.Quasis[i + 1].Length > 0)
            {
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(tpl.Quasis[i + 1]));
                _b.Emit(Opcode.Add);
            }
        }
    }

    /// <summary>
    /// Arrow functions desugar to a regular function for compilation. Their
    /// only semantic differences (lexical <c>this</c> + no <c>arguments</c>)
    /// are wired through closure analysis in B1b-2 when class methods land.
    /// Until then, the body is compiled identically and <c>this</c> rebinding
    /// is a known limitation tracked in tests.
    /// </summary>
    private void EmitArrowFunction(ArrowFunctionExpression arrow)
    {
        BlockStatement body = arrow.Body switch
        {
            BlockStatement b => b,
            Expression expr => new BlockStatement(
                [new ReturnStatement(expr, arrow.Start, arrow.End)],
                arrow.Start, arrow.End),
            _ => throw new InvalidOperationException("arrow body must be block or expression"),
        };
        var fe = BuildFunctionExpressionShim(null, arrow.Params, body, arrow.Start, arrow.End,
            isAsync: arrow.Async, isGenerator: arrow.Generator);
        EmitFunctionExpression(fe);
    }

    private static FunctionExpression BuildFunctionExpressionShim(
        Identifier? name, IReadOnlyList<Expression> @params, BlockStatement body,
        Starling.Js.Lex.JsPosition start, Starling.Js.Lex.JsPosition end,
        bool isAsync = false, bool isGenerator = false)
        => new(name, @params, body, Generator: isGenerator, start, end, Async: isAsync);

    private void EmitIdLoad(string name)
    {
        if (TryResolveLocal(name, out var slot))
        {
            EmitLoadLocalSlot(slot);
            return;
        }
        if (TryResolveUpvalue(name, out var upIdx))
        {
            // gap:closure-write-back — every upvalue is a Cell now, so
            // LoadUpvalue dereferences it transparently.
            _b.Emit(Opcode.LoadUpvalue, (byte)upIdx);
            return;
        }
        _b.EmitU16(Opcode.LoadGlobal, _b.AddConstant(name));
    }

    private bool TryResolveLocal(string name, out int slot)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out slot)) return true;
        slot = -1;
        return false;
    }

    /// <summary>
    /// Lazy upvalue resolution per §10.2.1.2. Walks the enclosing
    /// compiler chain: if a parent has <paramref name="name"/> as a
    /// local, record the capture as <c>IsLocalCapture=true</c> with the
    /// parent's slot index; if the parent has it transitively (i.e. it
    /// also captured the name), record <c>IsLocalCapture=false</c>
    /// pointing at the parent's upvalue index. Returns the index in
    /// this compiler's <see cref="_upvalues"/> table.
    /// </summary>
    private bool TryResolveUpvalue(string name, out int upIdx)
    {
        upIdx = -1;
        if (_parent is null) return false;

        // Already captured by this function — reuse the same slot so
        // multiple reads of the same name share one upvalue.
        if (_upvalueByName.TryGetValue(name, out upIdx)) return true;

        if (_parent.TryResolveLocal(name, out var parentSlot))
        {
            upIdx = AddUpvalue(name, new UpvalueRef(IsLocalCapture: true, Index: parentSlot));
            return true;
        }
        if (_parent.TryResolveUpvalue(name, out var parentUp))
        {
            upIdx = AddUpvalue(name, new UpvalueRef(IsLocalCapture: false, Index: parentUp));
            return true;
        }
        return false;
    }

    private int AddUpvalue(string name, UpvalueRef u)
    {
        if (_upvalues.Count >= 255)
            throw new NotSupportedException("more than 255 upvalues per function not supported");
        var idx = _upvalues.Count;
        _upvalues.Add(u);
        _upvalueByName[name] = idx;
        return idx;
    }

    private void EmitLogical(LogicalExpression log)
    {
        EmitExpression(log.Left);
        // Duplicate so the short-circuit value remains on the stack.
        _b.Emit(Opcode.Dup);
        Opcode jmp = log.Op switch
        {
            "&&" => Opcode.JumpIfFalse,
            "||" => Opcode.JumpIfTrue,
            "??" => Opcode.JumpIfNotNullish,
            _ => throw new NotSupportedException(log.Op),
        };
        var jumpAddr = _b.EmitJump(jmp);
        _b.Emit(Opcode.Pop); // discard the duplicated left-hand value
        EmitExpression(log.Right);
        _b.PatchJump(jumpAddr);
    }

    private void EmitUpdate(UpdateExpression up)
    {
        if (up.Argument is Identifier id)
        {
            // gap:closure-write-back — try local first, then upvalue, then global.
            if (TryResolveLocal(id.Name, out var slot))
            {
                EmitLoadLocalSlot(slot);
                if (!up.Prefix) _b.Emit(Opcode.Dup);
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(1.0));
                _b.Emit(up.Op == "++" ? Opcode.Add : Opcode.Sub);
                if (up.Prefix) _b.Emit(Opcode.Dup);
                EmitStoreLocalSlot(slot);
                return;
            }
            if (TryResolveUpvalue(id.Name, out var upIdx))
            {
                _b.Emit(Opcode.LoadUpvalue, (byte)upIdx);
                if (!up.Prefix) _b.Emit(Opcode.Dup);
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(1.0));
                _b.Emit(up.Op == "++" ? Opcode.Add : Opcode.Sub);
                if (up.Prefix) _b.Emit(Opcode.Dup);
                _b.Emit(Opcode.StoreUpvalue, (byte)upIdx);
                return;
            }
            // gap:script-top-var-not-global — `x++` where `x` is a global
            // (e.g. a script-top `var`) does Load, ±1, Store through the
            // global object. The `Dup` ordering mirrors the local/upvalue
            // arms above so postfix returns the pre-update value and prefix
            // returns the post-update value.
            var nameIdx = _b.AddConstant(id.Name);
            _b.EmitU16(Opcode.LoadGlobal, nameIdx);
            if (!up.Prefix) _b.Emit(Opcode.Dup);
            _b.EmitU16(Opcode.LoadConst, _b.AddConstant(1.0));
            _b.Emit(up.Op == "++" ? Opcode.Add : Opcode.Sub);
            if (up.Prefix) _b.Emit(Opcode.Dup);
            _b.EmitU16(Opcode.StoreGlobal, nameIdx);
            return;
        }
        throw new NotSupportedException("update target must be identifier in this slice");
    }

    /// <summary>gap:delete — lower <c>delete expr</c> per §13.5.1. For
    /// member targets, evaluate the receiver + key and emit
    /// <see cref="Opcode.DeleteProperty"/>. For non-Reference targets
    /// (plain identifiers, literals, parenthesized expressions), the spec
    /// says evaluate-for-side-effects then return <c>true</c> (sloppy mode);
    /// our engine does not enforce strict mode globally so unqualified
    /// <c>delete x</c> is a no-op that yields <c>true</c>.</summary>
    private void EmitDelete(UnaryExpression u)
    {
        if (u.Argument is MemberExpression me)
        {
            // Reject `delete super.x` — §13.5.1.2 throws SyntaxError.
            if (me.Object is SuperPropertyExpression)
                throw new NotSupportedException("delete of super property is a SyntaxError");
            // Private fields cannot be deleted (early error per §13.5.1).
            if (!me.Computed && me.Property is PrivateNameExpression)
                throw new NotSupportedException("delete of a private field is a SyntaxError");
            EmitExpression(me.Object);
            if (me.Computed) EmitExpression(me.Property);
            else _b.EmitU16(Opcode.LoadConst, _b.AddConstant(((Identifier)me.Property).Name));
            _b.Emit(Opcode.DeleteProperty);
            return;
        }
        // Non-Reference: evaluate the operand for side effects, drop, push true.
        // (Identifier deletes are also routed here — they are reference-of-an-
        // environment-record per spec; sloppy mode returns true.)
        if (u.Argument is Identifier)
        {
            _b.Emit(Opcode.LoadTrue);
            return;
        }
        EmitExpression(u.Argument);
        _b.Emit(Opcode.Pop);
        _b.Emit(Opcode.LoadTrue);
    }

    private void EmitAssignment(AssignmentExpression a)
    {
        if (IsPattern(a.Target))
        {
            // §13.15.1: a destructuring (array/object) assignment target only
            // pairs with the plain `=` operator — a compound operator such as
            // `[a] += x` is an early SyntaxError.
            if (a.Op != "=") throw new NotSupportedException("compound assignment with a destructuring target is a SyntaxError");
            // ECMA-262 §13.15 destructuring assignment evaluates the RHS once,
            // performs the pattern writes, and the whole expression returns the RHS.
            var rhsSlot = _b.ReserveLocal();
            EmitExpression(a.Value);
            _b.Emit(Opcode.StoreLocal, (byte)rhsSlot);
            EmitPatternFromLocal(a.Target, rhsSlot, isDeclaration: false);
            _b.Emit(Opcode.LoadLocal, (byte)rhsSlot);
            return;
        }
        if (a.Target is Identifier id)
        {
            if (a.Op != "=")
            {
                // Compound: load + apply binary + store.
                EmitIdLoad(id.Name);
                EmitExpression(a.Value);
                _b.Emit(CompoundOpToBinaryOpcode(a.Op));
            }
            else
            {
                EmitExpression(a.Value);
            }
            _b.Emit(Opcode.Dup); // assignment is an expression — leaves value on stack
            // gap:closure-write-back — assigning to a captured upvalue must
            // route through the shared cell; assignment to a captured local
            // routes through StoreCellLocal.
            if (TryResolveLocal(id.Name, out var slot))
                EmitStoreLocalSlot(slot);
            else if (TryResolveUpvalue(id.Name, out var upIdx))
                _b.Emit(Opcode.StoreUpvalue, (byte)upIdx);
            else
                _b.EmitU16(Opcode.StoreGlobal, _b.AddConstant(id.Name));
            return;
        }
        if (a.Target is MemberExpression me)
        {
            // Super-property assignment: super.x = v writes to `this`.
            if (me.Object is SuperPropertyExpression)
                throw new NotSupportedException("super.x = v is not supported in B1b-2a");
            // Private name assignment: this.#x = v
            if (!me.Computed && me.Property is PrivateNameExpression pne)
            {
                var mangled = ResolvePrivateName(pne.Name);
                EmitExpression(me.Object);
                EmitExpression(a.Value);
                if (a.Op != "=")
                    throw new NotSupportedException("compound assignment to private field not supported");
                _b.EmitU16(Opcode.PrivateSet, _b.AddConstant(mangled));
                return;
            }
            // gap:compound-assign-property — for compound forms (`obj.x += v`,
            // `obj[k] *= v`, …) the spec (§13.15.2) evaluates the base
            // **once**, reads the property, applies the op, then writes
            // back to the same base/key. Previously we re-emitted the
            // member expression on the write side, which evaluated the
            // base (and any computed-key side effects) a second time and
            // could route the write to the wrong place when the base
            // expression was non-pure. Fix: dup the resolved base (+ key
            // for computed) before the read, then reuse those dup'd
            // values on the store side.
            if (a.Op != "=")
            {
                EmitExpression(me.Object);
                if (me.Computed)
                {
                    EmitExpression(me.Property);
                    _b.Emit(Opcode.Dup2);                 // base, key, base, key
                    _b.Emit(Opcode.LoadComputed);         // base, key, oldVal
                    EmitExpression(a.Value);              // base, key, oldVal, rhs
                    _b.Emit(CompoundOpToBinaryOpcode(a.Op)); // base, key, newVal
                    _b.Emit(Opcode.StoreComputed);        // newVal
                }
                else
                {
                    _b.Emit(Opcode.Dup);                  // base, base
                    var nameIdx = _b.AddConstant(((Identifier)me.Property).Name);
                    _b.EmitU16(Opcode.LoadProperty, nameIdx); // base, oldVal
                    EmitExpression(a.Value);              // base, oldVal, rhs
                    _b.Emit(CompoundOpToBinaryOpcode(a.Op)); // base, newVal
                    _b.EmitU16(Opcode.StoreProperty, nameIdx); // newVal
                }
                return;
            }
            // Plain `obj.x = v` / `obj[k] = v`.
            // StoreProperty / StoreComputed already re-push the assigned
            // value, so the expression's net result is one value on the
            // stack — no extra Dup needed (it would leak a value).
            EmitExpression(me.Object);
            if (me.Computed) EmitExpression(me.Property);
            EmitExpression(a.Value);
            if (me.Computed) _b.Emit(Opcode.StoreComputed);
            else _b.EmitU16(Opcode.StoreProperty, _b.AddConstant(((Identifier)me.Property).Name));
            return;
        }
        // Array/object destructuring assignment targets are handled by the
        // IsPattern branch above; member and identifier targets by the branches
        // in between. Anything reaching here (e.g. `f() = x`, `1 = x`) is an
        // invalid assignment target — §13.15.1 makes these early SyntaxErrors.
        throw new NotSupportedException($"invalid assignment target '{a.Target.GetType().Name}'");
    }

    private void EmitMemberLoad(MemberExpression m)
    {
        // Private name: obj.#name
        if (!m.Computed && m.Property is PrivateNameExpression pne)
        {
            var mangled = ResolvePrivateName(pne.Name);
            EmitExpression(m.Object);
            _b.EmitU16(Opcode.PrivateGet, _b.AddConstant(mangled));
            return;
        }
        EmitExpression(m.Object);
        if (m.Computed)
        {
            EmitExpression(m.Property);
            _b.Emit(Opcode.LoadComputed);
        }
        else
        {
            var name = ((Identifier)m.Property).Name;
            _b.EmitU16(Opcode.LoadProperty, _b.AddConstant(name));
        }
    }

    private void EmitCall(CallExpression call)
    {
        if (call.Arguments.Count > 255)
            throw new NotSupportedException("more than 255 call args not supported");

        var hasSpread = false;
        foreach (var arg in call.Arguments)
            if (arg is SpreadElement) { hasSpread = true; break; }

        // super.method(args) — must bind this=current this.
        if (call.Callee is SuperPropertyExpression sp)
        {
            if (sp.Computed)
                throw new NotSupportedException("computed super[...] is not supported in B1b-2a");
            var name = ((Identifier)sp.Property).Name;
            // Push this for receiver, then the resolved super method.
            _b.Emit(_classMethodDepth > 0 ? Opcode.LoadThisChecked : Opcode.LoadThis);  // [this]
            _b.EmitU16(Opcode.LoadSuperProperty, _b.AddConstant(name));                 // [this, fn]
            if (hasSpread)
            {
                EmitArgsAsArray(call.Arguments);
                _b.Emit(Opcode.CallApplyMethod);
                return;
            }
            foreach (var arg in call.Arguments) EmitExpression(arg);
            _b.Emit(Opcode.CallMethod, (byte)call.Arguments.Count);
            return;
        }

        // Method call form: obj.method() or obj[key]() must bind
        // this=obj inside the callee. Emit obj once, Dup it, load the
        // property, then args, then CallMethod which consumes
        // [receiver, callee, args...]. For plain calls, emit the callee
        // alone and route through Call (this=Undefined).
        if (!call.Optional && call.Callee is MemberExpression me)
        {
            EmitExpression(me.Object);          // [obj]
            _b.Emit(Opcode.Dup);                // [obj, obj]
            if (me.Computed)
            {
                EmitExpression(me.Property);    // [obj, obj, key]
                _b.Emit(Opcode.LoadComputed);   // [obj, fn]
            }
            else if (me.Property is PrivateNameExpression pne)
            {
                var mangled = ResolvePrivateName(pne.Name);
                _b.EmitU16(Opcode.PrivateGet, _b.AddConstant(mangled));  // [obj, fn]
            }
            else
            {
                var nameIdx = _b.AddConstant(((Identifier)me.Property).Name);
                _b.EmitU16(Opcode.LoadProperty, nameIdx);  // [obj, fn]
            }
            if (hasSpread)
            {
                // Build args array first, then apply.
                EmitArgsAsArray(call.Arguments);
                _b.Emit(Opcode.CallApplyMethod);
                return;
            }
            foreach (var arg in call.Arguments) EmitExpression(arg);
            _b.Emit(Opcode.CallMethod, (byte)call.Arguments.Count);
            return;
        }

        EmitExpression(call.Callee);
        if (hasSpread)
        {
            EmitArgsAsArray(call.Arguments);
            _b.Emit(Opcode.CallApply);
            return;
        }
        foreach (var arg in call.Arguments) EmitExpression(arg);
        _b.Emit(Opcode.Call, (byte)call.Arguments.Count);
    }

    /// <summary>Materialise a possibly-spread argument list as a dense
    /// JsArray on the stack. Used by the CallApply / NewApply opcodes when
    /// at least one argument is a spread.</summary>
    private void EmitArgsAsArray(IReadOnlyList<Expression> args)
    {
        _b.Emit(Opcode.NewArray);
        var pushMode = false;
        var nextIdx = 0;
        foreach (var arg in args)
        {
            if (arg is SpreadElement sp)
            {
                EmitExpression(sp.Argument);    // [arr, iterable]
                _b.Emit(Opcode.SpreadIterable); // [arr]
                pushMode = true;
                continue;
            }
            if (!pushMode)
            {
                _b.Emit(Opcode.Dup);            // [arr, arr]
                EmitExpression(arg);            // [arr, arr, v]
                _b.EmitU16(Opcode.StoreProperty,
                    _b.AddConstant(nextIdx.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _b.Emit(Opcode.Pop);            // [arr]
                nextIdx++;
            }
            else
            {
                _b.Emit(Opcode.Dup);            // [arr, arr]
                _b.Emit(Opcode.Dup);            // [arr, arr, arr]
                _b.EmitU16(Opcode.LoadProperty, _b.AddConstant("length")); // [arr, arr, len]
                EmitExpression(arg);            // [arr, arr, len, v]
                _b.Emit(Opcode.StoreComputed);  // [arr, v]
                _b.Emit(Opcode.Pop);            // [arr]
            }
        }
    }

    private void EmitFunctionExpression(FunctionExpression fe)
    {
        // Compile the body in a sub-compiler parented to this one so
        // free identifiers can be lazily resolved as upvalues captured
        // from this scope.
        var sub = new JsCompiler(parent: this);
        sub.RunCaptureAnalysisForFunction(fe.Params, fe.Body.Body);
        sub.BindFunctionParameters(fe.Params);
        foreach (var s in fe.Body.Body) sub.EmitStatement(s);
        sub._b.Emit(Opcode.ReturnUndefined);
        // Per ES2024 §15.2 NamedEvaluation, anonymous FunctionExpression
        // produces `name === ""` (not "<anonymous>"); B2-2's Function intrinsic
        // surfaces this through the new-instance `name` slot.
        var name = fe.Name?.Name ?? "";
        var chunk = sub._b.Build(name);
        var kind = ResolveFunctionKind(fe.Async, fe.Generator);
        EmitFunctionConstructor(name, chunk, CountSimpleParams(fe.Params), sub._upvalues, kind);
    }

    /// <summary>B1b-2c — emit <c>yield expr</c> / <c>yield</c> / <c>yield* iter</c>.
    /// Pushes the value to yield, then emits a Suspend opcode (kind=0).
    /// On resume, the value passed to <c>.next(v)</c> is pushed back.</summary>
    private void EmitYield(YieldExpression yld)
    {
        if (yld.Delegate)
        {
            // yield* iter — push the iterable and hand off to the VM-level
            // YieldDelegate opcode, which implements §27.5.3.2 in full:
            // it forwards the outer generator's next/return/throw into the
            // inner iterator's matching method, exits when the inner says
            // done, and pushes the inner's value as the result of the
            // yield* expression.
            EmitExpression(yld.Argument!);
            _b.Emit(Opcode.YieldDelegate);
            return;
        }

        // Simple yield: push the value to yield (or undefined), then Suspend.
        if (yld.Argument is not null) EmitExpression(yld.Argument);
        else _b.Emit(Opcode.LoadUndefined);
        _b.Emit(Opcode.Suspend);
        _b.EmitU8Raw(0); // kind = 0 (yield)
        // After resume, stack-top holds the value passed to .next(v); that
        // becomes the result of the yield expression.
    }

    /// <summary>B1b-2c — emit <c>await expr</c>. Pushes the awaited value,
    /// then Suspend with kind=1. On resume, the resolved value is on top
    /// (or a JsThrow propagates if the promise rejected).</summary>
    private void EmitAwait(AwaitExpression aw)
    {
        EmitExpression(aw.Argument);
        _b.Emit(Opcode.Suspend);
        _b.EmitU8Raw(1); // kind = 1 (await)
    }

    /// <summary>B1b-2c — map AST async/generator flags to runtime kind.</summary>
    private static Runtime.JsFunctionKind ResolveFunctionKind(bool isAsync, bool isGenerator)
    {
        if (isAsync && isGenerator) return Runtime.JsFunctionKind.AsyncGenerator;
        if (isAsync) return Runtime.JsFunctionKind.Async;
        if (isGenerator) return Runtime.JsFunctionKind.Generator;
        return Runtime.JsFunctionKind.Normal;
    }

    private void EmitObjectLiteral(ObjectExpression oe)
    {
        _b.Emit(Opcode.NewObject); // [obj]
        foreach (var prop in oe.Properties)
        {
            // Spread: copy enumerable own properties from the source onto obj.
            // The parser tags spreads with a SpreadElement value and an empty
            // sentinel identifier key.
            if (prop.Value is SpreadElement spread)
            {
                _b.Emit(Opcode.Dup);               // [obj, obj]
                EmitExpression(spread.Argument);   // [obj, obj, src]
                _b.Emit(Opcode.SpreadInto);        // [obj]
                continue;
            }
            // Pattern per property: keep obj on stack, store, discard the
            // value-clone that StoreProperty/Computed re-pushes.
            _b.Emit(Opcode.Dup); // [obj, obj]
            if (prop.Computed)
            {
                EmitExpression(prop.Key);       // [obj, obj, key]
                EmitExpression(prop.Value);     // [obj, obj, key, value]
                _b.Emit(Opcode.StoreComputed);  // [obj, value]
            }
            else
            {
                var nameIdx = prop.Key switch
                {
                    Identifier id => _b.AddConstant(id.Name),
                    StringLiteral sl => _b.AddConstant(sl.Value),
                    NumericLiteral nl =>
                        _b.AddConstant(JsValue.ToStringValue(JsValue.Number(nl.Value))),
                    _ => throw new NotSupportedException(
                        $"object key kind '{prop.Key.GetType().Name}'"),
                };
                EmitExpression(prop.Value);                 // [obj, obj, value]
                _b.EmitU16(Opcode.StoreProperty, nameIdx);  // [obj, value]
            }
            _b.Emit(Opcode.Pop); // [obj]
        }
    }



    private enum RestExclusionKind { Constant, Local }
    private readonly record struct RestExclusion(RestExclusionKind Kind, string? Name, int Slot);

    private static bool IsPattern(Expression e) => e switch
    {
        ArrayExpression => true,
        ObjectExpression => true,
        BindingPattern => true,
        AssignmentExpression { Op: "=" } a => IsPattern(a.Target),
        AssignmentPattern a => IsPattern(a.Target),
        _ => false,
    };

    private void BindFunctionParameters(IReadOnlyList<Expression> parameters)
    {
        var argSlots = new int[parameters.Count];
        Array.Fill(argSlots, -1);

        // VM argument copying fills local slots 0..argc-1, so reserve every
        // positional parameter slot before declaring destructured binding locals.
        for (var i = 0; i < parameters.Count; i++)
        {
            if (parameters[i] is SpreadElement) break;
            argSlots[i] = _b.ReserveLocal();
        }

        for (var i = 0; i < parameters.Count; i++)
        {
            var param = parameters[i];
            if (param is SpreadElement spread)
            {
                DeclarePatternBindings(spread.Argument);
                // Full rest-parameter collection is a later iterator/Array task;
                // leave the binding undefined rather than corrupting following args.
                continue;
            }

            if (param is Identifier id)
            {
                _scopes[^1][id.Name] = argSlots[i];
                // gap:closure-write-back — if the parameter is captured by a
                // nested function, promote its argument value into a Cell so
                // writes from the nested function propagate back.
                if (IsNameCaptured(id.Name))
                {
                    _b.MarkCaptured(argSlots[i]);
                    _b.Emit(Opcode.PromoteParamCell, (byte)argSlots[i]);
                }
                continue;
            }

            DeclarePatternBindings(param);
            EmitPatternFromLocal(param, argSlots[i], isDeclaration: true);
        }
    }

    private void DeclarePatternBindings(Expression pattern)
    {
        switch (pattern)
        {
            case Identifier id:
                // gap:script-top-var-not-global — at script top, a `var`
                // binding becomes an own data property on the global object
                // (§16.1.7 ScriptEvaluation → CreateGlobalVarBinding). Emit
                // the idempotent declare opcode so a redeclaration without
                // an initializer doesn't reset an existing value, and a
                // hoisted function-decl that already installed the name
                // keeps its function value.
                if (IsScriptTop)
                {
                    _b.EmitU16(Opcode.DeclareGlobalVar, _b.AddConstant(id.Name));
                    return;
                }
                if (!_scopes[^1].ContainsKey(id.Name))
                {
                    var slot = _b.ReserveLocal();
                    _scopes[^1][id.Name] = slot;
                    // gap:closure-write-back — captured bindings use a Cell.
                    if (IsNameCaptured(id.Name))
                    {
                        _b.MarkCaptured(slot);
                        _b.Emit(Opcode.InitCellLocal, (byte)slot);
                    }
                    else
                    {
                        _b.Emit(Opcode.DeclareLocal, (byte)slot);
                    }
                }
                return;
            case AssignmentExpression { Op: "=" } a:
                DeclarePatternBindings(a.Target);
                return;
            case AssignmentPattern a:
                DeclarePatternBindings(a.Target);
                return;
            case ArrayPattern arr:
                foreach (var element in arr.Elements)
                {
                    switch (element)
                    {
                        case ArrayPatternBindingElement binding:
                            DeclarePatternBindings(binding.Target);
                            break;
                        case ArrayPatternRestElement rest:
                            DeclarePatternBindings(rest.Target);
                            break;
                    }
                }
                return;
            case ObjectPattern obj:
                foreach (var prop in obj.Properties) DeclarePatternBindings(prop.Target);
                if (obj.Rest is not null) DeclarePatternBindings(obj.Rest.Argument);
                return;
            case ArrayExpression arr:
                foreach (var element in arr.Elements)
                {
                    if (element is null) continue;
                    DeclarePatternBindings(element is SpreadElement spread ? spread.Argument : element);
                }
                return;
            case ObjectExpression obj:
                foreach (var prop in obj.Properties)
                {
                    if (prop.Value is SpreadElement spread) DeclarePatternBindings(spread.Argument);
                    else DeclarePatternBindings(prop.Value);
                }
                return;
            case SpreadElement spread:
                DeclarePatternBindings(spread.Argument);
                return;
        }
    }

    private void EmitPatternFromLocal(Expression pattern, int sourceSlot, bool isDeclaration)
    {
        _b.Emit(Opcode.LoadLocal, (byte)sourceSlot);
        EmitPatternFromStack(pattern, isDeclaration);
    }

    private void EmitPatternFromStack(Expression pattern, bool isDeclaration)
    {
        switch (pattern)
        {
            case Identifier id:
                StoreBindingIdentifier(id.Name);
                return;
            case MemberExpression me:
                StoreMemberTarget(me);
                return;
            case AssignmentExpression { Op: "=" } a:
                EmitDefaultedPattern(a.Target, a.Value, isDeclaration);
                return;
            case AssignmentPattern a:
                EmitDefaultedPattern(a.Target, a.Default, isDeclaration);
                return;
            case ArrayPattern arr:
                EmitArrayPattern(arr, isDeclaration);
                return;
            case ObjectPattern obj:
                EmitObjectPattern(obj, isDeclaration);
                return;
            case ArrayExpression arr:
                EmitArrayPattern(arr, isDeclaration);
                return;
            case ObjectExpression obj:
                EmitObjectPattern(obj, isDeclaration);
                return;
            case SpreadElement spread:
                EmitPatternFromStack(spread.Argument, isDeclaration);
                return;
            case RestElement rest:
                EmitPatternFromStack(rest.Argument, isDeclaration);
                return;
            default:
                throw new NotSupportedException($"invalid destructuring target '{pattern.GetType().Name}'");
        }
    }

    private void EmitDefaultedPattern(Expression target, Expression fallback, bool isDeclaration)
    {
        var valueSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)valueSlot);
        _b.Emit(Opcode.LoadLocal, (byte)valueSlot);
        _b.Emit(Opcode.LoadUndefined);
        _b.Emit(Opcode.StrictEq);
        var skipDefault = _b.EmitJump(Opcode.JumpIfFalse);
        EmitExpression(fallback);
        _b.Emit(Opcode.StoreLocal, (byte)valueSlot);
        _b.PatchJump(skipDefault);
        EmitPatternFromLocal(target, valueSlot, isDeclaration);
    }

    private void StoreBindingIdentifier(string name)
    {
        // gap:closure-write-back — captured-local writes route through the cell.
        if (TryResolveLocal(name, out var slot))
            EmitStoreLocalSlot(slot);
        else if (TryResolveUpvalue(name, out var upIdx))
            _b.Emit(Opcode.StoreUpvalue, (byte)upIdx);
        else
            _b.EmitU16(Opcode.StoreGlobal, _b.AddConstant(name));
    }

    private void StoreMemberTarget(MemberExpression me)
    {
        var valueSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)valueSlot);
        EmitExpression(me.Object);
        if (me.Computed) EmitExpression(me.Property);
        _b.Emit(Opcode.LoadLocal, (byte)valueSlot);
        if (me.Computed) _b.Emit(Opcode.StoreComputed);
        else _b.EmitU16(Opcode.StoreProperty, _b.AddConstant(((Identifier)me.Property).Name));
        _b.Emit(Opcode.Pop);
    }

    private void EmitArrayPattern(ArrayExpression arr, bool isDeclaration)
    {
        var srcSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
        for (var i = 0; i < arr.Elements.Count; i++)
        {
            var element = arr.Elements[i];
            if (element is null) continue;
            if (element is SpreadElement spread)
            {
                _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
                _b.EmitU16(Opcode.RestArray, i);
                EmitPatternFromStack(spread.Argument, isDeclaration);
                break;
            }
            _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
            _b.EmitU16(Opcode.LoadConst, _b.AddConstant((double)i));
            _b.Emit(Opcode.LoadComputed);
            EmitPatternFromStack(element, isDeclaration);
        }
    }

    private void EmitArrayPattern(ArrayPattern arr, bool isDeclaration)
    {
        var srcSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
        for (var i = 0; i < arr.Elements.Count; i++)
        {
            switch (arr.Elements[i])
            {
                case ArrayPatternHole:
                    continue;
                case ArrayPatternRestElement rest:
                    _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
                    _b.EmitU16(Opcode.RestArray, i);
                    EmitPatternFromStack(rest.Target, isDeclaration);
                    return;
                case ArrayPatternBindingElement binding:
                    _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
                    _b.EmitU16(Opcode.LoadConst, _b.AddConstant((double)i));
                    _b.Emit(Opcode.LoadComputed);
                    if (binding.Default is null) EmitPatternFromStack(binding.Target, isDeclaration);
                    else EmitDefaultedPattern(binding.Target, binding.Default, isDeclaration);
                    break;
            }
        }
    }

    private void EmitObjectPattern(ObjectExpression obj, bool isDeclaration)
    {
        var srcSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
        var exclusions = new List<RestExclusion>();
        foreach (var prop in obj.Properties)
        {
            if (prop.Value is SpreadElement spread)
            {
                EmitObjectRest(srcSlot, exclusions, spread.Argument, isDeclaration);
                continue;
            }

            EmitObjectPatternPropertyLoad(srcSlot, prop.Key, prop.Computed, exclusions);
            EmitPatternFromStack(prop.Value, isDeclaration);
        }
    }

    private void EmitObjectPattern(ObjectPattern obj, bool isDeclaration)
    {
        var srcSlot = _b.ReserveLocal();
        _b.Emit(Opcode.StoreLocal, (byte)srcSlot);
        var exclusions = new List<RestExclusion>();
        foreach (var prop in obj.Properties)
        {
            EmitObjectPatternPropertyLoad(srcSlot, prop.Key, prop.Computed, exclusions);
            if (prop.Default is null) EmitPatternFromStack(prop.Target, isDeclaration);
            else EmitDefaultedPattern(prop.Target, prop.Default, isDeclaration);
        }
        if (obj.Rest is not null)
        {
            EmitObjectRest(srcSlot, exclusions, obj.Rest.Argument, isDeclaration);
        }
    }

    private void EmitObjectPatternPropertyLoad(int srcSlot, Expression key, bool computed, List<RestExclusion> exclusions)
    {
        if (computed)
        {
            var keySlot = _b.ReserveLocal();
            EmitExpression(key);
            _b.Emit(Opcode.StoreLocal, (byte)keySlot);
            _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
            _b.Emit(Opcode.LoadLocal, (byte)keySlot);
            _b.Emit(Opcode.LoadComputed);
            exclusions.Add(new RestExclusion(RestExclusionKind.Local, null, keySlot));
        }
        else
        {
            var name = PropertyName(key);
            _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
            _b.EmitU16(Opcode.LoadProperty, _b.AddConstant(name));
            exclusions.Add(new RestExclusion(RestExclusionKind.Constant, name, -1));
        }
    }

    private void EmitObjectRest(int srcSlot, List<RestExclusion> exclusions, Expression target, bool isDeclaration)
    {
        _b.Emit(Opcode.LoadLocal, (byte)srcSlot);
        foreach (var ex in exclusions)
        {
            if (ex.Kind == RestExclusionKind.Constant)
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(ex.Name!));
            else
                _b.Emit(Opcode.LoadLocal, (byte)ex.Slot);
        }
        _b.EmitU16(Opcode.RestObject, exclusions.Count);
        EmitPatternFromStack(target, isDeclaration);
    }

    private void EmitArrayLiteral(ArrayExpression ae)
    {
        // B2-4: arrays are now dense JsArray exotics, so the magic length
        // slot is derived from the indexed slots. We still walk each element
        // and StoreProperty its index — JsArray's exotic [[Set]] routes
        // indexed writes into the dense backing. B3-2: spread elements
        // dispatch through SpreadIterable which walks @@iterator.
        _b.Emit(Opcode.NewArray);
        // Track the next dense index that a plain element should land at.
        // Once a spread runs, subsequent plain elements use Array.prototype.push
        // semantics via a length-derived index — implemented here by reading
        // the current length back from the array. Track via a "pushMode"
        // flag: once true, every plain element goes through the same length+
        // index loop.
        var pushMode = false;
        for (var i = 0; i < ae.Elements.Count; i++)
        {
            var element = ae.Elements[i];
            if (element is null)
            {
                // Hole: skip but make sure length advances if it's the last hole.
                continue;
            }
            if (element is SpreadElement spread)
            {
                // SpreadIterable peeks the target — no Dup needed.
                EmitExpression(spread.Argument); // [arr, iterable]
                _b.Emit(Opcode.SpreadIterable); // [arr]
                pushMode = true;
                continue;
            }
            if (!pushMode)
            {
                _b.Emit(Opcode.Dup);
                EmitExpression(element);
                _b.EmitU16(Opcode.StoreProperty, _b.AddConstant(i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                _b.Emit(Opcode.Pop);
            }
            else
            {
                // Push via length: arr[arr.length] = value
                _b.Emit(Opcode.Dup);            // [arr, arr]
                _b.Emit(Opcode.Dup);            // [arr, arr, arr]
                _b.EmitU16(Opcode.LoadProperty, _b.AddConstant("length")); // [arr, arr, len]
                EmitExpression(element);        // [arr, arr, len, value]
                _b.Emit(Opcode.StoreComputed);  // [arr, value]
                _b.Emit(Opcode.Pop);            // [arr]
            }
        }
        // Pad trailing holes (null elements in the AST = elided slots) by
        // bumping length up via the magic property. JsArray.SetLength handles
        // grow-with-undefined. Skipped when pushMode is active since length
        // already tracks pushed values.
        if (!pushMode && ae.Elements.Count > 0 && ae.Elements[^1] is null)
        {
            _b.Emit(Opcode.Dup);
            _b.EmitU16(Opcode.LoadConst, _b.AddConstant((double)ae.Elements.Count));
            _b.EmitU16(Opcode.StoreProperty, _b.AddConstant("length"));
            _b.Emit(Opcode.Pop);
        }
    }

    private static string PropertyName(Expression key) => key switch
    {
        Identifier id => id.Name,
        StringLiteral s => s.Value,
        NumericLiteral n => JsValue.ToStringValue(JsValue.Number(n.Value)),
        _ => throw new NotSupportedException($"object pattern key kind '{key.GetType().Name}'"),
    };

    private void EmitNew(NewExpression ne)
    {
        EmitExpression(ne.Callee);
        var hasSpread = false;
        foreach (var arg in ne.Arguments)
            if (arg is SpreadElement) { hasSpread = true; break; }
        if (hasSpread)
        {
            EmitArgsAsArray(ne.Arguments);
            _b.Emit(Opcode.NewApply);
            return;
        }
        foreach (var arg in ne.Arguments) EmitExpression(arg);
        if (ne.Arguments.Count > 255)
            throw new NotSupportedException("more than 255 new args not supported");
        _b.Emit(Opcode.New, (byte)ne.Arguments.Count);
    }

    private static Opcode BinaryOpToOpcode(string op) => op switch
    {
        "+" => Opcode.Add, "-" => Opcode.Sub, "*" => Opcode.Mul,
        "/" => Opcode.Div, "%" => Opcode.Mod, "**" => Opcode.Pow,
        "|" => Opcode.BitOr, "&" => Opcode.BitAnd, "^" => Opcode.BitXor,
        "<<" => Opcode.Shl, ">>" => Opcode.Shr, ">>>" => Opcode.Ushr,
        "==" => Opcode.Eq, "!=" => Opcode.NEq,
        "===" => Opcode.StrictEq, "!==" => Opcode.StrictNEq,
        "<" => Opcode.Lt, "<=" => Opcode.LtEq, ">" => Opcode.Gt, ">=" => Opcode.GtEq,
        "instanceof" => Opcode.Instanceof,
        "in" => Opcode.In,
        _ => throw new NotSupportedException($"binary op '{op}'"),
    };

    private static Opcode UnaryOpToOpcode(string op) => op switch
    {
        "-" => Opcode.Neg, "+" => Opcode.UnaryPlus, "!" => Opcode.Not,
        "~" => Opcode.BitNot, "typeof" => Opcode.TypeOf,
        _ => throw new NotSupportedException($"unary op '{op}'"),
    };

    private static Opcode CompoundOpToBinaryOpcode(string op) => op switch
    {
        "+=" => Opcode.Add, "-=" => Opcode.Sub, "*=" => Opcode.Mul,
        "/=" => Opcode.Div, "%=" => Opcode.Mod, "**=" => Opcode.Pow,
        "|=" => Opcode.BitOr, "&=" => Opcode.BitAnd, "^=" => Opcode.BitXor,
        "<<=" => Opcode.Shl, ">>=" => Opcode.Shr, ">>>=" => Opcode.Ushr,
        _ => throw new NotSupportedException($"compound op '{op}'"),
    };
}
