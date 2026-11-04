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

public sealed class ReportsTests
{
    private static readonly string[] IncompleteReasons = { "restore evidence is incomplete" };

    [Fact]
    public void StrictContentEligibilityMatrix()
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
                    Assert.True(observed == expected, $"Strict-content eligibility mismatch for {capability}, present={present}, active={active}.");
                }
            }
        }
    }

    [Fact]
    public void IdentityAndReportContracts()
    {


        var baselineEntry = Entry(CapabilityKind.BuildTargets, "build/common.targets", "a") with { Project = "Consumer.csproj" };
        var versionBump = baselineEntry with { Version = "1.1.0" };
        Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { versionBump }, strictContent: false).Count == 0,
            "A version-only update changed non-strict capability identity.");
        Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { versionBump }, strictContent: true).Count == 0,
            "A version-only update changed strict capability identity.");

        var changedContent = versionBump with { Sha256 = "b" };
        var changedDiagnostics = DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { changedContent }, strictContent: true);
        Assert.True(changedDiagnostics.Count(diagnostic => diagnostic.Id == "PS005") == 1 && changedDiagnostics.All(diagnostic => diagnostic.Id == "PS005"),
            "A version bump with changed content did not produce only PS005.");

        var addedPath = versionBump with { PackageRelativePath = "build/new.targets" };
        var addedDiagnostics = DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { addedPath }, strictContent: false);
        Assert.True(addedDiagnostics.Any(diagnostic => diagnostic.Id == "PS002") && addedDiagnostics.Any(diagnostic => diagnostic.Id == "PS003"),
            "A new active path did not produce scoped capability diagnostics.");

        var differentPackage = versionBump with { PackageId = "Other.Package" };
        Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { differentPackage }, strictContent: false).Any(diagnostic => diagnostic.Id == "PS001"),
            "A package identity change was incorrectly treated as a version-only update.");

        var relationshipChange = versionBump with { Relationship = "transitive" };
        Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(baselineEntry) }, new[] { relationshipChange }, strictContent: false).Any(diagnostic => diagnostic.Id == "PS003"),
            "A direct/transitive relationship change was incorrectly ignored.");

        var reportEntry = baselineEntry with { Present = false, Active = false, ObservedPrimitives = new[] { "Import", "UsingTask" } };
        var report = ReportDocument.Create(CommandKind.Scan, new SurfaceSnapshot(new[] { reportEntry }, Array.Empty<string>(), false, 1), Array.Empty<Diagnostic>());
        var text = report.ToText();
        Assert.True(text.Contains("present=False", StringComparison.Ordinal) && text.Contains("active=False", StringComparison.Ordinal) &&
                text.Contains("project=Consumer.csproj", StringComparison.Ordinal) && text.Contains("target=net8.0", StringComparison.Ordinal) &&
                text.Contains("primitives=Import,UsingTask", StringComparison.Ordinal),
            "Default report omitted independent state or target context.");
        var sarif = report.ToSarif();
        Assert.True(sarif.Runs.Single().Results.Count == 1 && sarif.Runs.Single().Results[0].RuleId == "PS-SURFACE",
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
