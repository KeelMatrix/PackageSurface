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
    internal static void RunBaselineContractRegression()
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
            Require(roundTrip.Entries.Single().Project == "../Library/Library.csproj", "A sibling solution project identity did not round-trip through the baseline contract.");

            var prior = File.ReadAllText(path);
            var invalid = snapshot with { Entries = new[] { sibling with { Project = Path.Combine(scratch, "absolute.csproj") } } };
            try
            {
                BaselineDocument.Write(path, invalid);
                throw new InvalidOperationException("An invalid project identity was written to the baseline.");
            }
            catch (InvalidDataException)
            {
                Require(File.ReadAllText(path) == prior, "A rejected baseline write did not preserve the previous baseline.");
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
                Require(File.ReadAllText(path) == prior, "An oversized baseline failure replaced the previous baseline.");
            }

            try
            {
                ReportDocument.Create(CommandKind.Scan, oversizedSnapshot, Array.Empty<Diagnostic>()).EnsureOutputWithinLimit(16 * 1024 * 1024);
                throw new InvalidOperationException("An oversized report passed the output preflight.");
            }
            catch (InvalidDataException)
            {
            }

            var project = Path.Combine(scratch, "Selected.csproj");
            File.WriteAllText(project, "<Project />");
            Require(CommandLine.Run(new[] { "scan", Path.Combine(scratch, "missing.sln"), "--project", project, "--no-telemetry" }) == 2,
                "--project bypassed validation of a missing primary input.");

            var manyProjects = Path.Combine(scratch, "many-projects");
            Directory.CreateDirectory(manyProjects);
            for (var index = 0; index < 129; index++)
            {
                File.WriteAllText(Path.Combine(manyProjects, $"Project{index:000}.csproj"), "<Project />");
            }

            var selectedAssets = Path.Combine(manyProjects, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(selectedAssets)!);
            WriteAssets(selectedAssets, Path.Combine(manyProjects, "cache"), "Narrowed.Package", Array.Empty<string>(), projectFileName: "Project000.csproj");
            File.WriteAllText(Path.Combine(manyProjects, "Project000.csproj"), "<Project />");
            Require(CommandLine.Run(new[] { "scan", manyProjects, "--project", Path.Combine(manyProjects, "Project000.csproj"), "--no-telemetry" }) == 0,
                "--project did not narrow a directory with more than 128 members.");

            var nonMember = Path.Combine(scratch, "not-a-member.csproj");
            File.WriteAllText(nonMember, "<Project />");
            Require(CommandLine.Run(new[] { "scan", manyProjects, "--project", nonMember, "--no-telemetry" }) == 2,
                "--project accepted a project outside a large directory input.");
            RunBaselineTransactionRegression(scratch);
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    internal static void RunBaselineTransactionRegression(string parent)
    {
        var root = Path.Combine(parent, "baseline-transaction");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var baseline = Path.Combine(root, "approved.json");
        WriteAssets(assets, cache, "Transaction.Package", Array.Empty<string>());
        Require(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--no-telemetry" }) == 0,
            "The transaction baseline seed did not succeed.");
        var approved = File.ReadAllBytes(baseline);
        RunTerminalIncompleteReportRegression(root);

        var aliasedAssets = File.ReadAllBytes(assets);
        Require(CaptureCommand("baseline", assets, "--output", assets, "--no-telemetry").ExitCode == 2,
            "A baseline output equal to project.assets.json was accepted.");
        Require(aliasedAssets.SequenceEqual(File.ReadAllBytes(assets)), "An aliased baseline attempt changed project.assets.json.");

        var hardlinkOutputs = new[]
        {
        Path.Combine(root, "hardlink-output.json"),
        Path.Combine(root, "hardlink-output-second.json")
    };
        Require(TryCreateHardLink(assets, hardlinkOutputs[0]), "The hardlink alias regression could not create a hardlink.");
        Require(TryCreateHardLink(assets, hardlinkOutputs[1]), "The multiple-hardlink alias regression could not create a second hardlink.");
        try
        {
            foreach (var hardlinkOutput in hardlinkOutputs)
            {
                Require(CaptureCommand("baseline", assets, "--output", hardlinkOutput, "--no-telemetry").ExitCode == 2,
                    "A hardlink alias to project.assets.json was accepted.");
                Require(aliasedAssets.SequenceEqual(File.ReadAllBytes(assets)), "A hardlink alias rejection changed project.assets.json.");
            }
        }
        finally
        {
            foreach (var hardlinkOutput in hardlinkOutputs)
            {
                if (File.Exists(hardlinkOutput)) File.Delete(hardlinkOutput);
            }
        }

        var symlinkOutput = Path.Combine(root, "symlink-output.json");
        try
        {
            File.CreateSymbolicLink(symlinkOutput, assets);
            var symlinkInputBefore = File.ReadAllBytes(assets);
            Require(CaptureCommand("baseline", assets, "--output", symlinkOutput, "--no-telemetry").ExitCode == 2,
                "A symlink alias to project.assets.json was accepted.");
            Require(symlinkInputBefore.SequenceEqual(File.ReadAllBytes(assets)), "A symlink alias rejection changed project.assets.json.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this platform."); }
        finally
        {
            if (File.Exists(symlinkOutput)) File.Delete(symlinkOutput);
        }

        var aliasedGenerated = Path.Combine(obj, "Test.csproj.nuget.g.targets");
        var generatedBefore = File.ReadAllBytes(aliasedGenerated);
        Require(CaptureCommand("baseline", assets, "--output", Path.Combine(obj, ".", "Test.csproj.nuget.g.targets"), "--no-telemetry").ExitCode == 2,
            "A baseline output equal to a generated restore import was accepted.");
        Require(generatedBefore.SequenceEqual(File.ReadAllBytes(aliasedGenerated)), "An aliased baseline attempt changed generated restore evidence.");

        var boundaryOutput = Path.Combine(root, "boundary.json");
        File.WriteAllBytes(boundaryOutput, new byte[16 * 1024 * 1024]);
        Require(CaptureCommand("baseline", assets, "--output", boundaryOutput, "--no-telemetry").ExitCode == 0,
            "A pre-existing baseline exactly at the size limit was rejected.");

        var oversizedOutput = Path.Combine(root, "oversized.json");
        File.WriteAllBytes(oversizedOutput, new byte[(16 * 1024 * 1024) + 1]);
        var oversizedBefore = File.ReadAllBytes(oversizedOutput);
        Require(CaptureCommand("baseline", assets, "--output", oversizedOutput, "--no-telemetry").ExitCode == 2,
            "A pre-existing oversized baseline was accepted.");
        Require(oversizedBefore.SequenceEqual(File.ReadAllBytes(oversizedOutput)), "Oversized baseline rejection changed prior data.");

        var projectInput = Path.Combine(root, "Test.csproj");
        File.WriteAllText(projectInput, "<Project />");
        var projectBefore = File.ReadAllBytes(projectInput);
        Require(CaptureCommand("baseline", assets, "--output", Path.Combine(root, ".", "Test.csproj"), "--no-telemetry").ExitCode == 2,
            "A baseline output equal to the selected project was accepted.");
        Require(projectBefore.SequenceEqual(File.ReadAllBytes(projectInput)), "A project-alias rejection changed the project.");

        var readOnlyOutput = Path.Combine(root, "readonly.json");
        File.WriteAllBytes(readOnlyOutput, approved);
        File.SetAttributes(readOnlyOutput, FileAttributes.ReadOnly);
        try
        {
            var readOnlyBefore = File.ReadAllBytes(readOnlyOutput);
            Require(CaptureCommand("baseline", assets, "--output", readOnlyOutput, "--no-telemetry").ExitCode == 2,
                "A read-only baseline output was accepted.");
            Require(readOnlyBefore.SequenceEqual(File.ReadAllBytes(readOnlyOutput)), "A read-only baseline failure changed prior data.");
        }
        finally
        {
            File.SetAttributes(readOnlyOutput, FileAttributes.Normal);
        }

        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            try
            {
                SetOutputSerializer((_, _) => new string('x', 16 * 1024 * 1024 + 1));
                Console.SetOut(output);
                var exitCode = CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", format, "--no-telemetry" });
                Require(exitCode == 2, $"An actual-size {format} output overflow returned exit {exitCode}.");
            }
            finally
            {
                Console.SetOut(priorOutput);
                SetOutputSerializer(null);
            }

            Require(approved.SequenceEqual(File.ReadAllBytes(baseline)), $"An actual-size {format} output overflow replaced the approved baseline.");
            Require(!output.ToString().Contains("Entries:", StringComparison.Ordinal), $"An actual-size {format} output overflow emitted partial success output.");
        }

        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            try
            {
                SetOutputSerializer((_, _) => throw new InvalidOperationException("serializer failure"));
                Console.SetOut(output);
                Require(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", "json", "--no-telemetry" }) == 2,
                    "A serializer failure did not return exit 2.");
            }
            finally
            {
                Console.SetOut(priorOutput);
                SetOutputSerializer(null);
            }

            Require(approved.SequenceEqual(File.ReadAllBytes(baseline)) && !output.ToString().Contains("Entries:", StringComparison.Ordinal),
                "A serializer failure did not roll back the baseline transaction.");
        }
        finally
        {
            SetOutputSerializer(null);
        }

        try
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            try
            {
                SetOutputSink(_ => throw new InvalidOperationException("output failure"));
                Console.SetOut(output);
                Require(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--format", "text", "--no-telemetry" }) == 2,
                    "An output sink failure did not return exit 2.");
            }
            finally
            {
                Console.SetOut(priorOutput);
                SetOutputSink(null);
            }

            Require(approved.SequenceEqual(File.ReadAllBytes(baseline)) && !output.ToString().Contains("Entries:", StringComparison.Ordinal),
                "An output sink failure did not roll back the baseline transaction.");
        }
        finally
        {
            SetOutputSink(null);
        }

        RunReachablePackageAliasRegression(parent);
    }

    internal static void RunTerminalIncompleteReportRegression(string parent)
    {
        var root = Path.Combine(parent, "late-terminal-incomplete");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var baseline = Path.Combine(root, "approved.json");
        WriteAssets(assets, cache, "Late.Strict.Package", new List<string> { "build/late.targets" }, createFiles: true);
        File.WriteAllText(Path.Combine(cache, "Late.Strict.Package", "1.0.0", "build", "late.targets"), "<Project />");
        Require(CommandLine.Run(new[] { "baseline", assets, "--output", baseline, "--no-telemetry" }) == 0,
            "The late strict-content baseline seed did not succeed.");

        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var result = CaptureCommand("check", assets, "--baseline", baseline, "--strict-content", "--format", format, "--no-telemetry");
            Require(result.ExitCode == 2 && result.Output.Contains("PS007", StringComparison.Ordinal),
                $"Late strict-baseline mismatch did not return PS007 for {format}.");
            AssertNoSuccessfulSurfaceOutput(result.Output, format, "late strict-baseline mismatch");
            if (format.Equals("json", StringComparison.Ordinal))
            {
                using var document = JsonDocument.Parse(result.Output);
                Require(document.RootElement.GetProperty("incompleteReasons").GetArrayLength() > 0,
                    "Late strict-baseline mismatch omitted incompleteReasons from JSON.");
            }
        }
    }

    internal static void RunBaselineJsonBoundaryRegression()
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
            Require(File.ReadAllBytes(first).SequenceEqual(File.ReadAllBytes(second)), "Canonical baseline write-read-write bytes were not stable.");

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
