using Starling.Js.Ast;

namespace Starling.Js.Bytecode;

/// <summary>
/// gap:closure-write-back (wp:M3-04c2). Static analysis that decides which
/// of a function's own locals must use shared <see cref="Starling.Js.Runtime.Cell"/>
/// storage rather than plain slot storage.
/// </summary>
/// <remarks>
/// <para>
/// A local is "captured" iff some nested function (or class method / field
/// initializer / arrow / static block) inside the same function body
/// references the name as a free variable — i.e. the name isn't shadowed
/// by an inner declaration.
/// </para>
/// <para>
/// The analysis runs once per <see cref="JsCompiler"/> instance, before
/// any bytecode is emitted. Captured locals are recorded by name; the
/// compiler then consults the set at declaration sites (params + var/let
/// decls + function-decl hoists) and either marks the slot as a cell or
/// leaves it as a plain slot. Non-captured locals stay fast — no boxing.
/// </para>
/// </remarks>
internal static class CaptureAnalysis
{
    /// <summary>Given the parameters and statements of one function body
    /// (or the top-level script), return the set of identifier names that
    /// are referenced from inside any nested function or class member —
    /// after accounting for inner declarations that shadow the name.</summary>
    /// <remarks>
    /// The returned set is intentionally over-approximate in one direction
    /// only: names that are NOT in the set are guaranteed safe to store as
    /// plain slot values. Names IN the set may end up referenced by code
    /// that the static walker conservatively conflated; the runtime cost
    /// is a single extra indirection per access, never a correctness bug.
    /// </remarks>
    public static HashSet<string> Compute(
        IReadOnlyList<Expression> parameters,
        IReadOnlyList<Statement> body)
    {
        var captured = new HashSet<string>(StringComparer.Ordinal);
        // The current function's lexical scope owns its params + declared
        // names; we don't seed those into `captured` here — the caller
        // intersects with its actual declarations to find which slots to box.
        foreach (var s in body) WalkStatementInOuter(s, captured);
        // Parameter destructuring defaults can themselves contain nested
        // functions whose free names should capture this function's other
        // parameters; walk those too.
        foreach (var p in parameters) WalkExpressionInOuter(p, captured);
        return captured;
    }

    // ----------------------------------------------------------------------
    // "Outer" walk: we are still in the function whose captures we're
    // computing. Whenever we hit a nested function/class member body, we
    // switch to InnerWalk, which tracks lexical scopes to subtract local
    // declarations from the free-name set.
    // ----------------------------------------------------------------------

    private static void WalkStatementInOuter(Statement? s, HashSet<string> captured)
    {
        if (s is null) return;
        switch (s)
        {
            case BlockStatement b: foreach (var x in b.Body) WalkStatementInOuter(x, captured); return;
            case ExpressionStatement es: WalkExpressionInOuter(es.Expression, captured); return;
            case ReturnStatement r: if (r.Argument is not null) WalkExpressionInOuter(r.Argument, captured); return;
            case ThrowStatement t: WalkExpressionInOuter(t.Argument, captured); return;
            case IfStatement i:
                WalkExpressionInOuter(i.Test, captured);
                WalkStatementInOuter(i.Consequent, captured);
                WalkStatementInOuter(i.Alternate, captured);
                return;
            case WhileStatement w:
                WalkExpressionInOuter(w.Test, captured);
                WalkStatementInOuter(w.Body, captured);
                return;
            case DoWhileStatement dw:
                WalkStatementInOuter(dw.Body, captured);
                WalkExpressionInOuter(dw.Test, captured);
                return;
            case ForStatement f:
                if (f.Init is Statement initS) WalkStatementInOuter(initS, captured);
                else if (f.Init is Expression initE) WalkExpressionInOuter(initE, captured);
                if (f.Test is not null) WalkExpressionInOuter(f.Test, captured);
                if (f.Update is not null) WalkExpressionInOuter(f.Update, captured);
                WalkStatementInOuter(f.Body, captured);
                return;
            case ForInStatement fi:
                if (fi.Left is Statement leftS) WalkStatementInOuter(leftS, captured);
                else if (fi.Left is Expression leftE) WalkExpressionInOuter(leftE, captured);
                WalkExpressionInOuter(fi.Right, captured);
                WalkStatementInOuter(fi.Body, captured);
                return;
            case ForOfStatement fo:
                if (fo.Left is Statement leftS2) WalkStatementInOuter(leftS2, captured);
                else if (fo.Left is Expression leftE2) WalkExpressionInOuter(leftE2, captured);
                WalkExpressionInOuter(fo.Right, captured);
                WalkStatementInOuter(fo.Body, captured);
                return;
            case SwitchStatement sw:
                WalkExpressionInOuter(sw.Discriminant, captured);
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null) WalkExpressionInOuter(c.Test, captured);
                    foreach (var s2 in c.Consequent) WalkStatementInOuter(s2, captured);
                }
                return;
            case TryStatement tr:
                WalkStatementInOuter(tr.Block, captured);
                if (tr.Handler is not null)
                {
                    // The catch-binding shadows for the catch body — but we're
                    // not in a nested function, so we don't need to remove it
                    // from the outer's captured set. Just walk the body.
                    foreach (var s2 in tr.Handler.Body.Body) WalkStatementInOuter(s2, captured);
                }
                if (tr.Finalizer is not null) WalkStatementInOuter(tr.Finalizer, captured);
                return;
            case LabeledStatement ls: WalkStatementInOuter(ls.Body, captured); return;
            case VariableDeclaration vd:
                foreach (var d in vd.Declarations)
                {
                    if (d.Init is not null) WalkExpressionInOuter(d.Init, captured);
                }
                return;
            case FunctionDeclaration fd:
                // A nested function. Compute its free names (after subtracting
                // its own params + inner declarations) and union into captured.
                AddFreeNames(fd.Params, fd.Body.Body, captured);
                return;
            case ClassDeclaration cd:
                if (cd.BaseClass is not null) WalkExpressionInOuter(cd.BaseClass, captured);
                AddClassBodyFreeNames(cd.Body, captured);
                return;
            case EmptyStatement: case BreakStatement: case ContinueStatement: case DebuggerStatement:
                return;
        }
    }

    private static void WalkExpressionInOuter(Expression? e, HashSet<string> captured)
    {
        if (e is null) return;
        switch (e)
        {
            case Identifier:
            case NumericLiteral: case StringLiteral: case BooleanLiteral:
            case NullLiteral: case BigIntLiteral: case RegExpLiteral:
            case ThisExpression: case PrivateNameExpression:
                return;
            case BinaryExpression bin: WalkExpressionInOuter(bin.Left, captured); WalkExpressionInOuter(bin.Right, captured); return;
            case LogicalExpression log: WalkExpressionInOuter(log.Left, captured); WalkExpressionInOuter(log.Right, captured); return;
            case UnaryExpression u: WalkExpressionInOuter(u.Argument, captured); return;
            case UpdateExpression up: WalkExpressionInOuter(up.Argument, captured); return;
            case AssignmentExpression a: WalkExpressionInOuter(a.Target, captured); WalkExpressionInOuter(a.Value, captured); return;
            case AssignmentPattern a: WalkExpressionInOuter(a.Target, captured); WalkExpressionInOuter(a.Default, captured); return;
            case RestElement rest: WalkExpressionInOuter(rest.Argument, captured); return;
            case ConditionalExpression c:
                WalkExpressionInOuter(c.Test, captured);
                WalkExpressionInOuter(c.Consequent, captured);
                WalkExpressionInOuter(c.Alternate, captured);
                return;
            case MemberExpression m: WalkExpressionInOuter(m.Object, captured); if (m.Computed) WalkExpressionInOuter(m.Property, captured); return;
            case CallExpression call:
                WalkExpressionInOuter(call.Callee, captured);
                foreach (var arg in call.Arguments) WalkExpressionInOuter(arg, captured);
                return;
            case NewExpression ne:
                WalkExpressionInOuter(ne.Callee, captured);
                foreach (var arg in ne.Arguments) WalkExpressionInOuter(arg, captured);
                return;
            case ArrayPattern ap:
                foreach (var el in ap.Elements)
                {
                    switch (el)
                    {
                        case ArrayPatternBindingElement binding:
                            WalkExpressionInOuter(binding.Target, captured);
                            WalkExpressionInOuter(binding.Default, captured);
                            break;
                        case ArrayPatternRestElement rest:
                            WalkExpressionInOuter(rest.Target, captured);
                            break;
                    }
                }
                return;
            case ObjectPattern op:
                foreach (var prop in op.Properties)
                {
                    if (prop.Computed) WalkExpressionInOuter(prop.Key, captured);
                    WalkExpressionInOuter(prop.Target, captured);
                    WalkExpressionInOuter(prop.Default, captured);
                }
                if (op.Rest is not null) WalkExpressionInOuter(op.Rest.Argument, captured);
                return;
            case ArrayExpression ae:
                foreach (var el in ae.Elements) WalkExpressionInOuter(el, captured);
                return;
            case ObjectExpression oe:
                foreach (var prop in oe.Properties)
                {
                    if (prop.Computed) WalkExpressionInOuter(prop.Key, captured);
                    WalkExpressionInOuter(prop.Value, captured);
                }
                return;
            case SequenceExpression seq:
                foreach (var x in seq.Expressions) WalkExpressionInOuter(x, captured);
                return;
            case TemplateLiteral tpl:
                foreach (var x in tpl.Expressions) WalkExpressionInOuter(x, captured);
                return;
            case TaggedTemplateExpression tte:
                WalkExpressionInOuter(tte.Tag, captured);
                WalkExpressionInOuter(tte.Quasi, captured);
                return;
            case SpreadElement sp: WalkExpressionInOuter(sp.Argument, captured); return;
            case FunctionExpression fe:
                AddFreeNames(fe.Params, fe.Body.Body, captured);
                return;
            case ArrowFunctionExpression arrow:
                {
                    // Arrow body is either an expression or a block.
                    if (arrow.Body is BlockStatement block)
                    {
                        AddFreeNames(arrow.Params, block.Body, captured);
                    }
                    else if (arrow.Body is Expression expr)
                    {
                        // Treat expr-body arrow as a single-return body.
                        var fakeBody = new[] { (Statement)new ReturnStatement(expr, arrow.Start, arrow.End) };
                        AddFreeNames(arrow.Params, fakeBody, captured);
                    }
                }
                return;
            case ClassExpression cls:
                if (cls.BaseClass is not null) WalkExpressionInOuter(cls.BaseClass, captured);
                AddClassBodyFreeNames(cls.Body, captured);
                return;
            case SuperPropertyExpression sp2:
                if (sp2.Computed) WalkExpressionInOuter(sp2.Property, captured);
                return;
            case SuperCallExpression sc:
                foreach (var arg in sc.Arguments) WalkExpressionInOuter(arg, captured);
                return;
        }
    }

    // ----------------------------------------------------------------------
    // "Inner" walk: we're traversing a nested function's body. We push a
    // scope, register the function's own declared names, walk the body, and
    // every Identifier reference that isn't shadowed by a scope frame is a
    // candidate captured-name for the outer.
    // ----------------------------------------------------------------------

    private static void AddFreeNames(
        IReadOnlyList<Expression> parameters,
        IReadOnlyList<Statement> body,
        HashSet<string> outerCaptured)
    {
        var scopes = new Stack<HashSet<string>>();
        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
        // Declare parameter bindings + hoisted function decls + var decls at
        // function-top scope. let/const get added per-block.
        foreach (var p in parameters) AddBindingNames(p, scopes.Peek());
        // Hoisted function declarations are bound at function-top scope per §14.1.18.
        foreach (var s in body)
            if (s is FunctionDeclaration fd) scopes.Peek().Add(fd.Name.Name);
        // var-hoisting: visit body and collect var names into function-top scope.
        foreach (var s in body) CollectVarHoistedBindings(s, scopes.Peek());

        // Walk inner body: any free identifier whose name isn't covered by
        // any active scope frame goes into outerCaptured.
        foreach (var s in body) InnerStatement(s, scopes, outerCaptured);
        foreach (var p in parameters)
        {
            // Walk default-value expressions for params (the param identifier
            // itself is bound; defaults can reference earlier params).
            InnerExpression(p, scopes, outerCaptured);
        }
        scopes.Pop();
    }

    private static void AddClassBodyFreeNames(ClassBody body, HashSet<string> outerCaptured)
    {
        if (body.Constructor is not null)
            AddFreeNames(body.Constructor.Params, body.Constructor.Body.Body, outerCaptured);
        foreach (var m in body.Methods)
            AddFreeNames(m.Params, m.Body.Body, outerCaptured);
        foreach (var f in body.Fields)
        {
            if (f.Computed) WalkExpressionInOuter(f.Key, outerCaptured);
            if (f.Initializer is not null)
            {
                // Field initializer runs as a one-arg closure (`this` is the
                // instance) — treat it as a nested function with no params.
                var fakeBody = new[] { (Statement)new ExpressionStatement(f.Initializer, f.Initializer.Start, f.Initializer.End) };
                AddFreeNames(Array.Empty<Expression>(), fakeBody, outerCaptured);
            }
        }
        foreach (var sb in body.StaticBlocks)
            AddFreeNames(Array.Empty<Expression>(), sb.Body, outerCaptured);
    }

    private static void CollectVarHoistedBindings(Statement? s, HashSet<string> scope)
    {
        if (s is null) return;
        switch (s)
        {
            case VariableDeclaration vd:
                // Per §14.3.2, `var` is function-scoped; `let`/`const` are
                // block-scoped. For the conservative purposes of capture
                // analysis we treat all decls as function-scoped here — this
                // can only over-shadow (mark a name as NOT captured when it
                // actually is in a stricter sense), so we restrict to "var".
                if (vd.Kind == "var")
                    foreach (var d in vd.Declarations) AddBindingNames(d.Id, scope);
                return;
            case BlockStatement b: foreach (var x in b.Body) CollectVarHoistedBindings(x, scope); return;
            case IfStatement i:
                CollectVarHoistedBindings(i.Consequent, scope);
                CollectVarHoistedBindings(i.Alternate, scope);
                return;
            case WhileStatement w: CollectVarHoistedBindings(w.Body, scope); return;
            case DoWhileStatement dw: CollectVarHoistedBindings(dw.Body, scope); return;
            case ForStatement f:
                if (f.Init is VariableDeclaration fvd && fvd.Kind == "var")
                    foreach (var d in fvd.Declarations) AddBindingNames(d.Id, scope);
                CollectVarHoistedBindings(f.Body, scope);
                return;
            case ForInStatement fi:
                if (fi.Left is VariableDeclaration vdi && vdi.Kind == "var")
                    foreach (var d in vdi.Declarations) AddBindingNames(d.Id, scope);
                CollectVarHoistedBindings(fi.Body, scope);
                return;
            case ForOfStatement fo:
                if (fo.Left is VariableDeclaration vdo && vdo.Kind == "var")
                    foreach (var d in vdo.Declarations) AddBindingNames(d.Id, scope);
                CollectVarHoistedBindings(fo.Body, scope);
                return;
            case SwitchStatement sw:
                foreach (var c in sw.Cases)
                    foreach (var s2 in c.Consequent) CollectVarHoistedBindings(s2, scope);
                return;
            case TryStatement tr:
                CollectVarHoistedBindings(tr.Block, scope);
                if (tr.Handler is not null)
                    foreach (var s2 in tr.Handler.Body.Body) CollectVarHoistedBindings(s2, scope);
                if (tr.Finalizer is not null) CollectVarHoistedBindings(tr.Finalizer, scope);
                return;
            case LabeledStatement ls: CollectVarHoistedBindings(ls.Body, scope); return;
        }
    }

    private static void AddBindingNames(Expression? pattern, HashSet<string> scope)
    {
        if (pattern is null) return;
        switch (pattern)
        {
            case Identifier id: scope.Add(id.Name); return;
            case AssignmentExpression a when a.Op == "=": AddBindingNames(a.Target, scope); return;
            case AssignmentPattern a: AddBindingNames(a.Target, scope); return;
            case ArrayPattern arr:
                foreach (var el in arr.Elements)
                {
                    switch (el)
                    {
                        case ArrayPatternBindingElement binding: AddBindingNames(binding.Target, scope); break;
                        case ArrayPatternRestElement rest: AddBindingNames(rest.Target, scope); break;
                    }
                }
                return;
            case ObjectPattern obj:
                foreach (var prop in obj.Properties) AddBindingNames(prop.Target, scope);
                if (obj.Rest is not null) AddBindingNames(obj.Rest.Argument, scope);
                return;
            case ArrayExpression arr:
                foreach (var el in arr.Elements)
                {
                    if (el is null) continue;
                    AddBindingNames(el is SpreadElement sp ? sp.Argument : el, scope);
                }
                return;
            case ObjectExpression obj:
                foreach (var prop in obj.Properties)
                {
                    if (prop.Value is SpreadElement sp) AddBindingNames(sp.Argument, scope);
                    else AddBindingNames(prop.Value, scope);
                }
                return;
            case SpreadElement spread: AddBindingNames(spread.Argument, scope); return;
        }
    }

    private static void InnerStatement(Statement? s, Stack<HashSet<string>> scopes, HashSet<string> outerCaptured)
    {
        if (s is null) return;
        switch (s)
        {
            case BlockStatement b:
                scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                // Hoist let/const + function decls at this block scope.
                foreach (var s2 in b.Body)
                {
                    if (s2 is VariableDeclaration vd && (vd.Kind == "let" || vd.Kind == "const"))
                        foreach (var d in vd.Declarations) AddBindingNames(d.Id, scopes.Peek());
                    else if (s2 is FunctionDeclaration fd2)
                        scopes.Peek().Add(fd2.Name.Name);
                    else if (s2 is ClassDeclaration cd2)
                        scopes.Peek().Add(cd2.Name.Name);
                }
                foreach (var x in b.Body) InnerStatement(x, scopes, outerCaptured);
                scopes.Pop();
                return;
            case ExpressionStatement es: InnerExpression(es.Expression, scopes, outerCaptured); return;
            case ReturnStatement r: if (r.Argument is not null) InnerExpression(r.Argument, scopes, outerCaptured); return;
            case ThrowStatement t: InnerExpression(t.Argument, scopes, outerCaptured); return;
            case IfStatement i:
                InnerExpression(i.Test, scopes, outerCaptured);
                InnerStatement(i.Consequent, scopes, outerCaptured);
                InnerStatement(i.Alternate, scopes, outerCaptured);
                return;
            case WhileStatement w:
                InnerExpression(w.Test, scopes, outerCaptured);
                InnerStatement(w.Body, scopes, outerCaptured);
                return;
            case DoWhileStatement dw:
                InnerStatement(dw.Body, scopes, outerCaptured);
                InnerExpression(dw.Test, scopes, outerCaptured);
                return;
            case ForStatement f:
                scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                if (f.Init is VariableDeclaration fvd)
                {
                    foreach (var d in fvd.Declarations) AddBindingNames(d.Id, scopes.Peek());
                    foreach (var d in fvd.Declarations)
                        if (d.Init is not null) InnerExpression(d.Init, scopes, outerCaptured);
                }
                else if (f.Init is Expression initE) InnerExpression(initE, scopes, outerCaptured);
                if (f.Test is not null) InnerExpression(f.Test, scopes, outerCaptured);
                if (f.Update is not null) InnerExpression(f.Update, scopes, outerCaptured);
                InnerStatement(f.Body, scopes, outerCaptured);
                scopes.Pop();
                return;
            case ForInStatement fi:
                scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                if (fi.Left is VariableDeclaration vdfi)
                    foreach (var d in vdfi.Declarations) AddBindingNames(d.Id, scopes.Peek());
                else if (fi.Left is Expression leftE) InnerExpression(leftE, scopes, outerCaptured);
                InnerExpression(fi.Right, scopes, outerCaptured);
                InnerStatement(fi.Body, scopes, outerCaptured);
                scopes.Pop();
                return;
            case ForOfStatement fo:
                scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                if (fo.Left is VariableDeclaration vdfo)
                    foreach (var d in vdfo.Declarations) AddBindingNames(d.Id, scopes.Peek());
                else if (fo.Left is Expression leftE2) InnerExpression(leftE2, scopes, outerCaptured);
                InnerExpression(fo.Right, scopes, outerCaptured);
                InnerStatement(fo.Body, scopes, outerCaptured);
                scopes.Pop();
                return;
            case SwitchStatement sw:
                InnerExpression(sw.Discriminant, scopes, outerCaptured);
                foreach (var c in sw.Cases)
                {
                    if (c.Test is not null) InnerExpression(c.Test, scopes, outerCaptured);
                    foreach (var s2 in c.Consequent) InnerStatement(s2, scopes, outerCaptured);
                }
                return;
            case TryStatement tr:
                InnerStatement(tr.Block, scopes, outerCaptured);
                if (tr.Handler is not null)
                {
                    scopes.Push(new HashSet<string>(StringComparer.Ordinal));
                    if (tr.Handler.Param is not null) AddBindingNames(tr.Handler.Param, scopes.Peek());
                    foreach (var s2 in tr.Handler.Body.Body) InnerStatement(s2, scopes, outerCaptured);
                    scopes.Pop();
                }
                if (tr.Finalizer is not null) InnerStatement(tr.Finalizer, scopes, outerCaptured);
                return;
            case LabeledStatement ls: InnerStatement(ls.Body, scopes, outerCaptured); return;
            case VariableDeclaration vd:
                // For let/const, declare into the topmost scope frame (block);
                // for var, into the function-top (already collected by
                // hoisting); either way the name is now in scope.
                foreach (var d in vd.Declarations)
                {
                    AddBindingNames(d.Id, scopes.Peek());
                    if (d.Init is not null) InnerExpression(d.Init, scopes, outerCaptured);
                }
                return;
            case FunctionDeclaration fd:
                scopes.Peek().Add(fd.Name.Name);
                // The nested-nested function is yet another walk; treat it
                // as a free-name source against the SAME outerCaptured (any
                // identifier it references that isn't shadowed in scopes
                // or in its own internals belongs to the outermost owner).
                InnerNestedFunctionFreeNames(fd.Params, fd.Body.Body, scopes, outerCaptured);
                return;
            case ClassDeclaration cd:
                scopes.Peek().Add(cd.Name.Name);
                if (cd.BaseClass is not null) InnerExpression(cd.BaseClass, scopes, outerCaptured);
                InnerClassBody(cd.Body, scopes, outerCaptured);
                return;
            case EmptyStatement: case BreakStatement: case ContinueStatement: case DebuggerStatement:
                return;
        }
    }

    private static void InnerExpression(Expression? e, Stack<HashSet<string>> scopes, HashSet<string> outerCaptured)
    {
        if (e is null) return;
        switch (e)
        {
            case Identifier id:
                if (!IsBoundIn(scopes, id.Name)) outerCaptured.Add(id.Name);
                return;
            case NumericLiteral: case StringLiteral: case BooleanLiteral:
            case NullLiteral: case BigIntLiteral: case RegExpLiteral:
            case ThisExpression: case PrivateNameExpression:
                return;
            case BinaryExpression bin: InnerExpression(bin.Left, scopes, outerCaptured); InnerExpression(bin.Right, scopes, outerCaptured); return;
            case LogicalExpression log: InnerExpression(log.Left, scopes, outerCaptured); InnerExpression(log.Right, scopes, outerCaptured); return;
            case UnaryExpression u: InnerExpression(u.Argument, scopes, outerCaptured); return;
            case UpdateExpression up: InnerExpression(up.Argument, scopes, outerCaptured); return;
            case AssignmentExpression a: InnerExpression(a.Target, scopes, outerCaptured); InnerExpression(a.Value, scopes, outerCaptured); return;
            case ConditionalExpression c:
                InnerExpression(c.Test, scopes, outerCaptured);
                InnerExpression(c.Consequent, scopes, outerCaptured);
                InnerExpression(c.Alternate, scopes, outerCaptured);
                return;
            case MemberExpression m: InnerExpression(m.Object, scopes, outerCaptured); if (m.Computed) InnerExpression(m.Property, scopes, outerCaptured); return;
            case CallExpression call:
                InnerExpression(call.Callee, scopes, outerCaptured);
                foreach (var arg in call.Arguments) InnerExpression(arg, scopes, outerCaptured);
                return;
            case NewExpression ne:
                InnerExpression(ne.Callee, scopes, outerCaptured);
                foreach (var arg in ne.Arguments) InnerExpression(arg, scopes, outerCaptured);
                return;
            case ArrayPattern ap:
                foreach (var el in ap.Elements)
                {
                    switch (el)
                    {
                        case ArrayPatternBindingElement binding:
                            InnerExpression(binding.Target, scopes, outerCaptured);
                            InnerExpression(binding.Default, scopes, outerCaptured);
                            break;
                        case ArrayPatternRestElement rest:
                            InnerExpression(rest.Target, scopes, outerCaptured);
                            break;
                    }
                }
                return;
            case ObjectPattern op:
                foreach (var prop in op.Properties)
                {
                    if (prop.Computed) InnerExpression(prop.Key, scopes, outerCaptured);
                    InnerExpression(prop.Target, scopes, outerCaptured);
                    InnerExpression(prop.Default, scopes, outerCaptured);
                }
                if (op.Rest is not null) InnerExpression(op.Rest.Argument, scopes, outerCaptured);
                return;
            case AssignmentPattern ap:
                InnerExpression(ap.Target, scopes, outerCaptured);
                InnerExpression(ap.Default, scopes, outerCaptured);
                return;
            case RestElement rest:
                InnerExpression(rest.Argument, scopes, outerCaptured);
                return;
            case ArrayExpression ae:
                foreach (var el in ae.Elements) InnerExpression(el, scopes, outerCaptured);
                return;
            case ObjectExpression oe:
                foreach (var prop in oe.Properties)
                {
                    if (prop.Computed) InnerExpression(prop.Key, scopes, outerCaptured);
                    InnerExpression(prop.Value, scopes, outerCaptured);
                }
                return;
            case SequenceExpression seq:
                foreach (var x in seq.Expressions) InnerExpression(x, scopes, outerCaptured);
                return;
            case TemplateLiteral tpl:
                foreach (var x in tpl.Expressions) InnerExpression(x, scopes, outerCaptured);
                return;
            case TaggedTemplateExpression tte:
                InnerExpression(tte.Tag, scopes, outerCaptured);
                InnerExpression(tte.Quasi, scopes, outerCaptured);
                return;
            case SpreadElement sp: InnerExpression(sp.Argument, scopes, outerCaptured); return;
            case FunctionExpression fe:
                InnerNestedFunctionFreeNames(fe.Params, fe.Body.Body, scopes, outerCaptured);
                return;
            case ArrowFunctionExpression arrow:
                {
                    IReadOnlyList<Statement> body;
                    if (arrow.Body is BlockStatement block) body = block.Body;
                    else if (arrow.Body is Expression expr)
                        body = new[] { (Statement)new ReturnStatement(expr, arrow.Start, arrow.End) };
                    else body = Array.Empty<Statement>();
                    InnerNestedFunctionFreeNames(arrow.Params, body, scopes, outerCaptured);
                }
                return;
            case ClassExpression cls:
                if (cls.BaseClass is not null) InnerExpression(cls.BaseClass, scopes, outerCaptured);
                InnerClassBody(cls.Body, scopes, outerCaptured);
                return;
            case SuperPropertyExpression sp2:
                if (sp2.Computed) InnerExpression(sp2.Property, scopes, outerCaptured);
                return;
            case SuperCallExpression sc:
                foreach (var arg in sc.Arguments) InnerExpression(arg, scopes, outerCaptured);
                return;
        }
    }

    /// <summary>A nested-nested function: push a fresh scope frame seeded
    /// with the nested function's own bindings (params + var/let hoists +
    /// inner function decls), then walk its body. Any free identifier still
    /// not resolved against any active scope frame escapes all the way up
    /// to <paramref name="outerCaptured"/>, which is the set we'll
    /// intersect with the outermost function's declarations.</summary>
    private static void InnerNestedFunctionFreeNames(
        IReadOnlyList<Expression> parameters,
        IReadOnlyList<Statement> body,
        Stack<HashSet<string>> scopes,
        HashSet<string> outerCaptured)
    {
        scopes.Push(new HashSet<string>(StringComparer.Ordinal));
        foreach (var p in parameters) AddBindingNames(p, scopes.Peek());
        foreach (var s in body)
            if (s is FunctionDeclaration fd) scopes.Peek().Add(fd.Name.Name);
        foreach (var s in body) CollectVarHoistedBindings(s, scopes.Peek());

        foreach (var s in body) InnerStatement(s, scopes, outerCaptured);
        foreach (var p in parameters) InnerExpression(p, scopes, outerCaptured);
        scopes.Pop();
    }

    private static void InnerClassBody(ClassBody body, Stack<HashSet<string>> scopes, HashSet<string> outerCaptured)
    {
        if (body.Constructor is not null)
            InnerNestedFunctionFreeNames(body.Constructor.Params, body.Constructor.Body.Body, scopes, outerCaptured);
        foreach (var m in body.Methods)
            InnerNestedFunctionFreeNames(m.Params, m.Body.Body, scopes, outerCaptured);
        foreach (var f in body.Fields)
        {
            if (f.Computed) InnerExpression(f.Key, scopes, outerCaptured);
            if (f.Initializer is not null)
            {
                var fakeBody = new[] { (Statement)new ExpressionStatement(f.Initializer, f.Initializer.Start, f.Initializer.End) };
                InnerNestedFunctionFreeNames(Array.Empty<Expression>(), fakeBody, scopes, outerCaptured);
            }
        }
        foreach (var sb in body.StaticBlocks)
            InnerNestedFunctionFreeNames(Array.Empty<Expression>(), sb.Body, scopes, outerCaptured);
    }

    private static bool IsBoundIn(Stack<HashSet<string>> scopes, string name)
    {
        foreach (var frame in scopes)
            if (frame.Contains(name)) return true;
        return false;
    }
}
