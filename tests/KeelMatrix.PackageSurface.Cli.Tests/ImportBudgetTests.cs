using System.Globalization;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.GeneratedImportFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.PackageFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class ImportBudgetTests
{
    [Fact]
    public void ImportGraphWorkIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "import-edge-budget");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var files = new[] { "build/Edge.props", "build/Edge.targets" };

        var withinPath = Path.Combine(obj, "within.assets.json");
        WriteAssets(withinPath, cache, "Edge.Package", files, createFiles: true);
        AddBuildGroups(withinPath, "Edge.Package", files);
        WritePackageBuildFiles(cache, files);
        WriteRepeatedGeneratedImports(obj, 4_096, 4_096, files);
        var within = ResolvedGraphClassifier.Analyze(withinPath, root, strictContent: false);
        Assert.True(within.IsComplete && within.Entries.Count == 2, $"The 8,192 cumulative generated-import edge boundary failed: {string.Join(" | ", within.IncompleteReasons)}");

        var overRoot = Path.Combine(scratch, "import-edge-budget-over");
        var overCache = Path.Combine(overRoot, "cache");
        var overObj = Path.Combine(overRoot, "obj");
        Directory.CreateDirectory(overObj);
        var overPath = Path.Combine(overObj, "over.assets.json");
        WriteAssets(overPath, overCache, "Edge.Package", files, createFiles: true);
        AddBuildGroups(overPath, "Edge.Package", files);
        WritePackageBuildFiles(overCache, files);
        WriteRepeatedGeneratedImports(overObj, 4_096, 4_097, files);
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var priorOutput = Console.Out;
        try
        {
            Console.SetOut(output);
            var exitCode = CommandLine.Run(new[] { "scan", overPath, "--format", "json", "--no-telemetry" });
            Assert.True(exitCode == 2, $"An 8,193 cumulative generated-import edge graph returned exit {exitCode}.");
        }
        finally
        {
            Console.SetOut(priorOutput);
        }

        Assert.True(!output.ToString().Contains("Edge.props", StringComparison.Ordinal) &&
            !output.ToString().Contains("Edge.targets", StringComparison.Ordinal),
            "The over-budget import graph emitted partial capability success output.");
    }
}
