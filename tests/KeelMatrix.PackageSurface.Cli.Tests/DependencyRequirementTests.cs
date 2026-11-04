using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class DependencyRequirementTests
{
    [Fact]
    public void DependencyRequirementsMustResolve()
    {
        using var fixture = new RestoreIdentityFixture();
        var assets = fixture.AssetsPath;
        var scratch = fixture.Root;
        var format4Path = fixture.Format4Path;
        var baselinePath = fixture.BaselinePath;


        Assert.True(CaptureCommand("baseline", format4Path, "--output", baselinePath, "--no-telemetry").ExitCode == 0,
            "The coherent format-4 graph must create its baseline.");



        var multiTargetPath = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-multi-rid.assets.json");
        var multiTarget = JsonNode.Parse(File.ReadAllText(format4Path))!.AsObject();
        var projectFrameworks = multiTarget["project"]!["frameworks"]!.AsObject();
        projectFrameworks["net9.0"] = new JsonObject
        {
            ["framework"] = "net9.0",
            ["targetAlias"] = "net9.0",
            ["dependencies"] = new JsonObject { ["XmlPackage"] = PackageDependency() }
        };
        multiTarget["project"]!["restore"]!["frameworks"]!.AsObject()["net9.0"] = new JsonObject
        {
            ["framework"] = "net9.0",
            ["targetAlias"] = "net9.0"
        };
        var targets = multiTarget["targets"]!.AsObject();
        targets["net8.0/win-x64"] = targets["net8.0"]!.DeepClone();
        targets["net9.0"] = targets["net8.0"]!.DeepClone();
        var groups = multiTarget["projectFileDependencyGroups"]!.AsObject();
        groups["net9.0"] = new JsonArray("XmlPackage >= 1.0.0");
        targets["net8.0/win-x64"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" };
        File.WriteAllText(multiTargetPath, multiTarget.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(multiTargetPath, scratch, baselinePath, "multi-target-rid dependency requirement");
        }
        finally
        {
            File.Delete(multiTargetPath);
        }

    }


    [Theory]
    [InlineData("project-dependency-invalid-range")]
    [InlineData("project-dependency-empty-range")]
    [InlineData("project-dependency-unsupported-version")]
    [InlineData("project-dependency-version-mismatch")]
    [InlineData("target-dependency-invalid-range")]
    [InlineData("target-dependency-invalid-operator")]
    [InlineData("target-dependency-version-mismatch")]
    [InlineData("format4-package-invalid-operator")]
    [InlineData("format4-package-invalid-version")]
    [InlineData("format4-package-version-mismatch")]
    [InlineData("format4-package-empty-value")]
    [InlineData("format4-absolute-project-path")]
    [InlineData("format4-unsupported-project-extension")]
    [InlineData("format4-ambiguous-project-path")]
    public void EachDependencyRequirementMustResolve(string caseName)
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
        ("project-dependency-invalid-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "not-a-version-range"),
        ("project-dependency-empty-range", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "  "),
        ("project-dependency-unsupported-version", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "6.*"),
        ("project-dependency-version-mismatch", document =>
            document["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["XmlPackage"]!["version"] = "[2.0.0, )"),
        ("target-dependency-invalid-range", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "not-a-version-range" }),
        ("target-dependency-invalid-operator", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = ">= 1.0.0" }),
        ("target-dependency-version-mismatch", document =>
            document["targets"]!["net8.0"]!["XmlPackage/1.0.0"]!["dependencies"] = new JsonObject { ["XmlPackage"] = "[2.0.0, )" }),
        ("format4-package-invalid-operator", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage => 1.0.0")),
        ("format4-package-invalid-version", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= not-a-version")),
        ("format4-package-version-mismatch", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("XmlPackage >= 2.0.0")),
        ("format4-package-empty-value", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("  ")),
        ("format4-absolute-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("C:/Referenced/Referenced.csproj")),
        ("format4-unsupported-project-extension", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.txt")),
        ("format4-ambiguous-project-path", document =>
            document["projectFileDependencyGroups"]!["net8.0"] = new JsonArray("../Referenced/Referenced.csproj extra"))
        };
        var (name, mutate) = matrix.Single(value => value.Item1 == caseName);

        var path = Path.Combine(Path.GetDirectoryName(format4Path)!, "dependency-requirement-" + name + ".assets.json");
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
