using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class AssetClassificationTests
{
    [Fact]
    public void AnalyzerExclusionsMatchRestoreRules()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunAnalyzerExclusionRegression(workspace.Root, workspace.Cache, workspace.Obj);
    }

    [Fact]
    public void ApplicabilityAndPresentStateAreConservative()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunApplicabilityHardeningRegressions(workspace.Root);
    }

    [Fact]
    public void CompilerExtensionLanguageConventionsAreApplied()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunLanguageConventionRegression(workspace.Root);
    }

    [Fact]
    public void ProjectLevelAssetsAreAggregated()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunProjectAggregationRegression(workspace.Root);
    }
}
