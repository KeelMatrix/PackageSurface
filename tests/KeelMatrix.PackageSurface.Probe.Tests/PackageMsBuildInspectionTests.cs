using Xunit;

namespace KeelMatrix.PackageSurface.Probe.Tests;

public sealed class PackageMsBuildInspectionTests
{
    [Theory]
    [InlineData("<Project><Target Name='Build'><Exec Command='never-executed' /></Target></Project>", "Exec")]
    [InlineData("<Project><UsingTask TaskName='Task' AssemblyFile='never-loaded.dll' /></Project>", "UsingTask")]
    [InlineData("<Project><Import Project='never-evaluated.props' /></Project>", "Import")]
    public void PrimitivesAreObservedFromXmlNodes(string xml, string expected)
    {
        using var workspace = new ProbeScenarioWorkspace();
        var path = Path.Combine(workspace.Root, "observed.targets");
        File.WriteAllText(path, xml);

        Assert.Equal(new[] { expected }, PackageMsBuildXml.InspectMsBuildXml(path));
    }

    [Fact]
    public void PrimitiveNamesInsideTextAreNotExecutableNodes()
    {
        using var workspace = new ProbeScenarioWorkspace();
        var path = Path.Combine(workspace.Root, "text.targets");
        File.WriteAllText(path, "<Project><PropertyGroup><Text>Exec UsingTask Import</Text></PropertyGroup></Project>");

        Assert.Empty(PackageMsBuildXml.InspectMsBuildXml(path));
    }
}
