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
