using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class NestedImportTests
{
    [Fact]
    public void NestedImportsAreAnalyzed()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "nested-imports");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageKey = "Nested.Package/1.0.0";
        var packageRoot = Path.Combine(cache, "Nested.Package", "1.0.0");
        var packageTargets = "build/Nested.Package.targets";
        var helperTargets = "helpers/Helper.targets";
        var grandchildProps = "helpers/Grandchild.props";
        WriteAssets(assets, cache, "Nested.Package", new[] { packageTargets, helperTargets, grandchildProps }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageKey]!["build"] = new JsonObject { [packageTargets] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../helpers/Helper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)),
            "<Project><ImportGroup Condition=\"'$(TargetFramework)' == 'net8.0'\"><Import Project=\"Grandchild.props\" /></ImportGroup></Project>");
        File.WriteAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<Project><PropertyGroup><NestedValue>1</NestedValue></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Package/1.0.0/{packageTargets}\" /></Project>");

        var baseline = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        var helper = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase));
        var grandchild = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(grandchildProps, StringComparison.OrdinalIgnoreCase));
        Assert.True(baseline.IsComplete && helper.Active && helper.Sha256 is not null && grandchild.Active && grandchild.Sha256 is not null,
            $"Nested arbitrary-path import graph was not active and strictly fingerprinted. helper={helper.Active}/{helper.Sha256}; grandchild={grandchild.Active}/{grandchild.Sha256}; complete={baseline.IsComplete}; reasons={string.Join(" | ", baseline.IncompleteReasons)}");

        File.AppendAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<!-- changed -->");
        var changed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(DiffEngine.Compare(baseline.Entries.Select(BaselineEntry.From).ToArray(), changed.Entries, strictContent: true).Any(diagnostic => diagnostic.Id == "PS005"),
            "A nested helper-only content change was not detected.");

        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"Missing.props\" /></Project>");
        var missing = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(!missing.IsComplete && missing.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)),
            "A missing arbitrary-path nested import was not fail-closed.");

        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../build/Nested.Package.targets\" /></Project>");
        var cycle = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(cycle.IsComplete && cycle.Entries.Any(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A bounded static import cycle was not handled deterministically.");

        File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"$(SensitiveDynamicImportMarker)\" /></Project>");
        var dynamic = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!dynamic.IsComplete && dynamic.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)) &&
            dynamic.IncompleteReasons.All(reason => !reason.Contains("SensitiveDynamicImportMarker", StringComparison.OrdinalIgnoreCase)),
            "An unsupported dynamic nested import was not fail-closed without disclosing its expression.");

    }
}
