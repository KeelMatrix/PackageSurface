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
using KeelMatrix.Telemetry;

var entries = new[]
{
    Entry(CapabilityKind.BuildProps, "build/changed.props", "a"),
    Entry(CapabilityKind.BuildTargets, "build/changed.targets", "d"),
    Entry(CapabilityKind.CompilerExtension, "analyzers/dotnet/cs/new.dll", "b"),
    Entry(CapabilityKind.NativeRuntime, "runtimes/win-x64/native/new.dll", "c")
};
var baseline = new[] { BaselineEntry.From(Entry(CapabilityKind.BuildProps, "build/changed.props", "old")) };
var diagnostics = DiffEngine.Compare(baseline, entries, strictContent: true);
var ids = diagnostics.Select(diagnostic => diagnostic.Id).ToHashSet(StringComparer.Ordinal);
var expected = new[] { "PS001", "PS002", "PS003", "PS004", "PS005", "PS006" };
if (expected.Any(id => !ids.Contains(id)))
{
    Console.Error.WriteLine("Missing diagnostics: " + string.Join(", ", expected.Where(id => !ids.Contains(id))));
    return 1;
}

RunStrictContentEligibilityMatrixTests();
RunIdentityAndReportContractTests();

var ps007 = new[] { Diagnostic.Create("PS007", "restore evidence is incomplete") };
var incompleteReasons = new[] { "restore evidence is incomplete" };
var incomplete = ReportDocument.Create(CommandKind.Check, new SurfaceSnapshot(Array.Empty<SurfaceEntry>(), incompleteReasons, false, 0), ps007);
if (incomplete.Diagnostics.Count != 1 || incomplete.Diagnostics[0].Id != "PS007")
{
    Console.Error.WriteLine("PS007 report contract failed.");
    return 1;
}

var requiredHelpClauses = new[]
{
    "Activation fields: event, tool, tool_version, telemetry_version, schema_version",
    "https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md",
    "Generated top-level and nested imports",
    "never replaces a baseline",
    "every reachable package-inventory file resolved from packageFolders",
    "global and fallback roots",
    "direct and transitive packages",
    "every TFM/RID",
    "nested static imports",
    "all inventory categories",
    "Lexical . and .. aliases",
    "single and multiple hardlinks",
    "direct, interior, and ancestor reparse/symlink aliases",
    "PS007 and controlled exit code 2 before any mutation",
    "preserves existing output and input bytes",
    "genuinely distinct outputs are accepted",
    "unknown members",
    "standard unconsumed MSBuild elements and attributes",
    "native SDK shape",
    "malformed nested",
    "compilerApiVersion",
    "non-canonical spellings of consumed JSON/XML members",
    "x- prefix",
    "urn:keelmatrix:packagesurface:extension",
    "NuGet.Versioning 7.9.0 parser/comparer"
};
var normalizedHelpText = Regex.Replace(Options.HelpText, @"\s+", " ");
var missingHelpClauses = requiredHelpClauses
    .Where(clause => !normalizedHelpText.Contains(clause, StringComparison.Ordinal))
    .ToArray();
if (missingHelpClauses.Length > 0)
{
    Console.Error.WriteLine("CLI help contract is missing: " + string.Join("; ", missingHelpClauses));
    return 1;
}

RunClassifierHardeningTests();
RunTelemetryStateMachineTests();
RunTelemetryPayloadAllowlistRegression();
RunParserMessageRegression();
RunParserTokenPrivacyRegression();
RunBaselineContractRegression();
RunPackageIdentityAndPathRegression();
RunBaselineJsonBoundaryRegression();
RunMacOsRootAliasPredicateRegression();

Console.WriteLine($"PASS: diagnostics {string.Join(", ", expected)}");
return 0;

static SurfaceEntry Entry(CapabilityKind capability, string path, string hash, bool active = true, bool present = true) => new(
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

static void RunClassifierHardeningTests()
{
    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-cli-tests-" + Guid.NewGuid().ToString("N"));
    var cache = Path.Combine(scratch, "cache");
    var obj = Path.Combine(scratch, "obj");
    Directory.CreateDirectory(cache);
    Directory.CreateDirectory(obj);

    try
    {
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, "XmlPackage", Array.Empty<string>());

        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), "<!DOCTYPE Project [<!ENTITY external SYSTEM 'file:///outside'>]><Project><Import Project='&external;' /></Project>");
        var dtdResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(!dtdResult.IsComplete, "DTD input was not rejected as incomplete.");

        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), "<Project />");
        var deepXml = new StringBuilder("<Project>");
        for (var depth = 0; depth < 80; depth++)
        {
            deepXml.Append("<ImportGroup>");
        }
        deepXml.Append("<Import Project=\"unused.props\" />");
        for (var depth = 0; depth < 80; depth++)
        {
            deepXml.Append("</ImportGroup>");
        }
        deepXml.Append("</Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), deepXml.ToString());
        var deepXmlResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(!deepXmlResult.IsComplete && deepXmlResult.IncompleteReasons.Any(reason => reason.Contains("XML depth", StringComparison.OrdinalIgnoreCase)),
            "Deep XML nesting was not bounded during parsing.");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), "<Project />");
        File.WriteAllText(Path.Combine(scratch, "Test.csproj"), "<Project />");
        RunAnalyzerExclusionRegression(scratch, cache, obj);
        RunMalformedAssetsShapeRegression(assets, scratch);
        RunPackageFoldersValidationRegression(assets, scratch);
        RunAssetsFormatRegression(assets, scratch);
        RunCapabilityGroupValidationRegression(scratch);
        RunFrameworkMonikerRegression(assets, scratch);
        RunRestoreIdentityCanonicalizationRegression(assets, scratch);
        RunRestoreJsonStructuralDuplicateRegression(assets, scratch);
        RunRestoreOptionalMetadataRegression(assets, scratch);
        RunRestoreMetadataShapeRegression(assets, scratch);
        RunReachabilityClosureRegression(scratch);
        RunVersionAwareReachabilityConflictRegression(scratch);
        RunVersionEquivalenceRegression(scratch);
        RunMalformedVersionEndToEndRegression(scratch);
        RunDiagnosticPathLeakRegression(scratch);
        RunApplicabilityHardeningRegressions(scratch);
        RunNestedImportRegression(scratch);
        RunGeneratedImportReachabilityRegression(scratch);
        RunImportEdgeBudgetRegression(scratch);
        RunInvocationStructuralBudgetRegression(scratch);

        var traversalAssets = Path.Combine(obj, "traversal.assets.json");
        var traversalFiles = new[] { "../escape.props" };
        WriteAssets(traversalAssets, cache, "TraversalPackage", traversalFiles);
        var traversalResult = ResolvedGraphClassifier.Analyze(traversalAssets, scratch, strictContent: false);
        Require(!traversalResult.IsComplete && traversalResult.IncompleteReasons.Any(reason => reason.Contains("invalid relative asset path", StringComparison.OrdinalIgnoreCase)), "Traversal-looking package path was not rejected.");

        var invalidPeAssets = Path.Combine(obj, "invalid-pe.assets.json");
        var invalidPeFiles = new[] { "analyzers/dotnet/cs/invalid.dll" };
        WriteAssets(invalidPeAssets, cache, "InvalidPePackage", invalidPeFiles);
        var invalidPe = Path.Combine(cache, "InvalidPePackage", "1.0.0", "analyzers", "dotnet", "cs");
        Directory.CreateDirectory(invalidPe);
        File.WriteAllBytes(Path.Combine(invalidPe, "invalid.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 });
        var invalidPeResult = ResolvedGraphClassifier.Analyze(invalidPeAssets, scratch, strictContent: false);
        Require(!invalidPeResult.IsComplete && invalidPeResult.IncompleteReasons.Any(reason => reason.Contains("analyzer metadata", StringComparison.OrdinalIgnoreCase)), "Invalid compiler metadata was not rejected.");

        RunAncestorCanonicalizationRegression(scratch);

        var oversized = Path.Combine(obj, "oversized.assets.json");
        File.WriteAllText(oversized, new string('x', 16 * 1024 * 1024 + 1));
        var oversizedResult = ResolvedGraphClassifier.Analyze(oversized, scratch, strictContent: false);
        Require(!oversizedResult.IsComplete, "Oversized metadata was not rejected as incomplete.");
        Require(CommandLine.Run(new[] { "scan", oversized, "--format", "json", "--no-telemetry" }) == 2,
            "The public CLI did not fail closed before materializing oversized restore input.");

        var largeAssets = Path.Combine(obj, "large.assets.json");
        const int packageCount = 300;
        WriteLargeAssets(largeAssets, cache, packageCount);
        var timer = Stopwatch.StartNew();
        var largeResult = ResolvedGraphClassifier.Analyze(largeAssets, scratch, strictContent: false);
        timer.Stop();
        Require(largeResult.IsComplete && largeResult.ResolvedPackageCount == packageCount, "Reachable graph coverage did not classify the complete graph.");
        Require(timer.Elapsed < TimeSpan.FromSeconds(10), $"Reachable graph analysis exceeded the resource test limit: {timer.Elapsed}.");
        RunInlineTaskConventionRegression(scratch);
    }
    finally
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }
}

static void RunInlineTaskConventionRegression(string scratch)
{
    var root = Path.Combine(scratch, "inline-task");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var relativePath = "build/inline.targets";
    WriteAssets(assets, cache, "Inline.Package", new[] { relativePath }, createFiles: true);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    document["targets"]!["net8.0"]!["Inline.Package/1.0.0"]!["build"] = new JsonObject { [relativePath] = new JsonObject() };
    File.WriteAllText(assets, document.ToJsonString());
    var assetPath = Path.Combine(cache, "Inline.Package", "1.0.0", relativePath.Replace('/', Path.DirectorySeparatorChar));
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/Inline.Package/1.0.0/build/inline.targets\" /></Project>");

    var fixtures = new[]
    {
        ("positive", "<Project xmlns:x=\"urn:keelmatrix:packagesurface:extension\" x:marker=\"ignored\"><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task><Code Type=\"Fragment\" Language=\"cs\">Log.LogMessage(\"ok\");</Code></Task></UsingTask></Project>", true),
        ("custom-factory", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CustomFactory\"><Task><Code>Log.LogMessage(\"text\");</Code></Task></UsingTask></Project>", false),
        ("factory-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\" /></Project>", false),
        ("comment-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task><!-- Code --></Task></UsingTask></Project>", false),
        ("text-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task>Code text</Task></UsingTask></Project>", false)
    };

    foreach (var (name, xml, expected) in fixtures)
    {
        File.WriteAllText(assetPath, xml);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.BuildTargets);
        var observed = entry.ObservedPrimitives?.Contains("InlineTaskFactory") == true;
        Require(result.IsComplete && observed == expected, $"Inline task fixture '{name}' was misclassified.");
    }
}

static void RunStrictContentEligibilityMatrixTests()
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

static void RunIdentityAndReportContractTests()
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

static void RunParserMessageRegression()
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

static void RunParserTokenPrivacyRegression()
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

static void RunBaselineContractRegression()
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

static void RunBaselineTransactionRegression(string parent)
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

static void RunTerminalIncompleteReportRegression(string parent)
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

static void RunPackageIdentityAndPathRegression()
{
    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-identity-provenance-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var mismatchRoot = Path.Combine(scratch, "mismatch");
        var mismatchCache = Path.Combine(mismatchRoot, "cache");
        var mismatchObj = Path.Combine(mismatchRoot, "obj");
        Directory.CreateDirectory(mismatchObj);
        var mismatchAssets = Path.Combine(mismatchObj, "project.assets.json");
        WriteAssets(mismatchAssets, mismatchCache, "Claimed.Package", new List<string> { "build/tampered.targets" }, createFiles: true);
        var mismatch = JsonNode.Parse(File.ReadAllText(mismatchAssets))!.AsObject();
        mismatch["libraries"]!["Claimed.Package/1.0.0"]!["path"] = "Physical.Package/2.0.0";
        mismatch["targets"]!["net8.0"]!["Claimed.Package/1.0.0"]!["build"] = new JsonObject { ["build/tampered.targets"] = new JsonObject() };
        var physical = Path.Combine(mismatchCache, "Physical.Package", "2.0.0", "build");
        Directory.CreateDirectory(physical);
        File.WriteAllText(Path.Combine(physical, "tampered.targets"), "<Project />");
        File.WriteAllText(Path.Combine(mismatchObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/Physical.Package/2.0.0/build/tampered.targets\" /></Project>");
        File.WriteAllText(mismatchAssets, mismatch.ToJsonString());
        var mismatchResult = ResolvedGraphClassifier.Analyze(mismatchAssets, mismatchRoot, strictContent: false);
        Require(!mismatchResult.IsComplete && mismatchResult.Entries.Count == 0, "A package library path from a different physical package identity was attributed successfully.");

        var normalizedRoot = Path.Combine(scratch, "normalized");
        var normalizedCache = Path.Combine(normalizedRoot, "cache");
        var normalizedObj = Path.Combine(normalizedRoot, "obj");
        Directory.CreateDirectory(normalizedObj);
        var normalizedAssets = Path.Combine(normalizedObj, "project.assets.json");
        WriteAssets(normalizedAssets, normalizedCache, "Normalized.Package", Array.Empty<string>());
        var normalized = JsonNode.Parse(File.ReadAllText(normalizedAssets))!.AsObject();
        normalized["libraries"]!["Normalized.Package/1.0.0"]!["path"] = "Normalized.Package/01.0";
        File.WriteAllText(normalizedAssets, normalized.ToJsonString());
        var normalizedResult = ResolvedGraphClassifier.Analyze(normalizedAssets, normalizedRoot, strictContent: false);
        Require(normalizedResult.IsComplete && normalizedResult.ResolvedPackageCount == 1, "Equivalent NuGet version representations were not resolved to the physical package root.");
        Require(PackageIdentity.TryCreate("Normalized.Package", "1.0.0-Alpha.1", out var prereleaseA) &&
            PackageIdentity.TryCreate("normalized.package", "1.0.0-alpha.1", out var prereleaseB) &&
            prereleaseA.Equals(prereleaseB), "Equivalent normalized prerelease versions were not treated as the same package identity.");

        var caseRoot = Path.Combine(scratch, "case");
        var caseCache = Path.Combine(caseRoot, "cache");
        var caseObj = Path.Combine(caseRoot, "obj");
        Directory.CreateDirectory(caseObj);
        var caseAssets = Path.Combine(caseObj, "project.assets.json");
        WriteAssets(caseAssets, caseCache, "Case.Package", new List<string> { "build/Case.targets" }, createFiles: true);
        var caseDocument = JsonNode.Parse(File.ReadAllText(caseAssets))!.AsObject();
        caseDocument["targets"]!["net8.0"]!["Case.Package/1.0.0"]!["build"] = new JsonObject { ["build/Case.targets"] = new JsonObject() };
        File.WriteAllText(caseAssets, caseDocument.ToJsonString());
        File.WriteAllText(Path.Combine(caseCache, "Case.Package", "1.0.0", "build", "Case.targets"), "<Project />");
        File.WriteAllText(Path.Combine(caseObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/Case.Package/1.0.0/build/case.targets\" /></Project>");
        using (FileSystemComparisonScope.Push(caseRoot, FileSystemCaseSensitivity.Sensitive))
        {
            var caseResult = ResolvedGraphClassifier.Analyze(caseAssets, caseRoot, strictContent: false, fileSystemCaseSensitivity: FileSystemCaseSensitivity.Sensitive);
            Require(!caseResult.IsComplete && caseResult.Entries.Count == 0, "A case-sensitive filesystem accepted a wrong-case generated import.");
            var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
            var projectCase = identity with { Project = "src/Consumer.csproj" };
            var pathCase = identity with { PackageRelativePath = "build/case.targets" };
            Require(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count > 0 &&
                DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count > 0,
                "Case-sensitive filesystem identity comparison collapsed distinct project or asset paths.");
        }

        using (FileSystemComparisonScope.Push(caseRoot, FileSystemCaseSensitivity.Insensitive))
        {
            var caseResult = ResolvedGraphClassifier.Analyze(caseAssets, caseRoot, strictContent: false, fileSystemCaseSensitivity: FileSystemCaseSensitivity.Insensitive);
            Require(caseResult.IsComplete && caseResult.Entries.Any(entry => entry.Active), "A case-insensitive filesystem rejected a case-only generated import: " + string.Join(" | ", caseResult.IncompleteReasons));
            var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
            var projectCase = identity with { Project = "src/Consumer.csproj" };
            var pathCase = identity with { PackageRelativePath = "build/case.targets" };
            Require(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count == 0 &&
                DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count == 0,
                "Case-insensitive filesystem identity comparison rejected an equivalent project or asset path.");
        }
    }
    finally
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

static void RunBaselineJsonBoundaryRegression()
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

static void RunLanguageConventionRegression(string scratch)
{
    var combinedRoot = Path.Combine(scratch, "analyzer-combined");
    var combinedAssets = Path.Combine(combinedRoot, "obj", "project.assets.json");
    Directory.CreateDirectory(Path.GetDirectoryName(combinedAssets)!);
    var baseAnalyzer = "analyzers/dotnet/cs/base.dll";
    var legacyAnalyzer = "analyzers/dotnet/roslyn3.8/cs/legacy.dll";
    var currentAnalyzer = "analyzers/dotnet/roslyn4.0/cs/current.dll";
    var incompatibleAnalyzer = "analyzers/dotnet/roslyn99.0/cs/incompatible.dll";
    var satelliteAnalyzer = "analyzers/dotnet/cs/base.resources.dll";
    var neutralAnalyzer = "analyzers/dotnet/neutral.dll";
    var neutralLegacyAnalyzer = "analyzers/dotnet/roslyn3.8/neutral-legacy.dll";
    var neutralCurrentAnalyzer = "analyzers/dotnet/roslyn4.0/neutral-current.dll";
    WriteAnalyzerAssets(combinedAssets, Path.Combine(combinedRoot, "cache"), true, baseAnalyzer, "Test.csproj", includeAnalyzers: true,
        additionalAnalyzerRelativePaths: new[] { legacyAnalyzer, currentAnalyzer, incompatibleAnalyzer, satelliteAnalyzer, neutralAnalyzer, neutralLegacyAnalyzer, neutralCurrentAnalyzer });
    var combinedResult = ResolvedGraphClassifier.Analyze(combinedAssets, combinedRoot, strictContent: false, compilerApiVersion: "4.0");
    var combinedEntries = combinedResult.Entries
        .Where(entry => entry.Capability == CapabilityKind.CompilerExtension)
        .ToDictionary(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase);
    Require(combinedResult.IsComplete && combinedEntries[baseAnalyzer].Active && combinedEntries[currentAnalyzer].Active && !combinedEntries[incompatibleAnalyzer].Active &&
        !combinedEntries[legacyAnalyzer].Active && !combinedEntries[satelliteAnalyzer].Active && combinedEntries[neutralAnalyzer].Active &&
        !combinedEntries[neutralLegacyAnalyzer].Active && combinedEntries[neutralCurrentAnalyzer].Active,
        $"SDK-selected base and highest applicable analyzer versions were not classified independently. complete={combinedResult.IsComplete}; entries={combinedResult.Entries.Count}; reasons={string.Join(" | ", combinedResult.IncompleteReasons)}");

    var missingCompilerContext = ResolvedGraphClassifier.Analyze(combinedAssets, combinedRoot, strictContent: false);
    Require(!missingCompilerContext.IsComplete && missingCompilerContext.IncompleteReasons.Any(reason => reason.Contains("explicit consuming compiler API version", StringComparison.Ordinal)),
        "Versioned analyzer assets did not fail closed when consuming compiler context was unavailable.");
    Require(CommandLine.Run(new[] { "scan", combinedAssets, "--compiler-api-version", "4.0", "--no-telemetry" }) == 0,
        "The public CLI did not pass explicit compiler applicability context through to analysis.");

    var analyzerCases = new[]
    {
        (Language: "cs", Project: "Test.csproj", Path: "analyzers/dotnet/cs/valid.dll", Active: true),
        (Language: "vb", Project: "Test.vbproj", Path: "analyzers/dotnet/vb/valid.dll", Active: true),
        (Language: "fs", Project: "Test.fsproj", Path: "analyzers/dotnet/fs/valid.dll", Active: false),
        (Language: "cross", Project: "Test.csproj", Path: "analyzers/dotnet/vb/valid.dll", Active: false),
        (Language: "neutral", Project: "Test.unknown", Path: "analyzers/dotnet/neutral.dll", Active: true),
        (Language: "exe", Project: "Test.csproj", Path: "analyzers/dotnet/neutral.exe", Active: false),
        (Language: "optional", Project: "Test.csproj", Path: "analyzers/dotnet/roslyn4.0/cs/valid.dll", Active: true),
        (Language: "casing", Project: "Test.csproj", Path: "ANALYZERS/DOTNET/CS/VALID.DLL", Active: true)
    };

    foreach (var testCase in analyzerCases)
    {
        var root = Path.Combine(scratch, "analyzer-" + testCase.Language);
        var assets = Path.Combine(root, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        WriteAnalyzerAssets(assets, Path.Combine(root, "cache"), true, testCase.Path, testCase.Project, includeAnalyzers: true);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false,
            compilerApiVersion: testCase.Path.Contains("roslyn", StringComparison.OrdinalIgnoreCase) ? "4.0" : null);
        var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.CompilerExtension);
        Require(result.IsComplete && entry.Active == testCase.Active, $"Analyzer convention case '{testCase.Language}' was misclassified. Complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
    }

    var contentCases = new[]
    {
        (Language: "cs", Project: "Test.csproj", Path: "contentFiles/cs/any/active.cs", CodeLanguage: "cs", BuildAction: "Compile", Active: true),
        (Language: "preprocessed", Project: "Test.csproj", Path: "contentFiles/cs/any/active.cs.pp", CodeLanguage: "cs", BuildAction: "Compile", Active: true),
        (Language: "vb", Project: "Test.vbproj", Path: "contentFiles/vb/any/active.vb", CodeLanguage: "vb", BuildAction: "Compile", Active: true),
        (Language: "fs", Project: "Test.fsproj", Path: "contentFiles/fs/any/active.fs", CodeLanguage: "fs", BuildAction: "Compile", Active: true),
        (Language: "any", Project: "Test.vbproj", Path: "contentFiles/any/any/active.vb", CodeLanguage: "any", BuildAction: "Compile", Active: true),
        (Language: "none", Project: "Test.csproj", Path: "contentFiles/cs/any/none.cs", CodeLanguage: "cs", BuildAction: "None", Active: false)
    };

    foreach (var testCase in contentCases)
    {
        var root = Path.Combine(scratch, "content-" + testCase.Language);
        var assets = Path.Combine(root, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        WriteContentAssets(assets, Path.Combine(root, "cache"), testCase.Project, testCase.Path, testCase.CodeLanguage, testCase.BuildAction);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.CompileSourceInjection);
        Require(result.IsComplete && entry.Active == testCase.Active, $"contentFiles convention case '{testCase.Language}' was misclassified.");
    }

    foreach (var malformed in new[] { "buildAction", "codeLanguage" })
    {
        var root = Path.Combine(scratch, "content-malformed-" + malformed);
        var assets = Path.Combine(root, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
        WriteContentAssets(assets, Path.Combine(root, "cache"), "Test.csproj", "contentFiles/cs/any/bad.cs", "cs", "Compile", malformed);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete, $"Malformed contentFiles {malformed} metadata was accepted.");
    }
}

static void RunApplicabilityHardeningRegressions(string scratch)
{
    var casingRoot = Path.Combine(scratch, "casing");
    var casingCache = Path.Combine(casingRoot, "cache");
    Directory.CreateDirectory(casingRoot);
    var casingAssets = Path.Combine(casingRoot, "project.assets.json");
    WriteAssets(casingAssets, casingCache, "Casing.Package", new List<string> { "tools/π-script.ps1" }, createFiles: true);
    var casingDocument = JsonNode.Parse(File.ReadAllText(casingAssets))!.AsObject();
    RenameJsonProperty(casingDocument["targets"]!["net8.0"]!.AsObject(), "Casing.Package/1.0.0", "cAsInG.pAcKaGe/1.0.0");
    RenameJsonProperty(casingDocument["libraries"]!.AsObject(), "Casing.Package/1.0.0", "CASING.PACKAGE/1.0.0");
    File.WriteAllText(casingAssets, casingDocument.ToJsonString());
    var casingResult = ResolvedGraphClassifier.Analyze(casingAssets, casingRoot, strictContent: false);
    Require(casingResult.IsComplete && casingResult.Entries.Any(entry => entry.PackageId.Equals("cAsInG.pAcKaGe", StringComparison.OrdinalIgnoreCase)),
        "Package identity casing variation was not resolved case-insensitively.");

    var unicodeRoot = Path.Combine(scratch, "unicode");
    var unicodeCache = Path.Combine(unicodeRoot, "cache");
    Directory.CreateDirectory(unicodeRoot);
    var unicodeAssets = Path.Combine(unicodeRoot, "project.assets.json");
    WriteAssets(unicodeAssets, unicodeCache, "Unicode.Package", new List<string> { "tools/資料.ps1" }, createFiles: true);
    var unicodeResult = ResolvedGraphClassifier.Analyze(unicodeAssets, unicodeRoot, strictContent: false);
    Require(unicodeResult.IsComplete && unicodeResult.Entries.Any(entry => entry.PackageRelativePath == "tools/資料.ps1"),
        "Unicode package-relative filename was not classified.");

    var duplicateRoot = Path.Combine(scratch, "duplicate");
    var duplicateCache = Path.Combine(duplicateRoot, "cache");
    Directory.CreateDirectory(duplicateRoot);
    var duplicateAssets = Path.Combine(duplicateRoot, "project.assets.json");
    WriteAssets(duplicateAssets, duplicateCache, "Duplicate.Package", new List<string> { "tools/duplicate.ps1", "tools/duplicate.ps1" }, createFiles: true);
    var duplicateResult = ResolvedGraphClassifier.Analyze(duplicateAssets, duplicateRoot, strictContent: false);
    Require(!duplicateResult.IsComplete && duplicateResult.IncompleteReasons.Any(reason => reason.Contains("duplicate asset path", StringComparison.OrdinalIgnoreCase)),
        "Duplicate package assets were silently accepted.");

    var unknownRoot = Path.Combine(scratch, "unknown-language");
    var unknownCache = Path.Combine(unknownRoot, "cache");
    var unknownObj = Path.Combine(unknownRoot, "obj");
    Directory.CreateDirectory(unknownObj);
    var unknownAssets = Path.Combine(unknownObj, "project.assets.json");
    WriteAnalyzerAssets(unknownAssets, unknownCache, true, "analyzers/dotnet/cs/unknown.dll");
    var unknownDocument = JsonNode.Parse(File.ReadAllText(unknownAssets))!.AsObject();
    unknownDocument["project"]!.AsObject().Remove("restore");
    File.WriteAllText(unknownAssets, unknownDocument.ToJsonString());
    var unknownResult = ResolvedGraphClassifier.Analyze(unknownAssets, unknownRoot, strictContent: false);
    Require(!unknownResult.IsComplete, "Language-specific compiler applicability did not fail closed when language evidence was unavailable.");
    var languageIndependentRoot = Path.Combine(scratch, "language-independent");
    var languageIndependentCache = Path.Combine(languageIndependentRoot, "cache");
    Directory.CreateDirectory(languageIndependentRoot);
    var languageIndependentAssets = Path.Combine(languageIndependentRoot, "project.assets.json");
    WriteAssets(languageIndependentAssets, languageIndependentCache, "Inventory.Package", new List<string> { "tools/inventory.ps1" }, createFiles: true);
    var languageIndependentResult = ResolvedGraphClassifier.Analyze(languageIndependentAssets, languageIndependentRoot, strictContent: false);
    Require(languageIndependentResult.IsComplete, "Unknown project language unnecessarily failed a language-independent graph.");

    RunLanguageConventionRegression(scratch);

    RunProjectAggregationRegression(scratch);
    RunReparsePointRegression(scratch);
    RunReparsePathVariantRegression(scratch);
}

static void RunNestedImportRegression(string scratch)
{
    var root = Path.Combine(scratch, "nested-imports");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var packageKey = "Nested.Package/1.0.0";
    var packageRoot = Path.Combine(cache, "Nested.Package", "1.0.0");
    var packageTargets = "build/Nested.Package.targets";
    var helperTargets = "helpers/Helper.targets";
    var grandchildProps = "helpers/Grandchild.props";
    WriteAssets(assets, cache, "Nested.Package", new[] { packageTargets, helperTargets, grandchildProps }, createFiles: true);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    document["targets"]!["net8.0"]![packageKey]!["build"] = new JsonObject { [packageTargets] = new JsonObject() };
    File.WriteAllText(assets, document.ToJsonString());
    File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../helpers/Helper.targets\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"Grandchild.props\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<Project><PropertyGroup><NestedValue>1</NestedValue></PropertyGroup></Project>");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Package/1.0.0/{packageTargets}\" /></Project>");

    var baseline = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    var helper = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase));
    var grandchild = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(grandchildProps, StringComparison.OrdinalIgnoreCase));
    Require(baseline.IsComplete && helper.Active && helper.Sha256 is not null && grandchild.Active && grandchild.Sha256 is not null,
        $"Nested arbitrary-path import graph was not active and strictly fingerprinted. helper={helper.Active}/{helper.Sha256}; grandchild={grandchild.Active}/{grandchild.Sha256}; complete={baseline.IsComplete}; reasons={string.Join(" | ", baseline.IncompleteReasons)}");

    File.AppendAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<!-- changed -->");
    var changed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(DiffEngine.Compare(baseline.Entries.Select(BaselineEntry.From).ToArray(), changed.Entries, strictContent: true).Any(diagnostic => diagnostic.Id == "PS005"),
        "A nested helper-only content change was not detected.");

    File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"Missing.props\" /></Project>");
    var missing = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(!missing.IsComplete && missing.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)),
        "A missing arbitrary-path nested import was not fail-closed.");

    File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../build/Nested.Package.targets\" /></Project>");
    var cycle = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    Require(cycle.IsComplete && cycle.Entries.Any(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase) && entry.Active),
        "A bounded static import cycle was not handled deterministically.");

    File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"$(SensitiveDynamicImportMarker)\" /></Project>");
    var dynamic = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    Require(!dynamic.IsComplete && dynamic.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)) &&
        dynamic.IncompleteReasons.All(reason => !reason.Contains("SensitiveDynamicImportMarker", StringComparison.OrdinalIgnoreCase)),
        "An unsupported dynamic nested import was not fail-closed without disclosing its expression.");

    RunNestedPhaseMatrixRegression(scratch);
    RunPackageXmlSchemaRegression(scratch);
}

static void RunPackageXmlSchemaRegression(string scratch)
{
    var root = Path.Combine(scratch, "package-xml-schema");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var packageId = "Package.Xml.Schema";
    var packagePath = "build/Package.Xml.Schema.targets";
    WriteAssets(assets, cache, packageId, new[] { packagePath }, createFiles: true);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [packagePath] = new JsonObject() };
    File.WriteAllText(assets, document.ToJsonString());

    var physicalPath = Path.Combine(cache, packageId, "1.0.0", packagePath.Replace('/', Path.DirectorySeparatorChar));
    var canonical = """
<Project ToolsVersion="Current">
  <PropertyGroup Label="standard">
    <SchemaValue>1</SchemaValue>
  </PropertyGroup>
  <ItemGroup Label="standard">
    <None Include="content.txt" />
    <None Update="content.txt" />
    <None Remove="obsolete.txt" />
  </ItemGroup>
  <Target Name="Standard" BeforeTargets="Build" AfterTargets="Build" DependsOnTargets="CoreCompile" Inputs="$(ProjectFile)" Outputs="$(TargetPath)" Returns="$(TargetPath)" KeepDuplicateOutputs="true" Label="standard" />
  <UsingTask TaskName="Inline" TaskFactory="CodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" Condition="'$(TargetFramework)' == 'never'">
    <Task>
      <Code Type="Fragment" Language="cs">Log.LogMessage("never executed");</Code>
    </Task>
  </UsingTask>
  <Exec Command="echo never-executed" Condition="'$(TargetFramework)' == 'never'" />
  <ImportGroup Label="standard">
    <Import Project="helpers/Helper.targets" Condition="'$(TargetFramework)' == 'never'" Label="standard" />
  </ImportGroup>
</Project>
""";
    File.WriteAllText(physicalPath, canonical);
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{packagePath}\" /></Project>");

    var baseline = Path.Combine(root, "baseline.json");
    var valid = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    Require(valid.IsComplete, $"Standard unconsumed MSBuild constructs were rejected: {string.Join(" | ", valid.IncompleteReasons)}");
    Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
        "The standard MSBuild XML baseline could not be created.");

    foreach (var (name, replacement) in new[]
    {
        ("element-import-case", (Old: "<Import Project=", New: "<import Project=")),
        ("element-import-near", (Old: "<Import Project=", New: "<Impor Project=")),
        ("element-using-task-case", (Old: "<UsingTask TaskName=", New: "<usingtask TaskName=")),
        ("element-using-task-near", (Old: "<UsingTask TaskName=", New: "<UsingTas TaskName=")),
        ("element-exec-case", (Old: "<Exec Command=", New: "<exec Command=")),
        ("element-exec-near", (Old: "<Exec Command=", New: "<Exce Command=")),
        ("element-code-case", (Old: "<Code Type=", New: "<code Type=")),
        ("element-code-near", (Old: "<Code Type=", New: "<Cod Type="))
    })
    {
        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, name, replacement.Old, replacement.New);
    }

    foreach (var (name, canonicalAttribute, alias, near) in new[]
    {
        ("Project", "Project", "project", "Projec"),
        ("Condition", "Condition", "condition", "Conditon"),
        ("TaskName", "TaskName", "taskname", "TaskNme"),
        ("TaskFactory", "TaskFactory", "taskfactory", "TaskFactor"),
        ("AssemblyFile", "AssemblyFile", "assemblyfile", "AssemblyFil"),
        ("Command", "Command", "command", "Comand"),
        ("Type", "Type", "type", "Typ"),
        ("Language", "Language", "language", "Langage"),
        ("BeforeTargets", "BeforeTargets", "beforetargets", "BeforeTarget"),
        ("AfterTargets", "AfterTargets", "aftertargets", "AfterTarget"),
        ("DependsOnTargets", "DependsOnTargets", "dependsonTargets", "DependsOnTarget"),
        ("Include", "Include", "include", "Includ"),
        ("Name", "Name", "name", "Nam"),
        ("ToolsVersion", "ToolsVersion", "toolsversion", "ToolsVersio")
    })
    {
        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-case", " " + canonicalAttribute + "=", " " + alias + "=");
        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-near", " " + canonicalAttribute + "=", " " + near + "=");
        AssertPackageXmlDuplicateAttributeFailure(assets, root, baseline, physicalPath, canonical, name, canonicalAttribute, alias);
    }
}

static void AssertPackageXmlVariantFailure(
    string assets,
    string projectRoot,
    string baseline,
    string physicalPath,
    string canonical,
    string label,
    string oldText,
    string newText)
{
    var variant = canonical.Replace(oldText, newText, StringComparison.Ordinal);
    Require(!variant.Equals(canonical, StringComparison.Ordinal), $"The XML regression variant '{label}' was not applied.");
    File.WriteAllText(physicalPath, variant);
    try
    {
        AssertRestoreIdentityFailure(assets, projectRoot, baseline, label);
    }
    finally
    {
        File.WriteAllText(physicalPath, canonical);
    }
}

static void AssertPackageXmlDuplicateAttributeFailure(
    string assets,
    string projectRoot,
    string baseline,
    string physicalPath,
    string canonical,
    string name,
    string canonicalAttribute,
    string alias)
{
    var marker = " " + canonicalAttribute + "=\"";
    var markerIndex = canonical.IndexOf(marker, StringComparison.Ordinal);
    Require(markerIndex >= 0, $"The XML duplicate variant '{name}' could not find its canonical attribute.");
    var valueEnd = canonical.IndexOf('"', markerIndex + marker.Length);
    Require(valueEnd >= 0, $"The XML duplicate variant '{name}' has no complete canonical attribute value.");
    var variant = canonical.Insert(valueEnd + 1, " " + alias + "=\"duplicate\"");
    File.WriteAllText(physicalPath, variant);
    try
    {
        AssertRestoreIdentityFailure(assets, projectRoot, baseline, "attribute-" + name + "-duplicate");
    }
    finally
    {
        File.WriteAllText(physicalPath, canonical);
    }
}

static void RunNestedPhaseMatrixRegression(string scratch)
{
    var root = Path.Combine(scratch, "nested-phase-matrix");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var packageId = "Nested.Phase.Matrix";
    var files = new[]
    {
        "build/Root.props",
        "build/Helper.targets",
        "buildTransitive/Transitive.targets",
        "buildTransitive/TransitiveHelper.props",
        "buildMultiTargeting/Outer.props",
        "buildMultiTargeting/OuterHelper.targets"
    };
    WriteAssets(assets, cache, packageId, files, createFiles: true);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
    package["build"] = new JsonObject { ["build/Root.props"] = new JsonObject() };
    package["buildTransitive"] = new JsonObject { ["buildTransitive/Transitive.targets"] = new JsonObject() };
    package["buildMultiTargeting"] = new JsonObject { ["buildMultiTargeting/Outer.props"] = new JsonObject() };
    File.WriteAllText(assets, document.ToJsonString());

    var packageRoot = Path.Combine(cache, packageId, "1.0.0");
    File.WriteAllText(Path.Combine(packageRoot, "build", "Root.props"), "<Project><Import Project=\"Helper.targets\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project><Import Project=\"Root.props\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "Transitive.targets"), "<Project><Import Project=\"TransitiveHelper.props\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "TransitiveHelper.props"), "<Project />");
    File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props"), "<Project><Import Project=\"OuterHelper.targets\" /></Project>");
    File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "OuterHelper.targets"), "<Project />");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"),
        "<Project>" +
        "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/build/Root.props\" />" +
        "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildMultiTargeting/Outer.props\" />" +
        "</Project>");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildTransitive/Transitive.targets\" /></Project>");

    var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(result.IsComplete, $"Nested phase matrix did not classify completely: {string.Join(" | ", result.IncompleteReasons)}");
    foreach (var path in files.Skip(1))
    {
        var entries = result.Entries.Where(candidate => candidate.PackageRelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
        Require(entries.Length > 0 && entries.All(entry => entry.Active && entry.Sha256 is not null), $"Nested phase matrix descendant '{path}' was not active and fingerprinted.");
    }

    var rootProps = Path.Combine(packageRoot, "build", "Root.props");
    File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
    File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' != '.NETCoreApp,Version=v8.0'\" /></Project>");
    var literalAlias = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(literalAlias.IsComplete && literalAlias.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
        "A literal framework alias was normalized instead of compared with MSBuild string semantics: entries=" + string.Join(" | ", literalAlias.Entries.Select(entry => $"{entry.PackageRelativePath}:{entry.Active}")) + " reasons=" + string.Join(" | ", literalAlias.IncompleteReasons));

    File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(TargetFramework)'\" /></Project>");
    var propertyReference = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(propertyReference.IsComplete && propertyReference.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
        "A supported property reference on the right side of a condition was not resolved.");

    File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"&quot;$( TargetFramework )&quot; == &quot;net8.0&quot; AND ('$(TargetFramework)' != 'net9.0' OR '$(TargetFramework)' == 'net8.0')\" /></Project>");
    var quotedBoolean = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(quotedBoolean.IsComplete && quotedBoolean.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
        "Quoted or nested AND/OR TargetFramework conditions were not evaluated with the supported string grammar.");

    var outerProps = Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props");
    File.WriteAllText(outerProps, "<Project><Import Project=\"OuterHelper.targets\" Condition=\"'$(TargetFramework)' == ''\" /></Project>");
    var outerBuild = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(outerBuild.IsComplete && outerBuild.Entries.Any(entry => entry.PackageRelativePath.Equals("buildMultiTargeting/OuterHelper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
        "Outer-build TargetFramework condition context was not evaluated independently from target-build context.");

    File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(UnsupportedConditionProperty)'\" /></Project>");
    var unsupportedExpansion = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
    Require(!unsupportedExpansion.IsComplete && unsupportedExpansion.IncompleteReasons.Any(reason => reason.Contains("unsupported property expansion", StringComparison.OrdinalIgnoreCase)),
        "An unsupported condition property expansion was treated as a known false branch.");
}

static void RunImportEdgeBudgetRegression(string scratch)
{
    var root = Path.Combine(scratch, "import-edge-budget");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var files = new[] { "build/Edge.props", "build/Edge.targets" };

    var withinPath = Path.Combine(obj, "within.assets.json");
    WriteAssets(withinPath, cache, "Edge.Package", files, createFiles: true);
    AddBuildGroups(withinPath, "Edge.Package", files);
    WritePackageBuildFiles(cache, files);
    WriteRepeatedGeneratedImports(obj, 4_096, 4_096, files);
    var within = ResolvedGraphClassifier.Analyze(withinPath, root, strictContent: false);
    Require(within.IsComplete && within.Entries.Count == 2, $"The 8,192 cumulative generated-import edge boundary failed: {string.Join(" | ", within.IncompleteReasons)}");

    var overRoot = Path.Combine(scratch, "import-edge-budget-over");
    var overCache = Path.Combine(overRoot, "cache");
    var overObj = Path.Combine(overRoot, "obj");
    Directory.CreateDirectory(overObj);
    var overPath = Path.Combine(overObj, "over.assets.json");
    WriteAssets(overPath, overCache, "Edge.Package", files, createFiles: true);
    AddBuildGroups(overPath, "Edge.Package", files);
    WritePackageBuildFiles(overCache, files);
    WriteRepeatedGeneratedImports(overObj, 4_096, 4_097, files);
    var output = new StringWriter(CultureInfo.InvariantCulture);
    var priorOutput = Console.Out;
    try
    {
        Console.SetOut(output);
        var exitCode = CommandLine.Run(new[] { "scan", overPath, "--format", "json", "--no-telemetry" });
        Require(exitCode == 2, $"An 8,193 cumulative generated-import edge graph returned exit {exitCode}.");
    }
    finally
    {
        Console.SetOut(priorOutput);
    }

    Require(!output.ToString().Contains("Edge.props", StringComparison.Ordinal) &&
        !output.ToString().Contains("Edge.targets", StringComparison.Ordinal),
        "The over-budget import graph emitted partial capability success output.");
}

static void RunGeneratedImportReachabilityRegression(string scratch)
{
    var directRoot = Path.Combine(scratch, "orphan-generated-import");
    var directCache = Path.Combine(directRoot, "cache");
    var directObj = Path.Combine(directRoot, "obj");
    Directory.CreateDirectory(directObj);
    var directAssets = Path.Combine(directObj, "project.assets.json");
    WriteAssets(directAssets, directCache, "Reachable.Package", Array.Empty<string>());
    var directBaseline = Path.Combine(directRoot, "baseline.json");
    Require(CaptureCommand("baseline", directAssets, "--output", directBaseline, "--no-telemetry").ExitCode == 0,
        "The coherent direct-import fixture could not create its baseline.");
    var directDocument = JsonNode.Parse(File.ReadAllText(directAssets))!.AsObject();
    AddUnreachableImportPackage(directDocument, directCache, "Unused.Package", "build/unused.targets");
    File.WriteAllText(directAssets, directDocument.ToJsonString());
    File.WriteAllText(Path.Combine(directObj, "Test.csproj.nuget.g.props"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/unused.package/1.0.0/build/unused.targets\" /></Project>");
    AssertGeneratedImportFailure("direct orphan import", directAssets, directBaseline);

    var coherentRoot = Path.Combine(scratch, "nested-orphan-generated-import");
    var coherentCache = Path.Combine(coherentRoot, "cache");
    var coherentObj = Path.Combine(coherentRoot, "obj");
    Directory.CreateDirectory(coherentObj);
    var coherentAssets = Path.Combine(coherentObj, "project.assets.json");
    var coherentFiles = new[] { "build/outer.targets" };
    WriteAssets(coherentAssets, coherentCache, "Reachable.Package", coherentFiles, createFiles: true);
    File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"), "<Project />");
    var coherentBaseline = Path.Combine(coherentRoot, "baseline.json");
    Require(CaptureCommand("baseline", coherentAssets, "--output", coherentBaseline, "--no-telemetry").ExitCode == 0,
        "The coherent nested-import fixture could not create its baseline.");
    var coherentDocument = JsonNode.Parse(File.ReadAllText(coherentAssets))!.AsObject();
    coherentDocument["targets"]!["net8.0"]!["Reachable.Package/1.0.0"]!["build"] = new JsonObject { ["build/outer.targets"] = new JsonObject() };
    File.WriteAllText(coherentAssets, coherentDocument.ToJsonString());
    File.WriteAllText(Path.Combine(coherentObj, "Test.csproj.nuget.g.props"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/Reachable.Package/1.0.0/build/outer.targets\" /></Project>");
    File.WriteAllText(Path.Combine(coherentObj, "Test.csproj.nuget.g.targets"), "<Project />");
    File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"), "<Project />");

    coherentDocument = JsonNode.Parse(File.ReadAllText(coherentAssets))!.AsObject();
    AddUnreachableImportPackage(coherentDocument, coherentCache, "Unused.Package", "build/unused.targets");
    File.WriteAllText(coherentAssets, coherentDocument.ToJsonString());
    File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/Unused.Package/1.0.0/build/unused.targets\" /></Project>");
    AssertGeneratedImportFailure("nested orphan import", coherentAssets, coherentBaseline);

    var conditionalRoot = Path.Combine(scratch, "conditional-orphan-generated-import");
    var conditionalCache = Path.Combine(conditionalRoot, "cache");
    var conditionalObj = Path.Combine(conditionalRoot, "obj");
    Directory.CreateDirectory(conditionalObj);
    var conditionalAssets = Path.Combine(conditionalObj, "project.assets.json");
    WriteAssets(conditionalAssets, conditionalCache, "Reachable.Package", Array.Empty<string>());
    var conditionalBaseline = Path.Combine(conditionalRoot, "baseline.json");
    Require(CaptureCommand("baseline", conditionalAssets, "--output", conditionalBaseline, "--no-telemetry").ExitCode == 0,
        "The coherent conditional-import fixture could not create its baseline.");
    var conditionalDocument = JsonNode.Parse(File.ReadAllText(conditionalAssets))!.AsObject();
    conditionalDocument["project"]!["frameworks"]!["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject() };
    AddUnreachableImportPackage(conditionalDocument, conditionalCache, "Only.Net9", "build/net9.targets");
    conditionalDocument["targets"]!["net9.0"] = new JsonObject
    {
        ["Only.Net9/1.0.0"] = new JsonObject { ["build"] = new JsonObject { ["build/net9.targets"] = new JsonObject() } }
    };
    File.WriteAllText(conditionalAssets, conditionalDocument.ToJsonString());
    File.WriteAllText(Path.Combine(conditionalCache, "Only.Net9", "1.0.0", "build", "net9.targets"), "<Project />");
    var conditionalImport = Path.Combine(conditionalObj, "Test.csproj.nuget.g.props");
    File.WriteAllText(Path.Combine(conditionalObj, "Test.csproj.nuget.g.targets"), "<Project />");
    File.WriteAllText(conditionalImport,
        "<Project><Import Project=\"$(NuGetPackageRoot)/Only.Net9/1.0.0/build/net9.targets\" Condition=\"'$(TargetFramework)' == 'net8.0'\" /></Project>");
    AssertGeneratedImportFailure("conditional target graph", conditionalAssets, conditionalBaseline);
}

static void AddUnreachableImportPackage(JsonObject document, string cache, string packageId, string relativePath)
{
    var packageKey = packageId + "/1.0.0";
    document["libraries"]!.AsObject()[packageKey] = new JsonObject
    {
        ["type"] = "package",
        ["path"] = packageKey,
        ["files"] = new JsonArray(relativePath)
    };
    var packagePath = Path.Combine(cache, packageId, "1.0.0", relativePath.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
    File.WriteAllText(packagePath, "<Project />");
}

static void AssertGeneratedImportFailure(string label, string assets, string baseline)
{
    var baselineBefore = File.ReadAllBytes(baseline);
    foreach (var format in new[] { "text", "json", "sarif" })
    {
        var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
        Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                !scan.Output.Contains("BuildTargets", StringComparison.Ordinal) &&
                !scan.Output.Contains("Unused.Package", StringComparison.Ordinal),
            $"{label} emitted a clean or partial {format} scan result.");

        var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
        Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                !failedBaseline.Output.Contains("BuildTargets", StringComparison.Ordinal),
            $"{label} emitted a clean or partial {format} baseline result.");
        Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)), $"{label} mutated the baseline.");

        var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
        Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                !check.Output.Contains("PS003", StringComparison.Ordinal),
            $"{label} emitted a clean or policy-diff {format} check result.");
    }
}

static void RunInvocationStructuralBudgetRegression(string scratch)
{
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
    Require(baselineResult.ExitCode == 0, "The within-budget project could not create its recovery baseline.");
    var baselineBefore = File.ReadAllBytes(baselinePath);

    foreach (var format in new[] { "text", "json", "sarif" })
    {
        var scan = CaptureCommand("scan", root, "--format", format, "--no-telemetry");
        Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                !scan.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
            $"Cumulative target-graph overflow emitted partial {format} scan output.");

        var baseline = CaptureCommand("baseline", root, "--output", baselinePath, "--format", format, "--no-telemetry");
        Require(baseline.ExitCode == 2 && baseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                !baseline.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
            $"Cumulative target-graph overflow emitted partial {format} baseline output.");
        Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baselinePath)),
            $"Cumulative target-graph overflow mutated the baseline for {format} output.");

        var check = CaptureCommand("check", root, "--baseline", baselinePath, "--format", format, "--no-telemetry");
        Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                !check.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal) &&
                !check.Output.Contains("PS003", StringComparison.Ordinal),
            $"Cumulative target-graph overflow emitted a clean-looking {format} check result.");
    }

    var narrowed = CaptureCommand("scan", projectA, "--project", projectA, "--no-telemetry");
    Require(narrowed.ExitCode == 0 && narrowed.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
        "--project did not provide the documented recovery path after an aggregate structural-budget failure.");
    Require(CaptureCommand("baseline", projectA, "--project", projectA, "--output", Path.Combine(root, "recovery.json"), "--no-telemetry").ExitCode == 0,
        "--project baseline recovery failed after the aggregate structural-budget failure.");
    Require(CaptureCommand("check", projectA, "--project", projectA, "--baseline", baselinePath, "--no-telemetry").ExitCode == 0,
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
    Require(inventoryFailure.ExitCode == 2 && !inventoryFailure.Output.Contains("inventory-", StringComparison.Ordinal),
        "Selecting two projects did not fail closed without partial package-inventory output.");
    Require(CaptureCommand("scan", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--project", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--no-telemetry").ExitCode == 0,
        "A narrowed project did not remain within the package-inventory file budget.");
}

static void AddBuildGroups(string assets, string packageId, IReadOnlyList<string> files)
{
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
    package["build"] = new JsonObject
    {
        [files[0]] = new JsonObject(),
        [files[1]] = new JsonObject()
    };
    File.WriteAllText(assets, document.ToJsonString());
}

static void WriteRepeatedGeneratedImports(string obj, int propsCount, int targetsCount, IReadOnlyList<string> files)
{
    var props = new StringBuilder("<Project>");
    for (var index = 0; index < propsCount; index++)
    {
        props.Append("<Import Project=\"$(NuGetPackageRoot)/Edge.Package/1.0.0/").Append(files[0]).Append("\" />");
    }
    props.Append("</Project>");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"), props.ToString());

    var targets = new StringBuilder("<Project>");
    for (var index = 0; index < targetsCount; index++)
    {
        targets.Append("<Import Project=\"$(NuGetPackageRoot)/Edge.Package/1.0.0/").Append(files[1]).Append("\" />");
    }
    targets.Append("</Project>");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), targets.ToString());
}

static void WritePackageBuildFiles(string cache, IReadOnlyList<string> files)
{
    foreach (var file in files)
    {
        var path = Path.Combine(cache, "Edge.Package", "1.0.0", file.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(path, "<Project />");
    }
}

static void WriteTargetGraphAssets(string assets, string cache, string projectPath, int targetCount, bool includeToolAsset = false)
{
    const string packageKey = "TargetGraph.Package/1.0.0";
    const string toolPath = "tools/fixture.ps1";
    var targets = new JsonObject();
    for (var index = 0; index < targetCount; index++)
    {
        targets[$"net8.0/rid{index.ToString(CultureInfo.InvariantCulture)}"] = includeToolAsset
            ? new JsonObject { [packageKey] = new JsonObject() }
            : new JsonObject();
    }

    var libraries = includeToolAsset
        ? new JsonObject
        {
            [packageKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = packageKey,
                ["files"] = new JsonArray(toolPath)
            }
        }
        : new JsonObject();
    if (includeToolAsset)
    {
        var packageRoot = Path.Combine(cache, "TargetGraph.Package", "1.0.0", "tools");
        Directory.CreateDirectory(packageRoot);
        File.WriteAllText(Path.Combine(packageRoot, "fixture.ps1"), "fixture");
    }

    var frameworkDependencies = includeToolAsset
        ? new JsonObject { ["TargetGraph.Package"] = PackageDependency() }
        : new JsonObject();
    var root = new JsonObject
    {
        ["version"] = 3,
        ["targets"] = targets,
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject
        {
            ["restore"] = new JsonObject { ["projectPath"] = projectPath },
            ["frameworks"] = new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = frameworkDependencies } }
        }
    };
    Directory.CreateDirectory(cache);
    File.WriteAllText(assets, root.ToJsonString());
    var obj = Path.GetDirectoryName(assets)!;
    var projectFile = Path.GetFileName(projectPath);
    WriteGeneratedImportEvidence(assets, projectFile);
}

static void RunProjectAggregationRegression(string scratch)
{
    var root = Path.Combine(scratch, "project-aggregation");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    var packageId = "Aggregate.Package";
    var packageKey = packageId + "/1.0.0";
    var relativePath = "buildMultiTargeting/Aggregate.props";
    var packageRoot = Path.Combine(cache, packageId, "1.0.0");
    Directory.CreateDirectory(Path.Combine(packageRoot, "buildMultiTargeting"));
    File.WriteAllText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)), "<Project />");
    var targetPackage = new JsonObject
    {
        ["type"] = "package",
        ["buildMultiTargeting"] = new JsonObject { [relativePath] = new JsonObject() }
    };
    var targets = new JsonObject
    {
        ["net8.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() },
        ["net9.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() }
    };
    var libraries = new JsonObject
    {
        [packageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = packageKey,
            ["files"] = new JsonArray(relativePath)
        }
    };
    var projectFile = Path.Combine(root, "Aggregate.csproj");
    File.WriteAllText(projectFile, "<Project />");
    File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.props"), "<Project><Import Project='$(NuGetPackageRoot)/Aggregate.Package/1.0.0/buildMultiTargeting/Aggregate.props' /></Project>");
    File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.targets"), "<Project />");
    var document = new JsonObject
    {
        ["version"] = 3,
        ["targets"] = targets,
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject
        {
            ["restore"] = new JsonObject { ["projectPath"] = projectFile },
            ["frameworks"] = new JsonObject
            {
                ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } },
                ["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } }
            }
        }
    };
    var assets = Path.Combine(obj, "project.assets.json");
    File.WriteAllText(assets, document.ToJsonString());
    var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    var entry = result.Entries.SingleOrDefault(candidate => candidate.Capability == CapabilityKind.BuildMultiTargeting);
    Require(result.IsComplete && entry is not null && entry.Relationship == "direct" && entry.Active,
        "Project-level direct/transitive aggregation was not deterministic or direct-wins.");
}

static void RunAncestorCanonicalizationRegression(string scratch)
{
    var root = Path.Combine(scratch, "ancestor-canonicalization-seam", "link");
    var target = Path.Combine(scratch, "ancestor-canonicalization-seam", "target");
    var cache = Path.Combine(root, "cache");
    var obj = Path.Combine(root, "obj");
    Directory.CreateDirectory(obj);
    Directory.CreateDirectory(target);
    var assets = Path.Combine(obj, "project.assets.json");
    var ancestorFiles = new[] { "tools/inside.ps1" };
    WriteAssets(assets, cache, "Ancestor.Seam", ancestorFiles, createFiles: true);
    var canonicalizer = new AncestorLinkCanonicalizer(root, target);
    var canonicalizerSlot = typeof(ResolvedGraphClassifier).GetField("PathCanonicalizer", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("The canonicalization seam is unavailable.");
    var canonicalizerValue = canonicalizerSlot.GetValue(null)
        ?? throw new InvalidOperationException("The canonicalization seam was not initialized.");
    var valueProperty = canonicalizerValue.GetType().GetProperty("Value")
        ?? throw new InvalidOperationException("The canonicalization seam has no value slot.");
    var priorCanonicalizer = valueProperty.GetValue(canonicalizerValue);
    valueProperty.SetValue(canonicalizerValue, (Func<string, string>)canonicalizer.Canonicalize);
    ProbeResult result;
    try
    {
        result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    }
    finally
    {
        valueProperty.SetValue(canonicalizerValue, priorCanonicalizer);
    }
    Require(result.IsComplete && canonicalizer.AncestorPathObserved,
        "The canonicalization seam did not exercise a deterministic ancestor-link containment case: " + string.Join(" | ", result.IncompleteReasons));
}

static void RunReparsePointRegression(string scratch)
{
    var root = Path.Combine(scratch, "reparse");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(root);
    var assets = Path.Combine(root, "project.assets.json");
    WriteAssets(assets, cache, "Link.Package", new List<string> { "tools/escape.ps1" });
    var packageTools = Path.Combine(cache, "Link.Package", "1.0.0", "tools");
    Directory.CreateDirectory(packageTools);
    var outside = Path.Combine(scratch, "outside.ps1");
    File.WriteAllText(outside, "outside");
    try
    {
        File.CreateSymbolicLink(Path.Combine(packageTools, "escape.ps1"), outside);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),
            "A supported filesystem link escape was not rejected.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this platform."); }
    finally
    {
        if (File.Exists(outside)) File.Delete(outside);
    }

    var rootLink = Path.Combine(scratch, "root-link");
    var rootLinkCache = Path.Combine(rootLink, "cache");
    var rootLinkObj = Path.Combine(rootLink, "obj");
    Directory.CreateDirectory(rootLinkObj);
    var rootLinkAssets = Path.Combine(rootLinkObj, "project.assets.json");
    WriteAssets(rootLinkAssets, rootLinkCache, "RootLink.Package", new List<string> { "build/root.targets" });
    var rootLinkPackage = Path.Combine(rootLinkCache, "RootLink.Package", "1.0.0");
    var outsidePackage = Path.Combine(scratch, "outside-package");
    Directory.CreateDirectory(Path.Combine(outsidePackage, "build"));
    File.WriteAllText(Path.Combine(outsidePackage, "build", "root.targets"), "<Project />");
    Directory.Delete(rootLinkPackage, recursive: true);
    try
    {
        Directory.CreateSymbolicLink(rootLinkPackage, outsidePackage);
        File.WriteAllText(Path.Combine(rootLinkObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/RootLink.Package/1.0.0/build/root.targets\" /></Project>");
        var rootLinkResult = ResolvedGraphClassifier.Analyze(rootLinkAssets, rootLink, strictContent: true);
        Require(!rootLinkResult.IsComplete && rootLinkResult.Entries.All(entry => entry.Sha256 is null),
            "A rejected package-root link was opened or hashed through a fallback path.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this platform."); }

    var interiorRoot = Path.Combine(scratch, "interior-link");
    var interiorCache = Path.Combine(interiorRoot, "cache");
    var interiorObj = Path.Combine(interiorRoot, "obj");
    Directory.CreateDirectory(interiorObj);
    var interiorAssets = Path.Combine(interiorObj, "project.assets.json");
    var interiorFiles = new[] { "build/inside.targets" };
    WriteAssets(interiorAssets, interiorCache, "InteriorLink.Package", interiorFiles, createFiles: true);
    var interiorPackage = Path.Combine(interiorCache, "InteriorLink.Package", "1.0.0");
    var interiorBuild = Path.Combine(interiorPackage, "build");
    var outsideBuild = Path.Combine(scratch, "outside-build");
    Directory.CreateDirectory(outsideBuild);
    File.WriteAllText(Path.Combine(outsideBuild, "inside.targets"), "<Project />");
    Directory.Delete(interiorBuild, recursive: true);
    try
    {
        Directory.CreateSymbolicLink(interiorBuild, outsideBuild);
        var interiorResult = ResolvedGraphClassifier.Analyze(interiorAssets, interiorRoot, strictContent: false);
        Require(!interiorResult.IsComplete && interiorResult.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),
            "An interior directory reparse point was not rejected.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this platform."); }

    var ancestorTarget = Path.Combine(scratch, "ancestor-target");
    var ancestorLink = Path.Combine(scratch, "ancestor-link");
    var ancestorCreated = false;
    try
    {
        Directory.CreateDirectory(ancestorTarget);
        Directory.CreateSymbolicLink(ancestorLink, ancestorTarget);
        ancestorCreated = true;
    }
    catch (UnauthorizedAccessException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this filesystem; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }
    catch (IOException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this filesystem; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }
    catch (PlatformNotSupportedException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this platform; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }

    if (ancestorCreated)
    {
        Directory.CreateDirectory(Path.Combine(ancestorTarget, "obj"));
        Directory.CreateDirectory(Path.Combine(ancestorTarget, "cache"));
        var ancestorCache = Path.Combine(ancestorLink, "cache");
        var ancestorAssets = Path.Combine(ancestorLink, "obj", "project.assets.json");
        WriteAssets(ancestorAssets, ancestorCache, "AncestorLink.Package", new List<string> { "build/inside.targets" }, createFiles: true);
        File.WriteAllText(Path.Combine(ancestorCache, "AncestorLink.Package", "1.0.0", "build", "inside.targets"), "<Project />");
        var ancestorResult = ResolvedGraphClassifier.Analyze(ancestorAssets, ancestorLink, strictContent: false);
        Require(ancestorResult.IsComplete, "A reparse point outside the resolved package root was treated as an unsafe package path: " + string.Join(" | ", ancestorResult.IncompleteReasons));
        Console.WriteLine($"VERIFIED: ancestor reparse-point regression exercised a real directory link; link='{ancestorLink}'; target='{ancestorTarget}'");
    }
}

static void RunReparsePathVariantRegression(string scratch)
{
    var variants = new[]
    {
        (Name: "mixed-case-separators", PackageId: "Case.Matrix", File: "BUILD\\Inside.TARGETS"),
        (Name: "unicode-separators", PackageId: "Unicode.包", File: "build\\内部.targets")
    };
    var states = new[] { "leaf", "package-root", "interior-directory", "ancestor" };

    foreach (var variant in variants)
    {
        foreach (var state in states)
        {
            var stateRoot = Path.Combine(scratch, "reparse-matrix", variant.Name, state);
            var root = stateRoot;
            var targetRoot = Path.Combine(stateRoot, "target");
            if (state == "ancestor")
            {
                Directory.CreateDirectory(targetRoot);
                root = Path.Combine(stateRoot, "link");
                try
                {
                    Directory.CreateSymbolicLink(root, targetRoot);
                }
                catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); continue; }
            }

            var obj = Path.Combine(root, "obj");
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(obj);
            var assets = Path.Combine(obj, "project.assets.json");
            var files = new[] { variant.File };
            WriteAssets(assets, cache, variant.PackageId, files, createFiles: true);
            var packageRoot = Path.Combine(cache, variant.PackageId, "1.0.0");
            var relativeFile = variant.File.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var packageFile = Path.Combine(packageRoot, relativeFile);
            File.WriteAllText(packageFile, "<Project />");

            var outside = Path.Combine(stateRoot, "outside");
            try
            {
                switch (state)
                {
                    case "leaf":
                        File.Delete(packageFile);
                        File.WriteAllText(outside + ".targets", "<Project />");
                        File.CreateSymbolicLink(packageFile, outside + ".targets");
                        break;
                    case "package-root":
                        Directory.Delete(packageRoot, recursive: true);
                        Directory.CreateDirectory(Path.Combine(outside, "build"));
                        File.WriteAllText(Path.Combine(outside, "build", "Inside.TARGETS"), "<Project />");
                        Directory.CreateSymbolicLink(packageRoot, outside);
                        break;
                    case "interior-directory":
                        var buildDirectory = Path.Combine(packageRoot, Path.GetDirectoryName(relativeFile)!);
                        Directory.Delete(buildDirectory, recursive: true);
                        Directory.CreateDirectory(Path.Combine(outside, "nested"));
                        File.WriteAllText(Path.Combine(outside, "nested", Path.GetFileName(relativeFile)), "<Project />");
                        Directory.CreateSymbolicLink(buildDirectory, Path.Combine(outside, "nested"));
                        break;
                    case "ancestor":
                        break;
                }
            }
            catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
            catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
            catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); continue; }

            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
            if (state == "ancestor")
            {
                Require(result.IsComplete, $"An ancestor reparse point was rejected for {variant.Name}.");
            }
            else
            {
                Require(!result.IsComplete && result.Entries.All(entry => entry.Sha256 is null),
                    $"The {state} reparse point was not rejected for {variant.Name}.");
            }
        }
    }

}

static void RunReachablePackageAliasRegression(string parent)
{
    var root = Path.Combine(parent, "reachable-package-alias");
    var obj = Path.Combine(root, "obj");
    var fallback = Path.Combine(root, "fallback-packages");
    var unusedGlobal = Path.Combine(root, "global-packages");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var files = new[]
    {
        "build/direct.props",
        "buildTransitive/transitive.targets",
        "buildMultiTargeting/outer.targets",
        "props/import.props",
        "targets/import.targets",
        "runtimes/win-x64/lib/runtime.dll",
        "ref/net8.0/compile.dll",
        "contentFiles/cs/net8.0/source.cs",
        "analyzers/dotnet/cs/analyzer.dll",
        "runtimes/win-x64/native/native.dll",
        "resource/en-US/messages.resources",
        "tools/tool.ps1",
        "build/nested/helper.targets"
    };
    WriteAssets(assets, fallback, "Alias.Package", files, createFiles: true);

    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    document["packageFolders"] = new JsonObject
    {
        [unusedGlobal] = new JsonObject(),
        [fallback] = new JsonObject()
    };
    var fallbackPackageRoot = Path.Combine(fallback, "Alias.Package", "1.0.0");
    var globalPackageRoot = Path.Combine(unusedGlobal, "Alias.Package", "1.0.0");
    Directory.CreateDirectory(Path.GetDirectoryName(globalPackageRoot)!);
    Directory.Move(fallbackPackageRoot, globalPackageRoot);
    const string transitivePackageKey = "Transitive.Alias.Package/1.0.0";
    const string transitiveRelative = "tools/transitive-tool.ps1";
    var transitiveRoot = Path.Combine(fallback, "Transitive.Alias.Package", "1.0.0");
    var transitiveFile = Path.Combine(transitiveRoot, transitiveRelative.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(Path.GetDirectoryName(transitiveFile)!);
    File.WriteAllText(transitiveFile, "transitive fixture");
    document["libraries"]![transitivePackageKey] = new JsonObject
    {
        ["type"] = "package",
        ["path"] = transitivePackageKey,
        ["files"] = new JsonArray(transitiveRelative)
    };
    var directTarget = document["targets"]!["net8.0"]!["Alias.Package/1.0.0"]!.AsObject();
    directTarget["dependencies"] = new JsonObject { ["Transitive.Alias.Package"] = "1.0.0" };
    document["targets"]!["net8.0"]![transitivePackageKey] = new JsonObject { ["type"] = "package" };
    document["targets"]!.AsObject()["net8.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone();
    File.WriteAllText(assets, document.ToJsonString());
    var packageRoot = globalPackageRoot;
    foreach (var relative in files)
    {
        var physical = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        if (physical.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || physical.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
        {
            File.WriteAllText(physical, "<Project />");
        }
        else if (relative.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, physical, overwrite: true);
        }
    }

    var reachable = ResolvedGraphClassifier.GetReachablePackageInputPaths(assets)
        .Select(Path.GetFullPath)
        .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    Require(files.All(relative => reachable.Contains(Path.GetFullPath(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))))),
        "The preflight package-input set omitted a reachable package inventory file or fallback-root asset.");
    Require(reachable.Contains(Path.GetFullPath(transitiveFile)),
        "The preflight package-input set omitted a reachable transitive package asset from the RID graph.");

    var seed = Path.Combine(root, "seed.json");
    Require(CaptureCommand("baseline", assets, "--output", seed, "--no-telemetry").ExitCode == 0,
        "The reachable package alias fixture could not create a valid baseline seed.");
    var distinct = Path.Combine(root, "distinct-output.json");
    var packageBytes = files.ToDictionary(
        relative => relative,
        relative => File.ReadAllBytes(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
        StringComparer.Ordinal);
    Require(CaptureCommand("baseline", assets, "--output", distinct, "--no-telemetry").ExitCode == 0,
        "A genuinely distinct baseline output was rejected while package inputs were present.");
    foreach (var relative in files)
    {
        var packageFile = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
        var lexicalAlias = Path.Combine(Path.GetDirectoryName(packageFile)!, ".", Path.GetFileName(packageFile));
        Require(CaptureCommand("baseline", assets, "--output", lexicalAlias, "--no-telemetry").ExitCode == 2,
            $"A lexical alias to reachable package input '{relative}' was accepted.");
        Require(packageBytes[relative].SequenceEqual(File.ReadAllBytes(packageFile)),
            $"A lexical package-input alias attempt changed '{relative}'.");
    }
    var transitiveAlias = Path.Combine(Path.GetDirectoryName(transitiveFile)!, ".", Path.GetFileName(transitiveFile));
    var transitiveBefore = File.ReadAllBytes(transitiveFile);
    Require(CaptureCommand("baseline", assets, "--output", transitiveAlias, "--no-telemetry").ExitCode == 2,
        "A lexical alias to a reachable transitive package input was accepted.");
    Require(transitiveBefore.SequenceEqual(File.ReadAllBytes(transitiveFile)),
        "A transitive package-input alias attempt changed the package file.");

    var hardlinkOutputs = new[] { Path.Combine(root, "package-hardlink.json"), Path.Combine(root, "package-hardlink-second.json") };
    var representative = Path.Combine(packageRoot, "tools", "tool.ps1");
    Require(TryCreateHardLink(representative, hardlinkOutputs[0]), "The package hardlink alias regression could not create a hardlink.");
    Require(TryCreateHardLink(representative, hardlinkOutputs[1]), "The package multiple-hardlink alias regression could not create a second hardlink.");
    try
    {
        foreach (var output in hardlinkOutputs)
        {
            Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                "A hardlink alias to a reachable package input was accepted.");
            Require(packageBytes["tools/tool.ps1"].SequenceEqual(File.ReadAllBytes(representative)),
                "A package hardlink alias attempt changed the representative package input.");
        }
    }
    finally
    {
        foreach (var output in hardlinkOutputs)
        {
            if (File.Exists(output)) File.Delete(output);
        }
    }

    RunSymlinkPackageAliasCase(root, assets, representative, packageBytes["tools/tool.ps1"], "direct");
    var packageDirectory = Path.GetDirectoryName(representative)!;
    RunDirectorySymlinkPackageAliasCase(root, assets, packageDirectory, representative, packageBytes["tools/tool.ps1"], "interior");
    RunDirectorySymlinkPackageAliasCase(root, assets, root, representative, packageBytes["tools/tool.ps1"], "ancestor");
}

static void RunSymlinkPackageAliasCase(string root, string assets, string packageFile, byte[] before, string label)
{
    var output = Path.Combine(root, "package-" + label + "-symlink.json");
    try
    {
        File.CreateSymbolicLink(output, packageFile);
        Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
            $"A {label} symlink alias to a reachable package input was accepted.");
        Require(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A {label} symlink alias attempt changed the package input.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this platform."); }
    finally
    {
        if (File.Exists(output)) File.Delete(output);
    }
}

static void RunDirectorySymlinkPackageAliasCase(string root, string assets, string target, string packageFile, byte[] before, string label)
{
    var link = Path.Combine(root, "package-" + label + "-link");
    var output = Path.Combine(link, "tool.ps1");
    try
    {
        Directory.CreateSymbolicLink(link, target);
        Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
            $"A package {label}-directory reparse alias was accepted.");
        Require(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A package {label}-directory alias attempt changed the package input.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this platform."); }
}

static void RunMacOsRootAliasPredicateRegression()
{
    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-macos-alias-" + Guid.NewGuid().ToString("N"));
    var fakeRoot = Path.Combine(scratch, "root");
    var privateRoot = Path.Combine(fakeRoot, "private");
    Directory.CreateDirectory(privateRoot);
    try
    {
        var method = typeof(BaselineFileTransaction).GetMethod("IsSystemRootAlias", BindingFlags.NonPublic | BindingFlags.Static, binder: null,
            types: new[] { typeof(DirectoryInfo), typeof(string), typeof(bool) }, modifiers: null)
            ?? throw new InvalidOperationException("The macOS root-alias predicate seam is unavailable.");
        var standardTarget = Path.Combine(privateRoot, "var");
        var standardLink = Path.Combine(fakeRoot, "var");
        Directory.CreateDirectory(standardTarget);
        Directory.CreateSymbolicLink(standardLink, standardTarget);
        Require((bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, true })!,
            "The standard macOS /var to /private/var alias was rejected.");

        var callerTarget = Path.Combine(privateRoot, "caller-alias");
        var callerLink = Path.Combine(fakeRoot, "caller-alias");
        Directory.CreateDirectory(callerTarget);
        Directory.CreateSymbolicLink(callerLink, callerTarget);
        Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(callerLink), fakeRoot, true })!,
            "A caller-controlled root-level /private/<name> alias was accepted as system-owned.");

        var nestedTarget = Path.Combine(privateRoot, "nested");
        var nestedLink = Path.Combine(standardLink, "nested");
        Directory.CreateDirectory(nestedTarget);
        Directory.CreateSymbolicLink(nestedLink, nestedTarget);
        Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(nestedLink), fakeRoot, true })!,
            "A nested link below the standard /var alias was accepted as system-owned.");
        Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, false })!,
            "The macOS root-alias exception ignored a non-macOS platform.");
    }
    catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this filesystem."); }
    catch (IOException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this filesystem."); }
    catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this platform."); }
    finally
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

static void RenameJsonProperty(JsonObject parent, string oldName, string newName)
{
    var value = parent[oldName]?.DeepClone() ?? throw new InvalidOperationException($"Missing JSON property {oldName}.");
    parent.Remove(oldName);
    parent[newName] = value;
}

static void RunTelemetryStateMachineTests()
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
            "Telemetry opt-out did not suppress activation.");

        SetTelemetryHook(() => throw new InvalidOperationException("simulated telemetry failure"));
        Require(CommandLine.Run(new[] { "check", assets, "--baseline", baseline }) == 0,
            "Telemetry client failure changed the successful analysis result.");
    }
    finally
    {
        SetTelemetryHook(null);
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

static void SetTelemetryHook(Action? hook)
{
    var property = typeof(CommandLine).GetProperty("TelemetryHook", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Telemetry test hook was not found.");
    property.SetValue(null, hook);
}

static void SetOutputSerializer(Func<ReportDocument, OutputFormat, string>? hook)
{
    var property = typeof(CommandLine).GetProperty("OutputSerializer", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Output serializer test hook was not found.");
    property.SetValue(null, hook);
}

static void SetOutputSink(Action<string>? hook)
{
    var property = typeof(CommandLine).GetProperty("OutputSink", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Output sink test hook was not found.");
    property.SetValue(null, hook);
}

static (int ExitCode, string Output) CaptureCommand(params string[] args)
{
    var output = new StringWriter(CultureInfo.InvariantCulture);
    var error = new StringWriter(CultureInfo.InvariantCulture);
    var priorOutput = Console.Out;
    var priorError = Console.Error;
    try
    {
        Console.SetOut(output);
        Console.SetError(error);
        var exitCode = CommandLine.Run(args);
        return (exitCode, output.ToString() + error.ToString());
    }
    finally
    {
        Console.SetOut(priorOutput);
        Console.SetError(priorError);
    }
}

static void RunTelemetryPayloadAllowlistRegression()
{
    var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-telemetry-payload-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(scratch);
    try
    {
        var assets = Path.Combine(scratch, "TelemetryRepresentative.csproj.assets.json");
        var telemetryFiles = new[] { "tools/diagnostic-marker.ps1" };
        WriteAssets(assets, Path.Combine(scratch, "cache"), "Telemetry.Representative.Package", telemetryFiles, createFiles: true, projectFileName: "TelemetryRepresentative.csproj");
        var result = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(result.IsComplete && result.ResolvedPackageCount == 1, "The representative telemetry project was not classified completely.");

        var telemetryAssembly = typeof(Client).Assembly;
        var activationType = telemetryAssembly.GetType("KeelMatrix.Telemetry.Events.ActivationEvent", throwOnError: true)!;
        var activationConstructor = activationType.GetConstructors(BindingFlags.Instance | BindingFlags.NonPublic).Single();
        var activation = activationConstructor.Invoke(new object[]
        {
            "packagesurface",
            "0.1.0",
            "0.1.1",
            1,
            new string('a', 64),
            new string('b', 64),
            "dotnet",
            "windows",
            false,
            "2026-09-27T00:00:00Z"
        });
        var serializerType = telemetryAssembly.GetType("KeelMatrix.Telemetry.Serialization.TelemetrySerializer", throwOnError: true)!;
        var serialize = serializerType.GetMethod("Serialize", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("The shared telemetry serializer was not found.");
        var json = (string?)serialize.Invoke(null, new[] { activation, "packagesurface" });
        Require(json is not null, "The shared telemetry serializer rejected the representative activation payload.");

        using var document = JsonDocument.Parse(json!);
        var actual = document.RootElement.EnumerateObject().Select(property => property.Name).ToHashSet(StringComparer.Ordinal);
        var expectedFields = new[]
        {
            "event", "tool", "tool_version", "telemetry_version", "schema_version", "project_hash", "installation_hash", "runtime", "os", "ci", "timestamp"
        };
        var expected = new HashSet<string>(expectedFields, StringComparer.Ordinal);
        Require(actual.SetEquals(expected), $"Activation telemetry field allowlist drifted: {string.Join(", ", actual.Order(StringComparer.Ordinal))}");
        var serialized = document.RootElement.GetRawText();
        var forbiddenFields = new[] { "Telemetry.Representative.Package", "diagnostic-marker.ps1", "TelemetryRepresentative.csproj", "net8.0", "baseline", "PS007" };
        foreach (var forbidden in forbiddenFields)
        {
            Require(!serialized.Contains(forbidden, StringComparison.OrdinalIgnoreCase), $"Activation telemetry serialized forbidden scanned data '{forbidden}'.");
        }
    }
    finally
    {
        if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
    }
}

static void RunDiagnosticPathLeakRegression(string scratch)
{
    var marker = "HomeCacheMarker-" + Guid.NewGuid().ToString("N");
    var root = Path.Combine(scratch, marker);
    var obj = Path.Combine(root, "obj");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(obj);
    Directory.CreateDirectory(cache);
    var assets = Path.Combine(obj, "project.assets.json");
    WriteAssets(assets, cache, "MissingPackage", new List<string> { "build/missing.targets" });
    var absoluteMarker = Path.GetFullPath(root);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    document["targets"]!["net8.0"]!["MissingPackage/1.0.0"]!["build"] = new JsonObject { ["build/missing.targets"] = new JsonObject() };
    File.WriteAllText(assets, document.ToJsonString());
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/MissingPackage/1.0.0/build/missing.targets\" Condition=\"'$(SENSITIVE_CONDITION_MARKER)' == 'enabled'\" /></Project>");
    foreach (var format in new[] { "text", "json", "sarif" })
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var priorOutput = Console.Out;
        var priorError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            _ = CommandLine.Run(new[] { "scan", assets, "--format", format, "--no-telemetry" });
        }
        finally
        {
            Console.SetOut(priorOutput);
            Console.SetError(priorError);
        }

        Require(!output.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an absolute cache marker.");
        Require(!error.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an absolute cache marker.");
        Require(!output.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an unsupported condition marker.");
        Require(!error.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an unsupported condition marker.");
    }
}

static void RunAnalyzerExclusionRegression(string scratch, string cache, string obj)
{
    var analyzerRelativePath = "analyzers/dotnet/cs/valid.dll";
    var directAssets = Path.Combine(obj, "analyzer-excluded-direct.assets.json");
    WriteAnalyzerAssets(directAssets, cache, true, analyzerRelativePath);
    var directResult = ResolvedGraphClassifier.Analyze(directAssets, scratch, strictContent: false);
    Require(directResult.IsComplete && directResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
        "Direct ExcludeAssets=analyzers was not honored.");

    var transitiveAssets = Path.Combine(obj, "analyzer-excluded-transitive.assets.json");
    WriteAnalyzerAssets(transitiveAssets, cache, false, analyzerRelativePath);
    var transitiveResult = ResolvedGraphClassifier.Analyze(transitiveAssets, scratch, strictContent: false);
    Require(transitiveResult.IsComplete && transitiveResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
        "Transitive ExcludeAssets=analyzers was not honored.");
}

static void RunMalformedAssetsShapeRegression(string assets, string scratch)
{
    var projectReferencePath = Path.Combine(Path.GetDirectoryName(assets)!, "project-reference.assets.json");
    var projectReference = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    projectReference["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["type"] = "project",
        ["framework"] = ".NETCoreApp,Version=v8.0",
        ["dependencies"] = new JsonObject { ["XmlPackage"] = "1.0.0" },
        ["compile"] = new JsonObject { ["bin/placeholder/Referenced.Project.dll"] = new JsonObject() }
    };
    projectReference["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["type"] = "project",
        ["path"] = "../Referenced.Project",
        ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
    };
    File.WriteAllText(projectReferencePath, projectReference.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(projectReferencePath, scratch, strictContent: false);
        Require(result.IsComplete, $"A normal project-reference target with scalar framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
    }
    finally
    {
        File.Delete(projectReferencePath);
    }

    var malformedProjectDependencyPath = Path.Combine(Path.GetDirectoryName(assets)!, "project-reference-malformed-dependency.assets.json");
    var malformedProjectDependency = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    malformedProjectDependency["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["type"] = "project",
        ["framework"] = ".NETCoreApp,Version=v8.0",
        ["dependencies"] = new JsonObject { ["XmlPackage"] = 7 }
    };
    malformedProjectDependency["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["type"] = "project",
        ["path"] = "../Referenced.Project",
        ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
    };
    File.WriteAllText(malformedProjectDependencyPath, malformedProjectDependency.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(malformedProjectDependencyPath, scratch, strictContent: false);
        Require(!result.IsComplete, "A project-node dependency with a scalar value was accepted.");
    }
    finally
    {
        File.Delete(malformedProjectDependencyPath);
    }

    var arrayMetadataPath = Path.Combine(Path.GetDirectoryName(assets)!, "array-metadata.assets.json");
    var arrayMetadata = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    arrayMetadata["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonArray("System.Xml");
    arrayMetadata["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray("Microsoft.NETCore.App");
    File.WriteAllText(arrayMetadataPath, arrayMetadata.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(arrayMetadataPath, scratch, strictContent: false);
        Require(result.IsComplete, $"Legitimate array-valued framework metadata was rejected. reasons={string.Join(" | ", result.IncompleteReasons)}");
    }
    finally
    {
        File.Delete(arrayMetadataPath);
    }

    foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
    {
        ("framework-assemblies-number", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonArray(1)),
        ("framework-references-object-element", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray(new JsonObject { ["name"] = "Microsoft.NETCore.App" })),
        ("framework-references-null-element", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkReferences"] = new JsonArray { null }),
        ("framework-assemblies-object", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["frameworkAssemblies"] = new JsonObject()),
        ("framework-reference-case-duplicate", root =>
        {
            var package = root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!.AsObject();
            package["frameworkReferences"] = new JsonArray("Microsoft.NETCore.App");
            package["FRAMEWORKREFERENCES"] = new JsonArray("Microsoft.NETCore.App");
        }),
        ("target-dependency-number", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = 7 }),
        ("target-dependency-null", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = null })
    })
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        mutate(root);
        File.WriteAllText(path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Require(!result.IsComplete && result.IncompleteReasons.Count > 0, $"Malformed typed metadata '{name}' was accepted.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    var malformedShapes = new (string Name, Action<JsonObject> Mutate)[]
    {
        ("packageFolders-array", root => root["packageFolders"] = new JsonArray()),
        ("targets-array", root => root["targets"] = new JsonArray()),
        ("target-array", root => root["targets"]!["net8.0"] = new JsonArray()),
        ("libraries-array", root => root["libraries"] = new JsonArray()),
        ("frameworks-array", root => root["project"]!["frameworks"] = new JsonArray()),
        ("framework-dependencies-scalar", root => root["project"]!["frameworks"]!["net8.0"]!["dependencies"] = 7),
        ("package-path-number", root => root["libraries"]!["XmlPackage/1.0.0"]!["path"] = 7),
        ("missing-declared-target", root => root["targets"]!.AsObject().Remove("net8.0")),
        ("empty-target-with-declarations", root =>
        {
            root["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = new JsonObject() };
            root["targets"]!["net8.0"] = new JsonObject();
        }),
        ("malformed-content-files", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["contentFiles"] = new JsonArray()),
        ("missing-target-file-inventory", root => root["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["build"] = new JsonObject { ["build/unrepresented.targets"] = new JsonObject() }),
        ("ambiguous-library-identity", root => root["libraries"]!.AsObject()["xmlpackage/1.0.0"] = root["libraries"]!["XmlPackage/1.0.0"]!.DeepClone())
    };

    foreach (var (name, mutate) in malformedShapes)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        mutate(root);
        File.WriteAllText(path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Require(!result.IsComplete, $"Malformed assets shape '{name}' was not fail-closed.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    var cliAssets = Path.Combine(Path.GetDirectoryName(assets)!, "packageFolders-cli.assets.json");
    var cliRoot = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    cliRoot["packageFolders"] = new JsonArray();
    File.WriteAllText(cliAssets, cliRoot.ToJsonString());
    var cliExitCode = CommandLine.Run(new[] { "scan", cliAssets, "--no-telemetry" });
    File.Delete(cliAssets);
    Require(cliExitCode == 2, $"Malformed assets CLI invocation returned {cliExitCode}, expected 2.");
}

static void RunAssetsFormatRegression(string assets, string scratch)
{
    var v4Path = Path.Combine(Path.GetDirectoryName(assets)!, "format4.assets.json");
    var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    root["version"] = 4;
    root["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
    var project = root["project"]!.AsObject();
    var projectFramework = project["frameworks"]!["net8.0"]!.AsObject();
    projectFramework["framework"] = "net8.0";
    projectFramework["targetAlias"] = "net8.0";
    project["restore"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
    };
    File.WriteAllText(v4Path, root.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(v4Path, scratch, strictContent: false);
        Require(result.IsComplete && result.ResolvedPackageCount == 1, "A coherent assets format 4 graph was not classified.");

        var malformedV4 = new (string Name, Action<JsonObject> Mutate)[]
        {
            ("format4-missing-framework", value => value["project"]!["frameworks"]!["net8.0"]!.AsObject().Remove("framework")),
            ("format4-missing-dependency-groups", value => value.Remove("projectFileDependencyGroups")),
            ("format4-mismatched-restore-framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("format4-dependency-object-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray(new JsonObject())),
            ("format4-dependency-null-element", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray { null }),
            ("format4-dependency-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "XmlPackage >= 1.0.0")),
            ("format4-dependency-case-duplicate", value => value["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 1.0.0", "xmlpackage >= 1.0.0"))
        };

        foreach (var (name, mutate) in malformedV4)
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, name + ".assets.json");
            var malformed = JsonNode.Parse(File.ReadAllText(v4Path))!.AsObject();
            mutate(malformed);
            File.WriteAllText(path, malformed.ToJsonString());
            try
            {
                var malformedResult = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
                Require(!malformedResult.IsComplete, $"Malformed assets format 4 shape '{name}' was not fail-closed.");
                if (name == "format4-dependency-case-duplicate")
                {
                    var validBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "format4-case-baseline.json");
                    try
                    {
                        var coherentBaseline = CaptureCommand("baseline", v4Path, "--output", validBaseline, "--no-telemetry");
                        Require(coherentBaseline.ExitCode == 0,
                            $"The coherent format 4 dependency-group fixture could not create its baseline: exit={coherentBaseline.ExitCode}; output={coherentBaseline.Output}");
                        AssertFormat4DependencyGroupFailure(path, validBaseline);
                    }
                    finally
                    {
                        if (File.Exists(validBaseline)) File.Delete(validBaseline);
                    }
                }
            }
            finally
            {
                File.Delete(path);
            }
        }
    }
    finally
    {
        File.Delete(v4Path);
    }
}

static void RunPackageFoldersValidationRegression(string assets, string scratch)
{
    var cache = Path.Combine(scratch, "cache");
    var malformed = new (string Name, Func<JsonObject, JsonObject> Mutate)[]
    {
        ("empty", _ => new JsonObject()),
        ("relative", _ => new JsonObject { ["relative-cache"] = new JsonObject() }),
        ("whitespace", _ => new JsonObject { ["   "] = new JsonObject() }),
        ("null-value", _ => new JsonObject { [cache] = null }),
        ("array-value", _ => new JsonObject { [cache] = new JsonArray() })
    };

    foreach (var (name, mutate) in malformed)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["packageFolders"] = mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Require(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
                $"Malformed packageFolders shape '{name}' was accepted.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    var fallbackPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-fallback.assets.json");
    var fallback = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    fallback["packageFolders"] = new JsonObject
    {
        [Path.Combine(scratch, "fallback-cache")] = new JsonObject(),
        [Path.GetFullPath(cache).Replace(Path.DirectorySeparatorChar, '/')] = new JsonObject()
    };
    File.WriteAllText(fallbackPath, fallback.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(fallbackPath, scratch, strictContent: false);
        Require(result.IsComplete && result.ResolvedPackageCount == 1,
            $"Valid absolute fallback package folders were rejected: {string.Join(" | ", result.IncompleteReasons)}");
    }
    finally
    {
        File.Delete(fallbackPath);
    }

    var absoluteFolderPath = Path.Combine(scratch, "PRIVATE_CACHE_MARKER");
    var mixedPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-mixed-invalid.assets.json");
    var mixed = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    mixed["packageFolders"] = new JsonObject
    {
        [Path.GetFullPath(cache)] = new JsonObject(),
        [absoluteFolderPath] = null
    };
    File.WriteAllText(mixedPath, mixed.ToJsonString());
    try
    {
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var result = CaptureCommand("scan", mixedPath, "--format", format, "--no-telemetry");
            Require(result.ExitCode == 2 && !result.Output.Contains(absoluteFolderPath, StringComparison.OrdinalIgnoreCase),
                $"Mixed valid/invalid packageFolders leaked its absolute cache path in {format} output.");
            AssertNoSuccessfulSurfaceOutput(result.Output, format, "mixed invalid packageFolders");
        }
    }
    finally
    {
        File.Delete(mixedPath);
    }

    foreach (var marker in new[]
    {
        "/home/PRIVATE_CACHE_MARKER",
        "C:\\PRIVATE_CACHE_MARKER",
        "ARBITRARY_PRIVATE_MEMBER_MARKER"
    })
    {
        var structuralPath = Path.Combine(Path.GetDirectoryName(assets)!, "package-folders-structural-marker.assets.json");
        var original = File.ReadAllText(assets);
        var property = JsonSerializer.Serialize(marker);
        File.WriteAllText(structuralPath, "{" + property + ":true," + property + ":false," + original.TrimStart()[1..]);
        try
        {
            foreach (var format in new[] { "text", "json", "sarif" })
            {
                var result = CaptureCommand("scan", structuralPath, "--format", format, "--no-telemetry");
                Require(result.ExitCode == 2 && !result.Output.Contains(marker, StringComparison.OrdinalIgnoreCase),
                    $"Duplicate/unknown path-marked restore members leaked in {format} output.");
                AssertNoSuccessfulSurfaceOutput(result.Output, format, "path-marked restore members");
            }
        }
        finally
        {
            File.Delete(structuralPath);
        }
    }
}

static void AssertFormat4DependencyGroupFailure(string assets, string baseline)
{
    var baselineBefore = File.ReadAllBytes(baseline);
    foreach (var format in new[] { "text", "json", "sarif" })
    {
        var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
        Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                !scan.Output.Contains("Entries: 1", StringComparison.Ordinal),
            $"Format 4 casing-equivalent dependency groups emitted a clean {format} scan.");

        var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
        Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
            $"Format 4 casing-equivalent dependency groups emitted a clean {format} baseline.");
        Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
            $"Format 4 casing-equivalent dependency groups mutated the baseline.");

        var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
        Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                !check.Output.Contains("PS003", StringComparison.Ordinal),
            $"Format 4 casing-equivalent dependency groups emitted a clean {format} check.");
    }
}

static void RunCapabilityGroupValidationRegression(string scratch)
{
    var cases = new (string Name, string Group, bool Empty)[]
    {
        ("case-variant-build", "Build", false),
        ("case-variant-build-transitive", "BUILDTRANSITIVE", false),
        ("case-variant-build-multi-targeting", "BUILDMULTITARGETING", false),
        ("unknown-capability-group", "unknownCapabilityGroup", false),
        ("unknown-empty-group", "unknownEmptyGroup", true),
        ("null-capability-group", "unknownNullGroup", true)
    };

    foreach (var (name, group, empty) in cases)
    {
        var root = Path.Combine(scratch, "capability-group-" + name);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        const string assetPath = "build/Case.targets";
        WriteAssets(assets, cache, "CaseGroup.Package", new[] { assetPath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var targetPackage = document["targets"]!["net8.0"]!["CaseGroup.Package/1.0.0"]!.AsObject();
        targetPackage[group] = empty && group != "unknownNullGroup"
            ? new JsonObject()
            : empty
                ? null
                : new JsonObject { [assetPath] = new JsonObject() };
        document["targets"]!.AsObject()["net8.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone();
        File.WriteAllText(assets, document.ToJsonString());

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete && result.Entries.Count == 0 && result.IncompleteReasons.Count > 0,
            $"Unsupported target asset group '{group}' was accepted as a clean or partial result.");
    }

    var mixedRoot = Path.Combine(scratch, "capability-group-mixed-known-unknown");
    var mixedObj = Path.Combine(mixedRoot, "obj");
    var mixedCache = Path.Combine(mixedRoot, "cache");
    Directory.CreateDirectory(mixedObj);
    var mixedAssets = Path.Combine(mixedObj, "project.assets.json");
    WriteAssets(mixedAssets, mixedCache, "MixedGroup.Package", new List<string> { "build/Mixed.targets" }, createFiles: true);
    var mixedDocument = JsonNode.Parse(File.ReadAllText(mixedAssets))!.AsObject();
    var mixedPackage = mixedDocument["targets"]!["net8.0"]!["MixedGroup.Package/1.0.0"]!.AsObject();
    mixedPackage["build"] = new JsonObject { ["build/Mixed.targets"] = new JsonObject() };
    mixedPackage["Build"] = new JsonObject { ["build/Mixed.targets"] = new JsonObject() };
    File.WriteAllText(mixedAssets, mixedDocument.ToJsonString());
    var mixedResult = ResolvedGraphClassifier.Analyze(mixedAssets, mixedRoot, strictContent: false);
    Require(!mixedResult.IsComplete && mixedResult.Entries.Count == 0 && mixedResult.IncompleteReasons.Count > 0,
        "A mixed canonical and unsupported target asset-group set was accepted as a clean or partial result.");

    var transitiveRoot = Path.Combine(scratch, "capability-group-transitive");
    var transitiveObj = Path.Combine(transitiveRoot, "obj");
    var transitiveCache = Path.Combine(transitiveRoot, "cache");
    Directory.CreateDirectory(transitiveObj);
    var transitiveAssets = Path.Combine(transitiveObj, "project.assets.json");
    WriteAssets(transitiveAssets, transitiveCache, "RootGroup.Package", Array.Empty<string>(), createFiles: true);
    Directory.CreateDirectory(Path.Combine(transitiveCache, "TransitiveGroup.Package", "1.0.0", "build"));
    File.WriteAllText(Path.Combine(transitiveCache, "TransitiveGroup.Package", "1.0.0", "build", "Case.targets"), "fixture");
    var transitiveDocument = JsonNode.Parse(File.ReadAllText(transitiveAssets))!.AsObject();
    transitiveDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonObject
    {
        ["RootGroup.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
    };
    transitiveDocument["targets"]!["net8.0"]!["RootGroup.Package/1.0.0"]!["dependencies"] = new JsonObject
    {
        ["TransitiveGroup.Package"] = "[1.0.0, )"
    };
    transitiveDocument["targets"]!["net8.0"]!["TransitiveGroup.Package/1.0.0"] = new JsonObject
    {
        ["type"] = "package",
        ["Build"] = new JsonObject { ["build/Case.targets"] = new JsonObject() }
    };
    transitiveDocument["libraries"]!["TransitiveGroup.Package/1.0.0"] = new JsonObject
    {
        ["type"] = "package",
        ["path"] = "TransitiveGroup.Package/1.0.0",
        ["files"] = new JsonArray("build/Case.targets")
    };
    transitiveDocument["targets"]!.AsObject()["net8.0/win-x64"] = transitiveDocument["targets"]!["net8.0"]!.DeepClone();
    File.WriteAllText(transitiveAssets, transitiveDocument.ToJsonString());
    var transitiveResult = ResolvedGraphClassifier.Analyze(transitiveAssets, transitiveRoot, strictContent: false);
    Require(!transitiveResult.IsComplete && transitiveResult.Entries.Count == 0 && transitiveResult.IncompleteReasons.Count > 0,
        "An unsupported target asset group on a transitive RID package was accepted as a clean or partial result.");
}

static void RunFrameworkMonikerRegression(string assets, string scratch)
{
    var cases = new[]
    {
        (Declared: "netstandard2.0", Target: ".NETStandard,Version=v2.0"),
        (Declared: "NET8.0", Target: ".NETCoreApp,Version=v8.0"),
        (Declared: "net472", Target: ".NETFramework,Version=v4.7.2"),
        (Declared: "NET472", Target: "net4.7.2/win-x64")
    };

    foreach (var (declared, target) in cases)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, "framework-" + declared.Replace('.', '-') + ".assets.json");
        var root = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var package = root["targets"]!["net8.0"]!.DeepClone();
        root["targets"] = new JsonObject { [target] = package };
        root["project"]!["frameworks"] = new JsonObject
        {
            [declared] = new JsonObject
            {
                ["targetAlias"] = declared,
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            }
        };
        File.WriteAllText(path, root.ToJsonString());
        try
        {
            var result = ResolvedGraphClassifier.Analyze(path, scratch, strictContent: false);
            Require(result.IsComplete, $"Equivalent framework monikers were not reconciled: {declared} vs {target}: {string.Join("; ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    var format4Path = Path.Combine(Path.GetDirectoryName(assets)!, "framework-format4.assets.json");
    var format4 = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    format4["version"] = 4;
    format4["targets"] = new JsonObject { [".NETCoreApp,Version=v8.0"] = format4["targets"]!["net8.0"]!.DeepClone() };
    format4["project"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject
        {
            ["framework"] = ".NETCoreApp,Version=v8.0",
            ["targetAlias"] = "net8.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
        }
    };
    format4["project"]!["restore"]!["frameworks"] = new JsonObject
    {
        [".NETCoreApp,Version=v8.0"] = new JsonObject
        {
            ["framework"] = "net8.0",
            ["targetAlias"] = "NET8.0"
        }
    };
    format4["projectFileDependencyGroups"] = new JsonObject
    {
        [".NETCoreApp,Version=v8.0"] = new JsonArray()
    };
    File.WriteAllText(format4Path, format4.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Require(result.IsComplete, "Format 4 project/restore framework aliases were not reconciled.");
    }
    finally
    {
        File.Delete(format4Path);
    }

    var negativePath = Path.Combine(Path.GetDirectoryName(assets)!, "framework-missing-target.assets.json");
    var negative = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    negative["project"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() } },
        ["netstandard2.0"] = new JsonObject { ["dependencies"] = new JsonObject() }
    };
    File.WriteAllText(negativePath, negative.ToJsonString());
    try
    {
        var result = ResolvedGraphClassifier.Analyze(negativePath, scratch, strictContent: false);
        Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase)),
            "A declared framework with no equivalent target graph did not fail closed.");
    }
    finally
    {
        File.Delete(negativePath);
    }
}

static void RunRestoreIdentityCanonicalizationRegression(string assets, string scratch)
{
    var format4Path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-valid.assets.json");
    var baselinePath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-valid-baseline.json");
    var baseDocument = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    baseDocument["version"] = 4;
    baseDocument["projectFileDependencyGroups"] = new JsonObject
    {
        ["net8.0"] = new JsonArray("../Referenced/Referenced.csproj", "XmlPackage >= 1.0.0")
    };
    baseDocument["project"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject
        {
            ["framework"] = "net8.0",
            ["targetAlias"] = "net8.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
        }
    };
    baseDocument["project"]!["restore"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
    };
    File.WriteAllText(format4Path, baseDocument.ToJsonString());
    WriteGeneratedImportEvidence(format4Path);

    try
    {
        var valid = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Require(valid.IsComplete && valid.Entries.Count == 0 && valid.ResolvedPackageCount == 1,
            $"The valid dependency-group paths fixture was rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Require(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The valid restore-identity fixture could not create its baseline.");

        RunRestoreIdentitySetCompletenessRegression(format4Path, scratch, baselinePath);
        RunDependencyRequirementRegression(format4Path, scratch, baselinePath);

        foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
        {
            ("project-effective-framework", value => value["project"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("project-target-alias", value => value["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net9.0"),
            ("restore-effective-framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = "net9.0"),
            ("restore-target-alias", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net9.0"),
            ("restore-cross-map-framework", value =>
            {
                value["project"]!["restore"]!["frameworks"]!["net9.0"] = new JsonObject
                {
                    ["framework"] = "net9.0",
                    ["targetAlias"] = "net9.0"
                };
            }),
            ("target-framework-key", value =>
            {
                var targets = value["targets"]!.AsObject();
                var target = targets["net8.0"]!.DeepClone();
                targets.Remove("net8.0");
                targets["net9.0"] = target;
            }),
            ("rid-framework-key", value =>
            {
                var targets = value["targets"]!.AsObject();
                var target = targets["net8.0"]!.DeepClone();
                targets.Remove("net8.0");
                targets["net9.0/win-x64"] = target;
            }),
            ("dependency-group-key", value =>
            {
                var groups = value["projectFileDependencyGroups"]!.AsObject();
                var group = groups["net8.0"]!.DeepClone();
                groups.Remove("net8.0");
                groups["net9.0"] = group;
            })
        })
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-coherence-" + name + ".assets.json");
            var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
            mutate(document);
            File.WriteAllText(path, document.ToJsonString());
            try
            {
                AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        var duplicateMetadataPath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-conflicting-metadata.assets.json");
        var validJson = File.ReadAllText(format4Path);
        const string metadataToken = "\"targetAlias\":\"net8.0\"";
        var metadataIndex = validJson.IndexOf(metadataToken, StringComparison.Ordinal);
        Require(metadataIndex >= 0, "The valid restore-identity fixture has no target-alias metadata token.");
        File.WriteAllText(
            duplicateMetadataPath,
            validJson[..metadataIndex] + metadataToken + ",\"targetAlias\":\"net9.0\"" + validJson[(metadataIndex + metadataToken.Length)..]);
        try
        {
            AssertRestoreIdentityFailure(duplicateMetadataPath, scratch, baselinePath, "conflicting duplicate metadata");
        }
        finally
        {
            File.Delete(duplicateMetadataPath);
        }

        var duplicateMaps = new[]
        {
            (Name: "project-frameworks", MapPath: "project.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "restore-frameworks", MapPath: "project.restore.frameworks", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "target-graphs", MapPath: "targets", Alias: ".NETCoreApp,Version=v8.0"),
            (Name: "dependency-groups", MapPath: "projectFileDependencyGroups", Alias: ".NETCoreApp,Version=v8.0")
        };

        foreach (var duplicateMap in duplicateMaps)
        {
            foreach (var duplicateKind in new[] { "case", "moniker" })
            {
                var path = Path.Combine(Path.GetDirectoryName(assets)!, $"restore-identity-{duplicateMap.Name}-{duplicateKind}.assets.json");
                var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
                var map = GetIdentityMap(document, duplicateMap.MapPath);
                var alias = duplicateKind == "case" ? "NET8.0" : duplicateMap.Alias;
                var duplicate = map["net8.0"]!.DeepClone();
                if (duplicateMap.MapPath.Contains("frameworks", StringComparison.Ordinal))
                {
                    duplicate!["framework"] = "net9.0";
                    duplicate["targetAlias"] = "net9.0";
                }
                map[alias] = duplicate;
                File.WriteAllText(path, document.ToJsonString());
                try
                {
                    AssertRestoreIdentityFailure(path, scratch, baselinePath, $"{duplicateMap.Name} {duplicateKind} duplicate");
                }
                finally
                {
                    File.Delete(path);
                }
            }

            var exactPath = Path.Combine(Path.GetDirectoryName(assets)!, $"restore-identity-{duplicateMap.Name}-raw-exact.assets.json");
            var raw = File.ReadAllText(format4Path);
            var rawMap = GetIdentityMap(JsonNode.Parse(raw)!.AsObject(), duplicateMap.MapPath);
            var rawValue = rawMap["net8.0"]!.ToJsonString();
            var rawDuplicate = InsertExactPropertyDuplicate(raw, "net8.0", rawValue);
            File.WriteAllText(exactPath, rawDuplicate);
            try
            {
                AssertRestoreIdentityFailure(exactPath, scratch, baselinePath, $"{duplicateMap.Name} raw exact duplicate");
            }
            finally
            {
                File.Delete(exactPath);
            }
        }

        foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
        {
            ("library-no-slash", root => root["libraries"]!.AsObject()["MalformedPackage"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "MalformedPackage", ["files"] = new JsonArray()
            }),
            ("library-empty-id", root => root["libraries"]!.AsObject()["/1.0.0"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "/1.0.0", ["files"] = new JsonArray()
            }),
            ("library-empty-version", root => root["libraries"]!.AsObject()["MalformedPackage/"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "MalformedPackage/", ["files"] = new JsonArray()
            }),
            ("library-extra-slash", root => root["libraries"]!.AsObject()["Malformed/1.0.0/extra"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed/1.0.0/extra", ["files"] = new JsonArray()
            }),
            ("library-invalid-id", root => root["libraries"]!.AsObject()["Malformed Package/1.0.0"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed Package/1.0.0", ["files"] = new JsonArray()
            }),
            ("library-invalid-version", root => root["libraries"]!.AsObject()["Malformed/1.x"] = new JsonObject
            {
                ["type"] = "package", ["path"] = "Malformed/1.x", ["files"] = new JsonArray()
            }),
            ("target-no-slash", root => root["targets"]!["net8.0"]!.AsObject()["MalformedPackage"] = new JsonObject()),
            ("target-empty-id", root => root["targets"]!["net8.0"]!.AsObject()["/1.0.0"] = new JsonObject()),
            ("target-empty-version", root => root["targets"]!["net8.0"]!.AsObject()["MalformedPackage/"] = new JsonObject()),
            ("target-extra-slash", root => root["targets"]!["net8.0"]!.AsObject()["Malformed/1.0.0/extra"] = new JsonObject())
        })
        {
            var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-identity-" + name + ".assets.json");
            var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
            mutate(document);
            File.WriteAllText(path, document.ToJsonString());
            try
            {
                AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
            }
            finally
            {
                File.Delete(path);
            }
        }

        var recovered = ResolvedGraphClassifier.Analyze(format4Path, scratch, strictContent: false);
        Require(recovered.IsComplete && recovered.ResolvedPackageCount == 1,
            "The valid restore-identity fixture did not recover after invalid identity probes.");

        var narrowedRoot = Path.Combine(scratch, "restore-identity-narrowed");
        var validProject = Path.Combine(narrowedRoot, "Valid", "Valid.csproj");
        var invalidProject = Path.Combine(narrowedRoot, "Invalid", "Invalid.csproj");
        foreach (var projectPath in new[] { validProject, invalidProject })
        {
            var projectRoot = Path.GetDirectoryName(projectPath)!;
            var projectAssets = Path.Combine(projectRoot, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(projectAssets)!);
            File.WriteAllText(projectPath, "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            var narrowedDocument = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
            narrowedDocument["project"]!["restore"]!["projectPath"] = projectPath;
            if (projectPath == invalidProject)
            {
                narrowedDocument["project"]!["frameworks"]!.AsObject()[".NETCoreApp,Version=v8.0"] = new JsonObject
                {
                    ["framework"] = "net9.0",
                    ["targetAlias"] = "net9.0",
                    ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
                };
            }

            File.WriteAllText(projectAssets, narrowedDocument.ToJsonString());
            WriteGeneratedImportEvidence(projectAssets, Path.GetFileName(projectPath));
        }

        var solution = Path.Combine(narrowedRoot, "RestoreIdentity.sln");
        File.WriteAllText(solution, string.Join(Environment.NewLine,
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Valid\", \"Valid\\Valid.csproj\", \"{11111111-1111-1111-1111-111111111111}\"",
            "EndProject",
            "Project(\"{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}\") = \"Invalid\", \"Invalid\\Invalid.csproj\", \"{22222222-2222-2222-2222-222222222222}\"",
            "EndProject"));

        var aggregate = CaptureCommand("scan", solution, "--format", "json", "--no-telemetry");
        Require(aggregate.ExitCode == 2 && aggregate.Output.Contains("PS007", StringComparison.Ordinal) &&
                !aggregate.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
            "Selecting a valid and malformed restore identity together did not fail closed.");
        var narrowed = CaptureCommand("scan", solution, "--project", validProject, "--format", "json", "--no-telemetry");
        Require(narrowed.ExitCode == 0 && !narrowed.Output.Contains("PS007", StringComparison.Ordinal),
            $"--project did not recover the valid restore identity from a malformed sibling: exit={narrowed.ExitCode}; output={narrowed.Output}");
    }
    finally
    {
        if (File.Exists(format4Path)) File.Delete(format4Path);
        if (File.Exists(baselinePath)) File.Delete(baselinePath);
    }
}

static void RunRestoreJsonStructuralDuplicateRegression(string assets, string scratch)
{
    var baseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-baseline.json");
    Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
        "The structural-duplicate baseline could not be created.");

    var source = File.ReadAllText(assets);
    var root = JsonNode.Parse(source)!.AsObject();
    var cases = new List<(string Name, string Json)>
    {
        ("root-version-exact", InsertExactPropertyDuplicate(source, "version", "3")),
        ("root-version-case", InsertCaseVariantPropertyDuplicate(source, "version", "3")),
        ("root-targets-exact", InsertRootPropertyDuplicate(source, root, "targets")),
        ("root-libraries-case", InsertCaseVariantRootPropertyDuplicate(source, root, "libraries")),
        ("root-package-folders-exact", InsertRootPropertyDuplicate(source, root, "packageFolders")),
        ("project-exact", InsertRootPropertyDuplicate(source, root, "project")),
        ("project-restore-case", InsertCaseVariantPropertyDuplicate(source, "restore", root["project"]!["restore"]!.ToJsonString())),
        ("project-path-exact", InsertExactPropertyDuplicate(source, "projectPath", JsonSerializer.Serialize(root["project"]!["restore"]!["projectPath"]!.GetValue<string>()))),
        ("framework-map-case", InsertCaseVariantPropertyDuplicate(source, "frameworks", root["project"]!["frameworks"]!.ToJsonString())),
        ("library-type-case", InsertCaseVariantPropertyDuplicate(source, "type", JsonSerializer.Serialize("package"))),
        ("library-path-exact", InsertExactPropertyDuplicate(source, "path", JsonSerializer.Serialize("XmlPackage/1.0.0"))),
        ("library-files-case", InsertCaseVariantPropertyDuplicate(source, "files", root["libraries"]!["XmlPackage/1.0.0"]!["files"]!.ToJsonString()))
    };

    var dependency = root["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
    var dependencyVersion = dependency["version"]!.GetValue<string>();
    cases.Add(("direct-dependency-version-exact", InsertExactPropertyDuplicate(source, "version", JsonSerializer.Serialize(dependencyVersion), source.IndexOf("\"XmlPackage\":{", StringComparison.Ordinal))));

    var targetWithBuild = root.DeepClone()!.AsObject();
    targetWithBuild["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["build"] = new JsonObject
    {
        ["build/structure.targets"] = new JsonObject()
    };
    var targetJson = targetWithBuild.ToJsonString();
    cases.Add(("target-package-build-case", InsertCaseVariantPropertyDuplicate(targetJson, "build", "{\"build/structure.targets\":{}}")));

    foreach (var (name, json) in cases)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-" + name + ".assets.json");
        File.WriteAllText(path, json);
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    var spellingCases = new List<(string Name, Action<JsonObject> Mutate)>
    {
        ("root-version-alias", value => RenameJsonProperty(value, "version", "Version")),
        ("root-targets-alias", value => RenameJsonProperty(value, "targets", "TARGETS")),
        ("root-libraries-alias", value => RenameJsonProperty(value, "libraries", "Libraries")),
        ("root-package-folders-alias", value => RenameJsonProperty(value, "packageFolders", "PACKAGEFOLDERS")),
        ("root-project-alias", value => RenameJsonProperty(value, "project", "Project")),
        ("root-dependency-groups-alias", value =>
        {
            value["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
            RenameJsonProperty(value, "projectFileDependencyGroups", "PROJECTFILEDEPENDENCYGROUPS");
        }),
        ("project-restore-alias", value => RenameJsonProperty(value["project"]!.AsObject(), "restore", "RESTORE")),
        ("project-frameworks-alias", value => RenameJsonProperty(value["project"]!.AsObject(), "frameworks", "FRAMEWORKS")),
        ("project-path-alias", value => RenameJsonProperty(value["project"]!["restore"]!.AsObject(), "projectPath", "PROJECTPATH")),
        ("restore-compiler-alias", value =>
        {
            var restore = value["project"]!["restore"]!.AsObject();
            restore["compilerApiVersion"] = "4.0";
            RenameJsonProperty(restore, "compilerApiVersion", "COMPILERAPIVERSION");
        }),
        ("framework-framework-alias", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["framework"] = "net8.0";
            RenameJsonProperty(framework, "framework", "FRAMEWORK");
        }),
        ("framework-target-alias-alias", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["targetAlias"] = "net8.0";
            RenameJsonProperty(framework, "targetAlias", "TARGETALIAS");
        }),
        ("framework-dependencies-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!.AsObject(), "dependencies", "DEPENDENCIES")),
        ("dependency-version-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject(), "version", "VERSION")),
        ("dependency-target-alias", value => RenameJsonProperty(value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject(), "target", "TARGET")),
        ("dependency-include-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["include"] = "all";
            RenameJsonProperty(dependency, "include", "INCLUDE");
        }),
        ("dependency-exclude-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exclude"] = "none";
            RenameJsonProperty(dependency, "exclude", "EXCLUDE");
        }),
        ("dependency-optional-alias", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["privateAssets"] = "all";
            RenameJsonProperty(dependency, "privateAssets", "PRIVATEASSETS");
        }),
        ("library-type-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "type", "TYPE")),
        ("library-path-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "path", "PATH")),
        ("library-files-alias", value => RenameJsonProperty(value["libraries"]!["XmlPackage/1.0.0"]!.AsObject(), "files", "FILES")),
        ("target-package-group-alias", value =>
        {
            var package = value["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!.AsObject();
            package["build"] = new JsonObject { ["build/structure.targets"] = new JsonObject() };
            RenameJsonProperty(package, "build", "BUILD");
        })
    };

    foreach (var (name, mutate) in spellingCases)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-spelling-" + name + ".assets.json");
        var malformed = JsonNode.Parse(source)!.AsObject();
        mutate(malformed);
        File.WriteAllText(path, malformed.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    var unknownSpellingCases = new List<(string Name, Action<JsonObject> Mutate)>
    {
        ("dependency-exclude-near-spelling", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exlcude"] = "all";
        }),
        ("dependency-exclude-near-spelling-conflicting", value =>
        {
            var dependency = value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!.AsObject();
            dependency["exclude"] = "none";
            dependency["exlcude"] = "all";
        }),
        ("framework-dependencies-near-spelling", value =>
        {
            var framework = value["project"]!["frameworks"]!["net8.0"]!.AsObject();
            framework["dependencis"] = framework["dependencies"]!.DeepClone();
        }),
        ("library-path-near-spelling", value =>
            value["libraries"]!["XmlPackage/1.0.0"]!["pathh"] = "XmlPackage/1.0.0"),
        ("target-package-dependencies-near-spelling", value =>
            value["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencis"] = new JsonObject())
    };

    foreach (var (name, mutate) in unknownSpellingCases)
    {
        var path = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-unknown-spelling-" + name + ".assets.json");
        var malformed = JsonNode.Parse(source)!.AsObject();
        mutate(malformed);
        File.WriteAllText(path, malformed.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baseline, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    var extensionPath = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-explicit-extensions.assets.json");
    var extensionDocument = JsonNode.Parse(source)!.AsObject();
    extensionDocument["x-root"] = "ignored";
    extensionDocument["project"]!["x-project"] = "ignored";
    extensionDocument["project"]!["restore"]!["x-restore"] = "ignored";
    extensionDocument["project"]!["frameworks"]!["net8.0"]!["x-framework"] = "ignored";
    extensionDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["x-dependency"] = "ignored";
    extensionDocument["libraries"]!["XmlPackage/1.0.0"]!["x-library"] = "ignored";
    extensionDocument["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["x-target-package"] = "ignored";
    extensionDocument["packageFolders"]!.AsObject().First().Value!["x-package-folder"] = "ignored";
    File.WriteAllText(extensionPath, extensionDocument.ToJsonString());
    try
    {
        var extensionResult = ResolvedGraphClassifier.Analyze(extensionPath, scratch, strictContent: false);
        Require(extensionResult.IsComplete, $"The explicit x- extension namespace was rejected: {string.Join(" | ", extensionResult.IncompleteReasons)}");
    }
    finally
    {
        File.Delete(extensionPath);
    }

    var generatedProps = Path.Combine(Path.GetDirectoryName(assets)!, "Test.csproj.nuget.g.props");
    var generatedPropsBefore = File.ReadAllBytes(generatedProps);
    try
    {
        foreach (var (name, xml) in new[]
        {
            ("generated-import-project-alias", "<Project><Import project=\"unused.props\" /></Project>"),
            ("generated-import-condition-alias", "<Project condition=\"'$(TargetFramework)' == 'net8.0'\" />"),
            ("generated-import-condition-near-spelling", "<Project><Import Project=\"unused.props\" Conditon=\"'$(TargetFramework)' == 'net8.0'\" /></Project>"),
            ("generated-import-case-duplicate", "<Project><Import Project=\"unused.props\" project=\"unused.props\" /></Project>"),
            ("generated-import-element-alias", "<Project><import Project=\"unused.props\" /></Project>"),
            ("generated-import-element-near-spelling", "<Project><Improt Project=\"unused.props\" /></Project>")
        })
        {
            File.WriteAllText(generatedProps, xml);
            AssertRestoreIdentityFailure(assets, scratch, baseline, name);
        }

        File.WriteAllText(generatedProps, "<Project xmlns:x=\"urn:keelmatrix:packagesurface:extension\" x:marker=\"ignored\" />");
        var xmlExtensionResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(xmlExtensionResult.IsComplete, $"The explicit XML extension namespace was rejected: {string.Join(" | ", xmlExtensionResult.IncompleteReasons)}");
    }
    finally
    {
        File.WriteAllBytes(generatedProps, generatedPropsBefore);
    }

    var contentAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata.assets.json");
    var contentBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-metadata-baseline.json");
    WriteContentAssets(contentAssets, Path.Combine(scratch, "content-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
    Require(CaptureCommand("baseline", contentAssets, "--output", contentBaseline, "--no-telemetry").ExitCode == 0,
        "The content metadata baseline could not be created.");
    try
    {
        var contentDocument = JsonNode.Parse(File.ReadAllText(contentAssets))!.AsObject();
        var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
        RenameJsonProperty(contentMetadata, "codeLanguage", "CODELANGUAGE");
        File.WriteAllText(contentAssets, contentDocument.ToJsonString());
        AssertRestoreIdentityFailure(contentAssets, scratch, contentBaseline, "content-file-metadata-alias");
    }
    finally
    {
        if (File.Exists(contentAssets)) File.Delete(contentAssets);
        if (File.Exists(contentBaseline)) File.Delete(contentBaseline);
    }

    var contentUnknownAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-unknown-spelling.assets.json");
    var contentUnknownBaseline = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-unknown-spelling-baseline.json");
    WriteContentAssets(contentUnknownAssets, Path.Combine(scratch, "content-unknown-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
    Require(CaptureCommand("baseline", contentUnknownAssets, "--output", contentUnknownBaseline, "--no-telemetry").ExitCode == 0,
        "The content unknown-spelling baseline could not be created.");
    try
    {
        var contentDocument = JsonNode.Parse(File.ReadAllText(contentUnknownAssets))!.AsObject();
        var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
        contentMetadata.Remove("codeLanguage");
        contentMetadata["codeLanguge"] = "cs";
        File.WriteAllText(contentUnknownAssets, contentDocument.ToJsonString());
        AssertRestoreIdentityFailure(contentUnknownAssets, scratch, contentUnknownBaseline, "content-file-metadata-near-spelling");
    }
    finally
    {
        if (File.Exists(contentUnknownAssets)) File.Delete(contentUnknownAssets);
        if (File.Exists(contentUnknownBaseline)) File.Delete(contentUnknownBaseline);
    }

    var contentExtensionAssets = Path.Combine(Path.GetDirectoryName(assets)!, "restore-structure-content-explicit-extension.assets.json");
    WriteContentAssets(contentExtensionAssets, Path.Combine(scratch, "content-extension-cache"), "Test.csproj", "contentFiles/cs/any/content.cs", "cs", "Compile");
    try
    {
        var contentDocument = JsonNode.Parse(File.ReadAllText(contentExtensionAssets))!.AsObject();
        var contentMetadata = contentDocument["targets"]!["net8.0"]!["Content.Package/1.0.0"]!["contentFiles"]!["contentFiles/cs/any/content.cs"]!.AsObject();
        contentMetadata["x-content"] = "ignored";
        File.WriteAllText(contentExtensionAssets, contentDocument.ToJsonString());
        var contentResult = ResolvedGraphClassifier.Analyze(contentExtensionAssets, scratch, strictContent: false);
        Require(contentResult.IsComplete, $"The explicit x- content extension was rejected: {string.Join(" | ", contentResult.IncompleteReasons)}");
    }
    finally
    {
        if (File.Exists(contentExtensionAssets)) File.Delete(contentExtensionAssets);
    }

    File.Delete(baseline);
}

static void RunRestoreOptionalMetadataRegression(string assets, string scratch)
{
    var format3Path = Path.Combine(scratch, "restore-optional-format3.assets.json");
    var format3Baseline = Path.Combine(scratch, "restore-optional-format3-baseline.json");
    var format3 = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    var restore = format3["project"]!["restore"]!.AsObject();
    restore["fallbackFolders"] = new JsonArray(@"C:\Program Files (x86)\Microsoft Visual Studio\Shared\NuGetPackages");
    restore["SdkAnalysisLevel"] = "10.0.400";
    format3["project"]!["frameworks"]!["net8.0"]!["downloadDependencies"] = new JsonArray
    {
        new JsonObject { ["name"] = "PackageDownload", ["version"] = "[1.0.0, )" }
    };
    File.WriteAllText(format3Path, format3.ToJsonString());
    WriteGeneratedImportEvidence(format3Path);

    try
    {
        AssertValidRestoreMetadata(format3Path, scratch, format3Baseline, "format 3 restore metadata");

        var format4Path = Path.Combine(scratch, "restore-optional-format4.assets.json");
        var format4Baseline = Path.Combine(scratch, "restore-optional-format4-baseline.json");
        var format4 = JsonNode.Parse(File.ReadAllText(format3Path))!.AsObject();
        format4["version"] = 4;
        format4["project"]!["frameworks"]!["net8.0"]!["framework"] = "net8.0";
        format4["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net8.0";
        format4["project"]!["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        format4["projectFileDependencyGroups"] = new JsonObject
        {
            ["net8.0"] = new JsonArray()
        };
        File.WriteAllText(format4Path, format4.ToJsonString());
        WriteGeneratedImportEvidence(format4Path);
        try
        {
            AssertValidRestoreMetadata(format4Path, scratch, format4Baseline, "format 4 restore metadata");
        }
        finally
        {
            if (File.Exists(format4Path)) File.Delete(format4Path);
            if (File.Exists(format4Baseline)) File.Delete(format4Baseline);
            foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
            {
                var generatedPath = Path.Combine(scratch, generated);
                if (File.Exists(generatedPath)) File.Delete(generatedPath);
            }
        }

        foreach (var (name, mutate) in new (string Name, Action<JsonObject> Mutate)[]
        {
            ("fallback-folders-type", value => value["project"]!["restore"]!["fallbackFolders"] = "not-an-array"),
            ("fallback-folders-item-type", value => value["project"]!["restore"]!["fallbackFolders"] = new JsonArray("valid", 42)),
            ("sdk-analysis-level-type", value => value["project"]!["restore"]!["SdkAnalysisLevel"] = 10)
        })
        {
            var invalidPath = Path.Combine(scratch, "restore-optional-invalid-" + name + ".assets.json");
            var invalid = JsonNode.Parse(File.ReadAllText(format3Path))!.AsObject();
            mutate(invalid);
            File.WriteAllText(invalidPath, invalid.ToJsonString());
            WriteGeneratedImportEvidence(invalidPath);
            try
            {
                AssertRestoreIdentityFailure(invalidPath, scratch, format3Baseline, name);
            }
            finally
            {
                if (File.Exists(invalidPath)) File.Delete(invalidPath);
            }
        }
    }
    finally
    {
        if (File.Exists(format3Path)) File.Delete(format3Path);
        if (File.Exists(format3Baseline)) File.Delete(format3Baseline);
        foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
        {
            var generatedPath = Path.Combine(scratch, generated);
            if (File.Exists(generatedPath)) File.Delete(generatedPath);
        }
    }
}

static void AssertValidRestoreMetadata(string assets, string projectRoot, string baseline, string label)
{
    var analyzed = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
    Require(analyzed.IsComplete && analyzed.Entries.Count == 0,
        $"{label} was rejected: {string.Join(" | ", analyzed.IncompleteReasons)}");

    var scan = CaptureCommand("scan", assets, "--format", "json", "--no-telemetry");
    Require(scan.ExitCode == 0 && !scan.Output.Contains("PS007", StringComparison.Ordinal),
        $"{label} scan failed: {scan.Output}");
    Require(CaptureCommand("baseline", assets, "--output", baseline, "--format", "json", "--no-telemetry").ExitCode == 0,
        $"{label} baseline failed.");
    var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", "json", "--no-telemetry");
    Require(check.ExitCode == 0 && !check.Output.Contains("PS007", StringComparison.Ordinal),
        $"{label} check failed: {check.Output}");
}

static void RunRestoreMetadataShapeRegression(string sourceAssets, string scratch)
{
    var validFormat3Path = Path.Combine(scratch, "restore-shape-valid-format3.assets.json");
    var validFormat3Baseline = Path.Combine(scratch, "restore-shape-valid-format3-baseline.json");
    var validFormat4Path = Path.Combine(scratch, "restore-shape-valid-format4.assets.json");
    var validFormat4Baseline = Path.Combine(scratch, "restore-shape-valid-format4-baseline.json");
    var validFormat3 = CreateValidRestoreShapeDocument(sourceAssets, format: 3);
    var validFormat4 = CreateValidRestoreShapeDocument(sourceAssets, format: 4);
    File.WriteAllText(validFormat3Path, validFormat3.ToJsonString());
    WriteGeneratedImportEvidence(validFormat3Path);
    File.WriteAllText(validFormat4Path, validFormat4.ToJsonString());
    WriteGeneratedImportEvidence(validFormat4Path);

    try
    {
        AssertValidRestoreMetadata(validFormat3Path, scratch, validFormat3Baseline, "all valid restore metadata shapes (format 3)");
        AssertValidRestoreMetadata(validFormat4Path, scratch, validFormat4Baseline, "all valid restore metadata shapes (format 4)");

        var mutations = new (string Name, Action<JsonObject> Mutate)[]
        {
            ("restore.centralPackageVersionsManagementEnabled", value => value["project"]!["restore"]!["centralPackageVersionsManagementEnabled"] = 42),
            ("restore.configFilePaths", value => value["project"]!["restore"]!["configFilePaths"] = "bad"),
            ("restore.crossTargeting", value => value["project"]!["restore"]!["crossTargeting"] = "bad"),
            ("restore.frameworks", value => value["project"]!["restore"]!["frameworks"] = new JsonArray()),
            ("restore.originalTargetFrameworks", value => value["project"]!["restore"]!["originalTargetFrameworks"] = new JsonArray(42)),
            ("restore.outputPath", value => value["project"]!["restore"]!["outputPath"] = 42),
            ("restore.packagesPath", value => value["project"]!["restore"]!["packagesPath"] = 42),
            ("restore.projectName", value => value["project"]!["restore"]!["projectName"] = 42),
            ("restore.projectPath", value => value["project"]!["restore"]!["projectPath"] = 42),
            ("restore.projectStyle", value => value["project"]!["restore"]!["projectStyle"] = 42),
            ("restore.projectUniqueName", value => value["project"]!["restore"]!["projectUniqueName"] = 42),
            ("restore.restoreAuditProperties", value => value["project"]!["restore"]!["restoreAuditProperties"] = "bad"),
            ("restore.fallbackFolders", value => value["project"]!["restore"]!["fallbackFolders"] = new JsonArray("valid", 42)),
            ("restore.sources", value => value["project"]!["restore"]!["sources"] = new JsonArray()),
            ("restore.warningProperties", value => value["project"]!["restore"]!["warningProperties"] = "bad"),
            ("restore.compilerApiVersion", value => value["project"]!["restore"]!["compilerApiVersion"] = 42),
            ("restore.SdkAnalysisLevel", value => value["project"]!["restore"]!["SdkAnalysisLevel"] = 10),
            ("project.framework", value => value["project"]!["frameworks"]!["net8.0"]!["framework"] = 42),
            ("project.targetAlias", value => value["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = 42),
            ("project.dependencies", value => value["project"]!["frameworks"]!["net8.0"]!["dependencies"] = new JsonArray()),
            ("project.dependencies.value", value => value["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"] = new JsonObject { ["version"] = 42 }),
            ("project.assetTargetFallback", value => value["project"]!["frameworks"]!["net8.0"]!["assetTargetFallback"] = "bad"),
            ("project.centralPackageVersions", value => value["project"]!["frameworks"]!["net8.0"]!["centralPackageVersions"] = new JsonArray()),
            ("project.centralPackageVersions.value", value => value["project"]!["frameworks"]!["net8.0"]!["centralPackageVersions"]!["XmlPackage"] = 42),
            ("project.frameworkReferences", value => value["project"]!["frameworks"]!["net8.0"]!["frameworkReferences"] = new JsonArray()),
            ("project.frameworkReferences.value", value => value["project"]!["frameworks"]!["net8.0"]!["frameworkReferences"]!["Microsoft.NETCore.App"] = "bad"),
            ("project.imports", value => value["project"]!["frameworks"]!["net8.0"]!["imports"] = new JsonArray("net8.0", 42)),
            ("project.runtimeIdentifierGraphPath", value => value["project"]!["frameworks"]!["net8.0"]!["runtimeIdentifierGraphPath"] = 42),
            ("project.downloadDependencies", value => value["project"]!["frameworks"]!["net8.0"]!["downloadDependencies"] = new JsonArray(new JsonObject { ["name"] = "PackageDownload", ["version"] = 42 })),
            ("project.warn", value => value["project"]!["frameworks"]!["net8.0"]!["warn"] = "bad"),
            ("restore.framework.framework", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["framework"] = 42),
            ("restore.framework.targetAlias", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["targetAlias"] = 42),
            ("restore.framework.projectReferences", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["projectReferences"] = new JsonArray()),
            ("restore.framework.projectReferences.value", value => value["project"]!["restore"]!["frameworks"]!["net8.0"]!["projectReferences"]!["Project.csproj"] = "bad")
        };

        foreach (var (name, mutate) in mutations)
        {
            foreach (var (format, template, baseline) in new[]
            {
                ("format3", validFormat3, validFormat3Baseline),
                ("format4", validFormat4, validFormat4Baseline)
            })
            {
                var invalidPath = Path.Combine(scratch, "restore-shape-invalid-" + format + "-" + name.Replace('.', '-') + ".assets.json");
                var invalid = template.DeepClone().AsObject();
                mutate(invalid);
                File.WriteAllText(invalidPath, invalid.ToJsonString());
                WriteGeneratedImportEvidence(invalidPath);
                try
                {
                    AssertRestoreIdentityFailure(invalidPath, scratch, baseline, name + " (" + format + ")");
                }
                finally
                {
                    if (File.Exists(invalidPath)) File.Delete(invalidPath);
                    foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
                    {
                        var generatedPath = Path.Combine(scratch, generated);
                        if (File.Exists(generatedPath)) File.Delete(generatedPath);
                    }
                }
            }
        }
    }
    finally
    {
        foreach (var path in new[] { validFormat3Path, validFormat3Baseline, validFormat4Path, validFormat4Baseline })
        {
            if (File.Exists(path)) File.Delete(path);
        }
        foreach (var generated in new[] { "Test.csproj.nuget.g.props", "Test.csproj.nuget.g.targets" })
        {
            var generatedPath = Path.Combine(scratch, generated);
            if (File.Exists(generatedPath)) File.Delete(generatedPath);
        }
    }
}

static JsonObject CreateValidRestoreShapeDocument(string sourceAssets, int format)
{
    var root = JsonNode.Parse(File.ReadAllText(sourceAssets))!.AsObject();
    var project = root["project"]!.AsObject();
    var restore = project["restore"]!.AsObject();
    restore["centralPackageVersionsManagementEnabled"] = true;
    restore["configFilePaths"] = new JsonArray("nuget.config");
    restore["crossTargeting"] = true;
    restore["originalTargetFrameworks"] = new JsonArray("net8.0");
    restore["outputPath"] = "obj/";
    restore["packagesPath"] = "packages/";
    restore["projectName"] = "Test";
    restore["projectStyle"] = "PackageReference";
    restore["projectUniqueName"] = "Test.csproj";
    restore["restoreAuditProperties"] = new JsonObject
    {
        ["enableAudit"] = "true",
        ["auditLevel"] = "low",
        ["auditMode"] = "direct",
        ["suppressedAdvisories"] = new JsonObject { ["ADV-1"] = null }
    };
    restore["fallbackFolders"] = new JsonArray("fallback");
    restore["sources"] = new JsonObject { ["https://example.test/v3/index.json"] = new JsonObject() };
    restore["warningProperties"] = new JsonObject
    {
        ["allWarningsAsErrors"] = true,
        ["noWarn"] = new JsonArray("NU1000"),
        ["warnAsError"] = new JsonArray("NU1605"),
        ["warnNotAsError"] = new JsonArray("NU1701")
    };
    restore["compilerApiVersion"] = "4.0";
    restore["SdkAnalysisLevel"] = "10.0.400";
    restore["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject
        {
            ["framework"] = "net8.0",
            ["targetAlias"] = "net8.0",
            ["projectReferences"] = new JsonObject
            {
                ["Project.csproj"] = new JsonObject
                {
                    ["projectPath"] = "Project.csproj",
                    ["includeAssets"] = "runtime; build",
                    ["excludeAssets"] = "none",
                    ["privateAssets"] = "all"
                }
            }
        }
    };

    var framework = project["frameworks"]!["net8.0"]!.AsObject();
    framework["framework"] = "net8.0";
    framework["targetAlias"] = "net8.0";
    framework["dependencies"] = new JsonObject { ["XmlPackage"] = "[1.0.0, )" };
    framework["assetTargetFallback"] = true;
    framework["centralPackageVersions"] = new JsonObject { ["XmlPackage"] = "1.0.0" };
    framework["frameworkReferences"] = new JsonObject
    {
        ["Microsoft.NETCore.App"] = new JsonObject { ["privateAssets"] = "all" }
    };
    framework["imports"] = new JsonArray("net461");
    framework["runtimeIdentifierGraphPath"] = "PortableRuntimeIdentifierGraph.json";
    framework["downloadDependencies"] = new JsonArray
    {
        new JsonObject { ["name"] = "PackageDownload", ["version"] = "[1.0.0, )" }
    };
    framework["warn"] = true;

    if (format == 4)
    {
        root["version"] = 4;
        root["projectFileDependencyGroups"] = new JsonObject { ["net8.0"] = new JsonArray() };
    }

    return root;
}

static void RunReachabilityClosureRegression(string scratch)
{
    var orphanRoot = Path.Combine(scratch, "reachability-orphan");
    var orphanObj = Path.Combine(orphanRoot, "obj");
    var orphanCache = Path.Combine(orphanRoot, "cache");
    Directory.CreateDirectory(orphanObj);
    var orphanAssets = Path.Combine(orphanObj, "project.assets.json");
    WriteAssets(orphanAssets, orphanCache, "Direct.Root", Array.Empty<string>());
    var orphanDocument = JsonNode.Parse(File.ReadAllText(orphanAssets))!.AsObject();
    var orphanFiles = new[]
    {
        "build/orphan.targets",
        "analyzers/dotnet/cs/orphan.dll",
        "contentFiles/cs/any/orphan.cs",
        "runtimes/win-x64/native/orphan.dll",
        "tools/orphan.ps1"
    };
    AddPackageToAssets(orphanDocument, orphanCache, "Orphan.Package", orphanFiles, new JsonObject
    {
        ["build"] = new JsonObject { ["build/orphan.targets"] = new JsonObject() },
        ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/orphan.dll"] = new JsonObject() },
        ["contentFiles"] = new JsonObject
        {
            ["contentFiles/cs/any/orphan.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
        },
        ["native"] = new JsonObject { ["runtimes/win-x64/native/orphan.dll"] = new JsonObject() },
        ["tools"] = new JsonObject { ["tools/orphan.ps1"] = new JsonObject() }
    });
    var orphanAnalyzer = Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "analyzers", "dotnet", "cs", "orphan.dll");
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, orphanAnalyzer, overwrite: true);
    File.WriteAllText(Path.Combine(orphanCache, "Orphan.Package", "1.0.0", "build", "orphan.targets"), "<Project />");
    File.WriteAllText(orphanAssets, orphanDocument.ToJsonString());
    File.WriteAllText(Path.Combine(orphanObj, "Test.csproj.nuget.g.props"), "<Project><Import Project=\"$(NuGetPackageRoot)/Orphan.Package/1.0.0/build/orphan.targets\" /></Project>");
    var orphanResult = ResolvedGraphClassifier.Analyze(orphanAssets, orphanRoot, strictContent: false);
    Require(!orphanResult.IsComplete && orphanResult.Entries.Count == 0 && orphanResult.ResolvedPackageCount == 0,
        "An orphan package with build, analyzer, content, native, and tool assets was classified as reachable.");

    var islandRoot = Path.Combine(scratch, "reachability-island");
    var islandObj = Path.Combine(islandRoot, "obj");
    var islandCache = Path.Combine(islandRoot, "cache");
    Directory.CreateDirectory(islandObj);
    var islandAssets = Path.Combine(islandObj, "project.assets.json");
    WriteAssets(islandAssets, islandCache, "Direct.Root", Array.Empty<string>());
    var islandDocument = JsonNode.Parse(File.ReadAllText(islandAssets))!.AsObject();
    AddPackageToAssets(islandDocument, islandCache, "Island.Root", Array.Empty<string>(), new JsonObject
    {
        ["dependencies"] = new JsonObject { ["Island.Leaf"] = "1.0.0" }
    });
    AddPackageToAssets(islandDocument, islandCache, "Island.Leaf", Array.Empty<string>());
    File.WriteAllText(islandAssets, islandDocument.ToJsonString());
    var islandResult = ResolvedGraphClassifier.Analyze(islandAssets, islandRoot, strictContent: false);
    Require(!islandResult.IsComplete && islandResult.Entries.Count == 0,
        "A disconnected multi-package dependency island was classified as reachable.");

    var rootedRoot = Path.Combine(scratch, "reachability-rooted-graphs");
    var rootedObj = Path.Combine(rootedRoot, "obj");
    var rootedCache = Path.Combine(rootedRoot, "cache");
    Directory.CreateDirectory(rootedObj);
    var rootedAssets = Path.Combine(rootedObj, "project.assets.json");
    WriteAssets(rootedAssets, rootedCache, "Direct.Root", new List<string> { "tools/direct.ps1" }, createFiles: true);
    var rootedDocument = JsonNode.Parse(File.ReadAllText(rootedAssets))!.AsObject();
    rootedDocument["targets"]!["net8.0"]!["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject
    {
        ["Chain.Leaf"] = "1.0.0",
        ["Cycle.A"] = "1.0.0"
    };
    AddPackageToAssets(rootedDocument, rootedCache, "Chain.Leaf", new List<string> { "tools/chain.ps1" }, new JsonObject { ["tools"] = new JsonObject { ["tools/chain.ps1"] = new JsonObject() } });
    AddPackageToAssets(rootedDocument, rootedCache, "Cycle.A", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.B"] = "1.0.0" } });
    AddPackageToAssets(rootedDocument, rootedCache, "Cycle.B", Array.Empty<string>(), new JsonObject { ["dependencies"] = new JsonObject { ["Cycle.A"] = "1.0.0" } });
    var projectReferenceKey = "Referenced.Project/1.0.0";
    rootedDocument["targets"]!["net8.0"]![projectReferenceKey] = new JsonObject { ["dependencies"] = new JsonObject { ["Project.Root"] = "1.0.0" } };
    rootedDocument["libraries"]![projectReferenceKey] = new JsonObject { ["type"] = "project", ["path"] = "../Referenced.Project", ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj" };
    var projectReferenceAnalyzer = "analyzers/dotnet/cs/project-reference.dll";
    AddPackageToAssets(rootedDocument, rootedCache, "Project.Root", new List<string> { "tools/project-root.ps1", projectReferenceAnalyzer }, new JsonObject
    {
        ["tools"] = new JsonObject { ["tools/project-root.ps1"] = new JsonObject() },
        ["analyzers"] = new JsonObject { [projectReferenceAnalyzer] = new JsonObject() }
    });
    var projectReferenceAnalyzerPath = Path.Combine(rootedCache, "Project.Root", "1.0.0", projectReferenceAnalyzer.Replace('/', Path.DirectorySeparatorChar));
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, projectReferenceAnalyzerPath, overwrite: true);
    File.WriteAllText(rootedAssets, rootedDocument.ToJsonString());
    var rootedResult = ResolvedGraphClassifier.Analyze(rootedAssets, rootedRoot, strictContent: false);
    Require(rootedResult.IsComplete && rootedResult.ResolvedPackageCount == 5,
        "Direct, transitive, cyclic, and project-reference roots did not share one complete closure: " + string.Join(" | ", rootedResult.IncompleteReasons));
    Require(rootedResult.Entries.Any(entry => entry.PackageId == "Chain.Leaf" && entry.Relationship == "transitive") &&
            rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Relationship == "transitive"),
        "Reachable transitive and project-reference packages were not classified with the expected relationship.");
    Require(rootedResult.Entries.Any(entry => entry.PackageId == "Project.Root" && entry.Capability == CapabilityKind.CompilerExtension && entry.Active),
        "An analyzer exported only through a project-reference root was not activated: " + string.Join(" | ", rootedResult.Entries.Select(entry => $"{entry.PackageId}:{entry.Capability}:{entry.Active}:{entry.PackageRelativePath}")) + " reasons=" + string.Join(" | ", rootedResult.IncompleteReasons));

    var ridRoot = Path.Combine(scratch, "reachability-rid");
    var ridObj = Path.Combine(ridRoot, "obj");
    var ridCache = Path.Combine(ridRoot, "cache");
    Directory.CreateDirectory(ridObj);
    var ridAssets = Path.Combine(ridObj, "project.assets.json");
    WriteAssets(ridAssets, ridCache, "Direct.Root", Array.Empty<string>());
    var ridDocument = JsonNode.Parse(File.ReadAllText(ridAssets))!.AsObject();
    AddPackageToAssets(ridDocument, ridCache, "Rid.Only", new List<string> { "runtimes/win-x64/native/rid-only.dll" }, new JsonObject
    {
        ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() }
    });
    ridDocument["targets"]!["net8.0"]!.AsObject().Remove("Rid.Only/1.0.0");
    var ridTarget = ridDocument["targets"]!["net8.0"]!.DeepClone()!.AsObject();
    ridTarget["Direct.Root/1.0.0"]!["dependencies"] = new JsonObject { ["Rid.Only"] = "1.0.0" };
    ridTarget["Rid.Only/1.0.0"] = new JsonObject { ["native"] = new JsonObject { ["runtimes/win-x64/native/rid-only.dll"] = new JsonObject() } };
    ridDocument["targets"]!.AsObject()["net8.0/win-x64"] = ridTarget;
    File.WriteAllText(ridAssets, ridDocument.ToJsonString());
    var ridResult = ResolvedGraphClassifier.Analyze(ridAssets, ridRoot, strictContent: false);
    Require(ridResult.IsComplete && ridResult.Entries.Count(entry => entry.PackageId == "Rid.Only") == 1 &&
            ridResult.Entries.Single(entry => entry.PackageId == "Rid.Only").RuntimeIdentifier == "win-x64",
        "A package reachable only from the RID-specific target graph was not confined to that graph: " + string.Join(" | ", ridResult.IncompleteReasons));
}

static void RunVersionAwareReachabilityConflictRegression(string scratch)
{
    var cases = new (string Name, string RootPackage, string? TransitivePackage, string DirectRange)[]
    {
        ("exact-direct-selection", "Versioned.Direct", null, "[1.0.0, 1.0.0]"),
        ("range-direct-selection", "Versioned.Range.Direct", null, "[1.0.0, 2.0.0)"),
        ("range-transitive-selection", "Versioned.Transitive.Root", "Versioned.Transitive.Dependency", "[1.0.0, 2.0.0)")
    };

    foreach (var (name, rootPackage, transitivePackage, directRange) in cases)
    {
        var root = Path.Combine(scratch, "version-aware-" + name);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, rootPackage, Array.Empty<string>());
        var baseline = Path.Combine(obj, "baseline.json");
        Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            $"The valid {name} baseline could not be created.");

        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        if (transitivePackage is null)
        {
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]![rootPackage]!["version"] = directRange;
        }
        else
        {
            document["targets"]!["net8.0"]![rootPackage + "/1.0.0"]!["dependencies"] = new JsonObject
            {
                [transitivePackage] = directRange
            };
            AddPackageToAssets(document, cache, transitivePackage, Array.Empty<string>(), new JsonObject
            {
                ["dependencies"] = new JsonObject()
            });
        }

        File.WriteAllText(assets, document.ToJsonString());
        var validResult = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var expectedResolvedCount = transitivePackage is null ? 1 : 2;
        Require(validResult.IsComplete && validResult.ResolvedPackageCount == expectedResolvedCount,
            $"The valid {name} version-range graph was not resolved before adding the conflicting sibling: {string.Join(" | ", validResult.IncompleteReasons)}");

        AddPackageVersionToAssets(document, cache, transitivePackage ?? rootPackage, "1.0.1", Array.Empty<string>());
        File.WriteAllText(assets, document.ToJsonString());
        AssertRestoreIdentityFailure(assets, root, baseline, name);
    }

    var surfaceRoot = Path.Combine(scratch, "version-aware-all-surfaces");
    var surfaceObj = Path.Combine(surfaceRoot, "obj");
    var surfaceCache = Path.Combine(surfaceRoot, "cache");
    Directory.CreateDirectory(surfaceObj);
    var surfaceAssets = Path.Combine(surfaceObj, "project.assets.json");
    WriteAssets(surfaceAssets, surfaceCache, "Versioned.Surface", Array.Empty<string>());
    var surfaceBaseline = Path.Combine(surfaceObj, "baseline.json");
    Require(CaptureCommand("baseline", surfaceAssets, "--output", surfaceBaseline, "--no-telemetry").ExitCode == 0,
        "The valid all-surfaces baseline could not be created.");

    var surfaceDocument = JsonNode.Parse(File.ReadAllText(surfaceAssets))!.AsObject();
    surfaceDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Versioned.Surface"]!["version"] = "[1.0.0, 1.0.0]";
    var surfaceFiles = new[]
    {
        "build/conflict.targets",
        "analyzers/dotnet/cs/conflict.dll",
        "contentFiles/cs/any/conflict.cs",
        "runtimes/win-x64/native/conflict.dll",
        "tools/conflict.ps1"
    };
    AddPackageVersionToAssets(surfaceDocument, surfaceCache, "Versioned.Surface", "1.0.1", surfaceFiles, new JsonObject
    {
        ["build"] = new JsonObject { ["build/conflict.targets"] = new JsonObject() },
        ["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/conflict.dll"] = new JsonObject() },
        ["contentFiles"] = new JsonObject
        {
            ["contentFiles/cs/any/conflict.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "C#" }
        },
        ["native"] = new JsonObject { ["runtimes/win-x64/native/conflict.dll"] = new JsonObject() },
        ["tools"] = new JsonObject { ["tools/conflict.ps1"] = new JsonObject() }
    });
    var surfacePackageRoot = Path.Combine(surfaceCache, "Versioned.Surface", "1.0.1");
    File.WriteAllText(Path.Combine(surfacePackageRoot, "build", "conflict.targets"), "<Project />");
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
        Path.Combine(surfacePackageRoot, "analyzers", "dotnet", "cs", "conflict.dll"), overwrite: true);
    File.WriteAllText(Path.Combine(surfaceObj, "Test.csproj.nuget.g.targets"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/Versioned.Surface/1.0.1/build/conflict.targets\" /></Project>");
    File.WriteAllText(surfaceAssets, surfaceDocument.ToJsonString());
    AssertRestoreIdentityFailure(surfaceAssets, surfaceRoot, surfaceBaseline, "all-surfaces-multi-version-selection");
}

static void RunVersionEquivalenceRegression(string scratch)
{
    var equivalent = new[] { "1", "1.0", "1.0.0", "1.0.0.0", "01.000.000.000" };
    var identities = equivalent.Select(value =>
    {
        Require(PackageIdentity.TryCreate("Version.Package", value, out var identity), $"NuGet-compatible version '{value}' was rejected.");
        return identity;
    }).ToArray();
    Require(identities.All(identity => identity.Equals(identities[0])), "Equivalent numeric package versions did not share one identity.");
    Require(PackageIdentity.TryCreate("Version.Package", "1.2.3.4", out var nonZeroFourth) &&
            !PackageIdentity.TryCreate("Version.Package", "1.2.3.4.5", out _),
        "The four-component version boundary was not enforced.");
    Require(PackageIdentity.TryCreate("Version.Package", "1.0.0-Alpha.1+Build.1", out var prerelease) &&
            PackageIdentity.TryCreate("version.package", "1.0.0-alpha.1+Other.2", out var equivalentPrerelease) &&
            prerelease.Equals(equivalentPrerelease),
        "Prerelease casing/components or build metadata did not use the canonical identity grammar.");
    foreach (var invalid in new[]
    {
        "1..0",
        "1.0-",
        "1.0+",
        "1.0.0-alpha.01",
        "1.0.0-01",
        "1.0.0-α",
        "1.0.0-alpha..1",
        "1.0.0-",
        "1.0.0/child",
        "1.0.0,,2.0.0",
        "1.0.0-+build",
        " 1.0.0"
    })
    {
        Require(!PackageIdentity.TryCreate("Version.Package", invalid, out _), $"Malformed version '{invalid}' was accepted.");
    }

    var root = Path.Combine(scratch, "version-ranges");
    var obj = Path.Combine(root, "obj");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    WriteAssets(assets, cache, "Range.Package", Array.Empty<string>());
    var source = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    foreach (var range in new[] { "[1, 1]", "[1.0, 1.0]", "[1.0.0, 1.0.0]", "[1.0.0.0, 1.0.0.0]", "1.0.0.0" })
    {
        source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = range;
        File.WriteAllText(assets, source.ToJsonString());
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(result.IsComplete, $"Dependency range '{range}' disagreed with the canonical package identity: {string.Join(" | ", result.IncompleteReasons)}");
    }

    source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = "[1.0.0.0.0, )";
    File.WriteAllText(assets, source.ToJsonString());
    var invalidRange = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
    Require(!invalidRange.IsComplete && invalidRange.Entries.Count == 0, "A five-component dependency range was accepted.");

    foreach (var invalid in new[] { "1.0.0-alpha.01", "1.0.0-01", "1.0.0-α", "[1.0.0-alpha.01, )", "(1.0.0-01, 2.0.0]" })
    {
        source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = invalid;
        File.WriteAllText(assets, source.ToJsonString());
        var malformed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!malformed.IsComplete && malformed.Entries.Count == 0,
            $"Malformed NuGet version range '{invalid}' was accepted.");
    }
}

static void RunMalformedVersionEndToEndRegression(string scratch)
{
    var root = Path.Combine(scratch, "malformed-version-surfaces");
    var obj = Path.Combine(root, "obj");
    var cache = Path.Combine(root, "cache");
    Directory.CreateDirectory(obj);
    var assets = Path.Combine(obj, "project.assets.json");
    var files = new[]
    {
        "build/surface.targets",
        "analyzers/dotnet/cs/surface.dll",
        "contentFiles/cs/any/surface.cs",
        "runtimes/win-x64/native/surface.dll",
        "tools/surface.ps1"
    };
    WriteAssets(assets, cache, "Version.Surface", files, createFiles: true);
    var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
    var targetPackage = document["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!.AsObject();
    targetPackage["build"] = new JsonObject { ["build/surface.targets"] = new JsonObject() };
    targetPackage["analyzers"] = new JsonObject { ["analyzers/dotnet/cs/surface.dll"] = new JsonObject() };
    targetPackage["contentFiles"] = new JsonObject
    {
        ["contentFiles/cs/any/surface.cs"] = new JsonObject { ["buildAction"] = "Compile", ["codeLanguage"] = "cs" }
    };
    targetPackage["native"] = new JsonObject { ["runtimes/win-x64/native/surface.dll"] = new JsonObject() };
    targetPackage["tools"] = new JsonObject { ["tools/surface.ps1"] = new JsonObject() };
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location,
        Path.Combine(cache, "Version.Surface", "1.0.0", "analyzers", "dotnet", "cs", "surface.dll"), overwrite: true);
    File.WriteAllText(Path.Combine(cache, "Version.Surface", "1.0.0", "build", "surface.targets"), "<Project />");
    File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
        "<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/1.0.0/build/surface.targets\" /></Project>");
    File.WriteAllText(assets, document.ToJsonString());

    var baseline = Path.Combine(obj, "baseline.json");
    var baselineResult = CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry");
    Require(baselineResult.ExitCode == 0,
        "The all-capability version fixture could not create its baseline: " + baselineResult.Output);
    var validSource = File.ReadAllText(assets);
    var invalidVersions = new[] { "1.0.0-alpha.01", "1.0.0-01", "1.0.0-α" };
    foreach (var invalid in invalidVersions)
    {
        var malformedPath = Path.Combine(obj, "invalid-key-" + invalid.Replace('/', '_') + ".assets.json");
        File.WriteAllText(malformedPath, validSource.Replace("Version.Surface/1.0.0", "Version.Surface/" + invalid, StringComparison.Ordinal));
        try
        {
            AssertRestoreIdentityFailure(malformedPath, root, baseline, "malformed package identity " + invalid);
        }
        finally
        {
            File.Delete(malformedPath);
        }
    }

    var libraryPathDocument = JsonNode.Parse(validSource)!.AsObject();
    libraryPathDocument["libraries"]!["Version.Surface/1.0.0"]!["path"] = "Version.Surface/1.0.0-alpha.01";
    var libraryPath = Path.Combine(obj, "invalid-library-path.assets.json");
    File.WriteAllText(libraryPath, libraryPathDocument.ToJsonString());
    try
    {
        AssertRestoreIdentityFailure(libraryPath, root, baseline, "malformed library package path");
    }
    finally
    {
        File.Delete(libraryPath);
    }

    var directRangeDocument = JsonNode.Parse(validSource)!.AsObject();
    directRangeDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Version.Surface"]!["version"] = "[1.0.0-alpha.01, )";
    var directRange = Path.Combine(obj, "invalid-direct-range.assets.json");
    File.WriteAllText(directRange, directRangeDocument.ToJsonString());
    try
    {
        AssertRestoreIdentityFailure(directRange, root, baseline, "malformed direct dependency range");
    }
    finally
    {
        File.Delete(directRange);
    }

    var targetRangeDocument = JsonNode.Parse(validSource)!.AsObject();
    targetRangeDocument["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!["dependencies"] = new JsonObject
    {
        ["Version.Surface"] = "[1.0.0-01, )"
    };
    var targetRange = Path.Combine(obj, "invalid-target-range.assets.json");
    File.WriteAllText(targetRange, targetRangeDocument.ToJsonString());
    try
    {
        AssertRestoreIdentityFailure(targetRange, root, baseline, "malformed target dependency range");
    }
    finally
    {
        File.Delete(targetRange);
    }

    var projectRangeDocument = JsonNode.Parse(validSource)!.AsObject();
    projectRangeDocument["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["dependencies"] = new JsonObject { ["Version.Surface"] = "[1.0.0-01, )" }
    };
    projectRangeDocument["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
    {
        ["type"] = "project",
        ["path"] = "../Referenced.Project",
        ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
    };
    var projectRange = Path.Combine(obj, "invalid-project-range.assets.json");
    File.WriteAllText(projectRange, projectRangeDocument.ToJsonString());
    try
    {
        AssertRestoreIdentityFailure(projectRange, root, baseline, "malformed project dependency range");
    }
    finally
    {
        File.Delete(projectRange);
    }

    var format4Document = JsonNode.Parse(validSource)!.AsObject();
    format4Document["version"] = 4;
    format4Document["projectFileDependencyGroups"] = new JsonObject
    {
        ["net8.0"] = new JsonArray("Version.Surface >= 1.0.0")
    };
    format4Document["project"]!["frameworks"]!["net8.0"]!["framework"] = "net8.0";
    format4Document["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net8.0";
    format4Document["project"]!["restore"]!["frameworks"] = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
    };
    var format4 = Path.Combine(obj, "malformed-format4-version.assets.json");
    var format4Baseline = Path.Combine(obj, "malformed-format4-version-baseline.json");
    File.WriteAllText(format4, format4Document.ToJsonString());
    var format4BaselineResult = CaptureCommand("baseline", format4, "--output", format4Baseline, "--no-telemetry");
    Require(format4BaselineResult.ExitCode == 0,
        "The valid format 4 version fixture could not create its baseline: " + format4BaselineResult.Output);
    try
    {
        format4Document["projectFileDependencyGroups"]!["net8.0"]![0] = "Version.Surface >= 1.0.0-alpha.01";
        File.WriteAllText(format4, format4Document.ToJsonString());
        AssertRestoreIdentityFailure(format4, root, format4Baseline, "malformed format 4 dependency requirement");
    }
    finally
    {
        if (File.Exists(format4)) File.Delete(format4);
        if (File.Exists(format4Baseline)) File.Delete(format4Baseline);
    }

    var generatedTargets = Path.Combine(obj, "Test.csproj.nuget.g.targets");
    var generatedTargetsBefore = File.ReadAllBytes(generatedTargets);
    try
    {
        foreach (var invalid in invalidVersions)
        {
            File.WriteAllText(generatedTargets,
                $"<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/{invalid}/build/surface.targets\" /></Project>");
            AssertRestoreIdentityFailure(assets, root, baseline, "malformed generated import version " + invalid);
        }
    }
    finally
    {
        File.WriteAllBytes(generatedTargets, generatedTargetsBefore);
    }

    var baselineDocument = JsonNode.Parse(File.ReadAllText(baseline))!.AsObject();
    foreach (var entry in baselineDocument["entries"]!.AsArray().OfType<JsonObject>())
    {
        entry["version"] = "1.0.0-alpha.01";
    }
    var malformedBaseline = Path.Combine(obj, "malformed-version-baseline.json");
    File.WriteAllText(malformedBaseline, baselineDocument.ToJsonString());
    try
    {
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var check = CaptureCommand("check", assets, "--baseline", malformedBaseline, "--format", format, "--no-telemetry");
            Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
                $"Malformed baseline version was accepted in {format} output.");
            AssertNoSuccessfulSurfaceOutput(check.Output, format, "malformed baseline version " + format);
        }
    }
    finally
    {
        File.Delete(malformedBaseline);
    }
}

static void RunRestoreIdentitySetCompletenessRegression(string format4Path, string scratch, string baselinePath)
{
    var cases = new (string Name, Action<JsonObject> Mutate)[]
    {
        ("project-framework-missing-from-restore", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["net9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("restore-framework-missing-from-project", document =>
        {
            document["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0"
            };
        }),
        ("dependency-group-missing-from-project", document =>
            document["projectFileDependencyGroups"]!.AsObject().Remove("net8.0")),
        ("dependency-group-extra-framework", document =>
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray()),
        ("rid-framework-missing-from-restore", document =>
            document["targets"]!.AsObject()["net9.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone()),
        ("case-folded-project-framework-set-difference", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["NET9.0"] = new JsonObject
            {
                ["framework"] = "NET9.0",
                ["targetAlias"] = "NET9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["NET9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["NET9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("equivalent-moniker-framework-set-difference", document =>
        {
            document["project"]!["frameworks"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = ".NETCoreApp,Version=v9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()[".NETCoreApp,Version=v9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        })
    };

    foreach (var (name, mutate) in cases)
    {
        var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "restore-set-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
        }
        finally
        {
            File.Delete(path);
        }
    }
}

static void RunDependencyRequirementRegression(string format4Path, string scratch, string baselinePath)
{
    var cases = new (string Name, Action<JsonObject> Mutate)[]
    {
        ("project-dependency-invalid-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "not-a-version-range"),
        ("project-dependency-empty-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "  "),
        ("project-dependency-unsupported-version", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "6.*"),
        ("project-dependency-version-mismatch", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "[2.0.0, )"),
        ("target-dependency-invalid-range", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" }),
        ("target-dependency-invalid-operator", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = ">= 1.0.0" }),
        ("target-dependency-version-mismatch", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "[2.0.0, )" }),
        ("format4-package-invalid-operator", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage => 1.0.0")),
        ("format4-package-invalid-version", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= not-a-version")),
        ("format4-package-version-mismatch", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 2.0.0")),
        ("format4-package-empty-value", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("  ")),
        ("format4-absolute-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("C:/Referenced/Referenced.csproj")),
        ("format4-unsupported-project-extension", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.txt")),
        ("format4-ambiguous-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.csproj extra"))
    };

    foreach (var (name, mutate) in cases)
    {
        var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
        }
        finally
        {
            File.Delete(path);
        }
    }

    var multiTargetPath = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-multi-rid.assets.json");
    var multiTarget = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
    var projectFrameworks = multiTarget["project"]!["frameworks"]!.AsObject();
    projectFrameworks["net9.0"] = new JsonObject
    {
        ["framework"] = "net9.0",
        ["targetAlias"] = "net9.0",
        ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
    };
    multiTarget["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
    {
        ["framework"] = "net9.0",
        ["targetAlias"] = "net9.0"
    };
    var targets = multiTarget["targets"]!.AsObject();
    targets["net8.0/win-x64"] = targets["net8.0"]!.DeepClone();
    targets["net9.0"] = targets["net8.0"]!.DeepClone();
    var groups = multiTarget["projectFileDependencyGroups"]!.AsObject();
    groups["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
    targets["net8.0/win-x64"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" };
    File.WriteAllText(multiTargetPath, multiTarget.ToJsonString());
    try
    {
        AssertRestoreIdentityFailure(multiTargetPath, scratch, baselinePath, "multi-target-rid dependency requirement");
    }
    finally
    {
        File.Delete(multiTargetPath);
    }
}

static JsonObject GetIdentityMap(JsonObject document, string path)
{
    var segments = path.Split('.');
    JsonNode current = document;
    foreach (var segment in segments)
    {
        current = current[segment] ?? throw new InvalidOperationException($"Identity map path '{path}' was not found.");
    }

    return current.AsObject();
}

static string InsertExactPropertyDuplicate(string json, string propertyName, string value, int searchStart = 0)
{
    var token = JsonSerializer.Serialize(propertyName) + ":" + value;
    var first = json.IndexOf(token, searchStart, StringComparison.Ordinal);
    Require(first >= 0, $"Could not locate exact property token for '{propertyName}'.");
    return json[..first] + token + "," + token + json[(first + token.Length)..];
}

static string InsertCaseVariantPropertyDuplicate(string json, string propertyName, string value)
{
    var token = JsonSerializer.Serialize(propertyName) + ":" + value;
    var first = json.IndexOf(token, StringComparison.Ordinal);
    Require(first >= 0, $"Could not locate case-variant property token for '{propertyName}'.");
    var variant = propertyName.Length == 0
        ? propertyName
        : char.IsUpper(propertyName[0])
            ? char.ToLowerInvariant(propertyName[0]) + propertyName[1..]
            : char.ToUpperInvariant(propertyName[0]) + propertyName[1..];
    var duplicate = JsonSerializer.Serialize(variant) + ":" + value;
    return json[..first] + token + "," + duplicate + json[(first + token.Length)..];
}

static string InsertRootPropertyDuplicate(string json, JsonObject root, string propertyName) =>
    InsertExactPropertyDuplicate(json, propertyName, root[propertyName]!.ToJsonString());

static string InsertCaseVariantRootPropertyDuplicate(string json, JsonObject root, string propertyName) =>
    InsertCaseVariantPropertyDuplicate(json, propertyName, root[propertyName]!.ToJsonString());

static void AddPackageToAssets(JsonObject document, string cache, string packageId, IReadOnlyList<string> files, JsonObject? targetMetadata = null, string libraryType = "package")
{
    var packageKey = packageId + "/1.0.0";
    AddPackageVersionToAssets(document, cache, packageId, "1.0.0", files, targetMetadata, libraryType);
}

static void AddPackageVersionToAssets(JsonObject document, string cache, string packageId, string version, IReadOnlyList<string> files, JsonObject? targetMetadata = null, string libraryType = "package")
{
    var packageKey = packageId + "/" + version;
    var target = targetMetadata?.DeepClone()?.AsObject() ?? new JsonObject();
    document["targets"]!["net8.0"]!.AsObject()[packageKey] = target;
    var filesNode = new JsonArray();
    foreach (var file in files)
    {
        filesNode.Add(file);
    }

    document["libraries"]!.AsObject()[packageKey] = new JsonObject
    {
        ["type"] = libraryType,
        ["path"] = packageKey,
        ["files"] = filesNode
    };
    var packageRoot = Path.Combine(cache, packageId, version);
    Directory.CreateDirectory(packageRoot);
    foreach (var file in files)
    {
        var physical = Path.Combine(packageRoot, file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
        File.WriteAllText(physical, "fixture");
    }
}

static void AssertRestoreIdentityFailure(string assets, string projectRoot, string baseline, string label)
{
    var baselineBefore = File.ReadAllBytes(baseline);
    foreach (var format in new[] { "text", "json", "sarif" })
    {
        var direct = ResolvedGraphClassifier.Analyze(assets, projectRoot, strictContent: false);
        Require(!direct.IsComplete && direct.Entries.Count == 0 && direct.IncompleteReasons.Count > 0,
            $"{label} was not rejected before capability filtering.");

        var scan = CaptureCommand("scan", assets, "--format", format, "--no-telemetry");
        Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} emitted a clean {format} scan.");
        AssertNoSuccessfulSurfaceOutput(scan.Output, format, label + " scan");

        var failedBaseline = CaptureCommand("baseline", assets, "--output", baseline, "--format", format, "--no-telemetry");
        Require(failedBaseline.ExitCode == 2 && failedBaseline.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} emitted a clean {format} baseline.");
        AssertNoSuccessfulSurfaceOutput(failedBaseline.Output, format, label + " baseline");
        Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baseline)),
            $"{label} mutated the existing baseline.");

        var check = CaptureCommand("check", assets, "--baseline", baseline, "--format", format, "--no-telemetry");
        Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
            $"{label} emitted a clean {format} check.");
        AssertNoSuccessfulSurfaceOutput(check.Output, format, label + " check");
    }
}

static void AssertNoSuccessfulSurfaceOutput(string output, string format, string label)
{
    var entryLine = output.Contains("Entries:", StringComparison.Ordinal);
    Require((!entryLine || output.Contains("Entries: 0;", StringComparison.Ordinal)) &&
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
        Require(document.RootElement.GetProperty("entries").GetArrayLength() == 0,
            $"{label} emitted surface entries.");
    }
    else if (format.Equals("sarif", StringComparison.Ordinal))
    {
        using var document = JsonDocument.Parse(output);
        var results = document.RootElement.GetProperty("runs")[0].GetProperty("results");
        Require(results.EnumerateArray().All(result => result.GetProperty("ruleId").GetString() == "PS007"),
            $"{label} emitted a non-PS007 SARIF result.");
    }
}

static void WriteAssets(string path, string cache, string packageId, IReadOnlyList<string> files, bool createFiles = false, string projectFileName = "Test.csproj")
{
    var packageKey = packageId + "/1.0.0";
    var filesNode = new JsonArray();
    foreach (var file in files)
    {
        filesNode.Add(file);
    }

    Directory.CreateDirectory(Path.Combine(cache, packageId, "1.0.0"));
    if (createFiles)
    {
        foreach (var file in files.Where(value => !value.Contains("..", StringComparison.Ordinal) && !Path.IsPathRooted(value)))
        {
            var physical = Path.Combine(cache, packageId, "1.0.0", file.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(physical)!);
            File.WriteAllText(physical, "fixture");
        }
    }
    var target = new JsonObject { [packageKey] = new JsonObject() };
    var targets = new JsonObject { ["net8.0"] = target };
    var libraries = new JsonObject
    {
        [packageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = packageKey,
            ["files"] = filesNode
        }
    };
    var packageFolders = new JsonObject { [cache] = new JsonObject() };
    var frameworks = new JsonObject
    {
        ["net8.0"] = new JsonObject
        {
            ["dependencies"] = new JsonObject { [packageId] = PackageDependency() }
        }
    };
    var root = new JsonObject
    {
        ["version"] = 3,
        ["targets"] = targets,
        ["libraries"] = libraries,
        ["packageFolders"] = packageFolders,
        ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, projectFileName) }, ["frameworks"] = frameworks }
    };
    File.WriteAllText(path, root.ToJsonString());
    WriteGeneratedImportEvidence(path, projectFileName);
}

static JsonObject PackageDependency(string version = "[1.0.0, )") => new()
{
    ["version"] = version,
    ["target"] = "Package"
};

static void WriteAnalyzerAssets(
    string path,
    string cache,
    bool direct,
    string analyzerRelativePath,
    string projectFileName = "Test.csproj",
    bool includeAnalyzers = false,
    IReadOnlyList<string>? additionalAnalyzerRelativePaths = null)
{
    const string compilerId = "CompilerPackage";
    const string rootId = "RootPackage";
    const string version = "1.0.0";
    var compilerKey = compilerId + "/" + version;
    var rootKey = rootId + "/" + version;
    var target = new JsonObject();
    var libraries = new JsonObject();
    var compilerFiles = new JsonArray();
    compilerFiles.Add(analyzerRelativePath);
    foreach (var additionalPath in additionalAnalyzerRelativePaths ?? Array.Empty<string>())
    {
        compilerFiles.Add(additionalPath);
    }
    target[compilerKey] = new JsonObject { ["type"] = "package" };
    libraries[compilerKey] = new JsonObject { ["type"] = "package", ["path"] = compilerKey, ["files"] = compilerFiles };
    var directPackageId = compilerId;
    if (!direct)
    {
        target[rootKey] = new JsonObject
        {
            ["type"] = "package",
            ["dependencies"] = new JsonObject { [compilerId] = version }
        };
        libraries[rootKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = rootKey,
            ["files"] = new JsonArray()
        };
        directPackageId = rootId;
    }

    foreach (var analyzerPath in new[] { analyzerRelativePath }.Concat(additionalAnalyzerRelativePaths ?? Array.Empty<string>()))
    {
        var packageRoot = Path.Combine(cache, compilerId, version, Path.GetDirectoryName(analyzerPath)!.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(packageRoot);
        File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, Path.Combine(packageRoot, Path.GetFileName(analyzerPath)), overwrite: true);
    }
    Directory.CreateDirectory(Path.Combine(cache, rootId, version));
    var dependency = PackageDependency();
    dependency["include"] = includeAnalyzers ? "analyzers" : "Runtime, Compile, Build, Native, ContentFiles, BuildTransitive";
    var frameworks = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [directPackageId] = dependency } }
    };
    var root = new JsonObject
    {
        ["version"] = 3,
        ["targets"] = new JsonObject { ["net8.0"] = target },
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, projectFileName) }, ["frameworks"] = frameworks }
    };
    File.WriteAllText(path, root.ToJsonString());
    WriteGeneratedImportEvidence(path, projectFileName);
}

static void WriteContentAssets(
    string path,
    string cache,
    string projectFileName,
    string contentRelativePath,
    string codeLanguage,
    string buildAction,
    string? malformedMetadata = null)
{
    var packageId = "Content.Package";
    WriteAssets(path, cache, packageId, new[] { contentRelativePath }, createFiles: true, projectFileName: projectFileName);
    var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
    var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
    var metadata = new JsonObject
    {
        ["buildAction"] = buildAction,
        ["codeLanguage"] = codeLanguage
    };
    if (malformedMetadata == "buildAction") metadata["buildAction"] = new JsonArray { JsonValue.Create("Compile") };
    if (malformedMetadata == "codeLanguage") metadata["codeLanguage"] = new JsonArray { JsonValue.Create("cs") };
    package["contentFiles"] = new JsonObject { [contentRelativePath] = metadata };
    File.WriteAllText(path, document.ToJsonString());
}

static void WriteLargeAssets(string path, string cache, int packageCount)
{
    var target = new JsonObject();
    var libraries = new JsonObject();
    for (var index = 0; index < packageCount; index++)
    {
        var packageId = "Reachable.Package." + index.ToString("D4", CultureInfo.InvariantCulture);
        var packageKey = packageId + "/1.0.0";
        Directory.CreateDirectory(Path.Combine(cache, packageId, "1.0.0"));
        target[packageKey] = new JsonObject();
        libraries[packageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = packageKey,
            ["files"] = new JsonArray()
        };
    }

    var frameworkDependencies = new JsonObject();
    for (var index = 0; index < packageCount; index++)
    {
        frameworkDependencies[$"Reachable.Package.{index.ToString("D4", CultureInfo.InvariantCulture)}"] = PackageDependency();
    }

    var root = new JsonObject
    {
        ["version"] = 3,
        ["targets"] = new JsonObject { ["net8.0"] = target },
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject
        {
            ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, "Test.csproj") },
            ["frameworks"] = new JsonObject
            {
                ["net8.0"] = new JsonObject
                {
                    ["dependencies"] = frameworkDependencies
                }
            }
        }
    };
    File.WriteAllText(path, root.ToJsonString());
    WriteGeneratedImportEvidence(path);
}

static void WriteGeneratedImportEvidence(string assetsPath, string projectFileName = "Test.csproj")
{
    var directory = Path.GetDirectoryName(assetsPath)!;
    File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.props"), "<Project />");
    File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.targets"), "<Project />");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static bool TryCreateHardLink(string existingPath, string linkPath) =>
    OperatingSystem.IsWindows()
        ? NativeMethods.CreateHardLink(linkPath, existingPath, IntPtr.Zero)
        : NativeMethods.Link(existingPath, linkPath) == 0;

#pragma warning disable CA2101
static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateHardLink(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPWStr)] string existingFileName,
        IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern int Link(
        [MarshalAs(UnmanagedType.LPStr)] string existingPath,
        [MarshalAs(UnmanagedType.LPStr)] string linkPath);
}
#pragma warning restore CA2101

sealed class AncestorLinkCanonicalizer
{
    private readonly string linkRoot;
    private readonly string targetRoot;

    public AncestorLinkCanonicalizer(string linkRoot, string targetRoot)
    {
        this.linkRoot = Path.GetFullPath(linkRoot);
        this.targetRoot = Path.GetFullPath(targetRoot);
    }

    public bool AncestorPathObserved { get; private set; }

    public string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(linkRoot, fullPath);
        if (relative == "." || (!Path.IsPathRooted(relative) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            AncestorPathObserved = true;
            return Path.GetFullPath(Path.Combine(targetRoot, relative));
        }

        return fullPath;
    }
}
