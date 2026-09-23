using System.Diagnostics;
using System.Globalization;
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

var inactiveBaseline = new[] { BaselineEntry.From(Entry(CapabilityKind.BuildProps, "build/net9.0/changed.props", "old", active: false)) };
var inactiveCurrent = new[] { Entry(CapabilityKind.BuildProps, "build/net9.0/changed.props", "new", active: false) };
var inactiveDiagnostics = DiffEngine.Compare(inactiveBaseline, inactiveCurrent, strictContent: true);
if (inactiveDiagnostics.Count(diagnostic => diagnostic.Id == "PS005") != 1)
{
    Console.Error.WriteLine("Strict content did not report a changed present-but-inactive asset.");
    return 1;
}

if (DiffEngine.Compare(inactiveBaseline, inactiveCurrent, strictContent: false).Any(diagnostic => diagnostic.Id == "PS005"))
{
    Console.Error.WriteLine("Non-strict content comparison reported a content fingerprint change.");
    return 1;
}

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

        File.WriteAllText(Path.Combine(obj, "project.nuget.g.props"), "<!DOCTYPE Project [<!ENTITY external SYSTEM 'file:///outside'>]><Project><Import Project='&external;' /></Project>");
        var dtdResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(!dtdResult.IsComplete, "DTD input was not rejected as incomplete.");

        File.WriteAllText(Path.Combine(obj, "project.nuget.g.props"), "<Project />");
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
        File.WriteAllText(Path.Combine(obj, "project.nuget.g.props"), deepXml.ToString());
        var deepXmlResult = ResolvedGraphClassifier.Analyze(assets, scratch, strictContent: false);
        Require(!deepXmlResult.IsComplete && deepXmlResult.IncompleteReasons.Any(reason => reason.Contains("XML depth", StringComparison.OrdinalIgnoreCase)),
            "Deep XML nesting was not bounded during parsing.");
        File.WriteAllText(Path.Combine(obj, "project.nuget.g.props"), "<Project />");
        RunAnalyzerExclusionRegression(scratch, cache, obj);
        RunMalformedAssetsShapeRegression(assets, scratch);
        RunDiagnosticPathLeakRegression(scratch);

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
    }
    finally
    {
        if (Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
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
    WriteAnalyzerAssets(directAssets, cache, direct: true, analyzerRelativePath);
    var directResult = ResolvedGraphClassifier.Analyze(directAssets, scratch, strictContent: false);
    Require(directResult.IsComplete && directResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
        "Direct ExcludeAssets=analyzers was not honored.");

    var transitiveAssets = Path.Combine(obj, "analyzer-excluded-transitive.assets.json");
    WriteAnalyzerAssets(transitiveAssets, cache, direct: false, analyzerRelativePath);
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

static void WriteAssets(string path, string cache, string packageId, IReadOnlyList<string> files)
{
    var packageKey = packageId + "/1.0.0";
    var filesNode = new JsonArray();
    foreach (var file in files)
    {
        filesNode.Add(file);
    }

    Directory.CreateDirectory(Path.Combine(cache, packageId, "1.0.0"));
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
        ["project"] = new JsonObject { ["frameworks"] = frameworks }
    };
    File.WriteAllText(path, root.ToJsonString());
}

static void WriteAnalyzerAssets(string path, string cache, bool direct, string analyzerRelativePath)
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

    var packageRoot = Path.Combine(cache, compilerId, version, "analyzers", "dotnet", "cs");
    Directory.CreateDirectory(packageRoot);
    File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, Path.Combine(packageRoot, "valid.dll"), overwrite: true);
    Directory.CreateDirectory(Path.Combine(cache, rootId, version));
    var dependency = new JsonObject { ["include"] = "Runtime, Compile, Build, Native, ContentFiles, BuildTransitive" };
    var frameworks = new JsonObject
    {
        ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [directPackageId] = dependency } }
    };
    var root = new JsonObject
    {
        ["targets"] = new JsonObject { ["net8.0"] = target },
        ["libraries"] = libraries,
        ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
        ["project"] = new JsonObject { ["frameworks"] = frameworks }
    };
    File.WriteAllText(path, root.ToJsonString());
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
        ["project"] = new JsonObject { ["frameworks"] = new JsonObject { ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject() } } }
    };
    File.WriteAllText(path, root.ToJsonString());
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
