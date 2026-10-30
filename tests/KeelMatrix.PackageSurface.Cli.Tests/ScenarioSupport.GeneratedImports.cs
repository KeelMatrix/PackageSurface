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
    internal static void RunInlineTaskConventionRegression(string scratch)
    {
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
            Require(result.IsComplete && observed == expected, $"Inline task fixture '{name}' was misclassified.");
        }
    }

    internal static void RunLanguageConventionRegression(string scratch)
    {
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
        Require(combinedResult.IsComplete && combinedEntries[baseAnalyzer].Active && combinedEntries[currentAnalyzer].Active && !combinedEntries[incompatibleAnalyzer].Active &&
            !combinedEntries[legacyAnalyzer].Active && !combinedEntries[satelliteAnalyzer].Active && combinedEntries[neutralAnalyzer].Active &&
            !combinedEntries[neutralLegacyAnalyzer].Active && combinedEntries[neutralCurrentAnalyzer].Active,
            $"SDK-selected base and highest applicable analyzer versions were not classified independently. complete={combinedResult.IsComplete}; entries={combinedResult.Entries.Count}; reasons={string.Join(" | ", combinedResult.IncompleteReasons)}");

        var missingCompilerContext = ResolvedGraphClassifier.Analyze(combinedAssets, combinedRoot, strictContent: false);
        Require(!missingCompilerContext.IsComplete && missingCompilerContext.IncompleteReasons.Any(reason => reason.Contains("explicit consuming compiler API version", StringComparison.Ordinal)),
            "Versioned analyzer assets did not fail closed when consuming compiler context was unavailable.");
        Require(CommandLine.Run(new[] { "scan", combinedAssets, "--compiler-api-version", "4.0", "--no-telemetry" }) == 0,
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
            Require(result.IsComplete && entry.Active == testCase.Active, $"Analyzer convention case '{testCase.Language}' was misclassified. Complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
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
            Require(result.IsComplete && entry.Active == testCase.Active, $"contentFiles convention case '{testCase.Language}' was misclassified.");
        }

        foreach (var malformed in new[] { "buildAction", "codeLanguage" })
        {
            var root = Path.Combine(scratch, "content-malformed-" + malformed);
            var assets = Path.Combine(root, "obj", "project.assets.json");
            Directory.CreateDirectory(Path.GetDirectoryName(assets)!);
            WriteContentAssets(assets, Path.Combine(root, "cache"), "Test.csproj", "contentFiles/cs/any/bad.cs", "cs", "Compile", malformed);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(!result.IsComplete, $"Malformed contentFiles {malformed} metadata was accepted.");
        }
    }

    internal static void RunApplicabilityHardeningRegressions(string scratch)
    {
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
        Require(casingResult.IsComplete && casingResult.Entries.Any(entry => entry.PackageId.Equals("cAsInG.pAcKaGe", StringComparison.OrdinalIgnoreCase)),
            "Package identity casing variation was not resolved case-insensitively.");

        var unicodeRoot = Path.Combine(scratch, "unicode");
        var unicodeCache = Path.Combine(unicodeRoot, "cache");
        Directory.CreateDirectory(unicodeRoot);
        var unicodeAssets = Path.Combine(unicodeRoot, "project.assets.json");
        WriteAssets(unicodeAssets, unicodeCache, "Unicode.Package", new List<string> { "tools/資料.ps1" }, createFiles: true);
        var unicodeResult = ResolvedGraphClassifier.Analyze(unicodeAssets, unicodeRoot, strictContent: false);
        Require(unicodeResult.IsComplete && unicodeResult.Entries.Any(entry => entry.PackageRelativePath == "tools/資料.ps1"),
            "Unicode package-relative filename was not classified.");

        var duplicateRoot = Path.Combine(scratch, "duplicate");
        var duplicateCache = Path.Combine(duplicateRoot, "cache");
        Directory.CreateDirectory(duplicateRoot);
        var duplicateAssets = Path.Combine(duplicateRoot, "project.assets.json");
        WriteAssets(duplicateAssets, duplicateCache, "Duplicate.Package", new List<string> { "tools/duplicate.ps1", "tools/duplicate.ps1" }, createFiles: true);
        var duplicateResult = ResolvedGraphClassifier.Analyze(duplicateAssets, duplicateRoot, strictContent: false);
        Require(!duplicateResult.IsComplete && duplicateResult.IncompleteReasons.Any(reason => reason.Contains("duplicate asset path", StringComparison.OrdinalIgnoreCase)),
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
        Require(!unknownResult.IsComplete, "Language-specific compiler applicability did not fail closed when language evidence was unavailable.");
        var languageIndependentRoot = Path.Combine(scratch, "language-independent");
        var languageIndependentCache = Path.Combine(languageIndependentRoot, "cache");
        Directory.CreateDirectory(languageIndependentRoot);
        var languageIndependentAssets = Path.Combine(languageIndependentRoot, "project.assets.json");
        WriteAssets(languageIndependentAssets, languageIndependentCache, "Inventory.Package", new List<string> { "tools/inventory.ps1" }, createFiles: true);
        var languageIndependentResult = ResolvedGraphClassifier.Analyze(languageIndependentAssets, languageIndependentRoot, strictContent: false);
        Require(languageIndependentResult.IsComplete, "Unknown project language unnecessarily failed a language-independent graph.");

        RunLanguageConventionRegression(scratch);

        RunProjectAggregationRegression(scratch);
        RunReparsePointRegression(scratch);
        RunReparsePathVariantRegression(scratch);
    }

    internal static void RunNestedImportRegression(string scratch)
    {
        var root = Path.Combine(scratch, "nested-imports");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageKey = "Nested.Package/1.0.0";
        var packageRoot = Path.Combine(cache, "Nested.Package", "1.0.0");
        var packageTargets = "build/Nested.Package.targets";
        var helperTargets = "helpers/Helper.targets";
        var grandchildProps = "helpers/Grandchild.props";
        WriteAssets(assets, cache, "Nested.Package", new[] { packageTargets, helperTargets, grandchildProps }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageKey]!["build"] = new JsonObject { [packageTargets] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());
        File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../helpers/Helper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)),
            "<Project><ImportGroup Condition=\"'$(TargetFramework)' == 'net8.0'\"><Import Project=\"Grandchild.props\" /></ImportGroup></Project>");
        File.WriteAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<Project><PropertyGroup><NestedValue>1</NestedValue></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Package/1.0.0/{packageTargets}\" /></Project>");

        var baseline = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        var helper = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase));
        var grandchild = baseline.Entries.Single(entry => entry.PackageRelativePath.Equals(grandchildProps, StringComparison.OrdinalIgnoreCase));
        Require(baseline.IsComplete && helper.Active && helper.Sha256 is not null && grandchild.Active && grandchild.Sha256 is not null,
            $"Nested arbitrary-path import graph was not active and strictly fingerprinted. helper={helper.Active}/{helper.Sha256}; grandchild={grandchild.Active}/{grandchild.Sha256}; complete={baseline.IsComplete}; reasons={string.Join(" | ", baseline.IncompleteReasons)}");

        File.AppendAllText(Path.Combine(packageRoot, grandchildProps.Replace('/', Path.DirectorySeparatorChar)), "<!-- changed -->");
        var changed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(DiffEngine.Compare(baseline.Entries.Select(BaselineEntry.From).ToArray(), changed.Entries, strictContent: true).Any(diagnostic => diagnostic.Id == "PS005"),
            "A nested helper-only content change was not detected.");

        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"Missing.props\" /></Project>");
        var missing = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(!missing.IsComplete && missing.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)),
            "A missing arbitrary-path nested import was not fail-closed.");

        File.WriteAllText(Path.Combine(packageRoot, helperTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"../build/Nested.Package.targets\" /></Project>");
        var cycle = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(cycle.IsComplete && cycle.Entries.Any(entry => entry.PackageRelativePath.Equals(helperTargets, StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A bounded static import cycle was not handled deterministically.");

        File.WriteAllText(Path.Combine(packageRoot, packageTargets.Replace('/', Path.DirectorySeparatorChar)), "<Project><Import Project=\"$(SensitiveDynamicImportMarker)\" /></Project>");
        var dynamic = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(!dynamic.IsComplete && dynamic.IncompleteReasons.Any(reason => reason.Contains("unsupported static import target", StringComparison.OrdinalIgnoreCase)) &&
            dynamic.IncompleteReasons.All(reason => !reason.Contains("SensitiveDynamicImportMarker", StringComparison.OrdinalIgnoreCase)),
            "An unsupported dynamic nested import was not fail-closed without disclosing its expression.");

        RunNestedPhaseMatrixRegression(scratch);
        RunPackageXmlSchemaRegression(scratch);
    }

    internal static void RunProjectExtensionsRegression(string scratch)
    {
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
        Require(generatedResult.IsComplete && generatedResult.Entries.Count == 0,
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
        Require(validNamespaceResult.IsComplete && validNamespaceResult.Entries.Any(entry => entry.PackageId == validNamespacePackage && entry.Active),
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
        Require(packageResult.IsComplete && outer is { Active: true } && (outer.ObservedPrimitives?.Count ?? 0) == 0 &&
                !packageResult.Entries.Any(entry => entry.PackageRelativePath == helperPath && entry.Active),
            $"Package ProjectExtensions content activated, recursed, or counted MSBuild primitives: complete={packageResult.IsComplete}; outer={outer?.Active}; primitives={string.Join(',', outer?.ObservedPrimitives ?? Array.Empty<string>())}; reasons={string.Join(" | ", packageResult.IncompleteReasons)}");
    }

    internal static void RunInvalidMsBuildContextRegression(string scratch)
    {
        var invalidGeneratedImports = new[]
        {
        ("target parent", "<Project><Target Name=\"Invalid\"><Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></Target></Project>"),
        ("unknown parent", "<Project><UnknownGroup><Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></UnknownGroup></Project>"),
        ("unknown namespace", "<Project xmlns:x=\"urn:unrecognized\"><x:Import Project=\"$(NuGetPackageRoot)/Generated.Context/1.0.0/build/context.targets\" /></Project>")
    };

        foreach (var (name, xml) in invalidGeneratedImports)
        {
            var root = Path.Combine(scratch, "invalid-generated-context-" + name.Replace(' ', '-'));
            var cache = Path.Combine(root, "cache");
            var obj = Path.Combine(root, "obj");
            Directory.CreateDirectory(obj);
            var assets = Path.Combine(obj, "project.assets.json");
            var packageId = "Generated.Context";
            var assetPath = "build/context.targets";
            WriteAssets(assets, cache, packageId, new[] { assetPath }, createFiles: true);
            var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [assetPath] = new JsonObject() };
            File.WriteAllText(assets, document.ToJsonString());
            File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), xml);

            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(!result.IsComplete && result.Entries.Count == 0,
                $"Generated import in {name} was accepted instead of failing closed: complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
        }

        var invalidPackageImports = new[]
        {
        ("target parent", "<Project><Target Name=\"Invalid\"><Import Project=\"helpers/child.targets\" /></Target></Project>"),
        ("unknown parent", "<Project><UnknownGroup><Import Project=\"helpers/child.targets\" /></UnknownGroup></Project>"),
        ("unknown namespace", "<Project xmlns:x=\"urn:unrecognized\"><x:Import Project=\"helpers/child.targets\" /></Project>")
    };

        foreach (var (name, xml) in invalidPackageImports)
        {
            var root = Path.Combine(scratch, "invalid-package-context-" + name.Replace(' ', '-'));
            var cache = Path.Combine(root, "cache");
            var obj = Path.Combine(root, "obj");
            Directory.CreateDirectory(obj);
            var assets = Path.Combine(obj, "project.assets.json");
            var packageId = "Nested.Context";
            var outerPath = "build/outer.targets";
            var childPath = "helpers/child.targets";
            WriteAssets(assets, cache, packageId, new[] { outerPath, childPath }, createFiles: true);
            var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [outerPath] = new JsonObject() };
            File.WriteAllText(assets, document.ToJsonString());
            File.WriteAllText(Path.Combine(cache, packageId, "1.0.0", outerPath.Replace('/', Path.DirectorySeparatorChar)), xml);
            File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
                $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{outerPath}\" /></Project>");

            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(!result.IsComplete && result.Entries.Count == 0,
                $"Nested import in {name} was accepted instead of failing closed: complete={result.IsComplete}; entries={result.Entries.Count}; reasons={string.Join(" | ", result.IncompleteReasons)}");
        }

        var invalidPrimitiveContexts = new[]
        {
        ("Exec at Project root", "<Project><Exec Command=\"not-a-target-task\" /></Project>"),
        ("UsingTask under Target", "<Project><Target Name=\"Invalid\"><UsingTask TaskName=\"Invalid\" AssemblyFile=\"task.dll\" /></Target></Project>"),
        ("Code outside Task", "<Project><Code>not-inline-task-code</Code></Project>")
    };
        foreach (var (name, xml) in invalidPrimitiveContexts)
        {
            var root = Path.Combine(scratch, "invalid-primitive-context-" + name.Replace(' ', '-'));
            var cache = Path.Combine(root, "cache");
            var obj = Path.Combine(root, "obj");
            Directory.CreateDirectory(obj);
            var assets = Path.Combine(obj, "project.assets.json");
            var packageId = "Primitive.Context";
            var packagePath = "build/primitive.targets";
            WriteAssets(assets, cache, packageId, new[] { packagePath }, createFiles: true);
            var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
            document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [packagePath] = new JsonObject() };
            File.WriteAllText(assets, document.ToJsonString());
            File.WriteAllText(Path.Combine(cache, packageId, "1.0.0", packagePath.Replace('/', Path.DirectorySeparatorChar)), xml);
            File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
                $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{packagePath}\" /></Project>");

            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            var scan = CaptureCommand("scan", assets, "--format", "text", "--no-telemetry");
            Require(!result.IsComplete && scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("BuildTargets", StringComparison.Ordinal),
                $"A package MSBuild primitive in {name} was accepted or emitted a partial surface: complete={result.IsComplete}; exit={scan.ExitCode}; output={scan.Output}; reasons={string.Join(" | ", result.IncompleteReasons)}");
        }
    }

    internal static void RunPackageXmlSchemaRegression(string scratch)
    {
        var root = Path.Combine(scratch, "package-xml-schema");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Package.Xml.Schema";
        var packagePath = "build/Package.Xml.Schema.targets";
        WriteAssets(assets, cache, packageId, new[] { packagePath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [packagePath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());

        var physicalPath = Path.Combine(cache, packageId, "1.0.0", packagePath.Replace('/', Path.DirectorySeparatorChar));
        var canonical = """
<Project ToolsVersion="Current">
  <PropertyGroup Label="standard">
    <SchemaValue>1</SchemaValue>
  </PropertyGroup>
  <ItemGroup Label="standard">
    <None Include="content.txt" />
    <None Update="content.txt" />
    <None Remove="obsolete.txt" />
  </ItemGroup>
  <Target Name="Standard" BeforeTargets="Build" AfterTargets="Build" DependsOnTargets="CoreCompile" Inputs="$(ProjectFile)" Outputs="$(TargetPath)" Returns="$(TargetPath)" KeepDuplicateOutputs="true" Label="standard" />
  <UsingTask TaskName="Inline" TaskFactory="CodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" Condition="'$(TargetFramework)' == 'never'">
    <Task>
      <Code Type="Fragment" Language="cs">Log.LogMessage("never executed");</Code>
    </Task>
  </UsingTask>
  <Target Name="NeverExecuted">
    <Exec Command="echo never-executed" Condition="'$(TargetFramework)' == 'never'" />
  </Target>
  <ImportGroup Label="standard">
    <Import Project="helpers/Helper.targets" Condition="'$(TargetFramework)' == 'never'" Label="standard" />
  </ImportGroup>
</Project>
""";
        File.WriteAllText(physicalPath, canonical);
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{packagePath}\" /></Project>");

        var baseline = Path.Combine(root, "baseline.json");
        var valid = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Require(valid.IsComplete, $"Standard unconsumed MSBuild constructs were rejected: {string.Join(" | ", valid.IncompleteReasons)}");
        Require(CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry").ExitCode == 0,
            "The standard MSBuild XML baseline could not be created.");

        foreach (var (name, replacement) in new[]
        {
        ("element-import-case", (Old: "<Import Project=", New: "<import Project=")),
        ("element-import-near", (Old: "<Import Project=", New: "<Impor Project=")),
        ("element-using-task-case", (Old: "<UsingTask TaskName=", New: "<usingtask TaskName=")),
        ("element-using-task-near", (Old: "<UsingTask TaskName=", New: "<UsingTas TaskName=")),
        ("element-exec-case", (Old: "<Exec Command=", New: "<exec Command=")),
        ("element-exec-near", (Old: "<Exec Command=", New: "<Exce Command=")),
        ("element-code-case", (Old: "<Code Type=", New: "<code Type=")),
        ("element-code-near", (Old: "<Code Type=", New: "<Cod Type="))
    })
        {
            AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, name, replacement.Old, replacement.New);
        }

        foreach (var (name, canonicalAttribute, alias, near) in new[]
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
    })
        {
            AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-case", " " + canonicalAttribute + "=", " " + alias + "=");
            AssertPackageXmlVariantFailure(assets, root, baseline, physicalPath, canonical, "attribute-" + name + "-near", " " + canonicalAttribute + "=", " " + near + "=");
            AssertPackageXmlDuplicateAttributeFailure(assets, root, baseline, physicalPath, canonical, name, canonicalAttribute, alias);
        }
    }

    internal static void RunNestedPhaseMatrixRegression(string scratch)
    {
        var root = Path.Combine(scratch, "nested-phase-matrix");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Nested.Phase.Matrix";
        var files = new[]
        {
        "build/Root.props",
        "build/Helper.targets",
        "buildTransitive/Transitive.targets",
        "buildTransitive/TransitiveHelper.props",
        "buildMultiTargeting/Outer.props",
        "buildMultiTargeting/OuterHelper.targets"
    };
        WriteAssets(assets, cache, packageId, files, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        var package = document["targets"]!["net8.0"]![packageId + "/1.0.0"]!.AsObject();
        package["build"] = new JsonObject { ["build/Root.props"] = new JsonObject() };
        package["buildTransitive"] = new JsonObject { ["buildTransitive/Transitive.targets"] = new JsonObject() };
        package["buildMultiTargeting"] = new JsonObject { ["buildMultiTargeting/Outer.props"] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());

        var packageRoot = Path.Combine(cache, packageId, "1.0.0");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Root.props"), "<Project><Import Project=\"Helper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project><Import Project=\"Root.props\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "Transitive.targets"), "<Project><Import Project=\"TransitiveHelper.props\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildTransitive", "TransitiveHelper.props"), "<Project />");
        File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props"), "<Project><Import Project=\"OuterHelper.targets\" /></Project>");
        File.WriteAllText(Path.Combine(packageRoot, "buildMultiTargeting", "OuterHelper.targets"), "<Project />");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.props"),
            "<Project>" +
            "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/build/Root.props\" />" +
            "<Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildMultiTargeting/Outer.props\" />" +
            "</Project>");
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"),
            "<Project><Import Project=\"$(NuGetPackageRoot)/Nested.Phase.Matrix/1.0.0/buildTransitive/Transitive.targets\" /></Project>");

        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(result.IsComplete, $"Nested phase matrix did not classify completely: {string.Join(" | ", result.IncompleteReasons)}");
        foreach (var path in files.Skip(1))
        {
            var entries = result.Entries.Where(candidate => candidate.PackageRelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
            Require(entries.Length > 0 && entries.All(entry => entry.Active && entry.Sha256 is not null), $"Nested phase matrix descendant '{path}' was not active and fingerprinted.");
        }

        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' != '.NETCoreApp,Version=v8.0'\" /></Project>");
        var literalAlias = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(literalAlias.IsComplete && literalAlias.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A literal framework alias was normalized instead of compared with MSBuild string semantics: entries=" + string.Join(" | ", literalAlias.Entries.Select(entry => $"{entry.PackageRelativePath}:{entry.Active}")) + " reasons=" + string.Join(" | ", literalAlias.IncompleteReasons));

        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(TargetFramework)'\" /></Project>");
        var propertyReference = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(propertyReference.IsComplete && propertyReference.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A supported property reference on the right side of a condition was not resolved.");

        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"&quot;$( TargetFramework )&quot; == &quot;net8.0&quot; AND ('$(TargetFramework)' != 'net9.0' OR '$(TargetFramework)' == 'net8.0')\" /></Project>");
        var quotedBoolean = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(quotedBoolean.IsComplete && quotedBoolean.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "Quoted or nested AND/OR TargetFramework conditions were not evaluated with the supported string grammar.");

        var outerProps = Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props");
        File.WriteAllText(outerProps, "<Project><Import Project=\"OuterHelper.targets\" Condition=\"'$(TargetFramework)' == ''\" /></Project>");
        var outerBuild = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(outerBuild.IsComplete && outerBuild.Entries.Any(entry => entry.PackageRelativePath.Equals("buildMultiTargeting/OuterHelper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "Outer-build TargetFramework condition context was not evaluated independently from target-build context.");

        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(UnsupportedConditionProperty)'\" /></Project>");
        var unsupportedExpansion = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Require(!unsupportedExpansion.IsComplete && unsupportedExpansion.IncompleteReasons.Any(reason => reason.Contains("unsupported property expansion", StringComparison.OrdinalIgnoreCase)),
            "An unsupported condition property expansion was treated as a known false branch.");
    }

    internal static void RunImportEdgeBudgetRegression(string scratch)
    {
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
        Require(within.IsComplete && within.Entries.Count == 2, $"The 8,192 cumulative generated-import edge boundary failed: {string.Join(" | ", within.IncompleteReasons)}");

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
            Require(exitCode == 2, $"An 8,193 cumulative generated-import edge graph returned exit {exitCode}.");
        }
        finally
        {
            Console.SetOut(priorOutput);
        }

        Require(!output.ToString().Contains("Edge.props", StringComparison.Ordinal) &&
            !output.ToString().Contains("Edge.targets", StringComparison.Ordinal),
            "The over-budget import graph emitted partial capability success output.");
    }

    internal static void RunGeneratedImportReachabilityRegression(string scratch)
    {
        var directRoot = Path.Combine(scratch, "orphan-generated-import");
        var directCache = Path.Combine(directRoot, "cache");
        var directObj = Path.Combine(directRoot, "obj");
        Directory.CreateDirectory(directObj);
        var directAssets = Path.Combine(directObj, "project.assets.json");
        WriteAssets(directAssets, directCache, "Reachable.Package", Array.Empty<string>());
        var directBaseline = Path.Combine(directRoot, "baseline.json");
        Require(CaptureCommand("baseline", directAssets, "--output", directBaseline, "--no-telemetry").ExitCode == 0,
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
        Require(CaptureCommand("baseline", coherentAssets, "--output", coherentBaseline, "--no-telemetry").ExitCode == 0,
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
        Require(CaptureCommand("baseline", conditionalAssets, "--output", conditionalBaseline, "--no-telemetry").ExitCode == 0,
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

    internal static void RunInvocationStructuralBudgetRegression(string scratch)
    {
        var root = Path.Combine(scratch, "invocation-budget");
        Directory.CreateDirectory(root);
        var projects = new[] { "ProjectA", "ProjectB" };
        foreach (var name in projects)
        {
            var projectRoot = Path.Combine(root, name);
            var obj = Path.Combine(projectRoot, "obj");
            Directory.CreateDirectory(obj);
            File.WriteAllText(Path.Combine(projectRoot, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            WriteTargetGraphAssets(Path.Combine(obj, "project.assets.json"), Path.Combine(projectRoot, "cache"), Path.Combine(projectRoot, name + ".csproj"), 257, includeToolAsset: true);
        }

        var projectA = Path.Combine(root, "ProjectA", "ProjectA.csproj");
        var baselinePath = Path.Combine(root, "approved.json");
        var baselineResult = CaptureCommand("baseline", projectA, "--output", baselinePath, "--no-telemetry");
        Require(baselineResult.ExitCode == 0, "The within-budget project could not create its recovery baseline.");
        var baselineBefore = File.ReadAllBytes(baselinePath);

        foreach (var format in new[] { "text", "json", "sarif" })
        {
            var scan = CaptureCommand("scan", root, "--format", format, "--no-telemetry");
            Require(scan.ExitCode == 2 && scan.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !scan.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted partial {format} scan output.");

            var baseline = CaptureCommand("baseline", root, "--output", baselinePath, "--format", format, "--no-telemetry");
            Require(baseline.ExitCode == 2 && baseline.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !baseline.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted partial {format} baseline output.");
            Require(baselineBefore.SequenceEqual(File.ReadAllBytes(baselinePath)),
                $"Cumulative target-graph overflow mutated the baseline for {format} output.");

            var check = CaptureCommand("check", root, "--baseline", baselinePath, "--format", format, "--no-telemetry");
            Require(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal) &&
                    !check.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal) &&
                    !check.Output.Contains("PS003", StringComparison.Ordinal),
                $"Cumulative target-graph overflow emitted a clean-looking {format} check result.");
        }

        var narrowed = CaptureCommand("scan", projectA, "--project", projectA, "--no-telemetry");
        Require(narrowed.ExitCode == 0 && narrowed.Output.Contains("ToolOrScriptPresent", StringComparison.Ordinal),
            "--project did not provide the documented recovery path after an aggregate structural-budget failure.");
        Require(CaptureCommand("baseline", projectA, "--project", projectA, "--output", Path.Combine(root, "recovery.json"), "--no-telemetry").ExitCode == 0,
            "--project baseline recovery failed after the aggregate structural-budget failure.");
        Require(CaptureCommand("check", projectA, "--project", projectA, "--baseline", baselinePath, "--no-telemetry").ExitCode == 0,
            "--project check recovery failed after the aggregate structural-budget failure.");

        var inventoryRoot = Path.Combine(scratch, "invocation-inventory-budget");
        var inventoryFiles = Enumerable.Range(0, 10_001)
            .Select(index => $"lib/net8.0/inventory-{index.ToString(CultureInfo.InvariantCulture)}.dat")
            .ToArray();
        foreach (var name in projects)
        {
            var projectRoot = Path.Combine(inventoryRoot, name);
            var obj = Path.Combine(projectRoot, "obj");
            Directory.CreateDirectory(obj);
            File.WriteAllText(Path.Combine(projectRoot, name + ".csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup></Project>");
            WriteAssets(Path.Combine(obj, "project.assets.json"), Path.Combine(projectRoot, "cache"), name + ".Inventory", inventoryFiles, projectFileName: name + ".csproj");
        }

        var inventoryFailure = CaptureCommand("scan", inventoryRoot, "--format", "json", "--no-telemetry");
        Require(inventoryFailure.ExitCode == 2 && !inventoryFailure.Output.Contains("inventory-", StringComparison.Ordinal),
            "Selecting two projects did not fail closed without partial package-inventory output.");
        Require(CaptureCommand("scan", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--project", Path.Combine(inventoryRoot, "ProjectA", "ProjectA.csproj"), "--no-telemetry").ExitCode == 0,
            "A narrowed project did not remain within the package-inventory file budget.");
    }

    internal static void RunProjectAggregationRegression(string scratch)
    {
        var root = Path.Combine(scratch, "project-aggregation");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var packageId = "Aggregate.Package";
        var packageKey = packageId + "/1.0.0";
        var relativePath = "buildMultiTargeting/Aggregate.props";
        var packageRoot = Path.Combine(cache, packageId, "1.0.0");
        Directory.CreateDirectory(Path.Combine(packageRoot, "buildMultiTargeting"));
        File.WriteAllText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)), "<Project />");
        var targetPackage = new JsonObject
        {
            ["type"] = "package",
            ["buildMultiTargeting"] = new JsonObject { [relativePath] = new JsonObject() }
        };
        var targets = new JsonObject
        {
            ["net8.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() },
            ["net9.0"] = new JsonObject { [packageKey] = targetPackage.DeepClone() }
        };
        var libraries = new JsonObject
        {
            [packageKey] = new JsonObject
            {
                ["type"] = "package",
                ["path"] = packageKey,
                ["files"] = new JsonArray(relativePath)
            }
        };
        var projectFile = Path.Combine(root, "Aggregate.csproj");
        File.WriteAllText(projectFile, "<Project />");
        File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.props"), "<Project><Import Project='$(NuGetPackageRoot)/Aggregate.Package/1.0.0/buildMultiTargeting/Aggregate.props' /></Project>");
        File.WriteAllText(Path.Combine(obj, "Aggregate.csproj.nuget.g.targets"), "<Project />");
        var document = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = targets,
            ["libraries"] = libraries,
            ["packageFolders"] = new JsonObject { [cache] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = projectFile },
                ["frameworks"] = new JsonObject
                {
                    ["net8.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } },
                    ["net9.0"] = new JsonObject { ["dependencies"] = new JsonObject { [packageId] = PackageDependency() } }
                }
            }
        };
        var assets = Path.Combine(obj, "project.assets.json");
        File.WriteAllText(assets, document.ToJsonString());
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        var entry = result.Entries.SingleOrDefault(candidate => candidate.Capability == CapabilityKind.BuildMultiTargeting);
        Require(result.IsComplete && entry is not null && entry.Relationship == "direct" && entry.Active,
            "Project-level direct/transitive aggregation was not deterministic or direct-wins.");
    }

}
