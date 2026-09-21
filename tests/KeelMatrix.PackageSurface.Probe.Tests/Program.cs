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

if (result.Entries.Any(entry => entry.Capability == CapabilityKind.ToolOrScriptPresent && entry.Active) ||
    (required.Contains(CapabilityKind.NativeRuntime) &&
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
