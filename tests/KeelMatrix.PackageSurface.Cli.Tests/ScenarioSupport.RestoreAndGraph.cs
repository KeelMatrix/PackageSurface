using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static partial class ScenarioSupport
{
    internal static void RunDiagnosticPathLeakRegression(string scratch)
    {
        var marker = "HomeCacheMarker-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(scratch, marker);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        Directory.CreateDirectory(cache);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, "MissingPackage", new List<string> { "build/missing.targets" });
        var absoluteMarker = Path.GetFullPath(root);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]!["MissingPackage/1.0.0"]!["build"] = new JsonObject { ["build/missing.targets"] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/MissingPackage/1.0.0/build/missing.targets\" Condition=\"'$(SENSITIVE_CONDITION_MARKER)' == 'enabled'\" /></Project>");
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            var priorError = Console.Error;
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                _ = CommandLine.Run(new[] { "scan", assets, "--format", format, "--no-telemetry" });
            }
            finally
            {
                Console.SetOut(priorOutput);
                Console.SetError(priorError);
            }

            Require(!output.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an absolute cache marker.");
            Require(!error.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an absolute cache marker.");
            Require(!output.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an unsupported condition marker.");
            Require(!error.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an unsupported condition marker.");
        }
    }

    internal static void RunAnalyzerExclusionRegression(string scratch, string cache, string obj)
    {
        var analyzerRelativePath = "analyzers/dotnet/cs/valid.dll";
        var directAssets = Path.Combine(obj, "analyzer-excluded-direct.assets.json");
        WriteAnalyzerAssets(directAssets, cache, true, analyzerRelativePath);
        var directResult = ResolvedGraphClassifier.Analyze(directAssets, scratch, strictContent: false);
        Require(directResult.IsComplete && directResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
            "Direct ExcludeAssets=analyzers was not honored.");

        var transitiveAssets = Path.Combine(obj, "analyzer-excluded-transitive.assets.json");
        WriteAnalyzerAssets(transitiveAssets, cache, false, analyzerRelativePath);
        var transitiveResult = ResolvedGraphClassifier.Analyze(transitiveAssets, scratch, strictContent: false);
        Require(transitiveResult.IsComplete && transitiveResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
            "Transitive ExcludeAssets=analyzers was not honored.");
    }

    internal static void RunMalformedAssetsShapeRegression(string assets, string scratch)
    {
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
            Require(result.IsComplete, $"A normal project-reference target with scalar framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
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
            Require(!result.IsComplete, "A project-node dependency with a scalar value was accepted.");
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
            Require(result.IsComplete, $"Legitimate array-valued framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(arrayMetadataPath);
        }

        foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
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
        })
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
            var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            mutate(root);
            File.WriteAllText(path, root.ToJsonString());
            try
            {
                var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                Require(!result.IsComplete && result.IncompleteReasons.Count > 0, $"Malformed typed metadata '{name}' was accepted.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        var malformedShapes = new (string Name, Action<JsonObject> Mutate)[]
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

        foreach (var (name, mutate) in malformedShapes)
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
            var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            mutate(root);
            File.WriteAllText(path, root.ToJsonString());
            try
            {
                var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                Require(!result.IsComplete, $"Malformed assets shape '{name}' was not fail-closed.");
            }
            finally
            {
                File.Delete(path);
            }
        }

        var cliAssets = Path.Combine(Path.GetDirectoryName(assets)!, "packageFolders-cli.assets.json");
        var cliRoot = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        cliRoot["packageFolders"] = new JsonArray();
        File.WriteAllText(cliAssets, cliRoot.ToJsonString());
        var cliExitCode = CommandLine.Run(new[] { "scan", cliAssets, "--no-telemetry" });
        File.Delete(cliAssets);
        Require(cliExitCode == 2, $"Malformed assets CLI invocation returned {cliExitCode}, expected 2.");
    }

    internal static void RunAssetsFormatRegression(string assets, string scratch)
    {
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
            Require(result.IsComplete && result.ResolvedPackageCount == 1, "A coherent assets format 4 graph was not classified.");

            var malformedV4 = new (string Name, Action<JsonObject> Mutate)[]
            {
            ("format4-missing-framework", value => value["project"]!["frameworks"]!["net8.0"]!.AsObject().Remove("framework")),
            ("format4-missing-dependency-groups", value => value.Remove("projectFileDependencyGroups")),
            ("format4-mismatched-restore-framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("format4-dependency-object-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray(new JsonObject())),
            ("format4-dependency-null-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray { null }),
            ("format4-dependency-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "XmlPackage >= 1.0.0")),
            ("format4-dependency-case-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "xmlpackage >= 1.0.0"))
            };

            foreach (var (name, mutate) in malformedV4)
            {
                var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
                var malformed = JsonNode.Parse(File.ReadAllText(v4Path))!.AsObject();
                mutate(malformed);
                File.WriteAllText(path, malformed.ToJsonString());
                try
                {
                    var malformedResult = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                    Require(!malformedResult.IsComplete, $"Malformed assets format 4 shape '{name}' was not fail-closed.");
                    if (name == "format4-dependency-case-duplicate")
                    {
                        var validBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "format4-case-baseline.json");
                        try
                        {
                            var coherentBaseline = CaptureCommand("baseline", v4Path, "--output", validBaseline, "--no-telemetry");
                            Require(coherentBaseline.ExitCode == 0,
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
        }
        finally
        {
            File.Delete(v4Path);
        }
    }

    internal static void RunPackageFoldersValidationRegression(string assets, string scratch)
    {
        var cache = Path.Combine(scratch, "cache");
        var malformed = new (string Name, Func<JsonObject, JsonObject> Mutate)[]
        {
        ("empty", _ => new JsonObject()),
        ("relative", _ => new JsonObject { ["relative-cache"] = new JsonObject() }),
        ("whitespace", _ => new JsonObject { ["   "] = new JsonObject() }),
        ("null-value", _ => new JsonObject { [cache] = null }),
        ("array-value", _ => new JsonObject { [cache] = new JsonArray() })
        };

        foreach (var (name, mutate) in malformed)
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-" + name + ".assets.json");
            var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            document["packageFolders"] = mutate(document);
            File.WriteAllText(path, document.ToJsonString());
            try
            {
                var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                Require(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
                    $"Malformed packageFolders shape '{name}' was accepted.");
            }
            finally
            {
                File.Delete(path);
            }
        }

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
            Require(result.IsComplete && result.ResolvedPackageCount == 1,
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
                Require(result.ExitCode == 2 && !result.Output.Contains(absoluteFolderPath, StringComparison.OrdinalIgnoreCase),
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
                    Require(result.ExitCode == 2 && !result.Output.Contains(marker, StringComparison.OrdinalIgnoreCase),
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

    internal static void RunCapabilityGroupValidationRegression(string scratch)
    {
        var cases = new (string Name, string Group, bool Empty)[]
        {
        ("case-variant-build", "Build", false),
        ("case-variant-build-transitive", "BUILDTRANSITIVE", false),
        ("case-variant-build-multi-targeting", "BUILDMULTITARGETING", false),
        ("unknown-capability-group", "unknownCapabilityGroup", false),
        ("unknown-empty-group", "unknownEmptyGroup", true),
        ("null-capability-group", "unknownNullGroup", true)
        };

        foreach (var (name, group, empty) in cases)
        {
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
            Require(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
                $"Unsupported target asset group '{group}' was accepted as a clean or partial result.");
        }

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
        Require(!mixedResult.IsComplete && mixedResult.Entries.Count == 0 && mixedResult.IncompleteReasons.Count > 0,
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
        Require(!transitiveResult.IsComplete && transitiveResult.Entries.Count == 0 && transitiveResult.IncompleteReasons.Count > 0,
            "An unsupported target asset group on a transitive RID package was accepted as a clean or partial result.");
    }

    internal static void RunFrameworkMonikerRegression(string assets, string scratch)
    {
        var cases = new[]
        {
        (Declared: "netstandard2.0", Target: ".NETStandard,Version=v2.0"),
        (Declared: "NET8.0", Target: ".NETCoreApp,Version=v8.0"),
        (Declared: "net472", Target: ".NETFramework,Version=v4.7.2"),
        (Declared: "NET472", Target: "net4.7.2/win-x64")
    };

        foreach (var (declared, target) in cases)
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, "framework-" + declared.Replace('.', '-') + ".assets.json");
            var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            var package = root["targets"]!["net8.0"]!.DeepClone();
            root["targets"] = new JsonObject { [target] = package };
            root["project"]!["frameworks"] = new JsonObject
            {
                [declared] = new JsonObject
                {
                    ["targetAlias"] = declared,
                    ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
                }
            };
            File.WriteAllText(path, root.ToJsonString());
            try
            {
                var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                Require(result.IsComplete, $"Equivalent framework monikers were not reconciled: {declared} vs {target}: {string.Join("; ", result.IncompleteReasons)}");
            }
            finally
            {
                File.Delete(path);
            }
        }

        var format4Path = Path.Combine(Path.GetDirectoryName(assets)!, "framework-format4.assets.json");
        var format4 = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        format4["version"] = 4;
        format4["targets"] = new JsonObject { [".NETCoreApp,Version=v8.0"] = format4["targets"]!["net8.0"]!.DeepClone() };
        format4["project"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["framework"] = ".NETCoreApp,Version=v8.0",
                ["targetAlias"] = "net8.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            }
        };
        format4["project"]!["restore"]!["frameworks"] = new JsonObject
        {
            [".NETCoreApp,Version=v8.0"] = new JsonObject
            {
                ["framework"] = "net8.0",
                ["targetAlias"] = "NET8.0"
            }
        };
        format4["projectFileDependencyGroups"] = new JsonObject
        {
            [".NETCoreApp,Version=v8.0"] = new JsonArray()
        };
        File.WriteAllText(format4Path, format4.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
            Require(result.IsComplete, "Format 4 project/restore framework aliases were not reconciled.");
        }
        finally
        {
            File.Delete(format4Path);
        }

        var negativePath = Path.Combine(Path.GetDirectoryName(assets)!, "framework-missing-target.assets.json");
        var negative = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        negative["project"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() } },
            ["netstandard2.0"] = new JsonObject { ["dependencies"] = new JsonObject() }
        };
        File.WriteAllText(negativePath, negative.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(negativePath, scratch, strictContent: false);
            Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase)),
                "A declared framework with no equivalent target graph did not fail closed.");
        }
        finally
        {
            File.Delete(negativePath);
        }
    }

    internal static void RunRestoreIdentityCanonicalizationRegression(string assets, string scratch)
    {
        var format4Path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-valid.assets.json");
        var baselinePath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-valid-baseline.json");
        var baseDocument = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        baseDocument["version"] = 4;
        baseDocument["projectFileDependencyGroups"] = new JsonObject
        {
            ["net8.0"] = new JsonArray("../Referenced/Referenced.csproj", "XmlPackage >= 1.0.0")
        };
        baseDocument["project"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject
            {
                ["framework"] = "net8.0",
                ["targetAlias"] = "net8.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            }
        };
        baseDocument["project"]!["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        File.WriteAllText(format4Path, baseDocument.ToJsonString());
        WriteGeneratedImportEvidence(format4Path);

        try
        {
            var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
            Require(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
                $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
            Require(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
                "The valid restore-identity fixture could not create its baseline.");

            RunRestoreIdentitySetCompletenessRegression(format4Path, scratch, baselinePath);
            RunDependencyRequirementRegression(format4Path, scratch, baselinePath);

            foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
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
            })
            {
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

            var duplicateMetadataPath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-conflicting-metadata.assets.json");
            var validJson = File.ReadAllText(format4Path);
            const string metadataToken = "\"targetAlias\":\"net8.0\"";
            var metadataIndex = validJson.IndexOf(metadataToken, StringComparison.Ordinal);
            Require(metadataIndex >= 0, "The valid restore-identity fixture has no target-alias metadata token.");
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

            var duplicateMaps = new[]
            {
            (Name: "project-frameworks", MapPath: "project.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "restore-frameworks", MapPath: "project.restore.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "target-graphs", MapPath: "targets", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "dependency-groups", MapPath: "projectFileDependencyGroups", Alias: ".NETCoreApp,Version=v8.0")
        };

            foreach (var duplicateMap in duplicateMaps)
            {
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

            foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
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
            })
            {
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

            var recovered = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
            Require(recovered.IsComplete && recovered.ResolvedPackageCount == 1,
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
            Require(aggregate.ExitCode == 2 && aggregate.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !aggregate.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                "Selecting a valid and malformed restore identity together did not fail closed.");
            var narrowed = CaptureCommand("scan", solution, "--project", validProject, "--format", "json", "--no-telemetry");
            Require(narrowed.ExitCode == 0 && !narrowed.Output.Contains("PS007", StringComparison.Ordinal),
                $"--project did not recover the valid restore identity from a malformed sibling: exit={narrowed.ExitCode}; output={narrowed.Output}");
        }
        finally
        {
            if (File.Exists(format4Path)) File.Delete(format4Path);
            if (File.Exists(baselinePath)) File.Delete(baselinePath);
        }
    }

    internal static void RunRestoreJsonStructuralDuplicateRegression(string assets, string scratch)
    {
        var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
        Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
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

        foreach (var (name, json) in cases)
        {
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

        var spellingCases = new List<(string Name, Action<JsonObject> Mutate)>
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

        foreach (var (name, mutate) in spellingCases)
        {
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

        var unknownSpellingCases = new List<(string Name, Action<JsonObject> Mutate)>
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

        foreach (var (name, mutate) in unknownSpellingCases)
        {
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
            Require(extensionResult.IsComplete, $"The explicit x- extension namespace was rejected: {string.Join(" | ", extensionResult.IncompleteReasons)}");
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
            Require(xmlExtensionResult.IsComplete, $"The explicit XML extension namespace was rejected: {string.Join(" | ", xmlExtensionResult.IncompleteReasons)}");
        }
        finally
        {
            File.WriteAllBytes(generatedProps, generatedPropsBefore);
        }

        var contentAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata.assets.json");
        var contentBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata-baseline.json");
        WriteContentAssets(contentAssets, Path.Combine(scratch, "content-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
        Require(CaptureCommand("baseline", contentAssets, "--output", contentBaseline, "--no-telemetry").ExitCode == 0,
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
        Require(CaptureCommand("baseline", contentUnknownAssets, "--output", contentUnknownBaseline, "--no-telemetry").ExitCode == 0,
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
            Require(contentResult.IsComplete, $"The explicit x- content extension was rejected: {string.Join(" | ", contentResult.IncompleteReasons)}");
        }
        finally
        {
            if (File.Exists(contentExtensionAssets)) File.Delete(contentExtensionAssets);
        }

        File.Delete(baseline);
    }

    internal static void RunRestoreOptionalMetadataRegression(string assets, string scratch)
    {
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

    internal static void RunRestoreMetadataShapeRegression(string sourceAssets, string scratch)
    {
        var validFormat3Path = Path.Combine(scratch, "restore-shape-valid-format3.assets.json");
        var validFormat3Baseline = Path.Combine(scratch, "restore-shape-valid-format3-baseline.json");
        var validFormat4Path = Path.Combine(scratch, "restore-shape-valid-format4.assets.json");
        var validFormat4Baseline = Path.Combine(scratch, "restore-shape-valid-format4-baseline.json");
        var validFormat3 = CreateValidRestoreShapeDocument(sourceAssets, format: 3);
        var validFormat4 = CreateValidRestoreShapeDocument(sourceAssets, format: 4);
        File.WriteAllText(validFormat3Path, validFormat3.ToJsonString());
        WriteGeneratedImportEvidence(validFormat3Path);
        File.WriteAllText(validFormat4Path, validFormat4.ToJsonString());
        WriteGeneratedImportEvidence(validFormat4Path);

        try
        {
            AssertValidRestoreMetadata(validFormat3Path, scratch, validFormat3Baseline, "all valid restore metadata shapes (format 3)");
            AssertValidRestoreMetadata(validFormat4Path, scratch, validFormat4Baseline, "all valid restore metadata shapes (format 4)");

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

            foreach (var (name, mutate) in mutations)
            {
                foreach (var (format, template, baseline) in new[]
                {
                ("format3", validFormat3, validFormat3Baseline),
                ("format4", validFormat4, validFormat4Baseline)
            })
                {
                    var invalidPath = Path.Combine(scratch, "restore-shape-invalid-" + format + "-" + name.Replace('.', '-') + ".assets.json");
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
        }
        finally
        {
            foreach (var path in new[] { validFormat3Path, validFormat3Baseline, validFormat4Path, validFormat4Baseline })
            {
                if (File.Exists(path)) File.Delete(path);
            }
            foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
            {
                var generatedPath = Path.Combine(scratch, generated);
                if (File.Exists(generatedPath)) File.Delete(generatedPath);
            }
        }
    }

    internal static void RunReachabilityClosureRegression(string scratch)
    {
        var orphanRoot = Path.Combine(scratch, "reachability-orphan");
        var orphanObj = Path.Combine(orphanRoot, "obj");
        var orphanCache = Path.Combine(orphanRoot, "cache");
        Directory.CreateDirectory(orphanObj);
        var orphanAssets = Path.Combine(orphanObj, "project.assets.json");
        WriteAssets(orphanAssets, orphanCache, "Direct.Root", Array.Empty<string>());
        var orphanDocument = JsonNode.Parse(File.ReadAllText(orphanAssets))!.AsObject();
        var orphanFiles = new[]
        {
        "build/orphan.targets",
        "analyzers/dotnet/cs/orphan.dll",
        "contentFiles/cs/any/orphan.cs",
        "runtimes/win-x64/native/orphan.dll",
        "tools/orphan.ps1"
    };
        AddPackageToAssets(orphanDocument, orphanCache, "Orphan.Package", orphanFiles, new JsonObject
        {
            ["build"] = new JsonObject { ["build/orphan.targets"] = new JsonObject() },
            ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/orphan.dll"] = new JsonObject() },
            ["contentFiles"] = new JsonObject
            {
                ["contentFiles/cs/any/orphan.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
            },
            ["native"] = new JsonObject { ["runtimes/win-x64/native/orphan.dll"] = new JsonObject() },
            ["tools"] = new JsonObject { ["tools/orphan.ps1"] = new JsonObject() }
        });
        var orphanAnalyzer = Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "analyzers", "dotnet", "cs", "orphan.dll");
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, orphanAnalyzer, overwrite: true);
        File.WriteAllText(Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "build", "orphan.targets"), "<Project />");
        File.WriteAllText(orphanAssets, orphanDocument.ToJsonString());
        File.WriteAllText(Path.Combine(orphanObj, "Test.csproj.nuget.g.props"), "<Project><Import Project=\"$(NuGetPackageRoot)/Orphan.Package/1.0.0/build/orphan.targets\" /></Project>");
        var orphanResult = ResolvedGraphClassifier.Analyze(orphanAssets, orphanRoot, strictContent: false);
        Require(!orphanResult.IsComplete && orphanResult.Entries.Count == 0 && orphanResult.ResolvedPackageCount == 0,
            "An orphan package with build, analyzer, content, native, and tool assets was classified as reachable.");

        var islandRoot = Path.Combine(scratch, "reachability-island");
        var islandObj = Path.Combine(islandRoot, "obj");
        var islandCache = Path.Combine(islandRoot, "cache");
        Directory.CreateDirectory(islandObj);
        var islandAssets = Path.Combine(islandObj, "project.assets.json");
        WriteAssets(islandAssets, islandCache, "Direct.Root", Array.Empty<string>());
        var islandDocument = JsonNode.Parse(File.ReadAllText(islandAssets))!.AsObject();
        AddPackageToAssets(islandDocument, islandCache, "Island.Root", Array.Empty<string>(), new JsonObject
        {
            ["dependencies"] = new JsonObject { ["Island.Leaf"] = "1.0.0" }
        });
        AddPackageToAssets(islandDocument, islandCache, "Island.Leaf", Array.Empty<string>());
        File.WriteAllText(islandAssets, islandDocument.ToJsonString());
        var islandResult = ResolvedGraphClassifier.Analyze(islandAssets, islandRoot, strictContent: false);
        Require(!islandResult.IsComplete && islandResult.Entries.Count == 0,
            "A disconnected multi-package dependency island was classified as reachable.");

        var rootedRoot = Path.Combine(scratch, "reachability-rooted-graphs");
        var rootedObj = Path.Combine(rootedRoot, "obj");
        var rootedCache = Path.Combine(rootedRoot, "cache");
        Directory.CreateDirectory(rootedObj);
        var rootedAssets = Path.Combine(rootedObj, "project.assets.json");
        WriteAssets(rootedAssets, rootedCache, "Direct.Root", new List<string> { "tools/direct.ps1" }, createFiles: true);
        var rootedDocument = JsonNode.Parse(File.ReadAllText(rootedAssets))!.AsObject();
        rootedDocument["targets"]!["net8.0"]!["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject
        {
            ["Chain.Leaf"] = "1.0.0",
            ["Cycle.A"] = "1.0.0"
        };
        AddPackageToAssets(rootedDocument, rootedCache, "Chain.Leaf", new List<string> { "tools/chain.ps1" }, new JsonObject { ["tools"] = new JsonObject { ["tools/chain.ps1"] = new JsonObject() } });
        AddPackageToAssets(rootedDocument, rootedCache, "Cycle.A", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.B"] = "1.0.0" } });
        AddPackageToAssets(rootedDocument, rootedCache, "Cycle.B", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.A"] = "1.0.0" } });
        var projectReferenceKey = "Referenced.Project/1.0.0";
        rootedDocument["targets"]!["net8.0"]![projectReferenceKey] = new JsonObject { ["dependencies"] = new JsonObject { ["Project.Root"] = "1.0.0" } };
        rootedDocument["libraries"]![projectReferenceKey] = new JsonObject { ["type"] = "project", ["path"] = "../Referenced.Project", ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj" };
        var projectReferenceAnalyzer = "analyzers/dotnet/cs/project-reference.dll";
        AddPackageToAssets(rootedDocument, rootedCache, "Project.Root", new List<string> { "tools/project-root.ps1", projectReferenceAnalyzer }, new JsonObject
        {
            ["tools"] = new JsonObject { ["tools/project-root.ps1"] = new JsonObject() },
            ["analyzers"] = new JsonObject { [projectReferenceAnalyzer] = new JsonObject() }
        });
        var projectReferenceAnalyzerPath = Path.Combine(rootedCache, "Project.Root", "1.0.0", projectReferenceAnalyzer.Replace('/', Path.DirectorySeparatorChar));
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, projectReferenceAnalyzerPath, overwrite: true);
        File.WriteAllText(rootedAssets, rootedDocument.ToJsonString());
        var rootedResult = ResolvedGraphClassifier.Analyze(rootedAssets, rootedRoot, strictContent: false);
        Require(rootedResult.IsComplete && rootedResult.ResolvedPackageCount == 5,
            "Direct, transitive, cyclic, and project-reference roots did not share one complete closure: " + string.Join(" | ", rootedResult.IncompleteReasons));
        Require(rootedResult.Entries.Any(entry => entry.PackageId == "Chain.Leaf" && entry.Relationship == "transitive") &&
                rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Relationship == "transitive"),
            "Reachable transitive and project-reference packages were not classified with the expected relationship.");
        Require(rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Capability == CapabilityKind.CompilerExtension && entry.Active),
            "An analyzer exported only through a project-reference root was not activated: " + string.Join(" | ", rootedResult.Entries.Select(entry => $"{entry.PackageId}:{entry.Capability}:{entry.Active}:{entry.PackageRelativePath}")) + " reasons=" + string.Join(" | ", rootedResult.IncompleteReasons));

        var ridRoot = Path.Combine(scratch, "reachability-rid");
        var ridObj = Path.Combine(ridRoot, "obj");
        var ridCache = Path.Combine(ridRoot, "cache");
        Directory.CreateDirectory(ridObj);
        var ridAssets = Path.Combine(ridObj, "project.assets.json");
        WriteAssets(ridAssets, ridCache, "Direct.Root", Array.Empty<string>());
        var ridDocument = JsonNode.Parse(File.ReadAllText(ridAssets))!.AsObject();
        AddPackageToAssets(ridDocument, ridCache, "Rid.Only", new List<string> { "runtimes/win-x64/native/rid-only.dll" }, new JsonObject
        {
            ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() }
        });
        ridDocument["targets"]!["net8.0"]!.AsObject().Remove("Rid.Only/1.0.0");
        var ridTarget = ridDocument["targets"]!["net8.0"]!.DeepClone()!.AsObject();
        ridTarget["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject { ["Rid.Only"] = "1.0.0" };
        ridTarget["Rid.Only/1.0.0"] = new JsonObject { ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() } };
        ridDocument["targets"]!.AsObject()["net8.0/win-x64"] = ridTarget;
        File.WriteAllText(ridAssets, ridDocument.ToJsonString());
        var ridResult = ResolvedGraphClassifier.Analyze(ridAssets, ridRoot, strictContent: false);
        Require(ridResult.IsComplete && ridResult.Entries.Count(entry => entry.PackageId == "Rid.Only") == 1 &&
                ridResult.Entries.Single(entry => entry.PackageId == "Rid.Only").RuntimeIdentifier == "win-x64",
            "A package reachable only from the RID-specific target graph was not confined to that graph: " + string.Join(" | ", ridResult.IncompleteReasons));
    }

    internal static void RunVersionAwareReachabilityConflictRegression(string scratch)
    {
        var cases = new (string Name, string RootPackage, string? TransitivePackage, string DirectRange)[]
        {
        ("exact-direct-selection", "Versioned.Direct", null, "[1.0.0, 1.0.0]"),
        ("range-direct-selection", "Versioned.Range.Direct", null, "[1.0.0, 2.0.0)"),
        ("range-transitive-selection", "Versioned.Transitive.Root", "Versioned.Transitive.Dependency", "[1.0.0, 2.0.0)")
        };

        foreach (var (name, rootPackage, transitivePackage, directRange) in cases)
        {
            var root = Path.Combine(scratch, "version-aware-" + name);
            var obj = Path.Combine(root, "obj");
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(obj);
            var assets = Path.Combine(obj, "project.assets.json");
            WriteAssets(assets, cache, rootPackage, Array.Empty<string>());
            var baseline = Path.Combine(obj, "baseline.json");
            Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
                $"The valid {name} baseline could not be created.");

            var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            if (transitivePackage is null)
            {
                document["project"]!["frameworks"]!["net8.0"]!["dependencies"]![rootPackage]!["version"] = directRange;
            }
            else
            {
                document["targets"]!["net8.0"]![rootPackage + "/1.0.0"]!["dependencies"] = new JsonObject
                {
                    [transitivePackage] = directRange
                };
                AddPackageToAssets(document, cache, transitivePackage, Array.Empty<string>(), new JsonObject
                {
                    ["dependencies"] = new JsonObject()
                });
            }

            File.WriteAllText(assets, document.ToJsonString());
            var validResult = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            var expectedResolvedCount = transitivePackage is null ? 1 : 2;
            Require(validResult.IsComplete && validResult.ResolvedPackageCount == expectedResolvedCount,
                $"The valid {name} version-range graph was not resolved before adding the conflicting sibling: {string.Join(" | ", validResult.IncompleteReasons)}");

            AddPackageVersionToAssets(document, cache, transitivePackage ?? rootPackage, "1.0.1", Array.Empty<string>());
            File.WriteAllText(assets, document.ToJsonString());
            AssertRestoreIdentityFailure(assets, root, baseline, name);
        }

        var surfaceRoot = Path.Combine(scratch, "version-aware-all-surfaces");
        var surfaceObj = Path.Combine(surfaceRoot, "obj");
        var surfaceCache = Path.Combine(surfaceRoot, "cache");
        Directory.CreateDirectory(surfaceObj);
        var surfaceAssets = Path.Combine(surfaceObj, "project.assets.json");
        WriteAssets(surfaceAssets, surfaceCache, "Versioned.Surface", Array.Empty<string>());
        var surfaceBaseline = Path.Combine(surfaceObj, "baseline.json");
        Require(CaptureCommand("baseline", surfaceAssets, "--output", surfaceBaseline, "--no-telemetry").ExitCode == 0,
            "The valid all-surfaces baseline could not be created.");

        var surfaceDocument = JsonNode.Parse(File.ReadAllText(surfaceAssets))!.AsObject();
        surfaceDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Versioned.Surface"]!["version"] = "[1.0.0, 1.0.0]";
        var surfaceFiles = new[]
        {
        "build/conflict.targets",
        "analyzers/dotnet/cs/conflict.dll",
        "contentFiles/cs/any/conflict.cs",
        "runtimes/win-x64/native/conflict.dll",
        "tools/conflict.ps1"
    };
        AddPackageVersionToAssets(surfaceDocument, surfaceCache, "Versioned.Surface", "1.0.1", surfaceFiles, new JsonObject
        {
            ["build"] = new JsonObject { ["build/conflict.targets"] = new JsonObject() },
            ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/conflict.dll"] = new JsonObject() },
            ["contentFiles"] = new JsonObject
            {
                ["contentFiles/cs/any/conflict.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
            },
            ["native"] = new JsonObject { ["runtimes/win-x64/native/conflict.dll"] = new JsonObject() },
            ["tools"] = new JsonObject { ["tools/conflict.ps1"] = new JsonObject() }
        });
        var surfacePackageRoot = Path.Combine(surfaceCache, "Versioned.Surface", "1.0.1");
        File.WriteAllText(Path.Combine(surfacePackageRoot, "build", "conflict.targets"), "<Project />");
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
            Path.Combine(surfacePackageRoot, "analyzers", "dotnet", "cs", "conflict.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(surfaceObj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Versioned.Surface/1.0.1/build/conflict.targets\" /></Project>");
        File.WriteAllText(surfaceAssets, surfaceDocument.ToJsonString());
        AssertRestoreIdentityFailure(surfaceAssets, surfaceRoot, surfaceBaseline, "all-surfaces-multi-version-selection");
    }

    internal static void RunVersionEquivalenceRegression(string scratch)
    {
        var equivalent = new[] { "1", "1.0", "1.0.0", "1.0.0.0", "01.000.000.000" };
        var identities = equivalent.Select(value =>
        {
            Require(PackageIdentity.TryCreate("Version.Package", value, out var identity), $"NuGet-compatible version '{value}' was rejected.");
            return identity;
        }).ToArray();
        Require(identities.All(identity => identity.Equals(identities[0])), "Equivalent numeric package versions did not share one identity.");
        Require(PackageIdentity.TryCreate("Version.Package", "1.2.3.4", out var nonZeroFourth) &&
                !PackageIdentity.TryCreate("Version.Package", "1.2.3.4.5", out _),
            "The four-component version boundary was not enforced.");
        Require(PackageIdentity.TryCreate("Version.Package", "1.0.0-Alpha.1+Build.1", out var prerelease) &&
                PackageIdentity.TryCreate("version.package", "1.0.0-alpha.1+Other.2", out var equivalentPrerelease) &&
                prerelease.Equals(equivalentPrerelease),
            "Prerelease casing/components or build metadata did not use the canonical identity grammar.");
        foreach (var invalid in new[]
        {
        "1..0",
        "1.0-",
        "1.0+",
        "1.0.0-alpha.01",
        "1.0.0-01",
        "1.0.0-α",
        "1.0.0-alpha..1",
        "1.0.0-",
        "1.0.0/child",
        "1.0.0,,2.0.0",
        "1.0.0-+build",
        " 1.0.0"
    })
        {
            Require(!PackageIdentity.TryCreate("Version.Package", invalid, out _), $"Malformed version '{invalid}' was accepted.");
        }

        var root = Path.Combine(scratch, "version-ranges");
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, "Range.Package", Array.Empty<string>());
        var source = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        foreach (var range in new[] { "[1, 1]", "[1.0, 1.0]", "[1.0.0, 1.0.0]", "[1.0.0.0, 1.0.0.0]", "1.0.0.0" })
        {
            source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = range;
            File.WriteAllText(assets, source.ToJsonString());
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(result.IsComplete, $"Dependency range '{range}' disagreed with the canonical package identity: {string.Join(" | ", result.IncompleteReasons)}");
        }

        source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = "[1.0.0.0.0, )";
        File.WriteAllText(assets, source.ToJsonString());
        var invalidRange = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!invalidRange.IsComplete && invalidRange.Entries.Count == 0, "A five-component dependency range was accepted.");

        foreach (var invalid in new[] { "1.0.0-alpha.01", "1.0.0-01", "1.0.0-α", "[1.0.0-alpha.01, )", "(1.0.0-01, 2.0.0]" })
        {
            source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = invalid;
            File.WriteAllText(assets, source.ToJsonString());
            var malformed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(!malformed.IsComplete && malformed.Entries.Count == 0,
                $"Malformed NuGet version range '{invalid}' was accepted.");
        }
    }

    internal static void RunMalformedVersionEndToEndRegression(string scratch)
    {
        var root = Path.Combine(scratch, "malformed-version-surfaces");
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var files = new[]
        {
        "build/surface.targets",
        "analyzers/dotnet/cs/surface.dll",
        "contentFiles/cs/any/surface.cs",
        "runtimes/win-x64/native/surface.dll",
        "tools/surface.ps1"
    };
        WriteAssets(assets, cache, "Version.Surface", files, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var targetPackage = document["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!.AsObject();
        targetPackage["build"] = new JsonObject { ["build/surface.targets"] = new JsonObject() };
        targetPackage["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/surface.dll"] = new JsonObject() };
        targetPackage["contentFiles"] = new JsonObject
        {
            ["contentFiles/cs/any/surface.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "cs" }
        };
        targetPackage["native"] = new JsonObject { ["runtimes/win-x64/native/surface.dll"] = new JsonObject() };
        targetPackage["tools"] = new JsonObject { ["tools/surface.ps1"] = new JsonObject() };
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
            Path.Combine(cache, "Version.Surface", "1.0.0", "analyzers", "dotnet", "cs", "surface.dll"), overwrite: true);
        File.WriteAllText(Path.Combine(cache, "Version.Surface", "1.0.0", "build", "surface.targets"), "<Project />");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/1.0.0/build/surface.targets\" /></Project>");
        File.WriteAllText(assets, document.ToJsonString());

        var baseline = Path.Combine(obj, "baseline.json");
        var baselineResult = CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry");
        Require(baselineResult.ExitCode == 0,
            "The all-capability version fixture could not create its baseline: " + baselineResult.Output);
        var validSource = File.ReadAllText(assets);
        var invalidVersions = new[] { "1.0.0-alpha.01", "1.0.0-01", "1.0.0-α" };
        foreach (var invalid in invalidVersions)
        {
            var malformedPath = Path.Combine(obj, "invalid-key-" + invalid.Replace('/', '_') + ".assets.json");
            File.WriteAllText(malformedPath, validSource.Replace("Version.Surface/1.0.0", "Version.Surface/" + invalid, StringComparison.Ordinal));
            try
            {
                AssertRestoreIdentityFailure(malformedPath, root, baseline, "malformed package identity " + invalid);
            }
            finally
            {
                File.Delete(malformedPath);
            }
        }

        var libraryPathDocument = JsonNode.Parse(validSource)!.AsObject();
        libraryPathDocument["libraries"]!["Version.Surface/1.0.0"]!["path"] = "Version.Surface/1.0.0-alpha.01";
        var libraryPath = Path.Combine(obj, "invalid-library-path.assets.json");
        File.WriteAllText(libraryPath, libraryPathDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(libraryPath, root, baseline, "malformed library package path");
        }
        finally
        {
            File.Delete(libraryPath);
        }

        var directRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        directRangeDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Version.Surface"]!["version"] = "[1.0.0-alpha.01, )";
        var directRange = Path.Combine(obj, "invalid-direct-range.assets.json");
        File.WriteAllText(directRange, directRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(directRange, root, baseline, "malformed direct dependency range");
        }
        finally
        {
            File.Delete(directRange);
        }

        var targetRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        targetRangeDocument["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!["dependencies"] = new JsonObject
        {
            ["Version.Surface"] = "[1.0.0-01, )"
        };
        var targetRange = Path.Combine(obj, "invalid-target-range.assets.json");
        File.WriteAllText(targetRange, targetRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(targetRange, root, baseline, "malformed target dependency range");
        }
        finally
        {
            File.Delete(targetRange);
        }

        var projectRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        projectRangeDocument["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["dependencies"] = new JsonObject { ["Version.Surface"] = "[1.0.0-01, )" }
        };
        projectRangeDocument["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["path"] = "../Referenced.Project",
            ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
        };
        var projectRange = Path.Combine(obj, "invalid-project-range.assets.json");
        File.WriteAllText(projectRange, projectRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(projectRange, root, baseline, "malformed project dependency range");
        }
        finally
        {
            File.Delete(projectRange);
        }

        var format4Document = JsonNode.Parse(validSource)!.AsObject();
        format4Document["version"] = 4;
        format4Document["projectFileDependencyGroups"] = new JsonObject
        {
            ["net8.0"] = new JsonArray("Version.Surface >= 1.0.0")
        };
        format4Document["project"]!["frameworks"]!["net8.0"]!["framework"] = "net8.0";
        format4Document["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net8.0";
        format4Document["project"]!["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        var format4 = Path.Combine(obj, "malformed-format4-version.assets.json");
        var format4Baseline = Path.Combine(obj, "malformed-format4-version-baseline.json");
        File.WriteAllText(format4, format4Document.ToJsonString());
        var format4BaselineResult = CaptureCommand("baseline", format4, "--output", format4Baseline, "--no-telemetry");
        Require(format4BaselineResult.ExitCode == 0,
            "The valid format 4 version fixture could not create its baseline: " + format4BaselineResult.Output);
        try
        {
            format4Document["projectFileDependencyGroups"]!["net8.0"]![0] = "Version.Surface >= 1.0.0-alpha.01";
            File.WriteAllText(format4, format4Document.ToJsonString());
            AssertRestoreIdentityFailure(format4, root, format4Baseline, "malformed format 4 dependency requirement");
        }
        finally
        {
            if (File.Exists(format4)) File.Delete(format4);
            if (File.Exists(format4Baseline)) File.Delete(format4Baseline);
        }

        var generatedTargets = Path.Combine(obj, "Test.csproj.nuget.g.targets");
        var generatedTargetsBefore = File.ReadAllBytes(generatedTargets);
        try
        {
            foreach (var invalid in invalidVersions)
            {
                File.WriteAllText(generatedTargets,
                    $"<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/{invalid}/build/surface.targets\" /></Project>");
                AssertRestoreIdentityFailure(assets, root, baseline, "malformed generated import version " + invalid);
            }
        }
        finally
        {
            File.WriteAllBytes(generatedTargets, generatedTargetsBefore);
        }

        var baselineDocument = JsonNode.Parse(File.ReadAllText(baseline))!.AsObject();
        foreach (var entry in baselineDocument["entries"]!.AsArray().OfType<JsonObject>())
        {
            entry["version"] = "1.0.0-alpha.01";
        }
        var malformedBaseline = Path.Combine(obj, "malformed-version-baseline.json");
        File.WriteAllText(malformedBaseline, baselineDocument.ToJsonString());
        try
        {
            foreach (var format in new[] { "text", "json", "sarif" })
            {
                var check = CaptureCommand("check", assets, "--baseline", malformedBaseline, "--format", format, "--no-telemetry");
                Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
                    $"Malformed baseline version was accepted in {format} output.");
                AssertNoSuccessfulSurfaceOutput(check.Output, format, "malformed baseline version " + format);
            }
        }
        finally
        {
            File.Delete(malformedBaseline);
        }
    }

    internal static void RunRestoreIdentitySetCompletenessRegression(string format4Path, string scratch, string baselinePath)
    {
        var cases = new (string Name, Action<JsonObject> Mutate)[]
        {
        ("project-framework-missing-from-restore", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["net9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("restore-framework-missing-from-project", document =>
        {
            document["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0"
            };
        }),
        ("dependency-group-missing-from-project", document =>
            document["projectFileDependencyGroups"]!.AsObject().Remove("net8.0")),
        ("dependency-group-extra-framework", document =>
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray()),
        ("rid-framework-missing-from-restore", document =>
            document["targets"]!.AsObject()["net9.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone()),
        ("case-folded-project-framework-set-difference", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["NET9.0"] = new JsonObject
            {
                ["framework"] = "NET9.0",
                ["targetAlias"] = "NET9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["NET9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["NET9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("equivalent-moniker-framework-set-difference", document =>
        {
            document["project"]!["frameworks"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = ".NETCoreApp,Version=v9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()[".NETCoreApp,Version=v9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        })
        };

        foreach (var (name, mutate) in cases)
        {
            var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "restore-set-" + name + ".assets.json");
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

}
