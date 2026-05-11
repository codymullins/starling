# 02 — Project Setup

## Scope

**In:** Solution + project layout, exact `dotnet` commands to bootstrap, NuGet pins, EditorConfig, analyzers, CI matrix, dev loop.
**Out:** Per-subsystem code (see technical docs).

## Prerequisites

- .NET SDK **10.0.100** or newer (LTS, released 2025-11-11). `dotnet --version` must report `10.0.*`.
- Any of: VS 2026, Rider 2025.3+, VS Code with C# Dev Kit. Pure CLI also fine.
- Git 2.30+.
- No other native dependencies. Specifically: **no** Node, **no** Python, **no** Rust, **no** Bun. The repo is .NET only.

## Bootstrap (one-shot, copy-paste runnable)

```bash
mkdir tessera && cd tessera
git init -b main
dotnet new gitignore
dotnet new sln -n Tessera

# Source projects (classlib unless noted)
for p in Common Url Net Html Dom Css Layout Paint Js Bindings Loop Engine; do
  dotnet new classlib -n "Tessera.$p" -o "src/Tessera.$p" -f net10.0
  dotnet sln add "src/Tessera.$p/Tessera.$p.csproj"
done
dotnet new console -n Tessera.Headless -o "src/Tessera.Headless" -f net10.0
dotnet sln add "src/Tessera.Headless/Tessera.Headless.csproj"
dotnet new avalonia.app -n Tessera.Shell -o "src/Tessera.Shell" -f net10.0 || true   # if template installed
dotnet sln add "src/Tessera.Shell/Tessera.Shell.csproj"

# Test projects
for p in Common Url Net Html Dom Css Layout Paint Js Bindings Loop Engine; do
  dotnet new xunit -n "Tessera.$p.Tests" -o "tests/Tessera.$p.Tests" -f net10.0
  dotnet sln add "tests/Tessera.$p.Tests/Tessera.$p.Tests.csproj"
  dotnet add "tests/Tessera.$p.Tests/Tessera.$p.Tests.csproj" reference "src/Tessera.$p/Tessera.$p.csproj"
done

# E2E project
dotnet new xunit -n Tessera.E2E -o tests/Tessera.E2E -f net10.0
dotnet sln add tests/Tessera.E2E/Tessera.E2E.csproj
dotnet add tests/Tessera.E2E/Tessera.E2E.csproj reference src/Tessera.Engine/Tessera.Engine.csproj

# Bench
dotnet new console -n Tessera.Bench -o bench/Tessera.Bench -f net10.0
dotnet sln add bench/Tessera.Bench/Tessera.Bench.csproj
```

Then wire up references per the dependency graph in [01_ARCHITECTURE.md](01_ARCHITECTURE.md#project-graph).

## Directory layout

```
tessera/
├── Tessera.sln
├── Directory.Build.props        # shared TFM + nullable + warnings
├── Directory.Packages.props     # central package versions (CPM)
├── .editorconfig
├── .gitignore
├── .github/workflows/ci.yml
├── docs/                        # this plan + ADRs
├── specs/                       # vendored html-aam, css-2025-snapshot, etc.
├── testdata/                    # WPT subset, golden PNGs, fixtures
├── src/
│   ├── Tessera.Common/
│   ├── Tessera.Url/
│   ├── Tessera.Net/
│   ├── Tessera.Html/
│   ├── Tessera.Dom/
│   ├── Tessera.Css/
│   ├── Tessera.Layout/
│   ├── Tessera.Paint/
│   ├── Tessera.Js/
│   ├── Tessera.Bindings/
│   ├── Tessera.Loop/
│   ├── Tessera.Engine/
│   ├── Tessera.Headless/        # CLI: tessera render <url> -o out.png
│   └── Tessera.Shell/           # Avalonia UI
├── tests/
│   ├── Tessera.<Module>.Tests/
│   └── Tessera.E2E/
└── bench/
    └── Tessera.Bench/
```

## `Directory.Build.props`

Drop at repo root. Applied to every csproj. Sets the common shape.

```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <LangVersion>14.0</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <WarningsAsErrors />
    <AnalysisLevel>latest</AnalysisLevel>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
    <RootNamespace>$(MSBuildProjectName)</RootNamespace>
    <AssemblyName>$(MSBuildProjectName)</AssemblyName>
    <Deterministic>true</Deterministic>
    <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.NetAnalyzers" PrivateAssets="all" />
    <PackageReference Include="Roslynator.Analyzers"               PrivateAssets="all" />
  </ItemGroup>
</Project>
```

## `Directory.Packages.props` (Central Package Management)

```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- Rendering -->
    <PackageVersion Include="SixLabors.ImageSharp"          Version="3.1.12" />
    <PackageVersion Include="SixLabors.ImageSharp.Drawing"  Version="2.1.7" />
    <PackageVersion Include="SixLabors.Fonts"               Version="2.1.3" />

    <!-- UI shell — Avalonia 12 (stable Apr 2026, targets .NET 10) -->
    <PackageVersion Include="Avalonia"                      Version="12.0.3" />
    <PackageVersion Include="Avalonia.Desktop"              Version="12.0.3" />
    <PackageVersion Include="Avalonia.Themes.Fluent"        Version="12.0.3" />
    <PackageVersion Include="Avalonia.Fonts.Inter"          Version="12.0.3" />
    <PackageVersion Include="Avalonia.ReactiveUI"           Version="12.0.3" />

    <!-- Crypto: pure-managed TLS support -->
    <PackageVersion Include="BouncyCastle.Cryptography"     Version="2.5.0" />

    <!-- Tests + bench -->
    <PackageVersion Include="xunit.v3"                      Version="1.0.0" />
    <PackageVersion Include="xunit.runner.visualstudio"     Version="3.0.0" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk"        Version="17.13.0" />
    <PackageVersion Include="FluentAssertions"              Version="6.12.2" />
    <PackageVersion Include="BenchmarkDotNet"               Version="0.14.0" />

    <!-- Analyzers -->
    <PackageVersion Include="Microsoft.CodeAnalysis.NetAnalyzers" Version="9.0.0" />
    <PackageVersion Include="Roslynator.Analyzers"               Version="4.13.1" />
  </ItemGroup>
</Project>
```

> **Note** — These are pinned versions as of 2026-05. Re-check before bumping. Adversarial transitives are blocked by `CentralPackageTransitivePinningEnabled`.

## `.editorconfig`

```ini
root = true

[*.cs]
indent_style = space
indent_size  = 4
end_of_line  = lf
charset      = utf-8
insert_final_newline = true
trim_trailing_whitespace = true

# Strong style — fail the build, don't lint forever
dotnet_diagnostic.IDE0005.severity = error       # remove unused usings
dotnet_diagnostic.CA1825.severity  = error       # avoid zero-length array allocations
dotnet_diagnostic.CA1859.severity  = error       # concrete types where possible
dotnet_diagnostic.CA1869.severity  = error       # cache JsonSerializerOptions
dotnet_diagnostic.CA2007.severity  = none        # we don't .ConfigureAwait everywhere — engine has its own sync context
dotnet_diagnostic.CS1591.severity  = none        # public docs not required everywhere

csharp_style_namespace_declarations = file_scoped:error
csharp_style_prefer_primary_constructors = true:suggestion
csharp_prefer_static_anonymous_function = true:warning

[*.{xml,csproj,props,targets}]
indent_size = 2
```

## `.gitignore`

Use `dotnet new gitignore` output and add:

```
testdata/wpt/results/
*.png.actual
bench/results/
.idea/
.vs/
.vscode/
*.user
```

## CI matrix (`.github/workflows/ci.yml`)

```yaml
name: ci
on:
  push: { branches: [main] }
  pull_request:
jobs:
  build:
    strategy:
      matrix:
        os: [ubuntu-24.04, macos-15, windows-2025]
      fail-fast: false
    runs-on: ${{ matrix.os }}
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet restore --locked-mode
      - run: dotnet build  --no-restore  -c Release
      - run: dotnet test   --no-build    -c Release --logger "trx;LogFileName=test.trx"
      - run: dotnet test   --no-build    -c Release --filter Category=GoldenImage
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: test-results-${{matrix.os}}, path: '**/*.trx' }
  lint:
    runs-on: ubuntu-24.04
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - run: dotnet format --verify-no-changes
      - name: forbid PInvoke
        run: |
          ! grep -rn 'DllImport\|LibraryImport' \
              src/Tessera.{Common,Url,Net,Html,Dom,Css,Layout,Paint,Js,Bindings,Loop,Engine}/
```

The lint job's `grep` enforces Rule 0.

## Lockfiles

Run `dotnet restore --use-lock-file` once per project, then commit `packages.lock.json`. CI uses `--locked-mode`. This is how we keep supply-chain attacks visible.

## Dev loop

```bash
# Build everything
dotnet build

# Run headless renderer
dotnet run --project src/Tessera.Headless -- render file://./testdata/hello.html -o out.png

# Run shell
dotnet run --project src/Tessera.Shell

# Run all tests
dotnet test

# Run a single test
dotnet test --filter "FullyQualifiedName~TokenizerTests.DataState"

# Run benchmarks
dotnet run --project bench/Tessera.Bench -c Release -- --filter "*Tokenizer*"

# Format
dotnet format
```

## Headless CLI shape

`Tessera.Headless` is the agent-friendly entry point. Used by integration tests, golden-image diffs, fuzzing, and "is the engine even alive" smoke checks.

```
tessera render <url-or-file> [-o out.png] [--viewport WxH] [--wait-for selector|networkidle|<ms>]
tessera tokenize <file>            # prints token stream
tessera parse <file>               # prints DOM as JSON-ish tree
tessera style <file>               # prints computed styles per element
tessera layout <file> [--viewport WxH]   # prints box tree
tessera js <file>                  # evaluates a JS file under a synthetic Realm
```

Each subcommand maps 1:1 to a public API in [01_ARCHITECTURE.md](01_ARCHITECTURE.md#core-public-apis). Agents implementing a subsystem should add tests through this CLI so a human auditor (or another agent) can reproduce results manually.

## Repository hygiene rules

1. One PR per work package (see [14_AGENT_TASKS.md](14_AGENT_TASKS.md)).
2. Each PR includes tests. No PR is merged with failing or absent tests.
3. No PR adds a NuGet package without bumping `Directory.Packages.props` and stating the reason in the PR body.
4. No PR adds a native dependency. CI's `lint` job will fail otherwise.

## Acceptance Tests

- [ ] `dotnet build` exits 0 on a freshly cloned repo, all platforms.
- [ ] `dotnet test` exits 0 with at least one test per subsystem project.
- [ ] `dotnet format --verify-no-changes` exits 0.
- [ ] CI badge green on all three OS matrix entries.
- [ ] `tessera render file://testdata/hello.html -o out.png` writes a non-empty PNG.
- [ ] `grep -rn 'DllImport\|LibraryImport' src/Tessera.*` is empty.
