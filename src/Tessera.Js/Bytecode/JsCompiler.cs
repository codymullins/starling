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

    public static Chunk Compile(Program program, string? name = "<script>")
    {
        var c = new JsCompiler();
        c.EmitProgram(program, keepLastExpression: false);
        return c._b.Build(name);
    }

    /// <summary>
    /// Compile in eval / REPL mode: if the last statement is an
    /// ExpressionStatement, leave its value on the stack so the caller of
    /// the VM can observe it. Used by <c>tessera js</c> and by tests.
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
            // Compile the body into a sub-chunk via a fresh JsCompiler.
            var sub = new JsCompiler();
            sub.EmitFunctionBody(fd);
            var chunk = sub._b.Build(fd.Name.Name);
            var fn = new Runtime.JsFunction(fd.Name.Name, chunk, CountSimpleParams(fd.Params));
            var idx = _b.AddConstant(fn);
            // Bind by name on the global object so recursive calls inside the
            // function body — which see only their own locals + globals —
            // can resolve the name. Closures (lexical capture) will land in
            // wp:M3-04c and replace this with proper upvalues.
            var nameIdx = _b.AddConstant(fd.Name.Name);
            _b.EmitU16(Opcode.LoadFunction, idx);
            _b.EmitU16(Opcode.StoreGlobal, nameIdx);
        }
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
            _b.Emit(Opcode.LoadLocal, (byte)slot);
        else
            _b.EmitU16(Opcode.LoadGlobal, _b.AddConstant(name));
    }

    private bool TryResolveLocal(string name, out int slot)
    {
        for (var i = _scopes.Count - 1; i >= 0; i--)
            if (_scopes[i].TryGetValue(name, out slot)) return true;
        slot = -1;
        return false;
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
        EmitExpression(call.Callee);
        foreach (var arg in call.Arguments)
        {
            if (arg is SpreadElement)
                throw new NotSupportedException("spread in call args is M3-04 work");
            EmitExpression(arg);
        }
        if (call.Arguments.Count > 255)
            throw new NotSupportedException("more than 255 call args not supported");
        _b.Emit(Opcode.Call, (byte)call.Arguments.Count);
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
