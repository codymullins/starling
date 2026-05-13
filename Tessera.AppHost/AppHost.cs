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

// MAUI GUI. Tessera.Gui's csproj uses <TargetFrameworks> (plural) so it can
// grow Windows / Android targets later; today only Mac Catalyst is shipped.
// `AddProject<T>()` invokes `dotnet run` with --no-launch-profile, which
// can't pick a TFM on its own — switching to AddExecutable lets us inject
// --framework explicitly via the same OS-detection logic we'd want anyway.
var guiFramework = DetectGuiFramework();
var guiDir = Path.Combine(repoRoot, "src", "Tessera.Gui");
builder.AddExecutable(
    name: "gui",
    command: "dotnet",
    workingDirectory: guiDir,
    "run",
    "--project", Path.Combine(guiDir, "Tessera.Gui.csproj"),
    "--framework", guiFramework,
    "--no-launch-profile");

// Headless CLI. Pre-baked to render the bundled hello.html fixture; the args
// are absolute paths because Aspire's default cwd for a project resource is
// the csproj directory (src/Tessera.Headless/), not the repo root.
builder.AddProject<Projects.Tessera_Headless>("headless")
    .WithArgs(
        "render",
        Path.Combine(repoRoot, "testdata", "hello.html"),
        "-o", Path.Combine(Path.GetTempPath(), "tessera-headless-out.png"));

builder.Build().Run();

// Walk up from this binary's location until we find Tessera.sln. AppContext.
// BaseDirectory points at AppHost's bin/, which is N levels below the repo
// root regardless of how the user launched `dotnet run`.
static string LocateRepoRoot()
{
    var dir = AppContext.BaseDirectory;
    while (!string.IsNullOrEmpty(dir) && !File.Exists(Path.Combine(dir, "Tessera.sln")))
        dir = Path.GetDirectoryName(dir);
    return string.IsNullOrEmpty(dir)
        ? throw new InvalidOperationException("Could not locate Tessera.sln from " + AppContext.BaseDirectory)
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
