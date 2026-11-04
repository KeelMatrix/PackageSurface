using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PackageContainmentTests
{
    [Fact]

    public void ReparsePointsAreRejected()

    {

        using var workspace = new CliScenarioWorkspace();

        var scratch = workspace.Root;



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

            Assert.True(!result.IsComplete && result.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),

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

            Assert.True(!rootLinkResult.IsComplete && rootLinkResult.Entries.All(entry => entry.Sha256 is null),

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

            Assert.True(!interiorResult.IsComplete && interiorResult.IncompleteReasons.Any(reason => reason.Contains("unsafe package path", StringComparison.OrdinalIgnoreCase)),

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

            Assert.True(ancestorResult.IsComplete, "A reparse point outside the resolved package root was treated as an unsafe package path: " + string.Join(" | ", ancestorResult.IncompleteReasons));

            Console.WriteLine($"VERIFIED: ancestor reparse-point regression exercised a real directory link; link='{ancestorLink}'; target='{ancestorTarget}'");

        }

    }


    [Theory]
    [InlineData("mixed-case-separators", "leaf")]
    [InlineData("mixed-case-separators", "package-root")]
    [InlineData("mixed-case-separators", "interior-directory")]
    [InlineData("mixed-case-separators", "ancestor")]
    [InlineData("unicode-separators", "leaf")]
    [InlineData("unicode-separators", "package-root")]
    [InlineData("unicode-separators", "interior-directory")]
    [InlineData("unicode-separators", "ancestor")]
    public void ReparsePathVariantsAreRejected(string caseName, string state)
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;
        var variants = new[]

        {

        (Name: "mixed-case-separators", PackageId: "Case.Matrix", File: "BUILD\\Inside.TARGETS"),

        (Name: "unicode-separators", PackageId: "Unicode.包", File: "build\\内部.targets")

    };
        var variant = variants.Single(value => value.Name == caseName);


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

            catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); return; }

            catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); return; }

            catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); return; }

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

        catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); return; }

        catch (IOException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this filesystem."); return; }

        catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: reparse matrix {variant.Name}/{state} is unavailable on this platform."); return; }



        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);

        if (state == "ancestor")

        {

            Assert.True(result.IsComplete, $"An ancestor reparse point was rejected for {variant.Name}.");

        }

        else

        {

            Assert.True(!result.IsComplete && result.Entries.All(entry => entry.Sha256 is null),

                $"The {state} reparse point was not rejected for {variant.Name}.");

        }


    }
}
