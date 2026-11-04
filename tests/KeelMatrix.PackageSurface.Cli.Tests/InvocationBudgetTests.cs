using System.Globalization;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class InvocationBudgetTests
{
    [Fact]
    public void InvocationStructureIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "invocation-budget");
        Directory.CreateDirectory(root);
        var projects = new[] { "ProjectA", "ProjectB" };
        foreach (var name in projects)
        {
            var projectRoot = Path.Combine(root, name);
            var obj = Path.Combine(projectRoot, "obj");
            Directory.CreateDirectory(obj);
            File.WriteAllText(Path.Combine(projectRoot, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            WriteTargetGraphAssets(Path.Combine(obj, "project.assets.json"), Path.Combine(projectRoot, "cache"), Path.Combine(projectRoot, name + ".csproj"), 257, includeToolAsset: true);
        }

        var projectA = Path.Combine(root, "ProjectA", "ProjectA.csproj");
        var baselinePath = Path.Combine(root, "approved.json");
        var baselineResult = CaptureCommand("baseline", projectA, "--output", baselinePath, "--no-telemetry");
        Assert.True(baselineResult.ExitCode == 0, "The within-budget project could not create its recovery baseline.");
        var baselineBefore = File.ReadAllBytes(baselinePath);

        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", root, "--format", format, "--no-telemetry");
            Assert.True(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted partial {format} scan output.");

            var baseline = CaptureCommand("baseline", root, "--output", baselinePath, "--format", format, "--no-telemetry");
            Assert.True(baseline.ExitCode == 2 && baseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !baseline.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted partial {format} baseline output.");
            Assert.True(baselineBefore.SequenceEqual(File.ReadAllBytes(baselinePath)),
                $"Cumulative target-graph overflow mutated the baseline for {format} output.");

            var check = CaptureCommand("check", root, "--baseline", baselinePath, "--format", format, "--no-telemetry");
            Assert.True(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted a clean-looking {format} check result.");
        }

        var narrowed = CaptureCommand("scan", projectA, "--project", projectA, "--no-telemetry");
        Assert.True(narrowed.ExitCode == 0 && narrowed.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
            "--project did not provide the documented recovery path after an aggregate structural-budget failure.");
        Assert.True(CaptureCommand("baseline", projectA, "--project", projectA, "--output", Path.Combine(root, "recovery.json"), "--no-telemetry").ExitCode == 0,
            "--project baseline recovery failed after the aggregate structural-budget failure.");
        Assert.True(CaptureCommand("check", projectA, "--project", projectA, "--baseline", baselinePath, "--no-telemetry").ExitCode == 0,
            "--project check recovery failed after the aggregate structural-budget failure.");

        var inventoryRoot = Path.Combine(scratch, "invocation-inventory-budget");
        var inventoryFiles = Enumerable.Range(0, 10_001)
            .Select(index => $"lib/net8.0/inventory-{index.ToString(CultureInfo.InvariantCulture)}.dat")
            .ToArray();
        foreach (var name in projects)
        {
            var projectRoot = Path.Combine(inventoryRoot, name);
            var obj = Path.Combine(projectRoot, "obj");
            Directory.CreateDirectory(obj);
            File.WriteAllText(Path.Combine(projectRoot, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            WriteAssets(Path.Combine(obj, "project.assets.json"), Path.Combine(projectRoot, "cache"), name + ".Inventory", inventoryFiles, projectFileName: name + ".csproj");
        }

        var inventoryFailure = CaptureCommand("scan", inventoryRoot, "--format", "json", "--no-telemetry");
        Assert.True(inventoryFailure.ExitCode == 2 && !inventoryFailure.Output.Contains("inventory-", StringComparison.Ordinal),
            "Selecting two projects did not fail closed without partial package-inventory output.");
        Assert.True(CaptureCommand("scan", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--project", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--no-telemetry").ExitCode == 0,
            "A narrowed project did not remain within the package-inventory file budget.");
    }
}
