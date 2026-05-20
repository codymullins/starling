using Starling.Js.Bytecode;
using Starling.Js.Parse;
using Starling.Js.Runtime;

namespace Starling.Js.Modules;

/// <summary>
/// ES2024 §16.2.1 module loader: resolves specifiers, fetches + compiles source
/// text, links the dependency graph depth-first (with cycle handling), and
/// evaluates modules in dependency order.
/// </summary>
/// <remarks>
/// <para>
/// The loader is host-agnostic: a <see cref="IModuleHost"/> supplies specifier
/// resolution and source fetching, so the engine can plug in its file:// + http
/// fetch mechanism while tests use an in-memory map.
/// </para>
/// <para>
/// Live bindings are realised by sharing <see cref="Cell"/>s between modules
/// (see <see cref="ModuleRecord"/>). Linking allocates one cell per local
/// binding of each module; an imported name reuses the exporting module's cell.
/// </para>
/// <para>
/// <b>Deferred.</b> Top-level <c>await</c> is detected (a module whose body
/// uses it) only insofar as the synchronous evaluation contract holds: a module
/// graph that needs async settling is evaluated synchronously and its microtasks
/// drained by the host. Dynamic <c>import()</c> and <c>import.meta</c> are out of
/// scope for this slice.
/// </para>
/// </remarks>
public sealed class ModuleLoader
{
    private readonly JsRuntime _runtime;
    private readonly IModuleHost _host;
    private readonly Dictionary<string, ModuleRecord> _registry = new(StringComparer.Ordinal);

    public ModuleLoader(JsRuntime runtime, IModuleHost host)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>Resolve, load, link and evaluate the module at
    /// <paramref name="specifier"/> (resolved relative to
    /// <paramref name="referrer"/>). Returns the evaluated record. Re-entrant:
    /// an already-evaluated module is returned from the registry without
    /// re-running.</summary>
    public ModuleRecord LoadAndEvaluate(string specifier, string? referrer = null)
    {
        var record = LoadGraph(specifier, referrer);
        Link(record);
        Evaluate(record);
        return record;
    }

    // -----------------------------------------------------------------------
    // Phase 1 — load + parse + compile the whole graph (§16.2.1.5.1 step graph)
    // -----------------------------------------------------------------------

    private ModuleRecord LoadGraph(string specifier, string? referrer)
    {
        var url = ResolveOrThrow(specifier, referrer);
        if (_registry.TryGetValue(url, out var existing)) return existing;

        var source = _host.FetchSource(url)
            ?? throw new JsThrow(_runtime.Realm.NewError(
                _runtime.Realm.TypeErrorPrototype, $"Failed to fetch module: {url}"));

        var program = new JsParser(source).ParseProgram();
        var compiled = JsCompiler.CompileModule(program, url);

        var record = new ModuleRecord(
            url, compiled.Chunk, compiled.Imports, compiled.Exports, compiled.RequestedModules);
        // Stash the binding order on the record via a side table so Link can
        // wire upvalue cells in slot order.
        _bindingOrders[record] = compiled.BindingOrder;
        _registry[url] = record;

        // Recurse into dependencies (depth-first) so the whole graph is present
        // before linking. Cycles terminate via the registry check above.
        foreach (var dep in record.RequestedModules)
            LoadGraph(dep, url);

        return record;
    }

    private readonly Dictionary<ModuleRecord, IReadOnlyList<ModuleBindingSlot>> _bindingOrders = new();

    // -----------------------------------------------------------------------
    // Phase 2 — link (§16.2.1.5.2 Link): allocate cells, resolve imports,
    // instantiate closures. Depth-first with cycle handling via [[Status]].
    // -----------------------------------------------------------------------

    private void Link(ModuleRecord record)
    {
        if (record.Status is ModuleStatus.Linked or ModuleStatus.Evaluating or ModuleStatus.Evaluated)
            return;
        if (record.Status == ModuleStatus.Linking)
            return; // already on the stack (cycle) — its cells exist; defer wiring is done below
        record.Status = ModuleStatus.Linking;

        // Allocate one live-binding cell per local declaration so importers can
        // alias them even while a cycle is mid-link.
        EnsureLocalCells(record);

        // Link dependencies first (depth-first) so their export cells exist.
        foreach (var dep in record.RequestedModules)
            Link(Resolve(dep, record.Url));

        // Build the upvalue table for the body in BindingOrder slot order.
        var order = _bindingOrders[record];
        var upvalues = new JsValue[order.Count];
        for (var i = 0; i < order.Count; i++)
        {
            var slot = order[i];
            Cell cell = slot.IsImport
                ? ResolveImportCell(record, slot.Import!)
                : record.LocalBindings[slot.Name];
            upvalues[i] = JsValue.Object(cell);
        }

        record.Instance = new JsFunction(record.Url, record.Body, arityDeclared: 0, upvalues);
        record.Status = ModuleStatus.Linked;
    }

    /// <summary>§16.2.1.5.3-style ResolveExport, reduced to cell resolution: find
    /// the <see cref="Cell"/> that backs <paramref name="entry"/>'s imported name
    /// in the exporting module (following indirect/star re-exports).</summary>
    private Cell ResolveImportCell(ModuleRecord importer, ModuleImportEntry entry)
    {
        var target = Resolve(entry.ModuleRequest, importer.Url);
        // Ensure the target's cells exist (it may be later in link order under a
        // cycle); allocating here is idempotent with EnsureLocalCells.
        EnsureLocalCells(target);

        if (entry.IsNamespace)
        {
            // Namespace import: back the local name with a cell holding the
            // module namespace object (built lazily, after the target links).
            var ns = GetOrBuildNamespace(target);
            return new Cell(JsValue.Object(ns));
        }

        var cell = ResolveExportCell(target, entry.ImportName, new HashSet<string>(StringComparer.Ordinal));
        return cell ?? throw new JsThrow(_runtime.Realm.NewSyntaxError(
            $"The requested module '{entry.ModuleRequest}' does not provide an export named '{entry.ImportName}'"));
    }

    /// <summary>Resolve the cell backing exported <paramref name="exportName"/> in
    /// <paramref name="module"/>, following indirect and star re-exports.</summary>
    private Cell? ResolveExportCell(ModuleRecord module, string exportName, HashSet<string> seen)
    {
        if (!seen.Add(module.Url + "\0" + exportName)) return null; // cycle in re-exports

        // 1) Local export (export const/function/class, export { x }).
        foreach (var e in module.ExportEntries)
        {
            if (e.IsLocal && e.ExportName == exportName && e.LocalName is not null)
            {
                EnsureLocalCells(module);
                if (module.LocalBindings.TryGetValue(e.LocalName, out var local)) return local;
            }
        }

        // 2) Indirect re-export (export { x } from "..."; export { x as y } from).
        foreach (var e in module.ExportEntries)
        {
            if (!e.IsLocal && !e.IsStar && e.ExportName == exportName && e.ImportName is not null)
            {
                var dep = Resolve(e.ModuleRequest!, module.Url);
                var c = ResolveExportCell(dep, e.ImportName, seen);
                if (c is not null) return c;
            }
        }

        // 3) Star re-export (export * from "..."). Search each star target for
        //    the name (named star re-exports — export * as ns — bind a namespace
        //    and are handled as a local export above once built).
        foreach (var e in module.ExportEntries)
        {
            if (!e.IsLocal && e.IsStar && e.ExportName is null)
            {
                var dep = Resolve(e.ModuleRequest!, module.Url);
                var c = ResolveExportCell(dep, exportName, seen);
                if (c is not null) return c;
            }
        }

        return null;
    }

    // -----------------------------------------------------------------------
    // Phase 3 — evaluate (§16.2.1.5.4): depth-first, idempotent, cycle-safe.
    // -----------------------------------------------------------------------

    private void Evaluate(ModuleRecord record)
    {
        if (record.Status is ModuleStatus.Evaluating or ModuleStatus.Evaluated)
        {
            if (record.EvaluationError is not null) throw record.EvaluationError;
            return;
        }
        record.Status = ModuleStatus.Evaluating;

        try
        {
            // Evaluate dependencies first so their bindings are initialised
            // before this module's body reads them. The Evaluating guard above
            // breaks cycles: a cyclic dependency observes whatever bindings the
            // partially-run module has published so far (spec behavior).
            foreach (var dep in record.RequestedModules)
                Evaluate(Resolve(dep, record.Url));

            var vm = _runtime.Realm.ActiveVm ?? new JsVm(_runtime);
            vm.CallFunction(record.Instance!, JsValue.Undefined, Array.Empty<JsValue>());
        }
        catch (JsThrow ex)
        {
            record.EvaluationError = ex;
            record.Status = ModuleStatus.Evaluated;
            throw;
        }

        record.Status = ModuleStatus.Evaluated;
    }

    // -----------------------------------------------------------------------
    // Module Namespace Exotic Object (§10.4.6) — live-binding accessor view.
    // -----------------------------------------------------------------------

    /// <summary>Build (or return cached) the namespace object for a module:
    /// every exported name is an accessor property whose getter reads the live
    /// backing cell.</summary>
    public JsObject GetOrBuildNamespace(ModuleRecord module)
    {
        if (module.Namespace is not null) return module.Namespace;

        var ns = _runtime.Realm.NewObjectWithProto(null);
        foreach (var name in ExportedNames(module, new HashSet<string>(StringComparer.Ordinal)))
        {
            var cell = ResolveExportCell(module, name, new HashSet<string>(StringComparer.Ordinal));
            if (cell is null) continue;
            var getter = new JsNativeFunction(_runtime.Realm, $"get {name}", length: 0,
                (_, _) => cell.Value, isConstructor: false);
            ns.DefineOwnProperty(name,
                PropertyDescriptor.Accessor(getter, setter: null, enumerable: true, configurable: false));
        }
        module.Namespace = ns;
        return ns;
    }

    /// <summary>Collect every name exported by a module, expanding
    /// <c>export *</c> re-exports (excluding <c>default</c>, per spec).</summary>
    private IEnumerable<string> ExportedNames(ModuleRecord module, HashSet<string> visited)
    {
        if (!visited.Add(module.Url)) yield break;
        var names = new List<string>();
        void Add(string n) { if (!names.Contains(n)) names.Add(n); }

        foreach (var e in module.ExportEntries)
        {
            if (e.ExportName is not null && !e.IsStar) Add(e.ExportName);
            else if (e.IsStar && e.ExportName is not null) Add(e.ExportName); // export * as ns
        }
        foreach (var e in module.ExportEntries)
        {
            if (e.IsStar && e.ExportName is null)
            {
                var dep = Resolve(e.ModuleRequest!, module.Url);
                foreach (var n in ExportedNames(dep, visited))
                    if (n != "default") Add(n);
            }
        }
        foreach (var n in names) yield return n;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>Allocate a live-binding <see cref="Cell"/> for every local
    /// (non-imported) top-level binding of a module. Idempotent.</summary>
    private void EnsureLocalCells(ModuleRecord record)
    {
        foreach (var slot in _bindingOrders[record])
            if (!slot.IsImport)
                record.LocalBindings.TryAdd(slot.Name, new Cell(JsValue.Undefined));
    }

    private ModuleRecord Resolve(string specifier, string referrer)
    {
        var url = ResolveOrThrow(specifier, referrer);
        return _registry.TryGetValue(url, out var rec)
            ? rec
            : throw new JsThrow(_runtime.Realm.NewError(
                _runtime.Realm.TypeErrorPrototype, $"Module not loaded: {url}"));
    }

    private string ResolveOrThrow(string specifier, string? referrer) =>
        _host.Resolve(specifier, referrer)
        ?? throw new JsThrow(_runtime.Realm.NewError(
            _runtime.Realm.TypeErrorPrototype, $"Failed to resolve module specifier: {specifier}"));
}

/// <summary>Host hook for specifier resolution and source fetching. Lets the
/// engine plug its file:// + http fetch path in while tests use an in-memory
/// map.</summary>
public interface IModuleHost
{
    /// <summary>Resolve <paramref name="specifier"/> relative to
    /// <paramref name="referrer"/> (the absolute URL of the importing module, or
    /// null for the entry module) into an absolute module-map key. Return null on
    /// failure.</summary>
    string? Resolve(string specifier, string? referrer);

    /// <summary>Fetch the source text for an already-resolved module URL. Return
    /// null on failure.</summary>
    string? FetchSource(string resolvedUrl);
}
