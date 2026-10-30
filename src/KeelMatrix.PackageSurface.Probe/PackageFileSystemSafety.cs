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

internal static class PackageFileSystemSafety
{

    internal sealed record PackageInventorySnapshot(
        Dictionary<string, string> PackageRoots,
        Dictionary<string, HashSet<string>> PackageInventories);


    internal sealed record PackageAssetPresence(bool Present, string PhysicalPath, string? Reason);


    // Containment compares canonical identities. Production uses Path.GetFullPath;
    // the test harness injects a deterministic canonicalization function through this
    // private seam to exercise an ancestor link without weakening root reparse checks.
    internal static readonly AsyncLocal<Func<string, string>?> PathCanonicalizer = new();



    internal static bool PathsEqual(string left, string right) =>
        FileSystemPathsEqual(left, right);


    internal static PackageInventorySnapshot PreparePackageInventory(
        RestoreGraphMetadata metadata,
        RestoreGraphSnapshot graph,
        string projectRoot,
        string? projectContext,
        string? selectedProjectPath,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        FileSystemComparisonContext.Current.RegisterProjectPath(
            projectContext,
            selectedProjectPath ?? ReadProjectPath(metadata.Root) ?? projectRoot);
        var reachablePackageKeys = graph.ReachablePackagesByTarget.Values
            .SelectMany(value => value.ReachablePackageKeys)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var packageRoots = ResolvePackageRoots(
            metadata.Identities,
            metadata.Libraries,
            metadata.PackageFolders,
            incomplete,
            reachablePackageKeys);
        FileSystemComparisonContext.Current.RegisterPackageRoots(projectContext, packageRoots);
        var packageInventories = BuildPackageInventories(
            packageRoots,
            metadata.Identities,
            metadata.Libraries,
            incomplete,
            budget);
        return new PackageInventorySnapshot(packageRoots, packageInventories);
    }


    internal static IReadOnlyList<string> GetPackageAssetFiles(
        string packageRoot,
        string libraryKey,
        JsonElement library,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        AnalysisBudget budget) =>
        packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
            ? inventory.Order(StringComparer.Ordinal).ToArray()
            : ReadLibraryFiles(library, libraryKey, incomplete, budget).ToArray();


    internal static PackageAssetPresence InspectAssetPresence(
        string packageRoot,
        string relativePath,
        string libraryKey,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var physicalPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var resolved = TryResolveExistingFilePath(physicalPath, out var actualPath);
        var safePath = resolved && IsSafeResolvedFile(packageRoot, actualPath);
        var present = safePath;
        if (present)
        {
            physicalPath = actualPath;
        }

        var reason = present ? null : safePath
            ? $"Reachable asset is missing from resolved package contents: {relativePath}."
            : $"Reachable asset resolves through an unsafe package path: {relativePath}.";
        if (!present)
        {
            incomplete.Add($"{libraryKey}: {reason}");
        }
        else
        {
            budget.Add(FileLength(physicalPath), "package asset");
        }

        return new PackageAssetPresence(present, physicalPath, reason);
    }


    internal static bool TryResolveNestedImportAsset(
        string packageRoot,
        string relativePath,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        out string physicalPath)
    {
        var candidatePath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (!TryResolveExistingFilePath(candidatePath, out physicalPath) || !IsSafeResolvedFile(packageRoot, physicalPath))
        {
            incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
            physicalPath = string.Empty;
            return false;
        }

        var actualRelativePath = Path.GetRelativePath(packageRoot, physicalPath).Replace(Path.DirectorySeparatorChar, '/');
        if (!IsDeclaredPackageFile(packageRoot, actualRelativePath, packageInventories))
        {
            incomplete.Add("A statically imported package build file is absent from the resolved package inventory.");
            physicalPath = string.Empty;
            return false;
        }

        return true;
    }



    internal static Dictionary<string, string> ResolvePackageRoots(
        RestoreIdentityIndex restoreIdentities,
        JsonElement libraries,
        IReadOnlyList<string> packageFolders,
        List<string> incomplete,
        HashSet<string>? reachablePackageKeys = null)
    {
        foreach (var package in restoreIdentities.Libraries.Values)
        {
            if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, package.Identity, out _, out var library) ||
                !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                reachablePackageKeys is not null && !reachablePackageKeys.Contains(package.CanonicalKey)) continue;
            if (!library.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"Library {package.SourceKey} has no valid package path.");
                continue;
            }

            var packageRoot = ResolvePackageRoot(package.Identity, path.GetString()!, packageFolders.ToArray(), incomplete);
            if (packageRoot is not null)
            {
                restoreIdentities.PackageRoots[package.CanonicalKey] = packageRoot;
            }
        }

        return restoreIdentities.PackageRoots;
    }



    internal static Dictionary<string, HashSet<string>> BuildPackageInventories(
        IReadOnlyDictionary<string, string> packageRoots,
        RestoreIdentityIndex restoreIdentities,
        JsonElement libraries,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var result = new Dictionary<string, HashSet<string>>(FileSystemPathComparer);
        foreach (var package in packageRoots)
        {
            if (!TryCreatePackageIdentity(package.Key, out var packageIdentity) ||
                !restoreIdentities.TryGetLibraryByCanonicalKey(libraries, packageIdentity, out _, out var library))
            {
                incomplete.Add("A resolved package root has no matching library inventory.");
                continue;
            }

            result[Path.GetFullPath(package.Value)] = ReadLibraryFiles(library, package.Key, incomplete, budget)
                .ToHashSet(GetPackageAssetStringComparer(package.Key));
        }

        return result;
    }



    internal static string? ResolvePackageRoot(PackageIdentity identity, string libraryPath, string[] packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {identity.Id}/{identity.Version} has an unsafe cache-relative path.");
            return null;
        }

        var separator = normalizedLibraryPath.IndexOf('/');
        if (separator <= 0 || separator == normalizedLibraryPath.Length - 1 ||
            normalizedLibraryPath.IndexOf('/', separator + 1) >= 0 ||
            !PackageIdentity.TryCreate(normalizedLibraryPath[..separator], normalizedLibraryPath[(separator + 1)..], out var pathIdentity) ||
            !identity.Equals(pathIdentity))
        {
            incomplete.Add($"Package {identity.Id}/{identity.Version} has a cache-relative path that does not belong to that package identity.");
            return null;
        }

        foreach (var folder in packageFolders)
        {
            if (!Path.IsPathRooted(folder))
            {
                continue;
            }

            var candidatePaths = new[]
            {
                Path.Combine(folder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(folder, identity.Id, identity.NormalizedVersion.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(folder, identity.Id.ToLowerInvariant(), identity.NormalizedVersion.Replace('/', Path.DirectorySeparatorChar))
            };
            foreach (var candidatePath in candidatePaths.Distinct(FileSystemPathComparer))
            {
                var candidate = Path.GetFullPath(candidatePath);
                if (Directory.Exists(candidate) && IsSafeResolvedDirectory(folder, candidate) && DirectoryInfoMatchesIdentity(candidate, identity))
                {
                    return candidate;
                }
            }
        }

        incomplete.Add($"Package {identity.Id}/{identity.Version} was not found in the resolved package folders.");
        return null;
    }



    internal static bool DirectoryInfoMatchesIdentity(string path, PackageIdentity identity)
    {
        var packageDirectory = new DirectoryInfo(path);
        return packageDirectory.Parent is not null &&
            PackageIdentity.TryCreate(packageDirectory.Parent.Name, packageDirectory.Name, out var physicalIdentity) &&
            identity.Equals(physicalIdentity);
    }



    internal static bool TryNormalizeRelativePath(string value, out string normalized)
    {
        normalized = value.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('\0') || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Contains(':') || parts.Any(part => part is "" or "." or ".."))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }



    internal static string NormalizeText(string value) => value.Replace('\\', '/').Trim();



    internal static bool TryGetNuGetPackageRootSuffix(string value, out string suffix)
    {
        const string property = "$(NuGetPackageRoot)";
        if (!value.StartsWith(property, StringComparison.OrdinalIgnoreCase))
        {
            suffix = string.Empty;
            return false;
        }

        suffix = value[property.Length..].TrimStart('/');
        return suffix.Length > 0;
    }



    internal static bool PackageRootImportEquals(string import, PackageIdentity expectedPackage, string expectedRelativePath)
    {
        if (!TryGetNuGetPackageRootSuffix(import, out var suffix)) return false;
        var firstSlash = suffix.IndexOf('/');
        var secondSlash = firstSlash < 0 ? -1 : suffix.IndexOf('/', firstSlash + 1);
        if (firstSlash <= 0 || secondSlash <= firstSlash + 1 || secondSlash == suffix.Length - 1 ||
            !PackageIdentity.TryCreate(suffix[..firstSlash], suffix[(firstSlash + 1)..secondSlash], out var importedPackage) ||
            !expectedPackage.Equals(importedPackage))
        {
            return false;
        }

        var importedRelativePath = suffix[(secondSlash + 1)..];
        return string.Equals(importedRelativePath.Replace('\\', '/'), expectedRelativePath.Replace('\\', '/'),
            FileSystemComparisonContext.Current.GetPackageAssetStringComparison(expectedPackage));
    }



    internal static string ComputeSha256(string path, AnalysisBudget budget)
    {
        EnsureFileWithinLimit(path, MaxMetadataFileBytes, "asset");
        budget.AddHash(FileLength(path));
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }



    internal static void EnsureFileWithinLimit(string path, long limit, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The required {description} is missing.");
        }

        if (new FileInfo(path).Length > limit)
        {
            throw new InvalidDataException($"The {description} exceeds the supported size limit.");
        }
    }



    internal static long FileLength(string path) => new FileInfo(path).Length;



    internal static bool IsSafeResolvedFile(string packageRoot, string file)
    {
        var fullFile = Path.GetFullPath(file);
        return IsWithinDirectory(packageRoot, fullFile, allowRoot: false) &&
            !HasReparsePoint(packageRoot, fullFile, includeLeaf: true);
    }



    internal static bool IsSafeResolvedDirectory(string packageFolder, string candidate)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        return IsWithinDirectory(packageFolder, fullCandidate, allowRoot: true) &&
            !HasReparsePoint(packageFolder, fullCandidate, includeLeaf: false);
    }



    internal static bool IsWithinDirectory(string root, string candidate, bool allowRoot)
    {
        var fullRoot = CanonicalizePath(root);
        var fullCandidate = CanonicalizePath(candidate);
        var fileSystem = FileSystemComparisonContext.Current;
        if (fileSystem.TryPathsEqual(fullRoot, fullCandidate, out var samePath) && samePath) return allowRoot;

        var rootPrefix = Path.EndsInDirectorySeparator(fullRoot)
            ? fullRoot
            : fullRoot + Path.DirectorySeparatorChar;
        return fullCandidate.Length > rootPrefix.Length &&
            fileSystem.TryPathsEqual(rootPrefix, fullCandidate[..rootPrefix.Length], out var hasRootPrefix) &&
            hasRootPrefix;
    }



    internal static StringComparison GetPackageAssetComparison(string packageKey) =>
        TryCreatePackageIdentity(packageKey, out var package)
            ? FileSystemComparisonContext.Current.GetPackageAssetStringComparison(package)
            : StringComparison.Ordinal;



    internal static StringComparer GetPackageAssetStringComparer(string packageKey) =>
        GetPackageAssetComparison(packageKey) == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;



    internal static StringComparer ProjectPathStringComparer =>
        FileSystemComparisonContext.Current.ProjectStringComparison == StringComparison.OrdinalIgnoreCase
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;



    internal static bool FileSystemPathsEqual(string left, string right) =>
        FileSystemComparisonContext.Current.TryPathsEqual(CanonicalizePath(left), CanonicalizePath(right), out var equal) && equal;



    internal static bool TryResolveExistingFilePath(string path, out string resolvedPath)
    {
        if (FileSystemComparisonContext.Current.TryResolveFilePath(path, out resolvedPath, out var exists) && exists)
        {
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }



    internal static string CanonicalizePath(string path) =>
        PathCanonicalizer.Value?.Invoke(path) ?? Path.GetFullPath(path);



    internal static IEqualityComparer<string> FileSystemPathComparer => FileSystemComparisonContext.Current.PathComparer;



    internal static bool HasReparsePoint(string root, string path, bool includeLeaf)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (includeLeaf && File.Exists(fullPath))
        {
            var file = new FileInfo(fullPath);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
            {
                return true;
            }
        }

        var current = new DirectoryInfo(includeLeaf ? Path.GetDirectoryName(fullPath)! : fullPath);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                return true;
            }

            if (FileSystemPathsEqual(current.FullName, fullRoot))
            {
                break;
            }

            current = current.Parent;
        }

        return false;
    }



}
