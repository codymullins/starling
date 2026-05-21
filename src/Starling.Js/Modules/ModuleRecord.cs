using Starling.Js.Bytecode;
using Starling.Js.Runtime;

namespace Starling.Js.Modules;

/// <summary>
/// ES2024 §16.2.1.5 Source Text Module Record (the subset Starling implements
/// today). Holds the compiled body, the static import/export metadata produced
/// by <see cref="JsCompiler.CompileModule"/>, and the mutable instantiation /
/// evaluation state that the linker (<see cref="ModuleLoader"/>) walks.
/// </summary>
/// <remarks>
/// Live-binding semantics are realised through <see cref="Cell"/> storage: every
/// local top-level binding of a module is a cell (allocated by the compiled body
/// at run time). An <c>import { x } from "./m"</c> resolves <c>x</c> to the same
/// cell the exporting module writes, so a later mutation in the exporter is
/// visible to the importer — exactly the ECMAScript "indirect binding" contract.
/// </remarks>
public sealed class ModuleRecord
{
    /// <summary>Absolute, resolved specifier this record was loaded from. Acts
    /// as the module map key (one record per resolved URL).</summary>
    public string Url { get; }

    /// <summary>Compiled module body. Parameter / upvalue slots correspond to
    /// the imported-binding cells supplied at instantiation, in
    /// <see cref="ImportEntries"/> order.</summary>
    public Chunk Body { get; }

    /// <summary>Static imports declared by this module (ES2024 §16.2.1.3).</summary>
    public IReadOnlyList<ModuleImportEntry> ImportEntries { get; }

    /// <summary>Names exported by this module — local, indirect (re-export of a
    /// single name), and star (re-export-all) forms (ES2024 §16.2.1.4).</summary>
    public IReadOnlyList<ModuleExportEntry> ExportEntries { get; }

    /// <summary>Distinct module specifiers this module depends on, in first-seen
    /// order. Used by the linker to drive depth-first instantiation.</summary>
    public IReadOnlyList<string> RequestedModules { get; }

    /// <summary>Live binding cells for this module's own top-level declarations,
    /// keyed by local binding name. Populated when the body runs (the compiled
    /// body publishes each captured cell here). Imported names are NOT in this
    /// map — they resolve through the exporting module.</summary>
    internal Dictionary<string, Cell> LocalBindings { get; } = new(StringComparer.Ordinal);

    /// <summary>The Module Namespace Exotic Object (§10.4.6) for this module,
    /// lazily built on first <c>import * as ns</c> / namespace request.</summary>
    public JsObject? Namespace { get; internal set; }

    /// <summary>wp:M3-03c — the module's <c>import.meta</c> object (§16.2.1.9
    /// HostGetImportMetaProperties), lazily built by the loader on first
    /// <c>import.meta</c> access. Carries at least <c>url</c> (the resolved
    /// module URL). Null until first accessed.</summary>
    internal JsObject? Meta { get; set; }

    /// <summary>§16.2.1.5 [[Status]] subset driving the linker state machine.</summary>
    public ModuleStatus Status { get; internal set; } = ModuleStatus.Unlinked;

    /// <summary>The runtime closure created at instantiation: the body wired to
    /// its imported-binding upvalue cells. Invoked once during evaluation.</summary>
    internal JsFunction? Instance { get; set; }

    /// <summary>Captured throw from evaluation, if the module errored
    /// (§16.2.1.5 [[EvaluationError]]).</summary>
    internal JsThrow? EvaluationError { get; set; }

    /// <summary>wp:M3-03b — true when this module's body uses a top-level
    /// <c>await</c> (or transitively, when it must await an async dependency).
    /// When set the loader builds <see cref="Instance"/> as an
    /// <see cref="JsFunctionKind.Async"/> function, so evaluating the body returns
    /// a Promise and the body suspends at each <c>await</c>.</summary>
    internal bool HasTopLevelAwait { get; set; }

    /// <summary>wp:M3-03b — §16.2.1.5 [[CycleRoot]] / async-evaluation slot. For
    /// an async module this is the Promise returned by invoking the async body;
    /// it settles when the body finishes (or rejects on a top-level-await
    /// rejection / synchronous throw). Null for a synchronous module (no
    /// async settling needed). The loader chains dependents on this so an
    /// importer's body waits for this module to finish.
    /// <para>wp:M3-03d — for a module that belongs to an <em>async</em> strongly
    /// connected component (a cycle containing a top-level-await module), this
    /// holds the SCC's shared settlement promise: every member of the SCC is
    /// assigned the same promise, so a dependent that chains on any member waits
    /// for the whole cycle to settle (no partial-binding read of an in-flight
    /// async member).</para></summary>
    internal JsPromise? EvaluationPromise { get; set; }

    // -----------------------------------------------------------------------
    // wp:M3-03d — async-module cycle (SCC) ordering. A pragmatic Tarjan-style
    // strongly-connected-component grouping over the evaluation DFS. Full
    // [[DFSIndex]] fidelity is approximated: we record DFS discovery / low-link
    // indices on the stack so an SCC root can gather its members and settle the
    // whole component jointly when it contains an async (TLA) module.
    // -----------------------------------------------------------------------

    /// <summary>§16.2.1.5 [[DFSIndex]] — discovery order during the evaluation
    /// DFS. -1 until visited.</summary>
    internal int DfsIndex { get; set; } = -1;

    /// <summary>§16.2.1.5 [[DFSAncestorIndex]] — Tarjan low-link: the lowest
    /// <see cref="DfsIndex"/> reachable through this module's subtree, used to
    /// detect the root of a strongly connected component (cycle). -1 until
    /// visited.</summary>
    internal int DfsAncestorIndex { get; set; } = -1;

    /// <summary>True while this module sits on the evaluation DFS stack — i.e. a
    /// dependency edge that lands here is a cyclic back-edge.</summary>
    internal bool OnEvalStack { get; set; }

    /// <summary>§16.2.1.5 [[AsyncEvaluation]] — set once this module is known to
    /// be part of an async evaluation (its own top-level await, or membership in
    /// a strongly connected component that contains a top-level-await module).
    /// Drives the deferred, jointly-settled SCC body execution.</summary>
    internal bool AsyncEvaluation { get; set; }

    public ModuleRecord(
        string url,
        Chunk body,
        IReadOnlyList<ModuleImportEntry> importEntries,
        IReadOnlyList<ModuleExportEntry> exportEntries,
        IReadOnlyList<string> requestedModules)
    {
        Url = url;
        Body = body;
        ImportEntries = importEntries;
        ExportEntries = exportEntries;
        RequestedModules = requestedModules;
    }
}

/// <summary>§16.2.1.5 [[Status]] — Starling tracks the linking/evaluation phases
/// that matter for cycle handling and idempotent evaluation.</summary>
public enum ModuleStatus
{
    Unlinked,
    Linking,
    Linked,
    Evaluating,
    Evaluated,
}

/// <summary>ES2024 §16.2.1.3 ImportEntry Record. A single local binding bound to
/// a name imported from <see cref="ModuleRequest"/>.</summary>
/// <param name="ModuleRequest">The (unresolved) specifier the import targets.</param>
/// <param name="ImportName">The exported name to bind, or
/// <see cref="ModuleImportEntry.NamespaceImport"/> for <c>import * as ns</c>.</param>
/// <param name="LocalName">The local binding name introduced in this module.</param>
public sealed record ModuleImportEntry(string ModuleRequest, string ImportName, string LocalName)
{
    /// <summary>Sentinel <see cref="ImportName"/> for a namespace import
    /// (<c>import * as ns from "..."</c>).</summary>
    public const string NamespaceImport = "*";

    public bool IsNamespace => ImportName == NamespaceImport;
}

/// <summary>ES2024 §16.2.1.4 ExportEntry Record covering local, indirect
/// (re-export of one name) and star (re-export-all) forms.</summary>
/// <param name="ExportName">The name visible to importers, or null for an
/// anonymous <c>export * from</c>.</param>
/// <param name="ModuleRequest">Non-null for re-exports; the specifier to forward
/// to.</param>
/// <param name="ImportName">For an indirect re-export, the name to pull from the
/// requested module; <see cref="ModuleExportEntry.StarReexport"/> for
/// <c>export *</c>.</param>
/// <param name="LocalName">For a local export, the local binding name backing
/// the export; null otherwise.</param>
public sealed record ModuleExportEntry(
    string? ExportName,
    string? ModuleRequest,
    string? ImportName,
    string? LocalName)
{
    /// <summary>Sentinel <see cref="ImportName"/> for an <c>export * from</c>
    /// re-export-all entry.</summary>
    public const string StarReexport = "*";

    public bool IsLocal => ModuleRequest is null;
    public bool IsStar => ImportName == StarReexport;
}
