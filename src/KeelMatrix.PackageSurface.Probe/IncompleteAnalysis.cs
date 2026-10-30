using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
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

internal static class IncompleteAnalysis
{


    internal static bool IsSafeFailureText(string message) =>
        !message.Contains(Path.DirectorySeparatorChar) &&
        !message.Contains(Path.AltDirectorySeparatorChar) &&
        !message.Contains(":\\", StringComparison.Ordinal);



    internal static string[] NormalizeIncompleteReasons(IEnumerable<string> reasons) =>
        DiagnosticDataPolicy.NormalizeReasons(reasons).ToArray();


    internal static bool TryCreateIncompleteResult(IReadOnlyCollection<string> reasons, out ProbeResult result)
    {
        if (reasons.Count == 0)
        {
            result = null!;
            return false;
        }

        result = CreateIncompleteResult(reasons);
        return true;
    }


    internal static ProbeResult CreateIncompleteResult(IEnumerable<string> reasons) =>
        new(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(reasons));


    internal static ProbeResult CreateFailureResult(List<string> reasons, InvalidDataException exception)
    {
        reasons.Add(IsSafeFailureText(exception.Message)
            ? exception.Message
            : "The resolved restore graph could not be analyzed completely.");
        return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(reasons));
    }


    internal static ProbeResult CreateFailureResult(List<string> reasons)
    {
        reasons.Add("The resolved restore graph could not be analyzed completely.");
        return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(reasons));
    }


    internal static ProbeResult CreateResult(IEnumerable<SurfaceEntry> entries, List<string> reasons, int resolvedPackageCount) =>
        new(entries.ToArray(), NormalizeIncompleteReasons(reasons), resolvedPackageCount);


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
