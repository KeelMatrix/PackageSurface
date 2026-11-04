using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.FilesystemFixture;


namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class BaselineAliasTests
{
    [Fact]
    public void ReachablePackageAliasesAreRejected()
    {
        using var fixture = new ReachablePackageFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var files = fixture.Files;
        var packageBytes = fixture.Bytes;
        var transitiveFile = fixture.TransitiveFile;
        foreach (var relative in files)
        {
            var packageFile = Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar));
            var lexicalAlias = Path.Combine(Path.GetDirectoryName(packageFile)!, ".", Path.GetFileName(packageFile));
            Assert.True(CaptureCommand("baseline", assets, "--output", lexicalAlias, "--no-telemetry").ExitCode == 2,
                $"A lexical alias to reachable package input '{relative}' was accepted.");
            Assert.True(packageBytes[relative].SequenceEqual(File.ReadAllBytes(packageFile)),
                $"A lexical package-input alias attempt changed '{relative}'.");
        }
        var transitiveAlias = Path.Combine(Path.GetDirectoryName(transitiveFile)!, ".", Path.GetFileName(transitiveFile));
        var transitiveBefore = File.ReadAllBytes(transitiveFile);
        Assert.True(CaptureCommand("baseline", assets, "--output", transitiveAlias, "--no-telemetry").ExitCode == 2,
            "A lexical alias to a reachable transitive package input was accepted.");
        Assert.True(transitiveBefore.SequenceEqual(File.ReadAllBytes(transitiveFile)),
            "A transitive package-input alias attempt changed the package file.");

        var hardlinkOutputs = new[] { Path.Combine(root, "package-hardlink.json"), Path.Combine(root, "package-hardlink-second.json") };
        var representative = Path.Combine(packageRoot, "tools", "tool.ps1");
        Assert.True(TryCreateHardLink(representative, hardlinkOutputs[0]), "The package hardlink alias regression could not create a hardlink.");
        Assert.True(TryCreateHardLink(representative, hardlinkOutputs[1]), "The package multiple-hardlink alias regression could not create a second hardlink.");
        try
        {
            foreach (var output in hardlinkOutputs)
            {
                Assert.True(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                    "A hardlink alias to a reachable package input was accepted.");
                Assert.True(packageBytes["tools/tool.ps1"].SequenceEqual(File.ReadAllBytes(representative)),
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


    }

    [Fact]
    public void FileSymlinkCannotReplacePackageInput()
    {
        using var fixture = new ReachablePackageFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var files = fixture.Files;
        var packageBytes = fixture.Bytes;
        var transitiveFile = fixture.TransitiveFile;
        var packageFile = Path.Combine(packageRoot, "tools", "tool.ps1");
        var before = packageBytes["tools/tool.ps1"];
        var label = "direct";

        var output = Path.Combine(root, "package-" + label + "-symlink.json");
        try
        {
            File.CreateSymbolicLink(output, packageFile);
            Assert.True(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                $"A {label} symlink alias to a reachable package input was accepted.");
            Assert.True(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A {label} symlink alias attempt changed the package input.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label} symlink-alias regression is unavailable on this platform."); }
        finally
        {
            if (File.Exists(output)) File.Delete(output);
        }

    }

    [Theory]
    [InlineData("interior")]
    [InlineData("ancestor")]
    public void DirectorySymlinkCannotReplacePackageInput(string label)
    {
        using var fixture = new ReachablePackageFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var files = fixture.Files;
        var packageBytes = fixture.Bytes;
        var transitiveFile = fixture.TransitiveFile;
        var packageFile = Path.Combine(packageRoot, "tools", "tool.ps1");
        var before = packageBytes["tools/tool.ps1"];
        var target = label == "interior" ? Path.GetDirectoryName(packageFile)! : root;

        var link = Path.Combine(root, "package-" + label + "-link");
        var output = Path.Combine(link, "tool.ps1");
        try
        {
            Directory.CreateSymbolicLink(link, target);
            Assert.True(CaptureCommand("baseline", assets, "--output", output, "--no-telemetry").ExitCode == 2,
                $"A package {label}-directory reparse alias was accepted.");
            Assert.True(before.SequenceEqual(File.ReadAllBytes(packageFile)), $"A package {label}-directory alias attempt changed the package input.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine($"UNVERIFIED: package {label}-directory reparse regression is unavailable on this platform."); }

    }

    [Fact]
    public void ReachableInputInventoryAllowsDistinctOutput()
    {
        using var fixture = new ReachablePackageFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var files = fixture.Files;
        var transitiveFile = fixture.TransitiveFile;

        var reachable = ResolvedGraphClassifier.GetReachablePackageInputPaths(assets)
            .Select(Path.GetFullPath)
            .ToHashSet(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        Assert.True(files.All(relative => reachable.Contains(Path.GetFullPath(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar))))),
            "The preflight package-input set omitted a reachable package inventory file or fallback-root asset.");
        Assert.True(reachable.Contains(Path.GetFullPath(transitiveFile)),
            "The preflight package-input set omitted a reachable transitive package asset from the RID graph.");

        var seed = Path.Combine(root, "seed.json");
        Assert.True(CaptureCommand("baseline", assets, "--output", seed, "--no-telemetry").ExitCode == 0,
            "The reachable package alias fixture could not create a valid baseline seed.");
        var distinct = Path.Combine(root, "distinct-output.json");

        Assert.True(CaptureCommand("baseline", assets, "--output", distinct, "--no-telemetry").ExitCode == 0,
            "A genuinely distinct baseline output was rejected while package inputs were present.");

    }
    [Fact]
    public void AssetsOutputAliasPreservesRestoreDocument()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var aliasedAssets = File.ReadAllBytes(assets);
        Assert.True(CaptureCommand("baseline", assets, "--output", assets, "--no-telemetry").ExitCode == 2,
            "A baseline output equal to project.assets.json was accepted.");
        Assert.True(aliasedAssets.SequenceEqual(File.ReadAllBytes(assets)), "An aliased baseline attempt changed project.assets.json.");


    }
    [Fact]
    public void HardlinkOutputAliasesPreserveRestoreDocument()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var aliasedAssets = File.ReadAllBytes(assets);
        var hardlinkOutputs = new[]
        {
        Path.Combine(root, "hardlink-output.json"),
        Path.Combine(root, "hardlink-output-second.json")
    };
        Assert.True(TryCreateHardLink(assets, hardlinkOutputs[0]), "The hardlink alias regression could not create a hardlink.");
        Assert.True(TryCreateHardLink(assets, hardlinkOutputs[1]), "The multiple-hardlink alias regression could not create a second hardlink.");
        try
        {
            foreach (var hardlinkOutput in hardlinkOutputs)
            {
                Assert.True(CaptureCommand("baseline", assets, "--output", hardlinkOutput, "--no-telemetry").ExitCode == 2,
                    "A hardlink alias to project.assets.json was accepted.");
                Assert.True(aliasedAssets.SequenceEqual(File.ReadAllBytes(assets)), "A hardlink alias rejection changed project.assets.json.");
            }
        }
        finally
        {
            foreach (var hardlinkOutput in hardlinkOutputs)
            {
                if (File.Exists(hardlinkOutput)) File.Delete(hardlinkOutput);
            }
        }


    }
    [Fact]
    public void SymlinkOutputAliasPreservesRestoreDocument()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var symlinkOutput = Path.Combine(root, "symlink-output.json");
        try
        {
            File.CreateSymbolicLink(symlinkOutput, assets);
            var symlinkInputBefore = File.ReadAllBytes(assets);
            Assert.True(CaptureCommand("baseline", assets, "--output", symlinkOutput, "--no-telemetry").ExitCode == 2,
                "A symlink alias to project.assets.json was accepted.");
            Assert.True(symlinkInputBefore.SequenceEqual(File.ReadAllBytes(assets)), "A symlink alias rejection changed project.assets.json.");
        }
        catch (UnauthorizedAccessException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this filesystem."); }
        catch (IOException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this filesystem."); }
        catch (PlatformNotSupportedException) { Console.WriteLine("UNVERIFIED: baseline symlink-alias regression is unavailable on this platform."); }
        finally
        {
            if (File.Exists(symlinkOutput)) File.Delete(symlinkOutput);
        }


    }
    [Fact]
    public void GeneratedImportOutputAliasPreservesRestoreDocument()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var aliasedGenerated = Path.Combine(obj, "Test.csproj.nuget.g.targets");
        var generatedBefore = File.ReadAllBytes(aliasedGenerated);
        Assert.True(CaptureCommand("baseline", assets, "--output", Path.Combine(obj, ".", "Test.csproj.nuget.g.targets"), "--no-telemetry").ExitCode == 2,
            "A baseline output equal to a generated restore import was accepted.");
        Assert.True(generatedBefore.SequenceEqual(File.ReadAllBytes(aliasedGenerated)), "An aliased baseline attempt changed generated restore evidence.");


    }
    [Fact]
    public void SelectedProjectOutputAliasPreservesProject()
    {
        using var fixture = new BaselineWorkspace();
        var root = fixture.Root;
        var obj = fixture.Obj;
        var assets = fixture.AssetsPath;
        var baseline = fixture.BaselinePath;
        var approved = fixture.Approved;
        var seedResult = fixture.SeedResult;
        Assert.True(seedResult.ExitCode == 0, "The transaction baseline seed did not succeed.");
        var projectInput = Path.Combine(root, "Test.csproj");
        File.WriteAllText(projectInput, "<Project />");
        var projectBefore = File.ReadAllBytes(projectInput);
        Assert.True(CaptureCommand("baseline", assets, "--output", Path.Combine(root, ".", "Test.csproj"), "--no-telemetry").ExitCode == 2,
            "A baseline output equal to the selected project was accepted.");
        Assert.True(projectBefore.SequenceEqual(File.ReadAllBytes(projectInput)), "A project-alias rejection changed the project.");


    }
}
