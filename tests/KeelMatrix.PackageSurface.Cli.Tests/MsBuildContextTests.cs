using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class MsBuildContextTests
{


    [Theory]
    [InlineData("target parent")]
    [InlineData("unknown parent")]
    [InlineData("unknown namespace")]
    public void GeneratedImportContextMustBeSupported(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var matrix = new[]
        {
        ("target parent", "<Project><Target Name=\"Invalid\"><Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></Target></Project>"),
        ("unknown parent", "<Project><UnknownGroup><Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></UnknownGroup></Project>"),
        ("unknown namespace", "<Project xmlns:x=\"urn:unrecognized\"><x:Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></Project>")
    };
        var (name, xml) = matrix.Single(value => value.Item1 == caseName);

        var root = Path.Combine(scratch, "invalid-generated-context-" + name.Replace(' ', '-'));
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Generated.Context";
        var assetPath = "build/context.targets";
        WriteAssets(assets, cache, packageId, new[] { assetPath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [assetPath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), xml);

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.Entries.Count == 0,
            $"Generated import in {name} was accepted instead of failing closed: complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");

    }

    [Theory]
    [InlineData("target parent")]
    [InlineData("unknown parent")]
    [InlineData("unknown namespace")]
    public void NestedImportContextMustBeSupported(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var matrix = new[]
        {
        ("target parent", "<Project><Target Name=\"Invalid\"><Import Project=\"helpers/child.targets\" /></Target></Project>"),
        ("unknown parent", "<Project><UnknownGroup><Import Project=\"helpers/child.targets\" /></UnknownGroup></Project>"),
        ("unknown namespace", "<Project xmlns:x=\"urn:unrecognized\"><x:Import Project=\"helpers/child.targets\" /></Project>")
    };
        var (name, xml) = matrix.Single(value => value.Item1 == caseName);

        var root = Path.Combine(scratch, "invalid-package-context-" + name.Replace(' ', '-'));
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Nested.Context";
        var outerPath = "build/outer.targets";
        var childPath = "helpers/child.targets";
        WriteAssets(assets, cache, packageId, new[] { outerPath, childPath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [outerPath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(cache, packageId, "1.0.0", outerPath.Replace('/', Path.DirectorySeparatorChar)), xml);
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{outerPath}\" /></Project>");

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.Entries.Count == 0,
            $"Nested import in {name} was accepted instead of failing closed: complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");

    }

    [Theory]
    [InlineData("Exec at Project root")]
    [InlineData("UsingTask under Target")]
    [InlineData("Code outside Task")]
    public void PrimitiveContextMustBeSupported(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var matrix = new[]
        {
        ("Exec at Project root", "<Project><Exec Command=\"not-a-target-task\" /></Project>"),
        ("UsingTask under Target", "<Project><Target Name=\"Invalid\"><UsingTask TaskName=\"Invalid\" AssemblyFile=\"task.dll\" /></Target></Project>"),
        ("Code outside Task", "<Project><Code>not-inline-task-code</Code></Project>")
    };
        var (name, xml) = matrix.Single(value => value.Item1 == caseName);

        var root = Path.Combine(scratch, "invalid-primitive-context-" + name.Replace(' ', '-'));
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Primitive.Context";
        var packagePath = "build/primitive.targets";
        WriteAssets(assets, cache, packageId, new[] { packagePath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [packagePath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(cache, packageId, "1.0.0", packagePath.Replace('/', Path.DirectorySeparatorChar)), xml);
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{packagePath}\" /></Project>");

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var scan = CaptureCommand("scan", assets, "--format", "text", "--no-telemetry");
        Assert.True(!result.IsComplete && scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                !scan.Output.Contains("BuildTargets", StringComparison.Ordinal),
            $"A package MSBuild primitive in {name} was accepted or emitted a partial surface: complete={result.IsComplete}; exit={scan.ExitCode}; output={scan.Output}; reasons={string.Join(" | ", result.IncompleteReasons)}");

    }
}
