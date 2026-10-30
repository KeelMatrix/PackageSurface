using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.GeneratedImportAnalyzer;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class ResolvedGraphAnalysis
{
    internal static ProbeResult Analyze(
        string assetsFile,
        string projectRoot,
        bool strictContent,
        string? projectContext,
        string? selectedProjectPath,
        AnalysisBudget? budget,
        string? compilerApiVersion,
        FileSystemCaseSensitivity fileSystemCaseSensitivity)
    {
        var incomplete = new List<string>();
        budget ??= new AnalysisBudget();
        using var fileSystemScope = FileSystemComparisonContext.Push(projectRoot, fileSystemCaseSensitivity);
        try
        {
            using var document = ReadAssetsDocument(assetsFile, budget);
            var metadata = ReadGraphMetadata(document.RootElement, incomplete, budget);
            if (metadata is null)
            {
                return CreateIncompleteResult(incomplete);
            }

            if (TryCreateIncompleteResult(incomplete, out var incompleteResult))
            {
                return incompleteResult;
            }

            var graph = AnalyzeRestoreGraph(metadata, projectRoot, incomplete, budget);
            if (TryCreateIncompleteResult(incomplete, out incompleteResult))
            {
                return incompleteResult;
            }

            var packageInventory = PreparePackageInventory(
                metadata,
                graph,
                projectRoot,
                projectContext,
                selectedProjectPath,
                incomplete,
                budget);
            ValidateRestoreEvidence(
                metadata.Root,
                metadata.Libraries,
                metadata.Targets,
                metadata.Identities,
                metadata.PackageFolders,
                packageInventory.PackageRoots,
                packageInventory.PackageInventories,
                incomplete,
                budget);
            if (TryCreateIncompleteResult(incomplete, out incompleteResult))
            {
                return incompleteResult;
            }

            var generatedImports = ReadAndValidate(
                metadata,
                assetsFile,
                projectRoot,
                selectedProjectPath,
                packageInventory,
                graph,
                incomplete,
                budget);
            if (TryCreateIncompleteResult(incomplete, out incompleteResult))
            {
                return incompleteResult;
            }

            var projectCapabilityContext = ReadProjectCapabilityContext(
                metadata.Root,
                projectRoot,
                selectedProjectPath,
                metadata.Identities,
                compilerApiVersion,
                incomplete);
            var targets = ResolveTargetPackages(metadata, graph, packageInventory.PackageRoots, incomplete, budget);
            return ResolvedTargetAnalysis.Analyze(
                metadata,
                targets,
                packageInventory,
                generatedImports,
                projectCapabilityContext,
                strictContent,
                projectContext,
                budget,
                incomplete);
        }
        catch (InvalidDataException exception)
        {
            return CreateFailureResult(incomplete, exception);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            return CreateFailureResult(incomplete);
        }
    }
}
