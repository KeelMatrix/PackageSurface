using System.Reflection;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;


namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class PlatformPathTests
{
    [Fact]
    public void MacosRootAliasPredicateIsStable()
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
            Assert.True((bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, true })!,
                "The standard macOS /var to /private/var alias was rejected.");

            var callerTarget = Path.Combine(privateRoot, "caller-alias");
            var callerLink = Path.Combine(fakeRoot, "caller-alias");
            Directory.CreateDirectory(callerTarget);
            Directory.CreateSymbolicLink(callerLink, callerTarget);
            Assert.True(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(callerLink), fakeRoot, true })!,
                "A caller-controlled root-level /private/<name> alias was accepted as system-owned.");

            var nestedTarget = Path.Combine(privateRoot, "nested");
            var nestedLink = Path.Combine(standardLink, "nested");
            Directory.CreateDirectory(nestedTarget);
            Directory.CreateSymbolicLink(nestedLink, nestedTarget);
            Assert.True(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(nestedLink), fakeRoot, true })!,
                "A nested link below the standard /var alias was accepted as system-owned.");
            Assert.True(!(bool)method.Invoke(null, new object[] { new DirectoryInfo(standardLink), fakeRoot, false })!,
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
