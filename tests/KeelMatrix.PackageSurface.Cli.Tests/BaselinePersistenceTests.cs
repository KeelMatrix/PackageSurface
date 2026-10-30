using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class BaselinePersistenceTests
{
    [Fact]
    public void BaselineWritesRemainAtomicAndFailClosed() => ScenarioSupport.RunBaselineContractRegression();

    [Fact]
    public void BaselineTransactionsPreserveThePreviousFile()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunBaselineTransactionRegression(workspace.Root);
    }

    [Fact]
    public void IncompleteAnalysisPreservesTheExistingBaseline()
    {
        using var workspace = new CliScenarioWorkspace();
        ScenarioSupport.RunTerminalIncompleteReportRegression(workspace.Root);
    }

    [Fact]
    public void BaselineJsonBoundariesAreValidated() => ScenarioSupport.RunBaselineJsonBoundaryRegression();
}
