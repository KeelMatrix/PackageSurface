using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

public sealed record SurfaceSnapshot(
    IReadOnlyList<SurfaceEntry> Entries,
    IReadOnlyList<string> IncompleteReasons,
    bool StrictContent,
    int ResolvedPackageCount)
{
    public static SurfaceSnapshot Create(IEnumerable<SurfaceEntry> entries, IEnumerable<string> reasons, bool strictContent, int resolvedPackageCount) =>
        new(entries.OrderBy(entry => entry.Project, StringComparer.Ordinal)
            .ThenBy(entry => entry.Context)
            .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
            .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Version, StringComparer.Ordinal)
            .ThenBy(entry => entry.Relationship, StringComparer.Ordinal)
            .ThenBy(entry => entry.Capability)
            .ThenBy(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.PackageRelativePath, StringComparer.Ordinal)
            .ToArray(), reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), strictContent, resolvedPackageCount);
}
