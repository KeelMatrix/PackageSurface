using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PackageVersionTests
{
    [Fact]
    public void VersionEquivalenceUsesNugetSemantics()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var equivalent = new[] { "1", "1.0", "1.0.0", "1.0.0.0", "01.000.000.000" };
        var identities = equivalent.Select(value =>
        {
            Assert.True(PackageIdentity.TryCreate("Version.Package", value, out var identity), $"NuGet-compatible version '{value}' was rejected.");
            return identity;
        }).ToArray();
        Assert.True(identities.All(identity => identity.Equals(identities[0])), "Equivalent numeric package versions did not share one identity.");
        Assert.True(PackageIdentity.TryCreate("Version.Package", "1.2.3.4", out var nonZeroFourth) &&
                !PackageIdentity.TryCreate("Version.Package", "1.2.3.4.5", out _),
            "The four-component version boundary was not enforced.");
        Assert.True(PackageIdentity.TryCreate("Version.Package", "1.0.0-Alpha.1+Build.1", out var prerelease) &&
                PackageIdentity.TryCreate("version.package", "1.0.0-alpha.1+Other.2", out var equivalentPrerelease) &&
                prerelease.Equals(equivalentPrerelease),
            "Prerelease casing/components or build metadata did not use the canonical identity grammar.");
        foreach (var invalid in new[]
        {
        "1..0",
        "1.0-",
        "1.0+",
        "1.0.0-alpha.01",
        "1.0.0-01",
        "1.0.0-α",
        "1.0.0-alpha..1",
        "1.0.0-",
        "1.0.0/child",
        "1.0.0,,2.0.0",
        "1.0.0-+build",
        " 1.0.0"
    })
        {
            Assert.True(!PackageIdentity.TryCreate("Version.Package", invalid, out _), $"Malformed version '{invalid}' was accepted.");
        }

        var root = Path.Combine(scratch, "version-ranges");
        var obj = Path.Combine(root, "obj");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, cache, "Range.Package", Array.Empty<string>());
        var source = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        foreach (var range in new[] { "[1, 1]", "[1.0, 1.0]", "[1.0.0, 1.0.0]", "[1.0.0.0, 1.0.0.0]", "1.0.0.0" })
        {
            source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = range;
            File.WriteAllText(assets, source.ToJsonString());
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Assert.True(result.IsComplete, $"Dependency range '{range}' disagreed with the canonical package identity: {string.Join(" | ", result.IncompleteReasons)}");
        }

        source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = "[1.0.0.0.0, )";
        File.WriteAllText(assets, source.ToJsonString());
        var invalidRange = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        Assert.True(!invalidRange.IsComplete && invalidRange.Entries.Count == 0, "A five-component dependency range was accepted.");

        foreach (var invalid in new[] { "1.0.0-alpha.01", "1.0.0-01", "1.0.0-α", "[1.0.0-alpha.01, )", "(1.0.0-01, 2.0.0]" })
        {
            source["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Range.Package"]!["version"] = invalid;
            File.WriteAllText(assets, source.ToJsonString());
            var malformed = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Assert.True(!malformed.IsComplete && malformed.Entries.Count == 0,
                $"Malformed NuGet version range '{invalid}' was accepted.");
        }
    }
    [Fact]
    public void AllCapabilityVersionFixtureCreatesBaseline()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;

        Assert.True(baselineResult.ExitCode == 0,
                    "The all-capability version fixture could not create its baseline: " + baselineResult.Output);
    }

    [Theory]
    [InlineData("1.0.0-alpha.01")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-α")]
    public void MalformedPackageKeysFailThroughTheCli(string invalid)
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;

        var malformedPath = Path.Combine(obj, "invalid-key-" + invalid.Replace('/', '_') + ".assets.json");
        File.WriteAllText(malformedPath, validSource.Replace("Version.Surface/1.0.0", "Version.Surface/" + invalid, StringComparison.Ordinal));
        try
        {
            AssertRestoreIdentityFailure(malformedPath, root, baseline, "malformed package identity " + invalid);
        }
        finally
        {
            File.Delete(malformedPath);
        }

    }

    [Fact]
    public void MalformedLibraryPathFailsThroughTheCli()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var libraryPathDocument = JsonNode.Parse(validSource)!.AsObject();
        libraryPathDocument["libraries"]!["Version.Surface/1.0.0"]!["path"] = "Version.Surface/1.0.0-alpha.01";
        var libraryPath = Path.Combine(obj, "invalid-library-path.assets.json");
        File.WriteAllText(libraryPath, libraryPathDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(libraryPath, root, baseline, "malformed library package path");
        }
        finally
        {
            File.Delete(libraryPath);
        }


    }

    [Fact]
    public void MalformedDirectRangeFailsThroughTheCli()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var directRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        directRangeDocument["project"]!["frameworks"]!["net8.0"]!["dependencies"]!["Version.Surface"]!["version"] = "[1.0.0-alpha.01, )";
        var directRange = Path.Combine(obj, "invalid-direct-range.assets.json");
        File.WriteAllText(directRange, directRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(directRange, root, baseline, "malformed direct dependency range");
        }
        finally
        {
            File.Delete(directRange);
        }


    }

    [Fact]
    public void MalformedTargetRangeFailsThroughTheCli()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var targetRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        targetRangeDocument["targets"]!["net8.0"]!["Version.Surface/1.0.0"]!["dependencies"] = new JsonObject
        {
            ["Version.Surface"] = "[1.0.0-01, )"
        };
        var targetRange = Path.Combine(obj, "invalid-target-range.assets.json");
        File.WriteAllText(targetRange, targetRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(targetRange, root, baseline, "malformed target dependency range");
        }
        finally
        {
            File.Delete(targetRange);
        }


    }

    [Fact]
    public void MalformedProjectRangeFailsThroughTheCli()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var projectRangeDocument = JsonNode.Parse(validSource)!.AsObject();
        projectRangeDocument["targets"]!["net8.0"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["dependencies"] = new JsonObject { ["Version.Surface"] = "[1.0.0-01, )" }
        };
        projectRangeDocument["libraries"]!["Referenced.Project/1.0.0"] = new JsonObject
        {
            ["type"] = "project",
            ["path"] = "../Referenced.Project",
            ["msbuildProject"] = "../Referenced.Project/Referenced.Project.csproj"
        };
        var projectRange = Path.Combine(obj, "invalid-project-range.assets.json");
        File.WriteAllText(projectRange, projectRangeDocument.ToJsonString());
        try
        {
            AssertRestoreIdentityFailure(projectRange, root, baseline, "malformed project dependency range");
        }
        finally
        {
            File.Delete(projectRange);
        }


    }

    [Fact]
    public void MalformedFormatFourRangeFailsThroughTheCli()
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var format4Document = JsonNode.Parse(validSource)!.AsObject();
        format4Document["version"] = 4;
        format4Document["projectFileDependencyGroups"] = new JsonObject
        {
            ["net8.0"] = new JsonArray("Version.Surface >= 1.0.0")
        };
        format4Document["project"]!["frameworks"]!["net8.0"]!["framework"] = "net8.0";
        format4Document["project"]!["frameworks"]!["net8.0"]!["targetAlias"] = "net8.0";
        format4Document["project"]!["restore"]!["frameworks"] = new JsonObject
        {
            ["net8.0"] = new JsonObject { ["framework"] = "net8.0", ["targetAlias"] = "net8.0" }
        };
        var format4 = Path.Combine(obj, "malformed-format4-version.assets.json");
        var format4Baseline = Path.Combine(obj, "malformed-format4-version-baseline.json");
        File.WriteAllText(format4, format4Document.ToJsonString());
        var format4BaselineResult = CaptureCommand("baseline", format4, "--output", format4Baseline, "--no-telemetry");
        Assert.True(format4BaselineResult.ExitCode == 0,
            "The valid format 4 version fixture could not create its baseline: " + format4BaselineResult.Output);
        try
        {
            format4Document["projectFileDependencyGroups"]!["net8.0"]![0] = "Version.Surface >= 1.0.0-alpha.01";
            File.WriteAllText(format4, format4Document.ToJsonString());
            AssertRestoreIdentityFailure(format4, root, format4Baseline, "malformed format 4 dependency requirement");
        }
        finally
        {
            if (File.Exists(format4)) File.Delete(format4);
            if (File.Exists(format4Baseline)) File.Delete(format4Baseline);
        }


    }

    [Theory]
    [InlineData("1.0.0-alpha.01")]
    [InlineData("1.0.0-01")]
    [InlineData("1.0.0-α")]
    public void MalformedGeneratedImportVersionFailsThroughTheCli(string invalid)
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var generatedTargets = Path.Combine(obj, "Test.csproj.nuget.g.targets");
        var generatedTargetsBefore = File.ReadAllBytes(generatedTargets);
        try
        {

            File.WriteAllText(generatedTargets,
                $"<Project><Import Project=\"$(NuGetPackageRoot)/Version.Surface/{invalid}/build/surface.targets\" /></Project>");
            AssertRestoreIdentityFailure(assets, root, baseline, "malformed generated import version " + invalid);

        }
        finally
        {
            File.WriteAllBytes(generatedTargets, generatedTargetsBefore);
        }


    }

    [Theory]
    [InlineData("text")]
    [InlineData("json")]
    [InlineData("sarif")]
    public void MalformedBaselineVersionFailsInEveryReport(string format)
    {
        using var fixture = new VersionSurfaceFixture();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var validSource = fixture.ValidSource;
        var baselineResult = fixture.BaselineResult;
        var baselineDocument = JsonNode.Parse(File.ReadAllText(baseline))!.AsObject();
        foreach (var entry in baselineDocument["entries"]!.AsArray().OfType<JsonObject>())
        {
            entry["version"] = "1.0.0-alpha.01";
        }
        var malformedBaseline = Path.Combine(obj, "malformed-version-baseline.json");
        File.WriteAllText(malformedBaseline, baselineDocument.ToJsonString());
        try
        {

            var check = CaptureCommand("check", assets, "--baseline", malformedBaseline, "--format", format, "--no-telemetry");
            Assert.True(check.ExitCode == 2 && check.Output.Contains("PS007", StringComparison.Ordinal),
                $"Malformed baseline version was accepted in {format} output.");
            AssertNoSuccessfulSurfaceOutput(check.Output, format, "malformed baseline version " + format);

        }
        finally
        {
            File.Delete(malformedBaseline);
        }

    }
}
