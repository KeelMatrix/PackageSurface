using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.SurfaceEntryFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PackageIdentityTests
{
    [Fact]
    public void PackageIdentityAndAssetPathsAreCanonical()
    {


        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-identity-provenance-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var mismatchRoot = Path.Combine(scratch, "mismatch");
            var mismatchCache = Path.Combine(mismatchRoot, "cache");
            var mismatchObj = Path.Combine(mismatchRoot, "obj");
            Directory.CreateDirectory(mismatchObj);
            var mismatchAssets = Path.Combine(mismatchObj, "project.assets.json");
            WriteAssets(mismatchAssets, mismatchCache, "Claimed.Package", new List<string> { "build/tampered.targets" }, createFiles: true);
            var mismatch = JsonNode.Parse(File.ReadAllText(mismatchAssets))!.AsObject();
            mismatch["libraries"]!["Claimed.Package/1.0.0"]!["path"] = "Physical.Package/2.0.0";
            mismatch["targets"]!["net8.0"]!["Claimed.Package/1.0.0"]!["build"] = new JsonObject { ["build/tampered.targets"] = new JsonObject() };
            var physical = Path.Combine(mismatchCache, "Physical.Package", "2.0.0", "build");
            Directory.CreateDirectory(physical);
            File.WriteAllText(Path.Combine(physical, "tampered.targets"), "<Project />");
            File.WriteAllText(Path.Combine(mismatchObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/Physical.Package/2.0.0/build/tampered.targets\" /></Project>");
            File.WriteAllText(mismatchAssets, mismatch.ToJsonString());
            var mismatchResult = ResolvedGraphClassifier.Analyze(mismatchAssets, mismatchRoot, strictContent: false);
            Assert.True(!mismatchResult.IsComplete && mismatchResult.Entries.Count == 0, "A package library path from a different physical package identity was attributed successfully.");

            var normalizedRoot = Path.Combine(scratch, "normalized");
            var normalizedCache = Path.Combine(normalizedRoot, "cache");
            var normalizedObj = Path.Combine(normalizedRoot, "obj");
            Directory.CreateDirectory(normalizedObj);
            var normalizedAssets = Path.Combine(normalizedObj, "project.assets.json");
            WriteAssets(normalizedAssets, normalizedCache, "Normalized.Package", Array.Empty<string>());
            var normalized = JsonNode.Parse(File.ReadAllText(normalizedAssets))!.AsObject();
            normalized["libraries"]!["Normalized.Package/1.0.0"]!["path"] = "Normalized.Package/01.0";
            File.WriteAllText(normalizedAssets, normalized.ToJsonString());
            var normalizedResult = ResolvedGraphClassifier.Analyze(normalizedAssets, normalizedRoot, strictContent: false);
            Assert.True(normalizedResult.IsComplete && normalizedResult.ResolvedPackageCount == 1, "Equivalent NuGet version representations were not resolved to the physical package root.");
            Assert.True(PackageIdentity.TryCreate("Normalized.Package", "1.0.0-Alpha.1", out var prereleaseA) &&
                PackageIdentity.TryCreate("normalized.package", "1.0.0-alpha.1", out var prereleaseB) &&
                prereleaseA.Equals(prereleaseB), "Equivalent normalized prerelease versions were not treated as the same package identity.");

            var caseRoot = Path.Combine(scratch, "case");
            var caseCache = Path.Combine(caseRoot, "cache");
            var caseObj = Path.Combine(caseRoot, "obj");
            Directory.CreateDirectory(caseObj);
            var caseAssets = Path.Combine(caseObj, "project.assets.json");
            WriteAssets(caseAssets, caseCache, "Case.Package", new List<string> { "build/Case.targets" }, createFiles: true);
            var caseDocument = JsonNode.Parse(File.ReadAllText(caseAssets))!.AsObject();
            caseDocument["targets"]!["net8.0"]!["Case.Package/1.0.0"]!["build"] = new JsonObject { ["build/Case.targets"] = new JsonObject() };
            File.WriteAllText(caseAssets, caseDocument.ToJsonString());
            File.WriteAllText(Path.Combine(caseCache, "Case.Package", "1.0.0", "build", "Case.targets"), "<Project />");
            File.WriteAllText(Path.Combine(caseObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/case.package/1.0.0/BUILD/case.targets\" /></Project>");
            using (FileSystemComparisonScope.Push(caseRoot, FileSystemCaseSensitivity.Sensitive))
            {
                var caseResult = ResolvedGraphClassifier.Analyze(caseAssets, caseRoot, strictContent: false, fileSystemCaseSensitivity: FileSystemCaseSensitivity.Sensitive);
                Assert.True(!caseResult.IsComplete && caseResult.Entries.Count == 0, "A case-sensitive filesystem accepted a wrong-case generated import.");
                var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
                var projectCase = identity with { Project = "src/Consumer.csproj" };
                var pathCase = identity with { PackageRelativePath = "build/case.targets" };
                Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count > 0 &&
                    DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count > 0,
                    "Case-sensitive filesystem identity comparison collapsed distinct project or asset paths.");
            }

            using (FileSystemComparisonScope.Push(caseRoot, FileSystemCaseSensitivity.Insensitive))
            {
                var caseResult = ResolvedGraphClassifier.Analyze(caseAssets, caseRoot, strictContent: false, fileSystemCaseSensitivity: FileSystemCaseSensitivity.Insensitive);
                Assert.True(caseResult.IsComplete && caseResult.Entries.Any(entry => entry.Active), "A case-insensitive filesystem rejected a case-only generated import: " + string.Join(" | ", caseResult.IncompleteReasons));
                var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
                var projectCase = identity with { Project = "src/Consumer.csproj" };
                var pathCase = identity with { PackageRelativePath = "build/case.targets" };
                Assert.True(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count == 0 &&
                    DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count == 0,
                    "Case-insensitive filesystem identity comparison rejected an equivalent project or asset path.");
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }
}
