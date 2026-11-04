using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.IncompleteAnalysisAssertions;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class PackageXmlAssertions
{
    internal static void AssertPackageXmlVariantFailure(
        string assets,
        string projectRoot,
        string baseline,
        string physicalPath,
        string canonical,
        string label,
        string oldText,
        string newText)
    {
        var variant = canonical.Replace(oldText, newText, StringComparison.Ordinal);
        Assert.True(!variant.Equals(canonical, StringComparison.Ordinal), $"The XML regression variant '{label}' was not applied.");
        File.WriteAllText(physicalPath, variant);
        try
        {
            AssertRestoreIdentityFailure(assets, projectRoot, baseline, label);
        }
        finally
        {
            File.WriteAllText(physicalPath, canonical);
        }
    }

    internal static void AssertPackageXmlDuplicateAttributeFailure(
        string assets,
        string projectRoot,
        string baseline,
        string physicalPath,
        string canonical,
        string name,
        string canonicalAttribute,
        string alias)
    {
        var marker = " " + canonicalAttribute + "=\"";
        var markerIndex = canonical.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(markerIndex >= 0, $"The XML duplicate variant '{name}' could not find its canonical attribute.");
        var valueEnd = canonical.IndexOf('"', markerIndex + marker.Length);
        Assert.True(valueEnd >= 0, $"The XML duplicate variant '{name}' has no complete canonical attribute value.");
        var variant = canonical.Insert(valueEnd + 1, " " + alias + "=\"duplicate\"");
        File.WriteAllText(physicalPath, variant);
        try
        {
            AssertRestoreIdentityFailure(assets, projectRoot, baseline, "attribute-" + name + "-duplicate");
        }
        finally
        {
            File.WriteAllText(physicalPath, canonical);
        }
    }
}
