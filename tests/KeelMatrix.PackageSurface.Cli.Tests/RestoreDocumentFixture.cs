using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class RestoreDocumentFixture
{
    internal static void WriteAssets(string path, string cache, string packageId, IReadOnlyList<string> files, bool createFiles = false, string projectFileName = "Test.csproj")
    {
        var packageKey = packageId + "/1.0.0";
        var filesNode = new JsonArray();
        foreach (var file in files)
        {
            filesNode.Add(file);
        }

        Directory.CreateDirectory(Path.Combine(cache, packageId, "1.0.0"));
        if (createFiles)
        {
            foreach (var file in files.Where(value => !value.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(value)))
            {
                var physical = Path.Combine(cache, packageId, "1.0.0", file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
                File.WriteAllText(physical, "fixture");
            }
        }
        var target = new JsonObject { [packageKey] = new JsonObject() };
        var targets = new JsonObject { ["net8.0"] = target };
        var libraries = new JsonObject
        {
            [packageKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = packageKey,
                ["files"] = filesNode
            }
        };
        var packageFolders = new JsonObject { [cache] = new JsonObject() };
        var frameworks = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["dependencies"] = new JsonObject { [packageId] = PackageDependency() }
            }
        };
        var root = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = targets,
            ["libraries"] = libraries,
            ["packageFolders"] = packageFolders,
            ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, projectFileName) }, ["frameworks"] = frameworks }
        };
        File.WriteAllText(path, root.ToJsonString());
        WriteGeneratedImportEvidence(path, projectFileName);
    }

    internal static JsonObject PackageDependency(string version = "[1.0.0, )") => new()
    {
        ["version"] = version,
        ["target"] = "Package"
    };

    internal static void WriteTargetGraphAssets(string assets, string cache, string projectPath, int targetCount, bool includeToolAsset = false)
    {
        const string packageKey = "TargetGraph.Package/1.0.0";
        const string toolPath = "tools/fixture.ps1";
        var targets = new JsonObject();
        for (var index = 0; index < targetCount; index++)
        {
            targets[$"net8.0/rid{index.ToString(CultureInfo.InvariantCulture)}"] = includeToolAsset
                ? new JsonObject { [packageKey] = new JsonObject() }
                : new JsonObject();
        }

        var libraries = includeToolAsset
            ? new JsonObject
            {
                [packageKey] = new JsonObject
                {
                    ["type"] = "package",
                    ["path"] = packageKey,
                    ["files"] = new JsonArray(toolPath)
                }
            }
            : new JsonObject();
        if (includeToolAsset)
        {
            var packageRoot = Path.Combine(cache, "TargetGraph.Package", "1.0.0", "tools");
            Directory.CreateDirectory(packageRoot);
            File.WriteAllText(Path.Combine(packageRoot, "fixture.ps1"), "fixture");
        }

        var frameworkDependencies = includeToolAsset
            ? new JsonObject { ["TargetGraph.Package"] = PackageDependency() }
            : new JsonObject();
        var root = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = targets,
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = projectPath },
                ["frameworks"] = new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = frameworkDependencies } }
            }
        };
        Directory.CreateDirectory(cache);
        File.WriteAllText(assets, root.ToJsonString());
        var obj = Path.GetDirectoryName(assets)!;
        var projectFile = Path.GetFileName(projectPath);
        WriteGeneratedImportEvidence(assets, projectFile);
    }

    internal static JsonObject CreateValidRestoreShapeDocument(string sourceAssets, int format)
    {
        var root = JsonNode.Parse(File.ReadAllText(sourceAssets))!.AsObject();
        var project = root["project"]!.AsObject();
        var restore = project["restore"]!.AsObject();
        restore["centralPackageVersionsManagementEnabled"] = true;
        restore["configFilePaths"] = new JsonArray("nuget.config");
        restore["crossTargeting"] = true;
        restore["originalTargetFrameworks"] = new JsonArray("net8.0");
        restore["outputPath"] = "obj/";
        restore["packagesPath"] = "packages/";
        restore["projectName"] = "Test";
        restore["projectStyle"] = "PackageReference";
        restore["projectUniqueName"] = "Test.csproj";
        restore["restoreAuditProperties"] = new JsonObject
        {
            ["enableAudit"] = "true",
            ["auditLevel"] = "low",
            ["auditMode"] = "direct",
            ["suppressedAdvisories"] = new JsonObject { ["ADV-1"] = null }
        };
        restore["fallbackFolders"] = new JsonArray("fallback");
        restore["sources"] = new JsonObject { ["https://example.test/v3/index.json"] = new JsonObject() };
        restore["warningProperties"] = new JsonObject
        {
            ["allWarningsAsErrors"] = true,
            ["noWarn"] = new JsonArray("NU1000"),
            ["warnAsError"] = new JsonArray("NU1605"),
            ["warnNotAsError"] = new JsonArray("NU1701")
        };
        restore["compilerApiVersion"] = "4.0";
        restore["SdkAnalysisLevel"] = "10.0.400";
        restore["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["framework"] = "net8.0",
                ["targetAlias"] = "net8.0",
                ["projectReferences"] = new JsonObject
                {
                    ["Project.csproj"] = new JsonObject
                    {
                        ["projectPath"] = "Project.csproj",
                        ["includeAssets"] = "runtime; build",
                        ["excludeAssets"] = "none",
                        ["privateAssets"] = "all"
                    }
                }
            }
        };

        var framework = project["frameworks"]!["net8.0"]!.AsObject();
        framework["framework"] = "net8.0";
        framework["targetAlias"] = "net8.0";
        framework["dependencies"] = new JsonObject { ["XmlPackage"] = "[1.0.0, )" };
        framework["assetTargetFallback"] = true;
        framework["centralPackageVersions"] = new JsonObject { ["XmlPackage"] = "1.0.0" };
        framework["frameworkReferences"] = new JsonObject
        {
            ["Microsoft.NETCore.App"] = new JsonObject { ["privateAssets"] = "all" }
        };
        framework["imports"] = new JsonArray("net461");
        framework["runtimeIdentifierGraphPath"] = "PortableRuntimeIdentifierGraph.json";
        framework["downloadDependencies"] = new JsonArray
    {
        new JsonObject { ["name"] = "PackageDownload", ["version"] = "[1.0.0, )" }
    };
        framework["warn"] = true;

        if (format == 4)
        {
            root["version"] = 4;
            root["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
        }

        return root;
    }

    internal static void RenameJsonProperty(JsonObject parent, string oldName, string newName)
    {
        var value = parent[oldName]?.DeepClone() ?? throw new InvalidOperationException($"Missing JSON property {oldName}.");
        parent.Remove(oldName);
        parent[newName] = value;
    }

    internal static JsonObject GetIdentityMap(JsonObject document, string path)
    {
        var segments = path.Split('.');
        JsonNode current = document;
        foreach (var segment in segments)
        {
            current = current[segment] ?? throw new InvalidOperationException($"Identity map path '{path}' was not found.");
        }

        return current.AsObject();
    }

    internal static string InsertExactPropertyDuplicate(string json, string propertyName, string value, int searchStart = 0)
    {
        var token = JsonSerializer.Serialize(propertyName) + ":" + value;
        var first = json.IndexOf(token, searchStart, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Could not locate exact property token for '{propertyName}'.");
        return json[..first] + token + "," + token + json[(first + token.Length)..];
    }

    internal static string InsertCaseVariantPropertyDuplicate(string json, string propertyName, string value)
    {
        var token = JsonSerializer.Serialize(propertyName) + ":" + value;
        var first = json.IndexOf(token, StringComparison.Ordinal);
        Assert.True(first >= 0, $"Could not locate case-variant property token for '{propertyName}'.");
        var variant = propertyName.Length == 0
            ? propertyName
            : char.IsUpper(propertyName[0])
                ? char.ToLowerInvariant(propertyName[0]) + propertyName[1..]
                : char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
        var duplicate = JsonSerializer.Serialize(variant) + ":" + value;
        return json[..first] + token + "," + duplicate + json[(first + token.Length)..];
    }

    internal static string InsertRootPropertyDuplicate(string json, JsonObject root, string propertyName) =>
        InsertExactPropertyDuplicate(json, propertyName, root[propertyName]!.ToJsonString());

    internal static string InsertCaseVariantRootPropertyDuplicate(string json, JsonObject root, string propertyName) =>
        InsertCaseVariantPropertyDuplicate(json, propertyName, root[propertyName]!.ToJsonString());
}
