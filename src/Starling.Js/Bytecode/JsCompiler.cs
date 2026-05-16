using Tessera.Js.Ast;
using Tessera.Js.Runtime;

namespace Tessera.Js.Bytecode;

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
public sealed class JsCompiler
{
    private readonly ChunkBuilder _b = new();
    private readonly List<Dictionary<string, int>> _scopes = [new()];

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

    public JsCompiler() : this(parent: null) { }

    private JsCompiler(JsCompiler? parent)
    {
        _parent = parent;
    }

    public static Chunk Compile(Program program, string? name = "<script>")
    {
        var c = new JsCompiler();
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
        c.EmitProgram(program, keepLastExpression: true);
        return c._b.Build(name);
    }

    private void EmitProgram(Program p, bool keepLastExpression)
    {
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
        foreach (var s in body)
        {
            if (s is not FunctionDeclaration fd) continue;
            // Compile the body in a fresh sub-compiler parented to this
            // one so the body can resolve free identifiers as upvalues
            // captured from this scope.
            var sub = new JsCompiler(parent: this);
            sub.EmitFunctionBody(fd);
            var chunk = sub._b.Build(fd.Name.Name);

            // Emit either LoadFunction (no captures) or push upvalues +
            // MakeClosure. Either way, leave the function value on the
            // stack, then store under the function's name as a global so
            // recursive references inside the body resolve to the same
            // closure instance.
            EmitFunctionConstructor(fd.Name.Name, chunk,
                CountSimpleParams(fd.Params), sub._upvalues);
            var nameIdx = _b.AddConstant(fd.Name.Name);
            _b.EmitU16(Opcode.StoreGlobal, nameIdx);
            // StoreGlobal does not re-push the value, so the stack is
            // balanced after the hoist.
        }
    }

    /// <summary>Materialize a function as either a plain template
    /// reference (no upvalues — emits <see cref="Opcode.LoadFunction"/>)
    /// or as a closure-construction sequence: push each upvalue's
    /// snapshot, then emit <see cref="Opcode.MakeClosure"/>. Leaves the
    /// resulting function value on the top of the stack.</summary>
    private void EmitFunctionConstructor(
        string name, Chunk body, int arity, IReadOnlyList<UpvalueRef> upvalues)
    {
        var fn = new Runtime.JsFunction(name, body, arity);
        var fnIdx = _b.AddConstant(fn);

        if (upvalues.Count == 0)
        {
            _b.EmitU16(Opcode.LoadFunction, fnIdx);
            return;
        }

        if (upvalues.Count > 255)
            throw new NotSupportedException("more than 255 captured variables not supported");

        // Push each captured value in upvalue-table order. For a local
        // capture, the value lives in this compiler's locals; for a
        // chained capture, it lives in this compiler's own upvalue table
        // (which the running closure will read via LoadUpvalue).
        foreach (var u in upvalues)
        {
            if (u.IsLocalCapture)
                _b.Emit(Opcode.LoadLocal, (byte)u.Index);
            else
                _b.Emit(Opcode.LoadUpvalue, (byte)u.Index);
        }
        _b.EmitU16(Opcode.MakeClosure, fnIdx);
        _b.EmitU8Raw((byte)upvalues.Count);
    }

    private static int CountSimpleParams(IReadOnlyList<Expression> ps)
    {
        // Spread/rest doesn't add to arity for first-cut binding.
        var n = 0;
        foreach (var p in ps) if (p is Identifier) n++;
        return n;
    }

    /// <summary>Compile a function body. Parameters get the first N local
    /// slots; the body's own var declarations follow.</summary>
    private void EmitFunctionBody(FunctionDeclaration fd)
    {
        // Reserve a local slot per simple-identifier parameter so the
        // callee sees args in slots 0..N-1.
        foreach (var p in fd.Params)
        {
            if (p is Identifier id)
            {
                var slot = _b.ReserveLocal();
                _scopes[^1][id.Name] = slot;
                // Parameters arrive in their slots before the body runs;
                // no DeclareLocal needed.
            }
            // SpreadElement (rest params) deferred to M3-04c.
        }
        foreach (var inner in fd.Body.Body) EmitStatement(inner);
        // Implicit `return undefined` if the body didn't return.
        _b.Emit(Opcode.ReturnUndefined);
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
                var loopStart = _b.Position;
                EmitExpression(w.Test);
                var jzWhile = _b.EmitJump(Opcode.JumpIfFalse);
                EmitStatement(w.Body);
                var jBack = _b.EmitJump(Opcode.Jump);
                // Patch loop back-edge: jump from end of jBack's operand to loopStart.
                PatchBackwardJump(jBack, loopStart);
                _b.PatchJump(jzWhile);
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
        }
        throw new NotSupportedException(
            $"compiler: statement kind '{s.GetType().Name}' not yet supported (see wp:M3-03 notes).");
    }

    private void EmitVarDecl(VariableDeclaration vd)
    {
        foreach (var d in vd.Declarations)
        {
            if (d.Id is not Identifier id)
                throw new NotSupportedException("destructuring patterns deferred to wp:M3-02d");
            var slot = _b.ReserveLocal();
            _scopes[^1][id.Name] = slot;
            _b.Emit(Opcode.DeclareLocal, (byte)slot);
            if (d.Init is not null)
            {
                EmitExpression(d.Init);
                _b.Emit(Opcode.StoreLocal, (byte)slot);
            }
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
                _b.EmitU16(Opcode.LoadConst, _b.AddConstant(new JsBigIntPlaceholder(bi.Digits)));
                return;
            case Identifier id:
                EmitIdLoad(id.Name);
                return;
            case ThisExpression:
                _b.Emit(Opcode.LoadThis);
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
        }
        throw new NotSupportedException(
            $"compiler: expression kind '{e.GetType().Name}' not yet supported.");
    }

    private void EmitIdLoad(string name)
    {
        if (TryResolveLocal(name, out var slot))
        {
            _b.Emit(Opcode.LoadLocal, (byte)slot);
            return;
        }
        if (TryResolveUpvalue(name, out var upIdx))
        {
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
            if (!TryResolveLocal(id.Name, out var slot))
                throw new NotSupportedException("update of global not yet supported");
            _b.Emit(Opcode.LoadLocal, (byte)slot);
            // For postfix, snapshot the current value before mutation.
            if (!up.Prefix) _b.Emit(Opcode.Dup);
            // Emit `+1` or `-1`.
            _b.EmitU16(Opcode.LoadConst, _b.AddConstant(1.0));
            _b.Emit(up.Op == "++" ? Opcode.Add : Opcode.Sub);
            // For prefix, the new value is what we keep; dup it before storing.
            if (up.Prefix) _b.Emit(Opcode.Dup);
            _b.Emit(Opcode.StoreLocal, (byte)slot);
            return;
        }
        throw new NotSupportedException("update target must be identifier in this slice");
    }

    private void EmitAssignment(AssignmentExpression a)
    {
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
            if (TryResolveLocal(id.Name, out var slot))
                _b.Emit(Opcode.StoreLocal, (byte)slot);
            else
                _b.EmitU16(Opcode.StoreGlobal, _b.AddConstant(id.Name));
            return;
        }
        if (a.Target is MemberExpression me)
        {
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
        throw new NotSupportedException("destructuring assignment is wp:M3-02d work");
    }

    private void EmitMemberLoad(MemberExpression m)
    {
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
            else
            {
                var nameIdx = _b.AddConstant(((Identifier)me.Property).Name);
                _b.EmitU16(Opcode.LoadProperty, nameIdx);  // [obj, fn]
            }
            foreach (var arg in call.Arguments)
            {
                if (arg is SpreadElement)
                    throw new NotSupportedException("spread in call args is M3-04c work");
                EmitExpression(arg);
            }
            _b.Emit(Opcode.CallMethod, (byte)call.Arguments.Count);
            return;
        }

        EmitExpression(call.Callee);
        foreach (var arg in call.Arguments)
        {
            if (arg is SpreadElement)
                throw new NotSupportedException("spread in call args is M3-04c work");
            EmitExpression(arg);
        }
        _b.Emit(Opcode.Call, (byte)call.Arguments.Count);
    }

    private void EmitFunctionExpression(FunctionExpression fe)
    {
        // Compile the body in a sub-compiler parented to this one so
        // free identifiers can be lazily resolved as upvalues captured
        // from this scope.
        var sub = new JsCompiler(parent: this);
        foreach (var p in fe.Params)
        {
            if (p is Identifier id)
            {
                var slot = sub._b.ReserveLocal();
                sub._scopes[^1][id.Name] = slot;
            }
        }
        foreach (var s in fe.Body.Body) sub.EmitStatement(s);
        sub._b.Emit(Opcode.ReturnUndefined);
        var name = fe.Name?.Name ?? "<anonymous>";
        var chunk = sub._b.Build(name);
        EmitFunctionConstructor(name, chunk, CountSimpleParams(fe.Params), sub._upvalues);
    }

    private void EmitObjectLiteral(ObjectExpression oe)
    {
        _b.Emit(Opcode.NewObject); // [obj]
        foreach (var prop in oe.Properties)
        {
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

    private void EmitNew(NewExpression ne)
    {
        EmitExpression(ne.Callee);
        foreach (var arg in ne.Arguments)
        {
            if (arg is SpreadElement)
                throw new NotSupportedException("spread in new args is M3-04c work");
            EmitExpression(arg);
        }
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
        "in" or "instanceof" => throw new NotSupportedException($"'{op}' opcode wp:M3-05"),
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
