using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class GraphIdentityTests
{
    [Fact]
    public void PackageIdentityAndAssetPathsAreCanonical() => ScenarioSupport.RunPackageIdentityAndPathRegression();

    [Fact]
    public void RestoreIdentitiesReconcileAcrossMetadataSources()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunRestoreIdentityCanonicalizationRegression(workspace.AssetsPath, workspace.Root);
    }

    [Fact]
    public void ReachabilityClosureIncludesOnlyResolvedPackages()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunReachabilityClosureRegression(workspace.Root);
    }

    [Fact]
    public void VersionConflictsAreResolvedPerTarget()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunVersionAwareReachabilityConflictRegression(workspace.Root);
    }

    [Fact]
    public void VersionEquivalenceUsesNugetSemantics()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunVersionEquivalenceRegression(workspace.Root);
    }

    [Fact]
    public void MalformedVersionsFailThroughTheCli()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunMalformedVersionEndToEndRegression(workspace.Root);
    }
}
