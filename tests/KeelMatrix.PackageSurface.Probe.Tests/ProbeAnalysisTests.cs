using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class ProbeAnalysisTests
{
    private static readonly string[] BuildPropsAsset = { "build/Example.props" };

    private static readonly string[] TraversalAsset = { "../escape.props" };
    private static readonly string[] MissingAsset = { "build/expected.targets" };

    [Fact]
    public void ValidEmptyPackageGraphProducesACompleteSurface()
    {
        using var workspace = new ProbeScenarioWorkspace();

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.True(result.IsComplete, string.Join("; ", result.IncompleteReasons));
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.ResolvedPackageCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"targets\":[]}")]
    public void MalformedRestoreDocumentsFailClosed(string contents)
    {
        using var workspace = new ProbeScenarioWorkspace();
        File.WriteAllText(workspace.AssetsPath, contents);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    [Fact]
    public void MissingAssetsFileFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace();

        var result = ResolvedGraphClassifier.Analyze(Path.Combine(workspace.Obj, "missing.assets.json"), workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    [Fact]
    public void TraversalLookingPackageAssetFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace(TraversalAsset);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.Contains(result.IncompleteReasons, reason => reason.Contains("invalid relative asset path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingReachablePackageAssetFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace(MissingAsset);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Contains(result.IncompleteReasons, reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OversizedRestoreFileFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace();
        File.WriteAllText(workspace.AssetsPath, new string('x', 16 * 1024 * 1024 + 1));

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }


    [Fact]
    public void ComposedTargetResultsPreserveFrameworkRidAndActiveState()
    {
        using var workspace = new ProbeScenarioWorkspace();
        const string packageKey = "Example.Package/1.0.0";
        const string linuxAsset = "runtimes/linux-x64/native/libfixture.so";
        const string windowsAsset = "runtimes/win-x64/native/fixture.dll";
        var packageRoot = Path.Combine(workspace.Cache, "Example.Package", "1.0.0");
        foreach (var asset in new[] { linuxAsset, windowsAsset })
        {
            var path = Path.Combine(packageRoot, asset.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, asset);
        }

        var document = JsonNode.Parse(File.ReadAllText(workspace.AssetsPath))!.AsObject();
        document["libraries"]![packageKey]!["files"] = new JsonArray(linuxAsset, windowsAsset);
        document["targets"] = new JsonObject
        {
            ["net8.0/linux-x64"] = new JsonObject
            {
                [packageKey] = new JsonObject { ["native"] = new JsonObject { [linuxAsset] = new JsonObject() } }
            },
            ["net9.0/win-x64"] = new JsonObject
            {
                [packageKey] = new JsonObject { ["native"] = new JsonObject { [windowsAsset] = new JsonObject() } }
            }
        };
        document["project"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject
                {
                    ["Example.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
                }
            },
            ["net9.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject
                {
                    ["Example.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
                }
            }
        };
        File.WriteAllText(workspace.AssetsPath, document.ToJsonString());

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root, strictContent: true);

        Assert.True(result.IsComplete, string.Join("; ", result.IncompleteReasons));
        Assert.Equal(1, result.ResolvedPackageCount);
        Assert.Equal(4, result.Entries.Count);
        Assert.All(result.Entries, entry =>
        {
            Assert.Equal("direct", entry.Relationship);
            Assert.Equal(CapabilityKind.NativeRuntime, entry.Capability);
            Assert.True(entry.Present);
        });
        Assert.Contains(result.Entries, entry => entry.TargetFramework == "net8.0" && entry.RuntimeIdentifier == "linux-x64" && entry.PackageRelativePath == linuxAsset && entry.Active && entry.Sha256 is null);
        Assert.Contains(result.Entries, entry => entry.TargetFramework == "net8.0" && entry.RuntimeIdentifier == "linux-x64" && entry.PackageRelativePath == windowsAsset && !entry.Active && entry.Sha256 is null);
        Assert.Contains(result.Entries, entry => entry.TargetFramework == "net9.0" && entry.RuntimeIdentifier == "win-x64" && entry.PackageRelativePath == windowsAsset && entry.Active && entry.Sha256 is null);
        Assert.Contains(result.Entries, entry => entry.TargetFramework == "net9.0" && entry.RuntimeIdentifier == "win-x64" && entry.PackageRelativePath == linuxAsset && !entry.Active && entry.Sha256 is null);
    }


    [Fact]
    public void BuildImportConditionsUseTargetAliasesForEachFramework()
    {
        using var workspace = new ProbeScenarioWorkspace(BuildPropsAsset, createFiles: true);
        const string packageKey = "Example.Package/1.0.0";
        const string asset = "build/Example.props";
        var document = JsonNode.Parse(File.ReadAllText(workspace.AssetsPath))!.AsObject();
        document["targets"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                [packageKey] = new JsonObject { ["build"] = new JsonObject { [asset] = new JsonObject() } }
            },
            ["net8.0-windows7.0"] = new JsonObject
            {
                [packageKey] = new JsonObject { ["build"] = new JsonObject { [asset] = new JsonObject() } }
            }
        };
        document["project"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject
                {
                    ["Example.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
                }
            },
            ["net8.0-windows7.0"] = new JsonObject
            {
                ["targetAlias"] = "net8.0-windows",
                ["dependencies"] = new JsonObject
                {
                    ["Example.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
                }
            }
        };
        File.WriteAllText(workspace.AssetsPath, document.ToJsonString());

        var importPath = Path.Combine(workspace.Cache, "Example.Package", "1.0.0", asset.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(importPath, "<Project />");
        File.WriteAllText(
            Path.Combine(workspace.Obj, "Test.csproj.nuget.g.props"),
            $"<Project><Import Project=\"{importPath.Replace('\\', '/')}\" Condition=\"'$(TargetFramework)' == 'net8.0' OR '$(TargetFramework)' == 'net8.0-windows'\" /></Project>");

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.True(result.IsComplete, string.Join("; ", result.IncompleteReasons));
        var entries = result.Entries.Where(entry => entry.PackageRelativePath == asset).ToArray();
        Assert.Equal(2, entries.Length);
        Assert.All(entries, entry => Assert.True(entry.Active, $"{entry.TargetFramework} should match the import target alias."));
    }
}
