using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class AssetClassificationTests
{
    [Fact]
    public void ProjectLevelAssetsAreAggregated()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "project-aggregation");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var packageId = "Aggregate.Package";
        var packageKey = packageId + "/1.0.0";
        var relativePath = "buildMultiTargeting/Aggregate.props";
        var packageRoot = Path.Combine(cache, packageId, "1.0.0");
        Directory.CreateDirectory(Path.Combine(packageRoot, "buildMultiTargeting"));
        File.WriteAllText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)), "<Project />");
        var targetPackage = new JsonObject
        {
            ["type"] = "package",
            ["buildMultiTargeting"] = new JsonObject { [relativePath] = new JsonObject() }
        };
        var targets = new JsonObject
        {
            ["net8.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() },
            ["net9.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() }
        };
        var libraries = new JsonObject
        {
            [packageKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = packageKey,
                ["files"] = new JsonArray(relativePath)
            }
        };
        var projectFile = Path.Combine(root, "Aggregate.csproj");
        File.WriteAllText(projectFile, "<Project />");
        File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.props"), "<Project><Import Project='$(NuGetPackageRoot)/Aggregate.Package/1.0.0/buildMultiTargeting/Aggregate.props' /></Project>");
        File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.targets"), "<Project />");
        var document = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = targets,
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = projectFile },
                ["frameworks"] = new JsonObject
                {
                    ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } },
                    ["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } }
                }
            }
        };
        var assets = Path.Combine(obj, "project.assets.json");
        File.WriteAllText(assets, document.ToJsonString());
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var entry = result.Entries.SingleOrDefault(candidate => candidate.Capability == CapabilityKind.BuildMultiTargeting);
        Assert.True(result.IsComplete && entry is not null && entry.Relationship == "direct" && entry.Active,
            "Project-level direct/transitive aggregation was not deterministic or direct-wins.");
    }
}
