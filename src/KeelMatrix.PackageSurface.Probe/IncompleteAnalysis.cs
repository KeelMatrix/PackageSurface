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


}
