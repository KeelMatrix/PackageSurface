using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class RestoreLibraryMetadata
{
    internal static bool IsDeclaredPackageFile(
        string packageRoot,
        string relativePath,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories)
    {
        if (!TryNormalizeRelativePath(relativePath, out var normalized)) return false;
        var inventory = packageInventories.FirstOrDefault(pair => FileSystemPathsEqual(pair.Key, packageRoot));
        return inventory.Value is not null && inventory.Value.Contains(normalized);
    }

    internal static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete, AnalysisBudget budget)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > MaxFilesPerLibrary)
        {
            incomplete.Add($"Library {libraryKey} has no files array.");
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var assetComparison = GetPackageAssetComparison(libraryKey);
        foreach (var file in files.EnumerateArray())
        {
            budget.AddLibraryFile();
            if (file.ValueKind != JsonValueKind.String || !TryNormalizeRelativePath(file.GetString()!, out var normalized))
            {
                incomplete.Add($"Library {libraryKey} contains an invalid relative asset path.");
                continue;
            }

            if (result.Any(existing => string.Equals(existing, normalized, assetComparison)))
            {
                incomplete.Add($"{libraryKey} contains a duplicate asset path: {normalized}.");
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }
}
