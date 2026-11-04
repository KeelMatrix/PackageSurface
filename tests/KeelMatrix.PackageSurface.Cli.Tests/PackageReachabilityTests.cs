using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.PackageFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PackageReachabilityTests
{
    [Fact]
    public void ReachabilityClosureIncludesOnlyResolvedPackages()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var orphanRoot = Path.Combine(scratch, "reachability-orphan");
        var orphanObj = Path.Combine(orphanRoot, "obj");
        var orphanCache = Path.Combine(orphanRoot, "cache");
        Directory.CreateDirectory(orphanObj);
        var orphanAssets = Path.Combine(orphanObj, "project.assets.json");
        WriteAssets(orphanAssets, orphanCache, "Direct.Root", Array.Empty<string>());
        var orphanDocument = JsonNode.Parse(File.ReadAllText(orphanAssets))!.AsObject();
        var orphanFiles = new[]
        {
        "build/orphan.targets",
        "analyzers/dotnet/cs/orphan.dll",
        "contentFiles/cs/any/orphan.cs",
        "runtimes/win-x64/native/orphan.dll",
        "tools/orphan.ps1"
    };
        AddPackageToAssets(orphanDocument, orphanCache, "Orphan.Package", orphanFiles, new JsonObject
        {
            ["build"] = new JsonObject { ["build/orphan.targets"] = new JsonObject() },
            ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/orphan.dll"] = new JsonObject() },
            ["contentFiles"] = new JsonObject
            {
                ["contentFiles/cs/any/orphan.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
            },
            ["native"] = new JsonObject { ["runtimes/win-x64/native/orphan.dll"] = new JsonObject() },
            ["tools"] = new JsonObject { ["tools/orphan.ps1"] = new JsonObject() }
        });
        var orphanAnalyzer = Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "analyzers", "dotnet", "cs", "orphan.dll");
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, orphanAnalyzer, overwrite: true);
        File.WriteAllText(Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "build", "orphan.targets"), "<Project />");
        File.WriteAllText(orphanAssets, orphanDocument.ToJsonString());
        File.WriteAllText(Path.Combine(orphanObj, "Test.csproj.nuget.g.props"), "<Project><Import Project=\"$(NuGetPackageRoot)/Orphan.Package/1.0.0/build/orphan.targets\" /></Project>");
        var orphanResult = ResolvedGraphClassifier.Analyze(orphanAssets, orphanRoot, strictContent: false);
        Assert.True(!orphanResult.IsComplete && orphanResult.Entries.Count == 0 && orphanResult.ResolvedPackageCount == 0,
            "An orphan package with build, analyzer, content, native, and tool assets was classified as reachable.");

        var islandRoot = Path.Combine(scratch, "reachability-island");
        var islandObj = Path.Combine(islandRoot, "obj");
        var islandCache = Path.Combine(islandRoot, "cache");
        Directory.CreateDirectory(islandObj);
        var islandAssets = Path.Combine(islandObj, "project.assets.json");
        WriteAssets(islandAssets, islandCache, "Direct.Root", Array.Empty<string>());
        var islandDocument = JsonNode.Parse(File.ReadAllText(islandAssets))!.AsObject();
        AddPackageToAssets(islandDocument, islandCache, "Island.Root", Array.Empty<string>(), new JsonObject
        {
            ["dependencies"] = new JsonObject { ["Island.Leaf"] = "1.0.0" }
        });
        AddPackageToAssets(islandDocument, islandCache, "Island.Leaf", Array.Empty<string>());
        File.WriteAllText(islandAssets, islandDocument.ToJsonString());
        var islandResult = ResolvedGraphClassifier.Analyze(islandAssets, islandRoot, strictContent: false);
        Assert.True(!islandResult.IsComplete && islandResult.Entries.Count == 0,
            "A disconnected multi-package dependency island was classified as reachable.");

        var rootedRoot = Path.Combine(scratch, "reachability-rooted-graphs");
        var rootedObj = Path.Combine(rootedRoot, "obj");
        var rootedCache = Path.Combine(rootedRoot, "cache");
        Directory.CreateDirectory(rootedObj);
        var rootedAssets = Path.Combine(rootedObj, "project.assets.json");
        WriteAssets(rootedAssets, rootedCache, "Direct.Root", new List<string> { "tools/direct.ps1" }, createFiles: true);
        var rootedDocument = JsonNode.Parse(File.ReadAllText(rootedAssets))!.AsObject();
        rootedDocument["targets"]!["net8.0"]!["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject
        {
            ["Chain.Leaf"] = "1.0.0",
            ["Cycle.A"] = "1.0.0"
        };
        AddPackageToAssets(rootedDocument, rootedCache, "Chain.Leaf", new List<string> { "tools/chain.ps1" }, new JsonObject { ["tools"] = new JsonObject { ["tools/chain.ps1"] = new JsonObject() } });
        AddPackageToAssets(rootedDocument, rootedCache, "Cycle.A", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.B"] = "1.0.0" } });
        AddPackageToAssets(rootedDocument, rootedCache, "Cycle.B", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.A"] = "1.0.0" } });
        var projectReferenceKey = "Referenced.Project/1.0.0";
        rootedDocument["targets"]!["net8.0"]![projectReferenceKey] = new JsonObject { ["dependencies"] = new JsonObject { ["Project.Root"] = "1.0.0" } };
        rootedDocument["libraries"]![projectReferenceKey] = new JsonObject { ["type"] = "project", ["path"] = "../Referenced.Project", ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj" };
        var projectReferenceAnalyzer = "analyzers/dotnet/cs/project-reference.dll";
        AddPackageToAssets(rootedDocument, rootedCache, "Project.Root", new List<string> { "tools/project-root.ps1", projectReferenceAnalyzer }, new JsonObject
        {
            ["tools"] = new JsonObject { ["tools/project-root.ps1"] = new JsonObject() },
            ["analyzers"] = new JsonObject { [projectReferenceAnalyzer] = new JsonObject() }
        });
        var projectReferenceAnalyzerPath = Path.Combine(rootedCache, "Project.Root", "1.0.0", projectReferenceAnalyzer.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, projectReferenceAnalyzerPath, overwrite: true);
        File.WriteAllText(rootedAssets, rootedDocument.ToJsonString());
        var rootedResult = ResolvedGraphClassifier.Analyze(rootedAssets, rootedRoot, strictContent: false);
        Assert.True(rootedResult.IsComplete && rootedResult.ResolvedPackageCount == 5,
            "Direct, transitive, cyclic, and project-reference roots did not share one complete closure: " + string.Join(" | ", rootedResult.IncompleteReasons));
        Assert.True(rootedResult.Entries.Any(entry => entry.PackageId == "Chain.Leaf" && entry.Relationship == "transitive") &&
                rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Relationship == "transitive"),
            "Reachable transitive and project-reference packages were not classified with the expected relationship.");
        Assert.True(rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Capability == CapabilityKind.CompilerExtension && entry.Active),
            "An analyzer exported only through a project-reference root was not activated: " + string.Join(" | ", rootedResult.Entries.Select(entry => $"{entry.PackageId}:{entry.Capability}:{entry.Active}:{entry.PackageRelativePath}")) + " reasons=" + string.Join(" | ", rootedResult.IncompleteReasons));

        var ridRoot = Path.Combine(scratch, "reachability-rid");
        var ridObj = Path.Combine(ridRoot, "obj");
        var ridCache = Path.Combine(ridRoot, "cache");
        Directory.CreateDirectory(ridObj);
        var ridAssets = Path.Combine(ridObj, "project.assets.json");
        WriteAssets(ridAssets, ridCache, "Direct.Root", Array.Empty<string>());
        var ridDocument = JsonNode.Parse(File.ReadAllText(ridAssets))!.AsObject();
        AddPackageToAssets(ridDocument, ridCache, "Rid.Only", new List<string> { "runtimes/win-x64/native/rid-only.dll" }, new JsonObject
        {
            ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() }
        });
        ridDocument["targets"]!["net8.0"]!.AsObject().Remove("Rid.Only/1.0.0");
        var ridTarget = ridDocument["targets"]!["net8.0"]!.DeepClone()!.AsObject();
        ridTarget["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject { ["Rid.Only"] = "1.0.0" };
        ridTarget["Rid.Only/1.0.0"] = new JsonObject { ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() } };
        ridDocument["targets"]!.AsObject()["net8.0/win-x64"] = ridTarget;
        File.WriteAllText(ridAssets, ridDocument.ToJsonString());
        var ridResult = ResolvedGraphClassifier.Analyze(ridAssets, ridRoot, strictContent: false);
        Assert.True(ridResult.IsComplete && ridResult.Entries.Count(entry => entry.PackageId == "Rid.Only") == 1 &&
                ridResult.Entries.Single(entry => entry.PackageId == "Rid.Only").RuntimeIdentifier == "win-x64",
            "A package reachable only from the RID-specific target graph was not confined to that graph: " + string.Join(" | ", ridResult.IncompleteReasons));
    }

    [Fact]
    public void VersionConflictsAreResolvedPerTarget()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;



        var surfaceRoot = Path.Combine(scratch, "version-aware-all-surfaces");
        var surfaceObj = Path.Combine(surfaceRoot, "obj");
        var surfaceCache = Path.Combine(surfaceRoot, "cache");
        Directory.CreateDirectory(surfaceObj);
        var surfaceAssets = Path.Combine(surfaceObj, "project.assets.json");
        WriteAssets(surfaceAssets, surfaceCache, "Versioned.Surface", Array.Empty<string>());
        var surfaceBaseline = Path.Combine(surfaceObj, "baseline.json");
        Assert.True(CaptureCommand("baseline", surfaceAssets, "--output", surfaceBaseline, "--no-telemetry").ExitCode == 0,
            "The valid all-surfaces baseline could not be created.");

        var surfaceDocument = JsonNode.Parse(File.ReadAllText(surfaceAssets))!.AsObject();
        surfaceDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Versioned.Surface"]!["version"] = "[1.0.0, 1.0.0]";
        var surfaceFiles = new[]
        {
        "build/conflict.targets",
        "analyzers/dotnet/cs/conflict.dll",
        "contentFiles/cs/any/conflict.cs",
        "runtimes/win-x64/native/conflict.dll",
        "tools/conflict.ps1"
    };
        AddPackageVersionToAssets(surfaceDocument, surfaceCache, "Versioned.Surface", "1.0.1", surfaceFiles, new JsonObject
        {
            ["build"] = new JsonObject { ["build/conflict.targets"] = new JsonObject() },
            ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/conflict.dll"] = new JsonObject() },
            ["contentFiles"] = new JsonObject
            {
                ["contentFiles/cs/any/conflict.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
            },
            ["native"] = new JsonObject { ["runtimes/win-x64/native/conflict.dll"] = new JsonObject() },
            ["tools"] = new JsonObject { ["tools/conflict.ps1"] = new JsonObject() }
        });
        var surfacePackageRoot = Path.Combine(surfaceCache, "Versioned.Surface", "1.0.1");
        File.WriteAllText(Path.Combine(surfacePackageRoot, "build", "conflict.targets"), "<Project />");
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
            Path.Combine(surfacePackageRoot, "analyzers", "dotnet", "cs", "conflict.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(surfaceObj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Versioned.Surface/1.0.1/build/conflict.targets\" /></Project>");
        File.WriteAllText(surfaceAssets, surfaceDocument.ToJsonString());
        AssertRestoreIdentityFailure(surfaceAssets, surfaceRoot, surfaceBaseline, "all-surfaces-multi-version-selection");
    }


    [Theory]
    [InlineData("exact-direct-selection")]
    [InlineData("range-direct-selection")]
    [InlineData("range-transitive-selection")]
    public void TargetVersionsMustMatchDependencyRequirements(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;


        var matrix = new (string Name, string RootPackage, string? TransitivePackage, string DirectRange)[]
        {
        ("exact-direct-selection", "Versioned.Direct", null, "[1.0.0, 1.0.0]"),
        ("range-direct-selection", "Versioned.Range.Direct", null, "[1.0.0, 2.0.0)"),
        ("range-transitive-selection", "Versioned.Transitive.Root", "Versioned.Transitive.Dependency", "[1.0.0, 2.0.0)")
        };
        var (name, rootPackage, transitivePackage, directRange) = matrix.Single(value => value.Item1 == caseName);

        var root = Path.Combine(scratch, "version-aware-" + name);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, rootPackage, Array.Empty<string>());
        var baseline = Path.Combine(obj, "baseline.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            $"The valid {name} baseline could not be created.");

        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        if (transitivePackage is null)
        {
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]![rootPackage]!["version"] = directRange;
        }
        else
        {
            document["targets"]!["net8.0"]![rootPackage + "/1.0.0"]!["dependencies"] = new JsonObject
            {
                [transitivePackage] = directRange
            };
            AddPackageToAssets(document, cache, transitivePackage, Array.Empty<string>(), new JsonObject
            {
                ["dependencies"] = new JsonObject()
            });
        }

        File.WriteAllText(assets, document.ToJsonString());
        var validResult = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var expectedResolvedCount = transitivePackage is null ? 1 : 2;
        Assert.True(validResult.IsComplete && validResult.ResolvedPackageCount == expectedResolvedCount,
            $"The valid {name} version-range graph was not resolved before adding the conflicting sibling: {string.Join(" | ", validResult.IncompleteReasons)}");

        AddPackageVersionToAssets(document, cache, transitivePackage ?? rootPackage, "1.0.1", Array.Empty<string>());
        File.WriteAllText(assets, document.ToJsonString());
        AssertRestoreIdentityFailure(assets, root, baseline, name);

    }
}
