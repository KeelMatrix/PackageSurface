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
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class TelemetryTests
{
    [Fact]
    public void EligibilityDoesNotExposePackageIdentity()
    {


        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-telemetry-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var cache = Path.Combine(scratch, "cache");
            var assets = Path.Combine(scratch, "project.assets.json");
            var baseline = Path.Combine(scratch, "baseline.json");
            WriteAssets(assets, cache, "TelemetryPackage", Array.Empty<string>());
            var attempts = 0;
            SetTelemetryHook(() => attempts++);

            Assert.True(CommandLine.Run(new[] { "baseline", assets, "--output", baseline }) == 0 && attempts == 1,
                "Successful baseline creation did not count activation.");
            attempts = 0;
            Assert.True(CommandLine.Run(new[] { "check", assets, "--baseline", baseline }) == 0 && attempts == 1,
                "Unchanged check did not count activation.");

            var emptyAssets = Path.Combine(scratch, "empty-project.assets.json");
            var emptyDocument = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            emptyDocument["targets"] = new JsonObject { ["net8.0"] = new JsonObject() };
            emptyDocument["libraries"] = new JsonObject();
            emptyDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonObject();
            File.WriteAllText(emptyAssets, emptyDocument.ToJsonString());
            WriteGeneratedImportEvidence(emptyAssets);
            attempts = 0;
            var emptyBaseline = Path.Combine(scratch, "empty-baseline.json");
            Assert.True(CommandLine.Run(new[] { "baseline", emptyAssets, "--output", emptyBaseline }) == 0 && attempts == 0,
                "A successful baseline with no resolved packages incorrectly counted activation.");
            Assert.True(CommandLine.Run(new[] { "check", emptyAssets, "--baseline", emptyBaseline }) == 0 && attempts == 0,
                "A successful check with no resolved packages incorrectly counted activation.");

            var changed = JsonNode.Parse(File.ReadAllText(baseline))!.AsObject();
            changed["entries"] = new JsonArray
        {
            new JsonObject
            {
                ["project"] = "project.csproj",
                ["context"] = "Target",
                ["targetFramework"] = "net8.0",
                ["runtimeIdentifier"] = null,
                ["packageId"] = "Missing.Approved.Package",
                ["version"] = "1.0.0",
                ["relationship"] = "direct",
                ["capability"] = "BuildTargets",
                ["packageRelativePath"] = "build/missing.targets",
                ["present"] = true,
                ["active"] = true,
                ["sha256"] = null,
                ["incomplete"] = false,
                ["incompleteReason"] = null
            }
        };
            var changedBaseline = Path.Combine(scratch, "changed-baseline.json");
            File.WriteAllText(changedBaseline, changed.ToJsonString());
            attempts = 0;
            Assert.True(CommandLine.Run(new[] { "check", assets, "--baseline", changedBaseline }) == 1 && attempts == 1,
                "Completed exit-1 surface difference did not count activation.");

            var incompleteAssets = Path.Combine(scratch, "incomplete.assets.json");
            WriteAssets(incompleteAssets, cache, "IncompletePackage", new List<string> { "build/missing.targets" });
            attempts = 0;
            Assert.True(CommandLine.Run(new[] { "check", incompleteAssets, "--baseline", baseline }) == 2 && attempts == 0,
                "PS007 analysis incorrectly counted activation.");
            attempts = 0;
            Assert.True(CommandLine.Run(new List<string> { "--invalid" }.ToArray()) == 2 && attempts == 0,
                "Invalid invocation incorrectly counted activation.");
            attempts = 0;
            var optOutBaseline = Path.Combine(scratch, "opt-out-baseline.json");
            Assert.True(CommandLine.Run(new[] { "baseline", assets, "--output", optOutBaseline, "--no-telemetry" }) == 0 && attempts == 0,
                "The per-invocation telemetry opt-out did not suppress activation.");

            attempts = 0;
            var missingValue = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry" };
            Assert.True(CommandLine.Run(missingValue.ToArray()) == 2 && attempts == 0,
                "The removed --telemetry option without a value was accepted.");
            attempts = 0;
            var emptyValue = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry=" };
            Assert.True(CommandLine.Run(emptyValue.ToArray()) == 2 && attempts == 0,
                "The removed --telemetry option with an empty value was accepted.");

            foreach (var value in new[] { "on", "off", "ON", "OFF" })
            {
                attempts = 0;
                var splitOption = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry", value };
                Assert.True(CommandLine.Run(splitOption.ToArray()) == 2 && attempts == 0,
                    $"The removed --telemetry {value} option was accepted.");

                attempts = 0;
                var equalsOption = new List<string> { "baseline", assets, "--output", optOutBaseline, $"--telemetry={value}" };
                Assert.True(CommandLine.Run(equalsOption.ToArray()) == 2 && attempts == 0,
                    $"The removed --telemetry={value} option was accepted.");
            }
        }
        finally
        {
            SetTelemetryHook(null);
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void ParserTokensAreNeverReported()
    {


        const string marker = "ParserSecretMarker-7f1c2a";
        var untrustedTokens = new[]
        {
        $"C:\\PRIVATE\\{marker}",
        $"/home/PRIVATE/{marker}",
        marker
    };
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            foreach (var token in untrustedTokens)
            {
                foreach (var arguments in new[]
                {
                new[] { token, "--no-telemetry" },
                new[] { "scan", "safe-input", $"--unknown={token}", "--format", format, "--no-telemetry" }
            })
                {
                    var error = new StringWriter(CultureInfo.InvariantCulture);
                    var output = new StringWriter(CultureInfo.InvariantCulture);
                    var priorError = Console.Error;
                    var priorOutput = Console.Out;
                    try
                    {
                        Console.SetError(error);
                        Console.SetOut(output);
                        Assert.True(CommandLine.Run(arguments) == 2, "A token privacy parser case did not return exit code 2.");
                    }
                    finally
                    {
                        Console.SetError(priorError);
                        Console.SetOut(priorOutput);
                    }

                    var combined = error.ToString() + output;
                    Assert.True(!combined.Contains(marker, StringComparison.Ordinal) &&
                            !combined.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"Parser diagnostic leaked untrusted token data for {format} output mode.");
                }
            }
        }
    }
}
