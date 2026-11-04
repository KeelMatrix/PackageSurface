using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;

namespace KeelMatrix.PackageSurface.Probe;

internal static class ImportExistsCondition
{
    // Bind the import's filesystem context once; the condition parser only consumes known/unknown truth.
    internal static Func<string, StaticImportCondition.ParsedCondition> ForImport(
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots) => requestedPath =>
        TryProveExists(requestedPath, importProject, restoreIdentities, packageRoots, out var exists, out var reason)
            ? StaticImportCondition.ParsedCondition.Known(new StaticImportCondition.ConstantCondition(exists))
            : StaticImportCondition.ParsedCondition.Unknown(reason!);

    internal static bool TryProveExists(
        string requestedPath,
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out bool exists,
        out string? reason)
    {
        exists = false;
        reason = null;
        try
        {
            if (!TryResolveExistsPath(requestedPath, restoreIdentities, packageRoots, out var requested))
            {
                reason = "the Exists expression is not a resolvable filesystem path";
                return false;
            }

            if (!TryResolveExistsPath(importProject, restoreIdentities, packageRoots, out var project))
            {
                reason = "the Exists expression is not the standard resolved-package-file guard";
                return false;
            }

            var requestedCanonical = CanonicalizePath(requested);
            var projectCanonical = CanonicalizePath(project);
            if (FileSystemComparisonContext.Current.TryPathsEqual(requestedCanonical, projectCanonical, out var pathsEqual))
            {
                if (pathsEqual)
                {
                    if (FileSystemComparisonContext.Current.TryFileExists(requested, out exists)) return true;
                    reason = "the Exists expression's filesystem result is indeterminate";
                    return false;
                }

                if (string.Equals(requestedCanonical, projectCanonical, StringComparison.OrdinalIgnoreCase))
                {
                    exists = false;
                    return true;
                }
            }

            reason = string.Equals(requestedCanonical, projectCanonical, StringComparison.OrdinalIgnoreCase)
                ? "the Exists expression's filesystem casing semantics are indeterminate"
                : "the Exists expression does not match the resolved import under the host filesystem contract";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            reason = "the Exists expression contains an invalid filesystem path";
            return false;
        }
    }

    internal static bool TryResolveExistsPath(
        string value,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out string resolvedPath)
    {
        var normalized = NormalizeText(value);
        if (TryGetNuGetPackageRootSuffix(normalized, out var suffix))
        {
            if (!restoreIdentities.TryGetPackageByImportSuffix(suffix, out var packageIdentity, out var relativePath) ||
                !packageRoots.TryGetValue(packageIdentity.CanonicalKey, out var packageRoot) ||
                !TryNormalizeRelativePath(relativePath, out var safeRelativePath))
            {
                resolvedPath = string.Empty;
                return false;
            }

            resolvedPath = Path.GetFullPath(Path.Combine(packageRoot, safeRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return true;
        }

        if (Path.IsPathRooted(normalized))
        {
            resolvedPath = Path.GetFullPath(normalized);
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }
}
