using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class ImportConditionTests
{
    [Theory]
    [InlineData("build/Helper.targets")]
    [InlineData("buildTransitive/Transitive.targets")]
    [InlineData("buildTransitive/TransitiveHelper.props")]
    [InlineData("buildMultiTargeting/Outer.props")]
    [InlineData("buildMultiTargeting/OuterHelper.targets")]
    public void NestedImportPhasesRemainDistinct(string path)
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var result = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(result.IsComplete, $"Nested phase matrix did not classify completely: {string.Join(" | ", result.IncompleteReasons)}");

        var entries = result.Entries.Where(candidate => candidate.PackageRelativePath.Equals(path, StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.True(entries.Length > 0 && entries.All(entry => entry.Active && entry.Sha256 is not null), $"Nested phase matrix descendant '{path}' was not active and fingerprinted.");

    }

    [Fact]
    public void FrameworkLiteralsUseStringSemantics()
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' != '.NETCoreApp,Version=v8.0'\" /></Project>");
        var literalAlias = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(literalAlias.IsComplete && literalAlias.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A literal framework alias was normalized instead of compared with MSBuild string semantics: entries=" + string.Join(" | ", literalAlias.Entries.Select(entry => $"{entry.PackageRelativePath}:{entry.Active}")) + " reasons=" + string.Join(" | ", literalAlias.IncompleteReasons));


    }

    [Fact]
    public void RightHandPropertyReferenceIsResolved()
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(TargetFramework)'\" /></Project>");
        var propertyReference = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(propertyReference.IsComplete && propertyReference.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "A supported property reference on the right side of a condition was not resolved.");


    }

    [Fact]
    public void QuotedBooleanConditionsAreResolved()
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"&quot;$( TargetFramework )&quot; == &quot;net8.0&quot; AND ('$(TargetFramework)' != 'net9.0' OR '$(TargetFramework)' == 'net8.0')\" /></Project>");
        var quotedBoolean = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(quotedBoolean.IsComplete && quotedBoolean.Entries.Any(entry => entry.PackageRelativePath.Equals("build/Helper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "Quoted or nested AND/OR TargetFramework conditions were not evaluated with the supported string grammar.");


    }

    [Fact]
    public void OuterBuildHasIndependentFrameworkContext()
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        var outerProps = Path.Combine(packageRoot, "buildMultiTargeting", "Outer.props");
        File.WriteAllText(outerProps, "<Project><Import Project=\"OuterHelper.targets\" Condition=\"'$(TargetFramework)' == ''\" /></Project>");
        var outerBuild = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(outerBuild.IsComplete && outerBuild.Entries.Any(entry => entry.PackageRelativePath.Equals("buildMultiTargeting/OuterHelper.targets", StringComparison.OrdinalIgnoreCase) && entry.Active),
            "Outer-build TargetFramework condition context was not evaluated independently from target-build context.");


    }

    [Fact]
    public void UnsupportedExpansionRemainsUnknown()
    {
        using var fixture = new NestedImportFixture();
        var root = fixture.Root;
        var assets = fixture.AssetsPath;
        var packageRoot = fixture.PackageRoot;
        var rootProps = Path.Combine(packageRoot, "build", "Root.props");
        File.WriteAllText(Path.Combine(packageRoot, "build", "Helper.targets"), "<Project />");
        File.WriteAllText(rootProps, "<Project><Import Project=\"Helper.targets\" Condition=\"'$(TargetFramework)' == '$(UnsupportedConditionProperty)'\" /></Project>");
        var unsupportedExpansion = ResolvedGraphClassifier.Analyze(assets, root, strictContent: true);
        Assert.True(!unsupportedExpansion.IsComplete && unsupportedExpansion.IncompleteReasons.Any(reason => reason.Contains("unsupported property expansion", StringComparison.OrdinalIgnoreCase)),
            "An unsupported condition property expansion was treated as a known false branch.");

    }

}
