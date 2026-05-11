using FluentAssertions;
using SixLabors.ImageSharp;
using Tessera.Common.Diagnostics;
using Tessera.Engine;
using Xunit;

namespace Tessera.E2E;

/// <summary>
/// End-to-end M0 acceptance: drive the engine the same way the headless CLI
/// does, and confirm a PNG comes out the other side. Tagged
/// <c>Category=GoldenImage</c> so CI runs it via the dedicated step in
/// <c>.github/workflows/ci.yml</c>.
/// </summary>
[Trait("Category", "GoldenImage")]
public class RenderE2ETests
{
    [Fact]
    public void Render_hello_html_fixture()
    {
        var repoRoot = LocateRepoRoot();
        var fixture = Path.Combine(repoRoot, "testdata", "hello.html");
        File.Exists(fixture).Should().BeTrue($"fixture missing: {fixture}");

        var output = Path.Combine(Path.GetTempPath(), $"tessera-e2e-{Guid.NewGuid():N}.png");
        var engine = new TesseraEngine(diagnostics: NoopDiagnostics.Instance);

        try
        {
            var result = engine.Render(
                "file://" + fixture.Replace('\\', '/'),
                new RenderOptions(new Size(800, 600), 32f),
                output);

            result.IsOk.Should().BeTrue(
                result.IsErr ? $"render failed: {result.Error.Message}" : "");
            File.Exists(output).Should().BeTrue();
            new FileInfo(output).Length.Should().BeGreaterThan(200);

            result.Value.DisplayText.Should().Contain("Hello, world.");
        }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    /// <summary>
    /// Walk up from the test binary until we find <c>Tessera.sln</c>. Avoids
    /// hard-coding the repo path so CI on win/mac/linux all work.
    /// </summary>
    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Tessera.sln")))
            dir = dir.Parent;
        if (dir is null)
            throw new InvalidOperationException("Could not locate Tessera.sln walking up from the test binary.");
        return dir.FullName;
    }
}
