using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.DependencyRequirements;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.ResolvedPackageResolver;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;
using static KeelMatrix.PackageSurface.Probe.RestoreLibraryMetadata;

namespace KeelMatrix.PackageSurface.Probe;

internal static class RestoreConsistencyValidator
{
    internal static void ValidateRestoreEvidence(
        JsonElement root,
        JsonElement libraries,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyList<string> packageFolders,
        IReadOnlyDictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (libraries.ValueKind != JsonValueKind.Object || targets.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore evidence has no declared framework graph.");
            return;
        }

        var declaredFrameworks = restoreIdentities.Frameworks.Keys.ToArray();
        var targetNames = restoreIdentities.Targets.Keys.ToArray();
        if (declaredFrameworks.Length == 0)
        {
            incomplete.Add("Restore evidence declares no frameworks.");
        }

        if (targetNames.Length == 0)
        {
            incomplete.Add("Restore evidence contains no resolved target graphs.");
        }

        foreach (var framework in declaredFrameworks)
        {
            if (!restoreIdentities.Targets.Values.Any(target => target.FrameworkKey.Equals(framework, StringComparison.OrdinalIgnoreCase)))
            {
                incomplete.Add($"Declared framework {framework} has no resolved target graph.");
            }
        }

        foreach (var target in restoreIdentities.Targets.Values)
        {
            if (!declaredFrameworks.Contains(target.FrameworkKey, StringComparer.OrdinalIgnoreCase))
            {
                incomplete.Add($"Resolved target {target.SourceKey} has no declared framework.");
            }
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object) continue;
            if (!restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity))
            {
                incomplete.Add($"Declared framework {framework.Name} has no canonical restore identity.");
                continue;
            }

            if (framework.Value.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Declared framework {framework.Name} has malformed dependency metadata.");
            }
            else if (framework.Value.TryGetProperty("dependencies", out dependencies))
            {
                AddDuplicatePropertyReasons(dependencies, $"declared dependency for {framework.Name}", incomplete);
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (!TryReadDependencyRequirement(dependency.Value, out var requirement) ||
                        !TryFindMatchingPackageInFrameworkTargets(targets, restoreIdentities, frameworkIdentity.CanonicalKey, dependency.Name, requirement))
                    {
                        incomplete.Add($"Declared framework {framework.Name} has an unresolved package requirement {dependency.Name}.");
                    }
                }
            }
        }

        ValidateFormat4DependencyGroups(root, targets, restoreIdentities, incomplete, budget);

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target {target.Name} is not an object.");
                continue;
            }

            if (!restoreIdentities.TryGetTarget(target.Name, out var targetIdentity))
            {
                continue;
            }

            if (!target.Value.EnumerateObject().Any() && frameworks.EnumerateObject()
                .Where(framework => restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity) &&
                    frameworkIdentity.CanonicalKey.Equals(targetIdentity.FrameworkKey, StringComparison.OrdinalIgnoreCase))
                .Any(framework => framework.Value.ValueKind == JsonValueKind.Object &&
                    framework.Value.TryGetProperty("dependencies", out var dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Object && dependencies.EnumerateObject().Any()))
            {
                incomplete.Add($"Resolved target {target.Name} is empty despite declared framework dependencies.");
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var packageIdentity, out var library))
                {
                    incomplete.Add($"Target {target.Name} refers to missing library {package.Name}.");
                    continue;
                }

                if (package.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target {target.Name} package {package.Name} has malformed asset metadata.");
                    continue;
                }

                var libraryType = library.TryGetProperty("type", out var libraryTypeElement) && libraryTypeElement.ValueKind == JsonValueKind.String
                    ? libraryTypeElement.GetString()
                    : null;
                if (libraryType is not ("package" or "project"))
                {
                    incomplete.Add($"Target {target.Name} library {package.Name} has an unsupported library type.");
                    continue;
                }

                ValidateTargetPackageAssets(
                    package.Value,
                    library,
                    packageIdentity.CanonicalKey,
                    libraryType,
                    target.Value,
                    restoreIdentities,
                    packageRoots,
                    packageInventories,
                    incomplete,
                    budget);
                if (TryGetPropertyIgnoreCase(package.Value, "dependencies", out var dependencies))
                {
                    if (dependencies.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Target package {package.Name} has malformed dependency metadata.");
                    }
                    else
                    {
                        foreach (var dependency in dependencies.EnumerateObject())
                        {
                            if (dependency.Value.ValueKind != JsonValueKind.String ||
                                !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement) ||
                                !TryFindMatchingTargetPackage(target.Value, restoreIdentities, dependency.Name, requirement, out _))
                            {
                                incomplete.Add($"Target package {package.Name} has an unresolved package requirement {dependency.Name}.");
                            }
                        }
                    }
                }
            }

            foreach (var framework in frameworks.EnumerateObject().Where(framework =>
                         restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity) &&
                         frameworkIdentity.CanonicalKey.Equals(targetIdentity.FrameworkKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (!framework.Value.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) continue;
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    budget.AddDependencyNode();
                    if (!TryReadDependencyRequirement(dependency.Value, out var requirement) ||
                        !TryFindMatchingTargetPackage(target.Value, restoreIdentities, dependency.Name, requirement, out _))
                    {
                        incomplete.Add($"Declared framework {framework.Name} refers to an unresolved package requirement {dependency.Name}.");
                    }
                }
            }
        }
    }

    internal static void ValidateTargetPackageAssets(
        JsonElement package,
        JsonElement library,
        string packageKey,
        string? libraryType,
        JsonElement targetAssets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var isPackage = string.Equals(libraryType, "package", StringComparison.OrdinalIgnoreCase);
        var files = isPackage && packageRoots.TryGetValue(packageKey, out var packageRoot) &&
                    packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
            ? inventory
            : isPackage
                ? ReadLibraryFiles(library, packageKey, incomplete, budget).ToHashSet(GetPackageAssetStringComparer(packageKey))
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDuplicatePropertyReasons(package, "target package metadata", incomplete);
        foreach (var group in package.EnumerateObject())
        {
            if (!TargetPackagePropertyNames.Contains(group.Name))
            {
                if (!IsJsonExtensionProperty(group.Name))
                {
                    incomplete.Add($"Target package {packageKey} has an unsupported asset group or metadata property '{group.Name}'.");
                }

                continue;
            }

            if (group.Name.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
            {
                if (group.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageKey} has malformed dependency metadata.");
                    continue;
                }

                AddDuplicatePropertyReasons(group.Value, "target dependency", incomplete);
                foreach (var dependency in group.Value.EnumerateObject())
                {
                    budget.AddDependencyNode();
                    if (dependency.Value.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement) ||
                        !TryFindMatchingTargetPackage(targetAssets, restoreIdentities, dependency.Name, requirement, out _))
                    {
                        incomplete.Add($"Target package {packageKey} has an unresolved package requirement {dependency.Name}.");
                    }
                }
                continue;
            }

            if (group.Name.Equals("type", StringComparison.OrdinalIgnoreCase) || group.Name.Equals("framework", StringComparison.OrdinalIgnoreCase))
            {
                if (group.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(group.Value.GetString()))
                {
                    incomplete.Add($"Target package {packageKey} has malformed {group.Name} metadata.");
                }
                continue;
            }

            if (group.Name.Equals("frameworkAssemblies", StringComparison.OrdinalIgnoreCase) || group.Name.Equals("frameworkReferences", StringComparison.OrdinalIgnoreCase))
            {
                ValidateStringArrayMetadata(group.Value, group.Name, packageKey, incomplete);
                continue;
            }

            if (group.Value.ValueKind == JsonValueKind.Array)
            {
                incomplete.Add($"Target package {packageKey} has malformed asset group {group.Name}.");
                continue;
            }

            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target package {packageKey} has malformed asset group {group.Name}.");
                continue;
            }

            if (group.Name.Equals("contentFiles", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var content in group.Value.EnumerateObject())
                {
                    if (content.Value.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Target package {packageKey} has malformed content file metadata.");
                        continue;
                    }

                    if (isPackage && !files.Contains(content.Name.Replace('\\', '/')))
                    {
                        incomplete.Add($"Target package {packageKey} references a content file absent from its package inventory.");
                    }
                }
                continue;
            }

            foreach (var asset in group.Value.EnumerateObject())
            {
                if (asset.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageKey} has malformed asset metadata.");
                    continue;
                }

                var assetPath = asset.Name.Replace('\\', '/');
                if (!TryNormalizeRelativePath(assetPath, out _))
                {
                    incomplete.Add($"Target package {packageKey} contains an unsafe asset reference.");
                    continue;
                }

                if (isPackage && !files.Contains(assetPath))
                {
                    incomplete.Add($"Target package {packageKey} references an asset absent from its package inventory.");
                }
            }
        }
    }

    internal static void ValidateFormat4DependencyGroups(
        JsonElement root,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (!root.TryGetProperty("projectFileDependencyGroups", out var groups) || groups.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var group in groups.EnumerateObject())
        {
            if (!restoreIdentities.TryGetFramework(group.Name, out var frameworkIdentity) || group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var dependency in group.Value.EnumerateArray())
            {
                budget.AddDependencyNode();
                if (dependency.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.GetString()))
                {
                    continue;
                }

                var value = dependency.GetString()!;
                if (IsValidProjectDependencyPath(value))
                {
                    continue;
                }

                if (!TryParseFormat4PackageRequirement(value, out var packageId, out var requirement) ||
                    !TryFindMatchingPackageInFrameworkTargets(targets, restoreIdentities, frameworkIdentity.CanonicalKey, packageId, requirement))
                {
                    incomplete.Add($"Assets format 4 dependency group {group.Name} has an unresolved package requirement.");
                }
            }
        }
    }
}
