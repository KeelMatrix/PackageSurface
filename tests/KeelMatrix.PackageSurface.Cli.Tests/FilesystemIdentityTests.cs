using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.FilesystemIdentityFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.SurfaceEntryFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class FilesystemIdentityTests
{
    [Theory]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "absolute-forward-slashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "root-slash")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "root-no-slash-backslashes")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "absolute-native")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "absolute-forward-slashes")]
    public void PackageAndProjectIdentityUseTheirOwnFilesystem(FileSystemCaseSensitivity processMode, FileSystemCaseSensitivity projectMode, FileSystemCaseSensitivity packageMode, string caseName)
    {
        using var fixture = new FilesystemIdentityFixture(processMode, projectMode, packageMode);
        var scenario = $"cwd-{processMode}-project-{projectMode}-cache-{packageMode}";
        var root = fixture.Root;
        var workingRoot = fixture.WorkingRoot;
        var projectRoot = fixture.ProjectRoot;
        var packageCache = fixture.PackageCache;
        var assets = fixture.AssetsPath;
        var packageAsset = fixture.PackageAsset;
        var generatedTargets = fixture.GeneratedTargets;
        var rootOverrides = fixture.RootOverrides;
        using var comparison = FileSystemComparisonScope.Push(workingRoot, FileSystemCaseSensitivity.Host, rootOverrides);

        var variants = new (string Name, string WrongCase, string CorrectCase)[]
                    {
                    ("root-slash", "$(NuGetPackageRoot)/Case.Package/1.0.0/build/case.targets", "$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets"),
                    ("root-no-slash-backslashes", "$(NuGetPackageRoot)Case.Package\\1.0.0\\build\\case.targets", "$(NuGetPackageRoot)Case.Package\\1.0.0\\build\\Case.targets"),
                    ("absolute-native", packageAsset.Replace("Case.targets", "case.targets", StringComparison.Ordinal), packageAsset),
                    ("absolute-forward-slashes", packageAsset.Replace("Case.targets", "case.targets", StringComparison.Ordinal).Replace('\\', '/'), packageAsset.Replace('\\', '/'))
                    };
        var variant = variants.Single(value => value.Name == caseName);

        WriteCaseImport(generatedTargets, variant.WrongCase);
        var before = ResolvedGraphClassifier.Analyze(
            assets,
            projectRoot,
            strictContent: true,
            projectContext: "Test.csproj",
            selectedProjectPath: Path.Combine(projectRoot, "Test.csproj"));
        Assert.True(before.IsComplete, $"{scenario}/{variant.Name}: wrong-case pre-transition scan was incomplete: {string.Join(" | ", before.IncompleteReasons)}");
        var beforeAsset = before.Entries.Single(entry => entry.PackageRelativePath == "build/Case.targets");
        var expectedBefore = packageMode == FileSystemCaseSensitivity.Insensitive;
        Assert.True(beforeAsset.Active == expectedBefore,
            $"{scenario}/{variant.Name}: package-root casing used the process or project volume instead of the package cache.");

        WriteCaseImport(generatedTargets, variant.CorrectCase);
        var after = ResolvedGraphClassifier.Analyze(
            assets,
            projectRoot,
            strictContent: true,
            projectContext: "Test.csproj",
            selectedProjectPath: Path.Combine(projectRoot, "Test.csproj"));
        Assert.True(after.IsComplete && after.Entries.Single(entry => entry.PackageRelativePath == "build/Case.targets").Active,
            $"{scenario}/{variant.Name}: correct-case package import was not active.");

        var expectedDifference = packageMode == FileSystemCaseSensitivity.Sensitive;
        var normalTransition = DiffEngine.Compare(before.Entries.Select(BaselineEntry.From).ToArray(), after.Entries, strictContent: false);
        var strictTransition = DiffEngine.Compare(before.Entries.Select(BaselineEntry.From).ToArray(), after.Entries, strictContent: true);
        Assert.True((normalTransition.Count > 0) == expectedDifference && (strictTransition.Count > 0) == expectedDifference,
            $"{scenario}/{variant.Name}: normal and strict transitions did not follow package-cache casing.");

        var baseEntry = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { PackageId = "Case.Package", Project = "Test.csproj" };
        var caseDistinctAsset = baseEntry with { PackageRelativePath = "build/case.targets" };
        var deduplicated = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance)
                    {
                        SurfaceIdentity.From(baseEntry),
                        SurfaceIdentity.From(caseDistinctAsset)
                    };
        Assert.True(deduplicated.Count == (packageMode == FileSystemCaseSensitivity.Sensitive ? 2 : 1),
            $"{scenario}/{variant.Name}: classifier identity grouping used the wrong root's casing rules.");

        var caseDistinctProject = baseEntry with { Project = "test.csproj" };
        var projectIdentities = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance)
                    {
                        SurfaceIdentity.From(baseEntry),
                        SurfaceIdentity.From(caseDistinctProject)
                    };
        Assert.True(projectIdentities.Count == (projectMode == FileSystemCaseSensitivity.Sensitive ? 2 : 1),
            $"{scenario}/{variant.Name}: project identity used package-cache or process-directory casing.");
        Assert.True(PackageIdentity.TryCreate("Case.Package", "1.0.0-Alpha.1", out var packageA) &&
PackageIdentity.TryCreate("case.package", "1.0.0-alpha.1", out var packageB) && packageA.Equals(packageB),
$"{scenario}: NuGet package ID/version identity stopped being case-insensitive.");

    }

    [Theory]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Sensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, FileSystemCaseSensitivity.Insensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Sensitive, "nuget-identity")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "asset-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "project-path")]
    [InlineData(FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, FileSystemCaseSensitivity.Insensitive, "nuget-identity")]
    public void BaselineDuplicateIdentityUsesSelectedRoots(FileSystemCaseSensitivity processMode, FileSystemCaseSensitivity projectMode, FileSystemCaseSensitivity packageMode, string caseName)
    {
        using var fixture = new FilesystemIdentityFixture(processMode, projectMode, packageMode);
        var scenario = $"cwd-{processMode}-project-{projectMode}-cache-{packageMode}";
        var root = fixture.Root;
        var workingRoot = fixture.WorkingRoot;
        var projectRoot = fixture.ProjectRoot;
        var packageCache = fixture.PackageCache;
        var assets = fixture.AssetsPath;
        var packageAsset = fixture.PackageAsset;
        var generatedTargets = fixture.GeneratedTargets;
        var rootOverrides = fixture.RootOverrides;
        using var comparison = FileSystemComparisonScope.Push(workingRoot, FileSystemCaseSensitivity.Host, rootOverrides);

        var cases = new[]
                {
                ("asset-path", (Action<JsonObject>)(entry => entry["packageRelativePath"] = "build/case.targets"), packageMode == FileSystemCaseSensitivity.Insensitive),
                ("project-path", (Action<JsonObject>)(entry => entry["project"] = "test.csproj"), projectMode == FileSystemCaseSensitivity.Insensitive),
                ("nuget-identity", (Action<JsonObject>)(entry => { entry["packageId"] = "case.package"; entry["version"] = "1.0.0-ALPHA.1"; }), true)
            };
        var (name, duplicateChange, expectedDuplicate) = cases.Single(value => value.Item1 == caseName);
        var previousCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            Environment.CurrentDirectory = workingRoot;

            var duplicateBaseline = Path.Combine(root, $"{name}-duplicate.json");
            WriteDuplicateBaseline(duplicateBaseline, duplicateChange);
            using var commandScope = FileSystemComparisonScope.Push(workingRoot, FileSystemCaseSensitivity.Host, rootOverrides);
            var check = CaptureCommand("check", assets, "--baseline", duplicateBaseline, "--format", "json", "--no-telemetry");
            Assert.True(check.ExitCode == (expectedDuplicate ? 2 : 1),
                $"{scenario}/{name}: baseline duplicate validation used working-directory semantics or disagreed with scan/check (exit {check.ExitCode}). Output: {check.Output}");

        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
        }

    }

    [Fact]
    public void AncestorAliasesAreCanonicalized()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "ancestor-canonicalization-seam", "link");
        var target = Path.Combine(scratch, "ancestor-canonicalization-seam", "target");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        Directory.CreateDirectory(target);
        var assets = Path.Combine(obj, "project.assets.json");
        var ancestorFiles = new[] { "tools/inside.ps1" };
        WriteAssets(assets, cache, "Ancestor.Seam", ancestorFiles, createFiles: true);
        var canonicalizer = new AncestorLinkCanonicalizer(root, target);
        var priorCanonicalizer = PackageFileSystemSafety.PathCanonicalizer.Value;
        PackageFileSystemSafety.PathCanonicalizer.Value = canonicalizer.Canonicalize;
        ProbeResult result;
        try
        {
            result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
        }
        finally
        {
            PackageFileSystemSafety.PathCanonicalizer.Value = priorCanonicalizer;
        }
        Assert.True(result.IsComplete && canonicalizer.AncestorPathObserved,
            "The canonicalization seam did not exercise a deterministic ancestor-link containment case: " + string.Join(" | ", result.IncompleteReasons));
    }
}
