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

internal static class CapabilityAssetClassifier
{


    internal static ProjectLanguage ReadProjectLanguage(JsonElement root, string projectRoot, string? selectedProjectPath, List<string> incomplete)
    {
        string? projectPath = null;
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("projectPath", out var restoredPath) && restoredPath.ValueKind == JsonValueKind.String)
        {
            projectPath = restoredPath.GetString();
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            incomplete.Add("Restore metadata does not identify the consuming project.");
            return ProjectLanguage.Unknown;
        }

        if (selectedProjectPath is not null && !PathsEqual(projectPath, selectedProjectPath))
        {
            incomplete.Add("Restore metadata identifies a different project than the selected input.");
            return ProjectLanguage.Unknown;
        }

        if (projectPath is not null && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.CSharp;
        if (projectPath is not null && projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.VisualBasic;
        if (projectPath is not null && projectPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectLanguage.FSharp;
        }

        return ProjectLanguage.Unknown;
    }



    internal static string? ReadCompilerApiVersion(JsonElement root)
    {
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("compilerApiVersion", out var compiler))
        {
            if (compiler.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException("Restore metadata field 'compilerApiVersion' must be a string.");
            }

            return compiler.GetString();
        }

        return null;
    }



    internal static bool IsBuildCapability(CapabilityKind capability) =>
        capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting;



    internal static HashSet<string> SelectAnalyzerPaths(
        IReadOnlyList<string> files,
        ProjectLanguage projectLanguage,
        HashSet<string> analyzerPackages,
        string libraryKey,
        List<string> incomplete,
        string? compilerApiVersion)
    {
        var candidates = new List<(string Path, Version? RoslynVersion)>();
        foreach (var path in files)
        {
            if (!path.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) || IsAnalyzerSatellite(path))
            {
                continue;
            }

            if (!TryParseAnalyzerPath(path, out _, out var roslynVersion))
            {
                incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
                continue;
            }

            if (!IsAnalyzerApplicableToProject(path, projectLanguage, incomplete, libraryKey))
            {
                continue;
            }

            if (analyzerPackages.Contains(libraryKey))
            {
                candidates.Add((path, roslynVersion));
            }
        }

        var selected = new HashSet<string>(GetPackageAssetStringComparer(libraryKey));
        foreach (var candidate in candidates.Where(candidate => candidate.RoslynVersion is null))
        {
            selected.Add(candidate.Path);
        }

        var versionedCandidates = candidates.Where(candidate => candidate.RoslynVersion is not null).ToArray();
        var compilerVersion = new Version(0, 0);
        if (versionedCandidates.Length > 0 && !Version.TryParse(compilerApiVersion, out compilerVersion))
        {
            incomplete.Add($"{libraryKey}: versioned analyzer applicability requires an explicit consuming compiler API version.");
            return selected;
        }

        var highest = versionedCandidates
            .Where(candidate => candidate.RoslynVersion is not null && candidate.RoslynVersion.CompareTo(compilerVersion) <= 0)
            .Select(candidate => candidate.RoslynVersion)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        if (versionedCandidates.Length > 0 && highest is null)
        {
            incomplete.Add($"{libraryKey}: no analyzer version is compatible with the consuming compiler API version.");
            return selected;
        }

        foreach (var candidate in versionedCandidates)
        {
            if (highest is not null && candidate.RoslynVersion == highest)
            {
                selected.Add(candidate.Path);
            }
        }

        return selected;
    }



    internal static bool TryParseAnalyzerPath(string path, out string? language, out Version? roslynVersion)
    {
        language = null;
        roslynVersion = null;
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("analyzers", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 1; index < segments.Length - 1; index++)
        {
            if (segments[index].StartsWith("roslyn", StringComparison.OrdinalIgnoreCase))
            {
                if (roslynVersion is not null || !Version.TryParse(segments[index]["roslyn".Length..], out roslynVersion))
                {
                    return false;
                }
            }
            else if (IsAnalyzerLanguage(segments[index]))
            {
                if (language is not null)
                {
                    return false;
                }

                language = segments[index].ToLowerInvariant();
            }
        }

        return true;
    }



    internal static bool IsAnalyzerLanguage(string value) =>
        value.Equals("cs", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("vb", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("fs", StringComparison.OrdinalIgnoreCase);



    internal static bool IsAnalyzerApplicableToProject(string path, ProjectLanguage language, List<string> incomplete, string libraryKey)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var hasCSharpSegment = segments.Any(segment => segment.Equals("cs", StringComparison.OrdinalIgnoreCase));
        var hasVisualBasicSegment = segments.Any(segment => segment.Equals("vb", StringComparison.OrdinalIgnoreCase));
        return language switch
        {
            ProjectLanguage.CSharp => hasCSharpSegment || !hasVisualBasicSegment,
            ProjectLanguage.VisualBasic => hasVisualBasicSegment || !hasCSharpSegment,
            ProjectLanguage.FSharp => false,
            ProjectLanguage.Unknown when !hasCSharpSegment && !hasVisualBasicSegment => true,
            ProjectLanguage.Unknown => AddUnknownAnalyzerLanguageReason(incomplete, libraryKey),
            _ => false
        };
    }



    internal static bool AddUnknownAnalyzerLanguageReason(List<string> incomplete, string libraryKey)
    {
        incomplete.Add($"{libraryKey}: consuming project language is required to determine compiler-extension applicability.");
        return false;
    }



    internal static bool IsAnalyzerSatellite(string path) =>
        path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);



    internal static bool IsCompilerExtensionActive(
        string path,
        ProjectLanguage language,
        HashSet<string> analyzerPackages,
        IReadOnlySet<string> selectedAnalyzers,
        string packageKey,
        List<string> incomplete,
        string libraryKey)
    {
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsAnalyzerSatellite(path))
        {
            return false;
        }

        if (!TryParseAnalyzerPath(path, out _, out _))
        {
            incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
            return false;
        }

        if (!analyzerPackages.Contains(packageKey) || !IsAnalyzerApplicableToProject(path, language, incomplete, libraryKey))
        {
            return false;
        }

        return selectedAnalyzers.Contains(path);
    }



    internal static bool IsCompileContentFile(JsonElement targetAssets, string assetName, ProjectLanguage projectLanguage, List<string> incomplete, string libraryKey)
    {
        if (!targetAssets.TryGetProperty("contentFiles", out var contentFiles))
        {
            return false;
        }

        if (contentFiles.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{libraryKey}: contentFiles metadata has an unsupported shape.");
            return false;
        }

        foreach (var contentFile in contentFiles.EnumerateObject())
        {
            if (!string.Equals(contentFile.Name.Replace('\\', '/'), assetName, GetPackageAssetComparison(libraryKey))) continue;
            if (contentFile.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"{libraryKey}: content file metadata is missing for {assetName}.");
                return false;
            }

            if (!contentFile.Value.TryGetProperty("buildAction", out var buildAction))
            {
                incomplete.Add($"{libraryKey}: content file buildAction metadata is missing for {assetName}.");
                return false;
            }

            if (buildAction.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"{libraryKey}: content file buildAction metadata is not a string for {assetName}.");
                return false;
            }

            if (!buildAction.GetString()!.Equals("Compile", StringComparison.OrdinalIgnoreCase)) return false;
            var languageName = "any";
            if (contentFile.Value.TryGetProperty("codeLanguage", out var language))
            {
                if (language.ValueKind != JsonValueKind.String)
                {
                    incomplete.Add($"{libraryKey}: content file codeLanguage metadata is not a string for {assetName}.");
                    return false;
                }

                languageName = language.GetString()!;
            }

            if (!languageName.Equals("any", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("cs", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("vb", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("fs", StringComparison.OrdinalIgnoreCase))
            {
                incomplete.Add($"{libraryKey}: content file codeLanguage metadata is unsupported for {assetName}.");
                return false;
            }

            return languageName.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                languageName.Equals(ProjectLanguageCode(projectLanguage), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }



    internal static string ProjectLanguageCode(ProjectLanguage language) => language switch
    {
        ProjectLanguage.CSharp => "cs",
        ProjectLanguage.VisualBasic => "vb",
        ProjectLanguage.FSharp => "fs",
        _ => string.Empty
    };



    internal static bool TryGetCapability(string path, out CapabilityKind capability)
    {
        capability = default;
        var normalized = path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        if (normalized.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildTransitive;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("buildMultiTargeting/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildMultiTargeting;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("build/", StringComparison.OrdinalIgnoreCase))
        {
            capability = extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? CapabilityKind.BuildProps : CapabilityKind.BuildTargets;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) &&
            (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            capability = CapabilityKind.CompilerExtension;
            return true;
        }

        if (normalized.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.CompileSourceInjection;
            return true;
        }

        if (normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) && normalized.Contains("/native/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.NativeRuntime;
            return true;
        }

        if (normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.ToolOrScriptPresent;
            return true;
        }

        return false;
    }



    internal static (string Tfm, string? Rid) SplitTarget(string target)
    {
        var slash = target.IndexOf('/');
        return slash < 0 ? (target, null) : (target[..slash], target[(slash + 1)..]);
    }



    internal enum ProjectLanguage
    {
        Unknown,
        CSharp,
        VisualBasic,
        FSharp
    }


}
