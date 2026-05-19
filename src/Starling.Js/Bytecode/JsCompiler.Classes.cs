using Tessera.Js.Ast;
using Tessera.Js.Lex;
using Tessera.Js.Runtime;

namespace Tessera.Js.Bytecode;

/// <summary>
/// B1b-2a — class declaration / expression / <c>super</c> / private
/// <c>#field</c> compilation. Lowers each class to a single
/// <see cref="Opcode.BuildClass"/> instruction whose
/// <see cref="ClassTemplate"/> describes the class shape; method bodies
/// run as ordinary JsFunctions with their
/// <see cref="JsFunction.HomeObject"/> stamped by the runtime helper.
/// </summary>
public sealed partial class JsCompiler
{
    /// <summary>True while emitting bytecode for a class method body — the
    /// compiler must then emit <see cref="Opcode.LoadThisChecked"/> for
    /// <c>this</c> references so derived-constructor uninitialized state
    /// surfaces as ReferenceError.</summary>
    private int _classMethodDepth;

    private void EmitClassDeclaration(ClassDeclaration cd)
    {
        // §15.7 — ClassDeclaration introduces a `let` binding. For B1b-2a we
        // emit a global write at the top level so static initializers and
        // method bodies that read the class name resolve through LoadGlobal
        // (which sees the post-BuildClass value) instead of through a
        // snapshot-captured upvalue (which would see the pre-write value
        // and force a TDZ workaround). Pin a follow-up to revisit when
        // block-scoped classes are needed.
        EmitClassValue(cd.Name, cd.BaseClass, cd.Body);
        _b.Emit(Opcode.Dup); // assignment is an expression — leave value on stack briefly
        _b.EmitU16(Opcode.StoreGlobal, _b.AddConstant(cd.Name.Name));
        _b.Emit(Opcode.Pop);
    }

    private void EmitClassExpression(ClassExpression ce)
    {
        EmitClassValue(ce.Name, ce.BaseClass, ce.Body);
    }

    private void EmitClassValue(Identifier? name, Expression? baseExpr, ClassBody body)
    {
        // Evaluate the base class first (if any) so it's on the stack before
        // method upvalues.
        if (baseExpr is not null)
            EmitExpression(baseExpr);

        var classId = ClassTemplate.NextClassId();

        // Push a private-name scope so method bodies inside this class can
        // resolve `#x` to its mangled own-property name.
        var privScope = new Dictionary<string, string>(StringComparer.Ordinal);
        _privateScopes.Push(privScope);
        try
        {
            // Collect every declared private name first so forward references
            // inside earlier method bodies resolve.
            CollectPrivateNames(body, classId, privScope);

            // Compile the constructor template (synthesizing one if absent).
            var ctorTemplate = CompileConstructorTemplate(name, baseExpr is not null, body, classId, out var ctorUpvalues);
            // Push ctor upvalues.
            EmitUpvaluePushes(ctorUpvalues);

            // Compile each method template.
            var methodEntries = new List<MethodEntry>();
            for (var i = 0; i < body.Methods.Count; i++)
            {
                var md = body.Methods[i];
                if (md.Computed)
                    throw new NotSupportedException("computed method names in class bodies are not supported in B1b-2a");
                var (entry, methodUps) = CompileMethodTemplate(md, classId);
                methodEntries.Add(entry);
                EmitUpvaluePushes(methodUps);
            }

            // Compile each field initializer (instance + static).
            var fieldEntries = new List<FieldEntry>();
            for (var i = 0; i < body.Fields.Count; i++)
            {
                var f = body.Fields[i];
                var (entry, fieldUps) = CompileFieldEntry(f, classId);
                fieldEntries.Add(entry);
                EmitUpvaluePushes(fieldUps);
            }

            // Compile each static block.
            var staticBlockEntries = new List<StaticBlockEntry>();
            for (var i = 0; i < body.StaticBlocks.Count; i++)
            {
                var (entry, sbUps) = CompileStaticBlockEntry(body.StaticBlocks[i]);
                staticBlockEntries.Add(entry);
                EmitUpvaluePushes(sbUps);
            }

            var template = new ClassTemplate(
                name?.Name ?? "",
                ctorTemplate,
                ctorUpvalues.Count,
                hasExtends: baseExpr is not null,
                methodEntries,
                fieldEntries,
                staticBlockEntries,
                classId);
            _b.EmitU16(Opcode.BuildClass, _b.AddConstant(template));
        }
        finally
        {
            _privateScopes.Pop();
        }
    }

    private static void CollectPrivateNames(ClassBody body, int classId, Dictionary<string, string> scope)
    {
        if (body.Constructor is not null) AddIfPrivate(body.Constructor.Key, classId, scope);
        foreach (var m in body.Methods) AddIfPrivate(m.Key, classId, scope);
        foreach (var f in body.Fields) AddIfPrivate(f.Key, classId, scope);

        static void AddIfPrivate(Expression key, int classId, Dictionary<string, string> scope)
        {
            if (key is PrivateNameExpression pne)
                scope[pne.Name] = ClassTemplate.MangledPrivateName(classId, pne.Name);
        }
    }

    private JsFunction CompileConstructorTemplate(
        Identifier? className, bool hasExtends, ClassBody body, int classId,
        out IReadOnlyList<UpvalueRef> upvalues)
    {
        var sub = new JsCompiler(parent: this);
        sub._privateScopes.Push(_privateScopes.Peek());
        sub._classMethodDepth = 1;
        MethodDefinition? userCtor = body.Constructor;
        IReadOnlyList<Expression> parameters;
        BlockStatement bodyBlock;
        var synthesizedDerived = false;
        if (userCtor is null)
        {
            // Synthesize a default constructor.
            if (hasExtends)
            {
                // Special-cased emission below — forwards all caller args via
                // Opcode.LoadCallerArgs to avoid needing rest-param support.
                synthesizedDerived = true;
                parameters = Array.Empty<Expression>();
                var pos = new JsPosition(0, 1, 1);
                bodyBlock = new BlockStatement(Array.Empty<Statement>(), pos, pos);
            }
            else
            {
                var pos = new JsPosition(0, 1, 1);
                parameters = Array.Empty<Expression>();
                bodyBlock = new BlockStatement(Array.Empty<Statement>(), pos, pos);
            }
        }
        else
        {
            parameters = userCtor.Params;
            bodyBlock = userCtor.Body;
        }

        sub.BindFunctionParameters(parameters);

        if (synthesizedDerived)
        {
            // super(...arguments) — push args array, CallSuperCtor, BindThis,
            // RunFieldInits. Mirrors EmitSuperCall's emission sequence.
            sub._b.Emit(Opcode.LoadCallerArgs);
            sub._b.Emit(Opcode.CallSuperCtor);
            sub._b.Emit(Opcode.Dup);
            sub._b.Emit(Opcode.BindThis);
            sub._b.Emit(Opcode.RunFieldInits);
            sub._b.Emit(Opcode.Pop);
        }
        else
        {
            // Prologue:
            //   Base class:    RunFieldInits before user body
            //   Derived class: deferred — emitted right after super(...)
            if (!hasExtends)
            {
                sub._b.Emit(Opcode.RunFieldInits);
            }

            foreach (var s in bodyBlock.Body) sub.EmitStatement(s);
        }

        sub._b.Emit(Opcode.ReturnUndefined);
        sub._privateScopes.Pop();

        var arity = CountSimpleParams(parameters);
        var ctorName = className?.Name ?? "";
        var chunk = sub._b.Build(ctorName);
        var template = new JsFunction(ctorName, chunk, arity)
        {
            ConstructorKind = hasExtends ? ClassConstructorKind.Derived : ClassConstructorKind.Base,
        };
        upvalues = sub._upvalues;
        return template;
    }

    private (MethodEntry Entry, IReadOnlyList<UpvalueRef> Upvalues) CompileMethodTemplate(
        MethodDefinition md, int classId)
    {
        var sub = new JsCompiler(parent: this);
        sub._privateScopes.Push(_privateScopes.Peek());
        sub._classMethodDepth = 1;
        sub.BindFunctionParameters(md.Params);
        foreach (var s in md.Body.Body) sub.EmitStatement(s);
        sub._b.Emit(Opcode.ReturnUndefined);
        sub._privateScopes.Pop();

        string keyName = md.Key switch
        {
            Identifier id => id.Name,
            StringLiteral sl => sl.Value,
            NumericLiteral nl => JsValue.ToStringValue(JsValue.Number(nl.Value)),
            PrivateNameExpression pne => pne.Name,
            _ => throw new NotSupportedException($"method key kind '{md.Key.GetType().Name}'"),
        };
        var arity = CountSimpleParams(md.Params);
        var chunk = sub._b.Build(keyName);
        var template = new JsFunction(keyName, chunk, arity);
        var kind = md.Kind switch
        {
            MethodKind.Get => ClassMethodKind.Get,
            MethodKind.Set => ClassMethodKind.Set,
            _ => ClassMethodKind.Method,
        };
        string? staticKey = null;
        string? mangled = null;
        if (md.Key is PrivateNameExpression pne2)
        {
            mangled = ClassTemplate.MangledPrivateName(classId, pne2.Name);
        }
        else
        {
            staticKey = keyName;
        }
        var entry = new MethodEntry(staticKey, mangled, kind, md.IsStatic, template, sub._upvalues.Count);
        return (entry, sub._upvalues);
    }

    private (FieldEntry Entry, IReadOnlyList<UpvalueRef> Upvalues) CompileFieldEntry(
        PropertyField field, int classId)
    {
        if (field.Computed)
            throw new NotSupportedException("computed field keys are not supported in B1b-2a");

        string? staticKey = null;
        string? mangled = null;
        if (field.Key is PrivateNameExpression pne)
        {
            mangled = ClassTemplate.MangledPrivateName(classId, pne.Name);
        }
        else
        {
            staticKey = field.Key switch
            {
                Identifier id => id.Name,
                StringLiteral sl => sl.Value,
                NumericLiteral nl => JsValue.ToStringValue(JsValue.Number(nl.Value)),
                _ => throw new NotSupportedException($"field key kind '{field.Key.GetType().Name}'"),
            };
        }

        if (field.Initializer is null)
        {
            return (new FieldEntry(staticKey, mangled, field.IsStatic, null, 0), Array.Empty<UpvalueRef>());
        }

        // Compile the initializer thunk: zero-arg function whose body is:
        //   this.<key> = <init>  (or DefinePrivateField for private fields).
        var sub = new JsCompiler(parent: this);
        sub._privateScopes.Push(_privateScopes.Peek());
        sub._classMethodDepth = 1;
        sub._b.Emit(Opcode.LoadThis);
        sub.EmitExpression(field.Initializer);
        if (mangled is not null)
        {
            sub._b.EmitU16(Opcode.DefinePrivateField, sub._b.AddConstant(mangled));
        }
        else
        {
            sub._b.EmitU16(Opcode.StoreProperty, sub._b.AddConstant(staticKey!));
            sub._b.Emit(Opcode.Pop);
        }
        sub._b.Emit(Opcode.ReturnUndefined);
        sub._privateScopes.Pop();
        var chunk = sub._b.Build($"#field-init:{(staticKey ?? mangled)}");
        var initTemplate = new JsFunction("", chunk, 0);
        return (new FieldEntry(staticKey, mangled, field.IsStatic, initTemplate, sub._upvalues.Count), sub._upvalues);
    }

    private (StaticBlockEntry Entry, IReadOnlyList<UpvalueRef> Upvalues) CompileStaticBlockEntry(BlockStatement block)
    {
        var sub = new JsCompiler(parent: this);
        sub._privateScopes.Push(_privateScopes.Peek());
        sub._classMethodDepth = 1;
        foreach (var s in block.Body) sub.EmitStatement(s);
        sub._b.Emit(Opcode.ReturnUndefined);
        sub._privateScopes.Pop();
        var chunk = sub._b.Build("#static-block");
        var tmpl = new JsFunction("", chunk, 0);
        return (new StaticBlockEntry(tmpl, sub._upvalues.Count), sub._upvalues);
    }

    private void EmitUpvaluePushes(IReadOnlyList<UpvalueRef> upvalues)
    {
        foreach (var u in upvalues)
        {
            if (u.IsLocalCapture) _b.Emit(Opcode.LoadLocal, (byte)u.Index);
            else _b.Emit(Opcode.LoadUpvalue, (byte)u.Index);
        }
    }

    // -------------------------------------------------------------- super.x
    private void EmitSuperProperty(SuperPropertyExpression sp)
    {
        if (sp.Computed)
            throw new NotSupportedException("computed super.[expr] is not supported in B1b-2a");
        var name = ((Identifier)sp.Property).Name;
        _b.EmitU16(Opcode.LoadSuperProperty, _b.AddConstant(name));
    }

    private void EmitSuperCall(SuperCallExpression sc)
    {
        // Marshal arguments as a single array and dispatch via CallSuperCtor.
        EmitArgsAsArray(sc.Arguments);
        _b.Emit(Opcode.CallSuperCtor);
        // Stack: [constructed]. Bind as this.
        _b.Emit(Opcode.Dup);
        _b.Emit(Opcode.BindThis);
        // Run instance field initializers immediately after super() per spec.
        _b.Emit(Opcode.RunFieldInits);
        // The value of `super(...)` as an expression is the constructed
        // object — leave it on the stack so an enclosing ExpressionStatement
        // can pop it.
    }

    /// <summary>Resolve a private name (<c>#name</c>) to its mangled own
    /// property key. Throws when the name is not declared in any enclosing
    /// class scope.</summary>
    internal string ResolvePrivateName(string privateName)
    {
        foreach (var scope in _privateScopes)
        {
            if (scope.TryGetValue(privateName, out var mangled)) return mangled;
        }
        for (var p = _parent; p is not null; p = p._parent)
        {
            foreach (var scope in p._privateScopes)
            {
                if (scope.TryGetValue(privateName, out var mangled)) return mangled;
            }
        }
        throw new InvalidOperationException(
            $"private name '{privateName}' is not declared in any enclosing class scope");
    }
}
