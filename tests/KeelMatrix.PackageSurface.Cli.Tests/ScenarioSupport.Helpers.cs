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
    internal static void AssertPackageXmlVariantFailure(
        string assets,
        string projectRoot,
        string baseline,
        string physicalPath,
        string canonical,
        string label,
        string oldText,
        string newText)
    {
        var variant = canonical.Replace(oldText, newText, StringComparison.Ordinal);
        Require(!variant.Equals(canonical, StringComparison.Ordinal), $"The XML regression variant '{label}' was not applied.");
        File.WriteAllText(physicalPath, variant);
        try
        {
            AssertRestoreIdentityFailure(assets, projectRoot, baseline, label);
        }
        finally
        {
            File.WriteAllText(physicalPath, canonical);
        }
    }

    internal static void AssertPackageXmlDuplicateAttributeFailure(
        string assets,
        string projectRoot,
        string baseline,
        string physicalPath,
        string canonical,
        string name,
        string canonicalAttribute,
        string alias)
    {
        var marker = " " + canonicalAttribute + "=\"";
        var markerIndex = canonical.IndexOf(marker, StringComparison.Ordinal);
        Require(markerIndex >= 0, $"The XML duplicate variant '{name}' could not find its canonical attribute.");
        var valueEnd = canonical.IndexOf('"', markerIndex + marker.Length);
        Require(valueEnd >= 0, $"The XML duplicate variant '{name}' has no complete canonical attribute value.");
        var variant = canonical.Insert(valueEnd + 1, " " + alias + "=\"duplicate\"");
        File.WriteAllText(physicalPath, variant);
        try
        {
            AssertRestoreIdentityFailure(assets, projectRoot, baseline, "attribute-" + name + "-duplicate");
        }
        finally
        {
            File.WriteAllText(physicalPath, canonical);
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

    internal static void AssertGeneratedImportFailure(string label, string assets, string baseline)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("BuildTargets", StringComparison.Ordinal) &&
                    !scan.Output.Contains("Unused.Package", StringComparison.Ordinal),
                $"{label} emitted a clean or partial {format} scan result.");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !failedBaseline.Output.Contains("BuildTargets", StringComparison.Ordinal),
                $"{label} emitted a clean or partial {format} baseline result.");
            Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)), $"{label} mutated the baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"{label} emitted a clean or policy-diff {format} check result.");
        }
    }

    internal static void AddBuildGroups(string assets, string packageId, IReadOnlyList<string> files)
    {
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
        package["build"] = new JsonObject
        {
            [files[0]] = new JsonObject(),
            [files[1]] = new JsonObject()
        };
        File.WriteAllText(assets, document.ToJsonString());
    }

    internal static void WriteRepeatedGeneratedImports(string obj, int propsCount, int targetsCount, IReadOnlyList<string> files)
    {
        var props = new StringBuilder("<Project>");
        for (var index = 0; index < propsCount; index++)
        {
            props.Append("<Import Project=\"$(NuGetPackageRoot)/Edge.Package/1.0.0/").Append(files[0]).Append("\" />");
        }
        props.Append("</Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), props.ToString());

        var targets = new StringBuilder("<Project>");
        for (var index = 0; index < targetsCount; index++)
        {
            targets.Append("<Import Project=\"$(NuGetPackageRoot)/Edge.Package/1.0.0/").Append(files[1]).Append("\" />");
        }
        targets.Append("</Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), targets.ToString());
    }

    internal static void WritePackageBuildFiles(string cache, IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            var path = Path.Combine(cache, "Edge.Package", "1.0.0", file.Replace('/', Path.DirectorySeparatorChar));
            File.WriteAllText(path, "<Project />");
        }
    }

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

    internal static void RunSymlinkPackageAliasCase(string root, string assets, string packageFile, byte[] before, string label)
    {
        var output = Path.Combine(root, "package-" + label + "-symlink.json");
        try
        {
            File.CreateSymbolicLink(output, packageFile);
            Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                $"A {label} symlink alias to a reachable package input was accepted.");
            Require(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A {label} symlink alias attempt changed the package input.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this platform."); }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    internal static void RunDirectorySymlinkPackageAliasCase(string root, string assets, string target, string packageFile, byte[] before, string label)
    {
        var link = Path.Combine(root, "package-" + label + "-link");
        var output = Path.Combine(link, "tool.ps1");
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                $"A package {label}-directory reparse alias was accepted.");
            Require(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A package {label}-directory alias attempt changed the package input.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this platform."); }
    }

    internal static void RenameJsonProperty(JsonObject parent, string oldName, string newName)
    {
        var value = parent[oldName]?.DeepClone() ?? throw new InvalidOperationException($"Missing JSON property {oldName}.");
        parent.Remove(oldName);
        parent[newName] = value;
    }

    internal static void SetTelemetryHook(Action? hook)
    {
        var property = typeof(CommandLine).GetProperty("TelemetryHook", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Telemetry test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static void SetOutputSerializer(Func<ReportDocument, OutputFormat, string>? hook)
    {
        var property = typeof(CommandLine).GetProperty("OutputSerializer", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Output serializer test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static void SetOutputSink(Action<string>? hook)
    {
        var property = typeof(CommandLine).GetProperty("OutputSink", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Output sink test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static (int ExitCode, string Output) CaptureCommand(params string[] args)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var priorOutput = Console.Out;
        var priorError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = CommandLine.Run(args);
            return (exitCode, output.ToString() + error.ToString());
        }
        finally
        {
            Console.SetOut(priorOutput);
            Console.SetError(priorError);
        }
    }

    internal static void AssertFormat4DependencyGroupFailure(string assets, string baseline)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("Entries: 1", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} scan.");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} baseline.");
            Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
                $"Format 4 casing-equivalent dependency groups mutated the baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} check.");
        }
    }

    internal static void AssertValidRestoreMetadata(string assets, string projectRoot, string baseline, string label)
    {
        var analyzed = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
        Require(analyzed.IsComplete && analyzed.Entries.Count == 0,
            $"{label} was rejected: {string.Join(" | ", analyzed.IncompleteReasons)}");

        var scan = CaptureCommand("scan", assets, "--format", "json", "--no-telemetry");
        Require(scan.ExitCode == 0 && !scan.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} scan failed: {scan.Output}");
        Require(CaptureCommand("baseline", assets, "--output", baseline, "--format", "json", "--no-telemetry").ExitCode == 0,
            $"{label} baseline failed.");
        var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", "json", "--no-telemetry");
        Require(check.ExitCode == 0 && !check.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} check failed: {check.Output}");
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

    internal static void RunDependencyRequirementRegression(string format4Path, string scratch, string baselinePath)
    {
        var cases = new (string Name, Action<JsonObject> Mutate)[]
        {
        ("project-dependency-invalid-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "not-a-version-range"),
        ("project-dependency-empty-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "  "),
        ("project-dependency-unsupported-version", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "6.*"),
        ("project-dependency-version-mismatch", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "[2.0.0, )"),
        ("target-dependency-invalid-range", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" }),
        ("target-dependency-invalid-operator", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = ">= 1.0.0" }),
        ("target-dependency-version-mismatch", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "[2.0.0, )" }),
        ("format4-package-invalid-operator", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage => 1.0.0")),
        ("format4-package-invalid-version", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= not-a-version")),
        ("format4-package-version-mismatch", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 2.0.0")),
        ("format4-package-empty-value", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("  ")),
        ("format4-absolute-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("C:/Referenced/Referenced.csproj")),
        ("format4-unsupported-project-extension", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.txt")),
        ("format4-ambiguous-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.csproj extra"))
        };

        foreach (var (name, mutate) in cases)
        {
            var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-" + name + ".assets.json");
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

        var multiTargetPath = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-multi-rid.assets.json");
        var multiTarget = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        var projectFrameworks = multiTarget["project"]!["frameworks"]!.AsObject();
        projectFrameworks["net9.0"] = new JsonObject
        {
            ["framework"] = "net9.0",
            ["targetAlias"] = "net9.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
        };
        multiTarget["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
        {
            ["framework"] = "net9.0",
            ["targetAlias"] = "net9.0"
        };
        var targets = multiTarget["targets"]!.AsObject();
        targets["net8.0/win-x64"] = targets["net8.0"]!.DeepClone();
        targets["net9.0"] = targets["net8.0"]!.DeepClone();
        var groups = multiTarget["projectFileDependencyGroups"]!.AsObject();
        groups["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        targets["net8.0/win-x64"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" };
        File.WriteAllText(multiTargetPath, multiTarget.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(multiTargetPath, scratch, baselinePath, "multi-target-rid dependency requirement");
        }
        finally
        {
            File.Delete(multiTargetPath);
        }
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
        Require(first >= 0, $"Could not locate exact property token for '{propertyName}'.");
        return json[..first] + token + "," + token + json[(first + token.Length)..];
    }

    internal static string InsertCaseVariantPropertyDuplicate(string json, string propertyName, string value)
    {
        var token = JsonSerializer.Serialize(propertyName) + ":" + value;
        var first = json.IndexOf(token, StringComparison.Ordinal);
        Require(first >= 0, $"Could not locate case-variant property token for '{propertyName}'.");
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

    internal static void AssertRestoreIdentityFailure(string assets, string projectRoot, string baseline, string label)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var direct = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
            Require(!direct.IsComplete && direct.Entries.Count == 0 && direct.IncompleteReasons.Count > 0,
                $"{label} was not rejected before capability filtering.");

            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} scan.");
            AssertNoSuccessfulSurfaceOutput(scan.Output, format, label + " scan");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} baseline.");
            AssertNoSuccessfulSurfaceOutput(failedBaseline.Output, format, label + " baseline");
            Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
                $"{label} mutated the existing baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} check.");
            AssertNoSuccessfulSurfaceOutput(check.Output, format, label + " check");
        }
    }

    internal static void AssertNoSuccessfulSurfaceOutput(string output, string format, string label)
    {
        var entryLine = output.Contains("Entries:", StringComparison.Ordinal);
        Require((!entryLine || output.Contains("Entries: 0;", StringComparison.Ordinal)) &&
                !output.Contains("PS001", StringComparison.Ordinal) &&
                !output.Contains("PS002", StringComparison.Ordinal) &&
                !output.Contains("PS003", StringComparison.Ordinal) &&
                !output.Contains("PS004", StringComparison.Ordinal) &&
                !output.Contains("PS005", StringComparison.Ordinal) &&
                !output.Contains("PS006", StringComparison.Ordinal),
            $"{label} emitted success-looking output.");

        if (format.Equals("json", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            Require(document.RootElement.GetProperty("entries").GetArrayLength() == 0,
                $"{label} emitted surface entries.");
        }
        else if (format.Equals("sarif", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
            Require(results.EnumerateArray().All(result => result.GetProperty("ruleId").GetString() == "PS007"),
                $"{label} emitted a non-PS007 SARIF result.");
        }
    }

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

    internal static void WriteGeneratedImportEvidence(string assetsPath, string projectFileName = "Test.csproj")
    {
        var directory = Path.GetDirectoryName(assetsPath)!;
        File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.targets"), "<Project />");
    }

    internal static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    internal static bool TryCreateHardLink(string existingPath, string linkPath) =>
        OperatingSystem.IsWindows()
            ? NativeMethods.CreateHardLink(linkPath, existingPath, IntPtr.Zero)
            : NativeMethods.Link(existingPath, linkPath) == 0;

}
