using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.GeneratedImportAnalyzer;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class PackageGraphAnalyzer
{


    // One graph representation is shared by inventory, capability, import, and analyzer
    // stages. The sets have deliberately different meanings: traversal roots
    // are graph nodes we may start from, reachable nodes include projects and
    // packages, and reachable package keys are the package-only asset view.
    internal sealed record ReachableGraph(
        HashSet<string> TraversalRootKeys,
        HashSet<string> ReachableNodeKeys,
        HashSet<string> ReachablePackageKeys,
        HashSet<string> DirectPackageKeys,
        HashSet<string> AnalyzerTraversalRootKeys,
        HashSet<string> AnalyzerPackageKeys);


    internal sealed record RestoreGraphSnapshot(
        Dictionary<string, Dictionary<string, PackageAssetRule>> DirectAssetRules,
        Dictionary<string, ReachableGraph> ReachablePackagesByTarget);


    internal sealed record ResolvedTargetPackage(
        string LibraryKey,
        RestorePackageIdentity Identity,
        JsonElement Library,
        JsonElement TargetAssets,
        string PackageRoot,
        bool IsDirect);


    internal sealed record ResolvedTarget(
        RestoreTargetIdentity Identity,
        JsonElement TargetAssets,
        IReadOnlyList<ResolvedTargetPackage> Packages,
        HashSet<string> DirectPackageKeys,
        HashSet<string> AnalyzerPackageKeys);


    internal sealed record ResolvedTargets(
        IReadOnlyList<ResolvedTarget> Targets,
        HashSet<PackageIdentity> ResolvedPackages);


    internal static RestoreGraphSnapshot AnalyzeRestoreGraph(
        RestoreGraphMetadata metadata,
        string projectRoot,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var directAssetRules = ReadDirectPackageAssetRules(metadata.Root, metadata.Identities, incomplete);
        var reachablePackagesByTarget = BuildReachablePackageClosures(
            metadata.Root,
            projectRoot,
            metadata.Targets,
            metadata.Libraries,
            metadata.Identities,
            directAssetRules,
            incomplete,
            budget);
        return new RestoreGraphSnapshot(directAssetRules, reachablePackagesByTarget);
    }


    internal static ResolvedTargets ResolveTargetPackages(
        RestoreGraphMetadata metadata,
        RestoreGraphSnapshot graph,
        IReadOnlyDictionary<string, string> packageRoots,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var targets = new List<ResolvedTarget>();
        var resolvedPackages = new HashSet<PackageIdentity>(PackageIdentityComparer.Instance);
        foreach (var target in metadata.Targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target {target.Name} is not an object.");
            }

            if (!metadata.Identities.TryGetTarget(target.Name, out var targetIdentity))
            {
                incomplete.Add($"Restore evidence contains an unresolved target identity: {target.Name}.");
                continue;
            }

            var closure = graph.ReachablePackagesByTarget[targetIdentity.CanonicalKey];
            var packages = new List<ResolvedTargetPackage>();
            foreach (var package in target.Value.EnumerateObject())
            {
                if (!metadata.Identities.TryGetLibrary(metadata.Libraries, package.Name, out var packageIdentity, out var library))
                {
                    incomplete.Add($"Target {target.Name} refers to missing library {package.Name}.");
                    continue;
                }

                var libraryKey = packageIdentity.CanonicalKey;
                if (library.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String &&
                    typeElement.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase))
                {
                    ReadProjectDependencies(package.Value, target.Value, libraryKey, metadata.Identities, incomplete, budget);
                    continue;
                }

                if (!library.TryGetProperty("type", out typeElement) || typeElement.ValueKind != JsonValueKind.String ||
                    !typeElement.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase))
                {
                    incomplete.Add($"Library {libraryKey} has an unsupported library type.");
                    continue;
                }

                if (!closure.ReachablePackageKeys.Contains(libraryKey))
                {
                    incomplete.Add($"Target {target.Name} contains a package that is not reachable from a project or direct root.");
                    continue;
                }

                resolvedPackages.Add(packageIdentity.Identity);
                if (!library.TryGetProperty("path", out var pathElement))
                {
                    incomplete.Add($"Library {libraryKey} has no package path.");
                    continue;
                }

                if (pathElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pathElement.GetString()))
                {
                    incomplete.Add($"Library {libraryKey} has an invalid package path.");
                    continue;
                }

                if (!packageRoots.TryGetValue(libraryKey, out var packageRoot))
                {
                    continue;
                }

                packages.Add(new ResolvedTargetPackage(
                    libraryKey,
                    packageIdentity,
                    library,
                    package.Value,
                    packageRoot,
                    closure.DirectPackageKeys.Contains(libraryKey)));
            }

            targets.Add(new ResolvedTarget(
                targetIdentity,
                target.Value,
                packages,
                closure.DirectPackageKeys,
                closure.AnalyzerPackageKeys));
        }

        return new ResolvedTargets(targets, resolvedPackages);
    }



    internal static void ReadProjectDependencies(
        JsonElement projectNode,
        JsonElement targetAssets,
        string projectKey,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (!projectNode.TryGetProperty("dependencies", out var dependencies)) return;
        if (dependencies.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Project node {projectKey} has invalid dependency metadata.");
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            budget.AddDependencyNode();
            if (dependency.Value.ValueKind != JsonValueKind.String ||
                !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
            {
                incomplete.Add($"Project node {projectKey} has malformed dependency value {dependency.Name}.");
                continue;
            }

            if (!TryFindMatchingTargetPackage(targetAssets, restoreIdentities, dependency.Name, requirement, out _))
            {
                incomplete.Add($"Project node {projectKey} refers to an unresolved package requirement {dependency.Name}.");
            }
        }
    }



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



    internal static bool TryReadDependencyRequirement(JsonElement value, out PackageVersionRange requirement)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return TryParsePackageVersionRange(value.GetString(), out requirement);
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.String)
        {
            return TryParsePackageVersionRange(version.GetString(), out requirement);
        }

        requirement = null!;
        return false;
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



    internal static Dictionary<string, ReachableGraph> BuildReachablePackageClosures(
        JsonElement root,
        string projectRoot,
        JsonElement targets,
        JsonElement libraries,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, Dictionary<string, PackageAssetRule>> directAssetRules,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var result = new Dictionary<string, ReachableGraph>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object || !restoreIdentities.TryGetTarget(target.Name, out var targetIdentity))
            {
                continue;
            }

            var packagesById = target.Value.EnumerateObject()
                .Where(package => restoreIdentities.TryGetPackage(package.Name, out _))
                .Select(package => (Property: package, Identity: GetPackageIdentity(restoreIdentities, package.Name)))
                .GroupBy(value => value.Identity.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var packageGroup in packagesById)
            {
                var versions = packageGroup.Value
                    .Select(value => value.Identity.CanonicalKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (versions.Length > 1)
                {
                    incomplete.Add($"Target {target.Name} contains multiple resolved versions for package {packageGroup.Key}.");
                }
            }

            if (incomplete.Count > 0)
            {
                continue;
            }

            var work = new Stack<(string SourceKey, int Depth)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var traversalRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reachableNodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reachablePackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var analyzerTraversalRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directRules = directAssetRules.TryGetValue(targetIdentity.FrameworkKey, out var rules)
                ? rules
                : new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
            var projectReferenceAnalyzerRules = ReadProjectReferenceAnalyzerRules(root, projectRoot, targetIdentity.FrameworkKey, target.Value, libraries, restoreIdentities, incomplete);

            foreach (var direct in directRules)
            {
                if (TryFindMatchingPackage(packagesById, direct.Key, direct.Value.Requirement, out var match, incomplete,
                        $"direct dependency {direct.Key}"))
                {
                    work.Push((match.Property.Name, 0));
                    traversalRoots.Add(match.Property.Name);
                    directPackages.Add(match.Identity.CanonicalKey);
                    if (direct.Value.AnalyzersIncluded) analyzerTraversalRoots.Add(match.Property.Name);
                }
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (restoreIdentities.TryGetLibrary(libraries, package.Name, out _, out var library) &&
                    library.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase))
                {
                    work.Push((package.Name, 0));
                    traversalRoots.Add(package.Name);
                    if (!projectReferenceAnalyzerRules.TryGetValue(package.Name, out var analyzersIncluded) || analyzersIncluded)
                    {
                        analyzerTraversalRoots.Add(package.Name);
                    }
                }
            }

            while (work.Count > 0)
            {
                var (sourceKey, depth) = work.Pop();
                if (!visited.Add(sourceKey)) continue;
                reachableNodes.Add(sourceKey);
                budget.AddDependencyNode();
                if (depth > MaxDependencyDepth || visited.Count > MaxDependencyNodes)
                {
                    throw new InvalidDataException("The resolved dependency graph exceeds the supported traversal limits.");
                }

                if (!restoreIdentities.TryGetPackage(sourceKey, out var packageIdentity) ||
                    !TryGetPropertyIgnoreCase(target.Value, sourceKey, out var packageNode) || packageNode.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target {target.Name} contains an unresolved package root.");
                    continue;
                }

                if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, packageIdentity.Identity, out _, out var library) ||
                    !library.TryGetProperty("type", out var libraryType) || libraryType.ValueKind != JsonValueKind.String)
                {
                    incomplete.Add($"Target {target.Name} contains a package without valid library metadata.");
                    continue;
                }

                var libraryTypeName = libraryType.GetString()!;
                var isProject = libraryTypeName.Equals("project", StringComparison.OrdinalIgnoreCase);
                var isPackage = libraryTypeName.Equals("package", StringComparison.OrdinalIgnoreCase);
                if (!isProject && !isPackage)
                {
                    incomplete.Add($"Target {target.Name} contains an unsupported library type.");
                    continue;
                }

                if (isPackage) reachablePackages.Add(packageIdentity.CanonicalKey);
                if (!packageNode.TryGetProperty("dependencies", out var dependencies)) continue;
                if (dependencies.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageIdentity.CanonicalKey} has malformed dependency metadata.");
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
                    {
                        incomplete.Add($"Target package {packageIdentity.CanonicalKey} has an invalid dependency range for {dependency.Name}.");
                        continue;
                    }

                    if (TryFindMatchingPackage(packagesById, dependency.Name, requirement, out var dependencyPackage, incomplete,
                            $"dependency {dependency.Name} of {packageIdentity.CanonicalKey}"))
                    {
                        work.Push((dependencyPackage.Property.Name, depth + 1));
                    }
                }
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var identity, out var library) ||
                    !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase)) continue;
                if (!reachablePackages.Contains(identity.CanonicalKey))
                {
                    incomplete.Add($"Target {target.Name} contains a package that is not reachable from a project or direct root.");
                }
            }

            var graph = new ReachableGraph(traversalRoots, reachableNodes, reachablePackages, directPackages, analyzerTraversalRoots, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var analyzerPackages = DetermineAnalyzerPackages(target.Value, directRules, restoreIdentities, graph, packagesById, budget, incomplete);
            result[targetIdentity.CanonicalKey] = graph with { AnalyzerPackageKeys = analyzerPackages };
        }

        return result;
    }



    internal static bool TryFindMatchingPackage(
        IReadOnlyDictionary<string, (JsonProperty Property, RestorePackageIdentity Identity)[]> packagesById,
        string packageId,
        PackageVersionRange requirement,
        out (JsonProperty Property, RestorePackageIdentity Identity) match,
        List<string> incomplete,
        string context)
    {
        match = default;
        if (!packagesById.TryGetValue(packageId, out var candidates))
        {
            incomplete.Add($"The resolved graph has no package matching {context}.");
            return false;
        }

        var matching = candidates.Where(candidate => requirement.Matches(candidate.Identity.Version)).ToArray();
        if (matching.Length != 1)
        {
            incomplete.Add(matching.Length == 0
                ? $"The resolved graph has no package version matching {context}."
                : $"The resolved graph has multiple package versions matching {context}.");
            return false;
        }

        match = matching[0];
        return true;
    }



    internal static RestorePackageIdentity GetPackageIdentity(RestoreIdentityIndex restoreIdentities, string sourceKey)
    {
        if (!restoreIdentities.TryGetPackage(sourceKey, out var identity))
        {
            throw new InvalidDataException("A target package has an invalid identity.");
        }

        return identity;
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




    internal static IReadOnlyList<string> GetReachablePackageInputPaths(string assetsFile)
    {
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var root = document.RootElement;
            var incomplete = new List<string>();
            ValidateRestoreJsonStructure(root, incomplete);
            if (incomplete.Count > 0) return Array.Empty<string>();
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            var targets = root.TryGetProperty("targets", out var targetsElement)
                ? targetsElement
                : throw new InvalidDataException("project.assets.json has no targets object.");
            if (libraries.ValueKind != JsonValueKind.Object || targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("project.assets.json has invalid package input maps.");
            }

            var restoreIdentities = BuildRestoreIdentityIndex(root, libraries, targets, incomplete);
            if (incomplete.Count > 0) return Array.Empty<string>();

            var budget = new AnalysisBudget();
            var directAssetRules = ReadDirectPackageAssetRules(root, restoreIdentities, incomplete);
            var inputProjectRoot = Directory.GetParent(Path.GetDirectoryName(assetsFile) ?? string.Empty)?.FullName ?? Path.GetDirectoryName(assetsFile)!;
            var reachablePackagesByTarget = BuildReachablePackageClosures(root, inputProjectRoot, targets, libraries, restoreIdentities, directAssetRules, incomplete, budget);
            if (incomplete.Count > 0) return Array.Empty<string>();
            var reachablePackageKeys = reachablePackagesByTarget.Values
                .SelectMany(value => value.ReachablePackageKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageRoots = ResolvePackageRoots(restoreIdentities, libraries, packageFolders, incomplete, reachablePackageKeys);
            FileSystemComparisonContext.Current.RegisterPackageRoots(null, packageRoots);
            var packageInventories = BuildPackageInventories(packageRoots, restoreIdentities, libraries, incomplete, budget);
            if (incomplete.Count > 0) return Array.Empty<string>();

            var result = new HashSet<string>(FileSystemPathComparer);
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var identity, out var library) ||
                        !reachablePackagesByTarget.TryGetValue(restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ? targetIdentity.CanonicalKey : string.Empty, out var closure) ||
                        !closure.ReachablePackageKeys.Contains(identity.CanonicalKey) ||
                        !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                        !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                        !packageRoots.TryGetValue(identity.CanonicalKey, out var packageRoot) ||
                        !packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory))
                    {
                        continue;
                    }

                    foreach (var relativePath in inventory)
                    {
                        result.Add(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                    }
                }
            }

            return result.Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return Array.Empty<string>();
        }
    }

}
