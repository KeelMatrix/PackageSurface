using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class SurfaceEntryFixture
{
    internal static SurfaceEntry Entry(CapabilityKind capability, string path, string hash, bool active = true, bool present = true) => new(
        "net8.0",
        capability == CapabilityKind.NativeRuntime ? "win-x64" : null,
        SurfaceContextKind.Target,
        "Example.Package",
        "1.0.0",
        "direct",
        capability,
        path,
        present,
        active,
        hash,
        false,
        null,
        "Example.csproj");
}
