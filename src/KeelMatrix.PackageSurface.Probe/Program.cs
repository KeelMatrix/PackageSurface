using System.Text.Json;
using System.Text.Json.Serialization;

using KeelMatrix.PackageSurface.Probe;

if (args.Length < 1 || args.Length > 2)
{
    Console.Error.WriteLine("Usage: probe <project.assets.json> [project-root]");
    return 2;
}

var assetsFile = Path.GetFullPath(args[0]);
var projectRoot = Path.GetFullPath(args.Length == 2 ? args[1] : Directory.GetParent(Path.GetDirectoryName(assetsFile)!)!.FullName);
var result = ResolvedGraphClassifier.Analyze(assetsFile, projectRoot);
Console.WriteLine(JsonSerializer.Serialize(result, ProbeJson.Options));
return result.IsComplete ? 0 : 2;

static class ProbeJson
{
    public static readonly JsonSerializerOptions Options = new() { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
}
