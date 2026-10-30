using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class CliParsingTests
{
    private static readonly string[] ExpectedDiagnosticIds = { "PS001", "PS002", "PS003", "PS004", "PS005", "PS006" };

    [Fact]
    public void DiffReportsAllCapabilityChanges()
    {
        var entries = new[]
        {
            ScenarioSupport.Entry(CapabilityKind.BuildProps, "build/changed.props", "a"),
            ScenarioSupport.Entry(CapabilityKind.BuildTargets, "build/changed.targets", "d"),
            ScenarioSupport.Entry(CapabilityKind.CompilerExtension, "analyzers/dotnet/cs/new.dll", "b"),
            ScenarioSupport.Entry(CapabilityKind.NativeRuntime, "runtimes/win-x64/native/new.dll", "c")
        };
        var baseline = new[] { BaselineEntry.From(ScenarioSupport.Entry(CapabilityKind.BuildProps, "build/changed.props", "old")) };
        var actual = DiffEngine.Compare(baseline, entries, strictContent: true).Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(ExpectedDiagnosticIds, actual.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void HelpDocumentsTheStableCommandContract()
    {
        var clauses = new[]
        {
            "package-surface scan <path> [options]",
            "package-surface baseline <path> --output <baseline> [options]",
            "package-surface check <path> --baseline <baseline> [options]",
            "--format text|json|sarif", "(default: text)", "--output <baseline>", "--baseline <baseline>",
            "--strict-content", "(default: off)", "--compiler-api-version <v>", "--project <path>", "--no-telemetry",
            "0 Scan/baseline succeeded", "1 Check found a surface or policy difference", "2 Invalid invocation",
            "https://github.com/KeelMatrix/PackageSurface/blob/main/docs/restore-and-baseline-safety.md",
            "https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md",
            "PS007 means analysis is incomplete", "baseline files are not replaced"
        };
        var normalized = Regex.Replace(Options.HelpText, @"\s+", " ");

        Assert.DoesNotContain(clauses, clause => !normalized.Contains(clause, StringComparison.Ordinal));
    }

    [Fact]
    public void ParserErrorsAreStableAndActionable() => ScenarioSupport.RunParserMessageRegression();
}
