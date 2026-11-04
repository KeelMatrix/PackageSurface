using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class PackageFixture
{
    internal static void AddPackageToAssets(JsonObject document, string cache, string packageId, IReadOnlyList<string> files, JsonObject? targetMetadata = null, string libraryType = "package")
    {
        var packageKey = packageId + "/1.0.0";
        AddPackageVersionToAssets(document, cache, packageId, "1.0.0", files, targetMetadata, libraryType);
    }

    internal static void AddPackageVersionToAssets(JsonObject document, string cache, string packageId, string version, IReadOnlyList<string> files, JsonObject? targetMetadata = null, string libraryType = "package")
    {
        var packageKey = packageId + "/" + version;
        var target = targetMetadata?.DeepClone()?.AsObject() ?? new JsonObject();
        document["targets"]!["net8.0"]!.AsObject()[packageKey] = target;
        var filesNode = new JsonArray();
        foreach (var file in files)
        {
            filesNode.Add(file);
        }

        document["libraries"]!.AsObject()[packageKey] = new JsonObject
        {
            ["type"] = libraryType,
            ["path"] = packageKey,
            ["files"] = filesNode
        };
        var packageRoot = Path.Combine(cache, packageId, version);
        Directory.CreateDirectory(packageRoot);
        foreach (var file in files)
        {
            var physical = Path.Combine(packageRoot, file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            File.WriteAllText(physical, "fixture");
        }
    }

    internal static void AddUnreachableImportPackage(JsonObject document, string cache, string packageId, string relativePath)
    {
        var packageKey = packageId + "/1.0.0";
        document["libraries"]!.AsObject()[packageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = packageKey,
            ["files"] = new JsonArray(relativePath)
        };
        var packagePath = Path.Combine(cache, packageId, "1.0.0", relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        File.WriteAllText(packagePath, "<Project />");
    }

    internal static void WritePackageBuildFiles(string cache, IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(cache, "Edge.Package", "1.0.0", file.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, "<Project />");
        }
    }
}
