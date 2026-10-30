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

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static partial class ScenarioSupport
{
    internal static SurfaceEntry Entry(CapabilityKind capability, string path, string hash, bool active = true, bool present = true) => new(
        "net8.0",
        capability == CapabilityKind.NativeRuntime ? "win-x64" : null,
        SurfaceContextKind.Target,
        "Example.Package",
        "1.0.0",
        "direct",
        capability,
        path,
        present,
        active,
        hash,
        false,
        null,
        "Example.csproj");

    internal static void RunStrictContentEligibilityMatrixTests()
    {
        foreach (var capability in Enum.GetValues<CapabilityKind>())
        {
            foreach (var present in new[] { false, true })
            {
                foreach (var active in new[] { false, true })
                {
                    var baselineEntry = Entry(capability, "build/eligibility.asset", "old", active, present);
                    var currentEntry = Entry(capability, "build/eligibility.asset", "new", active, present);
                    var diagnostics = DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { currentEntry }, strictContent: true);
                    var observed = diagnostics.Any(diagnostic => diagnostic.Id == "PS005");
                    var expected = CapabilityPolicy.IsStrictContentEligible(capability, present, active);
                    Require(observed == expected, $"Strict-content eligibility mismatch for {capability}, present={present}, active={active}.");
                }
            }
        }
    }

    internal static void RunIdentityAndReportContractTests()
    {
        var baselineEntry = Entry(CapabilityKind.BuildTargets, "build/common.targets", "a") with { Project = "Consumer.csproj" };
        var versionBump = baselineEntry with { Version = "1.1.0" };
        Require(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { versionBump }, strictContent: false).Count == 0,
            "A version-only update changed non-strict capability identity.");
        Require(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { versionBump }, strictContent: true).Count == 0,
            "A version-only update changed strict capability identity.");

        var changedContent = versionBump with { Sha256 = "b" };
        var changedDiagnostics = DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { changedContent }, strictContent: true);
        Require(changedDiagnostics.Count(diagnostic => diagnostic.Id == "PS005") == 1 && changedDiagnostics.All(diagnostic => diagnostic.Id == "PS005"),
            "A version bump with changed content did not produce only PS005.");

        var addedPath = versionBump with { PackageRelativePath = "build/new.targets" };
        var addedDiagnostics = DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { addedPath }, strictContent: false);
        Require(addedDiagnostics.Any(diagnostic => diagnostic.Id == "PS002") && addedDiagnostics.Any(diagnostic => diagnostic.Id == "PS003"),
            "A new active path did not produce scoped capability diagnostics.");

        var differentPackage = versionBump with { PackageId = "Other.Package" };
        Require(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { differentPackage }, strictContent: false).Any(diagnostic => diagnostic.Id == "PS001"),
            "A package identity change was incorrectly treated as a version-only update.");

        var relationshipChange = versionBump with { Relationship = "transitive" };
        Require(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { relationshipChange }, strictContent: false).Any(diagnostic => diagnostic.Id == "PS003"),
            "A direct/transitive relationship change was incorrectly ignored.");

        var reportEntry = baselineEntry with { Present = false, Active = false, ObservedPrimitives = new[] { "Import", "UsingTask" } };
        var report = ReportDocument.Create(CommandKind.Scan, new SurfaceSnapshot(new[] { reportEntry }, Array.Empty<string>(), false, 1), Array.Empty<Diagnostic>());
        var text = report.ToText();
        Require(text.Contains("present=False", StringComparison.Ordinal) && text.Contains("active=False", StringComparison.Ordinal) &&
                text.Contains("project=Consumer.csproj", StringComparison.Ordinal) && text.Contains("target=net8.0", StringComparison.Ordinal) &&
                text.Contains("primitives=Import,UsingTask", StringComparison.Ordinal),
            "Default report omitted independent state or target context.");
        var sarif = report.ToSarif();
        Require(sarif.Runs.Single().Results.Count == 1 && sarif.Runs.Single().Results[0].RuleId == "PS-SURFACE",
            "Successful scan SARIF silently discarded classified surface facts.");

        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-schema-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var invalidRid = BaselineEntry.From(baselineEntry with { Context = SurfaceContextKind.Project, TargetFramework = null, RuntimeIdentifier = "win-x64" });
            var invalidPrimitive = BaselineEntry.From(baselineEntry with { Capability = CapabilityKind.NativeRuntime, ObservedPrimitives = new List<string> { "Import" } });
            var duplicate = BaselineEntry.From(baselineEntry);
            foreach (var (name, entriesToWrite) in new[]
            {
            ("rid", new[] { invalidRid }),
            ("primitive", new[] { invalidPrimitive }),
            ("duplicate", new[] { duplicate, duplicate })
        })
            {
                var path = Path.Combine(scratch, name + ".json");
                File.WriteAllText(path, JsonSerializer.Serialize(new BaselineDocument(1, "0.1.0", false, entriesToWrite, Array.Empty<string>()), JsonOptions.Indented));
                try { _ = BaselineDocument.Read(path); throw new InvalidOperationException($"Invalid baseline '{name}' was accepted."); }
                catch (InvalidDataException) { }
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    internal static void RunParserMessageRegression()
    {
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var priorError = Console.Error;
        try
        {
            Console.SetError(error);
            var arguments = new[] { "scan", "safe-input", "--format" };
            Require(CommandLine.Run(arguments) == 2, "Missing option value did not return exit code 2.");
        }
        finally
        {
            Console.SetError(priorError);
        }

        var text = error.ToString();
        Require(text.Contains("--format requires a value", StringComparison.Ordinal) &&
                !text.Contains("invalid command-line arguments", StringComparison.Ordinal) &&
                !text.Contains("safe-input", StringComparison.Ordinal),
            "Missing option value lost its safe specific parser message or echoed input text.");
    }

    internal static void RunParserTokenPrivacyRegression()
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
                        Require(CommandLine.Run(arguments) == 2, "A token privacy parser case did not return exit code 2.");
                    }
                    finally
                    {
                        Console.SetError(priorError);
                        Console.SetOut(priorOutput);
                    }

                    var combined = error.ToString() + output;
                    Require(!combined.Contains(marker, StringComparison.Ordinal) &&
                            !combined.Contains(token, StringComparison.OrdinalIgnoreCase),
                        $"Parser diagnostic leaked untrusted token data for {format} output mode.");
                }
            }
        }
    }

    internal static void RunTelemetryEligibilityTests()
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

            Require(CommandLine.Run(new[] { "baseline", assets, "--output", baseline }) == 0 && attempts == 1,
                "Successful baseline creation did not count activation.");
            attempts = 0;
            Require(CommandLine.Run(new[] { "check", assets, "--baseline", baseline }) == 0 && attempts == 1,
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
            Require(CommandLine.Run(new[] { "baseline", emptyAssets, "--output", emptyBaseline }) == 0 && attempts == 0,
                "A successful baseline with no resolved packages incorrectly counted activation.");
            Require(CommandLine.Run(new[] { "check", emptyAssets, "--baseline", emptyBaseline }) == 0 && attempts == 0,
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
            Require(CommandLine.Run(new[] { "check", assets, "--baseline", changedBaseline }) == 1 && attempts == 1,
                "Completed exit-1 surface difference did not count activation.");

            var incompleteAssets = Path.Combine(scratch, "incomplete.assets.json");
            WriteAssets(incompleteAssets, cache, "IncompletePackage", new List<string> { "build/missing.targets" });
            attempts = 0;
            Require(CommandLine.Run(new[] { "check", incompleteAssets, "--baseline", baseline }) == 2 && attempts == 0,
                "PS007 analysis incorrectly counted activation.");
            attempts = 0;
            Require(CommandLine.Run(new List<string> { "--invalid" }.ToArray()) == 2 && attempts == 0,
                "Invalid invocation incorrectly counted activation.");
            attempts = 0;
            var optOutBaseline = Path.Combine(scratch, "opt-out-baseline.json");
            Require(CommandLine.Run(new[] { "baseline", assets, "--output", optOutBaseline, "--no-telemetry" }) == 0 && attempts == 0,
                "The per-invocation telemetry opt-out did not suppress activation.");

            attempts = 0;
            var missingValue = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry" };
            Require(CommandLine.Run(missingValue.ToArray()) == 2 && attempts == 0,
                "The removed --telemetry option without a value was accepted.");
            attempts = 0;
            var emptyValue = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry=" };
            Require(CommandLine.Run(emptyValue.ToArray()) == 2 && attempts == 0,
                "The removed --telemetry option with an empty value was accepted.");

            foreach (var value in new[] { "on", "off", "ON", "OFF" })
            {
                attempts = 0;
                var splitOption = new List<string> { "baseline", assets, "--output", optOutBaseline, "--telemetry", value };
                Require(CommandLine.Run(splitOption.ToArray()) == 2 && attempts == 0,
                    $"The removed --telemetry {value} option was accepted.");

                attempts = 0;
                var equalsOption = new List<string> { "baseline", assets, "--output", optOutBaseline, $"--telemetry={value}" };
                Require(CommandLine.Run(equalsOption.ToArray()) == 2 && attempts == 0,
                    $"The removed --telemetry={value} option was accepted.");
            }
        }
        finally
        {
            SetTelemetryHook(null);
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

}
