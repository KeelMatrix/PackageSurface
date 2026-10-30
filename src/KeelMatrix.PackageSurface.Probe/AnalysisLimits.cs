namespace KeelMatrix.PackageSurface.Probe;

internal static class AnalysisLimits
{
    internal const int MaxJsonDepth = 64;
    internal const int MaxTargets = AnalysisBudget.MaxTargetGraphs;
    internal const int MaxLibraries = AnalysisBudget.MaxLibraries;
    internal const int MaxFilesPerLibrary = AnalysisBudget.MaxLibraryFiles;
    internal const int MaxGeneratedImportFiles = 256;
    internal const int MaxEntries = AnalysisBudget.MaxEntries;
    internal const int MaxDependencyNodes = AnalysisBudget.MaxDependencyNodes;
    internal const int MaxDependencyDepth = 256;
    internal const int MaxConditionLength = 64 * 1024;
    internal const int MaxConditionTokens = 2_048;
    internal const int MaxConditionDepth = 64;
    internal const int MaxNestedImports = 512;
    internal const long MaxMetadataFileBytes = 16 * 1024 * 1024;
    internal const long MaxXmlCharacters = 8 * 1024 * 1024;
    internal const int MaxXmlDepth = 64;
    internal const string JsonExtensionPrefix = "x-";
    internal const string XmlExtensionNamespace = "urn:keelmatrix:packagesurface:extension";
    internal const string MsBuildXmlNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";
}
