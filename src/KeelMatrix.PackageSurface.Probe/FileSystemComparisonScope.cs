namespace KeelMatrix.PackageSurface.Probe;

public static class FileSystemComparisonScope
{
    public static IDisposable Push(string seedPath, FileSystemCaseSensitivity caseSensitivity = FileSystemCaseSensitivity.Host) =>
        FileSystemComparisonContext.Push(seedPath, caseSensitivity);

    public static IDisposable Push(
        string seedPath,
        FileSystemCaseSensitivity caseSensitivity,
        IReadOnlyDictionary<string, FileSystemCaseSensitivity> rootOverrides) =>
        FileSystemComparisonContext.Push(seedPath, caseSensitivity, rootOverrides);

    public static bool PathsEqual(string left, string right)
    {
        var context = FileSystemComparisonContext.Current;
        return context.TryPathsEqual(Path.GetFullPath(left), Path.GetFullPath(right), out var equal) && equal;
    }

    public static IEqualityComparer<string> PathComparer => FileSystemComparisonContext.Current.PathComparer;
}
