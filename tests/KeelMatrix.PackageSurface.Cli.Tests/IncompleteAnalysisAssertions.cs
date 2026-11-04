using System.Text.Json;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class IncompleteAnalysisAssertions
{
    internal static void AssertGeneratedImportFailure(string label, string assets, string baseline)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Assert.True(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("BuildTargets", StringComparison.Ordinal) &&
                    !scan.Output.Contains("Unused.Package", StringComparison.Ordinal),
                $"{label} emitted a clean or partial {format} scan result.");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Assert.True(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !failedBaseline.Output.Contains("BuildTargets", StringComparison.Ordinal),
                $"{label} emitted a clean or partial {format} baseline result.");
            Assert.True(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)), $"{label} mutated the baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Assert.True(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"{label} emitted a clean or policy-diff {format} check result.");
        }
    }

    internal static void AssertFormat4DependencyGroupFailure(string assets, string baseline)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Assert.True(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("Entries: 1", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} scan.");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Assert.True(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} baseline.");
            Assert.True(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
                $"Format 4 casing-equivalent dependency groups mutated the baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Assert.True(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"Format 4 casing-equivalent dependency groups emitted a clean {format} check.");
        }
    }

    internal static void AssertValidRestoreMetadata(string assets, string projectRoot, string baseline, string label)
    {
        var analyzed = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
        Assert.True(analyzed.IsComplete && analyzed.Entries.Count == 0,
            $"{label} was rejected: {string.Join(" | ", analyzed.IncompleteReasons)}");

        var scan = CaptureCommand("scan", assets, "--format", "json", "--no-telemetry");
        Assert.True(scan.ExitCode == 0 && !scan.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} scan failed: {scan.Output}");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--format", "json", "--no-telemetry").ExitCode == 0,
            $"{label} baseline failed.");
        var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", "json", "--no-telemetry");
        Assert.True(check.ExitCode == 0 && !check.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} check failed: {check.Output}");
    }

    internal static void AssertRestoreIdentityFailure(string assets, string projectRoot, string baseline, string label)
    {
        var baselineBefore = File.ReadAllBytes(baseline);
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var direct = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
            Assert.True(!direct.IsComplete && direct.Entries.Count == 0 && direct.IncompleteReasons.Count > 0,
                $"{label} was not rejected before capability filtering.");

            var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
            Assert.True(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} scan.");
            AssertNoSuccessfulSurfaceOutput(scan.Output, format, label + " scan");

            var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
            Assert.True(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} baseline.");
            AssertNoSuccessfulSurfaceOutput(failedBaseline.Output, format, label + " baseline");
            Assert.True(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
                $"{label} mutated the existing baseline.");

            var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
            Assert.True(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
                $"{label} emitted a clean {format} check.");
            AssertNoSuccessfulSurfaceOutput(check.Output, format, label + " check");
        }
    }

    internal static void AssertNoSuccessfulSurfaceOutput(string output, string format, string label)
    {
        var entryLine = output.Contains("Entries:", StringComparison.Ordinal);
        Assert.True((!entryLine || output.Contains("Entries: 0;", StringComparison.Ordinal)) &&
                !output.Contains("PS001", StringComparison.Ordinal) &&
                !output.Contains("PS002", StringComparison.Ordinal) &&
                !output.Contains("PS003", StringComparison.Ordinal) &&
                !output.Contains("PS004", StringComparison.Ordinal) &&
                !output.Contains("PS005", StringComparison.Ordinal) &&
                !output.Contains("PS006", StringComparison.Ordinal),
            $"{label} emitted success-looking output.");

        if (format.Equals("json", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            Assert.True(document.RootElement.GetProperty("entries").GetArrayLength() == 0,
                $"{label} emitted surface entries.");
        }
        else if (format.Equals("sarif", StringComparison.Ordinal))
        {
            using var document = JsonDocument.Parse(output);
            var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
            Assert.True(results.EnumerateArray().All(result => result.GetProperty("ruleId").GetString() == "PS007"),
                $"{label} emitted a non-PS007 SARIF result.");
        }
    }
}
