using System.Diagnostics;

// Aspire AppHost: brings up the Tessera dashboard (port printed on stdout —
// typically http://localhost:18888) and launches the registered resources.
// Run with:
//
//     dotnet run --project Tessera.AppHost
//
// The dashboard surfaces stdout/stderr and (when the resource wires it via
// Tessera.Telemetry) OpenTelemetry traces + metrics + logs.

var builder = DistributedApplication.CreateBuilder(args);

// Anchor everything to the repo root so relative paths in args don't blow up
// under Aspire's per-resource cwd (which defaults to each project's csproj
// directory).
var repoRoot = LocateRepoRoot();

// MAUI GUI. Two-step launch (build, then exec the bundle binary directly)
// — *not* `dotnet run` — because of how Mac Catalyst publishes the .app:
//
//   1. The default Run target on Catalyst invokes `open -a Foo.app`, which
//      hands the bundle to LaunchServices. The app becomes a child of
//      launchd, not of `dotnet run`, so OTEL env vars never cross the
//      boundary and stdout/stderr never stream back to Aspire.
//   2. The documented escape hatch `-p:RunWithOpen=false` rewrites the
//      RunCommand to `$(AssemblyName).app/Contents/MacOS/$(AssemblyName)`
//      — but MAUI names the bundle off `<ApplicationTitle>` (here:
//      `Tessera.app`), not off AssemblyName (`Tessera.Gui`). The SDK's
//      computed path `Tessera.Gui.app/Contents/MacOS/Tessera.Gui` doesn't
//      exist on disk, so `dotnet run` silently fails to exec anything and
//      Aspire sees the resource finish instantly.
//
// Resolving the bundle binary path ourselves and `AddExecutable`-ing it
// directly sidesteps both issues. The Catalyst process is a normal child
// of Aspire, OTEL env vars inherit, and stdio streams to the dashboard.
// `WithOtlpExporter()` pins down OTLP env injection — Aspire auto-injects
// for `AddProject` resources but the behavior is documented less crisply
// for `AddExecutable`.
//
// The trade-off is that we have to build the GUI before Aspire starts,
// since `AddExecutable` won't trigger a build on its own. A blocking
// `dotnet build` is fine here: the AppHost is a developer-only entry
// point, and `dotnet build` is incremental (a few hundred ms on a warm
// cache).
var guiFramework = DetectGuiFramework();
var guiProject = Path.Combine(repoRoot, "src", "Starling.Gui", "Starling.Gui.csproj");
var guiBinary = BuildGuiAndResolveBinary(guiProject, guiFramework);

var gui = builder.AddExecutable(
    name: "gui",
    command: guiBinary,
    workingDirectory: Path.GetDirectoryName(guiBinary)!)
    .WithOtlpExporter();

// Avalonia 12 GUI experiment. Plain net10.0 desktop exe — no Catalyst bundle,
// no `open -a`, no AssemblyName/_AppBundleName divergence — so the normal
// AddProject<>() path works and OTLP env vars inherit cleanly.
//
// Paint backend: defaults to `imagesharp` so `aspire run` picks the
// cross-platform managed rasterizer out of the box. The build-time gate
// (EnableImageSharpDrawing3) auto-flips on in Directory.Build.props when a
// Six Labors license is present, so this default is wired end-to-end. The
// developer-set TESSERA_PAINT_BACKEND below still wins (skia / imagesharp-webgpu).
//
// MCP port: the MAUI GUI defaults to http://127.0.0.1:3077/mcp. Push the
// Avalonia GUI to 3078 so both can listen simultaneously when Aspire brings
// them up together; the env var still wins if the developer overrides it.
var guiAvalonia = builder.AddProject<Projects.Starling_Gui_Avalonia>("gui-avalonia")
    .WithEnvironment("TESSERA_PAINT_BACKEND", "imagesharp")
    .WithEnvironment("TESSERA_MCP_URL", "http://127.0.0.1:3078/mcp")
    .WithOtlpExporter();

// Headless CLI. Pre-baked to render the bundled hello.html fixture; the args
// are absolute paths because Aspire's default cwd for a project resource is
// the csproj directory (src/Tessera.Headless/), not the repo root.
//
// TESSERA_PAINT_BACKEND on the AppHost process is forwarded to the headless
// resource — Aspire does not auto-propagate arbitrary host env vars, only
// those added via WithEnvironment. Selecting `imagesharp` additionally
// requires the headless build to compile in the backend, which means
// building the AppHost with `/p:EnableImageSharpDrawing3=true` so the
// property flows through the project references.
var headless = builder.AddProject<Projects.Starling_Headless>("headless")
    .WithArgs(
        "render",
        Path.Combine(repoRoot, "testdata", "hello.html"),
        "-o", Path.Combine(Path.GetTempPath(), "tessera-headless-out.png"))
    .WithOtlpExporter();

var paintBackend = Environment.GetEnvironmentVariable("TESSERA_PAINT_BACKEND");
if (!string.IsNullOrWhiteSpace(paintBackend))
{
    headless.WithEnvironment("TESSERA_PAINT_BACKEND", paintBackend);
    gui.WithEnvironment("TESSERA_PAINT_BACKEND", paintBackend);
    guiAvalonia.WithEnvironment("TESSERA_PAINT_BACKEND", paintBackend);
}

// wgpu-native (Rust) honors RUST_LOG for tracing. Forward it through so we
// can see why wgpuCreateInstance fails inside the Catalyst sandbox — the
// dylib loads cleanly but the actual init call returns ApiInitializationFailed
// with no detail from the .NET side. RUST_LOG=trace dumps backend selection,
// adapter enumeration, and Metal/Vulkan/D3D init failures to stderr (which
// Aspire captures).
var rustLog = Environment.GetEnvironmentVariable("RUST_LOG");
if (!string.IsNullOrWhiteSpace(rustLog))
{
    headless.WithEnvironment("RUST_LOG", rustLog);
    gui.WithEnvironment("RUST_LOG", rustLog);
    guiAvalonia.WithEnvironment("RUST_LOG", rustLog);
}

builder.Build().Run();

// Walk up from this binary's location until we find Starling.sln. AppContext.
// BaseDirectory points at AppHost's bin/, which is N levels below the repo
// root regardless of how the user launched `dotnet run`.
static string LocateRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Starling.sln")))
        dir = Path.GetDirectoryName(dir);
    return string.IsNullOrEmpty(dir)
        ? throw new InvalidOperationException("Could not locate Starling.sln from " + AppContext.BaseDirectory)
        : dir;
}

// Picks the TFM to invoke Tessera.Gui under. Set TESSERA_GUI_FRAMEWORK to
// override. Falls back to platform-appropriate defaults — Mac Catalyst on
// macOS (and as the absolute fallback), Windows when running on Windows.
// As Tessera.Gui's csproj grows new platform TFMs, extend this switch.
static string DetectGuiFramework()
{
    var fromEnv = Environment.GetEnvironmentVariable("TESSERA_GUI_FRAMEWORK");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;
    if (OperatingSystem.IsWindows()) return "net10.0-windows10.0.19041.0";
    return "net10.0-maccatalyst";
}

// Builds the GUI csproj for the given TFM, then asks MSBuild where the
// resulting .app bundle's main executable lives. We resolve the path via
// `dotnet msbuild -getProperty:` rather than computing it ourselves so the
// AppHost stays robust against changes to bundle-name / output-path
// conventions in future SDK versions.
static string BuildGuiAndResolveBinary(string project, string framework)
{
    Run("dotnet", ["build", project, "--framework", framework, "--nologo", "-v", "quiet"]);

    var json = RunCapture("dotnet",
        ["msbuild", project,
         $"-property:TargetFramework={framework}",
         "-getProperty:TargetDir,_AppBundleName,AssemblyName"]);

    // MSBuild's -getProperty returns a JSON blob like
    //   {"Properties":{"TargetDir":"...","_AppBundleName":"Tessera",...}}
    using var doc = System.Text.Json.JsonDocument.Parse(json);
    var props = doc.RootElement.GetProperty("Properties");
    var targetDir = props.GetProperty("TargetDir").GetString()!;
    var bundleName = props.GetProperty("_AppBundleName").GetString();
    var assemblyName = props.GetProperty("AssemblyName").GetString()!;

    // `_AppBundleName` is what MAUI uses for the bundle directory on
    // Catalyst; on TFMs that don't set it (or future targets) fall back to
    // AssemblyName, which is the SDK's documented default.
    if (string.IsNullOrWhiteSpace(bundleName)) bundleName = assemblyName;

    var binary = Path.Combine(targetDir, $"{bundleName}.app", "Contents", "MacOS", assemblyName);
    if (!File.Exists(binary))
        throw new InvalidOperationException(
            $"GUI bundle binary not found at '{binary}'. Build may have failed silently.");
    return binary;
}

static void Run(string command, string[] args)
{
    var psi = new ProcessStartInfo(command) { UseShellExecute = false };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to start {command}.");
    p.WaitForExit();
    if (p.ExitCode != 0)
        throw new InvalidOperationException(
            $"{command} {string.Join(' ', args)} exited with code {p.ExitCode}.");
}

static string RunCapture(string command, string[] args)
{
    var psi = new ProcessStartInfo(command)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
    };
    foreach (var a in args) psi.ArgumentList.Add(a);
    using var p = Process.Start(psi)
        ?? throw new InvalidOperationException($"Failed to start {command}.");
    var output = p.StandardOutput.ReadToEnd();
    p.WaitForExit();
    if (p.ExitCode != 0)
        throw new InvalidOperationException(
            $"{command} {string.Join(' ', args)} exited with code {p.ExitCode}.");
    return output;
}
