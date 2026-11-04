using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.DependencyRequirements;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class AnalyzerPropagation
{
    internal static Dictionary<string, Dictionary<string, PackageAssetRule>> ReadDirectPackageAssetRules(
        JsonElement root,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete)
    {
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has no frameworks object.");
        }

        var result = new Dictionary<string, Dictionary<string, PackageAssetRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Framework {framework.Name} is not an object.");
            }

            if (!framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                continue;
            }

            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Framework {framework.Name} has an invalid dependencies object.");
            }

            if (!restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity))
            {
                throw new InvalidDataException($"Framework {framework.Name} has no canonical restore identity.");
            }

            var frameworkRules = new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (!TryReadDependencyRequirement(dependency.Value, out var requirement))
                {
                    incomplete.Add($"Dependency {dependency.Name} has an invalid version range.");
                    continue;
                }

                frameworkRules[dependency.Name] = new PackageAssetRule(
                    requirement,
                    dependency.Value.ValueKind != JsonValueKind.Object || ReadAnalyzersIncluded(dependency.Value, dependency.Name));
            }
            result[frameworkIdentity.CanonicalKey] = frameworkRules;
        }

        return result;
    }

    internal static bool ReadAnalyzersIncluded(JsonElement dependency, string packageId)
    {
        var included = true;
        if (dependency.TryGetProperty("include", out var include))
        {
            if (include.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Dependency {packageId} has a non-string include selector.");
            }

            included = AssetSelectorContains(include.GetString(), "all") || AssetSelectorContains(include.GetString(), "analyzers");
        }

        if (dependency.TryGetProperty("exclude", out var exclude))
        {
            if (exclude.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Dependency {packageId} has a non-string exclude selector.");
            }

            if (AssetSelectorContains(exclude.GetString(), "all") || AssetSelectorContains(exclude.GetString(), "analyzers"))
            {
                included = false;
            }
        }

        return included;
    }

    internal static bool AssetSelectorContains(string? selector, string expected) =>
        selector?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase)) == true;

    internal static Dictionary<string, bool> ReadProjectReferenceAnalyzerRules(
        JsonElement root,
        string projectRoot,
        string targetFrameworkKey,
        JsonElement targetAssets,
        JsonElement libraries,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete)
    {
        var result = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("restore", out var restore) || restore.ValueKind != JsonValueKind.Object ||
            !restore.TryGetProperty("frameworks", out var restoreFrameworks) || restoreFrameworks.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        var projectReferences = restoreFrameworks.EnumerateObject()
            .Where(framework => TryGetFrameworkIdentity(framework.Name, out var frameworkIdentity) &&
                frameworkIdentity.Equals(targetFrameworkKey, StringComparison.OrdinalIgnoreCase) &&
                framework.Value.ValueKind == JsonValueKind.Object &&
                framework.Value.TryGetProperty("projectReferences", out var references) && references.ValueKind == JsonValueKind.Object)
            .SelectMany(framework => framework.Value.GetProperty("projectReferences").EnumerateObject())
            .ToArray();
        if (projectReferences.Length == 0) return result;

        var projectNodes = targetAssets.EnumerateObject()
            .Where(package => package.Value.ValueKind == JsonValueKind.Object &&
                restoreIdentities.TryGetLibrary(libraries, package.Name, out _, out var library) &&
                library.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                type.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase) &&
                library.TryGetProperty("msbuildProject", out var msbuildProject) && msbuildProject.ValueKind == JsonValueKind.String)
            .Select(package =>
            {
                restoreIdentities.TryGetLibrary(libraries, package.Name, out _, out var library);
                return (Package: package.Name, Path: library.GetProperty("msbuildProject").GetString());
            })
            .Where(value => value.Path is not null)
            .ToArray();

        foreach (var reference in projectReferences)
        {
            if (reference.Value.ValueKind != JsonValueKind.Object ||
                !reference.Value.TryGetProperty("projectPath", out var projectPath) || projectPath.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var included = ReadProjectReferenceAnalyzersIncluded(reference.Value);
            var requestedPath = NormalizeProjectReferencePath(projectPath.GetString()!, projectRoot);
            foreach (var node in projectNodes)
            {
                var nodePath = NormalizeProjectReferencePath(node.Path!, projectRoot);
                if (FileSystemPathsEqual(requestedPath, nodePath))
                {
                    result[node.Package] = included;
                }
            }
        }

        return result;
    }

    internal static bool ReadProjectReferenceAnalyzersIncluded(JsonElement reference)
    {
        var included = true;
        if (reference.TryGetProperty("includeAssets", out var include))
        {
            included = include.ValueKind != JsonValueKind.String ||
                AssetSelectorContains(include.GetString(), "all") || AssetSelectorContains(include.GetString(), "analyzers");
        }

        if (reference.TryGetProperty("excludeAssets", out var exclude) && exclude.ValueKind == JsonValueKind.String &&
            (AssetSelectorContains(exclude.GetString(), "all") || AssetSelectorContains(exclude.GetString(), "analyzers")))
        {
            included = false;
        }

        if (reference.TryGetProperty("privateAssets", out var privateAssets) && privateAssets.ValueKind == JsonValueKind.String &&
            (AssetSelectorContains(privateAssets.GetString(), "all") || AssetSelectorContains(privateAssets.GetString(), "analyzers")))
        {
            included = false;
        }

        return included;
    }

    internal static string NormalizeProjectReferencePath(string path, string projectRoot)
    {
        try
        {
            var baseDirectory = projectRoot;
            return Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));
        }
        catch (ArgumentException)
        {
            return path.Replace('\\', '/');
        }
        catch (NotSupportedException)
        {
            return path.Replace('\\', '/');
        }
    }

    internal static HashSet<string> DetermineAnalyzerPackages(
        JsonElement targetAssets,
        IReadOnlyDictionary<string, PackageAssetRule> directAssetRules,
        RestoreIdentityIndex restoreIdentities,
        ReachableGraph graph,
        IReadOnlyDictionary<string, (JsonProperty Property, RestorePackageIdentity Identity)[]> packagesById,
        AnalysisBudget budget,
        List<string> incomplete)
    {
        var excludedAnalyzerPackages = directAssetRules
            .Where(rule => !rule.Value.AnalyzersIncluded)
            .Select(rule => rule.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<(string Key, int Depth)>();

        foreach (var direct in directAssetRules)
        {
            if (!direct.Value.AnalyzersIncluded || !packagesById.TryGetValue(direct.Key, out var roots))
            {
                continue;
            }

            foreach (var root in roots.Where(root =>
                         graph.AnalyzerTraversalRootKeys.Contains(root.Property.Name) &&
                         graph.ReachablePackageKeys.Contains(root.Identity.CanonicalKey) &&
                         direct.Value.Requirement.Matches(root.Identity.Version)))
            {
                work.Push((root.Property.Name, 0));
            }
        }

        foreach (var project in targetAssets.EnumerateObject().Where(package =>
                     package.Value.ValueKind == JsonValueKind.Object &&
                     graph.AnalyzerTraversalRootKeys.Contains(package.Name) &&
                     graph.ReachableNodeKeys.Contains(package.Name)))
        {
            work.Push((project.Name, 0));
        }

        while (work.Count > 0)
        {
            var (packageKey, depth) = work.Pop();
            if (!visited.Add(packageKey)) continue;
            budget.AddDependencyNode();
            if (depth > MaxDependencyDepth || visited.Count > MaxDependencyNodes)
            {
                throw new InvalidDataException("The resolved dependency graph exceeds the supported traversal limits.");
            }

            if (!TryGetPropertyIgnoreCase(targetAssets, packageKey, out var package) || package.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} is not an object.");
            }

            if (!restoreIdentities.TryGetPackage(packageKey, out var packageIdentity))
            {
                throw new InvalidDataException($"Target package {packageKey} has no canonical restore identity.");
            }

            var packageId = packageIdentity.Id;
            if (excludedAnalyzerPackages.Contains(packageId)) continue;
            active.Add(packageIdentity.CanonicalKey);
            if (!package.TryGetProperty("dependencies", out var dependencies)) continue;
            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} has an invalid dependencies object.");
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.String ||
                    !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
                {
                    incomplete.Add($"Target package {packageKey} has an invalid dependency range for {dependency.Name}.");
                    continue;
                }

                if (!packagesById.TryGetValue(dependency.Name, out var dependencyCandidates))
                {
                    incomplete.Add($"Target package {packageKey} refers to missing dependency {dependency.Name}.");
                    continue;
                }

                var dependencyMatches = dependencyCandidates
                    .Where(candidate => graph.ReachablePackageKeys.Contains(candidate.Identity.CanonicalKey) &&
                        requirement.Matches(candidate.Identity.Version))
                    .ToArray();
                if (dependencyMatches.Length != 1)
                {
                    incomplete.Add(dependencyMatches.Length == 0
                        ? $"Target package {packageKey} has no reachable dependency version matching {dependency.Name}."
                        : $"Target package {packageKey} has multiple reachable dependency versions matching {dependency.Name}.");
                    continue;
                }

                work.Push((dependencyMatches[0].Property.Name, depth + 1));
            }
        }

        return active;
    }

    internal sealed record PackageAssetRule(PackageVersionRange Requirement, bool AnalyzersIncluded);
}
