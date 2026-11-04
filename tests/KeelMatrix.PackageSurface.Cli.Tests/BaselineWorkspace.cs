using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using static KeelMatrix.PackageSurface.Cli.Tests.CliInvocation;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal sealed class BaselineWorkspace : IDisposable
{
    private readonly CliScenarioWorkspace workspace;
    internal BaselineWorkspace()
    {

        workspace = new CliScenarioWorkspace();
        var parent = workspace.Root;

        var root = Path.Combine(parent, "baseline-transaction");
        var cache = Path.Combine(root, "cache");
        var obj = Path.Combine(root, "obj");
        Directory.CreateDirectory(obj);
        var assets = Path.Combine(obj, "project.assets.json");
        var baseline = Path.Combine(root, "approved.json");
        WriteAssets(assets, cache, "Transaction.Package", Array.Empty<string>());
        var seedResult = CaptureCommand("baseline", assets, "--output", baseline, "--no-telemetry");
        var approved = File.ReadAllBytes(baseline);


        Root = root; Obj = obj; AssetsPath = assets; BaselinePath = baseline; Approved = approved; SeedResult = seedResult;
    }
    internal string Root { get; }
    internal string Obj { get; }
    internal string AssetsPath { get; }
    internal string BaselinePath { get; }
    internal byte[] Approved { get; }
    internal (int ExitCode, string Output) SeedResult { get; }
    public void Dispose() => workspace.Dispose();

}
