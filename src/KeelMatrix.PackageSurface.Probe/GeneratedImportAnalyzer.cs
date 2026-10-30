using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.GeneratedImportAnalyzer;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class GeneratedImportAnalyzer
{


    internal static readonly Regex PropertyComparison = new(
        "^\\s*['\\\"]?\\$\\(\\s*(?<property>[A-Za-z_][A-Za-z0-9_.-]*)\\s*\\)['\\\"]?\\s*(?<operator>==|!=)\\s*['\\\"](?<value>[^'\\\"]*)['\\\"]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    internal static readonly Regex ExistsCondition = new(
        "^\\s*Exists\\s*\\(\\s*['\\\"](?<path>[^'\\\"]*)['\\\"]\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);


    internal static readonly string[] GeneratedImportElementNames = { "Import" };


    internal static readonly string[] PackageMsBuildElementNames = { "UsingTask", "Exec", "Import", "Code" };


    internal static readonly string[] GeneratedImportAttributeNames = { "Condition", "Include", "Project", "ToolsVersion" };


    internal static readonly string[] PackageMsBuildAttributeNames =
    {
        "AfterTargets",
        "AssemblyFile",
        "BeforeTargets",
        "Command",
        "Condition",
        "DependsOnTargets",
        "Include",
        "Language",
        "Name",
        "Project",
        "TaskFactory",
        "TaskName",
        "ToolsVersion",
        "Type"
    };


    // These built-in MSBuild names are exempt from near-spelling checks only so
    // standard unconsumed metadata cannot be mistaken for a consumed element.
    // Other unconsumed item/property/task names remain tolerated by default.
    internal static readonly HashSet<string> PackageMsBuildStandardElementNames = new(StringComparer.Ordinal)
    {
        "Project",
        "PropertyGroup",
        "ItemGroup",
        "ItemDefinitionGroup",
        "ImportGroup",
        "ItemMetadata",
        "Target",
        "Choose",
        "When",
        "Otherwise",
        "OnError",
        "Parameter",
        "ParameterGroup",
        "ProjectExtensions",
        "Property",
        "Sdk",
        "Task",
        "Data",
        "Output",
        "Error",
        "Warning",
        "Message",
        "CallTarget",
        "CreateProperty",
        "CreateItem",
        "RemoveProperty",
        "RemoveItem",
        "Item",
        "None",
        "Compile",
        "Content",
        "EmbeddedResource",
        "Resource",
        "Reference",
        "ProjectReference",
        "PackageReference",
        "FrameworkReference",
        "Analyzer",
        "AdditionalFiles",
        "Folder"
    };



    internal static IEnumerable<SurfaceEntry> CreateNestedImportEntries(
        IReadOnlyList<GeneratedImport> generatedImports,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        string targetFramework,
        string? runtimeIdentifier,
        HashSet<string> directPackages,
        string? projectContext,
        bool strictContent,
        AnalysisBudget budget,
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

            var packageId = packageIdentity.Id;
            var version = packageIdentity.Version;
            var physicalPath = Path.Combine(import.PackageRoot!, import.PackageRelativePath!.Replace('/', Path.DirectorySeparatorChar));
            if (!TryResolveExistingFilePath(physicalPath, out var actualPath) ||
                !IsSafeResolvedFile(import.PackageRoot!, actualPath))
            {
                incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
                continue;
            }
            physicalPath = actualPath;
            var actualRelativePath = Path.GetRelativePath(import.PackageRoot!, physicalPath).Replace(Path.DirectorySeparatorChar, '/');
            if (!IsDeclaredPackageFile(import.PackageRoot!, actualRelativePath, packageInventories))
            {
                incomplete.Add("A statically imported package build file is absent from the resolved package inventory.");
                continue;
            }

            budget.AddOperation("nested surface entry");
            IReadOnlyList<string>? observedPrimitives = null;
            try
            {
                observedPrimitives = InspectMsBuildXml(physicalPath);
            }
            catch (XmlException)
            {
                incomplete.Add($"{packageIdentity.CanonicalKey}: nested package MSBuild file {import.PackageRelativePath} is malformed.");
            }
            catch (InvalidOperationException)
            {
                incomplete.Add($"{packageIdentity.CanonicalKey}: nested package MSBuild file {import.PackageRelativePath} is unsupported.");
            }

            var sha = strictContent && CapabilityPolicy.IsStrictContentEligible(import.Capability.Value, true, true)
                ? ComputeSha256(physicalPath, budget)
                : null;
            yield return new SurfaceEntry(
                context == SurfaceContextKind.Project ? null : targetFramework,
                context == SurfaceContextKind.Project ? null : runtimeIdentifier,
                context,
                packageId,
                version,
                directPackages.Contains(packageIdentity.Identity.CanonicalKey) ? "direct" : "transitive",
                import.Capability.Value,
                import.PackageRelativePath,
                true,
                true,
                sha,
                false,
                null,
                projectContext,
                observedPrimitives);
        }
    }



    internal static void ValidateGeneratedImportEvidence(
        IReadOnlyList<GeneratedImport> imports,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, ReachableGraph> reachablePackagesByTarget,
        Dictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        foreach (var import in imports)
        {
            if (!import.Applicability.IsKnown) continue;

            var project = NormalizeText(import.Project);
            PackageIdentity? match;
            string? relative = null;
            if (TryGetNuGetPackageRootSuffix(project, out var suffix))
            {
                if (restoreIdentities.TryGetPackageByImportSuffix(suffix, out var packageIdentity, out var packageRelativePath))
                {
                    match = packageIdentity.Identity;
                    relative = packageRelativePath;
                }
                else
                {
                    match = null;
                }
            }
            else if (import.PackageRoot is not null)
            {
                match = restoreIdentities.TryGetPackageByRoot(import.PackageRoot, out var packageIdentity)
                    ? packageIdentity.Identity
                    : null;
            }
            else
            {
                continue;
            }

            if (match is null) continue;
            if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, match, out _, out var library))
            {
                incomplete.Add("Generated NuGet import evidence refers to an unrepresented package library.");
                continue;
            }

            var files = packageRoots.TryGetValue(match.CanonicalKey, out var packageRoot) && packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
                ? inventory
                : ReadLibraryFiles(library, match.CanonicalKey, incomplete, budget).ToHashSet(GetPackageAssetStringComparer(match.CanonicalKey));
            if (relative is not null && !files.Contains(relative))
            {
                incomplete.Add("Generated NuGet import evidence refers to a file absent from the resolved package inventory.");
                continue;
            }

            var targetGraphs = targets.EnumerateObject()
                .Where(target => restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) &&
                    (import.IsNested && import.Capability == CapabilityKind.BuildMultiTargeting
                        ? import.Applicability.AppliesTo(SurfaceContextKind.Project, string.Empty)
                        : import.Applicability.AppliesTo(SurfaceContextKind.Target, targetIdentity.TargetFramework)))
                .ToArray();
            if (targetGraphs.Length == 0) continue;

            foreach (var target in targetGraphs)
            {
                if (target.Value.ValueKind != JsonValueKind.Object ||
                    !restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ||
                    !reachablePackagesByTarget.TryGetValue(targetIdentity.CanonicalKey, out var closure) ||
                    !closure.ReachablePackageKeys.Contains(match.CanonicalKey))
                {
                    incomplete.Add("Generated NuGet import evidence refers to a package that is not reachable from an applicable project/direct root.");
                    break;
                }
            }
        }
    }



    internal static List<GeneratedImport> ReadGeneratedImports(
        JsonElement root,
        string assetsFile,
        string projectRoot,
        string? selectedProjectPath,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories,
        JsonElement libraries,
        IReadOnlySet<string> targetFrameworkContexts,
        List<string> incomplete,
        AnalysisBudget budget,
        HashSet<string> expectedGeneratedImportSources)
    {
        var result = new List<GeneratedImport>();
        foreach (var expectedSource in GetExpectedGeneratedImportSources(root, projectRoot, selectedProjectPath, incomplete))
        {
            expectedGeneratedImportSources.Add(expectedSource);
        }

        var generatedDirectory = Path.GetDirectoryName(Path.GetFullPath(assetsFile))!;
        var files = expectedGeneratedImportSources
            .Select(source => Path.Combine(generatedDirectory, source))
            .ToList();
        if (files.Count > MaxGeneratedImportFiles)
        {
            incomplete.Add("The project contains too many generated NuGet import files.");
            files = files.Take(MaxGeneratedImportFiles).ToList();
        }

        var visitedImports = new HashSet<string>(FileSystemPathComparer);

        foreach (var requestedFile in files.Distinct(FileSystemPathComparer).Order(StringComparer.Ordinal))
        {
            if (!TryResolveExistingFilePath(requestedFile, out var file))
            {
                incomplete.Add($"Expected generated import file {Path.GetFileName(requestedFile)} is missing from the assets directory.");
                continue;
            }

            if (!file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            try
            {
                EnsureFileWithinLimit(file, MaxMetadataFileBytes, "generated NuGet import");
                budget.Add(FileLength(file), "generated-import metadata");
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                    MaxCharactersInDocument = MaxXmlCharacters,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };
                using var reader = XmlReader.Create(file, settings);
                var xmlContext = new MsBuildXmlContext();
                var exceededDepth = false;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.EndElement)
                    {
                        xmlContext.Exit(reader);
                        continue;
                    }

                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Depth > MaxXmlDepth)
                    {
                        exceededDepth = true;
                        break;
                    }

                    var description = $"Generated import file {Path.GetFileName(file)}";
                    if (!xmlContext.TryEnter(reader, description, incomplete)) continue;
                    if (HasCaseInsensitiveDuplicateXmlAttributes(reader))
                    {
                        incomplete.Add($"{description} contains duplicate or case-variant attributes.");
                    }

                    var elementName = reader.LocalName;
                    ValidateConsumedXmlElementSpelling(reader, GeneratedImportElementNames, description, incomplete);
                    ValidateConsumedXmlAttributeSpellings(reader, GeneratedImportAttributeNames, description, incomplete);
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
                    var conditions = xmlContext.Elements
                        .Where(element => element.Name is "Project" or "ImportGroup")
                        .Select(element => new ConditionClause(element.Name, element.Condition))
                        .Append(new ConditionClause(elementName, reader.GetAttribute("Condition")))
                        .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                        .ToArray();
                    var applicability = DetermineApplicability(conditions, project, restoreIdentities, packageRoots, out var reason);
                    if (!applicability.IsKnown)
                    {
                        incomplete.Add($"{description} has an unsupported condition on {reason!.Owner}: {reason.Message}.");
                    }

                    result.Add(new GeneratedImport(Path.GetFileName(file), project, applicability));
                }

                if (exceededDepth)
                {
                    incomplete.Add($"Generated import file {Path.GetFileName(file)} exceeds the supported XML depth.");
                }

                foreach (var directImport in result.Where(import => string.Equals(import.SourceFile, Path.GetFileName(file), FileSystemComparisonContext.Current.ProjectStringComparison) && !import.IsNested).ToArray())
                {
                    string? resolutionReason = null;
                    if (directImport.Applicability.IsKnown &&
                        TryResolveStaticPackageImport(directImport.Project, file, restoreIdentities, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason))
                    {
                        var rootAssetPath = Path.GetRelativePath(nestedRoot, nestedPath).Replace(Path.DirectorySeparatorChar, '/');
                        if (!TryGetCapability(rootAssetPath, out var rootCapability))
                        {
                            incomplete.Add("Generated NuGet import evidence refers to an unsupported package build asset.");
                            continue;
                        }

                        if (!IsDeclaredPackageFile(nestedRoot, rootAssetPath, packageInventories))
                        {
                            incomplete.Add("Generated NuGet import evidence refers to a file absent from the resolved package inventory.");
                            continue;
                        }

                        WalkNestedImports(nestedPath, nestedRoot, directImport.SourceFile, directImport.Applicability.Condition, rootCapability, rootAssetPath, restoreIdentities, packageRoots, packageInventories, libraries, targetFrameworkContexts, result, incomplete, budget, visitedImports, 0);
                    }
                    else if (resolutionReason is not null)
                    {
                        incomplete.Add($"Generated import file {Path.GetFileName(file)} contains an unsupported static import target.");
                    }
                }
            }
            catch (XmlException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is malformed.");
            }
            catch (InvalidOperationException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is unsupported.");
            }
            catch (IOException)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} could not be read.");
            }
        }

        return result;
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
                    var applicability = DetermineApplicability(clauses, project, restoreIdentities, packageRoots, out var failure);
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



    internal static ConditionNode? CombineConditionNodes(ConditionNode? left, ConditionNode? right) =>
        left is null ? right : right is null ? left : new AndCondition(left, right);



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



    internal static bool IsActive(
        CapabilityKind capability,
        string relativePath,
        PackageIdentity packageIdentity,
        HashSet<string> analyzerPackages,
        IReadOnlySet<string> selectedAnalyzers,
        string packageRoot,
        JsonElement targetAssets,
        SurfaceContextKind context,
        string targetFramework,
        IReadOnlyList<GeneratedImport> generatedImports,
        IReadOnlySet<string> expectedGeneratedImportSources,
        bool isMultiTargetingProject,
        ProjectLanguage projectLanguage,
        ref string? reason,
        List<string> incomplete,
        string libraryKey)
    {
        if (capability == CapabilityKind.ToolOrScriptPresent)
        {
            return false;
        }

        if (capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting)
        {
            var reachableBuildAsset = ContainsBuildAsset(targetAssets, capability, relativePath, incomplete, libraryKey);
            var fullAsset = NormalizeText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var hasMatchingImport = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                var referencesAsset = FileSystemPathsEqual(normalizedImport, fullAsset) ||
                    PackageRootImportEquals(normalizedImport, packageIdentity, relativePath);
                return IsNestedOrExpectedGeneratedImportSource(import, relativePath, expectedGeneratedImportSources) && referencesAsset;
            });
            var imported = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                var referencesAsset = FileSystemPathsEqual(normalizedImport, fullAsset) ||
                    PackageRootImportEquals(normalizedImport, packageIdentity, relativePath);
                return IsNestedOrExpectedGeneratedImportSource(import, relativePath, expectedGeneratedImportSources) &&
                       referencesAsset &&
                       import.AppliesTo(context, targetFramework, capability == CapabilityKind.BuildMultiTargeting);
            });
            var hasWrongPhaseImport = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                var referencesAsset = FileSystemPathsEqual(normalizedImport, fullAsset) ||
                    PackageRootImportEquals(normalizedImport, packageIdentity, relativePath);
                return !import.IsNested && referencesAsset && !IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources);
            });
            if (!imported &&
                (hasWrongPhaseImport ||
                 (reachableBuildAsset && !hasMatchingImport && RequiresGeneratedImportEvidence(capability, relativePath, isMultiTargetingProject))))
            {
                reason = $"Required generated NuGet import evidence is missing or phase-mismatched: {relativePath}.";
                incomplete.Add($"{libraryKey}: {reason}");
            }

            return imported;
        }

        var assetName = relativePath.Replace('\\', '/');
        return capability switch
        {
            CapabilityKind.CompilerExtension => IsCompilerExtensionActive(assetName, projectLanguage, analyzerPackages, selectedAnalyzers, packageIdentity.CanonicalKey, incomplete, libraryKey),
            CapabilityKind.CompileSourceInjection => IsCompileContentFile(targetAssets, assetName, projectLanguage, incomplete, libraryKey),
            CapabilityKind.NativeRuntime => ContainsAsset(targetAssets, "native", assetName, incomplete, libraryKey) || ContainsAsset(targetAssets, "runtime", assetName, incomplete, libraryKey),
            _ => false
        };
    }



    internal static bool IsExpectedGeneratedImportSource(
        string sourceFile,
        string relativePath,
        IReadOnlySet<string> expectedGeneratedImportSources)
    {
        var assetExtension = Path.GetExtension(relativePath);
        if (!assetExtension.Equals(".props", StringComparison.OrdinalIgnoreCase) &&
            !assetExtension.Equals(".targets", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var expectedGeneratedImportSuffix = ".nuget.g" + assetExtension;
        if (!sourceFile.EndsWith(expectedGeneratedImportSuffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return expectedGeneratedImportSources.Count == 0 || expectedGeneratedImportSources.Contains(sourceFile);
    }



    internal static bool IsNestedOrExpectedGeneratedImportSource(
        GeneratedImport import,
        string relativePath,
        IReadOnlySet<string> expectedGeneratedImportSources) =>
        import.IsNested || IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources);



    internal static IEnumerable<string> GetExpectedGeneratedImportSources(JsonElement root, string projectRoot, string? selectedProjectPath, List<string> incomplete)
    {
        string? projectPath = null;
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("projectPath", out var restoredPath) && restoredPath.ValueKind == JsonValueKind.String)
        {
            projectPath = restoredPath.GetString();
        }

        if (string.IsNullOrWhiteSpace(projectPath))
        {
            incomplete.Add("Restore metadata does not identify the generated-import project.");
            yield break;
        }

        if (selectedProjectPath is not null && !PathsEqual(projectPath, selectedProjectPath))
        {
            incomplete.Add("Restore metadata identifies a different project than the generated-import input.");
            yield break;
        }

        var normalizedPath = projectPath.Replace('\\', '/');
        var fileName = normalizedPath[(normalizedPath.LastIndexOf('/') + 1)..];
        if (fileName.Length == 0)
        {
            yield break;
        }

        yield return fileName + ".nuget.g.props";
        yield return fileName + ".nuget.g.targets";
    }



    internal static bool ContainsAsset(JsonElement targetAssets, string propertyName, string assetName, List<string> incomplete, string libraryKey)
    {
        if (!targetAssets.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{libraryKey}: target asset group '{propertyName}' has an unsupported shape.");
            return false;
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"{libraryKey}: target asset group '{propertyName}' has malformed metadata.");
                continue;
            }

            if (string.Equals(property.Name.Replace('\\', '/'), assetName, GetPackageAssetComparison(libraryKey)))
            {
                return true;
            }
        }

        return false;
    }



    internal static bool ContainsBuildAsset(JsonElement targetAssets, CapabilityKind capability, string assetName, List<string> incomplete, string libraryKey)
    {
        var propertyName = capability switch
        {
            CapabilityKind.BuildProps => "build",
            CapabilityKind.BuildTargets => "build",
            CapabilityKind.BuildTransitive => "buildTransitive",
            CapabilityKind.BuildMultiTargeting => "buildMultiTargeting",
            _ => string.Empty
        };
        if (propertyName.Length == 0) return false;
        if (ContainsAsset(targetAssets, propertyName, assetName, incomplete, libraryKey)) return true;
        return capability == CapabilityKind.BuildTransitive &&
            ContainsAsset(targetAssets, "build", assetName, incomplete, libraryKey);
    }



    internal static bool RequiresGeneratedImportEvidence(CapabilityKind capability, string assetName, bool isMultiTargetingProject)
    {
        if (capability == CapabilityKind.BuildMultiTargeting && !isMultiTargetingProject) return false;
        return true;
    }



    internal static ImportApplicability DetermineApplicability(
        IReadOnlyList<ConditionClause> conditions,
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out ConditionFailure? failure)
    {
        failure = null;
        ConditionNode? combined = null;
        foreach (var condition in conditions)
        {
            var parsed = ParseCondition(condition.Expression!, importProject, restoreIdentities, packageRoots);
            if (!parsed.IsKnown)
            {
                failure = new ConditionFailure(condition.Owner, parsed.Reason!);
                return new ImportApplicability(null, parsed.Reason);
            }

            combined = combined is null ? parsed.Node : new AndCondition(combined, parsed.Node!);
        }

        return ImportApplicability.Known(combined);
    }



    internal static ParsedCondition ParseCondition(
        string expression,
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots)
    {
        if (!IsConditionWithinLimits(expression))
        {
            return ParsedCondition.Unknown("the condition exceeds the supported resource limits");
        }

        var normalized = StripOuterParentheses(expression.Trim());
        if (normalized.Length == 0)
        {
            return ParsedCondition.Unknown("the condition is empty");
        }

        if (TrySplitTopLevel(normalized, "OR", out var disjunction))
        {
            return CombineConditions(disjunction, importProject, restoreIdentities, packageRoots, static (left, right) => new OrCondition(left, right));
        }

        if (TrySplitTopLevel(normalized, "AND", out var conjunction))
        {
            return CombineConditions(conjunction, importProject, restoreIdentities, packageRoots, static (left, right) => new AndCondition(left, right));
        }

        var comparison = PropertyComparison.Match(normalized);
        if (comparison.Success)
        {
            var property = comparison.Groups["property"].Value;
            var @operator = comparison.Groups["operator"].Value;
            var value = comparison.Groups["value"].Value;
            if (property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Contains("$(", StringComparison.Ordinal) &&
                    !IsTargetFrameworkPropertyReference(value))
                {
                    return ParsedCondition.Unknown("the condition contains an unsupported property expansion");
                }

                return ParsedCondition.Known(new PropertyComparisonCondition(property, @operator, value));
            }

            if (property.Equals("ExcludeRestorePackageImports", StringComparison.OrdinalIgnoreCase) &&
                @operator == "!=" && value.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                return ParsedCondition.Known(new ConstantCondition(true));
            }

            return ParsedCondition.Unknown("the condition references an unsupported property");
        }

        var exists = ExistsCondition.Match(normalized);
        if (exists.Success)
        {
            if (TryProveExists(exists.Groups["path"].Value, importProject, restoreIdentities, packageRoots, out var existsValue, out var reason))
            {
                return ParsedCondition.Known(new ConstantCondition(existsValue));
            }

            return ParsedCondition.Unknown(reason!);
        }

        return ParsedCondition.Unknown("the expression is outside the supported condition grammar");
    }



    internal static ParsedCondition CombineConditions(
        IReadOnlyList<string> expressions,
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        Func<ConditionNode, ConditionNode, ConditionNode> combine)
    {
        ConditionNode? combined = null;
        foreach (var expression in expressions)
        {
            var parsed = ParseCondition(expression, importProject, restoreIdentities, packageRoots);
            if (!parsed.IsKnown)
            {
                return ParsedCondition.Unknown($"a subexpression is unproven: {parsed.Reason}");
            }

            combined = combined is null ? parsed.Node : combine(combined, parsed.Node!);
        }

        return ParsedCondition.Known(combined!);
    }



    internal static bool TryProveExists(
        string requestedPath,
        string importProject,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out bool exists,
        out string? reason)
    {
        exists = false;
        reason = null;
        try
        {
            if (!TryResolveExistsPath(requestedPath, restoreIdentities, packageRoots, out var requested))
            {
                reason = "the Exists expression is not a resolvable filesystem path";
                return false;
            }

            if (!TryResolveExistsPath(importProject, restoreIdentities, packageRoots, out var project))
            {
                reason = "the Exists expression is not the standard resolved-package-file guard";
                return false;
            }

            var requestedCanonical = CanonicalizePath(requested);
            var projectCanonical = CanonicalizePath(project);
            if (FileSystemComparisonContext.Current.TryPathsEqual(requestedCanonical, projectCanonical, out var pathsEqual))
            {
                if (pathsEqual)
                {
                    if (FileSystemComparisonContext.Current.TryFileExists(requested, out exists)) return true;
                    reason = "the Exists expression's filesystem result is indeterminate";
                    return false;
                }

                if (string.Equals(requestedCanonical, projectCanonical, StringComparison.OrdinalIgnoreCase))
                {
                    exists = false;
                    return true;
                }
            }

            reason = string.Equals(requestedCanonical, projectCanonical, StringComparison.OrdinalIgnoreCase)
                ? "the Exists expression's filesystem casing semantics are indeterminate"
                : "the Exists expression does not match the resolved import under the host filesystem contract";
            return false;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            reason = "the Exists expression contains an invalid filesystem path";
            return false;
        }
    }



    internal static bool TryResolveExistsPath(
        string value,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        out string resolvedPath)
    {
        var normalized = NormalizeText(value);
        if (TryGetNuGetPackageRootSuffix(normalized, out var suffix))
        {
            if (!restoreIdentities.TryGetPackageByImportSuffix(suffix, out var packageIdentity, out var relativePath) ||
                !packageRoots.TryGetValue(packageIdentity.CanonicalKey, out var packageRoot) ||
                !TryNormalizeRelativePath(relativePath, out var safeRelativePath))
            {
                resolvedPath = string.Empty;
                return false;
            }

            resolvedPath = Path.GetFullPath(Path.Combine(packageRoot, safeRelativePath.Replace('/', Path.DirectorySeparatorChar)));
            return true;
        }

        if (Path.IsPathRooted(normalized))
        {
            resolvedPath = Path.GetFullPath(normalized);
            return true;
        }

        resolvedPath = string.Empty;
        return false;
    }



    internal static bool IsConditionWithinLimits(string expression)
    {
        if (expression.Length > MaxConditionLength) return false;
        var depth = 0;
        var tokens = 0;
        var quote = '\0';
        foreach (var character in expression)
        {
            if (quote != '\0')
            {
                if (character == quote) quote = '\0';
                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                if (++depth > MaxConditionDepth) return false;
                tokens++;
            }
            else if (character == ')')
            {
                if (--depth < 0) return false;
                tokens++;
            }
            else if (char.IsLetterOrDigit(character) || character is '_' or '=' or '!')
            {
                tokens++;
                if (tokens > MaxConditionTokens) return false;
            }
        }

        return quote == '\0' && depth == 0 && tokens <= MaxConditionTokens;
    }



    internal static string StripOuterParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')' && HasSingleOuterPair(expression))
        {
            expression = expression[1..^1].Trim();
        }

        return expression;
    }



    internal static bool HasSingleOuterPair(string expression)
    {
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
            }
            else if (character == '(')
            {
                depth++;
            }
            else if (character == ')' && --depth == 0 && index != expression.Length - 1)
            {
                return false;
            }
        }

        return depth == 0 && quote == '\0';
    }



    internal static bool TrySplitTopLevel(string expression, string operatorText, out string[] parts)
    {
        var matches = new List<string>();
        var start = 0;
        var depth = 0;
        var quote = '\0';
        for (var index = 0; index < expression.Length; index++)
        {
            var character = expression[index];
            if (quote != '\0')
            {
                if (character == quote)
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '\"')
            {
                quote = character;
                continue;
            }

            if (character == '(')
            {
                depth++;
                continue;
            }

            if (character == ')')
            {
                depth--;
                continue;
            }

            if (depth != 0 || index + operatorText.Length > expression.Length ||
                !expression.AsSpan(index, operatorText.Length).Equals(operatorText.AsSpan(), StringComparison.OrdinalIgnoreCase) ||
                !IsConditionBoundary(expression, index - 1) ||
                !IsConditionBoundary(expression, index + operatorText.Length))
            {
                continue;
            }

            matches.Add(expression[start..index].Trim());
            start = index + operatorText.Length;
            index += operatorText.Length - 1;
        }

        if (matches.Count == 0)
        {
            parts = Array.Empty<string>();
            return false;
        }

        matches.Add(expression[start..].Trim());
        parts = matches.ToArray();
        return true;
    }



    internal static bool IsConditionBoundary(string expression, int index) =>
        index < 0 || index >= expression.Length || !char.IsLetterOrDigit(expression[index]) && expression[index] != '_';



    internal static bool IsTargetFrameworkPropertyReference(string value)
    {
        var normalized = value.Trim();
        return normalized.StartsWith("$(", StringComparison.Ordinal) &&
            normalized.EndsWith(')') &&
            normalized[2..^1].Trim().Equals("TargetFramework", StringComparison.OrdinalIgnoreCase);
    }



    internal sealed record ConditionClause(string Owner, string? Expression);



    internal sealed record ConditionFailure(string Owner, string Message);



    internal sealed record ParsedCondition(ConditionNode? Node, string? Reason)
    {
        public bool IsKnown => Reason is null;

        public static ParsedCondition Known(ConditionNode node) => new(node, null);

        public static ParsedCondition Unknown(string reason) => new(null, reason);
    }



    internal abstract record ConditionNode
    {
        public abstract bool Evaluate(SurfaceContextKind context, string targetFramework);
    }



    internal sealed record ConstantCondition(bool Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) => Value;
    }



    internal sealed record PropertyComparisonCondition(string Property, string Operator, string Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework)
        {
            var actual = context == SurfaceContextKind.Project ? string.Empty : targetFramework;
            var expected = Property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) &&
                IsTargetFrameworkPropertyReference(Value)
                ? actual
                : Value;
            var equal = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
            return Operator == "==" ? equal : !equal;
        }
    }



    internal sealed record AndCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) && Right.Evaluate(context, targetFramework);
    }



    internal sealed record OrCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) || Right.Evaluate(context, targetFramework);
    }



    internal sealed record ImportApplicability(ConditionNode? Condition, string? Failure)
    {
        public bool IsKnown => Failure is null;

        public static ImportApplicability Known(ConditionNode? condition) => new(condition, null);

        public bool AppliesTo(SurfaceContextKind context, string targetFramework) =>
            IsKnown && (Condition?.Evaluate(context, targetFramework) ?? true);
    }



    internal static bool HasCaseInsensitiveDuplicateXmlAttributes(XmlReader reader)
    {
        if (!reader.HasAttributes) return false;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < reader.AttributeCount; index++)
        {
            reader.MoveToAttribute(index);
            if (!names.Add(reader.Name))
            {
                reader.MoveToElement();
                return true;
            }
        }

        reader.MoveToElement();
        return false;
    }



    internal static void ValidateConsumedXmlElementSpelling(
        XmlReader reader,
        IReadOnlyCollection<string> consumedNames,
        string description,
        List<string>? incomplete,
        HashSet<string>? toleratedNames = null)
    {
        if (toleratedNames?.Contains(reader.LocalName) == true) return;

        var canonical = consumedNames.FirstOrDefault(name => reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (canonical is null)
        {
            canonical = consumedNames.FirstOrDefault(name => IsNearSpelling(reader.LocalName, name));
            if (canonical is null) return;

            var unknownMessage = $"{description} contains an unknown XML element '{reader.LocalName}' near consumed member '{canonical}'.";
            if (incomplete is null) throw new InvalidOperationException(unknownMessage);
            incomplete.Add(unknownMessage);
            return;
        }

        if (reader.LocalName.Equals(canonical, StringComparison.Ordinal)) return;

        var message = $"{description} contains a non-canonical spelling of consumed XML element '{reader.LocalName}'; expected '{canonical}'.";
        if (incomplete is null)
        {
            throw new InvalidOperationException(message);
        }

        incomplete.Add(message);
    }



    internal static bool IsNearSpelling(string candidate, string canonical)
    {
        if (candidate.Length == 0 || Math.Abs(candidate.Length - canonical.Length) > 2) return false;
        var previous = Enumerable.Range(0, canonical.Length + 1).ToArray();
        for (var row = 1; row <= candidate.Length; row++)
        {
            var current = new int[canonical.Length + 1];
            current[0] = row;
            for (var column = 1; column <= canonical.Length; column++)
            {
                var cost = char.ToUpperInvariant(candidate[row - 1]) == char.ToUpperInvariant(canonical[column - 1]) ? 0 : 1;
                current[column] = Math.Min(
                    Math.Min(current[column - 1] + 1, previous[column] + 1),
                    previous[column - 1] + cost);
            }

            previous = current;
        }

        return previous[canonical.Length] <= 2;
    }



    internal static void ValidateConsumedXmlAttributeSpellings(
        XmlReader reader,
        IReadOnlyCollection<string> allowedNames,
        string description,
        List<string>? incomplete,
        bool rejectUnknown = true)
    {
        if (!reader.HasAttributes) return;
        for (var index = 0; index < reader.AttributeCount; index++)
        {
            reader.MoveToAttribute(index);
            if (IsXmlNamespaceDeclaration(reader) || reader.NamespaceURI.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
            {
                continue;
            }

            var canonical = allowedNames.FirstOrDefault(name => reader.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null)
            {
                var near = allowedNames.FirstOrDefault(name => IsNearSpelling(reader.LocalName, name));
                if (near is null && !rejectUnknown)
                {
                    continue;
                }

                var message = near is null
                    ? $"{description} contains an unknown XML attribute '{reader.Name}'."
                    : $"{description} contains an unknown XML attribute '{reader.Name}' near consumed member '{near}'.";
                if (incomplete is null)
                {
                    reader.MoveToElement();
                    throw new InvalidOperationException(message);
                }

                incomplete.Add(message);
                continue;
            }

            if (!reader.LocalName.Equals(canonical, StringComparison.Ordinal))
            {
                var message = $"{description} contains a non-canonical spelling of consumed XML attribute '{reader.Name}'; expected '{canonical}'.";
                if (incomplete is null)
                {
                    reader.MoveToElement();
                    throw new InvalidOperationException(message);
                }

                incomplete.Add(message);
            }
        }

        reader.MoveToElement();
    }



    internal static bool IsXmlNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix.Equals("xmlns", StringComparison.Ordinal) || reader.Name.Equals("xmlns", StringComparison.Ordinal);



    internal static string[] InspectMsBuildXml(string path)
    {
        var observations = new HashSet<string>(StringComparer.Ordinal);
        var inlineTasks = new Stack<InlineTaskState>();
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersFromEntities = 0,
            MaxCharactersInDocument = MaxXmlCharacters,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        using var reader = XmlReader.Create(path, settings);
        var xmlContext = new MsBuildXmlContext();
        while (reader.Read())
        {
            if (reader.Depth > MaxXmlDepth) throw new InvalidOperationException("XML depth exceeds the supported limit.");
            if (reader.NodeType == XmlNodeType.EndElement)
            {
                if (!xmlContext.IsIgnoring && reader.LocalName.Equals("UsingTask", StringComparison.Ordinal) &&
                    IsSupportedMsBuildNamespace(reader.NamespaceURI) && inlineTasks.Count > 0 &&
                    inlineTasks.Peek().Depth == reader.Depth)
                {
                    var task = inlineTasks.Pop();
                    if (task.IsInlineFactory && task.HasCodeElement) observations.Add("InlineTaskFactory");
                }

                xmlContext.Exit(reader);
                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;
            if (!xmlContext.TryEnter(reader, "package MSBuild XML", incomplete: null)) continue;
            ValidateConsumedXmlElementSpelling(
                reader,
                PackageMsBuildElementNames,
                "package MSBuild XML",
                null,
                PackageMsBuildStandardElementNames);
            ValidateConsumedXmlAttributeSpellings(
                reader,
                PackageMsBuildAttributeNames,
                "package MSBuild XML",
                null,
                rejectUnknown: false);
            var elementName = reader.LocalName;
            if (elementName.Equals("ImportGroup", StringComparison.Ordinal) && !IsDirectProjectChild(xmlContext))
            {
                RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
            }

            switch (elementName)
            {
                case "UsingTask":
                    if (!IsDirectProjectChild(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("UsingTask");
                    var factory = reader.GetAttribute("TaskFactory");
                    if (!reader.IsEmptyElement)
                    {
                        inlineTasks.Push(new InlineTaskState(reader.Depth,
                            factory is not null && (factory.Equals("CodeTaskFactory", StringComparison.OrdinalIgnoreCase) || factory.Equals("RoslynCodeTaskFactory", StringComparison.OrdinalIgnoreCase)), false));
                    }
                    break;
                case "Code":
                    if (!IsSupportedInlineCodeLocation(xmlContext) || inlineTasks.Count == 0 || reader.Depth <= inlineTasks.Peek().Depth)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    var task = inlineTasks.Pop();
                    inlineTasks.Push(task with { HasCodeElement = true });
                    break;
                case "Exec":
                    if (!IsSupportedExecLocation(xmlContext))
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("Exec");
                    break;
                case "Import":
                    if (!IsSupportedImportLocation(xmlContext) || reader.GetAttribute("Project") is null)
                    {
                        RejectMsBuildContext(reader, xmlContext, incomplete: null, "package MSBuild XML");
                    }
                    observations.Add("Import");
                    break;
            }
        }

        return observations.Order(StringComparer.Ordinal).ToArray();
    }



    internal sealed record InlineTaskState(int Depth, bool IsInlineFactory, bool HasCodeElement);



    internal static bool IsSupportedMsBuildNamespace(string namespaceUri) =>
        namespaceUri.Length == 0 || namespaceUri.Equals(MsBuildXmlNamespace, StringComparison.Ordinal);



    internal static bool IsDirectProjectChild(MsBuildXmlContext context) =>
        context.Parent is { Name: "Project", Depth: 0 };



    internal static bool IsSupportedImportLocation(MsBuildXmlContext context) =>
        IsDirectProjectChild(context) ||
        (context.Parent is { Name: "ImportGroup", Depth: 1 } && context.Grandparent is { Name: "Project", Depth: 0 });



    internal static bool IsSupportedExecLocation(MsBuildXmlContext context) =>
        context.Parent is { Name: "Target", Depth: 1 } && context.Grandparent is { Name: "Project", Depth: 0 };



    internal static bool IsSupportedInlineCodeLocation(MsBuildXmlContext context) =>
        context.Parent is { Name: "Task", Depth: 2 } &&
        context.Grandparent is { Name: "UsingTask", Depth: 1 } &&
        context.GreatGrandparent is { Name: "Project", Depth: 0 };



    internal static void RejectMsBuildContext(XmlReader reader, MsBuildXmlContext context, List<string>? incomplete, string description)
    {
        var message = $"{description} contains an unsupported MSBuild XML context.";
        if (incomplete is null) throw new InvalidOperationException(message);
        incomplete.Add(message);
        context.IgnoreCurrent(reader);
    }



    internal sealed record MsBuildXmlElement(string Name, int Depth, string? Condition);



    internal sealed class MsBuildXmlContext
    {
        internal readonly List<MsBuildXmlElement> elements = new();
        internal int ignoredDepth = -1;

        public bool IsIgnoring => ignoredDepth >= 0;
        public IReadOnlyList<MsBuildXmlElement> Elements => elements;
        public MsBuildXmlElement? Parent { get; private set; }
        public MsBuildXmlElement? Grandparent { get; private set; }
        public MsBuildXmlElement? GreatGrandparent { get; private set; }

        public bool TryEnter(XmlReader reader, string description, List<string>? incomplete)
        {
            if (IsIgnoring) return false;
            PruneToDepth(reader.Depth);

            Parent = elements.Count == 0 ? null : elements[^1];
            Grandparent = elements.Count < 2 ? null : elements[^2];
            GreatGrandparent = elements.Count < 3 ? null : elements[^3];

            var name = reader.LocalName;
            var namespaceUri = reader.NamespaceURI;
            if (Parent is null)
            {
                if (!name.Equals("Project", StringComparison.Ordinal) || !IsSupportedMsBuildNamespace(namespaceUri))
                {
                    return Reject(reader, description, incomplete);
                }
            }
            else if (name.Equals("ProjectExtensions", StringComparison.Ordinal))
            {
                if (Parent.Name.Equals("Project", StringComparison.Ordinal) && Parent.Depth == 0 &&
                    IsSupportedMsBuildNamespace(namespaceUri))
                {
                    Ignore(reader);
                    return false;
                }

                if (!namespaceUri.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
                {
                    return Reject(reader, description, incomplete);
                }

                Ignore(reader);
                return false;
            }
            else if (namespaceUri.Equals(XmlExtensionNamespace, StringComparison.Ordinal))
            {
                Ignore(reader);
                return false;
            }
            else if (!IsSupportedMsBuildNamespace(namespaceUri))
            {
                return Reject(reader, description, incomplete);
            }

            if (!reader.IsEmptyElement)
            {
                elements.Add(new MsBuildXmlElement(name, reader.Depth, reader.GetAttribute("Condition")));
            }

            return true;
        }

        public void Exit(XmlReader reader)
        {
            if (IsIgnoring)
            {
                if (reader.Depth == ignoredDepth) ignoredDepth = -1;
                return;
            }

            PruneToDepth(reader.Depth);
            Parent = Grandparent = GreatGrandparent = null;
        }

        public void IgnoreCurrent(XmlReader reader)
        {
            PruneToDepth(reader.Depth);
            Parent = Grandparent = GreatGrandparent = null;
            Ignore(reader);
        }

        internal bool Reject(XmlReader reader, string description, List<string>? incomplete)
        {
            var message = $"{description} contains an unsupported MSBuild XML context.";
            if (incomplete is null) throw new InvalidOperationException(message);
            incomplete.Add(message);
            Ignore(reader);
            return false;
        }

        internal void Ignore(XmlReader reader)
        {
            if (!reader.IsEmptyElement) ignoredDepth = reader.Depth;
        }

        internal void PruneToDepth(int depth)
        {
            while (elements.Count > 0 && elements[^1].Depth >= depth)
            {
                elements.RemoveAt(elements.Count - 1);
            }
        }
    }



    internal sealed record GeneratedImport(
        string SourceFile,
        string Project,
        ImportApplicability Applicability,
        bool IsNested = false,
        string? PackageRoot = null,
        string? PackageRelativePath = null,
        CapabilityKind? Capability = null,
        string? RootAssetPath = null)
    {
        public bool AppliesTo(SurfaceContextKind context, string targetFramework, bool projectLevelCapability) =>
            context == (projectLevelCapability ? SurfaceContextKind.Project : SurfaceContextKind.Target) &&
            Applicability.AppliesTo(context, targetFramework);
    }


}
