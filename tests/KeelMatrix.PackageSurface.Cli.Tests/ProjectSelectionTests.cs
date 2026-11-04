using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;
using Xunit;
using static KeelMatrix.PackageSurface.Cli.Tests.RestoreDocumentFixture;

namespace KeelMatrix.PackageSurface.Cli.Tests;

public sealed class ProjectSelectionTests
{
    [Fact]
    public void ProjectSelectionValidatesInputAndMembership()
    {
        using var workspace = new CliScenarioWorkspace();
        var scratch = workspace.Root;
        var project = Path.Combine(scratch, "Selected.csproj");
        File.WriteAllText(project, "<Project />");
        Assert.True(CommandLine.Run(new[] { "scan", Path.Combine(scratch, "missing.sln"), "--project", project, "--no-telemetry" }) == 2,
            "--project bypassed validation of a missing primary input.");

        var manyProjects = Path.Combine(scratch, "many-projects");
        Directory.CreateDirectory(manyProjects);
        for (var index = 0; index < 129; index++)
        {
            File.WriteAllText(Path.Combine(manyProjects, $"Project{index:000}.csproj"), "<Project />");
        }

        var selectedAssets = Path.Combine(manyProjects, "obj", "project.assets.json");
        Directory.CreateDirectory(Path.GetDirectoryName(selectedAssets)!);
        WriteAssets(selectedAssets, Path.Combine(manyProjects, "cache"), "Narrowed.Package", Array.Empty<string>(), projectFileName: "Project000.csproj");
        File.WriteAllText(Path.Combine(manyProjects, "Project000.csproj"), "<Project />");
        Assert.True(CommandLine.Run(new[] { "scan", manyProjects, "--project", Path.Combine(manyProjects, "Project000.csproj"), "--no-telemetry" }) == 0,
            "--project did not narrow a directory with more than 128 members.");

        var nonMember = Path.Combine(scratch, "not-a-member.csproj");
        File.WriteAllText(nonMember, "<Project />");
        Assert.True(CommandLine.Run(new[] { "scan", manyProjects, "--project", nonMember, "--no-telemetry" }) == 2,
            "--project accepted a project outside a large directory input.");


    }

}
