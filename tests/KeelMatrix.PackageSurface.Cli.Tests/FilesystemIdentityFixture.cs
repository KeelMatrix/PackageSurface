using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;
using static KeelMatrix.PackageSurface.Cli.Tests.SurfaceEntryFixture;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class FilesystemIdentityFixture : IDisposable
{
    private readonly CliScenarioWorkspace workspace = new();
    internal FilesystemIdentityFixture(FileSystemCaseSensitivity processMode, FileSystemCaseSensitivity projectMode, FileSystemCaseSensitivity packageMode)
    {
        var scratch = workspace.Root;
        var singleBuildAsset = new List<string> { "build/Case.targets" };

        var scenario = $"cwd-{processMode}-project-{projectMode}-cache-{packageMode}";
        var root = Path.Combine(scratch, scenario);
        var workingRoot = Path.Combine(root, "working");
        var projectRoot = Path.Combine(root, "project");
        var packageCache = Path.Combine(root, "packages");
        var obj = Path.Combine(projectRoot, "obj");
        Directory.CreateDirectory(workingRoot);
        Directory.CreateDirectory(obj);

        var assets = Path.Combine(obj, "project.assets.json");
        WriteAssets(assets, packageCache, "Case.Package", singleBuildAsset, createFiles: true);
        var assetDocument = JsonNode.Parse(File.ReadAllText(assets))!.AsObject();
        assetDocument["targets"]!["net8.0"]!["Case.Package/1.0.0"]!["build"] = new JsonObject
        {
            ["build/Case.targets"] = new JsonObject()
        };
        File.WriteAllText(assets, assetDocument.ToJsonString());

        var packageAsset = Path.Combine(packageCache, "Case.Package", "1.0.0", "build", "Case.targets");
        File.WriteAllText(packageAsset, "<Project />");
        var generatedTargets = Path.Combine(obj, "Test.csproj.nuget.g.targets");
        var rootOverrides = new Dictionary<string, FileSystemCaseSensitivity>(StringComparer.Ordinal)
        {
            [workingRoot] = processMode,
            [projectRoot] = projectMode,
            [packageCache] = packageMode
        };


        WriteCaseImport(generatedTargets, packageAsset);
        Root = root; WorkingRoot = workingRoot; ProjectRoot = projectRoot; PackageCache = packageCache;
        AssetsPath = assets; PackageAsset = packageAsset; GeneratedTargets = generatedTargets; RootOverrides = rootOverrides;
    }
    internal string Root { get; }
    internal string WorkingRoot { get; }
    internal string ProjectRoot { get; }
    internal string PackageCache { get; }
    internal string AssetsPath { get; }
    internal string PackageAsset { get; }
    internal string GeneratedTargets { get; }
    internal Dictionary<string, FileSystemCaseSensitivity> RootOverrides { get; }
    public void Dispose() => workspace.Dispose();
    internal static void WriteCaseImport(string path, string existsPath)
    {
        File.WriteAllText(path,
            $"<Project><Import Project=\"$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets\" Condition=\"Exists('{existsPath}')\" /></Project>");
    }

    internal static void WriteDuplicateBaseline(string path, Action<JsonObject> alterDuplicate)
    {
        var entry = Entry(CapabilityKind.BuildTargets, "build/Case.targets", new string('a', 64)) with
        {
            PackageId = "Case.Package",
            Project = "Test.csproj"
        };
        BaselineDocument.Write(path, new SurfaceSnapshot(new[] { entry }, Array.Empty<string>(), false, 1));
        var document = JsonNode.Parse(File.ReadAllText(path))!.AsObject();
        var duplicate = document["entries"]!.AsArray()[0]!.DeepClone()!.AsObject();
        alterDuplicate(duplicate);
        document["entries"]!.AsArray().Add(duplicate);
        File.WriteAllText(path, document.ToJsonString());
    }

}
