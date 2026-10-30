using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class HostileInputTests
{
    [Fact]
    public void GeneratedPropsRejectDocumentTypeDeclarations()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunDtdGeneratedPropsRegression(workspace.Root, workspace.AssetsPath);
    }

    [Fact]
    public void GeneratedPropsXmlDepthIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunDeepGeneratedPropsRegression(workspace.Root, workspace.AssetsPath);
    }

    [Fact]
    public void TraversalLookingAssetPathsFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunTraversalAssetPathRegression(workspace.Root, workspace.Cache);
    }

    [Fact]
    public void InvalidCompilerMetadataFailsClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunInvalidAnalyzerMetadataRegression(workspace.Root, workspace.Cache);
    }

    [Fact]
    public void OversizedRestoreInputFailsThroughTheCli()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunOversizedRestoreRegression(workspace.Root);
    }

    [Fact]
    public void LargeReachableGraphStaysWithinTheResourceBudget()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunLargeGraphBudgetRegression(workspace.Root, workspace.Cache);
    }

    [Fact]
    public void DiagnosticsDoNotExposeLocalPaths()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunDiagnosticPathLeakRegression(workspace.Root);
    }
}
