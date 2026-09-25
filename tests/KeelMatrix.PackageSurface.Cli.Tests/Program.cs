using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;

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

if (!Options.HelpText.Contains("passes no analyzed dependency identity or content", StringComparison.Ordinal) ||
    !Options.HelpText.Contains("https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md", StringComparison.Ordinal))
{
    Console.Error.WriteLine("CLI telemetry privacy contract is missing from --help.");
    return 1;
}

RunClassifierHardeningTests();
RunTelemetryStateMachineTests();
RunParserMessageRegression();

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
        RunDiagnosticPathLeakRegression(scratch);
        RunApplicabilityHardeningRegressions(scratch);

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

        var oversized = Path.Combine(obj, "oversized.assets.json");
        File.WriteAllText(oversized, new string('x', 16 * 1024 * 1024 + 1));
        var oversizedResult = ResolvedGraphClassifier.Analyze(oversized, scratch, strictContent: false);
        Require(!oversizedResult.IsComplete, "Oversized metadata was not rejected as incomplete.");

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
        ("positive", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task><Code Type=\"Fragment\" Language=\"cs\">Log.LogMessage(\"ok\");</Code></Task></UsingTask></Project>", true),
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

static void RunLanguageConventionRegression(string scratch)
{
    var analyzerCases = new[]
    {
        (Language: "cs", Project: "Test.csproj", Path: "analyzers/dotnet/cs/valid.dll", Active: true),
        (Language: "vb", Project: "Test.vbproj", Path: "analyzers/dotnet/vb/valid.dll", Active: true),
        (Language: "fs", Project: "Test.fsproj", Path: "analyzers/dotnet/fs/valid.dll", Active: true),
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
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.CompilerExtension);
        Require(result.IsComplete && entry.Active == testCase.Active, $"Analyzer convention case '{testCase.Language}' was misclassified. Complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
    }

    var contentCases = new[]
    {
        (Language: "cs", Project: "Test.csproj", Path: "contentFiles/cs/any/active.cs", CodeLanguage: "cs", BuildAction: "Compile", Active: true),
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
        ["targets"] = targets,
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject
        {
            ["restore"] = new JsonObject { ["projectPath"] = projectFile },
            ["frameworks"] = new JsonObject
            {
                ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = new JsonObject() } },
                ["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject() }
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
    catch (UnauthorizedAccessException) { }
    catch (IOException) { }
    catch (PlatformNotSupportedException) { }
    finally
    {
        if (File.Exists(outside)) File.Delete(outside);
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
    var malformedShapes = new (string Name, Action<JsonObject> Mutate)[]
    {
        ("packageFolders-array", root => root["packageFolders"] = new JsonArray()),
        ("targets-array", root => root["targets"] = new JsonArray()),
        ("target-array", root => root["targets"]!["net8.0"] = new JsonArray()),
        ("libraries-array", root => root["libraries"] = new JsonArray()),
        ("frameworks-array", root => root["project"]!["frameworks"] = new JsonArray()),
        ("package-path-number", root => root["libraries"]!["XmlPackage/1.0.0"]!["path"] = 7)
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
            var physical = Path.Combine(cache, packageId, "1.0.0", file.Replace('/', Path.DirectorySeparatorChar));
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
    var frameworks = new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject() } };
    var root = new JsonObject
    {
        ["targets"] = targets,
        ["libraries"] = libraries,
        ["packageFolders"] = packageFolders,
        ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, projectFileName) }, ["frameworks"] = frameworks }
    };
    File.WriteAllText(path, root.ToJsonString());
    WriteGeneratedImportEvidence(path, projectFileName);
}

static void WriteAnalyzerAssets(string path, string cache, bool direct, string analyzerRelativePath, string projectFileName = "Test.csproj", bool includeAnalyzers = false)
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

    var packageRoot = Path.Combine(cache, compilerId, version, Path.GetDirectoryName(analyzerRelativePath)!.Replace('/', Path.DirectorySeparatorChar));
    Directory.CreateDirectory(packageRoot);
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, Path.Combine(packageRoot, Path.GetFileName(analyzerRelativePath)), overwrite: true);
    Directory.CreateDirectory(Path.Combine(cache, rootId, version));
    var dependency = new JsonObject { ["include"] = includeAnalyzers ? "analyzers" : "Runtime, Compile, Build, Native, ContentFiles, BuildTransitive" };
    var frameworks = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [directPackageId] = dependency } }
    };
    var root = new JsonObject
    {
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

    var root = new JsonObject
    {
        ["targets"] = new JsonObject { ["net8.0"] = target },
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject { ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Directory.GetParent(Path.GetDirectoryName(path)!)!.FullName, "Test.csproj") }, ["frameworks"] = new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject() } } }
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
