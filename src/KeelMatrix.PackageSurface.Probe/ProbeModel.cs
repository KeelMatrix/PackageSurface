namespace KeelMatrix.PackageSurface.Probe;

public enum CapabilityKind
{
    BuildProps,
    BuildTargets,
    BuildTransitive,
    BuildMultiTargeting,
    CompilerExtension,
    CompileSourceInjection,
    NativeRuntime,
    ToolOrScriptPresent
}

public enum SurfaceContextKind
{
    Target,
    Project
}

public sealed record SurfaceEntry(
    string? TargetFramework,
    string? RuntimeIdentifier,
    SurfaceContextKind Context,
    string PackageId,
    string Version,
    string Relationship,
    CapabilityKind Capability,
    string PackageRelativePath,
    bool Present,
    bool Active,
    string? Sha256,
    bool Incomplete,
    string? IncompleteReason,
    string? Project = null,
    IReadOnlyList<string>? ObservedPrimitives = null);

public sealed record ProbeResult(
    IReadOnlyList<SurfaceEntry> Entries,
    IReadOnlyList<string> IncompleteReasons,
    int ResolvedPackageCount = 0)
{
    public bool IsComplete => IncompleteReasons.Count == 0 && Entries.All(entry => !entry.Incomplete);
}
