using System.Text.Json;
using KeelMatrix.PackageSurface.Probe;

if (args.Length != 2)
{
    Console.Error.WriteLine("Usage: probe-tests <project.assets.json>");
    return 2;
}

var assets = Path.GetFullPath(args[0]);
var projectRoot = Directory.GetParent(Path.GetDirectoryName(assets)!)!.FullName;
var result = ResolvedGraphClassifier.Analyze(assets, projectRoot);
if (!result.IsComplete)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, result.IncompleteReasons));
    return 1;
}

var required = args[1].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(value => Enum.Parse<CapabilityKind>(value, ignoreCase: true))
    .ToArray();
var present = result.Entries.Select(entry => entry.Capability).Distinct().ToHashSet();
if (required.Any(kind => !present.Contains(kind)))
{
    Console.Error.WriteLine("Missing capability fixture: " + string.Join(", ", required.Where(kind => !present.Contains(kind))));
    return 1;
}

using (var document = JsonDocument.Parse(File.ReadAllText(assets)))
{
    var graphTargetFrameworks = document.RootElement.GetProperty("targets")
        .EnumerateObject()
        .Select(target => target.Name.Split('/', 2)[0])
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    var classifiedTargetFrameworks = result.Entries
        .Where(entry => entry.Context == SurfaceContextKind.Target)
        .Select(entry => entry.TargetFramework)
        .OfType<string>()
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);
    if (graphTargetFrameworks.Any(targetFramework => !classifiedTargetFrameworks.Contains(targetFramework)))
    {
        Console.Error.WriteLine("A target framework in the graph has no target-context classifier entry.");
        return 1;
    }

    var outerTargetEntries = result.Entries.Where(entry => entry.Capability == CapabilityKind.BuildMultiTargeting).ToArray();
    if (outerTargetEntries.Any(entry => entry.Context != SurfaceContextKind.Project || entry.TargetFramework is not null || entry.RuntimeIdentifier is not null) ||
        outerTargetEntries.GroupBy(entry => string.Join("|", entry.PackageId, entry.Version, entry.PackageRelativePath), StringComparer.OrdinalIgnoreCase).Count() != outerTargetEntries.Length)
    {
        Console.Error.WriteLine("Outer-target assets were associated with a target framework or duplicated.");
        return 1;
    }

    if (result.Entries.Any(entry => entry.Active && entry.Context == SurfaceContextKind.Target && entry.Capability == CapabilityKind.BuildMultiTargeting))
    {
        Console.Error.WriteLine("An outer-target asset was marked active for a target framework.");
        return 1;
    }
}

if (result.Entries.Any(entry => entry.Capability == CapabilityKind.ToolOrScriptPresent && entry.Active) ||
    (assets.Contains("RidTarget", StringComparison.OrdinalIgnoreCase) &&
     required.Contains(CapabilityKind.NativeRuntime) &&
     (!result.Entries.Any(entry => entry.Capability == CapabilityKind.NativeRuntime && entry.Active) ||
      !result.Entries.Any(entry => entry.Capability == CapabilityKind.NativeRuntime && !entry.Active))))
{
    Console.Error.WriteLine("Present/active fixture assertions failed.");
    return 1;
}

var malformed = Path.Combine(Path.GetTempPath(), "packagesurface-malformed-assets.json");
try
{
    File.WriteAllText(malformed, "{");
    var malformedResult = ResolvedGraphClassifier.Analyze(malformed, projectRoot);
    if (malformedResult.IsComplete)
    {
        Console.Error.WriteLine("Malformed assets were not treated as incomplete.");
        return 1;
    }
}
finally
{
    if (File.Exists(malformed))
    {
        File.Delete(malformed);
    }
}

Console.WriteLine(JsonSerializer.Serialize(new { entries = result.Entries.Count, complete = result.IsComplete }));
return 0;
