using System.Diagnostics;
using System.Globalization;
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

var ps007 = new[] { Diagnostic.Create("PS007", "restore evidence is incomplete") };
var incompleteReasons = new[] { "restore evidence is incomplete" };
var incomplete = ReportDocument.Create(CommandKind.Check, new SurfaceSnapshot(Array.Empty<SurfaceEntry>(), incompleteReasons, false, 0), ps007);
if (incomplete.Diagnostics.Count != 1 || incomplete.Diagnostics[0].Id != "PS007")
{
    Console.Error.WriteLine("PS007 report contract failed.");
    return 1;
}

RunClassifierHardeningTests();

Console.WriteLine($"PASS: diagnostics {string.Join(", ", expected)}");
return 0;

static SurfaceEntry Entry(CapabilityKind capability, string path, string hash) => new(
    "net8.0",
    capability == CapabilityKind.NativeRuntime ? "win-x64" : null,
    SurfaceContextKind.Target,
    "Example.Package",
    "1.0.0",
    "direct",
    capability,
    path,
    true,
    true,
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
