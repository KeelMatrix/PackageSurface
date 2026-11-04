using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class VersionSurfaceFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal VersionSurfaceFixture()
    {

        workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "malformed-version-surfaces");
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var files = new[]
        {
        "build/surface.targets",
        "analyzers/dotnet/cs/surface.dll",
        "contentFiles/cs/any/surface.cs",
        "runtimes/win-x64/native/surface.dll",
        "tools/surface.ps1"
    };
        WriteAssets(assets, cache, "Version.Surface", files, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var targetPackage = document["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!.AsObject();
        targetPackage["build"] = new JsonObject { ["build/surface.targets"] = new JsonObject() };
        targetPackage["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/surface.dll"] = new JsonObject() };
        targetPackage["contentFiles"] = new JsonObject
        {
            ["contentFiles/cs/any/surface.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "cs" }
        };
        targetPackage["native"] = new JsonObject { ["runtimes/win-x64/native/surface.dll"] = new JsonObject() };
        targetPackage["tools"] = new JsonObject { ["tools/surface.ps1"] = new JsonObject() };
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
            Path.Combine(cache, "Version.Surface", "1.0.0", "analyzers", "dotnet", "cs", "surface.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(cache, "Version.Surface", "1.0.0", "build", "surface.targets"), "<Project />");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/1.0.0/build/surface.targets\" /></Project>");
        File.WriteAllText(assets, document.ToJsonString());

        var baseline = Path.Combine(obj, "baseline.json");
        var baselineResult = CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry");

        var validSource = File.ReadAllText(assets);

        Root = root; Obj = obj; AssetsPath = assets; BaselinePath = baseline; ValidSource = validSource; BaselineResult = baselineResult;
    }
    internal string Root { get; }
    internal string Obj { get; }
    internal string AssetsPath { get; }
    internal string BaselinePath { get; }
    internal string ValidSource { get; }
    internal (int ExitCode, string Output) BaselineResult { get; }
    public void Dispose() => workspace.Dispose();

}
