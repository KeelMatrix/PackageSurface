using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CapabilityRestoreFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class ImportApplicabilityTests
{
    [Fact]
    public void ApplicabilityAndPresentStateAreConservative()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var casingRoot = Path.Combine(scratch, "casing");
        var casingCache = Path.Combine(casingRoot, "cache");
        Directory.CreateDirectory(casingRoot);
        var casingAssets = Path.Combine(casingRoot, "project.assets.json");
        WriteAssets(casingAssets, casingCache, "Casing.Package", new List<string> { "tools/π-script.ps1" }, createFiles: true);
        var casingDocument = JsonNode.Parse(File.ReadAllText(casingAssets))!.AsObject();
        RenameJsonProperty(casingDocument["targets"]!["net8.0"]!.AsObject(), "Casing.Package/1.0.0", "cAsInG.pAcKaGe/1.0.0");
        RenameJsonProperty(casingDocument["libraries"]!.AsObject(), "Casing.Package/1.0.0", "CASING.PACKAGE/1.0.0");
        File.WriteAllText(casingAssets, casingDocument.ToJsonString());
        var casingResult = ResolvedGraphClassifier.Analyze(casingAssets, casingRoot, strictContent: false);
        Assert.True(casingResult.IsComplete && casingResult.Entries.Any(entry => entry.PackageId.Equals("cAsInG.pAcKaGe", StringComparison.OrdinalIgnoreCase)),
            "Package identity casing variation was not resolved case-insensitively.");

        var unicodeRoot = Path.Combine(scratch, "unicode");
        var unicodeCache = Path.Combine(unicodeRoot, "cache");
        Directory.CreateDirectory(unicodeRoot);
        var unicodeAssets = Path.Combine(unicodeRoot, "project.assets.json");
        WriteAssets(unicodeAssets, unicodeCache, "Unicode.Package", new List<string> { "tools/資料.ps1" }, createFiles: true);
        var unicodeResult = ResolvedGraphClassifier.Analyze(unicodeAssets, unicodeRoot, strictContent: false);
        Assert.True(unicodeResult.IsComplete && unicodeResult.Entries.Any(entry => entry.PackageRelativePath == "tools/資料.ps1"),
            "Unicode package-relative filename was not classified.");

        var duplicateRoot = Path.Combine(scratch, "duplicate");
        var duplicateCache = Path.Combine(duplicateRoot, "cache");
        Directory.CreateDirectory(duplicateRoot);
        var duplicateAssets = Path.Combine(duplicateRoot, "project.assets.json");
        WriteAssets(duplicateAssets, duplicateCache, "Duplicate.Package", new List<string> { "tools/duplicate.ps1", "tools/duplicate.ps1" }, createFiles: true);
        var duplicateResult = ResolvedGraphClassifier.Analyze(duplicateAssets, duplicateRoot, strictContent: false);
        Assert.True(!duplicateResult.IsComplete && duplicateResult.IncompleteReasons.Any(reason => reason.Contains("duplicate asset path", StringComparison.OrdinalIgnoreCase)),
            "Duplicate package assets were silently accepted.");

        var unknownRoot = Path.Combine(scratch, "unknown-language");
        var unknownCache = Path.Combine(unknownRoot, "cache");
        var unknownObj = Path.Combine(unknownRoot, "obj");
        Directory.CreateDirectory(unknownObj);
        var unknownAssets = Path.Combine(unknownObj, "project.assets.json");
        WriteAnalyzerAssets(unknownAssets, unknownCache, true, "analyzers/dotnet/cs/unknown.dll");
        var unknownDocument = JsonNode.Parse(File.ReadAllText(unknownAssets))!.AsObject();
        unknownDocument["project"]!.AsObject().Remove("restore");
        File.WriteAllText(unknownAssets, unknownDocument.ToJsonString());
        var unknownResult = ResolvedGraphClassifier.Analyze(unknownAssets, unknownRoot, strictContent: false);
        Assert.True(!unknownResult.IsComplete, "Language-specific compiler applicability did not fail closed when language evidence was unavailable.");
        var languageIndependentRoot = Path.Combine(scratch, "language-independent");
        var languageIndependentCache = Path.Combine(languageIndependentRoot, "cache");
        Directory.CreateDirectory(languageIndependentRoot);
        var languageIndependentAssets = Path.Combine(languageIndependentRoot, "project.assets.json");
        WriteAssets(languageIndependentAssets, languageIndependentCache, "Inventory.Package", new List<string> { "tools/inventory.ps1" }, createFiles: true);
        var languageIndependentResult = ResolvedGraphClassifier.Analyze(languageIndependentAssets, languageIndependentRoot, strictContent: false);
        Assert.True(languageIndependentResult.IsComplete, "Unknown project language unnecessarily failed a language-independent graph.");

    }
}
