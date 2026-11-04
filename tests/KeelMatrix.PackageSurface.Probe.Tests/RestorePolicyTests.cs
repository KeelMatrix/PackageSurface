using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class RestorePolicyTests
{
    [Theory]
    [InlineData(".NETCoreApp,Version=v8.0", "net8.0")]
    [InlineData("NET8.0", "net8.0")]
    [InlineData(".NETStandard,Version=v2.0", "netstandard2.0")]
    [InlineData(".NETFramework,Version=v4.7.2", "net472")]
    public void FrameworkIdentityUsesCanonicalMonikers(string source, string expected)
    {
        Assert.Equal(expected, RestoreIdentityNormalization.NormalizeFrameworkMoniker(source));
    }

    [Theory]
    [InlineData("[1, 1]", "1.0.0", true)]
    [InlineData("[1.0, 2.0)", "1.9.0", true)]
    [InlineData("[1.0, 2.0)", "2.0.0", false)]
    [InlineData("(1.0, 2.0]", "1.0.0", false)]
    [InlineData("(1.0, 2.0]", "2.0.0", true)]
    [InlineData("[1.0.0-alpha.1]", "1.0.0-ALPHA.1", true)]
    [InlineData("[1.0.0, )", "1.0.0-alpha.1", false)]
    public void RequirementsMatchNugetVersionsAndRangeBoundaries(string text, string version, bool expected)
    {
        Assert.True(DependencyRequirements.TryParsePackageVersionRange(text, out var requirement));
        Assert.Equal(expected, requirement.Matches(version));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("6.*")]
    [InlineData("(,)")]
    [InlineData(">= 1.0.0")]
    [InlineData("not-a-version-range")]
    public void UnsupportedRequirementsFailClosed(string text)
    {
        Assert.False(DependencyRequirements.TryParsePackageVersionRange(text, out _));
    }
}
