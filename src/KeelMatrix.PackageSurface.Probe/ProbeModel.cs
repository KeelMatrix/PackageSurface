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

public static class CapabilityPolicy
{
    public static bool IsStrictContentEligible(SurfaceEntry entry) =>
        IsStrictContentEligible(entry.Capability, entry.Present, entry.Active);

    public static bool IsStrictContentEligible(CapabilityKind capability, bool present, bool active) =>
        present && active && capability is
            CapabilityKind.BuildProps or
            CapabilityKind.BuildTargets or
            CapabilityKind.BuildTransitive or
            CapabilityKind.BuildMultiTargeting or
            CapabilityKind.CompilerExtension or
            CapabilityKind.CompileSourceInjection;
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

/// <summary>
/// Cumulative safety budget shared by every analysis started for one CLI invocation.
/// </summary>
public sealed class AnalysisBudget
{
    public const long MaxTotalBytes = 512 * 1024 * 1024;
    public const long MaxTotalHashBytes = 256 * 1024 * 1024;
    public const int MaxTargetGraphs = 512;
    public const int MaxLibraries = 20_000;
    public const int MaxLibraryFiles = 20_000;
    public const int MaxDependencyNodes = 20_000;
    public const int MaxImportedFiles = 4_096;
    public const int MaxImportedEdges = 8_192;
    public const int MaxOperations = 250_000;
    public const int MaxEntries = 50_000;

    private long totalBytes;
    private long totalHashBytes;
    private int importedFiles;
    private int importedEdges;
    private int operations;
    private int entries;
    private int targetGraphs;
    private int libraries;
    private int libraryFiles;
    private int dependencyNodes;

    public void Add(long bytes, string description)
    {
        if (bytes < 0 || Interlocked.Add(ref totalBytes, bytes) > MaxTotalBytes)
        {
            throw new InvalidDataException($"The analysis exceeded the total {description} byte budget.");
        }
    }

    public void AddHash(long bytes)
    {
        if (bytes < 0 || Interlocked.Add(ref totalHashBytes, bytes) > MaxTotalHashBytes)
        {
            throw new InvalidDataException("The analysis exceeded the total hashing byte budget.");
        }
    }

    public void AddImportedFile()
    {
        if (Interlocked.Increment(ref importedFiles) > MaxImportedFiles)
        {
            throw new InvalidDataException("The analysis exceeded the total nested-import file budget.");
        }
    }

    public void AddImportedEdge()
    {
        if (Interlocked.Increment(ref importedEdges) > MaxImportedEdges)
        {
            throw new InvalidDataException("The analysis exceeded the total nested-import edge budget.");
        }
    }

    public void AddTargetGraph()
    {
        if (Interlocked.Increment(ref targetGraphs) > MaxTargetGraphs)
        {
            throw new InvalidDataException("The analysis exceeded the total target-graph budget.");
        }
    }

    public void AddLibrary()
    {
        if (Interlocked.Increment(ref libraries) > MaxLibraries)
        {
            throw new InvalidDataException("The analysis exceeded the total resolved-library budget.");
        }
    }

    public void AddLibraryFile()
    {
        if (Interlocked.Increment(ref libraryFiles) > MaxLibraryFiles)
        {
            throw new InvalidDataException("The analysis exceeded the total package-inventory file budget.");
        }
    }

    public void AddDependencyNode()
    {
        if (Interlocked.Increment(ref dependencyNodes) > MaxDependencyNodes)
        {
            throw new InvalidDataException("The analysis exceeded the total dependency-traversal budget.");
        }
    }

    public void AddOperation(string description)
    {
        if (Interlocked.Increment(ref operations) > MaxOperations)
        {
            throw new InvalidDataException($"The analysis exceeded the supported {description} work budget.");
        }
    }

    public void AddEntries(int count)
    {
        if (count < 0 || Interlocked.Add(ref entries, count) > MaxEntries)
        {
            throw new InvalidDataException("The selected capability surface exceeds the supported entry budget.");
        }
    }
}
