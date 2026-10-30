using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class ProbeAnalysisTests
{
    private static readonly string[] TraversalAsset = { "../escape.props" };
    private static readonly string[] MissingAsset = { "build/expected.targets" };

    [Fact]
    public void ValidEmptyPackageGraphProducesACompleteSurface()
    {
        using var workspace = new ProbeScenarioWorkspace();

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.True(result.IsComplete, string.Join("; ", result.IncompleteReasons));
        Assert.Empty(result.Entries);
        Assert.Equal(1, result.ResolvedPackageCount);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("[]")]
    [InlineData("{\"targets\":[]}")]
    public void MalformedRestoreDocumentsFailClosed(string contents)
    {
        using var workspace = new ProbeScenarioWorkspace();
        File.WriteAllText(workspace.AssetsPath, contents);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    [Fact]
    public void MissingAssetsFileFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace();

        var result = ResolvedGraphClassifier.Analyze(Path.Combine(workspace.Obj, "missing.assets.json"), workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }

    [Fact]
    public void TraversalLookingPackageAssetFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace(TraversalAsset);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.Contains(result.IncompleteReasons, reason => reason.Contains("invalid relative asset path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void MissingReachablePackageAssetFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace(MissingAsset);

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Contains(result.IncompleteReasons, reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void OversizedRestoreFileFailsClosed()
    {
        using var workspace = new ProbeScenarioWorkspace();
        File.WriteAllText(workspace.AssetsPath, new string('x', 16 * 1024 * 1024 + 1));

        var result = ResolvedGraphClassifier.Analyze(workspace.AssetsPath, workspace.Root);

        Assert.False(result.IsComplete);
        Assert.Empty(result.Entries);
        Assert.NotEmpty(result.IncompleteReasons);
    }
}
