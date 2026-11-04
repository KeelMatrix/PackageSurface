using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class FrameworkIdentityTests
{
    [Fact]
    public void FrameworkMonikersAreCanonicalized()
    {
        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;



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
            Assert.True(result.IsComplete, "Format 4 project/restore framework aliases were not reconciled.");
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
            Assert.True(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("netstandard2.0", StringComparison.OrdinalIgnoreCase)),
                "A declared framework with no equivalent target graph did not fail closed.");
        }
        finally
        {
            File.Delete(negativePath);
        }
    }


    [Theory]
    [InlineData("netstandard2.0")]
    [InlineData("NET8.0")]
    [InlineData("net472")]
    [InlineData("NET472")]
    public void FrameworkAliasesMatchResolvedTargets(string caseName)
    {

        using var workspace = new CliScenarioWorkspace();
        var assets = workspace.AssetsPath;
        var scratch = workspace.Root;


        var matrix = new[]
        {
        (Declared: "netstandard2.0", Target: ".NETStandard,Version=v2.0"),
        (Declared: "NET8.0", Target: ".NETCoreApp,Version=v8.0"),
        (Declared: "net472", Target: ".NETFramework,Version=v4.7.2"),
        (Declared: "NET472", Target: "net4.7.2/win-x64")
    };
        var (declared, target) = matrix.Single(value => value.Item1 == caseName);

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
            Assert.True(result.IsComplete, $"Equivalent framework monikers were not reconciled: {declared} vs {target}: {string.Join("; ", result.IncompleteReasons)}");
        }
        finally
        {
            File.Delete(path);
        }

    }
}
