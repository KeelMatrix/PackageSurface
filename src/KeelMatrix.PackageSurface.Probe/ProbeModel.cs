using NuGet.Versioning;

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

/// <summary>
/// The identity NuGet assigns to a resolved package. Package IDs and versions
/// compare using NuGet's case-insensitive, normalized-version rules; the
/// original spelling is retained for developer-facing output.
/// </summary>
public sealed class PackageIdentity : IEquatable<PackageIdentity>
{
    private PackageIdentity(string id, string version, NuGetVersion parsedVersion)
    {
        Id = id;
        Version = version;
        NormalizedVersion = parsedVersion.ToNormalizedString();
        ParsedVersion = parsedVersion;
    }

    public string Id { get; }
    public string Version { get; }
    public string NormalizedVersion { get; }
    public string CanonicalKey => Id + "/" + NormalizedVersion;

    public static bool TryCreate(string id, string version, out PackageIdentity identity)
    {
        identity = null!;
        if (!IsValidId(id) || !TryParseNuGetVersion(version, out var parsedVersion)) return false;
        identity = new PackageIdentity(id, version, parsedVersion);
        return true;
    }

    public int CompareVersionTo(PackageIdentity other) => VersionComparer.VersionRelease.Compare(ParsedVersion, other.ParsedVersion);

    public bool Equals(PackageIdentity? other) => other is not null &&
        string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase) &&
        VersionComparer.VersionRelease.Equals(ParsedVersion, other.ParsedVersion);

    public override bool Equals(object? obj) => Equals(obj as PackageIdentity);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
        VersionComparer.VersionRelease.GetHashCode(ParsedVersion));

    private static bool IsValidId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return false;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_')) return false;
        }

        return true;
    }

    public static bool TryParseNuGetVersion(string value, out NuGetVersion parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal)) return false;
        if (value.IndexOfAny(['[', ']', '(', ')', ',', '*', '|']) >= 0 ||
            !VersionRange.TryParse(value, out var range) ||
            range is null || range.IsFloating || !range.HasLowerBound || range.HasUpperBound)
        {
            return false;
        }

        parsed = range.MinVersion;
        return parsed is not null;
    }

    private NuGetVersion ParsedVersion { get; }
}

/// <summary>
/// The stable surface identity used by classification, deduplication,
/// baselines, diffs, and reports. Package version remains provenance, but is
/// intentionally not part of equality so a version-only update is checked as
/// the same reviewed asset and can be detected by strict-content hashing.
/// </summary>
public sealed class SurfaceIdentity
{
    private SurfaceIdentity(
        string? project,
        SurfaceContextKind context,
        string? targetFramework,
        string? runtimeIdentifier,
        PackageIdentity package,
        string relationship,
        CapabilityKind capability,
        string? packageRelativePath)
    {
        Project = project;
        Context = context;
        TargetFramework = targetFramework;
        RuntimeIdentifier = runtimeIdentifier;
        Package = package;
        Relationship = relationship;
        Capability = capability;
        PackageRelativePath = packageRelativePath;
    }

    public string? Project { get; }
    public SurfaceContextKind Context { get; }
    public string? TargetFramework { get; }
    public string? RuntimeIdentifier { get; }
    public PackageIdentity Package { get; }
    public string Relationship { get; }
    public CapabilityKind Capability { get; }
    public string? PackageRelativePath { get; }

    public static SurfaceIdentity Create(
        string? project,
        SurfaceContextKind context,
        string? targetFramework,
        string? runtimeIdentifier,
        string packageId,
        string version,
        string relationship,
        CapabilityKind capability,
        string? packageRelativePath)
    {
        if (!PackageIdentity.TryCreate(packageId, version, out var package))
        {
            throw new InvalidDataException("The package identity is not valid.");
        }

        return new SurfaceIdentity(NormalizePath(project), context, targetFramework, runtimeIdentifier, package, relationship, capability, NormalizePath(packageRelativePath));
    }

    public static SurfaceIdentity From(SurfaceEntry entry) => Create(
        entry.Project,
        entry.Context,
        entry.TargetFramework,
        entry.RuntimeIdentifier,
        entry.PackageId,
        entry.Version,
        entry.Relationship,
        entry.Capability,
        entry.PackageRelativePath);

    public static SurfaceIdentity ForProjectAggregation(SurfaceEntry entry) => Create(
        entry.Project,
        entry.Context,
        entry.TargetFramework,
        entry.RuntimeIdentifier,
        entry.PackageId,
        entry.Version,
        string.Empty,
        entry.Capability,
        entry.PackageRelativePath);

    private static string? NormalizePath(string? value) => value?.Replace('\\', '/');
}

public sealed class SurfaceIdentityComparer : IEqualityComparer<SurfaceIdentity>
{
    public static SurfaceIdentityComparer Instance { get; } = new();
    private static StringComparison FileSystemComparison => OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    private static StringComparer FileSystemComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    public bool Equals(SurfaceIdentity? x, SurfaceIdentity? y)
    {
        if (x is null || y is null) return x is null && y is null;
        return string.Equals(x.Project, y.Project, FileSystemComparison) &&
            x.Context == y.Context &&
            string.Equals(x.TargetFramework, y.TargetFramework, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.RuntimeIdentifier, y.RuntimeIdentifier, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Package.Id, y.Package.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Relationship, y.Relationship, StringComparison.OrdinalIgnoreCase) &&
            x.Capability == y.Capability &&
            string.Equals(x.PackageRelativePath, y.PackageRelativePath, FileSystemComparison);
    }

    public int GetHashCode(SurfaceIdentity obj)
    {
        var hash = new HashCode();
        hash.Add(obj.Project, FileSystemComparer);
        hash.Add(obj.Context);
        hash.Add(obj.TargetFramework, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.RuntimeIdentifier, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Package.Id, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Relationship, StringComparer.OrdinalIgnoreCase);
        hash.Add(obj.Capability);
        hash.Add(obj.PackageRelativePath, FileSystemComparer);
        return hash.ToHashCode();
    }
}

public sealed class PackageIdentityComparer : IEqualityComparer<PackageIdentity>
{
    public static PackageIdentityComparer Instance { get; } = new();
    public bool Equals(PackageIdentity? x, PackageIdentity? y) => x?.Equals(y) == true;
    public int GetHashCode(PackageIdentity obj) => obj.GetHashCode();
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
/// Applies the package-relative diagnostic boundary to every validation reason
/// and exception message before it can reach a report or stderr.
/// </summary>
public static class DiagnosticDataPolicy
{
    public const string GenericIncompleteReason = "The input, baseline, or restore evidence could not be analyzed completely.";
    public const string GenericParserReason = "Invalid command-line arguments.";
    private static readonly HashSet<string> SafeParserReasons = new(StringComparer.Ordinal)
    {
        "--version cannot be combined with another argument.",
        "Unknown command.",
        "--format must be text, json, or sarif.",
        "Unknown option.",
        "Only one input path is allowed.",
        "An input path is required.",
        "baseline requires --output <baseline>.",
        "--output is only valid with baseline.",
        "check requires --baseline <baseline>.",
        "--baseline is only valid with check.",
        "--format requires a value.",
        "--output requires a value.",
        "--baseline requires a value.",
        "--project requires a value.",
        "--compiler-api-version requires a value.",
        "--telemetry requires a value.",
        "--telemetry must be on or off."
    };

    public static IReadOnlyList<string> NormalizeReasons(IEnumerable<string> reasons) =>
        reasons.Select(SanitizeReason)
            .Where(reason => reason.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

    public static string SafeExceptionMessage(Exception exception) =>
        exception is InvalidDataException && !ContainsAbsolutePathMarker(exception.Message)
            ? SanitizeReason(exception.Message)
            : GenericIncompleteReason;

    public static string SafeParserMessage(string? message) =>
        message is not null && SafeParserReasons.Contains(message.Trim())
            ? message.Trim()
            : GenericParserReason;

    public static string SanitizeReason(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason) || ContainsAbsolutePathMarker(reason))
        {
            return GenericIncompleteReason;
        }

        if (ContainsUntrustedMemberName(reason))
        {
            if (reason.Contains("unknown", StringComparison.OrdinalIgnoreCase)) return "Restore evidence contains an unknown metadata member.";
            if (reason.Contains("non-canonical", StringComparison.OrdinalIgnoreCase)) return "Restore evidence contains a non-canonical metadata member spelling.";
            return "Restore evidence contains duplicate or case-variant metadata members.";
        }

        return reason.Trim();
    }

    private static bool ContainsUntrustedMemberName(string value)
    {
        var memberName = value.Contains("property", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("field", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("member", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("element", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("attribute", StringComparison.OrdinalIgnoreCase);
        var failureKind = value.Contains("unknown", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("duplicate", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("case-variant", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("non-canonical", StringComparison.OrdinalIgnoreCase);
        return memberName && failureKind;
    }

    private static bool ContainsAbsolutePathMarker(string value)
    {
        for (var index = 0; index < value.Length; index++)
        {
            if (index + 2 < value.Length && char.IsLetter(value[index]) && value[index + 1] == ':' &&
                (value[index + 2] == '\\' || value[index + 2] == '/'))
            {
                return true;
            }

            if (value[index] == '\\' && index + 1 < value.Length && value[index + 1] == '\\')
            {
                return true;
            }

            if (value[index] != '/') continue;
            var atBoundary = index == 0 || char.IsWhiteSpace(value[index - 1]) ||
                value[index - 1] is '\'' or '\"' or '(' or '[' or ':' or '=';
            if (atBoundary) return true;
        }

        return false;
    }
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
