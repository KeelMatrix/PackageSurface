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
using static KeelMatrix.PackageSurface.Cli.Tests.FilesystemFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.SurfaceEntryFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class BaselinePersistenceTests
{
    [Fact]
    public void BaselineWritesRemainAtomicAndFailClosed()
    {


        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-baseline-contract-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var path = Path.Combine(scratch, "package-surface.json");
            var sibling = Entry(CapabilityKind.BuildTargets, "build/Library.targets", new string('a', 64)) with { Project = "../Library/Library.csproj" };
            var snapshot = new SurfaceSnapshot(new[] { sibling }, Array.Empty<string>(), false, 1);
            BaselineDocument.Write(path, snapshot);
            var roundTrip = BaselineDocument.Read(path);
            Assert.True(roundTrip.Entries.Single().Project == "../Library/Library.csproj", "A sibling solution project identity did not round-trip through the baseline contract.");

            var prior = File.ReadAllText(path);
            var invalid = snapshot with { Entries = new[] { sibling with { Project = Path.Combine(scratch, "absolute.csproj") } } };
            try
            {
                BaselineDocument.Write(path, invalid);
                throw new InvalidOperationException("An invalid project identity was written to the baseline.");
            }
            catch (InvalidDataException)
            {
                Assert.True(File.ReadAllText(path) == prior, "A rejected baseline write did not preserve the previous baseline.");
            }

            var oversizedEntries = Enumerable.Range(0, 40_000)
                .Select(index => Entry(CapabilityKind.BuildTargets, $"build/Generated{index}.targets", new string('b', 64)) with
                {
                    PackageId = "Generated.Package." + index
                })
                .ToArray();
            var oversizedSnapshot = new SurfaceSnapshot(oversizedEntries, Array.Empty<string>(), false, oversizedEntries.Length);
            try
            {
                BaselineDocument.Write(path, oversizedSnapshot);
                throw new InvalidOperationException("An oversized baseline was written.");
            }
            catch (InvalidDataException)
            {
                Assert.True(File.ReadAllText(path) == prior, "An oversized baseline failure replaced the previous baseline.");
            }

            try
            {
                ReportDocument.Create(CommandKind.Scan, oversizedSnapshot, Array.Empty<Diagnostic>()).EnsureOutputWithinLimit(16 * 1024 * 1024);
                throw new InvalidOperationException("An oversized report passed the output preflight.");
            }
            catch (InvalidDataException)
            {
            }

        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }
    [Fact]
    public void ExistingBaselineAtSizeBoundaryCanBeReplaced()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var boundaryOutput = Path.Combine(root, "boundary.json");
        File.WriteAllBytes(boundaryOutput, new byte[16 * 1024 * 1024]);
        Assert.True(CaptureCommand("baseline", assets, "--output", boundaryOutput, "--no-telemetry").ExitCode == 0,
            "A pre-existing baseline exactly at the size limit was rejected.");


    }

    [Fact]
    public void OversizedBaselinePreservesPreviousBytes()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var oversizedOutput = Path.Combine(root, "oversized.json");
        File.WriteAllBytes(oversizedOutput, new byte[(16 * 1024 * 1024) + 1]);
        var oversizedBefore = File.ReadAllBytes(oversizedOutput);
        Assert.True(CaptureCommand("baseline", assets, "--output", oversizedOutput, "--no-telemetry").ExitCode == 2,
            "A pre-existing oversized baseline was accepted.");
        Assert.True(oversizedBefore.SequenceEqual(File.ReadAllBytes(oversizedOutput)), "Oversized baseline rejection changed prior data.");


    }

    [Fact]
    public void ReadOnlyBaselinePreservesPreviousBytes()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var readOnlyOutput = Path.Combine(root, "readonly.json");
        File.WriteAllBytes(readOnlyOutput, approved);
        File.SetAttributes(readOnlyOutput, FileAttributes.ReadOnly);
        try
        {
            var readOnlyBefore = File.ReadAllBytes(readOnlyOutput);
            Assert.True(CaptureCommand("baseline", assets, "--output", readOnlyOutput, "--no-telemetry").ExitCode == 2,
                "A read-only baseline output was accepted.");
            Assert.True(readOnlyBefore.SequenceEqual(File.ReadAllBytes(readOnlyOutput)), "A read-only baseline failure changed prior data.");
        }
        finally
        {
            File.SetAttributes(readOnlyOutput, FileAttributes.Normal);
        }


    }

    [Fact]
    public void IncompleteAnalysisPreservesTheExistingBaseline()
    {
        using var workspace = new CliScenarioWorkspace();
        var parent = workspace.Root;

        var root = Path.Combine(parent, "late-terminal-incomplete");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var baseline = Path.Combine(root, "approved.json");
        WriteAssets(assets, cache, "Late.Strict.Package", new List<string> { "build/late.targets" }, createFiles: true);
        File.WriteAllText(Path.Combine(cache, "Late.Strict.Package", "1.0.0", "build", "late.targets"), "<Project />");
        Assert.True(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--no-telemetry" }) == 0,
            "The late strict-content baseline seed did not succeed.");

        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var result = CaptureCommand("check", assets, "--baseline", baseline, "--strict-content", "--format", format, "--no-telemetry");
            Assert.True(result.ExitCode == 2 && result.Output.Contains("PS007", StringComparison.Ordinal),
                $"Late strict-baseline mismatch did not return PS007 for {format}.");
            AssertNoSuccessfulSurfaceOutput(result.Output, format, "late strict-baseline mismatch");
            if (format.Equals("json", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(result.Output);
                Assert.True(document.RootElement.GetProperty("incompleteReasons").GetArrayLength() > 0,
                    "Late strict-baseline mismatch omitted incompleteReasons from JSON.");
            }
        }
    }

    [Fact]
    public void BaselineJsonBoundariesAreValidated()
    {


        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-json-boundary-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var entry = Entry(CapabilityKind.BuildTargets, "build/Schema.targets", new string('a', 64)) with { Project = "Consumer.csproj" };
            var first = Path.Combine(scratch, "first.json");
            var second = Path.Combine(scratch, "second.json");
            var snapshot = new SurfaceSnapshot(new[] { entry }, Array.Empty<string>(), false, 1);
            BaselineDocument.Write(first, snapshot);
            var roundTrip = BaselineDocument.Read(first);
            BaselineDocument.Write(second, new SurfaceSnapshot(roundTrip.Entries.Select(value => new SurfaceEntry(value.TargetFramework, value.RuntimeIdentifier, value.Context, value.PackageId, value.Version, value.Relationship, value.Capability, value.PackageRelativePath, value.Present, value.Active, value.Sha256, value.Incomplete, value.IncompleteReason, value.Project, value.ObservedPrimitives)).ToArray(), Array.Empty<string>(), roundTrip.StrictContent, 1));
            Assert.True(File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)), "Canonical baseline write-read-write bytes were not stable.");

            var canonical = JsonSerializer.Serialize(new BaselineDocument(1, "0.1.0", false, new[] { BaselineEntry.From(entry) }, Array.Empty<string>()), JsonOptions.Default);
            var cases = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["exact duplicate"] = canonical.Replace("\"schemaVersion\":1,", "\"schemaVersion\":1,\"schemaVersion\":1,", StringComparison.Ordinal),
                ["case duplicate"] = canonical.Replace("\"strictContent\":false,", "\"strictContent\":false,\"StrictContent\":false,", StringComparison.Ordinal),
                ["integer enum"] = canonical.Replace("\"context\":\"Target\"", "\"context\":0", StringComparison.Ordinal),
                ["alternative enum spelling"] = canonical.Replace("\"context\":\"Target\"", "\"context\":\"target\"", StringComparison.Ordinal),
                ["unknown enum"] = canonical.Replace("\"capability\":\"BuildTargets\"", "\"capability\":\"Unknown\"", StringComparison.Ordinal),
                ["wrong primitive"] = canonical.Replace("\"packageId\":\"Example.Package\"", "\"packageId\":null", StringComparison.Ordinal),
                ["unknown member"] = canonical[..^1] + ",\"futureField\":true}",
                ["future schema"] = canonical.Replace("\"schemaVersion\":1", "\"schemaVersion\":2", StringComparison.Ordinal)
            };
            foreach (var (name, json) in cases)
            {
                var path = Path.Combine(scratch, name.Replace(' ', '-') + ".json");
                File.WriteAllText(path, json);
                try
                {
                    _ = BaselineDocument.Read(path);
                    throw new InvalidOperationException($"Baseline JSON boundary case '{name}' was accepted.");
                }
                catch (InvalidDataException)
                {
                }
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }
}
