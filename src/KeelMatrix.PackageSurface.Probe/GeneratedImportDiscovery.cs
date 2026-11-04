using System.Text.Json;
using System.Xml;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.MsBuildXmlSchema;
using static KeelMatrix.PackageSurface.Probe.NestedImportTraversal;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;
using static KeelMatrix.PackageSurface.Probe.RestoreLibraryMetadata;
using static KeelMatrix.PackageSurface.Probe.StaticImportCondition;

namespace KeelMatrix.PackageSurface.Probe;

internal static class GeneratedImportDiscovery
{
    internal static readonly string[] GeneratedImportElementNames = { "Import" };

    internal static readonly string[] GeneratedImportAttributeNames = { "Condition", "Include", "Project", "ToolsVersion" };

    internal static void ValidateGeneratedImportEvidence(
        IReadOnlyList<GeneratedImport> imports,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, ReachableGraph> reachablePackagesByTarget,
        Dictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        foreach (var import in imports)
        {
            if (!import.Applicability.IsKnown) continue;

            var project = NormalizeText(import.Project);
            PackageIdentity? match;
            string? relative = null;
            if (TryGetNuGetPackageRootSuffix(project, out var suffix))
            {
                if (restoreIdentities.TryGetPackageByImportSuffix(suffix, out var packageIdentity, out var packageRelativePath))
                {
                    match = packageIdentity.Identity;
                    relative = packageRelativePath;
                }
                else
                {
                    match = null;
                }
            }
            else if (import.PackageRoot is not null)
            {
                match = restoreIdentities.TryGetPackageByRoot(import.PackageRoot, out var packageIdentity)
                    ? packageIdentity.Identity
                    : null;
            }
            else
            {
                continue;
            }

            if (match is null) continue;
            if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, match, out _, out var library))
            {
                incomplete.Add("Generated NuGet import evidence refers to an unrepresented package library.");
                continue;
            }

            var files = packageRoots.TryGetValue(match.CanonicalKey, out var packageRoot) && packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
                ? inventory
                : ReadLibraryFiles(library, match.CanonicalKey, incomplete, budget).ToHashSet(GetPackageAssetStringComparer(match.CanonicalKey));
            if (relative is not null && !files.Contains(relative))
            {
                incomplete.Add("Generated NuGet import evidence refers to a file absent from the resolved package inventory.");
                continue;
            }

            var targetGraphs = targets.EnumerateObject()
                .Where(target => restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) &&
                    (import.IsNested && import.Capability == CapabilityKind.BuildMultiTargeting
                        ? import.Applicability.AppliesTo(SurfaceContextKind.Project, string.Empty)
                        : import.Applicability.AppliesTo(SurfaceContextKind.Target, targetIdentity.TargetFramework)))
                .ToArray();
            if (targetGraphs.Length == 0) continue;

            foreach (var target in targetGraphs)
            {
                if (target.Value.ValueKind != JsonValueKind.Object ||
                    !restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ||
                    !reachablePackagesByTarget.TryGetValue(targetIdentity.CanonicalKey, out var closure) ||
                    !closure.ReachablePackageKeys.Contains(match.CanonicalKey))
                {
                    incomplete.Add("Generated NuGet import evidence refers to a package that is not reachable from an applicable project/direct root.");
                    break;
                }
            }
        }
    }

    internal static List<GeneratedImport> ReadGeneratedImports(
        JsonElement root,
        string assetsFile,
        string projectRoot,
        string? selectedProjectPath,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        IReadOnlySet<string> targetFrameworkContexts,
        List<string> incomplete,
        AnalysisBudget budget,
        HashSet<string> expectedGeneratedImportSources)
    {
        var result = new List<GeneratedImport>();
        foreach (var expectedSource in GetExpectedGeneratedImportSources(root, projectRoot, selectedProjectPath, incomplete))
        {
            expectedGeneratedImportSources.Add(expectedSource);
        }

        var generatedDirectory = Path.GetDirectoryName(Path.GetFullPath(assetsFile))!;
        var files = expectedGeneratedImportSources
            .Select(source => Path.Combine(generatedDirectory, source))
            .ToList();
        if (files.Count > MaxGeneratedImportFiles)
        {
            incomplete.Add("The project contains too many generated NuGet import files.");
            files = files.Take(MaxGeneratedImportFiles).ToList();
        }

        var visitedImports = new HashSet<string>(FileSystemPathComparer);

        foreach (var requestedFile in files.Distinct(FileSystemPathComparer).Order(StringComparer.Ordinal))
        {
            if (!TryResolveExistingFilePath(requestedFile, out var file))
            {
                incomplete.Add($"Expected generated import file {Path.GetFileName(requestedFile)} is missing from the assets directory.");
                continue;
            }

            if (!file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                EnsureFileWithinLimit(file, MaxMetadataFileBytes, "generated NuGet import");
                budget.Add(FileLength(file), "generated-import metadata");
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                    MaxCharactersInDocument = MaxXmlCharacters,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };
                using var reader = XmlReader.Create(file, settings);
                var xmlContext = new MsBuildXmlContext();
                var exceededDepth = false;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        xmlContext.Exit(reader);
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Depth > MaxXmlDepth)
                    {
                        exceededDepth = true;
                        break;
                    }

                    var description = $"Generated import file {Path.GetFileName(file)}";
                    if (!xmlContext.TryEnter(reader, description, incomplete)) continue;
                    if (HasCaseInsensitiveDuplicateXmlAttributes(reader))
                    {
                        incomplete.Add($"{description} contains duplicate or case-variant attributes.");
                    }

                    var elementName = reader.LocalName;
                    ValidateConsumedXmlElementSpelling(reader, GeneratedImportElementNames, description, incomplete);
                    ValidateConsumedXmlAttributeSpellings(reader, GeneratedImportAttributeNames, description, incomplete);
                    if (elementName.Equals("ImportGroup", StringComparison.Ordinal) && !IsDirectProjectChild(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete, description);
                        continue;
                    }

                    if (!elementName.Equals("Import", StringComparison.Ordinal)) continue;
                    if (!IsSupportedImportLocation(xmlContext) || reader.GetAttribute("Project") is not string project)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete, description);
                        continue;
                    }

                    budget.AddImportedEdge();
                    var conditions = xmlContext.Elements
                        .Where(element => element.Name is "Project" or "ImportGroup")
                        .Select(element => new ConditionClause(element.Name, element.Condition))
                        .Append(new ConditionClause(elementName, reader.GetAttribute("Condition")))
                        .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                        .ToArray();
                    var applicability = DetermineApplicability(conditions, ImportExistsCondition.ForImport(project, restoreIdentities, packageRoots), out var reason);
                    if (!applicability.IsKnown)
                    {
                        incomplete.Add($"{description} has an unsupported condition on {reason!.Owner}: {reason.Message}.");
                    }

                    result.Add(new GeneratedImport(Path.GetFileName(file), project, applicability));
                }

                if (exceededDepth)
                {
                    incomplete.Add($"Generated import file {Path.GetFileName(file)} exceeds the supported XML depth.");
                }

                foreach (var directImport in result.Where(import => string.Equals(import.SourceFile, Path.GetFileName(file), FileSystemComparisonContext.Current.ProjectStringComparison) && !import.IsNested).ToArray())
                {
                    string? resolutionReason = null;
                    if (directImport.Applicability.IsKnown &&
                        TryResolveStaticPackageImport(directImport.Project, file, restoreIdentities, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason))
                    {
                        var rootAssetPath = Path.GetRelativePath(nestedRoot, nestedPath).Replace(Path.DirectorySeparatorChar, '/');
                        if (!TryGetCapability(rootAssetPath, out var rootCapability))
                        {
                            incomplete.Add("Generated NuGet import evidence refers to an unsupported package build asset.");
                            continue;
                        }

                        if (!IsDeclaredPackageFile(nestedRoot, rootAssetPath, packageInventories))
                        {
                            incomplete.Add("Generated NuGet import evidence refers to a file absent from the resolved package inventory.");
                            continue;
                        }

                        WalkNestedImports(nestedPath, nestedRoot, directImport.SourceFile, directImport.Applicability.Condition, rootCapability, rootAssetPath, restoreIdentities, packageRoots, packageInventories, libraries, targetFrameworkContexts, result, incomplete, budget, visitedImports, 0);
                    }
                    else if (resolutionReason is not null)
                    {
                        incomplete.Add($"Generated import file {Path.GetFileName(file)} contains an unsupported static import target.");
                    }
                }
            }
            catch (XmlException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is malformed.");
            }
            catch (InvalidOperationException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is unsupported.");
            }
            catch (IOException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} could not be read.");
            }
        }

        return result;
    }

    internal static bool IsExpectedGeneratedImportSource(
        string sourceFile,
        string relativePath,
        IReadOnlySet<string> expectedGeneratedImportSources)
    {
        var assetExtension = Path.GetExtension(relativePath);
        if (!assetExtension.Equals(".props", StringComparison.OrdinalIgnoreCase) &&
            !assetExtension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedGeneratedImportSuffix = ".nuget.g" + assetExtension;
        if (!sourceFile.EndsWith(expectedGeneratedImportSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return expectedGeneratedImportSources.Count == 0 || expectedGeneratedImportSources.Contains(sourceFile);
    }

    internal static bool IsNestedOrExpectedGeneratedImportSource(
        GeneratedImport import,
        string relativePath,
        IReadOnlySet<string> expectedGeneratedImportSources) =>
        import.IsNested || IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources);

    internal static IEnumerable<string> GetExpectedGeneratedImportSources(JsonElement root, string projectRoot, string? selectedProjectPath, List<string> incomplete)
    {
        string? projectPath = null;
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("projectPath", out var restoredPath) && restoredPath.ValueKind == JsonValueKind.String)
        {
            projectPath = restoredPath.GetString();
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            incomplete.Add("Restore metadata does not identify the generated-import project.");
            yield break;
        }

        if (selectedProjectPath is not null && !PathsEqual(projectPath, selectedProjectPath))
        {
            incomplete.Add("Restore metadata identifies a different project than the generated-import input.");
            yield break;
        }

        var normalizedPath = projectPath.Replace('\\', '/');
        var fileName = normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];
        if (fileName.Length == 0)
        {
            yield break;
        }

        yield return fileName + ".nuget.g.props";
        yield return fileName + ".nuget.g.targets";
    }

    internal static bool ContainsAsset(JsonElement targetAssets, string propertyName, string assetName, List<string> incomplete, string libraryKey)
    {
        if (!targetAssets.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{libraryKey}: target asset group '{propertyName}' has an unsupported shape.");
            return false;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"{libraryKey}: target asset group '{propertyName}' has malformed metadata.");
                continue;
            }

            if (string.Equals(property.Name.Replace('\\', '/'), assetName, GetPackageAssetComparison(libraryKey)))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool ContainsBuildAsset(JsonElement targetAssets, CapabilityKind capability, string assetName, List<string> incomplete, string libraryKey)
    {
        var propertyName = capability switch
        {
            CapabilityKind.BuildProps => "build",
            CapabilityKind.BuildTargets => "build",
            CapabilityKind.BuildTransitive => "buildTransitive",
            CapabilityKind.BuildMultiTargeting => "buildMultiTargeting",
            _ => string.Empty
        };
        if (propertyName.Length == 0) return false;
        if (ContainsAsset(targetAssets, propertyName, assetName, incomplete, libraryKey)) return true;
        return capability == CapabilityKind.BuildTransitive &&
            ContainsAsset(targetAssets, "build", assetName, incomplete, libraryKey);
    }

    internal static bool RequiresGeneratedImportEvidence(CapabilityKind capability, string assetName, bool isMultiTargetingProject)
    {
        if (capability == CapabilityKind.BuildMultiTargeting && !isMultiTargetingProject) return false;
        return true;
    }
}
