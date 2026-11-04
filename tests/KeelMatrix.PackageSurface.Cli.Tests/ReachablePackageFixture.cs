using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class ReachablePackageFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal ReachablePackageFixture()
    {

        workspace = new CliScenarioWorkspace();
        var parent = workspace.Root;

        var root = Path.Combine(parent, "reachable-package-alias");
        var obj = Path.Combine(root, "obj");
        var fallback = Path.Combine(root, "fallback-packages");
        var unusedGlobal = Path.Combine(root, "global-packages");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var files = new[]
        {
        "build/direct.props",
        "buildTransitive/transitive.targets",
        "buildMultiTargeting/outer.targets",
        "props/import.props",
        "targets/import.targets",
        "runtimes/win-x64/lib/runtime.dll",
        "ref/net8.0/compile.dll",
        "contentFiles/cs/net8.0/source.cs",
        "analyzers/dotnet/cs/analyzer.dll",
        "runtimes/win-x64/native/native.dll",
        "resource/en-US/messages.resources",
        "tools/tool.ps1",
        "build/nested/helper.targets"
    };
        WriteAssets(assets, fallback, "Alias.Package", files, createFiles: true);

        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["packageFolders"] = new JsonObject
        {
            [unusedGlobal] = new JsonObject(),
            [fallback] = new JsonObject()
        };
        var fallbackPackageRoot = Path.Combine(fallback, "Alias.Package", "1.0.0");
        var globalPackageRoot = Path.Combine(unusedGlobal, "Alias.Package", "1.0.0");
        Directory.CreateDirectory(Path.GetDirectoryName(globalPackageRoot)!);
        Directory.Move(fallbackPackageRoot, globalPackageRoot);
        const string transitivePackageKey = "Transitive.Alias.Package/1.0.0";
        const string transitiveRelative = "tools/transitive-tool.ps1";
        var transitiveRoot = Path.Combine(fallback, "Transitive.Alias.Package", "1.0.0");
        var transitiveFile = Path.Combine(transitiveRoot, transitiveRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(transitiveFile)!);
        File.WriteAllText(transitiveFile, "transitive fixture");
        document["libraries"]![transitivePackageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = transitivePackageKey,
            ["files"] = new JsonArray(transitiveRelative)
        };
        var directTarget = document["targets"]!["net8.0"]!["Alias.Package/1.0.0"]!.AsObject();
        directTarget["dependencies"] = new JsonObject { ["Transitive.Alias.Package"] = "1.0.0" };
        document["targets"]!["net8.0"]![transitivePackageKey] = new JsonObject { ["type"] = "package" };
        document["targets"]!.AsObject()["net8.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone();
        File.WriteAllText(assets, document.ToJsonString());
        var packageRoot = globalPackageRoot;
        foreach (var relative in files)
        {
            var physical = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (physical.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || physical.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(physical, "<Project />");
            }
            else if (relative.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, physical, overwrite: true);
            }
        }
        var packageBytes = files.ToDictionary(
                    relative => relative,
                    relative => File.ReadAllBytes(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
                    StringComparer.Ordinal);

        Root = root; AssetsPath = assets; PackageRoot = packageRoot; Files = files; Bytes = packageBytes; TransitiveFile = transitiveFile;
    }
    internal string Root { get; }
    internal string AssetsPath { get; }
    internal string PackageRoot { get; }
    internal string[] Files { get; }
    internal Dictionary<string, byte[]> Bytes { get; }
    internal string TransitiveFile { get; }
    public void Dispose() => workspace.Dispose();

}
