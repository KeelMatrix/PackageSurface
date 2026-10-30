using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class ProbeModuleTests
{
    private static readonly string[] ReachableToolFile = { "tools/fixture.ps1" };
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
}
