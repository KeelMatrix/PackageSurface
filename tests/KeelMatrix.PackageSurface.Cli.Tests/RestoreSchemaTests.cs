using System.Text.Json;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreSchemaTests
{
    [Fact]
    public void MalformedAssetsShapesFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var projectReferencePath = Path.Combine(Path.GetDirectoryName(assets)!, "project-reference.assets.json");
        var projectReference = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        projectReference["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["framework"] = ".NETCoreApp,Version=v8.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = "1.0.0" },
            ["compile"] = new JsonObject { ["bin/placeholder/Referenced.Project.dll"] = new JsonObject() }
        };
        projectReference["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["path"] = "../Referenced.Project",
            ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
        };
        File.WriteAllText(projectReferencePath, projectReference.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(projectReferencePath, scratch, strictContent: false);
            Assert.True(result.IsComplete, $"A normal project-reference target with scalar framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(projectReferencePath);
        }

        var malformedProjectDependencyPath = Path.Combine(Path.GetDirectoryName(assets)!, "project-reference-malformed-dependency.assets.json");
        var malformedProjectDependency = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        malformedProjectDependency["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["framework"] = ".NETCoreApp,Version=v8.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = 7 }
        };
        malformedProjectDependency["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["path"] = "../Referenced.Project",
            ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
        };
        File.WriteAllText(malformedProjectDependencyPath, malformedProjectDependency.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(malformedProjectDependencyPath, scratch, strictContent: false);
            Assert.True(!result.IsComplete, "A project-node dependency with a scalar value was accepted.");
        }
        finally
        {
            File.Delete(malformedProjectDependencyPath);
        }

        var arrayMetadataPath = Path.Combine(Path.GetDirectoryName(assets)!, "array-metadata.assets.json");
        var arrayMetadata = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        arrayMetadata["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonArray("System.Xml");
        arrayMetadata["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray("Microsoft.NETCore.App");
        File.WriteAllText(arrayMetadataPath, arrayMetadata.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(arrayMetadataPath, scratch, strictContent: false);
            Assert.True(result.IsComplete, $"Legitimate array-valued framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(arrayMetadataPath);
        }





        var cliAssets = Path.Combine(Path.GetDirectoryName(assets)!, "packageFolders-cli.assets.json");
        var cliRoot = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        cliRoot["packageFolders"] = new JsonArray();
        File.WriteAllText(cliAssets, cliRoot.ToJsonString());
        var cliExitCode = CommandLine.Run(new[] { "scan", cliAssets, "--no-telemetry" });
        File.Delete(cliAssets);
        Assert.True(cliExitCode == 2, $"Malformed assets CLI invocation returned {cliExitCode}, expected 2.");
    }


    [Theory]
    [InlineData("framework-assemblies-number")]
    [InlineData("framework-references-object-element")]
    [InlineData("framework-references-null-element")]
    [InlineData("framework-assemblies-object")]
    [InlineData("framework-reference-case-duplicate")]
    [InlineData("target-dependency-number")]
    [InlineData("target-dependency-null")]
    public void MalformedTargetMetadataFailsClosed(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var matrix = new (string Name, Action<JsonObject> Mutate)[]
        {
        ("framework-assemblies-number", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonArray(1)),
        ("framework-references-object-element", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray(new JsonObject { ["name"] = "Microsoft.NETCore.App" })),
        ("framework-references-null-element", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray { null }),
        ("framework-assemblies-object", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonObject()),
        ("framework-reference-case-duplicate", root =>
        {
            var package = root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!.AsObject();
            package["frameworkReferences"] = new JsonArray("Microsoft.NETCore.App");
            package["FRAMEWORKREFERENCES"] = new JsonArray("Microsoft.NETCore.App");
        }),
        ("target-dependency-number", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = 7 }),
        ("target-dependency-null", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = null })
        };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        mutate(root);
        File.WriteAllText(path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Assert.True(!result.IsComplete && result.IncompleteReasons.Count > 0, $"Malformed typed metadata '{name}' was accepted.");
        }
        finally
        {
            File.Delete(path);
        }

    }

    [Theory]
    [InlineData("packageFolders-array")]
    [InlineData("targets-array")]
    [InlineData("target-array")]
    [InlineData("libraries-array")]
    [InlineData("frameworks-array")]
    [InlineData("framework-dependencies-scalar")]
    [InlineData("package-path-number")]
    [InlineData("missing-declared-target")]
    [InlineData("empty-target-with-declarations")]
    [InlineData("malformed-content-files")]
    [InlineData("missing-target-file-inventory")]
    [InlineData("ambiguous-library-identity")]
    public void MalformedDocumentShapesFailClosed(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var matrix = new (string Name, Action<JsonObject> Mutate)[]
        {
        ("packageFolders-array", root => root["packageFolders"] = new JsonArray()),
        ("targets-array", root => root["targets"] = new JsonArray()),
        ("target-array", root => root["targets"]!["net8.0"] = new JsonArray()),
        ("libraries-array", root => root["libraries"] = new JsonArray()),
        ("frameworks-array", root => root["project"]!["frameworks"] = new JsonArray()),
        ("framework-dependencies-scalar", root => root["project"]!["frameworks"]!["net8.0"]!["dependencies"] = 7),
        ("package-path-number", root => root["libraries"]!["XmlPackage/1.0.0"]!["path"] = 7),
        ("missing-declared-target", root => root["targets"]!.AsObject().Remove("net8.0")),
        ("empty-target-with-declarations", root =>
        {
            root["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = new JsonObject() };
            root["targets"]!["net8.0"] = new JsonObject();
        }),
        ("malformed-content-files", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["contentFiles"] = new JsonArray()),
        ("missing-target-file-inventory", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["build"] = new JsonObject { ["build/unrepresented.targets"] = new JsonObject() }),
        ("ambiguous-library-identity", root => root["libraries"]!.AsObject()["xmlpackage/1.0.0"] = root["libraries"]!["XmlPackage/1.0.0"]!.DeepClone())
        };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        mutate(root);
        File.WriteAllText(path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Assert.True(!result.IsComplete, $"Malformed assets shape '{name}' was not fail-closed.");
        }
        finally
        {
            File.Delete(path);
        }

    }
    [Fact]
    public void PackageFolderMetadataIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var cache = Path.Combine(scratch, "cache");


        var fallbackPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-fallback.assets.json");
        var fallback = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        fallback["packageFolders"] = new JsonObject
        {
            [Path.Combine(scratch, "fallback-cache")] = new JsonObject(),
            [Path.GetFullPath(cache).Replace(Path.DirectorySeparatorChar, '/')] = new JsonObject()
        };
        File.WriteAllText(fallbackPath, fallback.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(fallbackPath, scratch, strictContent: false);
            Assert.True(result.IsComplete && result.ResolvedPackageCount == 1,
                $"Valid absolute fallback package folders were rejected: {string.Join(" | ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(fallbackPath);
        }

        var absoluteFolderPath = Path.Combine(scratch, "PRIVATE_CACHE_MARKER");
        var mixedPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-mixed-invalid.assets.json");
        var mixed = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        mixed["packageFolders"] = new JsonObject
        {
            [Path.GetFullPath(cache)] = new JsonObject(),
            [absoluteFolderPath] = null
        };
        File.WriteAllText(mixedPath, mixed.ToJsonString());
        try
        {
            foreach (var format in new[] { "text", "json", "sarif" })
            {
                var result = CaptureCommand("scan", mixedPath, "--format", format, "--no-telemetry");
                Assert.True(result.ExitCode == 2 && !result.Output.Contains(absoluteFolderPath, StringComparison.OrdinalIgnoreCase),
                    $"Mixed valid/invalid packageFolders leaked its absolute cache path in {format} output.");
                AssertNoSuccessfulSurfaceOutput(result.Output, format, "mixed invalid packageFolders");
            }
        }
        finally
        {
            File.Delete(mixedPath);
        }

        foreach (var marker in new[]
        {
        "/home/PRIVATE_CACHE_MARKER",
        "C:\\PRIVATE_CACHE_MARKER",
        "ARBITRARY_PRIVATE_MEMBER_MARKER"
    })
        {
            var structuralPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-structural-marker.assets.json");
            var original = File.ReadAllText(assets);
            var property = JsonSerializer.Serialize(marker);
            File.WriteAllText(structuralPath, "{" + property + ":true," + property + ":false," + original.TrimStart()[1..]);
            try
            {
                foreach (var format in new[] { "text", "json", "sarif" })
                {
                    var result = CaptureCommand("scan", structuralPath, "--format", format, "--no-telemetry");
                    Assert.True(result.ExitCode == 2 && !result.Output.Contains(marker, StringComparison.OrdinalIgnoreCase),
                        $"Duplicate/unknown path-marked restore members leaked in {format} output.");
                    AssertNoSuccessfulSurfaceOutput(result.Output, format, "path-marked restore members");
                }
            }
            finally
            {
                File.Delete(structuralPath);
            }
        }
    }


    [Theory]
    [InlineData("empty")]
    [InlineData("relative")]
    [InlineData("whitespace")]
    [InlineData("null-value")]
    [InlineData("array-value")]
    public void MalformedPackageFoldersFailClosed(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var cache = Path.Combine(scratch, "cache");

        var matrix = new (string Name, Func<JsonObject, JsonObject> Mutate)[]
        {
        ("empty", _ => new JsonObject()),
        ("relative", _ => new JsonObject { ["relative-cache"] = new JsonObject() }),
        ("whitespace", _ => new JsonObject { ["   "] = new JsonObject() }),
        ("null-value", _ => new JsonObject { [cache] = null }),
        ("array-value", _ => new JsonObject { [cache] = new JsonArray() })
        };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["packageFolders"] = mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Assert.True(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
                $"Malformed packageFolders shape '{name}' was accepted.");
        }
        finally
        {
            File.Delete(path);
        }

    }
    [Fact]
    public void AssetsFormatIsValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var v4Path = Path.Combine(Path.GetDirectoryName(assets)!, "format4.assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        root["version"] = 4;
        root["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
        var project = root["project"]!.AsObject();
        var projectFramework = project["frameworks"]!["net8.0"]!.AsObject();
        projectFramework["framework"] = "net8.0";
        projectFramework["targetAlias"] = "net8.0";
        project["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        File.WriteAllText(v4Path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(v4Path, scratch, strictContent: false);
            Assert.True(result.IsComplete && result.ResolvedPackageCount == 1, "A coherent assets format 4 graph was not classified.");


        }
        finally
        {
            File.Delete(v4Path);
        }
    }


    [Theory]
    [InlineData("format4-missing-framework")]
    [InlineData("format4-missing-dependency-groups")]
    [InlineData("format4-mismatched-restore-framework")]
    [InlineData("format4-dependency-object-element")]
    [InlineData("format4-dependency-null-element")]
    [InlineData("format4-dependency-duplicate")]
    [InlineData("format4-dependency-case-duplicate")]
    public void FormatFourMetadataMustBeComplete(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

        var v4Path = Path.Combine(Path.GetDirectoryName(assets)!, "format4.assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        root["version"] = 4;
        root["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
        var project = root["project"]!.AsObject();
        var projectFramework = project["frameworks"]!["net8.0"]!.AsObject();
        projectFramework["framework"] = "net8.0";
        projectFramework["targetAlias"] = "net8.0";
        project["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        File.WriteAllText(v4Path, root.ToJsonString());

        var result = ResolvedGraphClassifier.Analyze(v4Path, scratch, strictContent: false);
        Assert.True(result.IsComplete && result.ResolvedPackageCount == 1, "A coherent assets format 4 graph was not classified.");


        var matrix = new (string Name, Action<JsonObject> Mutate)[]
            {
            ("format4-missing-framework", value => value["project"]!["frameworks"]!["net8.0"]!.AsObject().Remove("framework")),
            ("format4-missing-dependency-groups", value => value.Remove("projectFileDependencyGroups")),
            ("format4-mismatched-restore-framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("format4-dependency-object-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray(new JsonObject())),
            ("format4-dependency-null-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray { null }),
            ("format4-dependency-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "XmlPackage >= 1.0.0")),
            ("format4-dependency-case-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "xmlpackage >= 1.0.0"))
            };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
        var malformed = JsonNode.Parse(File.ReadAllText(v4Path))!.AsObject();
        mutate(malformed);
        File.WriteAllText(path, malformed.ToJsonString());
        try
        {
            var malformedResult = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Assert.True(!malformedResult.IsComplete, $"Malformed assets format 4 shape '{name}' was not fail-closed.");
            if (name == "format4-dependency-case-duplicate")
            {
                var validBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "format4-case-baseline.json");
                try
                {
                    var coherentBaseline = CaptureCommand("baseline", v4Path, "--output", validBaseline, "--no-telemetry");
                    Assert.True(coherentBaseline.ExitCode == 0,
                        $"The coherent format 4 dependency-group fixture could not create its baseline: exit={coherentBaseline.ExitCode}; output={coherentBaseline.Output}");
                    AssertFormat4DependencyGroupFailure(path, validBaseline);
                }
                finally
                {
                    if (File.Exists(validBaseline)) File.Delete(validBaseline);
                }
            }
        }
        finally
        {
            File.Delete(path);
        }

    }
    [Fact]
    public void CapabilityGroupsAreValidated()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;



        var mixedRoot = Path.Combine(scratch, "capability-group-mixed-known-unknown");
        var mixedObj = Path.Combine(mixedRoot, "obj");
        var mixedCache = Path.Combine(mixedRoot, "cache");
        Directory.CreateDirectory(mixedObj);
        var mixedAssets = Path.Combine(mixedObj, "project.assets.json");
        WriteAssets(mixedAssets, mixedCache, "MixedGroup.Package", new List<string> { "build/Mixed.targets" }, createFiles: true);
        var mixedDocument = JsonNode.Parse(File.ReadAllText(mixedAssets))!.AsObject();
        var mixedPackage = mixedDocument["targets"]!["net8.0"]!["MixedGroup.Package/1.0.0"]!.AsObject();
        mixedPackage["build"] = new JsonObject { ["build/Mixed.targets"] = new JsonObject() };
        mixedPackage["Build"] = new JsonObject { ["build/Mixed.targets"] = new JsonObject() };
        File.WriteAllText(mixedAssets, mixedDocument.ToJsonString());
        var mixedResult = ResolvedGraphClassifier.Analyze(mixedAssets, mixedRoot, strictContent: false);
        Assert.True(!mixedResult.IsComplete && mixedResult.Entries.Count == 0 && mixedResult.IncompleteReasons.Count > 0,
            "A mixed canonical and unsupported target asset-group set was accepted as a clean or partial result.");

        var transitiveRoot = Path.Combine(scratch, "capability-group-transitive");
        var transitiveObj = Path.Combine(transitiveRoot, "obj");
        var transitiveCache = Path.Combine(transitiveRoot, "cache");
        Directory.CreateDirectory(transitiveObj);
        var transitiveAssets = Path.Combine(transitiveObj, "project.assets.json");
        WriteAssets(transitiveAssets, transitiveCache, "RootGroup.Package", Array.Empty<string>(), createFiles: true);
        Directory.CreateDirectory(Path.Combine(transitiveCache, "TransitiveGroup.Package", "1.0.0", "build"));
        File.WriteAllText(Path.Combine(transitiveCache, "TransitiveGroup.Package", "1.0.0", "build", "Case.targets"), "fixture");
        var transitiveDocument = JsonNode.Parse(File.ReadAllText(transitiveAssets))!.AsObject();
        transitiveDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonObject
        {
            ["RootGroup.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
        };
        transitiveDocument["targets"]!["net8.0"]!["RootGroup.Package/1.0.0"]!["dependencies"] = new JsonObject
        {
            ["TransitiveGroup.Package"] = "[1.0.0, )"
        };
        transitiveDocument["targets"]!["net8.0"]!["TransitiveGroup.Package/1.0.0"] = new JsonObject
        {
            ["type"] = "package",
            ["Build"] = new JsonObject { ["build/Case.targets"] = new JsonObject() }
        };
        transitiveDocument["libraries"]!["TransitiveGroup.Package/1.0.0"] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = "TransitiveGroup.Package/1.0.0",
            ["files"] = new JsonArray("build/Case.targets")
        };
        transitiveDocument["targets"]!.AsObject()["net8.0/win-x64"] = transitiveDocument["targets"]!["net8.0"]!.DeepClone();
        File.WriteAllText(transitiveAssets, transitiveDocument.ToJsonString());
        var transitiveResult = ResolvedGraphClassifier.Analyze(transitiveAssets, transitiveRoot, strictContent: false);
        Assert.True(!transitiveResult.IsComplete && transitiveResult.Entries.Count == 0 && transitiveResult.IncompleteReasons.Count > 0,
            "An unsupported target asset group on a transitive RID package was accepted as a clean or partial result.");
    }


    [Theory]
    [InlineData("case-variant-build")]
    [InlineData("case-variant-build-transitive")]
    [InlineData("case-variant-build-multi-targeting")]
    [InlineData("unknown-capability-group")]
    [InlineData("unknown-empty-group")]
    [InlineData("null-capability-group")]
    public void UnsupportedCapabilityGroupsFailClosed(string caseName)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var matrix = new (string Name, string Group, bool Empty)[]
        {
        ("case-variant-build", "Build", false),
        ("case-variant-build-transitive", "BUILDTRANSITIVE", false),
        ("case-variant-build-multi-targeting", "BUILDMULTITARGETING", false),
        ("unknown-capability-group", "unknownCapabilityGroup", false),
        ("unknown-empty-group", "unknownEmptyGroup", true),
        ("null-capability-group", "unknownNullGroup", true)
        };
        var (name, group, empty) = matrix.Single(value => value.Item1 == caseName);

        var root = Path.Combine(scratch, "capability-group-" + name);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        const string assetPath = "build/Case.targets";
        WriteAssets(assets, cache, "CaseGroup.Package", new[] { assetPath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var targetPackage = document["targets"]!["net8.0"]!["CaseGroup.Package/1.0.0"]!.AsObject();
        targetPackage[group] = empty && group != "unknownNullGroup"
            ? new JsonObject()
            : empty
                ? null
                : new JsonObject { [assetPath] = new JsonObject() };
        document["targets"]!.AsObject()["net8.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone();
        File.WriteAllText(assets, document.ToJsonString());

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
            $"Unsupported target asset group '{group}' was accepted as a clean or partial result.");

    }
}
