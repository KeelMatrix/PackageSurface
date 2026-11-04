using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class RestoreIdentityFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal RestoreIdentityFixture()
    {

        workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;

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


        Format4Path = format4Path;
        BaselinePath = baselinePath;
    }
    internal string AssetsPath => workspace.AssetsPath;
    internal string Root => workspace.Root;
    internal string Format4Path { get; }
    internal string BaselinePath { get; }
    public void Dispose() => workspace.Dispose();

}
