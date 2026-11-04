using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.PackageXmlAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PackageMsBuildXmlTests
{
    [Fact]
    public void PackageMsbuildXmlMustMatchTheSupportedSchema()
    {
        using var fixture = new PackageMsBuildFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var physicalPath = fixture.PhysicalPath;
        var canonical = fixture.Canonical;
        var baseline = fixture.BaselinePath;
        var valid = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(valid.IsComplete, $"Standard unconsumed MSBuild constructs were rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The standard MSBuild XML baseline could not be created.");




    }


    [Theory]
    [InlineData("element-import-case")]
    [InlineData("element-import-near")]
    [InlineData("element-using-task-case")]
    [InlineData("element-using-task-near")]
    [InlineData("element-exec-case")]
    [InlineData("element-exec-near")]
    [InlineData("element-code-case")]
    [InlineData("element-code-near")]
    public void ConsumedXmlElementSpellingMustBeCanonical(string caseName)
    {

        using var fixture = new PackageMsBuildFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var physicalPath = fixture.PhysicalPath;
        var canonical = fixture.Canonical;
        var baseline = fixture.BaselinePath;
        var valid = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(valid.IsComplete, $"Standard unconsumed MSBuild constructs were rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The standard MSBuild XML baseline could not be created.");


        var matrix = new[]
        {
        ("element-import-case", (Old: "<Import Project=", New: "<import Project=")),
        ("element-import-near", (Old: "<Import Project=", New: "<Impor Project=")),
        ("element-using-task-case", (Old: "<UsingTask TaskName=", New: "<usingtask TaskName=")),
        ("element-using-task-near", (Old: "<UsingTask TaskName=", New: "<UsingTas TaskName=")),
        ("element-exec-case", (Old: "<Exec Command=", New: "<exec Command=")),
        ("element-exec-near", (Old: "<Exec Command=", New: "<Exce Command=")),
        ("element-code-case", (Old: "<Code Type=", New: "<code Type=")),
        ("element-code-near", (Old: "<Code Type=", New: "<Cod Type="))
    };
        var (name, replacement) = matrix.Single(value => value.Item1 == caseName);

        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, name, replacement.Old, replacement.New);

    }

    [Theory]
    [InlineData("Project")]
    [InlineData("Condition")]
    [InlineData("TaskName")]
    [InlineData("TaskFactory")]
    [InlineData("AssemblyFile")]
    [InlineData("Command")]
    [InlineData("Type")]
    [InlineData("Language")]
    [InlineData("BeforeTargets")]
    [InlineData("AfterTargets")]
    [InlineData("DependsOnTargets")]
    [InlineData("Include")]
    [InlineData("Name")]
    [InlineData("ToolsVersion")]
    public void ConsumedXmlAttributeSpellingMustBeCanonical(string caseName)
    {

        using var fixture = new PackageMsBuildFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var physicalPath = fixture.PhysicalPath;
        var canonical = fixture.Canonical;
        var baseline = fixture.BaselinePath;
        var valid = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(valid.IsComplete, $"Standard unconsumed MSBuild constructs were rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Assert.True(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The standard MSBuild XML baseline could not be created.");


        var matrix = new[]
        {
        ("Project", "Project", "project", "Projec"),
        ("Condition", "Condition", "condition", "Conditon"),
        ("TaskName", "TaskName", "taskname", "TaskNme"),
        ("TaskFactory", "TaskFactory", "taskfactory", "TaskFactor"),
        ("AssemblyFile", "AssemblyFile", "assemblyfile", "AssemblyFil"),
        ("Command", "Command", "command", "Comand"),
        ("Type", "Type", "type", "Typ"),
        ("Language", "Language", "language", "Langage"),
        ("BeforeTargets", "BeforeTargets", "beforetargets", "BeforeTarget"),
        ("AfterTargets", "AfterTargets", "aftertargets", "AfterTarget"),
        ("DependsOnTargets", "DependsOnTargets", "dependsonTargets", "DependsOnTarget"),
        ("Include", "Include", "include", "Includ"),
        ("Name", "Name", "name", "Nam"),
        ("ToolsVersion", "ToolsVersion", "toolsversion", "ToolsVersio")
    };
        var (name, canonicalAttribute, alias, near) = matrix.Single(value => value.Item1 == caseName);

        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-case", " " + canonicalAttribute + "=", " " + alias + "=");
        AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-near", " " + canonicalAttribute + "=", " " + near + "=");
        AssertPackageXmlDuplicateAttributeFailure(assets, root, baseline, physicalPath, canonical, name, canonicalAttribute, alias);

    }
    [Fact]
    public void InlineTaskConventionIsDetected()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "inline-task");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var relativePath = "build/inline.targets";
        WriteAssets(assets, cache, "Inline.Package", new[] { relativePath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]!["Inline.Package/1.0.0"]!["build"] = new JsonObject { [relativePath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        var assetPath = Path.Combine(cache, "Inline.Package", "1.0.0", relativePath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/Inline.Package/1.0.0/build/inline.targets\" /></Project>");

        var fixtures = new[]
        {
        ("positive", "<Project xmlns:x=\"urn:keelmatrix:packagesurface:extension\" x:marker=\"ignored\"><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task><Code Type=\"Fragment\" Language=\"cs\">Log.LogMessage(\"ok\");</Code></Task></UsingTask></Project>", true),
        ("custom-factory", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CustomFactory\"><Task><Code>Log.LogMessage(\"text\");</Code></Task></UsingTask></Project>", false),
        ("factory-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\" /></Project>", false),
        ("comment-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task><!-- Code --></Task></UsingTask></Project>", false),
        ("text-only", "<Project><UsingTask TaskName=\"Inline\" TaskFactory=\"CodeTaskFactory\"><Task>Code text</Task></UsingTask></Project>", false)
    };

        foreach (var (name, xml, expected) in fixtures)
        {
            File.WriteAllText(assetPath, xml);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.BuildTargets);
            var observed = entry.ObservedPrimitives?.Contains("InlineTaskFactory") == true;
            Assert.True(result.IsComplete && observed == expected, $"Inline task fixture '{name}' was misclassified.");
        }
    }
}
