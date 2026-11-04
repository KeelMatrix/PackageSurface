using System.Text.Json;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CapabilityRestoreFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreStructureTests
{
    [Fact]
    public void StructuralDuplicatePropertiesFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The structural-duplicate baseline could not be created.");

        var source = File.ReadAllText(assets);
        var root = JsonNode.Parse(source)!.AsObject();






        var extensionPath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-explicit-extensions.assets.json");
        var extensionDocument = JsonNode.Parse(source)!.AsObject();
        extensionDocument["x-root"] = "ignored";
        extensionDocument["project"]!["x-project"] = "ignored";
        extensionDocument["project"]!["restore"]!["x-restore"] = "ignored";
        extensionDocument["project"]!["frameworks"]!["net8.0"]!["x-framework"] = "ignored";
        extensionDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["x-dependency"] = "ignored";
        extensionDocument["libraries"]!["XmlPackage/1.0.0"]!["x-library"] = "ignored";
        extensionDocument["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["x-target-package"] = "ignored";
        extensionDocument["packageFolders"]!.AsObject().First().Value!["x-package-folder"] = "ignored";
        File.WriteAllText(extensionPath, extensionDocument.ToJsonString());
        try
        {
            var extensionResult = ResolvedGraphClassifier.Analyze(extensionPath, scratch, strictContent: false);
            Assert.True(extensionResult.IsComplete, $"The explicit x- extension namespace was rejected: {string.Join(" | ", extensionResult.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(extensionPath);
        }

        var generatedProps = Path.Combine(Path.GetDirectoryName(assets)!, "Test.csproj.nuget.g.props");
        var generatedPropsBefore = File.ReadAllBytes(generatedProps);
        try
        {
            foreach (var (name, xml) in new[]
            {
            ("generated-import-project-alias", "<Project><Import project=\"unused.props\" /></Project>"),
            ("generated-import-condition-alias", "<Project condition=\"'$(TargetFramework)' == 'net8.0'\" />"),
            ("generated-import-condition-near-spelling", "<Project><Import Project=\"unused.props\" Conditon=\"'$(TargetFramework)' == 'net8.0'\" /></Project>"),
            ("generated-import-case-duplicate", "<Project><Import Project=\"unused.props\" project=\"unused.props\" /></Project>"),
            ("generated-import-element-alias", "<Project><import Project=\"unused.props\" /></Project>"),
            ("generated-import-element-near-spelling", "<Project><Improt Project=\"unused.props\" /></Project>")
        })
            {
                File.WriteAllText(generatedProps, xml);
                AssertRestoreIdentityFailure(assets, scratch, baseline, name);
            }

            File.WriteAllText(generatedProps, "<Project xmlns:x=\"urn:keelmatrix:packagesurface:extension\" x:marker=\"ignored\" />");
            var xmlExtensionResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
            Assert.True(xmlExtensionResult.IsComplete, $"The explicit XML extension namespace was rejected: {string.Join(" | ", xmlExtensionResult.IncompleteReasons)}");
        }
        finally
        {
            File.WriteAllBytes(generatedProps, generatedPropsBefore);
        }

        var contentAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata.assets.json");
        var contentBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata-baseline.json");
        WriteContentAssets(contentAssets, Path.Combine(scratch, "content-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
        Assert.True(CaptureCommand("baseline", contentAssets, "--output", contentBaseline, "--no-telemetry").ExitCode == 0,
            "The content metadata baseline could not be created.");
        try
        {
            var contentDocument = JsonNode.Parse(File.ReadAllText(contentAssets))!.AsObject();
            var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
            RenameJsonProperty(contentMetadata, "codeLanguage", "CODELANGUAGE");
            File.WriteAllText(contentAssets, contentDocument.ToJsonString());
            AssertRestoreIdentityFailure(contentAssets, scratch, contentBaseline, "content-file-metadata-alias");
        }
        finally
        {
            if (File.Exists(contentAssets)) File.Delete(contentAssets);
            if (File.Exists(contentBaseline)) File.Delete(contentBaseline);
        }

        var contentUnknownAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-unknown-spelling.assets.json");
        var contentUnknownBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-unknown-spelling-baseline.json");
        WriteContentAssets(contentUnknownAssets, Path.Combine(scratch, "content-unknown-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
        Assert.True(CaptureCommand("baseline", contentUnknownAssets, "--output", contentUnknownBaseline, "--no-telemetry").ExitCode == 0,
            "The content unknown-spelling baseline could not be created.");
        try
        {
            var contentDocument = JsonNode.Parse(File.ReadAllText(contentUnknownAssets))!.AsObject();
            var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
            contentMetadata.Remove("codeLanguage");
            contentMetadata["codeLanguge"] = "cs";
            File.WriteAllText(contentUnknownAssets, contentDocument.ToJsonString());
            AssertRestoreIdentityFailure(contentUnknownAssets, scratch, contentUnknownBaseline, "content-file-metadata-near-spelling");
        }
        finally
        {
            if (File.Exists(contentUnknownAssets)) File.Delete(contentUnknownAssets);
            if (File.Exists(contentUnknownBaseline)) File.Delete(contentUnknownBaseline);
        }

        var contentExtensionAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-explicit-extension.assets.json");
        WriteContentAssets(contentExtensionAssets, Path.Combine(scratch, "content-extension-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
        try
        {
            var contentDocument = JsonNode.Parse(File.ReadAllText(contentExtensionAssets))!.AsObject();
            var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
            contentMetadata["x-content"] = "ignored";
            File.WriteAllText(contentExtensionAssets, contentDocument.ToJsonString());
            var contentResult = ResolvedGraphClassifier.Analyze(contentExtensionAssets, scratch, strictContent: false);
            Assert.True(contentResult.IsComplete, $"The explicit x- content extension was rejected: {string.Join(" | ", contentResult.IncompleteReasons)}");
        }
        finally
        {
            if (File.Exists(contentExtensionAssets)) File.Delete(contentExtensionAssets);
        }

        File.Delete(baseline);
    }


    [Theory]
    [InlineData("root-version-exact")]
    [InlineData("root-version-case")]
    [InlineData("root-targets-exact")]
    [InlineData("root-libraries-case")]
    [InlineData("root-package-folders-exact")]
    [InlineData("project-exact")]
    [InlineData("project-restore-case")]
    [InlineData("project-path-exact")]
    [InlineData("framework-map-case")]
    [InlineData("library-type-case")]
    [InlineData("library-path-exact")]
    [InlineData("library-files-case")]
    [InlineData("direct-dependency-version-exact")]
    [InlineData("target-package-build-case")]
    public void DuplicateJsonPropertiesFailClosed(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The structural-duplicate baseline could not be created.");

        var source = File.ReadAllText(assets);
        var root = JsonNode.Parse(source)!.AsObject();
        var cases = new List<(string Name, string Json)>
    {
        ("root-version-exact", InsertExactPropertyDuplicate(source, "version", "3")),
        ("root-version-case", InsertCaseVariantPropertyDuplicate(source, "version", "3")),
        ("root-targets-exact", InsertRootPropertyDuplicate(source, root, "targets")),
        ("root-libraries-case", InsertCaseVariantRootPropertyDuplicate(source, root, "libraries")),
        ("root-package-folders-exact", InsertRootPropertyDuplicate(source, root, "packageFolders")),
        ("project-exact", InsertRootPropertyDuplicate(source, root, "project")),
        ("project-restore-case", InsertCaseVariantPropertyDuplicate(source, "restore", root["project"]!["restore"]!.ToJsonString())),
        ("project-path-exact", InsertExactPropertyDuplicate(source, "projectPath", JsonSerializer.Serialize(root["project"]!["restore"]!["projectPath"]!.GetValue<string>()))),
        ("framework-map-case", InsertCaseVariantPropertyDuplicate(source, "frameworks", root["project"]!["frameworks"]!.ToJsonString())),
        ("library-type-case", InsertCaseVariantPropertyDuplicate(source, "type", JsonSerializer.Serialize("package"))),
        ("library-path-exact", InsertExactPropertyDuplicate(source, "path", JsonSerializer.Serialize("XmlPackage/1.0.0"))),
        ("library-files-case", InsertCaseVariantPropertyDuplicate(source, "files", root["libraries"]!["XmlPackage/1.0.0"]!["files"]!.ToJsonString()))
    };

        var dependency = root["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
        var dependencyVersion = dependency["version"]!.GetValue<string>();
        cases.Add(("direct-dependency-version-exact", InsertExactPropertyDuplicate(source, "version", JsonSerializer.Serialize(dependencyVersion), source.IndexOf("\"XmlPackage\":{", StringComparison.Ordinal))));

        var targetWithBuild = root.DeepClone()!.AsObject();
        targetWithBuild["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["build"] = new JsonObject
        {
            ["build/structure.targets"] = new JsonObject()
        };
        var targetJson = targetWithBuild.ToJsonString();
        cases.Add(("target-package-build-case", InsertCaseVariantPropertyDuplicate(targetJson, "build", "{\"build/structure.targets\":{}}")));


        var (name, json) = cases.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-" + name + ".assets.json");
        File.WriteAllText(path, json);
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }

    }

    [Theory]
    [InlineData("root-version-alias")]
    [InlineData("root-targets-alias")]
    [InlineData("root-libraries-alias")]
    [InlineData("root-package-folders-alias")]
    [InlineData("root-project-alias")]
    [InlineData("root-dependency-groups-alias")]
    [InlineData("project-restore-alias")]
    [InlineData("project-frameworks-alias")]
    [InlineData("project-path-alias")]
    [InlineData("restore-compiler-alias")]
    [InlineData("framework-framework-alias")]
    [InlineData("framework-target-alias-alias")]
    [InlineData("framework-dependencies-alias")]
    [InlineData("dependency-version-alias")]
    [InlineData("dependency-target-alias")]
    [InlineData("dependency-include-alias")]
    [InlineData("dependency-exclude-alias")]
    [InlineData("dependency-optional-alias")]
    [InlineData("library-type-alias")]
    [InlineData("library-path-alias")]
    [InlineData("library-files-alias")]
    [InlineData("target-package-group-alias")]
    public void CaseVariantJsonPropertiesFailClosed(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The structural-duplicate baseline could not be created.");

        var source = File.ReadAllText(assets);
        var root = JsonNode.Parse(source)!.AsObject();

        var matrix = new List<(string Name, Action<JsonObject> Mutate)>
    {
        ("root-version-alias", value => RenameJsonProperty(value, "version", "Version")),
        ("root-targets-alias", value => RenameJsonProperty(value, "targets", "TARGETS")),
        ("root-libraries-alias", value => RenameJsonProperty(value, "libraries", "Libraries")),
        ("root-package-folders-alias", value => RenameJsonProperty(value, "packageFolders", "PACKAGEFOLDERS")),
        ("root-project-alias", value => RenameJsonProperty(value, "project", "Project")),
        ("root-dependency-groups-alias", value =>
        {
            value["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
            RenameJsonProperty(value, "projectFileDependencyGroups", "PROJECTFILEDEPENDENCYGROUPS");
        }),
        ("project-restore-alias", value => RenameJsonProperty(value["project"]!.AsObject(), "restore", "RESTORE")),
        ("project-frameworks-alias", value => RenameJsonProperty(value["project"]!.AsObject(), "frameworks", "FRAMEWORKS")),
        ("project-path-alias", value => RenameJsonProperty(value["project"]!["restore"]!.AsObject(), "projectPath", "PROJECTPATH")),
        ("restore-compiler-alias", value =>
        {
            var restore = value["project"]!["restore"]!.AsObject();
            restore["compilerApiVersion"] = "4.0";
            RenameJsonProperty(restore, "compilerApiVersion", "COMPILERAPIVERSION");
        }),
        ("framework-framework-alias", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["framework"] = "net8.0";
            RenameJsonProperty(framework, "framework", "FRAMEWORK");
        }),
        ("framework-target-alias-alias", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["targetAlias"] = "net8.0";
            RenameJsonProperty(framework, "targetAlias", "TARGETALIAS");
        }),
        ("framework-dependencies-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!.AsObject(), "dependencies", "DEPENDENCIES")),
        ("dependency-version-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject(), "version", "VERSION")),
        ("dependency-target-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject(), "target", "TARGET")),
        ("dependency-include-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["include"] = "all";
            RenameJsonProperty(dependency, "include", "INCLUDE");
        }),
        ("dependency-exclude-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exclude"] = "none";
            RenameJsonProperty(dependency, "exclude", "EXCLUDE");
        }),
        ("dependency-optional-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["privateAssets"] = "all";
            RenameJsonProperty(dependency, "privateAssets", "PRIVATEASSETS");
        }),
        ("library-type-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "type", "TYPE")),
        ("library-path-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "path", "PATH")),
        ("library-files-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "files", "FILES")),
        ("target-package-group-alias", value =>
        {
            var package = value["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!.AsObject();
            package["build"] = new JsonObject { ["build/structure.targets"] = new JsonObject() };
            RenameJsonProperty(package, "build", "BUILD");
        })
    };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-spelling-" + name + ".assets.json");
        var malformed = JsonNode.Parse(source)!.AsObject();
        mutate(malformed);
        File.WriteAllText(path, malformed.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }

    }

    [Theory]
    [InlineData("dependency-exclude-near-spelling")]
    [InlineData("dependency-exclude-near-spelling-conflicting")]
    [InlineData("framework-dependencies-near-spelling")]
    [InlineData("library-path-near-spelling")]
    [InlineData("target-package-dependencies-near-spelling")]
    public void UnknownJsonPropertiesFailClosed(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The structural-duplicate baseline could not be created.");

        var source = File.ReadAllText(assets);
        var root = JsonNode.Parse(source)!.AsObject();

        var matrix = new List<(string Name, Action<JsonObject> Mutate)>
    {
        ("dependency-exclude-near-spelling", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exlcude"] = "all";
        }),
        ("dependency-exclude-near-spelling-conflicting", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exclude"] = "none";
            dependency["exlcude"] = "all";
        }),
        ("framework-dependencies-near-spelling", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["dependencis"] = framework["dependencies"]!.DeepClone();
        }),
        ("library-path-near-spelling", value =>
            value["libraries"]!["XmlPackage/1.0.0"]!["pathh"] = "XmlPackage/1.0.0"),
        ("target-package-dependencies-near-spelling", value =>
            value["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencis"] = new JsonObject())
    };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-unknown-spelling-" + name + ".assets.json");
        var malformed = JsonNode.Parse(source)!.AsObject();
        mutate(malformed);
        File.WriteAllText(path, malformed.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }

    }
}
