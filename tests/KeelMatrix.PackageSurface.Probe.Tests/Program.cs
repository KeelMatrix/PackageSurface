using System.Diagnostics;
using System.Text.Json;
using System.Xml.Linq;
using KeelMatrix.PackageSurface.Probe;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: probe-tests <project.assets.json> <capabilities>");
    return 2;
}

var assets = Path.GetFullPath(args[0]);
var projectRoot = Directory.GetParent(Path.GetDirectoryName(assets)!)!.FullName;
var result = ResolvedGraphClassifier.Analyze(assets, projectRoot);
if (!result.IsComplete)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, result.IncompleteReasons));
    return 1;
}

var required = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(value => Enum.Parse<CapabilityKind>(value, ignoreCase: true))
    .ToArray();
var present = result.Entries.Select(entry => entry.Capability).Distinct().ToHashSet();
if (required.Any(kind => !present.Contains(kind)))
{
    Console.Error.WriteLine("Missing capability fixture: " + string.Join(", ", required.Where(kind => !present.Contains(kind))));
    return 1;
}

using (var document = JsonDocument.Parse(File.ReadAllText(assets)))
{
    var graphTargetFrameworks = document.RootElement.GetProperty("targets")
        .EnumerateObject()
        .Select(target => target.Name.Split('/', 2)[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var classifiedTargetFrameworks = result.Entries
        .Where(entry => entry.Context == SurfaceContextKind.Target)
        .Select(entry => entry.TargetFramework)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (graphTargetFrameworks.Any(targetFramework => !classifiedTargetFrameworks.Contains(targetFramework)))
    {
        Console.Error.WriteLine("A target framework in the graph has no target-context classifier entry.");
        return 1;
    }

    var outerTargetEntries = result.Entries.Where(entry => entry.Capability == CapabilityKind.BuildMultiTargeting).ToArray();
    if (outerTargetEntries.Any(entry => entry.Context != SurfaceContextKind.Project || entry.TargetFramework is not null || entry.RuntimeIdentifier is not null) ||
        outerTargetEntries.GroupBy(entry => string.Join("|", entry.PackageId, entry.Version, entry.PackageRelativePath), StringComparer.OrdinalIgnoreCase).Count() != outerTargetEntries.Length)
    {
        Console.Error.WriteLine("Outer-target assets were associated with a target framework or duplicated.");
        return 1;
    }

    if (result.Entries.Any(entry => entry.Active && entry.Context == SurfaceContextKind.Target && entry.Capability == CapabilityKind.BuildMultiTargeting))
    {
        Console.Error.WriteLine("An outer-target asset was marked active for a target framework.");
        return 1;
    }
}

if (result.Entries.Any(entry => entry.Capability == CapabilityKind.ToolOrScriptPresent && entry.Active) ||
    (assets.Contains("RidTarget", StringComparison.OrdinalIgnoreCase) &&
     required.Contains(CapabilityKind.NativeRuntime) &&
     (!result.Entries.Any(entry => entry.Capability == CapabilityKind.NativeRuntime && entry.Active) ||
      !result.Entries.Any(entry => entry.Capability == CapabilityKind.NativeRuntime && !entry.Active))))
{
    Console.Error.WriteLine("Present/active fixture assertions failed.");
    return 1;
}

RunGeneratedImportConditionRegression(assets);
RunGeneratedImportFileIdentityRegression(assets);
RunGeneratedImportPhaseCrossWireRegression(assets);

var malformed = Path.Combine(Path.GetTempPath(), "packagesurface-malformed-assets.json");
try
{
    File.WriteAllText(malformed, "{");
    var malformedResult = ResolvedGraphClassifier.Analyze(malformed, projectRoot);
    if (malformedResult.IsComplete)
    {
        Console.Error.WriteLine("Malformed assets were not treated as incomplete.");
        return 1;
    }
}
finally
{
    if (File.Exists(malformed))
    {
        File.Delete(malformed);
    }
}

Console.WriteLine(JsonSerializer.Serialize(new { entries = result.Entries.Count, complete = result.IsComplete }));
return 0;

static void RunGeneratedImportConditionRegression(string baselineAssets)
{
    using var document = JsonDocument.Parse(File.ReadAllText(baselineAssets));
    var root = document.RootElement;
    var projectPath = root.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString()!;
    var normalizedProjectPath = projectPath.Replace('\\', '/');
    var projectFileName = normalizedProjectPath[(normalizedProjectPath.LastIndexOf('/') + 1)..];
    var generatedPropsFileName = projectFileName + ".nuget.g.props";
    var generatedTargetsFileName = projectFileName + ".nuget.g.targets";
    var targetFrameworks = root.GetProperty("targets").EnumerateObject().Select(target => target.Name.Split('/', 2)[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    var allTargetFrameworks = string.Join(',', targetFrameworks);
    var nonNet8TargetFrameworks = string.Join(',', targetFrameworks.Where(target => !target.Equals("net8.0", StringComparison.OrdinalIgnoreCase)));
    var targetAliases = root.GetProperty("project").GetProperty("frameworks").EnumerateObject()
        .Select(framework => framework.Value.TryGetProperty("targetAlias", out var alias) ? alias.GetString()! : framework.Name)
        .ToArray();
    var alternateTargetFramework = targetAliases.FirstOrDefault(target => !target.Equals("net8.0", StringComparison.OrdinalIgnoreCase)) ?? "net8.0";
    var packageFolder = root.GetProperty("packageFolders").EnumerateObject().Select(property => property.Name).First();
    var buildPropsLibrary = root.GetProperty("libraries").GetProperty("KeelMatrix.Phase0.BuildProps/1.0.0");
    var buildPropsRoot = Path.Combine(packageFolder, buildPropsLibrary.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
    var buildPropsPath = "build/KeelMatrix.Phase0.BuildProps.props";
    var buildPropsImport = Path.Combine(buildPropsRoot, buildPropsPath.Replace('/', Path.DirectorySeparatorChar));
    var buildPropsMacroPath = "$(NuGetPackageRoot)/" + buildPropsLibrary.GetProperty("path").GetString()!.Replace('/', '/') + "/" + buildPropsPath;

    var buildMultiLibrary = root.GetProperty("libraries").GetProperty("KeelMatrix.Phase0.BuildMultiTargeting/1.0.0");
    var buildMultiRoot = Path.Combine(packageFolder, buildMultiLibrary.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
    var buildMultiPath = "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets";
    var buildMultiImport = Path.Combine(buildMultiRoot, buildMultiPath.Replace('/', Path.DirectorySeparatorChar));

    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-condition-tests-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(scratch, "obj"));

    try
    {
        File.Copy(baselineAssets, Path.Combine(scratch, "obj", "project.assets.json"));
        File.WriteAllText(Path.Combine(scratch, generatedTargetsFileName), "<Project />");
        AssertTarget("unconditional import", CreateImports(buildPropsImport, null, null), true, null, allTargetFrameworks);
        AssertTarget("TFM equality", CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0'", null), true, null, "net8.0");
        AssertTarget("TFM inequality", CreateImports(buildPropsImport, "'$(TargetFramework)' != 'net8.0'", null), true, null, nonNet8TargetFrameworks);
        AssertTarget("empty string equality", CreateImports(buildPropsImport, "'$(TargetFramework)' == ''", null), true, null, string.Empty);
        AssertTarget("non-empty string inequality", CreateImports(buildPropsImport, "'$(TargetFramework)' != ''", null), true, null, allTargetFrameworks);
        AssertTarget("AND composition", CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0' AND '$(ExcludeRestorePackageImports)' != 'true'", null), true, null, "net8.0");
        AssertTarget("repeated TFM comparisons", CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0' AND '$(TargetFramework)' == 'net8.0'", null), true, null, "net8.0");
        AssertTarget("OR composition", CreateImports(buildPropsImport, $"'$(TargetFramework)' == 'net8.0' OR '$(TargetFramework)' == '{alternateTargetFramework}'", null), true, null, allTargetFrameworks);
        AssertTarget("restore guard", CreateImports(buildPropsImport, "'$(ExcludeRestorePackageImports)' != 'true'", null), true, null, allTargetFrameworks);
        AssertTarget("standard Exists guard", CreateImports(buildPropsImport, null, $"Exists('{buildPropsMacroPath}')"), true, null, allTargetFrameworks);
        AssertTarget("ImportGroup and Import conditions", CreateImports(buildPropsImport, "'$(ExcludeRestorePackageImports)' != 'true'", "'$(TargetFramework)' == 'net8.0'"), true, null, "net8.0");
        AssertTarget("nested groups", CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0'", "'$(ExcludeRestorePackageImports)' != 'true'", nested: true), true, null, "net8.0");
        AssertTarget("arbitrary property", CreateImports(buildPropsImport, "'$(Configuration)' == 'Debug'", null), false, "Configuration", string.Empty);
        AssertTarget("additional arbitrary clause", CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0' AND '$(Configuration)' == 'Debug'", null), false, "Configuration", string.Empty);
        AssertTarget("nested arbitrary condition", CreateImports(buildPropsImport, "'$(Configuration)' == 'Debug'", null, nested: true), false, "Configuration", string.Empty);
        AssertTarget("unproven Exists", CreateImports(buildPropsImport, null, "Exists('$(SomeRoot)/unknown.props')"), false, "Exists(...)", string.Empty);

        File.WriteAllText(Path.Combine(scratch, generatedPropsFileName), "<Project />");
        AssertProject("empty TFM project context", CreateImports(buildMultiImport, "'$(TargetFramework)' == ''", null), complete: true, active: true, buildMultiPath);

        CreateImports(buildPropsImport, "'$(TargetFramework)' == 'net8.0' AND '$(Configuration)' == 'Debug'", null).Save(Path.Combine(scratch, generatedPropsFileName));
        AssertTargetExitCode(scratch, expected: 2);
    }
    finally
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    XDocument CreateImports(string importProject, string? groupCondition, string? importCondition, bool nested = false)
    {
        var import = new XElement("Import", new XAttribute("Project", importProject));
        if (importCondition is not null)
        {
            import.SetAttributeValue("Condition", importCondition);
        }

        var group = new XElement("ImportGroup");
        if (groupCondition is not null)
        {
            group.SetAttributeValue("Condition", groupCondition);
        }

        if (nested)
        {
            group.Add(new XElement("ImportGroup", new XAttribute("Condition", importCondition ?? string.Empty), import));
        }
        else
        {
            group.Add(import);
        }

        return new XDocument(new XElement("Project", group));
    }

    void AssertTarget(string scenario, XDocument generatedImports, bool complete, string? reason, string activeTargetFrameworks)
    {
        generatedImports.Save(Path.Combine(scratch, generatedPropsFileName));
        var analyzed = ResolvedGraphClassifier.Analyze(Path.Combine(scratch, "obj", "project.assets.json"), scratch);
        var entries = analyzed.Entries.Where(entry =>
            entry.Context == SurfaceContextKind.Target &&
            entry.PackageId.Equals("KeelMatrix.Phase0.BuildProps", StringComparison.OrdinalIgnoreCase) &&
            entry.PackageRelativePath.Equals(buildPropsPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        var actualActive = entries.Where(entry => entry.Active).Select(entry => entry.TargetFramework!).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var expectedActive = activeTargetFrameworks.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Console.WriteLine($"condition-case {scenario}: complete={analyzed.IsComplete}; active={string.Join(',', actualActive.Order(StringComparer.OrdinalIgnoreCase))}");
        if (analyzed.IsComplete != complete || !actualActive.SetEquals(expectedActive) ||
            (reason is not null && !analyzed.IncompleteReasons.Any(message => message.Contains(reason, StringComparison.OrdinalIgnoreCase))))
        {
            throw new InvalidOperationException($"Generated import condition regression failed for {scenario}: complete={analyzed.IsComplete}, active={string.Join(",", actualActive)}, reasons={string.Join("; ", analyzed.IncompleteReasons)}");
        }
    }

    void AssertProject(string scenario, XDocument generatedImports, bool complete, bool active, string relativePath)
    {
        generatedImports.Save(Path.Combine(scratch, generatedTargetsFileName));
        var analyzed = ResolvedGraphClassifier.Analyze(Path.Combine(scratch, "obj", "project.assets.json"), scratch);
        var entries = analyzed.Entries.Where(entry =>
            entry.Context == SurfaceContextKind.Project &&
            entry.PackageId.Equals("KeelMatrix.Phase0.BuildMultiTargeting", StringComparison.OrdinalIgnoreCase) &&
            entry.PackageRelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)).ToArray();
        Console.WriteLine($"condition-case {scenario}: complete={analyzed.IsComplete}; active={entries.FirstOrDefault()?.Active}");
        if (analyzed.IsComplete != complete || entries.Length != 1 || entries[0].Active != active)
        {
            throw new InvalidOperationException($"Project import condition regression failed for {scenario}: complete={analyzed.IsComplete}, active={entries.FirstOrDefault()?.Active}, reasons={string.Join("; ", analyzed.IncompleteReasons)}");
        }
    }

    void AssertTargetExitCode(string projectRoot, int expected)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "KeelMatrix.PackageSurface.Probe.dll"));
        startInfo.ArgumentList.Add(Path.Combine(projectRoot, "obj", "project.assets.json"));
        startInfo.ArgumentList.Add(projectRoot);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Could not start the probe process.");
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != expected)
        {
            throw new InvalidOperationException($"Incomplete generated-import analysis exited {process.ExitCode}, expected {expected}.");
        }
        Console.WriteLine($"condition-exit incomplete analysis: {process.ExitCode}");
    }
}

static void RunGeneratedImportFileIdentityRegression(string baselineAssets)
{
    using var document = JsonDocument.Parse(File.ReadAllText(baselineAssets));
    var root = document.RootElement;
    var projectPath = root.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString()!;
    var normalizedProjectPath = projectPath.Replace('\\', '/');
    var projectFileName = normalizedProjectPath[(normalizedProjectPath.LastIndexOf('/') + 1)..];
    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-generated-import-identity-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(scratch, "obj"));

    try
    {
        File.Copy(baselineAssets, Path.Combine(scratch, "obj", "project.assets.json"));
        File.WriteAllText(Path.Combine(scratch, "obj", "Other.csproj.nuget.g.props"), "<Project />");

        var analyzed = ResolvedGraphClassifier.Analyze(Path.Combine(scratch, "obj", "project.assets.json"), scratch, strictContent: false);
        if (analyzed.IsComplete || !analyzed.IncompleteReasons.Any(reason => reason.Contains("required generated NuGet import evidence is missing", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"Generated import filename mismatch was treated as complete; expected {projectFileName}.nuget.g.props evidence. Reasons: {string.Join("; ", analyzed.IncompleteReasons)}");
        }

        Console.WriteLine("generated-import filename mismatch: incomplete");
    }
    finally
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}

static void RunGeneratedImportPhaseCrossWireRegression(string baselineAssets)
{
    using var document = JsonDocument.Parse(File.ReadAllText(baselineAssets));
    var root = document.RootElement;
    var projectPath = root.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString()!;
    var normalizedProjectPath = projectPath.Replace('\\', '/');
    var projectFileName = normalizedProjectPath[(normalizedProjectPath.LastIndexOf('/') + 1)..];
    var packageFolder = root.GetProperty("packageFolders").EnumerateObject().Select(property => property.Name).First();
    var scenarios = new[]
    {
        ("BuildProps", "KeelMatrix.Phase0.BuildProps/1.0.0", "build/KeelMatrix.Phase0.BuildProps.props"),
        ("BuildProps TFM", "KeelMatrix.Phase0.BuildProps/1.0.0", "build/net9.0/KeelMatrix.Phase0.BuildProps.props"),
        ("BuildTargets", "KeelMatrix.Phase0.BuildTargets/1.0.0", "build/KeelMatrix.Phase0.BuildTargets.targets"),
        ("BuildTargets TFM", "KeelMatrix.Phase0.BuildTargets/1.0.0", "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets"),
        ("BuildTransitive props", "KeelMatrix.Phase0.BuildTransitive/1.0.0", "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props"),
        ("BuildTransitive props TFM", "KeelMatrix.Phase0.BuildTransitive/1.0.0", "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props"),
        ("BuildTransitive targets", "KeelMatrix.Phase0.BuildTransitive/1.0.0", "buildTransitive/KeelMatrix.Phase0.BuildTransitive.targets"),
        ("BuildTransitive targets TFM", "KeelMatrix.Phase0.BuildTransitive/1.0.0", "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets"),
        ("BuildMultiTargeting props", "KeelMatrix.Phase0.BuildMultiTargeting/1.0.0", "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.props"),
        ("BuildMultiTargeting props TFM", "KeelMatrix.Phase0.BuildMultiTargeting/1.0.0", "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.props"),
        ("BuildMultiTargeting targets", "KeelMatrix.Phase0.BuildMultiTargeting/1.0.0", "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets"),
        ("BuildMultiTargeting targets TFM", "KeelMatrix.Phase0.BuildMultiTargeting/1.0.0", "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets")
    };

    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-generated-import-phase-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(Path.Combine(scratch, "obj"));
    try
    {
        File.Copy(baselineAssets, Path.Combine(scratch, "obj", "project.assets.json"));
        var generatedProps = Path.Combine(scratch, projectFileName + ".nuget.g.props");
        var generatedTargets = Path.Combine(scratch, projectFileName + ".nuget.g.targets");
        foreach (var (label, libraryKey, relativePath) in scenarios)
        {
            if (!root.GetProperty("libraries").TryGetProperty(libraryKey, out var library))
            {
                throw new InvalidOperationException($"Cross-wire scenario {label} is missing library {libraryKey}.");
            }

            var packageRoot = Path.Combine(packageFolder, library.GetProperty("path").GetString()!.Replace('/', Path.DirectorySeparatorChar));
            var assetPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(assetPath))
            {
                throw new InvalidOperationException($"Cross-wire scenario {label} is missing asset {relativePath}.");
            }

            var importPath = System.Security.SecurityElement.Escape(assetPath) ?? assetPath;
            var assetExtension = Path.GetExtension(relativePath);
            var wrongPhaseFile = assetExtension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? generatedTargets : generatedProps;
            var correctPhaseFile = assetExtension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? generatedProps : generatedTargets;
            File.WriteAllText(correctPhaseFile, "<Project />");
            File.WriteAllText(wrongPhaseFile, $"<Project><Import Project=\"{importPath}\" /></Project>");

            var analyzed = ResolvedGraphClassifier.Analyze(Path.Combine(scratch, "obj", "project.assets.json"), scratch, strictContent: false);
            var entries = analyzed.Entries.Where(entry =>
                entry.PackageId.Equals(libraryKey.Split('/')[0], StringComparison.OrdinalIgnoreCase) &&
                entry.PackageRelativePath.Equals(relativePath, StringComparison.OrdinalIgnoreCase)).ToArray();
            var tfmSpecific = relativePath.Contains("/net9.0/", StringComparison.OrdinalIgnoreCase);
            var missingIncompleteEvidence = !tfmSpecific && !entries.Any(entry => entry.Incomplete);
            if (entries.Length == 0 || entries.Any(entry => entry.Active) || missingIncompleteEvidence)
            {
                throw new InvalidOperationException($"Cross-wire phase regression failed for {label}: entries={entries.Length}, complete={analyzed.IsComplete}, active={string.Join(',', entries.Select(entry => entry.Active))}, incomplete={string.Join(',', entries.Select(entry => entry.Incomplete))}, reasons={string.Join("; ", analyzed.IncompleteReasons)}");
            }

            Console.WriteLine($"generated-import cross-wire {label}: inactive/{(tfmSpecific ? "unreachable-variant" : "incomplete")}");
        }
    }
    finally
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}
