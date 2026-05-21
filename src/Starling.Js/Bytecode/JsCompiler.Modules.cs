using Starling.Js.Ast;
using Starling.Js.Modules;
using Starling.Js.Runtime;

namespace Starling.Js.Bytecode;

/// <summary>
/// ES2024 §16.2 module-record compilation. Lowers a parsed module
/// <see cref="Program"/> into a <see cref="Chunk"/> plus the static
/// import/export metadata the linker needs.
/// </summary>
/// <remarks>
/// <para>
/// All module handling lives in this partial so the classic-script compiler in
/// <c>JsCompiler.cs</c> stays untouched (a sibling agent edits that file for
/// destructuring assignment). The two paths never overlap: classic scripts route
/// top-level bindings to the realm global; modules route every top-level binding
/// to a <see cref="Cell"/> upvalue supplied by the loader at instantiation.
/// </para>
/// <para>
/// <b>Live bindings.</b> Each top-level binding (local declaration <em>and</em>
/// imported name) maps to an upvalue slot. The loader allocates one
/// <see cref="Cell"/> per local binding and reuses the exporting module's cell
/// for an imported name, so reads/writes on either side observe the same
/// storage — the ECMAScript indirect-binding contract. <c>LoadUpvalue</c> /
/// <c>StoreUpvalue</c> (already supported by the VM) do the dereferencing.
/// </para>
/// <para>
/// <b>Deferred.</b> Top-level <c>await</c> is not lowered here (the loader
/// reports it); see <see cref="ModuleLoader"/>. <c>import.meta</c> and dynamic
/// <c>import()</c> are out of scope for this slice.
/// </para>
/// </remarks>
public sealed partial class JsCompiler
{
    /// <summary>Module top-level binding name → upvalue index. Populated before
    /// emission so <see cref="EmitIdLoad"/> / <see cref="StoreBindingIdentifier"/>
    /// resolve module bindings as upvalues (cells) rather than globals.</summary>
    private Dictionary<string, int>? _moduleBindingUpvalues;

    /// <summary>Compile a module body. Returns the chunk plus the static
    /// import/export tables. The chunk expects one upvalue per entry in the
    /// returned binding-order list (locals first, then imports), supplied by the
    /// loader as <see cref="Cell"/>s at instantiation.</summary>
    public static ModuleCompilation CompileModule(Program program, string? name = "<module>")
    {
        // Parent a throwaway root so IsScriptTop is false (top-level bindings
        // become non-global) and the upvalue machinery is available.
        var root = new JsCompiler();
        var c = new JsCompiler(parent: root) { _moduleBindingUpvalues = new(StringComparer.Ordinal) };
        return c.EmitModule(program, name);
    }

    private ModuleCompilation EmitModule(Program program, string? name)
    {
        var imports = new List<ModuleImportEntry>();
        var exports = new List<ModuleExportEntry>();
        var requested = new List<string>();
        var bindingOrder = new List<ModuleBindingSlot>();

        CollectImportEntries(program.Body, imports, requested);
        CollectExportEntries(program.Body, exports, requested);

        // 1) Reserve an upvalue slot for every local top-level binding name…
        var localNames = new List<string>();
        CollectLocalBindingNames(program.Body, localNames);
        foreach (var local in localNames)
            ReserveModuleBinding(local, isImport: false, bindingOrder);

        // 2) …then one per imported local name (each resolves to the exporting
        //    module's cell at instantiation).
        foreach (var imp in imports)
            ReserveModuleBinding(imp.LocalName, isImport: true, bindingOrder, imp);

        // Hoist module-top function declarations (they bind into the upvalue
        // cell so importers see them, and so they are callable before their
        // textual position).
        HoistModuleFunctionDeclarations(program.Body);

        foreach (var stmt in program.Body)
            EmitModuleItem(stmt);

        _b.Emit(Opcode.Halt);
        var chunk = _b.Build(name);
        return new ModuleCompilation(chunk, imports, exports, requested, bindingOrder);
    }

    /// <summary>Register a module top-level binding name as an upvalue. The
    /// returned index is the slot the loader fills with the binding's
    /// <see cref="Cell"/>.</summary>
    private void ReserveModuleBinding(
        string name, bool isImport, List<ModuleBindingSlot> order, ModuleImportEntry? import = null)
    {
        if (_moduleBindingUpvalues!.ContainsKey(name)) return; // first binding wins
        var idx = order.Count;
        _moduleBindingUpvalues[name] = idx;
        _upvalues.Add(new UpvalueRef(IsLocalCapture: false, Index: idx));
        _upvalueByName[name] = idx;
        order.Add(new ModuleBindingSlot(name, isImport, import));
    }

    // -----------------------------------------------------------------------
    // Static metadata collection (ES2024 §16.2.1.3 / §16.2.1.4)
    // -----------------------------------------------------------------------

    private static void CollectImportEntries(
        IReadOnlyList<Statement> body, List<ModuleImportEntry> imports, List<string> requested)
    {
        foreach (var s in body)
        {
            if (s is not ImportDeclaration imp) continue;
            AddRequested(requested, imp.Source);
            foreach (var spec in imp.Specifiers)
            {
                switch (spec)
                {
                    case ImportDefaultSpecifier d:
                        imports.Add(new ModuleImportEntry(imp.Source, "default", d.Local.Name));
                        break;
                    case ImportNamespaceSpecifier ns:
                        imports.Add(new ModuleImportEntry(imp.Source, ModuleImportEntry.NamespaceImport, ns.Local.Name));
                        break;
                    case ImportNamedSpecifier named:
                        imports.Add(new ModuleImportEntry(imp.Source, ModuleExportName(named.Imported), named.Local.Name));
                        break;
                    case ImportSideEffectSpecifier:
                        break; // import "x"; — dependency only, no binding
                }
            }
        }
    }

    private static void CollectExportEntries(
        IReadOnlyList<Statement> body, List<ModuleExportEntry> exports, List<string> requested)
    {
        foreach (var s in body)
        {
            switch (s)
            {
                case ExportLocalDeclaration local:
                    foreach (var nameOfLocal in DeclaredNames(local.Declaration))
                        exports.Add(new ModuleExportEntry(nameOfLocal, null, null, nameOfLocal));
                    break;
                case ExportDefaultDeclaration:
                    exports.Add(new ModuleExportEntry("default", null, null, DefaultBindingName));
                    break;
                case ExportNamedDeclaration named:
                    if (named.Source is not null) AddRequested(requested, named.Source);
                    foreach (var spec in named.Specifiers)
                    {
                        var exportName = ModuleExportName(spec.Exported);
                        if (named.Source is null)
                            exports.Add(new ModuleExportEntry(exportName, null, null, ModuleExportName(spec.Local)));
                        else
                            exports.Add(new ModuleExportEntry(exportName, named.Source, ModuleExportName(spec.Local), null));
                    }
                    break;
                case ExportAllDeclaration all:
                    AddRequested(requested, all.Source);
                    exports.Add(new ModuleExportEntry(
                        all.ExportedName?.Name, all.Source, ModuleExportEntry.StarReexport, null));
                    break;
            }
        }
    }

    /// <summary>Local binding name for the synthetic <c>default</c> export.</summary>
    internal const string DefaultBindingName = "*default*";

    private static void AddRequested(List<string> requested, string spec)
    {
        if (!requested.Contains(spec)) requested.Add(spec);
    }

    private static string ModuleExportName(Expression e) => e switch
    {
        Identifier id => id.Name,
        StringLiteral s => s.Value,
        _ => throw new InvalidOperationException("unsupported module export name node"),
    };

    /// <summary>Names introduced by a local export declaration (var/let/const,
    /// function, class).</summary>
    private static IEnumerable<string> DeclaredNames(Statement declaration)
    {
        switch (declaration)
        {
            case VariableDeclaration vd:
                foreach (var d in vd.Declarations)
                    foreach (var n in PatternNames(d.Id)) yield return n;
                break;
            case FunctionDeclaration fd: yield return fd.Name.Name; break;
            case ClassDeclaration cd: yield return cd.Name.Name; break;
        }
    }

    private static IEnumerable<string> PatternNames(Expression pattern)
    {
        switch (pattern)
        {
            case Identifier id: yield return id.Name; break;
            case AssignmentPattern a:
                foreach (var n in PatternNames(a.Target)) yield return n;
                break;
            case ArrayPattern arr:
                foreach (var el in arr.Elements)
                {
                    var target = el switch
                    {
                        ArrayPatternBindingElement b => b.Target,
                        ArrayPatternRestElement r => r.Target,
                        _ => null,
                    };
                    if (target is null) continue;
                    foreach (var n in PatternNames(target)) yield return n;
                }
                break;
            case ObjectPattern obj:
                foreach (var prop in obj.Properties)
                    foreach (var n in PatternNames(prop.Target)) yield return n;
                if (obj.Rest is not null)
                    foreach (var n in PatternNames(obj.Rest.Argument)) yield return n;
                break;
        }
    }

    /// <summary>Every top-level binding name a module declares locally
    /// (var/let/const/function/class, plus default-export bindings) — these get
    /// loader-allocated cells.</summary>
    private static void CollectLocalBindingNames(IReadOnlyList<Statement> body, List<string> names)
    {
        void Add(string n) { if (!names.Contains(n)) names.Add(n); }
        foreach (var s in body)
        {
            switch (s)
            {
                case VariableDeclaration vd:
                    foreach (var d in vd.Declarations)
                        foreach (var n in PatternNames(d.Id)) Add(n);
                    break;
                case FunctionDeclaration fd: Add(fd.Name.Name); break;
                case ClassDeclaration cd: Add(cd.Name.Name); break;
                case ExportLocalDeclaration local:
                    foreach (var n in DeclaredNames(local.Declaration)) Add(n);
                    break;
                case ExportDefaultDeclaration: Add(DefaultBindingName); break;
            }
        }
    }

    // -----------------------------------------------------------------------
    // Emission
    // -----------------------------------------------------------------------

    /// <summary>Module-mode function-declaration hoist: compile each top-level
    /// function body, build the closure, and store it into the function name's
    /// upvalue cell so it is callable before its textual site and visible to
    /// importers.</summary>
    private void HoistModuleFunctionDeclarations(IReadOnlyList<Statement> body)
    {
        foreach (var s in body)
        {
            var fd = s switch
            {
                FunctionDeclaration f => f,
                ExportLocalDeclaration { Declaration: FunctionDeclaration f } => f,
                _ => null,
            };
            if (fd is null) continue;
            EmitModuleFunctionValue(fd);
            StoreModuleBinding(fd.Name.Name);
        }
    }

    private void EmitModuleFunctionValue(FunctionDeclaration fd)
    {
        var sub = new JsCompiler(parent: this);
        sub.RunCaptureAnalysisForFunction(fd.Params, fd.Body.Body);
        sub.EmitFunctionBody(fd);
        var chunk = sub._b.Build(fd.Name.Name);
        EmitFunctionConstructor(fd.Name.Name, chunk,
            CountSimpleParams(fd.Params), sub._upvalues,
            ResolveFunctionKind(fd.Async, fd.Generator));
    }

    /// <summary>Emit a write of the top-of-stack value into a module top-level
    /// binding's upvalue cell.</summary>
    private void StoreModuleBinding(string name)
    {
        if (!_moduleBindingUpvalues!.TryGetValue(name, out var idx))
            throw new InvalidOperationException($"module binding '{name}' not reserved");
        _b.Emit(Opcode.StoreUpvalue, (byte)idx);
    }

    private void EmitModuleItem(Statement s)
    {
        switch (s)
        {
            case ImportDeclaration:
                return; // bindings supplied as upvalues; nothing to emit
            case FunctionDeclaration:
                return; // already hoisted
            case ExportLocalDeclaration local:
                EmitExportLocal(local.Declaration);
                return;
            case ExportDefaultDeclaration def:
                EmitExportDefault(def);
                return;
            case ExportNamedDeclaration:
            case ExportAllDeclaration:
                // Pure re-export / specifier-only list: no runtime code; the
                // export tables already capture the binding wiring.
                return;
            case VariableDeclaration vd:
                // Module-top var/let/const (exported or not) route to upvalue
                // cells so every top-level binding is consistently a live cell.
                EmitModuleVarDecl(vd);
                return;
            case ClassDeclaration cd:
                EmitModuleClassDeclaration(cd.Name.Name, cd);
                return;
            default:
                EmitStatement(s);
                return;
        }
    }

    private void EmitExportLocal(Statement declaration)
    {
        switch (declaration)
        {
            case FunctionDeclaration:
                return; // hoisted by HoistModuleFunctionDeclarations
            case VariableDeclaration vd:
                EmitModuleVarDecl(vd);
                return;
            case ClassDeclaration cd:
                EmitModuleClassDeclaration(cd.Name.Name, cd);
                return;
            default:
                EmitStatement(declaration);
                return;
        }
    }

    private void EmitExportDefault(ExportDefaultDeclaration def)
    {
        switch (def.Declaration)
        {
            case FunctionExpression fe:
                EmitExpression(fe);
                break;
            case ClassExpression ce:
                EmitExpression(ce);
                break;
            case Expression expr:
                EmitExpression(expr);
                break;
            default:
                throw new InvalidOperationException("unsupported default export declaration");
        }
        StoreModuleBinding(DefaultBindingName);
    }

    /// <summary>Module-scope variable declaration — initializers write through
    /// the binding's upvalue cell instead of the global object.</summary>
    /// <remarks>
    /// Single-identifier targets store directly into the reserved upvalue cell.
    /// Destructuring binding patterns (<c>const { a, b } = obj</c>,
    /// <c>let [x, ...rest] = arr</c>, with nesting/defaults/rest) reuse the
    /// function-scope pattern lowering via <see cref="EmitDestructuringFromStack"/>:
    /// each bound name was already reserved as a module upvalue by
    /// <see cref="CollectLocalBindingNames"/> → <see cref="ReserveModuleBinding"/>,
    /// so the leaf store resolves through <c>StoreUpvalue</c> (the module's live
    /// cell) rather than a shadowing local — preserving live bindings even when
    /// the names are re-exported.
    /// </remarks>
    private void EmitModuleVarDecl(VariableDeclaration vd)
    {
        foreach (var d in vd.Declarations)
        {
            if (d.Init is null) continue;
            if (d.Id is Identifier id && _moduleBindingUpvalues!.ContainsKey(id.Name))
            {
                EmitExpression(d.Init);
                StoreModuleBinding(id.Name);
            }
            else
            {
                // Destructuring binding pattern: push the source value and let
                // the shared pattern walker extract each name into its module
                // upvalue cell. No DeclarePatternBindings call → no shadowing
                // local, so live bindings stay intact (the subtlety the prior
                // implementation's NotSupportedException guarded against).
                EmitExpression(d.Init);
                EmitDestructuringFromStack(d.Id);
            }
        }
    }

    /// <summary>Module-scope class declaration — bind the class value into its
    /// upvalue cell (the classic path writes to the global object).</summary>
    private void EmitModuleClassDeclaration(string name, ClassDeclaration cd)
    {
        EmitClassValue(cd.Name, cd.BaseClass, cd.Body);
        StoreModuleBinding(name);
    }
}

/// <summary>Result of compiling one module: the body chunk plus the static
/// metadata the loader threads into a <see cref="ModuleRecord"/>.</summary>
/// <param name="Chunk">Compiled module body. Expects one upvalue cell per
/// <see cref="BindingOrder"/> entry, in order.</param>
/// <param name="Imports">Static import entries (§16.2.1.3).</param>
/// <param name="Exports">Static export entries (§16.2.1.4).</param>
/// <param name="RequestedModules">Distinct dependency specifiers, first-seen order.</param>
/// <param name="BindingOrder">Upvalue slot order: each entry says which binding
/// the slot at that index holds (local cell vs. imported cell).</param>
public sealed record ModuleCompilation(
    Chunk Chunk,
    IReadOnlyList<ModuleImportEntry> Imports,
    IReadOnlyList<ModuleExportEntry> Exports,
    IReadOnlyList<string> RequestedModules,
    IReadOnlyList<ModuleBindingSlot> BindingOrder);

/// <summary>One upvalue slot in a compiled module body. Locals get a fresh cell
/// from the loader; imports reuse the exporting module's cell, identified by
/// <see cref="Import"/>.</summary>
/// <param name="Name">Local binding name this slot backs.</param>
/// <param name="IsImport">True when the slot is an imported binding.</param>
/// <param name="Import">Import metadata when <see cref="IsImport"/>; null for locals.</param>
public sealed record ModuleBindingSlot(string Name, bool IsImport, ModuleImportEntry? Import);
