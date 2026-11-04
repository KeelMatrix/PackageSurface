using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class NestedImportFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal NestedImportFixture()
    {

        workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "nested-phase-matrix");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Nested.Phase.Matrix";
        var files = new[]
        {
        "build/Root.props",
        "build/Helper.targets",
        "buildTransitive/Transitive.targets",
        "buildTransitive/TransitiveHelper.props",
        "buildMultiTargeting/Outer.props",
        "buildMultiTargeting/OuterHelper.targets"
    };
        WriteAssets(assets, cache, packageId, files, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
        package["build"] = new JsonObject { ["build/Root.props"] = new JsonObject() };
        package["buildTransitive"] = new JsonObject { ["buildTransitive/Transitive.targets"] = new JsonObject() };
        package["buildMultiTargeting"] = new JsonObject { ["buildMultiTargeting/Outer.props"] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());

        var packageRoot = Path.Combine(cache, packageId, "1.0.0");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Root.props"), "<Project><Import Project=\"Helper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project><Import Project=\"Root.props\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "Transitive.targets"), "<Project><Import Project=\"TransitiveHelper.props\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "TransitiveHelper.props"), "<Project />");
        File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props"), "<Project><Import Project=\"OuterHelper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "OuterHelper.targets"), "<Project />");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"),
            "<Project>" +
            "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/build/Root.props\" />" +
            "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildMultiTargeting/Outer.props\" />" +
            "</Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildTransitive/Transitive.targets\" /></Project>");


        Root = root; AssetsPath = assets; PackageRoot = packageRoot; Files = files;
    }
    internal string Root { get; }
    internal string AssetsPath { get; }
    internal string PackageRoot { get; }
    internal string[] Files { get; }
    public void Dispose() => workspace.Dispose();

}
