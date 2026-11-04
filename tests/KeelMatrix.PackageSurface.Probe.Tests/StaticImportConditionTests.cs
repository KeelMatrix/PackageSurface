using Xunit;
using static KeelMatrix.PackageSurface.Probe.StaticImportCondition;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class StaticImportConditionTests
{
    [Theory]
    [InlineData("'$(TargetFramework)' == 'net8.0'", "net8.0", true)]
    [InlineData("'$(TargetFramework)' == '.NETCoreApp,Version=v8.0'", "net8.0", false)]
    [InlineData("'$(TargetFramework)' != '.NETCoreApp,Version=v8.0'", "net8.0", true)]
    [InlineData("'$(TargetFramework)' == '$(TargetFramework)'", "net8.0", true)]
    [InlineData("'$(TargetFramework)' == 'net8.0' OR '$(TargetFramework)' == 'net9.0'", "net9.0", true)]
    [InlineData("'$(TargetFramework)' == 'net8.0' AND '$(TargetFramework)' == 'net9.0'", "net8.0", false)]
    [InlineData("\"$( TargetFramework )\" == \"net8.0\" AND ('$(TargetFramework)' != 'net9.0' OR '$(TargetFramework)' == 'net8.0')", "net8.0", true)]
    [InlineData("'$(ExcludeRestorePackageImports)' != 'true'", "net8.0", true)]
    public void StaticGrammarUsesMsbuildStringAndBooleanSemantics(string text, string framework, bool expected)
    {
        var parsed = ParseCondition(text, _ => throw new InvalidOperationException("Static comparisons must not read the filesystem."));

        Assert.True(parsed.IsKnown, parsed.Reason);
        Assert.Equal(expected, parsed.Node!.Evaluate(SurfaceContextKind.Target, framework));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ExistsConsumesOnlyTheSuppliedFilesystemProof(bool exists)
    {
        var paths = new List<string>();
        var parsed = ParseCondition("Exists('package/file.props')", path =>
        {
            paths.Add(path);
            return ParsedCondition.Known(new ConstantCondition(exists));
        });

        Assert.Equal("package/file.props", Assert.Single(paths));
        Assert.True(parsed.IsKnown);
        Assert.Equal(exists, parsed.Node!.Evaluate(SurfaceContextKind.Target, "net8.0"));
    }

    [Theory]
    [InlineData("Exists('unproven')")]
    [InlineData("'$(TargetFramework)' == 'net8.0' OR Exists('unproven')")]
    [InlineData("'$(TargetFramework)' != 'net8.0' AND Exists('unproven')")]
    [InlineData("'$(TargetFramework)' == '$(UnsupportedProperty)'")]
    [InlineData("'$(UnsupportedProperty)' == 'value'")]
    [InlineData("()")]
    public void UnprovenConditionsRemainUnknownEvenInBooleanBranches(string text)
    {
        var parsed = ParseCondition(text, _ => ParsedCondition.Unknown("filesystem proof unavailable"));

        Assert.False(parsed.IsKnown);
        Assert.NotNull(parsed.Reason);
    }

    [Theory]
    [InlineData(64, true)]
    [InlineData(65, false)]
    public void ParenthesisDepthIsBounded(int depth, bool expected)
    {
        var text = new string('(', depth) + "'$(TargetFramework)' == 'net8.0'" + new string(')', depth);
        var parsed = ParseCondition(text, _ => throw new InvalidOperationException());

        Assert.Equal(expected, parsed.IsKnown);
    }

    [Theory]
    [InlineData(65536, true)]
    [InlineData(65537, false)]
    public void ConditionLengthIsBounded(int length, bool expected)
    {
        var text = "'$(TargetFramework)' == 'net8.0'".PadRight(length);
        var parsed = ParseCondition(text, _ => throw new InvalidOperationException());

        Assert.Equal(expected, parsed.IsKnown);
    }
}
