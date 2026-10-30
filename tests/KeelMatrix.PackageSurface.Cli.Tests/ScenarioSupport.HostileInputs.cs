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

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static partial class ScenarioSupport
{
    private static readonly string[] TraversalAssetPaths = { "../escape.props" };
    private static readonly string[] InvalidAnalyzerPaths = { "analyzers/dotnet/cs/invalid.dll" };

    internal static void RunDtdGeneratedPropsRegression(string root, string assets)
    {
        File.WriteAllText(Path.Combine(root, "obj", "Test.csproj.nuget.g.props"), "<!DOCTYPE Project [<!ENTITY external SYSTEM 'file:///outside'>]><Project><Import Project='&external;' /></Project>");
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete, "DTD input was not rejected as incomplete.");
    }

    internal static void RunDeepGeneratedPropsRegression(string root, string assets)
    {
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
        Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("XML depth", StringComparison.OrdinalIgnoreCase)),
            "Deep XML nesting was not bounded during parsing.");
    }

    internal static void RunTraversalAssetPathRegression(string root, string cache)
    {
        var assets = Path.Combine(root, "obj", "traversal.assets.json");
        WriteAssets(assets, cache, "TraversalPackage", TraversalAssetPaths);
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("invalid relative asset path", StringComparison.OrdinalIgnoreCase)),
            "Traversal-looking package path was not rejected.");
    }

    internal static void RunInvalidAnalyzerMetadataRegression(string root, string cache)
    {
        var assets = Path.Combine(root, "obj", "invalid-pe.assets.json");
        WriteAssets(assets, cache, "InvalidPePackage", InvalidAnalyzerPaths);
        var analyzerDirectory = Path.Combine(cache, "InvalidPePackage", "1.0.0", "analyzers", "dotnet", "cs");
        Directory.CreateDirectory(analyzerDirectory);
        File.WriteAllBytes(Path.Combine(analyzerDirectory, "invalid.dll"), new byte[] { 0x4D, 0x5A, 0x00, 0x01, 0x02 });
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("analyzer metadata", StringComparison.OrdinalIgnoreCase)),
            "Invalid compiler metadata was not rejected.");
    }

    internal static void RunOversizedRestoreRegression(string root)
    {
        var assets = Path.Combine(root, "obj", "oversized.assets.json");
        File.WriteAllText(assets, new string('x', 16 * 1024 * 1024 + 1));
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!result.IsComplete, "Oversized metadata was not rejected as incomplete.");
        Require(CommandLine.Run(new[] { "scan", assets, "--format", "json", "--no-telemetry" }) == 2,
            "The public CLI did not fail closed before materializing oversized restore input.");
    }

    internal static void RunLargeGraphBudgetRegression(string root, string cache)
    {
        var assets = Path.Combine(root, "obj", "large.assets.json");
        const int packageCount = 300;
        WriteLargeAssets(assets, cache, packageCount);
        var timer = Stopwatch.StartNew();
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        timer.Stop();
        Require(result.IsComplete && result.ResolvedPackageCount == packageCount, "Reachable graph coverage did not classify the complete graph.");
        Require(timer.Elapsed < TimeSpan.FromSeconds(10), $"Reachable graph analysis exceeded the resource test limit: {timer.Elapsed}.");
    }

}
