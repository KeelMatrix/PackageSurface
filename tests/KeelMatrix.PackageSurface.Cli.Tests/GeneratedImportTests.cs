using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class GeneratedImportTests
{
    [Fact]
    public void NestedImportsAreAnalyzed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunNestedImportRegression(workspace.Root);
    }

    [Fact]
    public void NestedImportPhasesRemainDistinct()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunNestedPhaseMatrixRegression(workspace.Root);
    }

    [Fact]
    public void ProjectExtensionsAreResolved()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunProjectExtensionsRegression(workspace.Root);
    }

    [Fact]
    public void UnsupportedMsbuildContextsFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunInvalidMsBuildContextRegression(workspace.Root);
    }

    [Fact]
    public void GeneratedImportsMustBeReachable()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunGeneratedImportReachabilityRegression(workspace.Root);
    }

    [Fact]
    public void PackageMsbuildXmlMustMatchTheSupportedSchema()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunPackageXmlSchemaRegression(workspace.Root);
    }

    [Fact]
    public void ImportGraphWorkIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunImportEdgeBudgetRegression(workspace.Root);
    }

    [Fact]
    public void InvocationStructureIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunInvocationStructuralBudgetRegression(workspace.Root);
    }

    [Fact]
    public void InlineTaskConventionIsDetected()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunInlineTaskConventionRegression(workspace.Root);
    }
}
