// Aspire AppHost: brings up the Starling dashboard (port printed on stdout —
// typically http://localhost:18888) and launches the registered resources.
// Run with:
//
//     aspire run                            # defaults: --starling --imagesharp-gpu --incremental
//     aspire run -- --jint                  # Jint JS backend
//     aspire run -- --jint --imagesharp     # Jint + CPU paint backend
//     aspire run -- --full                  # full (non-incremental) relayout
//
// Runtime-selection flags (everything after `aspire run --` lands in args) are
// parsed here and forwarded to BOTH the gui and headless resources:
//
//     --starling | --jint                JS engine     (default: --starling)
//     --imagesharp | --imagesharp-gpu    paint backend (default: --imagesharp-gpu)
//     --incremental | --full             layout        (default: --incremental)
//
// A command-line flag wins over the matching env var (STARLING_JS_ENGINE /
// STARLING_PAINT_BACKEND / STARLING_INCREMENTAL_LAYOUT); the env var wins over
// the built-in default.
//
// The dashboard surfaces stdout/stderr and (when the resource wires it via
// Starling.Telemetry) OpenTelemetry traces + metrics + logs.

// Resolve selection up front so the flags can be stripped from the args handed
// to Aspire — left in, they'd be parsed as bogus configuration keys.
var jsEngine = SelectJsEngine(args) ?? Env("STARLING_JS_ENGINE") ?? "starling";
var paintBackend = SelectPaintBackend(args) ?? Env("STARLING_PAINT_BACKEND") ?? "imagesharp-gpu";
// The env var is a 0/1 switch; map it onto the same "incremental"/"full" label
// the flag uses so the flag -> env -> default chain reads like the others.
var layoutEnv = Env("STARLING_INCREMENTAL_LAYOUT") switch { "1" => "incremental", null => null, _ => "full" };
var layout = SelectLayout(args) ?? layoutEnv ?? "incremental";
var incrementalLayout = layout == "incremental" ? "1" : "0";

var builder = DistributedApplication.CreateBuilder(
    args.Where(a => !IsStarlingSelectionFlag(a)).ToArray());

// Anchor everything to the repo root so relative paths in args don't blow up
// under Aspire's per-resource cwd (which defaults to each project's csproj
// directory).
var repoRoot = LocateRepoRoot();

// Avalonia 12 GUI. Plain net10.0 desktop exe — no Catalyst bundle,
// no `open -a`, no AssemblyName/_AppBundleName divergence — so the normal
// AddProject<>() path works and OTLP env vars inherit cleanly.
//
// Paint + JS selections (resolved above from flags / env / default) are wired
// onto the resource. The CPU `imagesharp` backend additionally needs the build
// to compile it in (`/p:EnableImageSharpDrawing3=true`, auto-on in
// Directory.Build.props when a Six Labors license is present).
//
// MCP port: defaults to http://127.0.0.1:3078/mcp; STARLING_MCP_URL still wins.
var gui = builder.AddProject<Projects.Starling_Gui>("gui")
    .WithEnvironment("STARLING_PAINT_BACKEND", paintBackend)
    .WithEnvironment("STARLING_JS_ENGINE", jsEngine)
    .WithEnvironment("STARLING_INCREMENTAL_LAYOUT", incrementalLayout)
    .WithEnvironment("STARLING_MCP_URL", "http://127.0.0.1:3078/mcp")
    .WithOtlpExporter();

// Headless CLI. Pre-baked to render the bundled hello.html fixture; the args
// are absolute paths because Aspire's default cwd for a project resource is
// the csproj directory (src/Starling.Headless/), not the repo root.
// WithExplicitStart keeps the resource visible in the dashboard but skips it
// on `aspire run` — start it on demand from the dashboard when you want to
// exercise the headless render path.
var headless = builder.AddProject<Projects.Starling_Headless>("headless")
    .WithArgs(
        "render",
        Path.Combine(repoRoot, "testdata", "hello.html"),
        "-o", Path.Combine(Path.GetTempPath(), "starling-headless-out.png"))
    .WithEnvironment("STARLING_PAINT_BACKEND", paintBackend)
    .WithEnvironment("STARLING_JS_ENGINE", jsEngine)
    .WithEnvironment("STARLING_INCREMENTAL_LAYOUT", incrementalLayout)
    .WithExplicitStart()
    .WithOtlpExporter();

// wgpu-native (Rust) honors RUST_LOG for tracing. Forward it through so we
// can see why wgpuCreateInstance fails when it does — RUST_LOG=trace dumps
// backend selection, adapter enumeration, and Metal/Vulkan/D3D init failures
// to stderr (which Aspire captures).
var rustLog = Env("RUST_LOG");
if (rustLog is not null)
{
    headless.WithEnvironment("RUST_LOG", rustLog);
    gui.WithEnvironment("RUST_LOG", rustLog);
}

// Test sites. A YARP resource serves the testdata/sites/ directory as static
// files, so every subfolder is reachable at a stable localhost path
// (e.g. testdata/sites/todo/ -> http://localhost:8088/todo/). The browser engine can
// then load these like any HTTP page. The YARP integration runs as a container
// and copies the folder in at container start (not a live bind-mount), so adding
// or editing a site needs an AppHost (or "sites" resource) restart to take
// effect. The fixed host port keeps the URLs stable across runs; drop the
// WithHostPort call to let Aspire assign one (read it from the dashboard).
builder.AddYarp("sites")
    .WithStaticFiles(Path.Combine(repoRoot, "testdata", "sites"))
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

// Read an env var, normalizing unset/blank to null so `?? default` chains work.
static string? Env(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    return string.IsNullOrWhiteSpace(value) ? null : value;
}

// --starling | --jint  ->  STARLING_JS_ENGINE value (null if no flag given).
static string? SelectJsEngine(string[] args) => SelectFlag(
    args, "JS engine",
    ("--starling", "starling"),
    ("--jint", "jint"));

// --imagesharp/--cpu | --imagesharp-gpu/--imagesharp-webgpu/--gpu  ->  STARLING_PAINT_BACKEND.
static string? SelectPaintBackend(string[] args) => SelectFlag(
    args, "paint backend",
    ("--imagesharp", "imagesharp"),
    ("--cpu", "imagesharp"),
    ("--imagesharp-gpu", "imagesharp-gpu"),
    ("--imagesharp-webgpu", "imagesharp-gpu"),
    ("--gpu", "imagesharp-gpu"));

// --incremental | --full  ->  "incremental"/"full" (null if no flag given).
static string? SelectLayout(string[] args) => SelectFlag(
    args, "layout",
    ("--incremental", "incremental"),
    ("--full", "full"));

// Scan args for any of the given flag->value mappings; return the selected value
// (null if none present). Throws on conflicting selections (e.g. --jint --starling).
static string? SelectFlag(string[] args, string label, params (string Flag, string Value)[] mappings)
{
    string? selected = null;
    string? selectedFlag = null;
    foreach (var arg in args)
        foreach (var (flag, value) in mappings)
        {
            if (arg != flag) continue;
            if (selected is not null && selected != value)
                throw new InvalidOperationException(
                    $"Conflicting {label} flags: {selectedFlag} and {flag}. Pass only one.");
            (selected, selectedFlag) = (value, flag);
        }
    return selected;
}

// True for any flag SelectJsEngine / SelectPaintBackend recognize, so it can be
// stripped before the args reach Aspire's command-line configuration provider.
static bool IsStarlingSelectionFlag(string arg) => arg is
    "--starling" or "--jint"
    or "--imagesharp" or "--cpu"
    or "--imagesharp-gpu" or "--imagesharp-webgpu" or "--gpu"
    or "--incremental" or "--full";
