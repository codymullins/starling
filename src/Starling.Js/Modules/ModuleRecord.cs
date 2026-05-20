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

    /// <summary>§16.2.1.5 [[Status]] subset driving the linker state machine.</summary>
    public ModuleStatus Status { get; internal set; } = ModuleStatus.Unlinked;

    /// <summary>The runtime closure created at instantiation: the body wired to
    /// its imported-binding upvalue cells. Invoked once during evaluation.</summary>
    internal JsFunction? Instance { get; set; }

    /// <summary>Captured throw from evaluation, if the module errored
    /// (§16.2.1.5 [[EvaluationError]]).</summary>
    internal JsThrow? EvaluationError { get; set; }

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
