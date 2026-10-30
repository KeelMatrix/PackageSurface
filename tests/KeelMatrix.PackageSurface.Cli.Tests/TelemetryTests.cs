using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void EligibilityDoesNotExposePackageIdentity() => ScenarioSupport.RunTelemetryEligibilityTests();

    [Fact]
    public void ParserTokensAreNeverReported() => ScenarioSupport.RunParserTokenPrivacyRegression();
}
