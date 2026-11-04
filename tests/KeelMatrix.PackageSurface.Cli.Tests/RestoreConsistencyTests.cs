using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreConsistencyTests
{
    [Fact]
    public void RestoreIdentitiesReconcileAcrossMetadataSources()
    {
        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;

        try
        {
            var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
            Assert.True(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
                $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
            Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
                "The valid restore-identity fixture could not create its baseline.");



            var duplicateMetadataPath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-conflicting-metadata.assets.json");
            var validJson = File.ReadAllText(format4Path);
            const string metadataToken = "\"targetAlias\":\"net8.0\"";
            var metadataIndex = validJson.IndexOf(metadataToken, StringComparison.Ordinal);
            Assert.True(metadataIndex >= 0, "The valid restore-identity fixture has no target-alias metadata token.");
            File.WriteAllText(
                duplicateMetadataPath,
                validJson[..metadataIndex] + metadataToken + ",\"targetAlias\":\"net9.0\"" + validJson[(metadataIndex + metadataToken.Length)..]);
            try
            {
                AssertRestoreIdentityFailure(duplicateMetadataPath, scratch, baselinePath, "conflicting duplicate metadata");
            }
            finally
            {
                File.Delete(duplicateMetadataPath);
            }





            var recovered = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
            Assert.True(recovered.IsComplete && recovered.ResolvedPackageCount == 1,
                "The valid restore-identity fixture did not recover after invalid identity probes.");

            var narrowedRoot = Path.Combine(scratch, "restore-identity-narrowed");
            var validProject = Path.Combine(narrowedRoot, "Valid", "Valid.csproj");
            var invalidProject = Path.Combine(narrowedRoot, "Invalid", "Invalid.csproj");
            foreach (var projectPath in new[] { validProject, invalidProject })
            {
                var projectRoot = Path.GetDirectoryName(projectPath)!;
                var projectAssets = Path.Combine(projectRoot, "obj", "project.assets.json");
                Directory.CreateDirectory(Path.GetDirectoryName(projectAssets)!);
                File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
                var narrowedDocument = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
                narrowedDocument["project"]!["restore"]!["projectPath"] = projectPath;
                if (projectPath == invalidProject)
                {
                    narrowedDocument["project"]!["frameworks"]!.AsObject()[".NETCoreApp,Version=v8.0"] = new JsonObject
                    {
                        ["framework"] = "net9.0",
                        ["targetAlias"] = "net9.0",
                        ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
                    };
                }

                File.WriteAllText(projectAssets, narrowedDocument.ToJsonString());
                WriteGeneratedImportEvidence(projectAssets, Path.GetFileName(projectPath));
            }

            var solution = Path.Combine(narrowedRoot, "RestoreIdentity.sln");
            File.WriteAllText(solution, string.Join(Environment.NewLine,
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Valid\", \"Valid\\Valid.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
                "EndProject",
                "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Invalid\", \"Invalid\\Invalid.csproj\", \"{22222222-2222-2222-2222-222222222222}\"",
                "EndProject"));

            var aggregate = CaptureCommand("scan", solution, "--format", "json", "--no-telemetry");
            Assert.True(aggregate.ExitCode == 2 && aggregate.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !aggregate.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                "Selecting a valid and malformed restore identity together did not fail closed.");
            var narrowed = CaptureCommand("scan", solution, "--project", validProject, "--format", "json", "--no-telemetry");
            Assert.True(narrowed.ExitCode == 0 && !narrowed.Output.Contains("PS007", StringComparison.Ordinal),
                $"--project did not recover the valid restore identity from a malformed sibling: exit={narrowed.ExitCode}; output={narrowed.Output}");
        }
        finally
        {
            if (File.Exists(format4Path)) File.Delete(format4Path);
            if (File.Exists(baselinePath)) File.Delete(baselinePath);
        }
    }


    [Theory]
    [InlineData("project-effective-framework")]
    [InlineData("project-target-alias")]
    [InlineData("restore-effective-framework")]
    [InlineData("restore-target-alias")]
    [InlineData("restore-cross-map-framework")]
    [InlineData("target-framework-key")]
    [InlineData("rid-framework-key")]
    [InlineData("dependency-group-key")]
    public void FrameworkMetadataMustAgree(string caseName)
    {

        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Assert.True(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
            $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The valid restore-identity fixture could not create its baseline.");


        var matrix = new (string Name, Action<JsonObject> Mutate)[]
            {
            ("project-effective-framework", value => value["project"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("project-target-alias", value => value["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net9.0"),
            ("restore-effective-framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("restore-target-alias", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net9.0"),
            ("restore-cross-map-framework", value =>
            {
                value["project"]!["restore"]!["frameworks"]!["net9.0"] = new JsonObject
                {
                    ["framework"] = "net9.0",
                    ["targetAlias"] = "net9.0"
                };
            }),
            ("target-framework-key", value =>
            {
                var targets = value["targets"]!.AsObject();
                var target = targets["net8.0"]!.DeepClone();
                targets.Remove("net8.0");
                targets["net9.0"] = target;
            }),
            ("rid-framework-key", value =>
            {
                var targets = value["targets"]!.AsObject();
                var target = targets["net8.0"]!.DeepClone();
                targets.Remove("net8.0");
                targets["net9.0/win-x64"] = target;
            }),
            ("dependency-group-key", value =>
            {
                var groups = value["projectFileDependencyGroups"]!.AsObject();
                var group = groups["net8.0"]!.DeepClone();
                groups.Remove("net8.0");
                groups["net9.0"] = group;
            })
            };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-coherence-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
        }
        finally
        {
            File.Delete(path);
        }

    }

    [Theory]
    [InlineData("project-frameworks")]
    [InlineData("restore-frameworks")]
    [InlineData("target-graphs")]
    [InlineData("dependency-groups")]
    public void DuplicateFrameworkIdentitiesFailClosed(string caseName)
    {

        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Assert.True(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
            $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The valid restore-identity fixture could not create its baseline.");


        var matrix = new[]
            {
            (Name: "project-frameworks", MapPath: "project.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "restore-frameworks", MapPath: "project.restore.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "target-graphs", MapPath: "targets", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "dependency-groups", MapPath: "projectFileDependencyGroups", Alias: ".NETCoreApp,Version=v8.0")
        };
        var duplicateMap = matrix.Single(value => value.Item1 == caseName);

        foreach (var duplicateKind in new[] { "case", "moniker" })
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, $"restore-identity-{duplicateMap.Name}-{duplicateKind}.assets.json");
            var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
            var map = GetIdentityMap(document, duplicateMap.MapPath);
            var alias = duplicateKind == "case" ? "NET8.0" : duplicateMap.Alias;
            var duplicate = map["net8.0"]!.DeepClone();
            if (duplicateMap.MapPath.Contains("frameworks", StringComparison.Ordinal))
            {
                duplicate!["framework"] = "net9.0";
                duplicate["targetAlias"] = "net9.0";
            }
            map[alias] = duplicate;
            File.WriteAllText(path, document.ToJsonString());
            try
            {
                AssertRestoreIdentityFailure(path, scratch, baselinePath, $"{duplicateMap.Name} {duplicateKind} duplicate");
            }
            finally
            {
                File.Delete(path);
            }
        }

        var exactPath = Path.Combine(Path.GetDirectoryName(assets)!, $"restore-identity-{duplicateMap.Name}-raw-exact.assets.json");
        var raw = File.ReadAllText(format4Path);
        var rawMap = GetIdentityMap(JsonNode.Parse(raw)!.AsObject(), duplicateMap.MapPath);
        var rawValue = rawMap["net8.0"]!.ToJsonString();
        var rawDuplicate = InsertExactPropertyDuplicate(raw, "net8.0", rawValue);
        File.WriteAllText(exactPath, rawDuplicate);
        try
        {
            AssertRestoreIdentityFailure(exactPath, scratch, baselinePath, $"{duplicateMap.Name} raw exact duplicate");
        }
        finally
        {
            File.Delete(exactPath);
        }

    }

    [Theory]
    [InlineData("library-no-slash")]
    [InlineData("library-empty-id")]
    [InlineData("library-empty-version")]
    [InlineData("library-extra-slash")]
    [InlineData("library-invalid-id")]
    [InlineData("library-invalid-version")]
    [InlineData("target-no-slash")]
    [InlineData("target-empty-id")]
    [InlineData("target-empty-version")]
    [InlineData("target-extra-slash")]
    public void MalformedPackageIdentitiesFailClosed(string caseName)
    {

        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Assert.True(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
            $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The valid restore-identity fixture could not create its baseline.");


        var matrix = new (string Name, Action<JsonObject> Mutate)[]
            {
            ("library-no-slash", root => root["libraries"]!.AsObject()["MalformedPackage"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "MalformedPackage", ["files"] = new JsonArray()
            }),
            ("library-empty-id", root => root["libraries"]!.AsObject()["/1.0.0"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "/1.0.0", ["files"] = new JsonArray()
            }),
            ("library-empty-version", root => root["libraries"]!.AsObject()["MalformedPackage/"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "MalformedPackage/", ["files"] = new JsonArray()
            }),
            ("library-extra-slash", root => root["libraries"]!.AsObject()["Malformed/1.0.0/extra"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed/1.0.0/extra", ["files"] = new JsonArray()
            }),
            ("library-invalid-id", root => root["libraries"]!.AsObject()["Malformed Package/1.0.0"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed Package/1.0.0", ["files"] = new JsonArray()
            }),
            ("library-invalid-version", root => root["libraries"]!.AsObject()["Malformed/1.x"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed/1.x", ["files"] = new JsonArray()
            }),
            ("target-no-slash", root => root["targets"]!["net8.0"]!.AsObject()["MalformedPackage"] = new JsonObject()),
            ("target-empty-id", root => root["targets"]!["net8.0"]!.AsObject()["/1.0.0"] = new JsonObject()),
            ("target-empty-version", root => root["targets"]!["net8.0"]!.AsObject()["MalformedPackage/"] = new JsonObject()),
            ("target-extra-slash", root => root["targets"]!["net8.0"]!.AsObject()["Malformed/1.0.0/extra"] = new JsonObject())
            };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
        }
        finally
        {
            File.Delete(path);
        }

    }
}
