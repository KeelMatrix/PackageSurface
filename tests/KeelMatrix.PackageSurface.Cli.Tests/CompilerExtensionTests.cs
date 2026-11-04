using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CapabilityRestoreFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class CompilerExtensionTests
{
    [Fact]
    public void CompilerExtensionLanguageConventionsAreApplied()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var combinedRoot = Path.Combine(scratch, "analyzer-combined");
        var combinedAssets = Path.Combine(combinedRoot, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(combinedAssets)!);
        var baseAnalyzer = "analyzers/dotnet/cs/base.dll";
        var legacyAnalyzer = "analyzers/dotnet/roslyn3.8/cs/legacy.dll";
        var currentAnalyzer = "analyzers/dotnet/roslyn4.0/cs/current.dll";
        var incompatibleAnalyzer = "analyzers/dotnet/roslyn99.0/cs/incompatible.dll";
        var satelliteAnalyzer = "analyzers/dotnet/cs/base.resources.dll";
        var neutralAnalyzer = "analyzers/dotnet/neutral.dll";
        var neutralLegacyAnalyzer = "analyzers/dotnet/roslyn3.8/neutral-legacy.dll";
        var neutralCurrentAnalyzer = "analyzers/dotnet/roslyn4.0/neutral-current.dll";
        WriteAnalyzerAssets(combinedAssets, Path.Combine(combinedRoot, "cache"), true, baseAnalyzer, "Test.csproj", includeAnalyzers: true,
            additionalAnalyzerRelativePaths: new[] { legacyAnalyzer, currentAnalyzer, incompatibleAnalyzer, satelliteAnalyzer, neutralAnalyzer, neutralLegacyAnalyzer, neutralCurrentAnalyzer });
        var combinedResult = ResolvedGraphClassifier.Analyze(combinedAssets, combinedRoot, strictContent: false, compilerApiVersion: "4.0");
        var combinedEntries = combinedResult.Entries
            .Where(entry => entry.Capability == CapabilityKind.CompilerExtension)
            .ToDictionary(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase);
        Assert.True(combinedResult.IsComplete && combinedEntries[baseAnalyzer].Active && combinedEntries[currentAnalyzer].Active && !combinedEntries[incompatibleAnalyzer].Active &&
            !combinedEntries[legacyAnalyzer].Active && !combinedEntries[satelliteAnalyzer].Active && combinedEntries[neutralAnalyzer].Active &&
            !combinedEntries[neutralLegacyAnalyzer].Active && combinedEntries[neutralCurrentAnalyzer].Active,
            $"SDK-selected base and highest applicable analyzer versions were not classified independently. complete={combinedResult.IsComplete}; entries={combinedResult.Entries.Count}; reasons={string.Join(" | ", combinedResult.IncompleteReasons)}");

        var missingCompilerContext = ResolvedGraphClassifier.Analyze(combinedAssets, combinedRoot, strictContent: false);
        Assert.True(!missingCompilerContext.IsComplete && missingCompilerContext.IncompleteReasons.Any(reason => reason.Contains("explicit consuming compiler API version", StringComparison.Ordinal)),
            "Versioned analyzer assets did not fail closed when consuming compiler context was unavailable.");
        Assert.True(CommandLine.Run(new[] { "scan", combinedAssets, "--compiler-api-version", "4.0", "--no-telemetry" }) == 0,
            "The public CLI did not pass explicit compiler applicability context through to analysis.");

        var analyzerCases = new[]
        {
        (Language: "cs", Project: "Test.csproj", Path: "analyzers/dotnet/cs/valid.dll", Active: true),
        (Language: "vb", Project: "Test.vbproj", Path: "analyzers/dotnet/vb/valid.dll", Active: true),
        (Language: "fs", Project: "Test.fsproj", Path: "analyzers/dotnet/fs/valid.dll", Active: false),
        (Language: "cross", Project: "Test.csproj", Path: "analyzers/dotnet/vb/valid.dll", Active: false),
        (Language: "neutral", Project: "Test.unknown", Path: "analyzers/dotnet/neutral.dll", Active: true),
        (Language: "exe", Project: "Test.csproj", Path: "analyzers/dotnet/neutral.exe", Active: false),
        (Language: "optional", Project: "Test.csproj", Path: "analyzers/dotnet/roslyn4.0/cs/valid.dll", Active: true),
        (Language: "casing", Project: "Test.csproj", Path: "ANALYZERS/DOTNET/CS/VALID.DLL", Active: true)
    };

        foreach (var testCase in analyzerCases)
        {
            var root = Path.Combine(scratch, "analyzer-" + testCase.Language);
            var assets = Path.Combine(root, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            WriteAnalyzerAssets(assets, Path.Combine(root, "cache"), true, testCase.Path, testCase.Project, includeAnalyzers: true);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false,
                compilerApiVersion: testCase.Path.Contains("roslyn", StringComparison.OrdinalIgnoreCase) ? "4.0" : null);
            var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.CompilerExtension);
            Assert.True(result.IsComplete && entry.Active == testCase.Active, $"Analyzer convention case '{testCase.Language}' was misclassified. Complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
        }

        var contentCases = new[]
        {
        (Language: "cs", Project: "Test.csproj", Path: "contentFiles/cs/any/active.cs", CodeLanguage: "cs", BuildAction: "Compile", Active: true),
        (Language: "preprocessed", Project: "Test.csproj", Path: "contentFiles/cs/any/active.cs.pp", CodeLanguage: "cs", BuildAction: "Compile", Active: true),
        (Language: "vb", Project: "Test.vbproj", Path: "contentFiles/vb/any/active.vb", CodeLanguage: "vb", BuildAction: "Compile", Active: true),
        (Language: "fs", Project: "Test.fsproj", Path: "contentFiles/fs/any/active.fs", CodeLanguage: "fs", BuildAction: "Compile", Active: true),
        (Language: "any", Project: "Test.vbproj", Path: "contentFiles/any/any/active.vb", CodeLanguage: "any", BuildAction: "Compile", Active: true),
        (Language: "none", Project: "Test.csproj", Path: "contentFiles/cs/any/none.cs", CodeLanguage: "cs", BuildAction: "None", Active: false)
    };

        foreach (var testCase in contentCases)
        {
            var root = Path.Combine(scratch, "content-" + testCase.Language);
            var assets = Path.Combine(root, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            WriteContentAssets(assets, Path.Combine(root, "cache"), testCase.Project, testCase.Path, testCase.CodeLanguage, testCase.BuildAction);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            var entry = result.Entries.Single(candidate => candidate.Capability == CapabilityKind.CompileSourceInjection);
            Assert.True(result.IsComplete && entry.Active == testCase.Active, $"contentFiles convention case '{testCase.Language}' was misclassified.");
        }

        foreach (var malformed in new[] { "buildAction", "codeLanguage" })
        {
            var root = Path.Combine(scratch, "content-malformed-" + malformed);
            var assets = Path.Combine(root, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            WriteContentAssets(assets, Path.Combine(root, "cache"), "Test.csproj", "contentFiles/cs/any/bad.cs", "cs", "Compile", malformed);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Assert.True(!result.IsComplete, $"Malformed contentFiles {malformed} metadata was accepted.");
        }
    }
}
