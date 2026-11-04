using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.SurfaceEntryFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class CliParsingTests
{
    private static readonly string[] ExpectedDiagnosticIds = { "PS001", "PS002", "PS003", "PS004", "PS005", "PS006" };

    [Fact]
    public void DiffReportsAllCapabilityChanges()
    {
        var entries = new[]
        {
            Entry(CapabilityKind.BuildProps, "build/changed.props", "a"),
            Entry(CapabilityKind.BuildTargets, "build/changed.targets", "d"),
            Entry(CapabilityKind.CompilerExtension, "analyzers/dotnet/cs/new.dll", "b"),
            Entry(CapabilityKind.NativeRuntime, "runtimes/win-x64/native/new.dll", "c")
        };
        var baseline = new[] { BaselineEntry.From(Entry(CapabilityKind.BuildProps, "build/changed.props", "old")) };
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
    public void ParserErrorsAreStableAndActionable()
    {


        var error = new StringWriter(CultureInfo.InvariantCulture);
        var priorError = Console.Error;
        try
        {
            Console.SetError(error);
            var arguments = new[] { "scan", "safe-input", "--format" };
            Assert.True(CommandLine.Run(arguments) == 2, "Missing option value did not return exit code 2.");
        }
        finally
        {
            Console.SetError(priorError);
        }

        var text = error.ToString();
        Assert.True(text.Contains("--format requires a value", StringComparison.Ordinal) &&
                !text.Contains("invalid command-line arguments", StringComparison.Ordinal) &&
                !text.Contains("safe-input", StringComparison.Ordinal),
            "Missing option value lost its safe specific parser message or echoed input text.");
    }
}
