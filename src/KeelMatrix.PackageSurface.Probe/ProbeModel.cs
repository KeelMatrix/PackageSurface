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
    private PackageIdentity(string id, string version, string normalizedVersion, PackageVersionParts versionParts)
    {
        Id = id;
        Version = version;
        NormalizedVersion = normalizedVersion;
        VersionParts = versionParts;
    }

    public string Id { get; }
    public string Version { get; }
    public string NormalizedVersion { get; }
    public string CanonicalKey => Id + "/" + NormalizedVersion;

    public static bool TryCreate(string id, string version, out PackageIdentity identity)
    {
        identity = null!;
        if (!IsValidId(id) || !TryParseVersion(version, out var parsedVersion)) return false;
        identity = new PackageIdentity(id, version, parsedVersion.Normalized, parsedVersion);
        return true;
    }

    public int CompareVersionTo(PackageIdentity other) => CompareVersionParts(VersionParts, other.VersionParts);

    public bool Equals(PackageIdentity? other) => other is not null &&
        string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(NormalizedVersion, other.NormalizedVersion, StringComparison.OrdinalIgnoreCase);

    public override bool Equals(object? obj) => Equals(obj as PackageIdentity);

    public override int GetHashCode() => HashCode.Combine(
        StringComparer.OrdinalIgnoreCase.GetHashCode(Id),
        StringComparer.OrdinalIgnoreCase.GetHashCode(NormalizedVersion));

    private static bool IsValidId(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "." or "..") return false;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_')) return false;
        }

        return true;
    }

    private static bool TryParseVersion(string value, out PackageVersionParts parsed)
    {
        parsed = null!;
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal)) return false;

        var buildSeparator = value.IndexOf('+');
        var withoutBuild = buildSeparator < 0 ? value : value[..buildSeparator];
        if (buildSeparator >= 0 && !IsValidIdentifiers(value[(buildSeparator + 1)..])) return false;

        var prereleaseSeparator = withoutBuild.IndexOf('-');
        var numeric = prereleaseSeparator < 0 ? withoutBuild : withoutBuild[..prereleaseSeparator];
        var prerelease = prereleaseSeparator < 0 ? null : withoutBuild[(prereleaseSeparator + 1)..];
        if (numeric.Length == 0 || (prerelease is not null && !IsValidIdentifiers(prerelease))) return false;

        var numericParts = numeric.Split('.');
        if (numericParts.Length is < 1 or > 4 || numericParts.Any(part => part.Length == 0 || part.Any(character => !char.IsDigit(character)))) return false;
        var normalizedParts = numericParts
            .Select(part => part.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0")
            .ToList();
        while (normalizedParts.Count < 3) normalizedParts.Add("0");
        while (normalizedParts.Count > 3 && normalizedParts[^1] == "0") normalizedParts.RemoveAt(normalizedParts.Count - 1);

        var normalized = string.Join('.', normalizedParts);
        var normalizedPrerelease = Array.Empty<string>();
        if (!string.IsNullOrEmpty(prerelease))
        {
            normalizedPrerelease = prerelease.Split('.').Select(NormalizeVersionIdentifier).ToArray();
            normalized += "-" + string.Join('.', normalizedPrerelease);
        }

        parsed = new PackageVersionParts(normalized, normalizedParts.ToArray(), normalizedPrerelease);
        return true;
    }

    private static int CompareVersionParts(PackageVersionParts left, PackageVersionParts right)
    {
        for (var index = 0; index < Math.Max(left.NumericParts.Length, right.NumericParts.Length); index++)
        {
            var leftPart = index < left.NumericParts.Length ? left.NumericParts[index] : "0";
            var rightPart = index < right.NumericParts.Length ? right.NumericParts[index] : "0";
            var length = leftPart.Length.CompareTo(rightPart.Length);
            if (length != 0) return length;
            var numeric = string.CompareOrdinal(leftPart, rightPart);
            if (numeric != 0) return numeric;
        }

        if (left.PrereleaseParts.Length == 0 && right.PrereleaseParts.Length == 0) return 0;
        if (left.PrereleaseParts.Length == 0) return 1;
        if (right.PrereleaseParts.Length == 0) return -1;
        for (var index = 0; index < Math.Max(left.PrereleaseParts.Length, right.PrereleaseParts.Length); index++)
        {
            if (index >= left.PrereleaseParts.Length) return -1;
            if (index >= right.PrereleaseParts.Length) return 1;
            var leftPart = left.PrereleaseParts[index];
            var rightPart = right.PrereleaseParts[index];
            var leftNumeric = leftPart.All(char.IsDigit);
            var rightNumeric = rightPart.All(char.IsDigit);
            if (leftNumeric && rightNumeric)
            {
                var numeric = CompareNumericIdentifiers(leftPart, rightPart);
                if (numeric != 0) return numeric;
            }
            else if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }
            else
            {
                var text = string.CompareOrdinal(leftPart, rightPart);
                if (text != 0) return text;
            }
        }

        return 0;
    }

    private static int CompareNumericIdentifiers(string left, string right)
    {
        left = left.TrimStart('0');
        right = right.TrimStart('0');
        left = left.Length == 0 ? "0" : left;
        right = right.Length == 0 ? "0" : right;
        return left.Length != right.Length ? left.Length.CompareTo(right.Length) : string.CompareOrdinal(left, right);
    }

    private static string NormalizeVersionIdentifier(string value) =>
        value.All(char.IsDigit)
            ? value.TrimStart('0') is { Length: > 0 } trimmed ? trimmed : "0"
            : value.ToLowerInvariant();

    private static bool IsValidIdentifiers(string value) =>
        value.Length > 0 && value.Split('.').All(part => part.Length > 0 && part.All(character => char.IsLetterOrDigit(character) || character == '-'));

    private PackageVersionParts VersionParts { get; }

    private sealed record PackageVersionParts(string Normalized, string[] NumericParts, string[] PrereleaseParts);
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
