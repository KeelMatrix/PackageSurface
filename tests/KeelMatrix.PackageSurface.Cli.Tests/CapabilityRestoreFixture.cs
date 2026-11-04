using System.Globalization;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class CapabilityRestoreFixture
{
    internal static void WriteAnalyzerAssets(
        string path,
        string cache,
        bool direct,
        string analyzerRelativePath,
        string projectFileName = "Test.csproj",
        bool includeAnalyzers = false,
        IReadOnlyList<string>? additionalAnalyzerRelativePaths = null)
    {
        const string compilerId = "CompilerPackage";
        const string rootId = "RootPackage";
        const string version = "1.0.0";
        var compilerKey = compilerId + "/" + version;
        var rootKey = rootId + "/" + version;
        var target = new JsonObject();
        var libraries = new JsonObject();
        var compilerFiles = new JsonArray();
        compilerFiles.Add(analyzerRelativePath);
        foreach (var additionalPath in additionalAnalyzerRelativePaths ?? Array.Empty<string>())
        {
            compilerFiles.Add(additionalPath);
        }
        target[compilerKey] = new JsonObject { ["type"] = "package" };
        libraries[compilerKey] = new JsonObject { ["type"] = "package", ["path"] = compilerKey, ["files"] = compilerFiles };
        var directPackageId = compilerId;
        if (!direct)
        {
            target[rootKey] = new JsonObject
            {
                ["type"] = "package",
                ["dependencies"] = new JsonObject { [compilerId] = version }
            };
            libraries[rootKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = rootKey,
                ["files"] = new JsonArray()
            };
            directPackageId = rootId;
        }

        foreach (var analyzerPath in new[] { analyzerRelativePath }.Concat(additionalAnalyzerRelativePaths ?? Array.Empty<string>()))
        {
            var packageRoot = Path.Combine(cache, compilerId, version, Path.GetDirectoryName(analyzerPath)!.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(packageRoot);
            File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, Path.Combine(packageRoot, Path.GetFileName(analyzerPath)), overwrite: true);
        }
        Directory.CreateDirectory(Path.Combine(cache, rootId, version));
        var dependency = PackageDependency();
        dependency["include"] = includeAnalyzers ? "analyzers" : "Runtime, Compile, Build, Native, ContentFiles, BuildTransitive";
        var frameworks = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [directPackageId] = dependency } }
        };
        var root = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = new JsonObject { ["net8.0"] = target },
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
            ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, projectFileName) }, ["frameworks"] = frameworks }
        };
        File.WriteAllText(path, root.ToJsonString());
        WriteGeneratedImportEvidence(path, projectFileName);
    }

    internal static void WriteContentAssets(
        string path,
        string cache,
        string projectFileName,
        string contentRelativePath,
        string codeLanguage,
        string buildAction,
        string? malformedMetadata = null)
    {
        var packageId = "Content.Package";
        WriteAssets(path, cache, packageId, new[] { contentRelativePath }, createFiles: true, projectFileName: projectFileName);
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
        var metadata = new JsonObject
        {
            ["buildAction"] = buildAction,
            ["codeLanguage"] = codeLanguage
        };
        if (malformedMetadata == "buildAction") metadata["buildAction"] = new JsonArray { JsonValue.Create("Compile") };
        if (malformedMetadata == "codeLanguage") metadata["codeLanguage"] = new JsonArray { JsonValue.Create("cs") };
        package["contentFiles"] = new JsonObject { [contentRelativePath] = metadata };
        File.WriteAllText(path, document.ToJsonString());
    }

    internal static void WriteLargeAssets(string path, string cache, int packageCount)
    {
        var target = new JsonObject();
        var libraries = new JsonObject();
        for (var index = 0; index < packageCount; index++)
        {
            var packageId = "Reachable.Package." + index.ToString("D4", CultureInfo.InvariantCulture);
            var packageKey = packageId + "/1.0.0";
            Directory.CreateDirectory(Path.Combine(cache, packageId, "1.0.0"));
            target[packageKey] = new JsonObject();
            libraries[packageKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = packageKey,
                ["files"] = new JsonArray()
            };
        }

        var frameworkDependencies = new JsonObject();
        for (var index = 0; index < packageCount; index++)
        {
            frameworkDependencies[$"Reachable.Package.{index.ToString("D4", CultureInfo.InvariantCulture)}"] = PackageDependency();
        }

        var root = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = new JsonObject { ["net8.0"] = target },
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, "Test.csproj") },
                ["frameworks"] = new JsonObject
                {
                    ["net8.0"] = new JsonObject
                    {
                        ["dependencies"] = frameworkDependencies
                    }
                }
            }
        };
        File.WriteAllText(path, root.ToJsonString());
        WriteGeneratedImportEvidence(path);
    }
}
