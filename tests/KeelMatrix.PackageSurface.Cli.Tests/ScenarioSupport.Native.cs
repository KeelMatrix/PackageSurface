using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface.Cli.Tests;


#pragma warning disable CA2101
static class NativeMethods
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CreateHardLink(
        [MarshalAs(UnmanagedType.LPWStr)] string fileName,
        [MarshalAs(UnmanagedType.LPWStr)] string existingFileName,
        IntPtr securityAttributes);

    [DllImport("libc", EntryPoint = "link", CharSet = CharSet.Ansi, SetLastError = true)]
    public static extern int Link(
        [MarshalAs(UnmanagedType.LPStr)] string existingPath,
        [MarshalAs(UnmanagedType.LPStr)] string linkPath);
}


#pragma warning restore CA2101

sealed class AncestorLinkCanonicalizer
{
    private readonly string linkRoot;
    private readonly string targetRoot;

    public AncestorLinkCanonicalizer(string linkRoot, string targetRoot)
    {
        this.linkRoot = Path.GetFullPath(linkRoot);
        this.targetRoot = Path.GetFullPath(targetRoot);
    }

    public bool AncestorPathObserved { get; private set; }

    public string Canonicalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var relative = Path.GetRelativePath(linkRoot, fullPath);
        if (relative == "." || (!Path.IsPathRooted(relative) && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)))
        {
            AncestorPathObserved = true;
            return Path.GetFullPath(Path.Combine(targetRoot, relative));
        }

        return fullPath;
    }
}
