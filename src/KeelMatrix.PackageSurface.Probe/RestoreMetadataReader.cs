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
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class RestoreMetadataReader
{
    internal sealed record RestoreGraphMetadata(
        JsonElement Root,
        JsonElement Libraries,
        JsonElement Targets,
        string[] PackageFolders,
        RestoreIdentityIndex Identities);

    internal static JsonDocument ReadAssetsDocument(string assetsFile, AnalysisBudget budget)
    {
        EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
        budget.Add(FileLength(assetsFile), "metadata");
        using var stream = File.OpenRead(assetsFile);
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
    }

    internal static RestoreGraphMetadata? ReadGraphMetadata(
        JsonElement root,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        ValidateRestoreJsonStructure(root, incomplete);
        if (incomplete.Count > 0)
        {
            return null;
        }

        _ = ValidateAssetsFormat(root, incomplete);
        var packageFolders = ReadPackageFolders(root, incomplete);
        var libraries = root.TryGetProperty("libraries", out var librariesElement)
            ? librariesElement
            : throw new InvalidDataException("project.assets.json has no libraries object.");
        if (libraries.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has an invalid libraries object.");
        }

        foreach (var _ in libraries.EnumerateObject())
        {
            budget.AddLibrary();
        }

        if (!root.TryGetProperty("targets", out var targets))
        {
            throw new InvalidDataException("project.assets.json has no targets object.");
        }

        if (targets.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has an invalid targets object.");
        }

        foreach (var _ in targets.EnumerateObject())
        {
            budget.AddTargetGraph();
        }

        var identities = BuildRestoreIdentityIndex(root, libraries, targets, incomplete);
        return new RestoreGraphMetadata(root, libraries, targets, packageFolders, identities);
    }

    internal static string? ReadRestoreProjectPath(string assetsFile)
    {
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var incomplete = new List<string>();
            ValidateRestoreJsonStructure(document.RootElement, incomplete);
            if (incomplete.Count > 0) return null;

            if (document.RootElement.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
                restore.TryGetProperty("projectPath", out var projectPath) && projectPath.ValueKind == JsonValueKind.String)
            {
                return projectPath.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
        }

        return null;
    }
}
