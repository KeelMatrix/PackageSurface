namespace KeelMatrix.PackageSurface.Probe;

public static class ResolvedGraphClassifier
{
    public static ProbeResult Analyze(
        string assetsFile,
        string projectRoot,
        bool strictContent = true,
        string? projectContext = null,
        string? selectedProjectPath = null,
        AnalysisBudget? budget = null,
        string? compilerApiVersion = null,
        FileSystemCaseSensitivity fileSystemCaseSensitivity = FileSystemCaseSensitivity.Host) =>
        ResolvedGraphAnalysis.Analyze(assetsFile, projectRoot, strictContent, projectContext, selectedProjectPath, budget, compilerApiVersion, fileSystemCaseSensitivity);

    public static string? ReadRestoreProjectPath(string assetsFile) =>
        RestoreMetadataReader.ReadRestoreProjectPath(assetsFile);

    public static IReadOnlyList<string> GetReachablePackageInputPaths(string assetsFile) =>
        PackageGraphAnalyzer.GetReachablePackageInputPaths(assetsFile);
}
