using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CapabilityRestoreFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class HostileInputTests
{
    private static readonly string[] InvalidAnalyzerPaths = { "analyzers/dotnet/cs/invalid.dll" };

    private static readonly string[] TraversalAssetPaths = { "../escape.props" };

    [Fact]
    public void GeneratedPropsRejectDocumentTypeDeclarations()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;
        var assets = workspace.AssetsPath;

        File.WriteAllText(Path.Combine(root, "obj", "Test.csproj.nuget.g.props"), "<!DOCTYPE Project [<!ENTITY external SYSTEM 'file:///outside'>]><Project><Import Project='&external;' /></Project>");
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete, "DTD input was not rejected as incomplete.");
    }

    [Fact]
    public void GeneratedPropsXmlDepthIsBounded()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;
        var assets = workspace.AssetsPath;

        var deepXml = new StringBuilder("<Project>");
        for (var depth = 0; depth < 80; depth++)
        {
            deepXml.Append("<ImportGroup>");
        }
        deepXml.Append("<Import Project=\"unused.props\" />");
        for (var depth = 0; depth < 80; depth++)
        {
            deepXml.Append("</ImportGroup>");
        }
        deepXml.Append("</Project>");
        File.WriteAllText(Path.Combine(root, "obj", "Test.csproj.nuget.g.props"), deepXml.ToString());

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("XML depth", StringComparison.OrdinalIgnoreCase)),
            "Deep XML nesting was not bounded during parsing.");
    }

    [Fact]
    public void TraversalLookingAssetPathsFailClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;
        var cache = workspace.Cache;

        var assets = Path.Combine(root, "obj", "traversal.assets.json");
        WriteAssets(assets, cache, "TraversalPackage", TraversalAssetPaths);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("invalid relative asset path", StringComparison.OrdinalIgnoreCase)),
            "Traversal-looking package path was not rejected.");
    }

    [Fact]
    public void InvalidCompilerMetadataFailsClosed()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;
        var cache = workspace.Cache;

        var assets = Path.Combine(root, "obj", "invalid-pe.assets.json");
        WriteAssets(assets, cache, "InvalidPePackage", InvalidAnalyzerPaths);
        var analyzerDirectory = Path.Combine(cache, "InvalidPePackage", "1.0.0", "analyzers", "dotnet", "cs");
        Directory.CreateDirectory(analyzerDirectory);
        File.WriteAllBytes(Path.Combine(analyzerDirectory, "invalid.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 });
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("analyzer metadata", StringComparison.OrdinalIgnoreCase)),
            "Invalid compiler metadata was not rejected.");
    }

    [Fact]
    public void OversizedRestoreInputFailsThroughTheCli()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;

        var assets = Path.Combine(root, "obj", "oversized.assets.json");
        File.WriteAllText(assets, new string('x', 16 * 1024 * 1024 + 1));
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!result.IsComplete, "Oversized metadata was not rejected as incomplete.");
        Assert.True(CommandLine.Run(new[] { "scan", assets, "--format", "json", "--no-telemetry" }) == 2,
            "The public CLI did not fail closed before materializing oversized restore input.");
    }

    [Fact]
    public void LargeReachableGraphStaysWithinTheResourceBudget()
    {
        using var workspace = new CliScenarioWorkspace();
        var root = workspace.Root;
        var cache = workspace.Cache;

        var assets = Path.Combine(root, "obj", "large.assets.json");
        const int packageCount = 300;
        WriteLargeAssets(assets, cache, packageCount);
        var timer = Stopwatch.StartNew();
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        timer.Stop();
        Assert.True(result.IsComplete && result.ResolvedPackageCount == packageCount, "Reachable graph coverage did not classify the complete graph.");
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(10), $"Reachable graph analysis exceeded the resource test limit: {timer.Elapsed}.");
    }

    [Fact]
    public void DiagnosticsDoNotExposeLocalPaths()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var marker = "HomeCacheMarker-" + Guid.NewGuid().ToString("N");
        var root = Path.Combine(scratch, marker);
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        Directory.CreateDirectory(cache);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, "MissingPackage", new List<string> { "build/missing.targets" });
        var absoluteMarker = Path.GetFullPath(root);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]!["MissingPackage/1.0.0"]!["build"] = new JsonObject { ["build/missing.targets"] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/MissingPackage/1.0.0/build/missing.targets\" Condition=\"'$(SENSITIVE_CONDITION_MARKER)' == 'enabled'\" /></Project>");
        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var output = new StringWriter(CultureInfo.InvariantCulture);
            var error = new StringWriter(CultureInfo.InvariantCulture);
            var priorOutput = Console.Out;
            var priorError = Console.Error;
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                _ = CommandLine.Run(new[] { "scan", assets, "--format", format, "--no-telemetry" });
            }
            finally
            {
                Console.SetOut(priorOutput);
                Console.SetError(priorError);
            }

            Assert.True(!output.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an absolute cache marker.");
            Assert.True(!error.ToString().Contains(absoluteMarker, StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an absolute cache marker.");
            Assert.True(!output.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} output disclosed an unsupported condition marker.");
            Assert.True(!error.ToString().Contains("SENSITIVE_CONDITION_MARKER", StringComparison.OrdinalIgnoreCase), $"{format} stderr disclosed an unsupported condition marker.");
        }
    }
}
