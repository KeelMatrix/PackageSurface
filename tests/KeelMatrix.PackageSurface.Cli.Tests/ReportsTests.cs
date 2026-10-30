using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class ReportsTests
{
    private static readonly string[] IncompleteReasons = { "restore evidence is incomplete" };

    [Fact]
    public void StrictContentEligibilityMatrix() => ScenarioSupport.RunStrictContentEligibilityMatrixTests();

    [Fact]
    public void IdentityAndReportContracts() => ScenarioSupport.RunIdentityAndReportContractTests();

    [Fact]
    public void IncompleteAnalysisUsesPs007()
    {
        var report = ReportDocument.Create(
            CommandKind.Check,
            new SurfaceSnapshot(Array.Empty<SurfaceEntry>(), IncompleteReasons, false, 0),
            new[] { Diagnostic.Create("PS007", "restore evidence is incomplete") });

        var diagnostic = Assert.Single(report.Diagnostics);
        Assert.Equal("PS007", diagnostic.Id);
    }
}
