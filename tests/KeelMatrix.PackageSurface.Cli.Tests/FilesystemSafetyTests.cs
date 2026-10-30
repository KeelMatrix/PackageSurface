using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class FilesystemSafetyTests
{
    [Fact]
    public void MixedFilesystemIdentityIsConsistent() => ScenarioSupport.RunMixedFilesystemIdentityRegression();

    [Fact]
    public void AncestorAliasesAreCanonicalized()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunAncestorCanonicalizationRegression(workspace.Root);
    }

    [Fact]
    public void ReparsePointsAreRejected()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunReparsePointRegression(workspace.Root);
    }

    [Fact]
    public void ReparsePathVariantsAreRejected()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunReparsePathVariantRegression(workspace.Root);
    }

    [Fact]
    public void ReachablePackageAliasesAreRejected()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunReachablePackageAliasRegression(workspace.Root);
    }

    [Fact]
    public void MacosRootAliasPredicateIsStable() => ScenarioSupport.RunMacOsRootAliasPredicateRegression();
}
