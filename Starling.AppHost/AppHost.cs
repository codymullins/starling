// Aspire AppHost: brings up the Starling dashboard (port printed on stdout —
// typically http://localhost:18888) and launches the registered resources.
// Run with:
//
//     dotnet run --project Starling.AppHost
//
// The dashboard surfaces stdout/stderr and (when the resource wires it via
// Starling.Telemetry) OpenTelemetry traces + metrics + logs.

var builder = DistributedApplication.CreateBuilder(args);

// Anchor everything to the repo root so relative paths in args don't blow up
// under Aspire's per-resource cwd (which defaults to each project's csproj
// directory).
var repoRoot = LocateRepoRoot();

// Avalonia 12 GUI. Plain net10.0 desktop exe — no Catalyst bundle,
// no `open -a`, no AssemblyName/_AppBundleName divergence — so the normal
// AddProject<>() path works and OTLP env vars inherit cleanly.
//
// Paint backend: defaults to `imagesharp-gpu` so `aspire run` picks the
// WebGPU-accelerated rasterizer out of the box. The build-time gate
// (EnableImageSharpDrawing3) auto-flips on in Directory.Build.props when a
// Six Labors license is present, so this default is wired end-to-end. The
// developer-set STARLING_PAINT_BACKEND below still wins (imagesharp / imagesharp-webgpu).
//
// MCP port: defaults to http://127.0.0.1:3078/mcp; the env var still wins
// if the developer overrides it.
var gui = builder.AddProject<Projects.Starling_Gui>("gui")
    .WithEnvironment("STARLING_PAINT_BACKEND", "imagesharp-gpu")
    .WithEnvironment("STARLING_MCP_URL", "http://127.0.0.1:3078/mcp")
    .WithOtlpExporter();

// Headless CLI. Pre-baked to render the bundled hello.html fixture; the args
// are absolute paths because Aspire's default cwd for a project resource is
// the csproj directory (src/Starling.Headless/), not the repo root.
//
// STARLING_PAINT_BACKEND on the AppHost process is forwarded to the headless
// resource — Aspire does not auto-propagate arbitrary host env vars, only
// those added via WithEnvironment. Selecting `imagesharp` additionally
// requires the headless build to compile in the backend, which means
// building the AppHost with `/p:EnableImageSharpDrawing3=true` so the
// property flows through the project references.
var headless = builder.AddProject<Projects.Starling_Headless>("headless")
    .WithArgs(
        "render",
        Path.Combine(repoRoot, "testdata", "hello.html"),
        "-o", Path.Combine(Path.GetTempPath(), "starling-headless-out.png"))
    .WithOtlpExporter();

var paintBackend = Environment.GetEnvironmentVariable("STARLING_PAINT_BACKEND");
if (!string.IsNullOrWhiteSpace(paintBackend))
{
    headless.WithEnvironment("STARLING_PAINT_BACKEND", paintBackend);
    gui.WithEnvironment("STARLING_PAINT_BACKEND", paintBackend);
}

// wgpu-native (Rust) honors RUST_LOG for tracing. Forward it through so we
// can see why wgpuCreateInstance fails when it does — RUST_LOG=trace dumps
// backend selection, adapter enumeration, and Metal/Vulkan/D3D init failures
// to stderr (which Aspire captures).
var rustLog = Environment.GetEnvironmentVariable("RUST_LOG");
if (!string.IsNullOrWhiteSpace(rustLog))
{
    headless.WithEnvironment("RUST_LOG", rustLog);
    gui.WithEnvironment("RUST_LOG", rustLog);
}

// JS engine selection (STARLING_JS_ENGINE=starling|jint) is read by
// Starling.Engine/JsEngineSelector at runtime. Like the blocks above, Aspire
// won't auto-propagate it, so forward the host value to both resources when set.
var jsEngine = Environment.GetEnvironmentVariable("STARLING_JS_ENGINE");
if (!string.IsNullOrWhiteSpace(jsEngine))
{
    headless.WithEnvironment("STARLING_JS_ENGINE", jsEngine);
    gui.WithEnvironment("STARLING_JS_ENGINE", jsEngine);
}

// Test sites. A YARP resource serves the repo-root sites/ directory as static
// files, so every subfolder is reachable at a stable localhost path
// (e.g. sites/todo/ -> http://localhost:8088/todo/). The browser engine can
// then load these like any HTTP page. The YARP integration runs as a container
// and copies the folder in at container start (not a live bind-mount), so adding
// or editing a site needs an AppHost (or "sites" resource) restart to take
// effect. The fixed host port keeps the URLs stable across runs; drop the
// WithHostPort call to let Aspire assign one (read it from the dashboard).
builder.AddYarp("sites")
    .WithStaticFiles(Path.Combine(repoRoot, "sites"))
    .WithHostPort(8088);

builder.Build().Run();

// Walk up from this binary's location until we find Starling.slnx. AppContext.
// BaseDirectory points at AppHost's bin/, which is N levels below the repo
// root regardless of how the user launched `dotnet run`.
static string LocateRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Starling.slnx")))
        dir = Path.GetDirectoryName(dir);
    return string.IsNullOrEmpty(dir)
        ? throw new InvalidOperationException("Could not locate Starling.slnx from " + AppContext.BaseDirectory)
        : dir;
}
