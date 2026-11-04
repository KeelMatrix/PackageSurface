using static KeelMatrix.PackageSurface.Probe.StaticImportCondition;

namespace KeelMatrix.PackageSurface.Probe;

internal sealed record GeneratedImportSnapshot(
        List<GeneratedImport> Imports,
        HashSet<string> ExpectedGeneratedImportSources);

internal sealed record NestedImportAsset(
        PackageIdentity Package,
        string PackageRoot,
        string RelativePath,
        CapabilityKind Capability,
        SurfaceContextKind Context,
        string TargetFramework,
        string? RuntimeIdentifier);

internal sealed record GeneratedImport(
        string SourceFile,
        string Project,
        ImportApplicability Applicability,
        bool IsNested = false,
        string? PackageRoot = null,
        string? PackageRelativePath = null,
        CapabilityKind? Capability = null,
        string? RootAssetPath = null)
{
    public bool AppliesTo(SurfaceContextKind context, string targetFramework, bool projectLevelCapability) =>
        context == (projectLevelCapability ? SurfaceContextKind.Project : SurfaceContextKind.Target) &&
        Applicability.AppliesTo(context, targetFramework);
}
