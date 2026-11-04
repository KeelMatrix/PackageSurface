using System.Text.Json;
using System.Xml;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.MsBuildXmlSchema;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreLibraryMetadata;
using static KeelMatrix.PackageSurface.Probe.StaticImportCondition;

namespace KeelMatrix.PackageSurface.Probe;

internal static class NestedImportTraversal
{
    internal static IEnumerable<NestedImportAsset> FindNestedImportAssets(
        IReadOnlyList<GeneratedImport> generatedImports,
        RestoreIdentityIndex restoreIdentities,
        JsonElement libraries,
        string targetFramework,
        string? runtimeIdentifier,
        List<string> incomplete)
    {
        foreach (var import in generatedImports.Where(value => value.IsNested && value.PackageRoot is not null && value.PackageRelativePath is not null))
        {
            if (import.Capability is null || !import.Applicability.IsKnown)
            {
                continue;
            }

            var context = import.Capability == CapabilityKind.BuildMultiTargeting
                ? SurfaceContextKind.Project
                : SurfaceContextKind.Target;
            if (!import.Applicability.AppliesTo(context, targetFramework))
            {
                continue;
            }

            if (!restoreIdentities.TryGetPackageByRoot(import.PackageRoot!, out var packageIdentity) ||
                !restoreIdentities.TryGetLibraryByCanonicalKey(libraries, packageIdentity.CanonicalKey, out _, out _))
            {
                incomplete.Add("Nested package import could not be mapped to a resolved package library.");
                continue;
            }

            yield return new NestedImportAsset(
                packageIdentity.Identity,
                import.PackageRoot!,
                import.PackageRelativePath!,
                import.Capability.Value,
                context,
                targetFramework,
                runtimeIdentifier);
        }
    }

    internal static void WalkNestedImports(
        string file,
        string packageRoot,
        string sourceFile,
        ConditionNode? inheritedCondition,
        CapabilityKind rootCapability,
        string rootAssetPath,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        IReadOnlySet<string> targetFrameworkContexts,
        List<GeneratedImport> result,
        List<string> incomplete,
        AnalysisBudget budget,
        HashSet<string> visited,
        int depth)
    {
        var work = new Stack<(string File, string PackageRoot, string SourceFile, ConditionNode? Condition, int Depth)>();
        work.Push((file, packageRoot, sourceFile, inheritedCondition, depth));
        while (work.Count > 0)
        {
            var current = work.Pop();
            if (current.Depth >= MaxNestedImports)
            {
                incomplete.Add("Nested package import depth exceeds the supported resource limit.");
                continue;
            }

            budget.AddOperation("nested import");
            if (!TryResolveExistingFilePath(current.File, out var currentFile) ||
                !IsSafeResolvedFile(current.PackageRoot, currentFile))
            {
                incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
                continue;
            }
            if (!visited.Add(Path.GetFullPath(currentFile) + "|" + current.Condition + "|" + rootCapability + "|" + rootAssetPath)) continue;

            var currentRelativePath = Path.GetRelativePath(current.PackageRoot, currentFile).Replace(Path.DirectorySeparatorChar, '/');
            if (!IsDeclaredPackageFile(current.PackageRoot, currentRelativePath, packageInventories))
            {
                incomplete.Add("A statically imported package build file is absent from the resolved package inventory.");
                continue;
            }

            try
            {
                budget.AddImportedFile();
                EnsureFileWithinLimit(currentFile, MaxMetadataFileBytes, "nested package import");
                budget.Add(FileLength(currentFile), "nested-import metadata");
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                    MaxCharactersInDocument = MaxXmlCharacters,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };
                using var reader = XmlReader.Create(currentFile, settings);
                var xmlContext = new MsBuildXmlContext();
                var nestedWork = new List<(string File, string PackageRoot, string SourceFile, ConditionNode? Condition, int Depth)>();
                while (reader.Read())
                {
                    if (reader.Depth > MaxXmlDepth)
                    {
                        incomplete.Add("A nested package import exceeds the supported XML depth.");
                        break;
                    }

                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        xmlContext.Exit(reader);
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element) continue;
                    var description = "Nested package import";
                    if (!xmlContext.TryEnter(reader, description, incomplete)) continue;
                    if (HasCaseInsensitiveDuplicateXmlAttributes(reader))
                    {
                        incomplete.Add("A nested package import contains duplicate or case-variant attributes.");
                    }

                    ValidateConsumedXmlElementSpelling(reader, PackageMsBuildElementNames, description, incomplete, PackageMsBuildStandardElementNames);
                    ValidateConsumedXmlAttributeSpellings(reader, PackageMsBuildAttributeNames, description, incomplete, rejectUnknown: false);
                    var elementName = reader.LocalName;
                    if (elementName.Equals("ImportGroup", StringComparison.Ordinal) && !IsDirectProjectChild(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete, description);
                        continue;
                    }

                    if (!elementName.Equals("Import", StringComparison.Ordinal)) continue;
                    if (!IsSupportedImportLocation(xmlContext) || reader.GetAttribute("Project") is not string project)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete, description);
                        continue;
                    }

                    budget.AddImportedEdge();
                    var clauses = xmlContext.Elements
                        .Where(element => element.Name is "Project" or "ImportGroup")
                        .Select(element => new ConditionClause(element.Name, element.Condition))
                        .Append(new ConditionClause(elementName, reader.GetAttribute("Condition")))
                        .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                        .ToArray();
                    var applicability = DetermineApplicability(clauses, ImportExistsCondition.ForImport(project, restoreIdentities, packageRoots), out var failure);
                    if (!applicability.IsKnown)
                    {
                        incomplete.Add($"Nested package import contains an unsupported condition on {failure!.Owner}: {failure.Message}.");
                    }

                    string? resolutionReason = null;
                    var resolved = TryResolveStaticPackageImport(project, currentFile, restoreIdentities, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason);
                    var combined = applicability.IsKnown
                        ? ImportApplicability.Known(CombineConditionNodes(current.Condition, applicability.Condition))
                        : applicability;
                    var couldApply = rootCapability == CapabilityKind.BuildMultiTargeting
                        ? combined.AppliesTo(SurfaceContextKind.Project, string.Empty)
                        : targetFrameworkContexts.Any(targetFramework => combined.AppliesTo(SurfaceContextKind.Target, targetFramework));
                    result.Add(new GeneratedImport(current.SourceFile, resolved ? nestedPath : project, combined, true,
                        resolved ? nestedRoot : null,
                        resolved ? Path.GetRelativePath(nestedRoot, nestedPath).Replace(Path.DirectorySeparatorChar, '/') : null,
                        rootCapability,
                        rootAssetPath));
                    if (!couldApply)
                    {
                        continue;
                    }

                    if (combined.IsKnown && resolved)
                    {
                        var nestedRelativePath = Path.GetRelativePath(nestedRoot, nestedPath).Replace(Path.DirectorySeparatorChar, '/');
                        if (!IsDeclaredPackageFile(nestedRoot, nestedRelativePath, packageInventories))
                        {
                            incomplete.Add("Nested package import refers to a file absent from the resolved package inventory.");
                        }
                        else
                        {
                            nestedWork.Add((nestedPath, nestedRoot, current.SourceFile, combined.Condition, current.Depth + 1));
                        }
                    }
                    else if (resolutionReason is not null && !resolutionReason.Equals("not a package import", StringComparison.Ordinal))
                    {
                        incomplete.Add("Nested package import contains an unsupported static import target.");
                    }
                    else if (resolutionReason is "not a package import" &&
                             (Path.IsPathRooted(project) ||
                              (!project.Contains("$(", StringComparison.Ordinal) &&
                               !IsRelativeImportUnderAnyPackageRoot(currentFile, project, packageRoots.Values))))
                    {
                        incomplete.Add("Nested package import is outside the resolved package root or is missing from the package inventory.");
                    }
                }

                for (var index = nestedWork.Count - 1; index >= 0; index--)
                {
                    work.Push(nestedWork[index]);
                }
            }
            catch (XmlException)
            {
                incomplete.Add("A statically imported package build file is malformed.");
            }
            catch (InvalidOperationException)
            {
                incomplete.Add("A statically imported package build file is unsupported.");
            }
            catch (IOException)
            {
                incomplete.Add("A statically imported package build file could not be read.");
            }
        }
    }

    internal static bool IsRelativeImportUnderAnyPackageRoot(string currentFile, string importProject, IEnumerable<string> packageRoots)
    {
        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentFile))!;
        var fullFile = Path.GetFullPath(Path.Combine(currentDirectory, importProject.Replace('/', Path.DirectorySeparatorChar)));
        return packageRoots.Any(root => IsWithinDirectory(root, fullFile, allowRoot: false));
    }

    internal static bool TryResolveStaticPackageImport(
        string importProject,
        string currentFile,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out string resolvedPath,
        out string packageRoot,
        out string? reason)
    {
        resolvedPath = string.Empty;
        packageRoot = string.Empty;
        reason = null;
        var requested = NormalizeText(importProject);
        if (requested.Length == 0)
        {
            reason = "empty import target";
            return false;
        }

        const string fileNameProperty = "$(MSBuildThisFileName)";
        var fileNamePropertyIndex = requested.IndexOf(fileNameProperty, StringComparison.OrdinalIgnoreCase);
        if (fileNamePropertyIndex >= 0)
        {
            var fileName = Path.GetFileNameWithoutExtension(currentFile);
            requested = requested[..fileNamePropertyIndex]
                + fileName
                + requested[(fileNamePropertyIndex + fileNameProperty.Length)..];
        }

        if (requested.Contains("$(", StringComparison.Ordinal) &&
            !TryGetNuGetPackageRootSuffix(requested, out _))
        {
            reason = "dynamic import target";
            return false;
        }

        if (TryGetNuGetPackageRootSuffix(requested, out var packageSuffix))
        {
            if (!restoreIdentities.TryGetPackageByImportSuffix(packageSuffix, out var packageIdentity, out var relative) ||
                !packageRoots.TryGetValue(packageIdentity.CanonicalKey, out var candidateRoot))
            {
                reason = "unresolved package import target";
                return false;
            }

            if (!TryNormalizeRelativePath(relative, out _))
            {
                reason = "unsafe package import target";
                return false;
            }

            var candidatePath = Path.GetFullPath(Path.Combine(candidateRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithinDirectory(candidateRoot, candidatePath, allowRoot: false))
            {
                reason = "unsafe package import target";
                return false;
            }

            if (!TryResolveExistingFilePath(candidatePath, out var actualPath))
            {
                reason = "missing package import (package-root reference)";
                return false;
            }
            if (!IsSafeResolvedFile(candidateRoot, actualPath))
            {
                reason = "unsafe package import target";
                return false;
            }

            resolvedPath = actualPath;
            packageRoot = candidateRoot;
            return true;
        }

        if (Path.IsPathRooted(requested))
        {
            var absolute = Path.GetFullPath(requested);
            foreach (var candidate in packageRoots.Values.Distinct(FileSystemPathComparer))
            {
                if (!IsWithinDirectory(candidate, absolute, allowRoot: false)) continue;
                if (!TryResolveExistingFilePath(absolute, out var actualPath))
                {
                    reason = "missing package import (absolute reference)";
                    return false;
                }
                if (!IsSafeResolvedFile(candidate, actualPath)) continue;
                resolvedPath = actualPath;
                packageRoot = candidate;
                return true;
            }

            reason = "not a package import";
            return false;
        }

        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentFile))!;
        var relativePath = Path.GetFullPath(Path.Combine(currentDirectory, requested.Replace('/', Path.DirectorySeparatorChar)));
        var sawSafeRoot = false;
        foreach (var candidate in packageRoots.Values.Distinct(FileSystemPathComparer))
        {
            if (!IsWithinDirectory(candidate, relativePath, allowRoot: false)) continue;
            sawSafeRoot = true;
            if (!TryResolveExistingFilePath(relativePath, out var actualPath))
            {
                reason = "missing package import (relative reference)";
                return false;
            }
            if (!IsSafeResolvedFile(candidate, actualPath)) continue;
            resolvedPath = actualPath;
            packageRoot = candidate;
            return true;
        }

        reason = sawSafeRoot ? "missing package import (relative reference; safe-root)" : "not a package import";
        return false;
    }
}
