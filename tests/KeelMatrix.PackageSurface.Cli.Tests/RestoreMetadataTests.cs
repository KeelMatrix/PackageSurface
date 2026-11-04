using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreMetadataTests
{
    [Fact]
    public void OptionalRestoreMetadataIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var format3Path = Path.Combine(scratch, "restore-optional-format3.assets.json");
        var format3Baseline = Path.Combine(scratch, "restore-optional-format3-baseline.json");
        var format3 = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var restore = format3["project"]!["restore"]!.AsObject();
        restore["fallbackFolders"] = new JsonArray(@"C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages");
        restore["SdkAnalysisLevel"] = "10.0.400";
        format3["project"]!["frameworks"]!["net8.0"]!["downloadDependencies"] = new JsonArray
    {
        new JsonObject { ["name"] = "PackageDownload", ["version"] = "[1.0.0, )" }
    };
        File.WriteAllText(format3Path, format3.ToJsonString());
        WriteGeneratedImportEvidence(format3Path);

        try
        {
            AssertValidRestoreMetadata(format3Path, scratch, format3Baseline, "format 3 restore metadata");

            var format4Path = Path.Combine(scratch, "restore-optional-format4.assets.json");
            var format4Baseline = Path.Combine(scratch, "restore-optional-format4-baseline.json");
            var format4 = JsonNode.Parse(File.ReadAllText(format3Path))!.AsObject();
            format4["version"] = 4;
            format4["project"]!["frameworks"]!["net8.0"]!["framework"] = "net8.0";
            format4["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net8.0";
            format4["project"]!["restore"]!["frameworks"] = new JsonObject
            {
                ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
            };
            format4["projectFileDependencyGroups"] = new JsonObject
            {
                ["net8.0"] = new JsonArray()
            };
            File.WriteAllText(format4Path, format4.ToJsonString());
            WriteGeneratedImportEvidence(format4Path);
            try
            {
                AssertValidRestoreMetadata(format4Path, scratch, format4Baseline, "format 4 restore metadata");
            }
            finally
            {
                if (File.Exists(format4Path)) File.Delete(format4Path);
                if (File.Exists(format4Baseline)) File.Delete(format4Baseline);
                foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
                {
                    var generatedPath = Path.Combine(scratch, generated);
                    if (File.Exists(generatedPath)) File.Delete(generatedPath);
                }
            }

            foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
            {
            ("fallback-folders-type", value => value["project"]!["restore"]!["fallbackFolders"] = "not-an-array"),
            ("fallback-folders-item-type", value => value["project"]!["restore"]!["fallbackFolders"] = new JsonArray("valid", 42)),
            ("sdk-analysis-level-type", value => value["project"]!["restore"]!["SdkAnalysisLevel"] = 10)
            })
            {
                var invalidPath = Path.Combine(scratch, "restore-optional-invalid-" + name + ".assets.json");
                var invalid = JsonNode.Parse(File.ReadAllText(format3Path))!.AsObject();
                mutate(invalid);
                File.WriteAllText(invalidPath, invalid.ToJsonString());
                WriteGeneratedImportEvidence(invalidPath);
                try
                {
                    AssertRestoreIdentityFailure(invalidPath, scratch, format3Baseline, name);
                }
                finally
                {
                    if (File.Exists(invalidPath)) File.Delete(invalidPath);
                }
            }
        }
        finally
        {
            if (File.Exists(format3Path)) File.Delete(format3Path);
            if (File.Exists(format3Baseline)) File.Delete(format3Baseline);
            foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
            {
                var generatedPath = Path.Combine(scratch, generated);
                if (File.Exists(generatedPath)) File.Delete(generatedPath);
            }
        }
    }
    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void SupportedMetadataShapesRoundTrip(int format)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;
        var template = CreateValidRestoreShapeDocument(workspace.AssetsPath, format);
        var validPath = Path.Combine(scratch, "restore-shape-valid-format" + format + ".assets.json");
        var baseline = Path.Combine(scratch, "baseline.json");
        File.WriteAllText(validPath, template.ToJsonString());
        WriteGeneratedImportEvidence(validPath);
        AssertValidRestoreMetadata(validPath, scratch, baseline, "all valid restore metadata shapes (format " + format + ")");

    }

    [Theory]
    [InlineData("restore.centralPackageVersionsManagementEnabled", 3)]
    [InlineData("restore.centralPackageVersionsManagementEnabled", 4)]
    [InlineData("restore.configFilePaths", 3)]
    [InlineData("restore.configFilePaths", 4)]
    [InlineData("restore.crossTargeting", 3)]
    [InlineData("restore.crossTargeting", 4)]
    [InlineData("restore.frameworks", 3)]
    [InlineData("restore.frameworks", 4)]
    [InlineData("restore.originalTargetFrameworks", 3)]
    [InlineData("restore.originalTargetFrameworks", 4)]
    [InlineData("restore.outputPath", 3)]
    [InlineData("restore.outputPath", 4)]
    [InlineData("restore.packagesPath", 3)]
    [InlineData("restore.packagesPath", 4)]
    [InlineData("restore.projectName", 3)]
    [InlineData("restore.projectName", 4)]
    [InlineData("restore.projectPath", 3)]
    [InlineData("restore.projectPath", 4)]
    [InlineData("restore.projectStyle", 3)]
    [InlineData("restore.projectStyle", 4)]
    [InlineData("restore.projectUniqueName", 3)]
    [InlineData("restore.projectUniqueName", 4)]
    [InlineData("restore.restoreAuditProperties", 3)]
    [InlineData("restore.restoreAuditProperties", 4)]
    [InlineData("restore.fallbackFolders", 3)]
    [InlineData("restore.fallbackFolders", 4)]
    [InlineData("restore.sources", 3)]
    [InlineData("restore.sources", 4)]
    [InlineData("restore.warningProperties", 3)]
    [InlineData("restore.warningProperties", 4)]
    [InlineData("restore.compilerApiVersion", 3)]
    [InlineData("restore.compilerApiVersion", 4)]
    [InlineData("restore.SdkAnalysisLevel", 3)]
    [InlineData("restore.SdkAnalysisLevel", 4)]
    [InlineData("project.framework", 3)]
    [InlineData("project.framework", 4)]
    [InlineData("project.targetAlias", 3)]
    [InlineData("project.targetAlias", 4)]
    [InlineData("project.dependencies", 3)]
    [InlineData("project.dependencies", 4)]
    [InlineData("project.dependencies.value", 3)]
    [InlineData("project.dependencies.value", 4)]
    [InlineData("project.assetTargetFallback", 3)]
    [InlineData("project.assetTargetFallback", 4)]
    [InlineData("project.centralPackageVersions", 3)]
    [InlineData("project.centralPackageVersions", 4)]
    [InlineData("project.centralPackageVersions.value", 3)]
    [InlineData("project.centralPackageVersions.value", 4)]
    [InlineData("project.frameworkReferences", 3)]
    [InlineData("project.frameworkReferences", 4)]
    [InlineData("project.frameworkReferences.value", 3)]
    [InlineData("project.frameworkReferences.value", 4)]
    [InlineData("project.imports", 3)]
    [InlineData("project.imports", 4)]
    [InlineData("project.runtimeIdentifierGraphPath", 3)]
    [InlineData("project.runtimeIdentifierGraphPath", 4)]
    [InlineData("project.downloadDependencies", 3)]
    [InlineData("project.downloadDependencies", 4)]
    [InlineData("project.warn", 3)]
    [InlineData("project.warn", 4)]
    [InlineData("restore.framework.framework", 3)]
    [InlineData("restore.framework.framework", 4)]
    [InlineData("restore.framework.targetAlias", 3)]
    [InlineData("restore.framework.targetAlias", 4)]
    [InlineData("restore.framework.projectReferences", 3)]
    [InlineData("restore.framework.projectReferences", 4)]
    [InlineData("restore.framework.projectReferences.value", 3)]
    [InlineData("restore.framework.projectReferences.value", 4)]
    public void MalformedMetadataShapesFailClosed(string caseName, int format)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;
        var template = CreateValidRestoreShapeDocument(workspace.AssetsPath, format);
        var validPath = Path.Combine(scratch, "restore-shape-valid-format" + format + ".assets.json");
        var baseline = Path.Combine(scratch, "baseline.json");
        File.WriteAllText(validPath, template.ToJsonString());
        WriteGeneratedImportEvidence(validPath);
        AssertValidRestoreMetadata(validPath, scratch, baseline, "all valid restore metadata shapes (format " + format + ")");

        var mutations = new (string Name, Action<JsonObject> Mutate)[]
            {
            ("restore.centralPackageVersionsManagementEnabled", value => value["project"]!["restore"]!["centralPackageVersionsManagementEnabled"] = 42),
            ("restore.configFilePaths", value => value["project"]!["restore"]!["configFilePaths"] = "bad"),
            ("restore.crossTargeting", value => value["project"]!["restore"]!["crossTargeting"] = "bad"),
            ("restore.frameworks", value => value["project"]!["restore"]!["frameworks"] = new JsonArray()),
            ("restore.originalTargetFrameworks", value => value["project"]!["restore"]!["originalTargetFrameworks"] = new JsonArray(42)),
            ("restore.outputPath", value => value["project"]!["restore"]!["outputPath"] = 42),
            ("restore.packagesPath", value => value["project"]!["restore"]!["packagesPath"] = 42),
            ("restore.projectName", value => value["project"]!["restore"]!["projectName"] = 42),
            ("restore.projectPath", value => value["project"]!["restore"]!["projectPath"] = 42),
            ("restore.projectStyle", value => value["project"]!["restore"]!["projectStyle"] = 42),
            ("restore.projectUniqueName", value => value["project"]!["restore"]!["projectUniqueName"] = 42),
            ("restore.restoreAuditProperties", value => value["project"]!["restore"]!["restoreAuditProperties"] = "bad"),
            ("restore.fallbackFolders", value => value["project"]!["restore"]!["fallbackFolders"] = new JsonArray("valid", 42)),
            ("restore.sources", value => value["project"]!["restore"]!["sources"] = new JsonArray()),
            ("restore.warningProperties", value => value["project"]!["restore"]!["warningProperties"] = "bad"),
            ("restore.compilerApiVersion", value => value["project"]!["restore"]!["compilerApiVersion"] = 42),
            ("restore.SdkAnalysisLevel", value => value["project"]!["restore"]!["SdkAnalysisLevel"] = 10),
            ("project.framework", value => value["project"]!["frameworks"]!["net8.0"]!["framework"] = 42),
            ("project.targetAlias", value => value["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = 42),
            ("project.dependencies", value => value["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonArray()),
            ("project.dependencies.value", value => value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"] = new JsonObject { ["version"] = 42 }),
            ("project.assetTargetFallback", value => value["project"]!["frameworks"]!["net8.0"]!["assetTargetFallback"] = "bad"),
            ("project.centralPackageVersions", value => value["project"]!["frameworks"]!["net8.0"]!["centralPackageVersions"] = new JsonArray()),
            ("project.centralPackageVersions.value", value => value["project"]!["frameworks"]!["net8.0"]!["centralPackageVersions"]!["XmlPackage"] = 42),
            ("project.frameworkReferences", value => value["project"]!["frameworks"]!["net8.0"]!["frameworkReferences"] = new JsonArray()),
            ("project.frameworkReferences.value", value => value["project"]!["frameworks"]!["net8.0"]!["frameworkReferences"]!["Microsoft.NETCore.App"] = "bad"),
            ("project.imports", value => value["project"]!["frameworks"]!["net8.0"]!["imports"] = new JsonArray("net8.0", 42)),
            ("project.runtimeIdentifierGraphPath", value => value["project"]!["frameworks"]!["net8.0"]!["runtimeIdentifierGraphPath"] = 42),
            ("project.downloadDependencies", value => value["project"]!["frameworks"]!["net8.0"]!["downloadDependencies"] = new JsonArray(new JsonObject { ["name"] = "PackageDownload", ["version"] = 42 })),
            ("project.warn", value => value["project"]!["frameworks"]!["net8.0"]!["warn"] = "bad"),
            ("restore.framework.framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = 42),
            ("restore.framework.targetAlias", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["targetAlias"] = 42),
            ("restore.framework.projectReferences", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["projectReferences"] = new JsonArray()),
            ("restore.framework.projectReferences.value", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["projectReferences"]!["Project.csproj"] = "bad")
            };
        var (name, mutate) = mutations.Single(value => value.Name == caseName);

        var invalidPath = Path.Combine(scratch, "restore-shape-invalid-format" + format + "-" + name.Replace('.', '-') + ".assets.json");
        var invalid = template.DeepClone().AsObject();
        mutate(invalid);
        File.WriteAllText(invalidPath, invalid.ToJsonString());
        WriteGeneratedImportEvidence(invalidPath);
        try
        {
            AssertRestoreIdentityFailure(invalidPath, scratch, baseline, name + " (" + format + ")");
        }
        finally
        {
            if (File.Exists(invalidPath)) File.Delete(invalidPath);
            foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
            {
                var generatedPath = Path.Combine(scratch, generated);
                if (File.Exists(generatedPath)) File.Delete(generatedPath);
            }
        }

    }
}
