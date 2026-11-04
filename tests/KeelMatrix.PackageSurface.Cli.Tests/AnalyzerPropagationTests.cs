using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CapabilityRestoreFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class AnalyzerPropagationTests
{
    [Fact]
    public void AnalyzerExclusionsMatchRestoreRules()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;
        var cache = workspace.Cache;
        var obj = workspace.Obj;

        var analyzerRelativePath = "analyzers/dotnet/cs/valid.dll";
        var directAssets = Path.Combine(obj, "analyzer-excluded-direct.assets.json");
        WriteAnalyzerAssets(directAssets, cache, true, analyzerRelativePath);
        var directResult = ResolvedGraphClassifier.Analyze(directAssets, scratch, strictContent: false);
        Assert.True(directResult.IsComplete && directResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
            "Direct ExcludeAssets=analyzers was not honored.");

        var transitiveAssets = Path.Combine(obj, "analyzer-excluded-transitive.assets.json");
        WriteAnalyzerAssets(transitiveAssets, cache, false, analyzerRelativePath);
        var transitiveResult = ResolvedGraphClassifier.Analyze(transitiveAssets, scratch, strictContent: false);
        Assert.True(transitiveResult.IsComplete && transitiveResult.Entries.Single(entry => entry.Capability == CapabilityKind.CompilerExtension).Active == false,
            "Transitive ExcludeAssets=analyzers was not honored.");
    }
}
