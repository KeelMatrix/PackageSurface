using System.Text;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class GeneratedImportFixture
{
    internal static void AddBuildGroups(string assets, string packageId, IReadOnlyList<string> files)
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

    internal static void WriteRepeatedGeneratedImports(string obj, int propsCount, int targetsCount, IReadOnlyList<string> files)
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

    internal static void WriteGeneratedImportEvidence(string assetsPath, string projectFileName = "Test.csproj")
    {
        var directory = Path.GetDirectoryName(assetsPath)!;
        File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.props"), "<Project />");
        File.WriteAllText(Path.Combine(directory, projectFileName + ".nuget.g.targets"), "<Project />");
    }
}
