using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class ProbeModuleTests
{
    private static readonly string[] ReachableToolFile = { "tools/fixture.ps1" };
    private static readonly string[] PresentAsset = { "build/Example.props" };
    private static readonly string[] IncompleteReasonCases = { "zeta evidence", "alpha evidence", "zeta evidence" };
    private static readonly string[] ExpectedIncompleteReasons = { "alpha evidence", "zeta evidence" };

    [Fact]
    public void RestoreMetadataReaderReadsTheProjectIdentity()
    {
        using var workspace = new ProbeScenarioWorkspace();

        Assert.Equal(Path.Combine(workspace.Root, "Test.csproj"), RestoreMetadataReader.ReadRestoreProjectPath(workspace.AssetsPath));
    }

    [Fact]
    public void PackageGraphReaderReturnsOnlyReachablePackageInputs()
    {
        using var workspace = new ProbeScenarioWorkspace(ReachableToolFile, createFiles: true);

        var inputs = PackageGraphAnalyzer.GetReachablePackageInputPaths(workspace.AssetsPath);

        Assert.Equal(new[] { Path.Combine(workspace.Cache, "Example.Package", "1.0.0", "tools", "fixture.ps1") }, inputs);
    }

    [Fact]
    public void GeneratedImportConditionParserEvaluatesTargetFrameworks()
    {
        var parsed = GeneratedImportAnalyzer.ParseCondition(
            "'$(TargetFramework)' == 'net8.0'",
            "Test.props",
            new RestoreMetadataReader.RestoreIdentityIndex(),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        Assert.True(parsed.IsKnown, parsed.Reason);
        Assert.True(parsed.Node!.Evaluate(SurfaceContextKind.Target, "net8.0"));
        Assert.False(parsed.Node.Evaluate(SurfaceContextKind.Target, "net9.0"));
    }

    [Fact]
    public void AssetClassifierMapsBuildTransitiveFiles()
    {
        var recognized = CapabilityAssetClassifier.TryGetCapability("buildTransitive/Example.targets", out var capability);

        Assert.True(recognized);
        Assert.Equal(CapabilityKind.BuildTransitive, capability);
    }

    [Fact]
    public void FilesystemSafetyChecksContainmentAtTheModuleSeam()
    {
        var root = Path.Combine(Path.GetTempPath(), "packagesurface-module-paths-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var inside = Path.Combine(root, "package", "asset.targets");
        var outside = root + "-outside";
        using var scope = FileSystemComparisonScope.Push(root, FileSystemCaseSensitivity.Host);

        try
        {
            Assert.True(PackageFileSystemSafety.IsWithinDirectory(root, inside, allowRoot: false));
            Assert.False(PackageFileSystemSafety.IsWithinDirectory(root, outside, allowRoot: false));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void IncompleteAnalysisNormalizesReasonsDeterministically()
    {
        var normalized = IncompleteAnalysis.NormalizeIncompleteReasons(IncompleteReasonCases);

        Assert.Equal(ExpectedIncompleteReasons, normalized);
    }


    [Fact]
    public void RestoreGraphComponentsPreserveTargetPackageProvenance()
    {
        using var workspace = new ProbeScenarioWorkspace();
        var incomplete = new List<string>();
        var budget = new AnalysisBudget();
        using var document = RestoreMetadataReader.ReadAssetsDocument(workspace.AssetsPath, budget);
        var metadata = RestoreMetadataReader.ReadGraphMetadata(document.RootElement, incomplete, budget);

        Assert.NotNull(metadata);
        var graph = PackageGraphAnalyzer.AnalyzeRestoreGraph(metadata, workspace.Root, incomplete, budget);
        var inventory = PackageFileSystemSafety.PreparePackageInventory(
            metadata,
            graph,
            workspace.Root,
            projectContext: null,
            selectedProjectPath: null,
            incomplete,
            budget);
        var targets = PackageGraphAnalyzer.ResolveTargetPackages(metadata, graph, inventory.PackageRoots, incomplete, budget);

        Assert.Empty(incomplete);
        Assert.Equal("Example.Package/1.0.0", Assert.Single(Assert.Single(targets.Targets).Packages).LibraryKey);
        Assert.Single(targets.ResolvedPackages);
    }


    [Fact]
    public void GeneratedImportAnalysisOwnsExpectedSourceAndImportEvidence()
    {
        using var workspace = new ProbeScenarioWorkspace();
        var incomplete = new List<string>();
        var budget = new AnalysisBudget();
        using var document = RestoreMetadataReader.ReadAssetsDocument(workspace.AssetsPath, budget);
        var metadata = RestoreMetadataReader.ReadGraphMetadata(document.RootElement, incomplete, budget);
        Assert.NotNull(metadata);
        var graph = PackageGraphAnalyzer.AnalyzeRestoreGraph(metadata, workspace.Root, incomplete, budget);
        var inventory = PackageFileSystemSafety.PreparePackageInventory(
            metadata,
            graph,
            workspace.Root,
            projectContext: null,
            selectedProjectPath: null,
            incomplete,
            budget);

        var result = GeneratedImportAnalyzer.ReadAndValidate(
            metadata,
            workspace.AssetsPath,
            workspace.Root,
            selectedProjectPath: null,
            inventory,
            graph,
            incomplete,
            budget);

        Assert.Empty(incomplete);
        Assert.Empty(result.Imports);
        Assert.Contains("Test.csproj.nuget.g.props", result.ExpectedGeneratedImportSources);
        Assert.Contains("Test.csproj.nuget.g.targets", result.ExpectedGeneratedImportSources);
    }


    [Fact]
    public void CapabilityClassifierOwnsAssetContextAndApplicability()
    {
        Assert.True(CapabilityAssetClassifier.TryClassifyAssetPath("buildMultiTargeting/Example.props", out var projectAsset));
        Assert.Equal(CapabilityKind.BuildMultiTargeting, projectAsset.Capability);
        Assert.Equal(SurfaceContextKind.Project, projectAsset.Context);

        using var targetAssets = System.Text.Json.JsonDocument.Parse(
            "{\"native\":{\"runtimes/linux-x64/native/libfixture.so\":{}}}");
        Assert.True(PackageIdentity.TryCreate("Example.Package", "1.0.0", out var package));
        string? reason = null;
        var incomplete = new List<string>();
        var active = CapabilityAssetClassifier.IsActive(
            CapabilityKind.NativeRuntime,
            "runtimes/linux-x64/native/libfixture.so",
            package,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            Path.GetTempPath(),
            targetAssets.RootElement,
            SurfaceContextKind.Target,
            "net8.0",
            Array.Empty<GeneratedImportAnalyzer.GeneratedImport>(),
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            isMultiTargetingProject: false,
            CapabilityAssetClassifier.ProjectLanguage.Unknown,
            ref reason,
            incomplete,
            "Example.Package/1.0.0");

        Assert.True(active);
        Assert.Null(reason);
        Assert.Empty(incomplete);
    }


    [Fact]
    public void FilesystemComponentOwnsPresenceAndResolvedPathEvidence()
    {
        using var workspace = new ProbeScenarioWorkspace(PresentAsset, createFiles: true);
        var packageRoot = Path.Combine(workspace.Cache, "Example.Package", "1.0.0");
        var incomplete = new List<string>();
        using var scope = FileSystemComparisonScope.Push(workspace.Root, FileSystemCaseSensitivity.Host);

        var present = PackageFileSystemSafety.InspectAssetPresence(
            packageRoot,
            "build/Example.props",
            "Example.Package/1.0.0",
            incomplete,
            new AnalysisBudget());
        var missing = PackageFileSystemSafety.InspectAssetPresence(
            packageRoot,
            "build/Missing.props",
            "Example.Package/1.0.0",
            incomplete,
            new AnalysisBudget());

        Assert.True(present.Present);
        Assert.True(File.Exists(present.PhysicalPath));
        Assert.False(missing.Present);
        Assert.Contains(incomplete, reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase));
    }


    [Fact]
    public void IncompleteAnalysisOwnsBudgetFailureConversion()
    {
        var budget = new AnalysisBudget();
        budget.Add(AnalysisBudget.MaxTotalBytes, "metadata");
        var failure = Assert.Throws<InvalidDataException>(() => budget.Add(1, "metadata"));
        var result = IncompleteAnalysis.CreateFailureResult(new List<string>(), failure);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.Equal(failure.Message, Assert.Single(result.IncompleteReasons));
    }
}
