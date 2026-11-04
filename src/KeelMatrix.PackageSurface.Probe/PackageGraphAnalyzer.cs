using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.AnalyzerPropagation;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.DependencyRequirements;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.ResolvedPackageResolver;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;
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

            var packagesById = IndexTargetPackages(target.Value, target.Name, restoreIdentities, incomplete);

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
}
