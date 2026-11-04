using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class FilesystemFixture
{
    internal static bool TryCreateHardLink(string existingPath, string linkPath) =>
        OperatingSystem.IsWindows()
            ? NativeMethods.CreateHardLink(linkPath, existingPath, IntPtr.Zero)
            : NativeMethods.Link(existingPath, linkPath) == 0;
}
