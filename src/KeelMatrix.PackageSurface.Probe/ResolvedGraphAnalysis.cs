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

internal static class ResolvedGraphAnalysis
{


    internal static ProbeResult Analyze(
        string assetsFile,
        string projectRoot,
        bool strictContent,
        string? projectContext,
        string? selectedProjectPath,
        AnalysisBudget? budget,
        string? compilerApiVersion,
        FileSystemCaseSensitivity fileSystemCaseSensitivity)
    {
        var incomplete = new List<string>();
        using var fileSystemScope = FileSystemComparisonContext.Push(projectRoot, fileSystemCaseSensitivity);
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            budget ??= new AnalysisBudget();
            budget.Add(FileLength(assetsFile), "metadata");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var root = document.RootElement;
            ValidateRestoreJsonStructure(root, incomplete);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
            }
            _ = ValidateAssetsFormat(root, incomplete);
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            if (libraries.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("project.assets.json has an invalid libraries object.");
            }

            foreach (var _ in libraries.EnumerateObject())
            {
                budget.AddLibrary();
            }
            if (!root.TryGetProperty("targets", out var targets))
            {
                throw new InvalidDataException("project.assets.json has no targets object.");
            }

            if (targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("project.assets.json has an invalid targets object.");
            }

            foreach (var _ in targets.EnumerateObject())
            {
                budget.AddTargetGraph();
            }

            var restoreIdentities = BuildRestoreIdentityIndex(root, libraries, targets, incomplete);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
            }

            var directAssetRules = ReadDirectPackageAssetRules(root, restoreIdentities, incomplete);
            var reachablePackagesByTarget = BuildReachablePackageClosures(root, projectRoot, targets, libraries, restoreIdentities, directAssetRules, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
            }
            compilerApiVersion ??= ReadCompilerApiVersion(root);
            FileSystemComparisonContext.Current.RegisterProjectPath(
                projectContext,
                selectedProjectPath ?? ReadProjectPath(root) ?? projectRoot);
            var entries = new List<SurfaceEntry>();
            var projectEntries = new Dictionary<SurfaceIdentity, List<SurfaceEntry>>(SurfaceIdentityComparer.Instance);
            var resolvedPackages = new HashSet<PackageIdentity>(PackageIdentityComparer.Instance);
            var accumulatedEntries = 0;
            var reachablePackageKeys = reachablePackagesByTarget.Values
                .SelectMany(value => value.ReachablePackageKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageRoots = ResolvePackageRoots(restoreIdentities, libraries, packageFolders, incomplete, reachablePackageKeys);
            FileSystemComparisonContext.Current.RegisterPackageRoots(projectContext, packageRoots);
            var packageInventories = BuildPackageInventories(packageRoots, restoreIdentities, libraries, incomplete, budget);
            ValidateRestoreEvidence(root, libraries, targets, restoreIdentities, packageFolders, packageRoots, packageInventories, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
            }

            var targetFrameworkContexts = restoreIdentities.FrameworkContexts;
            var expectedGeneratedImportSources = new HashSet<string>(ProjectPathStringComparer);
            var generatedImports = ReadGeneratedImports(root, assetsFile, projectRoot, selectedProjectPath, restoreIdentities, packageRoots, packageInventories, libraries, targetFrameworkContexts, incomplete, budget, expectedGeneratedImportSources);
            ValidateGeneratedImportEvidence(generatedImports, targets, restoreIdentities, reachablePackagesByTarget, packageRoots, packageInventories, libraries, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
            }

            var projectLanguage = ReadProjectLanguage(root, projectRoot, selectedProjectPath, incomplete);
            var isMultiTargetingProject = restoreIdentities.Targets.Values
                .Select(target => target.FrameworkKey)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() > 1;

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Target {target.Name} is not an object.");
                }

                if (!restoreIdentities.TryGetTarget(target.Name, out var targetIdentity))
                {
                    incomplete.Add($"Restore evidence contains an unresolved target identity: {target.Name}.");
                    continue;
                }

                var targetFrameworkKey = targetIdentity.FrameworkKey;
                var tfm = targetIdentity.TargetFramework;
                var rid = targetIdentity.RuntimeIdentifier;
                var targetAlias = targetIdentity.TargetAlias;
                var closure = reachablePackagesByTarget[targetIdentity.CanonicalKey];
                var reachablePackages = closure.ReachablePackageKeys;
                var targetDirectRules = directAssetRules.TryGetValue(targetFrameworkKey, out var rules)
                    ? rules
                    : new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
                var directPackages = closure.DirectPackageKeys;
                var analyzerPackages = closure.AnalyzerPackageKeys;
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var packageIdentity, out var library))
                    {
                        incomplete.Add($"Target {target.Name} refers to missing library {package.Name}.");
                        continue;
                    }

                    var packageId = packageIdentity.Id;
                    var version = packageIdentity.Version;
                    var libraryKey = packageIdentity.CanonicalKey;

                    if (library.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String &&
                        typeElement.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadProjectDependencies(package.Value, target.Value, libraryKey, restoreIdentities, incomplete, budget);
                        continue;
                    }

                    if (!library.TryGetProperty("type", out typeElement) || typeElement.ValueKind != JsonValueKind.String ||
                        !typeElement.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase))
                    {
                        incomplete.Add($"Library {libraryKey} has an unsupported library type.");
                        continue;
                    }

                    if (!reachablePackages.Contains(libraryKey))
                    {
                        incomplete.Add($"Target {target.Name} contains a package that is not reachable from a project or direct root.");
                        continue;
                    }

                    resolvedPackages.Add(packageIdentity.Identity);

                    if (!library.TryGetProperty("path", out var pathElement))
                    {
                        incomplete.Add($"Library {libraryKey} has no package path.");
                        continue;
                    }

                    if (pathElement.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(pathElement.GetString()))
                    {
                        incomplete.Add($"Library {libraryKey} has an invalid package path.");
                        continue;
                    }

                    var libraryPath = pathElement.GetString()!;
                    if (!packageRoots.TryGetValue(libraryKey, out var packageRoot))
                    {
                        continue;
                    }

                    var files = packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
                        ? inventory.Order(StringComparer.Ordinal).ToArray()
                        : ReadLibraryFiles(library, libraryKey, incomplete, budget).ToArray();
                    var selectedAnalyzers = SelectAnalyzerPaths(files, projectLanguage, analyzerPackages, libraryKey, incomplete, compilerApiVersion);
                    var targetAssets = package.Value;
                    foreach (var relativePath in files)
                    {
                        budget.AddOperation("resolved asset");
                        if (!TryGetCapability(relativePath, out var capability))
                        {
                            continue;
                        }

                        var physicalPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        var resolved = TryResolveExistingFilePath(physicalPath, out var actualPath);
                        var safePath = resolved && IsSafeResolvedFile(packageRoot, actualPath);
                        var present = safePath;
                        if (present) physicalPath = actualPath;
                        var reason = present ? null : safePath
                            ? $"Reachable asset is missing from resolved package contents: {relativePath}."
                            : $"Reachable asset resolves through an unsafe package path: {relativePath}.";
                        if (!present)
                        {
                            incomplete.Add($"{libraryKey}: {reason}");
                        }
                        else
                        {
                            budget.Add(FileLength(physicalPath), "package asset");
                        }

                        var context = capability == CapabilityKind.BuildMultiTargeting
                            ? SurfaceContextKind.Project
                            : SurfaceContextKind.Target;
                        if (present && capability == CapabilityKind.CompilerExtension && !ValidateCompilerMetadata(physicalPath, out var metadataReason))
                        {
                            incomplete.Add($"{libraryKey}: analyzer metadata is invalid for {relativePath}: {metadataReason}");
                            reason = $"Compiler extension metadata is invalid: {relativePath}.";
                        }

                        IReadOnlyList<string>? observedPrimitives = null;
                        if (present && IsBuildCapability(capability))
                        {
                            try
                            {
                                observedPrimitives = InspectMsBuildXml(physicalPath);
                            }
                            catch (XmlException)
                            {
                                incomplete.Add($"{libraryKey}: package MSBuild file {relativePath} is malformed.");
                                reason = $"Package MSBuild XML is malformed: {relativePath}.";
                            }
                            catch (InvalidOperationException)
                            {
                                incomplete.Add($"{libraryKey}: package MSBuild file {relativePath} is unsupported.");
                                reason = $"Package MSBuild XML is unsupported: {relativePath}.";
                            }
                        }

                        var active = IsActive(capability, relativePath, packageIdentity.Identity, analyzerPackages, selectedAnalyzers, packageRoot, targetAssets, context, targetAlias, generatedImports, expectedGeneratedImportSources, isMultiTargetingProject, projectLanguage, ref reason, incomplete, libraryKey);
                        var sha = present && strictContent && CapabilityPolicy.IsStrictContentEligible(capability, present, active)
                            ? ComputeSha256(physicalPath, budget)
                            : null;
                        var entry = new SurfaceEntry(
                            context == SurfaceContextKind.Project ? null : tfm,
                            context == SurfaceContextKind.Project ? null : rid,
                            context,
                            packageId,
                            version,
                            directPackages.Contains(libraryKey) ? "direct" : "transitive",
                            capability,
                            relativePath,
                            present,
                            active,
                             sha,
                             reason is not null,
                             reason,
                             projectContext,
                             observedPrimitives);
                        if (context == SurfaceContextKind.Project)
                        {
                            var key = SurfaceIdentity.ForProjectAggregation(entry);
                            if (!projectEntries.TryGetValue(key, out var candidates))
                            {
                                candidates = new List<SurfaceEntry>();
                                projectEntries.Add(key, candidates);
                            }

                            candidates.Add(entry);
                            if (++accumulatedEntries > MaxEntries)
                            {
                                throw new InvalidDataException("The resolved capability surface exceeds the supported entry limit.");
                            }
                        }
                        else
                        {
                            entries.Add(entry);
                            if (++accumulatedEntries > MaxEntries)
                            {
                                throw new InvalidDataException("The resolved capability surface exceeds the supported entry limit.");
                            }
                        }
                    }
                }

                foreach (var nestedEntry in CreateNestedImportEntries(
                    generatedImports,
                    restoreIdentities,
                    packageRoots,
                    packageInventories,
                    libraries,
                    tfm,
                    rid,
                    directPackages,
                    projectContext,
                    strictContent,
                    budget,
                    incomplete))
                {
                    if (nestedEntry.Context == SurfaceContextKind.Project)
                    {
                        var key = SurfaceIdentity.ForProjectAggregation(nestedEntry);
                        if (!projectEntries.TryGetValue(key, out var candidates))
                        {
                            candidates = new List<SurfaceEntry>();
                            projectEntries.Add(key, candidates);
                        }

                        candidates.Add(nestedEntry);
                    }
                    else
                    {
                        entries.Add(nestedEntry);
                    }

                    if (++accumulatedEntries > MaxEntries)
                    {
                        throw new InvalidDataException("The resolved capability surface exceeds the supported entry limit.");
                    }
                }
            }

            entries.AddRange(projectEntries.Values.Select(AggregateProjectEntries));
            return new ProbeResult(Deduplicate(entries), NormalizeIncompleteReasons(incomplete), resolvedPackages.Count);
        }
        catch (InvalidDataException ex)
        {
            incomplete.Add(IsSafeFailureText(ex.Message) ? ex.Message : "The resolved restore graph could not be analyzed completely.");
            return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            incomplete.Add("The resolved restore graph could not be analyzed completely.");
            return new ProbeResult(Array.Empty<SurfaceEntry>(), NormalizeIncompleteReasons(incomplete));
        }
    }



    internal static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(SurfaceIdentity.From, SurfaceIdentityComparer.Instance)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.Context)
        .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Capability)
            .ThenBy(entry => entry.PackageRelativePath, StringComparer.Ordinal)
        .ToArray();



    internal static SurfaceEntry AggregateProjectEntries(IEnumerable<SurfaceEntry> candidates)
    {
        var ordered = candidates
            .OrderByDescending(entry => entry.Relationship.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(entry => entry.Active)
            .ThenByDescending(entry => entry.Present)
            .ThenBy(entry => entry.Sha256, StringComparer.Ordinal)
            .ThenBy(entry => entry.IncompleteReason, StringComparer.Ordinal)
            .ToArray();
        var first = ordered[0];
        var incomplete = ordered.Any(entry => entry.Incomplete);
        var reason = ordered.Select(entry => entry.IncompleteReason).FirstOrDefault(value => value is not null);
        var eligible = CapabilityPolicy.IsStrictContentEligible(first.Capability, ordered.All(entry => entry.Present), ordered.Any(entry => entry.Active));
        var hash = eligible ? ordered.Select(entry => entry.Sha256).FirstOrDefault(value => value is not null) : null;
        var primitives = ordered
            .SelectMany(entry => entry.ObservedPrimitives ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return first with
        {
            Relationship = ordered.Any(entry => entry.Relationship.Equals("direct", StringComparison.OrdinalIgnoreCase)) ? "direct" : "transitive",
            Present = ordered.All(entry => entry.Present),
            Active = ordered.Any(entry => entry.Active),
            Sha256 = hash,
            Incomplete = incomplete,
            IncompleteReason = reason,
            ObservedPrimitives = primitives.Length == 0 ? null : primitives
        };
    }


}
