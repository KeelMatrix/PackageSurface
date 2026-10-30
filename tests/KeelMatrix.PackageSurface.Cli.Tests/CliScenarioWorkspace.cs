using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class CliScenarioWorkspace : IDisposable
{
    public CliScenarioWorkspace()
    {
        Root = Path.Combine(Path.GetTempPath(), "packagesurface-discovered-tests-" + Guid.NewGuid().ToString("N"));
        Cache = Path.Combine(Root, "cache");
        Obj = Path.Combine(Root, "obj");
        AssetsPath = Path.Combine(Obj, "project.assets.json");
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Obj);
        ScenarioSupport.WriteAssets(AssetsPath, Cache, "XmlPackage", Array.Empty<string>());
        File.WriteAllText(Path.Combine(Root, "Test.csproj"), "<Project />");
    }

    public string Root { get; }
    public string Cache { get; }
    public string Obj { get; }
    public string AssetsPath { get; }

    public void Dispose()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
