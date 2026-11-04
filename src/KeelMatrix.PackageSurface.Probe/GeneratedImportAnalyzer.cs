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
using static KeelMatrix.PackageSurface.Probe.GeneratedImportDiscovery;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class GeneratedImportAnalyzer
{
    internal static GeneratedImportSnapshot ReadAndValidate(
        RestoreGraphMetadata metadata,
        string assetsFile,
        string projectRoot,
        string? selectedProjectPath,
        PackageInventorySnapshot packageInventory,
        RestoreGraphSnapshot graph,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var expectedSources = new HashSet<string>(ProjectPathStringComparer);
        var imports = ReadGeneratedImports(
            metadata.Root,
            assetsFile,
            projectRoot,
            selectedProjectPath,
            metadata.Identities,
            packageInventory.PackageRoots,
            packageInventory.PackageInventories,
            metadata.Libraries,
            metadata.Identities.FrameworkContexts,
            incomplete,
            budget,
            expectedSources);
        ValidateGeneratedImportEvidence(
            imports,
            metadata.Targets,
            metadata.Identities,
            graph.ReachablePackagesByTarget,
            packageInventory.PackageRoots,
            packageInventory.PackageInventories,
            metadata.Libraries,
            incomplete,
            budget);
        return new GeneratedImportSnapshot(imports, expectedSources);
    }
}
