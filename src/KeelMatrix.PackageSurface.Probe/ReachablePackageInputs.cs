using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.AnalyzerPropagation;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class ReachablePackageInputs
{
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
