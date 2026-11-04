using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.PackageFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class GeneratedImportProvenanceTests
{
    [Fact]
    public void ProjectExtensionsAreResolved()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var generatedRoot = Path.Combine(scratch, "project-extensions-generated");
        var generatedCache = Path.Combine(generatedRoot, "cache");
        var generatedObj = Path.Combine(generatedRoot, "obj");
        Directory.CreateDirectory(generatedObj);
        var generatedAssets = Path.Combine(generatedObj, "project.assets.json");
        WriteAssets(generatedAssets, generatedCache, "Reachable.Package", Array.Empty<string>());
        var generatedDocument = JsonNode.Parse(File.ReadAllText(generatedAssets))!.AsObject();
        AddUnreachableImportPackage(generatedDocument, generatedCache, "Ignored.Package", "build/ignored.targets");
        File.WriteAllText(generatedAssets, generatedDocument.ToJsonString());

        var ignoredGeneratedImport = "<Project><ProjectExtensions><Import Project=\"$(NuGetPackageRoot)/Ignored.Package/1.0.0/build/ignored.targets\" /></ProjectExtensions></Project>";
        File.WriteAllText(Path.Combine(generatedObj, "Test.csproj.nuget.g.props"), ignoredGeneratedImport);
        File.WriteAllText(Path.Combine(generatedObj, "Test.csproj.nuget.g.targets"), ignoredGeneratedImport);
        var generatedResult = ResolvedGraphClassifier.Analyze(generatedAssets, generatedRoot, strictContent: false);
        Assert.True(generatedResult.IsComplete && generatedResult.Entries.Count == 0,
            $"Ignored generated ProjectExtensions imports changed graph reachability: complete={generatedResult.IsComplete}; reasons={string.Join(" | ", generatedResult.IncompleteReasons)}");

        var validNamespaceRoot = Path.Combine(scratch, "valid-namespaced-generated-import");
        var validNamespaceCache = Path.Combine(validNamespaceRoot, "cache");
        var validNamespaceObj = Path.Combine(validNamespaceRoot, "obj");
        Directory.CreateDirectory(validNamespaceObj);
        var validNamespaceAssets = Path.Combine(validNamespaceObj, "project.assets.json");
        var validNamespacePackage = "Valid.Namespace.Package";
        var validNamespacePath = "build/valid.targets";
        WriteAssets(validNamespaceAssets, validNamespaceCache, validNamespacePackage, new[] { validNamespacePath }, createFiles: true);
        var validNamespaceDocument = JsonNode.Parse(File.ReadAllText(validNamespaceAssets))!.AsObject();
        validNamespaceDocument["targets"]!["net8.0"]![validNamespacePackage + "/1.0.0"]!["build"] = new JsonObject
        {
            [validNamespacePath] = new JsonObject()
        };
        File.WriteAllText(validNamespaceAssets, validNamespaceDocument.ToJsonString());
        File.WriteAllText(Path.Combine(validNamespaceCache, validNamespacePackage, "1.0.0", validNamespacePath.Replace('/', Path.DirectorySeparatorChar)), "<Project />");
        File.WriteAllText(Path.Combine(validNamespaceObj, "Test.csproj.nuget.g.targets"),
            $"<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\"><Import Project=\"$(NuGetPackageRoot)/{validNamespacePackage}/1.0.0/{validNamespacePath}\" /></Project>");
        var validNamespaceResult = ResolvedGraphClassifier.Analyze(validNamespaceAssets, validNamespaceRoot, strictContent: false);
        Assert.True(validNamespaceResult.IsComplete && validNamespaceResult.Entries.Any(entry => entry.PackageId == validNamespacePackage && entry.Active),
            $"A valid root import in the standard MSBuild namespace was rejected: complete={validNamespaceResult.IsComplete}; reasons={string.Join(" | ", validNamespaceResult.IncompleteReasons)}");

        var packageRoot = Path.Combine(scratch, "project-extensions-package");
        var packageCache = Path.Combine(packageRoot, "cache");
        var packageObj = Path.Combine(packageRoot, "obj");
        Directory.CreateDirectory(packageObj);
        var packageAssets = Path.Combine(packageObj, "project.assets.json");
        var packageId = "Extensions.Package";
        var outerPath = "build/outer.targets";
        var helperPath = "helpers/hidden.targets";
        WriteAssets(packageAssets, packageCache, packageId, new[] { outerPath, helperPath }, createFiles: true);
        var packageDocument = JsonNode.Parse(File.ReadAllText(packageAssets))!.AsObject();
        packageDocument["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [outerPath] = new JsonObject() };
        File.WriteAllText(packageAssets, packageDocument.ToJsonString());

        var physicalOuter = Path.Combine(packageCache, packageId, "1.0.0", outerPath.Replace('/', Path.DirectorySeparatorChar));
        File.WriteAllText(physicalOuter, """
<Project>
  <ProjectExtensions>
    <Import Project="helpers/hidden.targets" />
    <Exec Command="must-not-count" />
    <UsingTask TaskName="Ignored" TaskFactory="CodeTaskFactory"><Task><Code>must-not-count</Code></Task></UsingTask>
  </ProjectExtensions>
</Project>
""");
        File.WriteAllText(Path.Combine(packageCache, packageId, "1.0.0", helperPath.Replace('/', Path.DirectorySeparatorChar)), "<Project />");
        File.WriteAllText(Path.Combine(packageObj, "Test.csproj.nuget.g.targets"),
            $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{outerPath}\" /></Project>");

        var packageResult = ResolvedGraphClassifier.Analyze(packageAssets, packageRoot, strictContent: false);
        var outer = packageResult.Entries.SingleOrDefault(entry => entry.PackageId == packageId && entry.PackageRelativePath == outerPath);
        Assert.True(packageResult.IsComplete && outer is { Active: true } && (outer.ObservedPrimitives?.Count ?? 0) == 0 &&
                !packageResult.Entries.Any(entry => entry.PackageRelativePath == helperPath && entry.Active),
            $"Package ProjectExtensions content activated, recursed, or counted MSBuild primitives: complete={packageResult.IsComplete}; outer={outer?.Active}; primitives={string.Join(',', outer?.ObservedPrimitives ?? Array.Empty<string>())}; reasons={string.Join(" | ", packageResult.IncompleteReasons)}");
    }

    [Fact]
    public void GeneratedImportsMustBeReachable()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var directRoot = Path.Combine(scratch, "orphan-generated-import");
        var directCache = Path.Combine(directRoot, "cache");
        var directObj = Path.Combine(directRoot, "obj");
        Directory.CreateDirectory(directObj);
        var directAssets = Path.Combine(directObj, "project.assets.json");
        WriteAssets(directAssets, directCache, "Reachable.Package", Array.Empty<string>());
        var directBaseline = Path.Combine(directRoot, "baseline.json");
        Assert.True(CaptureCommand("baseline", directAssets, "--output", directBaseline, "--no-telemetry").ExitCode == 0,
            "The coherent direct-import fixture could not create its baseline.");
        var directDocument = JsonNode.Parse(File.ReadAllText(directAssets))!.AsObject();
        AddUnreachableImportPackage(directDocument, directCache, "Unused.Package", "build/unused.targets");
        File.WriteAllText(directAssets, directDocument.ToJsonString());
        File.WriteAllText(Path.Combine(directObj, "Test.csproj.nuget.g.props"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/unused.package/1.0.0/build/unused.targets\" /></Project>");
        AssertGeneratedImportFailure("direct orphan import", directAssets, directBaseline);

        var coherentRoot = Path.Combine(scratch, "nested-orphan-generated-import");
        var coherentCache = Path.Combine(coherentRoot, "cache");
        var coherentObj = Path.Combine(coherentRoot, "obj");
        Directory.CreateDirectory(coherentObj);
        var coherentAssets = Path.Combine(coherentObj, "project.assets.json");
        var coherentFiles = new[] { "build/outer.targets" };
        WriteAssets(coherentAssets, coherentCache, "Reachable.Package", coherentFiles, createFiles: true);
        File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"), "<Project />");
        var coherentBaseline = Path.Combine(coherentRoot, "baseline.json");
        Assert.True(CaptureCommand("baseline", coherentAssets, "--output", coherentBaseline, "--no-telemetry").ExitCode == 0,
            "The coherent nested-import fixture could not create its baseline.");
        var coherentDocument = JsonNode.Parse(File.ReadAllText(coherentAssets))!.AsObject();
        coherentDocument["targets"]!["net8.0"]!["Reachable.Package/1.0.0"]!["build"] = new JsonObject { ["build/outer.targets"] = new JsonObject() };
        File.WriteAllText(coherentAssets, coherentDocument.ToJsonString());
        File.WriteAllText(Path.Combine(coherentObj, "Test.csproj.nuget.g.props"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Reachable.Package/1.0.0/build/outer.targets\" /></Project>");
        File.WriteAllText(Path.Combine(coherentObj, "Test.csproj.nuget.g.targets"), "<Project />");
        File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"), "<Project />");

        coherentDocument = JsonNode.Parse(File.ReadAllText(coherentAssets))!.AsObject();
        AddUnreachableImportPackage(coherentDocument, coherentCache, "Unused.Package", "build/unused.targets");
        File.WriteAllText(coherentAssets, coherentDocument.ToJsonString());
        File.WriteAllText(Path.Combine(coherentCache, "Reachable.Package", "1.0.0", "build", "outer.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Unused.Package/1.0.0/build/unused.targets\" /></Project>");
        AssertGeneratedImportFailure("nested orphan import", coherentAssets, coherentBaseline);

        var conditionalRoot = Path.Combine(scratch, "conditional-orphan-generated-import");
        var conditionalCache = Path.Combine(conditionalRoot, "cache");
        var conditionalObj = Path.Combine(conditionalRoot, "obj");
        Directory.CreateDirectory(conditionalObj);
        var conditionalAssets = Path.Combine(conditionalObj, "project.assets.json");
        WriteAssets(conditionalAssets, conditionalCache, "Reachable.Package", Array.Empty<string>());
        var conditionalBaseline = Path.Combine(conditionalRoot, "baseline.json");
        Assert.True(CaptureCommand("baseline", conditionalAssets, "--output", conditionalBaseline, "--no-telemetry").ExitCode == 0,
            "The coherent conditional-import fixture could not create its baseline.");
        var conditionalDocument = JsonNode.Parse(File.ReadAllText(conditionalAssets))!.AsObject();
        conditionalDocument["project"]!["frameworks"]!["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject() };
        AddUnreachableImportPackage(conditionalDocument, conditionalCache, "Only.Net9", "build/net9.targets");
        conditionalDocument["targets"]!["net9.0"] = new JsonObject
        {
            ["Only.Net9/1.0.0"] = new JsonObject { ["build"] = new JsonObject { ["build/net9.targets"] = new JsonObject() } }
        };
        File.WriteAllText(conditionalAssets, conditionalDocument.ToJsonString());
        File.WriteAllText(Path.Combine(conditionalCache, "Only.Net9", "1.0.0", "build", "net9.targets"), "<Project />");
        var conditionalImport = Path.Combine(conditionalObj, "Test.csproj.nuget.g.props");
        File.WriteAllText(Path.Combine(conditionalObj, "Test.csproj.nuget.g.targets"), "<Project />");
        File.WriteAllText(conditionalImport,
            "<Project><Import Project=\"$(NuGetPackageRoot)/Only.Net9/1.0.0/build/net9.targets\" Condition=\"'$(TargetFramework)' == 'net8.0'\" /></Project>");
        AssertGeneratedImportFailure("conditional target graph", conditionalAssets, conditionalBaseline);
    }
}
