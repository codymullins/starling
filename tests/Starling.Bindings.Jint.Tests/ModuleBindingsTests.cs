using AwesomeAssertions;
using Starling.Common.Diagnostics;
using Starling.Html;
using Starling.Js.Hosting;

namespace Starling.Bindings.Jint.Tests;

/// <summary>
/// J4 — ES module loader, top-level await, and dynamic <c>import()</c> over the
/// Jint backend. Exercises the full path through a real
/// <see cref="JintScriptSession"/>: the engine is constructed with module support
/// enabled (<c>EnableModules</c> + <c>UseHostFactory</c>), and the
/// <see cref="StarlingJintModuleLoader"/> resolves specifiers against the
/// document base and fetches dependency module bodies through an in-memory fetch
/// delegate (no network). Module results are observed via <c>console.log</c>,
/// captured by the session's <see cref="IScriptSession.ConsoleSink"/>.
/// </summary>
[TestClass]
public sealed class ModuleBindingsTests
{
    private const string Base = "https://modules.test/";

    [TestMethod]
    public void Module_imports_named_and_default_exports_from_a_fetched_dependency()
    {
        var logs = new List<string>();
        var modules = new Dictionary<string, string>
        {
            ["https://modules.test/dep.js"] =
                "export const greeting = 'hello'; export default 7;",
        };
        var session = NewSession(logs, modules);

        // The entry module imports a default + a named export from a dependency
        // the loader fetches via the in-memory map.
        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/entry.js").Value;
        session.RunModuleScriptAsync(
            entryUrl,
            "import answer, { greeting } from './dep.js';\n" +
            "console.log(greeting + ' ' + answer);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("hello 7");
    }

    [TestMethod]
    public void Module_top_level_await_settles_a_promise_before_returning()
    {
        var logs = new List<string>();
        var session = NewSession(logs, new Dictionary<string, string>());

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/tla.js").Value;
        session.RunModuleScriptAsync(
            entryUrl,
            "const value = await Promise.resolve(42);\n" +
            "console.log('tla:' + value);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("tla:42");
    }

    [TestMethod]
    public void Module_import_meta_url_reflects_the_module_location()
    {
        var logs = new List<string>();
        var session = NewSession(logs, new Dictionary<string, string>());

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/meta.js").Value;
        session.RunModuleScriptAsync(
            entryUrl,
            "console.log(import.meta.url);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("https://modules.test/meta.js");
    }

    [TestMethod]
    public void Dynamic_import_returns_a_module_namespace()
    {
        var logs = new List<string>();
        var modules = new Dictionary<string, string>
        {
            ["https://modules.test/lazy.js"] =
                "export const value = 'lazy-loaded'; export default 'd';",
        };
        var session = NewSession(logs, modules);

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/dyn.js").Value;
        session.RunModuleScriptAsync(
            entryUrl,
            "const ns = await import('./lazy.js');\n" +
            "console.log(ns.value + ' / ' + ns.default);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("lazy-loaded / d");
    }

    [TestMethod]
    public void Inline_module_resolves_relative_imports_against_the_document_base()
    {
        var logs = new List<string>();
        var modules = new Dictionary<string, string>
        {
            ["https://modules.test/util.js"] = "export const x = 'util';",
        };
        var session = NewSession(logs, modules);

        // Engine.cs passes the document base URL for inline <script type=module>.
        var docBase = global::Starling.Url.UrlParser.Parse(Base).Value;
        session.RunModuleScriptAsync(
            docBase,
            "import { x } from './util.js';\nconsole.log('inline:' + x);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("inline:util");
    }

    [TestMethod]
    public void Missing_dependency_surfaces_as_ScriptThrow()
    {
        var logs = new List<string>();
        // No modules registered → ./missing.js cannot be fetched.
        var session = NewSession(logs, new Dictionary<string, string>());

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/badentry.js").Value;
        var act = () => session.RunModuleScriptAsync(
            entryUrl,
            "import { nope } from './missing.js';\nconsole.log(nope);",
            CancellationToken.None).GetAwaiter().GetResult();

        act.Should().Throw<ScriptThrow>();
    }

    [TestMethod]
    public void Throwing_module_surfaces_as_ScriptThrow()
    {
        var logs = new List<string>();
        var session = NewSession(logs, new Dictionary<string, string>());

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/throws.js").Value;
        var act = () => session.RunModuleScriptAsync(
            entryUrl,
            "throw new TypeError('boom from module');",
            CancellationToken.None).GetAwaiter().GetResult();

        act.Should().Throw<ScriptThrow>()
            .WithMessage("*boom from module*");
    }

    [TestMethod]
    public void Re_export_chains_through_an_intermediate_module()
    {
        var logs = new List<string>();
        var modules = new Dictionary<string, string>
        {
            ["https://modules.test/leaf.js"] = "export const leaf = 'leaf-value';",
            ["https://modules.test/mid.js"] = "export { leaf } from './leaf.js';",
        };
        var session = NewSession(logs, modules);

        var entryUrl = global::Starling.Url.UrlParser.Parse("https://modules.test/reexport.js").Value;
        session.RunModuleScriptAsync(
            entryUrl,
            "import { leaf } from './mid.js';\nconsole.log('re:' + leaf);",
            CancellationToken.None).GetAwaiter().GetResult();

        logs.Should().ContainSingle().Which.Should().Be("re:leaf-value");
    }

    // ---- shared setup -------------------------------------------------------

    private static JintScriptSession NewSession(List<string> logs, Dictionary<string, string> modules)
    {
        var doc = HtmlParser.Parse("<!doctype html><html><body></body></html>");
        var url = global::Starling.Url.UrlParser.Parse(Base).Value;
        var http = new Starling.Net.StarlingHttpClient();

        // In-memory fetch: resolve module bodies from the supplied map; null
        // (not found) for anything else, which the loader turns into a throw.
        Task<string?> Fetch(global::Starling.Url.Url u, CancellationToken _)
            => Task.FromResult(modules.TryGetValue(u.ToString(), out var src) ? src : null);

        var options = new ScriptSessionOptions(
            Document: doc,
            BaseUrl: url,
            Fetcher: Fetch,
            Http: http,
            LayoutHost: null,
            Diag: NoopDiagnostics.Instance);

        return new JintScriptSession(options)
        {
            ConsoleSink = (_, msg) => logs.Add(msg),
        };
    }
}
