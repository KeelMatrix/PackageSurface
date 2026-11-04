using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class PackageMsBuildFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal PackageMsBuildFixture()
    {

        workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;

        var root = Path.Combine(scratch, "package-xml-schema");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var packageId = "Package.Xml.Schema";
        var packagePath = "build/Package.Xml.Schema.targets";
        WriteAssets(assets, cache, packageId, new[] { packagePath }, createFiles: true);
        var document = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        document["targets"]!["net8.0"]![packageId + "/1.0.0"]!["build"] = new JsonObject { [packagePath] = new JsonObject() };
        File.WriteAllText(assets, document.ToJsonString());

        var physicalPath = Path.Combine(cache, packageId, "1.0.0", packagePath.Replace('/', Path.DirectorySeparatorChar));
        var canonical = """
<Project ToolsVersion="Current">
  <PropertyGroup Label="standard">
    <SchemaValue>1</SchemaValue>
  </PropertyGroup>
  <ItemGroup Label="standard">
    <None Include="content.txt" />
    <None Update="content.txt" />
    <None Remove="obsolete.txt" />
  </ItemGroup>
  <Target Name="Standard" BeforeTargets="Build" AfterTargets="Build" DependsOnTargets="CoreCompile" Inputs="$(ProjectFile)" Outputs="$(TargetPath)" Returns="$(TargetPath)" KeepDuplicateOutputs="true" Label="standard" />
  <UsingTask TaskName="Inline" TaskFactory="CodeTaskFactory" AssemblyFile="$(MSBuildToolsPath)/Microsoft.Build.Tasks.Core.dll" Condition="'$(TargetFramework)' == 'never'">
    <Task>
      <Code Type="Fragment" Language="cs">Log.LogMessage("never executed");</Code>
    </Task>
  </UsingTask>
  <Target Name="NeverExecuted">
    <Exec Command="echo never-executed" Condition="'$(TargetFramework)' == 'never'" />
  </Target>
  <ImportGroup Label="standard">
    <Import Project="helpers/Helper.targets" Condition="'$(TargetFramework)' == 'never'" Label="standard" />
  </ImportGroup>
</Project>
""";
        File.WriteAllText(physicalPath, canonical);
        File.WriteAllText(Path.Combine(obj, "Test.csproj.nuget.g.targets"), $"<Project><Import Project=\"$(NuGetPackageRoot)/{packageId}/1.0.0/{packagePath}\" /></Project>");

        var baseline = Path.Combine(root, "baseline.json");

        Root = root; AssetsPath = assets; PhysicalPath = physicalPath; Canonical = canonical; BaselinePath = baseline;
    }
    internal string Root { get; }
    internal string AssetsPath { get; }
    internal string PhysicalPath { get; }
    internal string Canonical { get; }
    internal string BaselinePath { get; }
    public void Dispose() => workspace.Dispose();

}
