using System.Text.Json.Nodes;

namespace KeelMatrix.PackageSurface.Probe.Tests;

internal sealed class ProbeScenarioWorkspace : IDisposable
{
    public ProbeScenarioWorkspace(IReadOnlyList<string>? files = null, bool createFiles = false)
    {
        Root = Path.Combine(Path.GetTempPath(), "packagesurface-probe-tests-" + Guid.NewGuid().ToString("N"));
        Cache = Path.Combine(Root, "cache");
        Obj = Path.Combine(Root, "obj");
        AssetsPath = Path.Combine(Obj, "project.assets.json");
        Directory.CreateDirectory(Cache);
        Directory.CreateDirectory(Obj);
        Directory.CreateDirectory(Path.Combine(Cache, "Example.Package", "1.0.0"));
        if (createFiles)
        {
            foreach (var file in files ?? Array.Empty<string>())
            {
                var path = Path.Combine(Cache, "Example.Package", "1.0.0", file.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, "fixture");
            }
        }

        var packageKey = "Example.Package/1.0.0";
        var fileEntries = new JsonArray();
        foreach (var file in files ?? Array.Empty<string>())
        {
            fileEntries.Add(file);
        }
        var root = new JsonObject
        {
            ["version"] = 3,
            ["targets"] = new JsonObject { ["net8.0"] = new JsonObject { [packageKey] = new JsonObject() } },
            ["libraries"] = new JsonObject
            {
                [packageKey] = new JsonObject { ["type"] = "package", ["path"] = packageKey, ["files"] = fileEntries }
            },
            ["packageFolders"] = new JsonObject { [Cache] = new JsonObject() },
            ["project"] = new JsonObject
            {
                ["restore"] = new JsonObject { ["projectPath"] = Path.Combine(Root, "Test.csproj") },
                ["frameworks"] = new JsonObject
                {
                    ["net8.0"] = new JsonObject
                    {
                        ["dependencies"] = new JsonObject
                        {
                            ["Example.Package"] = new JsonObject { ["version"] = "[1.0.0, )", ["target"] = "Package" }
                        }
                    }
                }
            }
        };
        File.WriteAllText(AssetsPath, root.ToJsonString());
        File.WriteAllText(Path.Combine(Root, "Test.csproj"), "<Project />");
        File.WriteAllText(Path.Combine(Obj, "Test.csproj.nuget.g.props"), "<Project />");
        File.WriteAllText(Path.Combine(Obj, "Test.csproj.nuget.g.targets"), "<Project />");
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
