using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class RestoreIdentitySetTests
{
    [Fact]
    public void FrameworkSetsMustAgree()
    {
        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The coherent format-4 graph must create its baseline.");



    }


    [Theory]
    [InlineData("project-framework-missing-from-restore")]
    [InlineData("restore-framework-missing-from-project")]
    [InlineData("dependency-group-missing-from-project")]
    [InlineData("dependency-group-extra-framework")]
    [InlineData("rid-framework-missing-from-restore")]
    [InlineData("case-folded-project-framework-set-difference")]
    [InlineData("equivalent-moniker-framework-set-difference")]
    public void EveryIdentitySetMustAgree(string caseName)
    {

        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The coherent format-4 graph must create its baseline.");


        var matrix = new (string Name, Action<JsonObject> Mutate)[]
        {
        ("project-framework-missing-from-restore", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["net9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("restore-framework-missing-from-project", document =>
        {
            document["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = "net9.0"
            };
        }),
        ("dependency-group-missing-from-project", document =>
            document["projectFileDependencyGroups"]!.AsObject().Remove("net8.0")),
        ("dependency-group-extra-framework", document =>
            document["projectFileDependencyGroups"]!.AsObject()["net9.0"] = new JsonArray()),
        ("rid-framework-missing-from-restore", document =>
            document["targets"]!.AsObject()["net9.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone()),
        ("case-folded-project-framework-set-difference", document =>
        {
            var projectFrameworks = document["project"]!["frameworks"]!.AsObject();
            projectFrameworks["NET9.0"] = new JsonObject
            {
                ["framework"] = "NET9.0",
                ["targetAlias"] = "NET9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()["NET9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()["NET9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        }),
        ("equivalent-moniker-framework-set-difference", document =>
        {
            document["project"]!["frameworks"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonObject
            {
                ["framework"] = "net9.0",
                ["targetAlias"] = ".NETCoreApp,Version=v9.0",
                ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
            };
            document["targets"]!.AsObject()[".NETCoreApp,Version=v9.0"] = document["targets"]!["net8.0"]!.DeepClone();
            document["projectFileDependencyGroups"]!.AsObject()[".NETCoreApp,Version=v9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        })
        };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "restore-set-" + name + ".assets.json");
        var document = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        mutate(document);
        File.WriteAllText(path, document.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(path, scratch, baselinePath, name);
        }
        finally
        {
            File.Delete(path);
        }

    }
}
