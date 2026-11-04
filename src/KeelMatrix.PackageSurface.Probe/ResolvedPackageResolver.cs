using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.DependencyRequirements;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class ResolvedPackageResolver
{
    internal static bool TryFindMatchingPackageInFrameworkTargets(
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        string frameworkKey,
        string packageId,
        PackageVersionRange requirement)
    {
        foreach (var target in targets.EnumerateObject())
        {
            if (!restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ||
                !targetIdentity.FrameworkKey.Equals(frameworkKey, StringComparison.OrdinalIgnoreCase) ||
                target.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryFindMatchingTargetPackage(target.Value, restoreIdentities, packageId, requirement, out _))
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryFindMatchingTargetPackage(
        JsonElement targetAssets,
        RestoreIdentityIndex restoreIdentities,
        string packageId,
        PackageVersionRange requirement,
        out RestorePackageIdentity identity)
    {
        foreach (var package in targetAssets.EnumerateObject())
        {
            if (restoreIdentities.TryGetPackage(package.Name, out var candidate) &&
                candidate.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
                requirement.Matches(candidate.Version))
            {
                identity = candidate;
                return true;
            }
        }

        identity = null!;
        return false;
    }

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
    internal static Dictionary<string, (JsonProperty Property, RestorePackageIdentity Identity)[]> IndexTargetPackages(
        JsonElement targetAssets,
        string targetName,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete)
    {
        var packagesById = targetAssets.EnumerateObject()
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
                incomplete.Add($"Target {targetName} contains multiple resolved versions for package {packageGroup.Key}.");
            }
        }

        return packagesById;
    }
}
