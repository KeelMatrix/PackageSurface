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
    internal static void RunPackageIdentityAndPathRegression()
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
            Require(!mismatchResult.IsComplete && mismatchResult.Entries.Count == 0, "A package library path from a different physical package identity was attributed successfully.");

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
            Require(normalizedResult.IsComplete && normalizedResult.ResolvedPackageCount == 1, "Equivalent NuGet version representations were not resolved to the physical package root.");
            Require(PackageIdentity.TryCreate("Normalized.Package", "1.0.0-Alpha.1", out var prereleaseA) &&
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
                Require(!caseResult.IsComplete && caseResult.Entries.Count == 0, "A case-sensitive filesystem accepted a wrong-case generated import.");
                var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
                var projectCase = identity with { Project = "src/Consumer.csproj" };
                var pathCase = identity with { PackageRelativePath = "build/case.targets" };
                Require(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count > 0 &&
                    DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count > 0,
                    "Case-sensitive filesystem identity comparison collapsed distinct project or asset paths.");
            }

            using (FileSystemComparisonScope.Push(caseRoot, FileSystemCaseSensitivity.Insensitive))
            {
                var caseResult = ResolvedGraphClassifier.Analyze(caseAssets, caseRoot, strictContent: false, fileSystemCaseSensitivity: FileSystemCaseSensitivity.Insensitive);
                Require(caseResult.IsComplete && caseResult.Entries.Any(entry => entry.Active), "A case-insensitive filesystem rejected a case-only generated import: " + string.Join(" | ", caseResult.IncompleteReasons));
                var identity = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { Project = "Src/Consumer.csproj" };
                var projectCase = identity with { Project = "src/Consumer.csproj" };
                var pathCase = identity with { PackageRelativePath = "build/case.targets" };
                Require(DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { projectCase }, false).Count == 0 &&
                    DiffEngine.Compare(new[] { BaselineEntry.From(identity) }, new[] { pathCase }, false).Count == 0,
                    "Case-insensitive filesystem identity comparison rejected an equivalent project or asset path.");
            }
        }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

    internal static void RunMixedFilesystemIdentityRegression()
    {
        var singleBuildAsset = new List<string> { "build/Case.targets" };
        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-mixed-filesystem-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var previousCurrentDirectory = Environment.CurrentDirectory;
        try
        {
            var configuredModes = Enum.GetValues<FileSystemCaseSensitivity>().Where(mode => mode != FileSystemCaseSensitivity.Host).ToArray();
            var modePairs = from processMode in configuredModes
                            from projectMode in configuredModes
                            from packageMode in configuredModes
                            select (processMode, projectMode, packageMode);

            foreach (var (processMode, projectMode, packageMode) in modePairs)
            {
                var scenario = $"cwd-{processMode}-project-{projectMode}-cache-{packageMode}";
                var root = Path.Combine(scratch, scenario);
                var workingRoot = Path.Combine(root, "working");
                var projectRoot = Path.Combine(root, "project");
                var packageCache = Path.Combine(root, "packages");
                var obj = Path.Combine(projectRoot, "obj");
                Directory.CreateDirectory(workingRoot);
                Directory.CreateDirectory(obj);

                var assets = Path.Combine(obj, "project.assets.json");
                WriteAssets(assets, packageCache, "Case.Package", singleBuildAsset, createFiles: true);
                var assetDocument = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
                assetDocument["targets"]!["net8.0"]!["Case.Package/1.0.0"]!["build"] = new JsonObject
                {
                    ["build/Case.targets"] = new JsonObject()
                };
                File.WriteAllText(assets, assetDocument.ToJsonString());

                var packageAsset = Path.Combine(packageCache, "Case.Package", "1.0.0", "build", "Case.targets");
                File.WriteAllText(packageAsset, "<Project />");
                var generatedTargets = Path.Combine(obj, "Test.csproj.nuget.g.targets");
                var rootOverrides = new Dictionary<string, FileSystemCaseSensitivity>(StringComparer.Ordinal)
                {
                    [workingRoot] = processMode,
                    [projectRoot] = projectMode,
                    [packageCache] = packageMode
                };

                using (FileSystemComparisonScope.Push(workingRoot, FileSystemCaseSensitivity.Host, rootOverrides))
                {
                    var variants = new (string Name, string WrongCase, string CorrectCase)[]
                    {
                    ("root-slash", "$(NuGetPackageRoot)/Case.Package/1.0.0/build/case.targets", "$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets"),
                    ("root-no-slash-backslashes", "$(NuGetPackageRoot)Case.Package\\1.0.0\\build\\case.targets", "$(NuGetPackageRoot)Case.Package\\1.0.0\\build\\Case.targets"),
                    ("absolute-native", packageAsset.Replace("Case.targets", "case.targets", StringComparison.Ordinal), packageAsset),
                    ("absolute-forward-slashes", packageAsset.Replace("Case.targets", "case.targets", StringComparison.Ordinal).Replace('\\', '/'), packageAsset.Replace('\\', '/'))
                    };

                    foreach (var variant in variants)
                    {
                        WriteCaseImport(generatedTargets, variant.WrongCase);
                        var before = ResolvedGraphClassifier.Analyze(
                            assets,
                            projectRoot,
                            strictContent: true,
                            projectContext: "Test.csproj",
                            selectedProjectPath: Path.Combine(projectRoot, "Test.csproj"));
                        Require(before.IsComplete, $"{scenario}/{variant.Name}: wrong-case pre-transition scan was incomplete: {string.Join(" | ", before.IncompleteReasons)}");
                        var beforeAsset = before.Entries.Single(entry => entry.PackageRelativePath == "build/Case.targets");
                        var expectedBefore = packageMode == FileSystemCaseSensitivity.Insensitive;
                        Require(beforeAsset.Active == expectedBefore,
                            $"{scenario}/{variant.Name}: package-root casing used the process or project volume instead of the package cache.");

                        WriteCaseImport(generatedTargets, variant.CorrectCase);
                        var after = ResolvedGraphClassifier.Analyze(
                            assets,
                            projectRoot,
                            strictContent: true,
                            projectContext: "Test.csproj",
                            selectedProjectPath: Path.Combine(projectRoot, "Test.csproj"));
                        Require(after.IsComplete && after.Entries.Single(entry => entry.PackageRelativePath == "build/Case.targets").Active,
                            $"{scenario}/{variant.Name}: correct-case package import was not active.");

                        var expectedDifference = packageMode == FileSystemCaseSensitivity.Sensitive;
                        var normalTransition = DiffEngine.Compare(before.Entries.Select(BaselineEntry.From).ToArray(), after.Entries, strictContent: false);
                        var strictTransition = DiffEngine.Compare(before.Entries.Select(BaselineEntry.From).ToArray(), after.Entries, strictContent: true);
                        Require((normalTransition.Count > 0) == expectedDifference && (strictTransition.Count > 0) == expectedDifference,
                            $"{scenario}/{variant.Name}: normal and strict transitions did not follow package-cache casing.");

                        var baseEntry = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with { PackageId = "Case.Package", Project = "Test.csproj" };
                        var caseDistinctAsset = baseEntry with { PackageRelativePath = "build/case.targets" };
                        var deduplicated = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance)
                    {
                        SurfaceIdentity.From(baseEntry),
                        SurfaceIdentity.From(caseDistinctAsset)
                    };
                        Require(deduplicated.Count == (packageMode == FileSystemCaseSensitivity.Sensitive ? 2 : 1),
                            $"{scenario}/{variant.Name}: classifier identity grouping used the wrong root's casing rules.");

                        var caseDistinctProject = baseEntry with { Project = "test.csproj" };
                        var projectIdentities = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance)
                    {
                        SurfaceIdentity.From(baseEntry),
                        SurfaceIdentity.From(caseDistinctProject)
                    };
                        Require(projectIdentities.Count == (projectMode == FileSystemCaseSensitivity.Sensitive ? 2 : 1),
                            $"{scenario}/{variant.Name}: project identity used package-cache or process-directory casing.");
                    }

                    Require(PackageIdentity.TryCreate("Case.Package", "1.0.0-Alpha.1", out var packageA) &&
                            PackageIdentity.TryCreate("case.package", "1.0.0-alpha.1", out var packageB) && packageA.Equals(packageB),
                        $"{scenario}: NuGet package ID/version identity stopped being case-insensitive.");
                }

                Environment.CurrentDirectory = workingRoot;
                foreach (var (name, duplicateChange, expectedDuplicate) in new[]
                {
                ("asset-path", (Action<JsonObject>)(entry => entry["packageRelativePath"] = "build/case.targets"), packageMode == FileSystemCaseSensitivity.Insensitive),
                ("project-path", (Action<JsonObject>)(entry => entry["project"] = "test.csproj"), projectMode == FileSystemCaseSensitivity.Insensitive),
                ("nuget-identity", (Action<JsonObject>)(entry => { entry["packageId"] = "case.package"; entry["version"] = "1.0.0-ALPHA.1"; }), true)
            })
                {
                    var duplicateBaseline = Path.Combine(root, $"{name}-duplicate.json");
                    WriteDuplicateBaseline(duplicateBaseline, duplicateChange);
                    using var commandScope = FileSystemComparisonScope.Push(workingRoot, FileSystemCaseSensitivity.Host, rootOverrides);
                    var check = CaptureCommand("check", assets, "--baseline", duplicateBaseline, "--format", "json", "--no-telemetry");
                    Require(check.ExitCode == (expectedDuplicate ? 2 : 1),
                        $"{scenario}/{name}: baseline duplicate validation used working-directory semantics or disagreed with scan/check (exit {check.ExitCode}). Output: {check.Output}");
                }
            }

            Console.WriteLine("filesystem identity matrix: 8 process/project/cache root combinations; 4 Exists path forms; no skips.");
        }
        finally
        {
            Environment.CurrentDirectory = previousCurrentDirectory;
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }

        static void WriteCaseImport(string path, string existsPath)
        {
            File.WriteAllText(path,
                $"<Project><Import Project=\"$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets\" Condition=\"Exists('{existsPath}')\" /></Project>");
        }

        static void WriteDuplicateBaseline(string path, Action<JsonObject> alterDuplicate)
        {
            var entry = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with
            {
                PackageId = "Case.Package",
                Project = "Test.csproj"
            };
            BaselineDocument.Write(path, new SurfaceSnapshot(new[] { entry }, Array.Empty<string>(), false, 1));
            var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
            var duplicate = document["entries"]!.AsArray()[0]!.DeepClone()!.AsObject();
            alterDuplicate(duplicate);
            document["entries"]!.AsArray().Add(duplicate);
            File.WriteAllText(path, document.ToJsonString());
        }
    }

    internal static void RunAncestorCanonicalizationRegression(string scratch)
    {
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
        Require(result.IsComplete && canonicalizer.AncestorPathObserved,
            "The canonicalization seam did not exercise a deterministic ancestor-link containment case: " + string.Join(" | ", result.IncompleteReasons));
    }

    internal static void RunReparsePointRegression(string scratch)
    {
        var root = Path.Combine(scratch, "reparse");
        var cache = Path.Combine(root, "cache");
        Directory.CreateDirectory(root);
        var assets = Path.Combine(root, "project.assets.json");
        WriteAssets(assets, cache, "Link.Package", new List<string> { "tools/escape.ps1" });
        var packageTools = Path.Combine(cache, "Link.Package", "1.0.0", "tools");
        Directory.CreateDirectory(packageTools);
        var outside = Path.Combine(scratch, "outside.ps1");
        File.WriteAllText(outside, "outside");
        try
        {
            File.CreateSymbolicLink(Path.Combine(packageTools, "escape.ps1"), outside);
            var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: false);
            Require(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),
                "A supported filesystem link escape was not rejected.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: leaf reparse-point regression is unavailable on this platform."); }
        finally
        {
            if (File.Exists(outside)) File.Delete(outside);
        }

        var rootLink = Path.Combine(scratch, "root-link");
        var rootLinkCache = Path.Combine(rootLink, "cache");
        var rootLinkObj = Path.Combine(rootLink, "obj");
        Directory.CreateDirectory(rootLinkObj);
        var rootLinkAssets = Path.Combine(rootLinkObj, "project.assets.json");
        WriteAssets(rootLinkAssets, rootLinkCache, "RootLink.Package", new List<string> { "build/root.targets" });
        var rootLinkPackage = Path.Combine(rootLinkCache, "RootLink.Package", "1.0.0");
        var outsidePackage = Path.Combine(scratch, "outside-package");
        Directory.CreateDirectory(Path.Combine(outsidePackage, "build"));
        File.WriteAllText(Path.Combine(outsidePackage, "build", "root.targets"), "<Project />");
        Directory.Delete(rootLinkPackage, recursive: true);
        try
        {
            Directory.CreateSymbolicLink(rootLinkPackage, outsidePackage);
            File.WriteAllText(Path.Combine(rootLinkObj, "Test.csproj.nuget.g.targets"), "<Project><Import Project=\"$(NuGetPackageRoot)/RootLink.Package/1.0.0/build/root.targets\" /></Project>");
            var rootLinkResult = ResolvedGraphClassifier.Analyze(rootLinkAssets, rootLink, strictContent: true);
            Require(!rootLinkResult.IsComplete && rootLinkResult.Entries.All(entry => entry.Sha256 is null),
                "A rejected package-root link was opened or hashed through a fallback path.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: package-root reparse-point regression is unavailable on this platform."); }

        var interiorRoot = Path.Combine(scratch, "interior-link");
        var interiorCache = Path.Combine(interiorRoot, "cache");
        var interiorObj = Path.Combine(interiorRoot, "obj");
        Directory.CreateDirectory(interiorObj);
        var interiorAssets = Path.Combine(interiorObj, "project.assets.json");
        var interiorFiles = new[] { "build/inside.targets" };
        WriteAssets(interiorAssets, interiorCache, "InteriorLink.Package", interiorFiles, createFiles: true);
        var interiorPackage = Path.Combine(interiorCache, "InteriorLink.Package", "1.0.0");
        var interiorBuild = Path.Combine(interiorPackage, "build");
        var outsideBuild = Path.Combine(scratch, "outside-build");
        Directory.CreateDirectory(outsideBuild);
        File.WriteAllText(Path.Combine(outsideBuild, "inside.targets"), "<Project />");
        Directory.Delete(interiorBuild, recursive: true);
        try
        {
            Directory.CreateSymbolicLink(interiorBuild, outsideBuild);
            var interiorResult = ResolvedGraphClassifier.Analyze(interiorAssets, interiorRoot, strictContent: false);
            Require(!interiorResult.IsComplete && interiorResult.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),
                "An interior directory reparse point was not rejected.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: interior-directory reparse-point regression is unavailable on this platform."); }

        var ancestorTarget = Path.Combine(scratch, "ancestor-target");
        var ancestorLink = Path.Combine(scratch, "ancestor-link");
        var ancestorCreated = false;
        try
        {
            Directory.CreateDirectory(ancestorTarget);
            Directory.CreateSymbolicLink(ancestorLink, ancestorTarget);
            ancestorCreated = true;
        }
        catch (UnauthorizedAccessException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this filesystem; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }
        catch (IOException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this filesystem; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }
        catch (PlatformNotSupportedException ex) { Console.WriteLine($"UNVERIFIED: ancestor reparse-point regression is unavailable on this platform; operation=Directory.CreateSymbolicLink('{ancestorLink}', '{ancestorTarget}'); exception={ex.GetType().Name}; message={ex.Message}"); }

        if (ancestorCreated)
        {
            Directory.CreateDirectory(Path.Combine(ancestorTarget, "obj"));
            Directory.CreateDirectory(Path.Combine(ancestorTarget, "cache"));
            var ancestorCache = Path.Combine(ancestorLink, "cache");
            var ancestorAssets = Path.Combine(ancestorLink, "obj", "project.assets.json");
            WriteAssets(ancestorAssets, ancestorCache, "AncestorLink.Package", new List<string> { "build/inside.targets" }, createFiles: true);
            File.WriteAllText(Path.Combine(ancestorCache, "AncestorLink.Package", "1.0.0", "build", "inside.targets"), "<Project />");
            var ancestorResult = ResolvedGraphClassifier.Analyze(ancestorAssets, ancestorLink, strictContent: false);
            Require(ancestorResult.IsComplete, "A reparse point outside the resolved package root was treated as an unsafe package path: " + string.Join(" | ", ancestorResult.IncompleteReasons));
            Console.WriteLine($"VERIFIED: ancestor reparse-point regression exercised a real directory link; link='{ancestorLink}'; target='{ancestorTarget}'");
        }
    }

    internal static void RunReparsePathVariantRegression(string scratch)
    {
        var variants = new[]
        {
        (Name: "mixed-case-separators", PackageId: "Case.Matrix", File: "BUILD\\Inside.TARGETS"),
        (Name: "unicode-separators", PackageId: "Unicode.包", File: "build\\内部.targets")
    };
        var states = new[] { "leaf", "package-root", "interior-directory", "ancestor" };

        foreach (var variant in variants)
        {
            foreach (var state in states)
            {
                var stateRoot = Path.Combine(scratch, "reparse-matrix", variant.Name, state);
                var root = stateRoot;
                var targetRoot = Path.Combine(stateRoot, "target");
                if (state == "ancestor")
                {
                    Directory.CreateDirectory(targetRoot);
                    root = Path.Combine(stateRoot, "link");
                    try
                    {
                        Directory.CreateSymbolicLink(root, targetRoot);
                    }
                    catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                    catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                    catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); continue; }
                }

                var obj = Path.Combine(root, "obj");
                var cache = Path.Combine(root, "cache");
                Directory.CreateDirectory(obj);
                var assets = Path.Combine(obj, "project.assets.json");
                var files = new[] { variant.File };
                WriteAssets(assets, cache, variant.PackageId, files, createFiles: true);
                var packageRoot = Path.Combine(cache, variant.PackageId, "1.0.0");
                var relativeFile = variant.File.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                var packageFile = Path.Combine(packageRoot, relativeFile);
                File.WriteAllText(packageFile, "<Project />");

                var outside = Path.Combine(stateRoot, "outside");
                try
                {
                    switch (state)
                    {
                        case "leaf":
                            File.Delete(packageFile);
                            File.WriteAllText(outside + ".targets", "<Project />");
                            File.CreateSymbolicLink(packageFile, outside + ".targets");
                            break;
                        case "package-root":
                            Directory.Delete(packageRoot, recursive: true);
                            Directory.CreateDirectory(Path.Combine(outside, "build"));
                            File.WriteAllText(Path.Combine(outside, "build", "Inside.TARGETS"), "<Project />");
                            Directory.CreateSymbolicLink(packageRoot, outside);
                            break;
                        case "interior-directory":
                            var buildDirectory = Path.Combine(packageRoot, Path.GetDirectoryName(relativeFile)!);
                            Directory.Delete(buildDirectory, recursive: true);
                            Directory.CreateDirectory(Path.Combine(outside, "nested"));
                            File.WriteAllText(Path.Combine(outside, "nested", Path.GetFileName(relativeFile)), "<Project />");
                            Directory.CreateSymbolicLink(buildDirectory, Path.Combine(outside, "nested"));
                            break;
                        case "ancestor":
                            break;
                    }
                }
                catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); continue; }
                catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); continue; }

                var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
                if (state == "ancestor")
                {
                    Require(result.IsComplete, $"An ancestor reparse point was rejected for {variant.Name}.");
                }
                else
                {
                    Require(!result.IsComplete && result.Entries.All(entry => entry.Sha256 is null),
                        $"The {state} reparse point was not rejected for {variant.Name}.");
                }
            }
        }

    }

    internal static void RunReachablePackageAliasRegression(string parent)
    {
        var root = Path.Combine(parent, "reachable-package-alias");
        var obj = Path.Combine(root, "obj");
        var fallback = Path.Combine(root, "fallback-packages");
        var unusedGlobal = Path.Combine(root, "global-packages");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var files = new[]
        {
        "build/direct.props",
        "buildTransitive/transitive.targets",
        "buildMultiTargeting/outer.targets",
        "props/import.props",
        "targets/import.targets",
        "runtimes/win-x64/lib/runtime.dll",
        "ref/net8.0/compile.dll",
        "contentFiles/cs/net8.0/source.cs",
        "analyzers/dotnet/cs/analyzer.dll",
        "runtimes/win-x64/native/native.dll",
        "resource/en-US/messages.resources",
        "tools/tool.ps1",
        "build/nested/helper.targets"
    };
        WriteAssets(assets, fallback, "Alias.Package", files, createFiles: true);

        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["packageFolders"] = new JsonObject
        {
            [unusedGlobal] = new JsonObject(),
            [fallback] = new JsonObject()
        };
        var fallbackPackageRoot = Path.Combine(fallback, "Alias.Package", "1.0.0");
        var globalPackageRoot = Path.Combine(unusedGlobal, "Alias.Package", "1.0.0");
        Directory.CreateDirectory(Path.GetDirectoryName(globalPackageRoot)!);
        Directory.Move(fallbackPackageRoot, globalPackageRoot);
        const string transitivePackageKey = "Transitive.Alias.Package/1.0.0";
        const string transitiveRelative = "tools/transitive-tool.ps1";
        var transitiveRoot = Path.Combine(fallback, "Transitive.Alias.Package", "1.0.0");
        var transitiveFile = Path.Combine(transitiveRoot, transitiveRelative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(transitiveFile)!);
        File.WriteAllText(transitiveFile, "transitive fixture");
        document["libraries"]![transitivePackageKey] = new JsonObject
        {
            ["type"] = "package",
            ["path"] = transitivePackageKey,
            ["files"] = new JsonArray(transitiveRelative)
        };
        var directTarget = document["targets"]!["net8.0"]!["Alias.Package/1.0.0"]!.AsObject();
        directTarget["dependencies"] = new JsonObject { ["Transitive.Alias.Package"] = "1.0.0" };
        document["targets"]!["net8.0"]![transitivePackageKey] = new JsonObject { ["type"] = "package" };
        document["targets"]!.AsObject()["net8.0/win-x64"] = document["targets"]!["net8.0"]!.DeepClone();
        File.WriteAllText(assets, document.ToJsonString());
        var packageRoot = globalPackageRoot;
        foreach (var relative in files)
        {
            var physical = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            if (physical.EndsWith(".props", StringComparison.OrdinalIgnoreCase) || physical.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                File.WriteAllText(physical, "<Project />");
            }
            else if (relative.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(typeof(ResolvedGraphClassifier).Assembly.Location, physical, overwrite: true);
            }
        }

        var reachable = ResolvedGraphClassifier.GetReachablePackageInputPaths(assets)
            .Select(Path.GetFullPath)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Require(files.All(relative => reachable.Contains(Path.GetFullPath(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))))),
            "The preflight package-input set omitted a reachable package inventory file or fallback-root asset.");
        Require(reachable.Contains(Path.GetFullPath(transitiveFile)),
            "The preflight package-input set omitted a reachable transitive package asset from the RID graph.");

        var seed = Path.Combine(root, "seed.json");
        Require(CaptureCommand("baseline", assets, "--output", seed, "--no-telemetry").ExitCode == 0,
            "The reachable package alias fixture could not create a valid baseline seed.");
        var distinct = Path.Combine(root, "distinct-output.json");
        var packageBytes = files.ToDictionary(
            relative => relative,
            relative => File.ReadAllBytes(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))),
            StringComparer.Ordinal);
        Require(CaptureCommand("baseline", assets, "--output", distinct, "--no-telemetry").ExitCode == 0,
            "A genuinely distinct baseline output was rejected while package inputs were present.");
        foreach (var relative in files)
        {
            var packageFile = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var lexicalAlias = Path.Combine(Path.GetDirectoryName(packageFile)!, ".", Path.GetFileName(packageFile));
            Require(CaptureCommand("baseline", assets, "--output", lexicalAlias, "--no-telemetry").ExitCode == 2,
                $"A lexical alias to reachable package input '{relative}' was accepted.");
            Require(packageBytes[relative].SequenceEqual(File.ReadAllBytes(packageFile)),
                $"A lexical package-input alias attempt changed '{relative}'.");
        }
        var transitiveAlias = Path.Combine(Path.GetDirectoryName(transitiveFile)!, ".", Path.GetFileName(transitiveFile));
        var transitiveBefore = File.ReadAllBytes(transitiveFile);
        Require(CaptureCommand("baseline", assets, "--output", transitiveAlias, "--no-telemetry").ExitCode == 2,
            "A lexical alias to a reachable transitive package input was accepted.");
        Require(transitiveBefore.SequenceEqual(File.ReadAllBytes(transitiveFile)),
            "A transitive package-input alias attempt changed the package file.");

        var hardlinkOutputs = new[] { Path.Combine(root, "package-hardlink.json"), Path.Combine(root, "package-hardlink-second.json") };
        var representative = Path.Combine(packageRoot, "tools", "tool.ps1");
        Require(TryCreateHardLink(representative, hardlinkOutputs[0]), "The package hardlink alias regression could not create a hardlink.");
        Require(TryCreateHardLink(representative, hardlinkOutputs[1]), "The package multiple-hardlink alias regression could not create a second hardlink.");
        try
        {
            foreach (var output in hardlinkOutputs)
            {
                Require(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                    "A hardlink alias to a reachable package input was accepted.");
                Require(packageBytes["tools/tool.ps1"].SequenceEqual(File.ReadAllBytes(representative)),
                    "A package hardlink alias attempt changed the representative package input.");
            }
        }
        finally
        {
            foreach (var output in hardlinkOutputs)
            {
                if (File.Exists(output)) File.Delete(output);
            }
        }

        RunSymlinkPackageAliasCase(root, assets, representative, packageBytes["tools/tool.ps1"], "direct");
        var packageDirectory = Path.GetDirectoryName(representative)!;
        RunDirectorySymlinkPackageAliasCase(root, assets, packageDirectory, representative, packageBytes["tools/tool.ps1"], "interior");
        RunDirectorySymlinkPackageAliasCase(root, assets, root, representative, packageBytes["tools/tool.ps1"], "ancestor");
    }

    internal static void RunMacOsRootAliasPredicateRegression()
    {
        var scratch = Path.Combine(Path.GetTempPath(), "packagesurface-macos-alias-" + Guid.NewGuid().ToString("N"));
        var fakeRoot = Path.Combine(scratch, "root");
        var privateRoot = Path.Combine(fakeRoot, "private");
        Directory.CreateDirectory(privateRoot);
        try
        {
            var method = typeof(BaselineFileTransaction).GetMethod("IsSystemRootAlias", BindingFlags.NonPublic | BindingFlags.Static, binder: null,
                types: new[] { typeof(DirectoryInfo), typeof(string), typeof(bool) }, modifiers: null)
                ?? throw new InvalidOperationException("The macOS root-alias predicate seam is unavailable.");
            var standardTarget = Path.Combine(privateRoot, "var");
            var standardLink = Path.Combine(fakeRoot, "var");
            Directory.CreateDirectory(standardTarget);
            Directory.CreateSymbolicLink(standardLink, standardTarget);
            Require((bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, true })!,
                "The standard macOS /var to /private/var alias was rejected.");

            var callerTarget = Path.Combine(privateRoot, "caller-alias");
            var callerLink = Path.Combine(fakeRoot, "caller-alias");
            Directory.CreateDirectory(callerTarget);
            Directory.CreateSymbolicLink(callerLink, callerTarget);
            Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(callerLink), fakeRoot, true })!,
                "A caller-controlled root-level /private/<name> alias was accepted as system-owned.");

            var nestedTarget = Path.Combine(privateRoot, "nested");
            var nestedLink = Path.Combine(standardLink, "nested");
            Directory.CreateDirectory(nestedTarget);
            Directory.CreateSymbolicLink(nestedLink, nestedTarget);
            Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(nestedLink), fakeRoot, true })!,
                "A nested link below the standard /var alias was accepted as system-owned.");
            Require(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, false })!,
                "The macOS root-alias exception ignored a non-macOS platform.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: macOS root-alias predicate filesystem regression is unavailable on this platform."); }
        finally
        {
            if (Directory.Exists(scratch)) Directory.Delete(scratch, recursive: true);
        }
    }

}
