using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace KeelMatrix.PackageSurface.Probe;

public static class ResolvedGraphClassifier
{
    private const int MaxJsonDepth = 64;
    private const int MaxTargets = 512;
    private const int MaxLibraries = 20_000;
    private const int MaxFilesPerLibrary = 20_000;
    private const int MaxGeneratedImportFiles = 256;
    private const int MaxEntries = 50_000;
    private const int MaxDependencyNodes = 20_000;
    private const int MaxDependencyDepth = 256;
    private const int MaxConditionLength = 64 * 1024;
    private const int MaxConditionTokens = 2_048;
    private const int MaxConditionDepth = 64;
    private const int MaxNestedImports = 512;
    private const long MaxMetadataFileBytes = 16 * 1024 * 1024;
    private const long MaxXmlCharacters = 8 * 1024 * 1024;
    private const int MaxXmlDepth = 64;
    private const long MaxTotalBytes = 512 * 1024 * 1024;
    private const long MaxTotalHashBytes = 256 * 1024 * 1024;

    private static readonly Regex PropertyComparison = new(
        "^\\s*['\\\"]?\\$\\(\\s*(?<property>[A-Za-z_][A-Za-z0-9_.-]*)\\s*\\)['\\\"]?\\s*(?<operator>==|!=)\\s*['\\\"](?<value>[^'\\\"]*)['\\\"]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExistsCondition = new(
        "^\\s*Exists\\s*\\(\\s*['\\\"](?<path>[^'\\\"]*)['\\\"]\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static ProbeResult Analyze(string assetsFile, string projectRoot, bool strictContent = true, string? projectContext = null, string? selectedProjectPath = null)
    {
        var incomplete = new List<string>();
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            var budget = new WorkBudget();
            budget.Add(FileLength(assetsFile), "metadata");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var root = document.RootElement;
            ValidateAssetsFormat(root, incomplete);
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            if (libraries.ValueKind != JsonValueKind.Object || libraries.EnumerateObject().Take(MaxLibraries + 1).Count() > MaxLibraries)
            {
                throw new InvalidDataException("project.assets.json contains too many resolved libraries.");
            }
            var directAssetRules = ReadDirectPackageAssetRules(root);
            var targetAliases = ReadTargetAliases(root);
            var entries = new List<SurfaceEntry>();
            var projectEntries = new Dictionary<string, List<SurfaceEntry>>(StringComparer.OrdinalIgnoreCase);
            var resolvedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var accumulatedEntries = 0;

            if (!root.TryGetProperty("targets", out var targets))
            {
                throw new InvalidDataException("project.assets.json has no targets object.");
            }

            if (targets.ValueKind != JsonValueKind.Object || targets.EnumerateObject().Take(MaxTargets + 1).Count() > MaxTargets)
            {
                throw new InvalidDataException("project.assets.json contains too many target graphs.");
            }
            ValidateRestoreEvidence(root, libraries, targets, packageFolders, incomplete);
            var packageRoots = ResolvePackageRoots(libraries, packageFolders, incomplete);
            var expectedGeneratedImportSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedImports = ReadGeneratedImports(root, assetsFile, projectRoot, selectedProjectPath, packageRoots, incomplete, budget, expectedGeneratedImportSources);
            ValidateGeneratedImportEvidence(generatedImports, packageRoots, libraries, incomplete);
            var projectLanguage = ReadProjectLanguage(root, projectRoot, selectedProjectPath, incomplete);
            var isMultiTargetingProject = targets.EnumerateObject().Select(target => SplitTarget(target.Name).Tfm).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Target {target.Name} is not an object.");
                }

                var (tfm, rid) = SplitTarget(target.Name);
                var targetAlias = targetAliases.TryGetValue(target.Name, out var alias) ? alias : tfm;
                var targetDirectRules = directAssetRules.TryGetValue(tfm, out var rules)
                    ? rules
                    : new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
                var directPackages = targetDirectRules.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
                var analyzerPackages = DetermineAnalyzerPackages(target.Value, targetDirectRules);
                foreach (var package in target.Value.EnumerateObject())
                {
                    var (packageId, version) = SplitPackageKey(package.Name);
                    var libraryKey = packageId + "/" + version;
                    if (!TryGetPropertyIgnoreCase(libraries, libraryKey, out var library))
                    {
                        incomplete.Add($"Target {target.Name} refers to missing library {libraryKey}.");
                        continue;
                    }

                    if (library.TryGetProperty("type", out var typeElement) && typeElement.ValueKind == JsonValueKind.String &&
                        typeElement.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase))
                    {
                        ReadProjectDependencies(package.Value, target.Value, libraryKey, incomplete);
                        continue;
                    }

                    if (!library.TryGetProperty("type", out typeElement) || typeElement.ValueKind != JsonValueKind.String ||
                        !typeElement.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase))
                    {
                        incomplete.Add($"Library {libraryKey} has an unsupported library type.");
                        continue;
                    }

                    resolvedPackages.Add(libraryKey);

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

                    var files = ReadLibraryFiles(library, libraryKey, incomplete);
                    var selectedAnalyzers = SelectAnalyzerPaths(files, projectLanguage, targetAlias, analyzerPackages, packageId, libraryKey, incomplete);
                    var targetAssets = package.Value;
                    foreach (var relativePath in files)
                    {
                        if (!TryGetCapability(relativePath, out var capability))
                        {
                            continue;
                        }

                        var physicalPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        var safePath = IsSafeResolvedFile(packageRoot, physicalPath);
                        var present = safePath && File.Exists(physicalPath);
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

                        var active = IsActive(capability, relativePath, packageId, libraryPath, analyzerPackages, selectedAnalyzers, packageRoot, targetAssets, context, targetAlias, generatedImports, expectedGeneratedImportSources, isMultiTargetingProject, projectLanguage, ref reason, incomplete, libraryKey);
                        var sha = present && strictContent && CapabilityPolicy.IsStrictContentEligible(capability, present, active)
                            ? ComputeSha256(physicalPath, budget)
                            : null;
                        var entry = new SurfaceEntry(
                            context == SurfaceContextKind.Project ? null : tfm,
                            context == SurfaceContextKind.Project ? null : rid,
                            context,
                            packageId,
                            version,
                            directPackages.Contains(packageId) ? "direct" : "transitive",
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
                            var key = string.Join("|", entry.PackageId, entry.Version, entry.Capability, entry.PackageRelativePath);
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
            }

            entries.AddRange(projectEntries.Values.Select(AggregateProjectEntries));
            return new ProbeResult(Deduplicate(entries), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), resolvedPackages.Count);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            incomplete.Add("The resolved restore graph could not be analyzed completely.");
            return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static void ReadProjectDependencies(JsonElement projectNode, JsonElement targetAssets, string projectKey, List<string> incomplete)
    {
        if (!projectNode.TryGetProperty("dependencies", out var dependencies)) return;
        if (dependencies.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Project node {projectKey} has invalid dependency metadata.");
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            var found = targetAssets.EnumerateObject().Any(candidate =>
            {
                var (id, _) = SplitPackageKey(candidate.Name);
                return id.Equals(dependency.Name, StringComparison.OrdinalIgnoreCase);
            });
            if (!found) incomplete.Add($"Project node {projectKey} refers to missing package dependency {dependency.Name}.");
        }
    }

    private static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(entry => string.Join("|", entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Capability, entry.PackageRelativePath), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.Context)
        .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
        .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Capability)
        .ThenBy(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static SurfaceEntry AggregateProjectEntries(IEnumerable<SurfaceEntry> candidates)
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

    private static Dictionary<string, Dictionary<string, PackageAssetRule>> ReadDirectPackageAssetRules(JsonElement root)
    {
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has no frameworks object.");
        }

        var result = new Dictionary<string, Dictionary<string, PackageAssetRule>>(StringComparer.OrdinalIgnoreCase);
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Framework {framework.Name} is not an object.");
            }

            if (!framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                continue;
            }

            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Framework {framework.Name} has an invalid dependencies object.");
            }

            var frameworkRules = new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Dependency {dependency.Name} has an invalid metadata object.");
                }

                frameworkRules[dependency.Name] = new PackageAssetRule(ReadAnalyzersIncluded(dependency.Value, dependency.Name));
            }
            result[framework.Name] = frameworkRules;
        }

        return result;
    }

    private static bool ReadAnalyzersIncluded(JsonElement dependency, string packageId)
    {
        var included = true;
        if (dependency.TryGetProperty("include", out var include))
        {
            if (include.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Dependency {packageId} has a non-string include selector.");
            }

            included = AssetSelectorContains(include.GetString(), "all") || AssetSelectorContains(include.GetString(), "analyzers");
        }

        if (dependency.TryGetProperty("exclude", out var exclude))
        {
            if (exclude.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Dependency {packageId} has a non-string exclude selector.");
            }

            if (AssetSelectorContains(exclude.GetString(), "all") || AssetSelectorContains(exclude.GetString(), "analyzers"))
            {
                included = false;
            }
        }

        return included;
    }

    private static bool AssetSelectorContains(string? selector, string expected) =>
        selector?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Any(value => value.Equals(expected, StringComparison.OrdinalIgnoreCase)) == true;

    private static HashSet<string> DetermineAnalyzerPackages(JsonElement targetAssets, IReadOnlyDictionary<string, PackageAssetRule> directAssetRules)
    {
        var packageProperties = targetAssets.EnumerateObject().ToArray();
        var packageKeysById = packageProperties
            .Select(package => (Key: package.Name, Id: SplitPackageKey(package.Name).Id))
            .GroupBy(item => item.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(item => item.Key).ToArray(), StringComparer.OrdinalIgnoreCase);
        var excludedAnalyzerPackages = directAssetRules
            .Where(rule => !rule.Value.AnalyzersIncluded)
            .Select(rule => rule.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<(string Key, int Depth)>();

        foreach (var direct in directAssetRules)
        {
            if (!direct.Value.AnalyzersIncluded || !packageKeysById.TryGetValue(direct.Key, out var roots))
            {
                continue;
            }

            foreach (var root in roots)
            {
                work.Push((root, 0));
            }
        }

        while (work.Count > 0)
        {
            var (packageKey, depth) = work.Pop();
            if (!visited.Add(packageKey)) continue;
            if (depth > MaxDependencyDepth || visited.Count > MaxDependencyNodes)
            {
                throw new InvalidDataException("The resolved dependency graph exceeds the supported traversal limits.");
            }

            if (!TryGetPropertyIgnoreCase(targetAssets, packageKey, out var package) || package.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} is not an object.");
            }

            var packageId = SplitPackageKey(packageKey).Id;
            if (excludedAnalyzerPackages.Contains(packageId)) continue;
            active.Add(packageId);
            if (!package.TryGetProperty("dependencies", out var dependencies)) continue;
            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} has an invalid dependencies object.");
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (!packageKeysById.TryGetValue(dependency.Name, out var dependencyKeys))
                {
                    throw new InvalidDataException($"Target package {packageKey} refers to missing dependency {dependency.Name}.");
                }

                foreach (var dependencyKey in dependencyKeys)
                {
                    work.Push((dependencyKey, depth + 1));
                }
            }
        }

        return active;
    }

    private static Dictionary<string, string> ReadTargetAliases(JsonElement root)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks))
        {
            throw new InvalidDataException("project.assets.json has no frameworks object.");
        }

        if (project.ValueKind != JsonValueKind.Object || frameworks.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has an invalid frameworks object.");
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Framework {framework.Name} is not an object.");
            }

            if (framework.Value.TryGetProperty("targetAlias", out var alias) && alias.ValueKind == JsonValueKind.String)
            {
                var targetKey = framework.Name;
                result[targetKey] = alias.GetString()!;
            }
        }

        return result;
    }

    private static ProjectLanguage ReadProjectLanguage(JsonElement root, string projectRoot, string? selectedProjectPath, List<string> incomplete)
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
            incomplete.Add("Restore metadata does not identify the consuming project.");
            return ProjectLanguage.Unknown;
        }

        if (selectedProjectPath is not null && !PathsEqual(projectPath, selectedProjectPath))
        {
            incomplete.Add("Restore metadata identifies a different project than the selected input.");
            return ProjectLanguage.Unknown;
        }

        if (projectPath is not null && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.CSharp;
        if (projectPath is not null && projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.VisualBasic;
        if (projectPath is not null && projectPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectLanguage.FSharp;
        }

        return ProjectLanguage.Unknown;
    }

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);

    private static string[] ReadPackageFolders(JsonElement root, List<string> incomplete)
    {
        if (!root.TryGetProperty("packageFolders", out var folders))
        {
            throw new InvalidDataException("project.assets.json has no packageFolders object.");
        }

        if (folders.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("project.assets.json has an invalid packageFolders object.");
        }

        var result = folders.EnumerateObject().Select(folder => folder.Name).Where(folder => !string.IsNullOrWhiteSpace(folder)).ToArray();
        if (result.Length == 0)
        {
            throw new InvalidDataException("project.assets.json has no resolved package folder.");
        }

        return result;
    }

    private static void ValidateAssetsFormat(JsonElement root, List<string> incomplete)
    {
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var value) || value is < 1 or > 3)
        {
            incomplete.Add("The restore assets format version is missing or unsupported.");
        }
    }

    private static void ValidateRestoreEvidence(
        JsonElement root,
        JsonElement libraries,
        JsonElement targets,
        IReadOnlyList<string> packageFolders,
        List<string> incomplete)
    {
        if (libraries.ValueKind != JsonValueKind.Object || targets.ValueKind != JsonValueKind.Object) return;
        AddDuplicatePropertyReasons(libraries, "library identity", incomplete);
        AddDuplicatePropertyReasons(targets, "target identity", incomplete);

        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore evidence has no declared framework graph.");
            return;
        }

        AddDuplicatePropertyReasons(frameworks, "framework identity", incomplete);
        var declaredFrameworks = frameworks.EnumerateObject().Select(framework => framework.Name).ToArray();
        var targetNames = targets.EnumerateObject().Select(target => target.Name).ToArray();
        foreach (var framework in declaredFrameworks)
        {
            if (!targetNames.Any(target => SplitTarget(target).Tfm.Equals(framework, StringComparison.OrdinalIgnoreCase)))
            {
                incomplete.Add($"Declared framework {framework} has no resolved target graph.");
            }
        }

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target {target.Name} is not an object.");
                continue;
            }

            var packageIds = target.Value.EnumerateObject()
                .Select(property => SplitPackageKey(property.Name).Id)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var package in target.Value.EnumerateObject())
            {
                if (!TryGetPropertyIgnoreCase(libraries, package.Name, out var library))
                {
                    incomplete.Add($"Target {target.Name} refers to missing library {package.Name}.");
                    continue;
                }

                if (package.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target {target.Name} package {package.Name} has malformed asset metadata.");
                    continue;
                }

                ValidateTargetPackageAssets(package.Value, library, package.Name, incomplete);
                if (package.Value.TryGetProperty("dependencies", out var dependencies))
                {
                    if (dependencies.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Target package {package.Name} has malformed dependency metadata.");
                    }
                    else
                    {
                        foreach (var dependency in dependencies.EnumerateObject())
                        {
                            if (!packageIds.Contains(dependency.Name))
                            {
                                incomplete.Add($"Target package {package.Name} refers to missing dependency {dependency.Name}.");
                            }
                        }
                    }
                }
            }

            foreach (var framework in frameworks.EnumerateObject().Where(framework => framework.Name.Equals(SplitTarget(target.Name).Tfm, StringComparison.OrdinalIgnoreCase)))
            {
                if (!framework.Value.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) continue;
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (!packageIds.Contains(dependency.Name))
                    {
                        incomplete.Add($"Declared framework {framework.Name} refers to unresolved dependency {dependency.Name}.");
                    }
                }
            }
        }
    }

    private static void ValidateTargetPackageAssets(JsonElement package, JsonElement library, string packageKey, List<string> incomplete)
    {
        var files = ReadLibraryFiles(library, packageKey, incomplete)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var group in package.EnumerateObject())
        {
            if (group.Name is "dependencies" or "type") continue;
            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target package {packageKey} has malformed asset group {group.Name}.");
                continue;
            }

            if (group.Name.Equals("contentFiles", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var content in group.Value.EnumerateObject())
                {
                    if (!files.Contains(content.Name.Replace('\\', '/')))
                    {
                        incomplete.Add($"Target package {packageKey} references a content file absent from its package inventory.");
                    }
                }
                continue;
            }

            foreach (var asset in group.Value.EnumerateObject())
            {
                var assetPath = asset.Name.Replace('\\', '/');
                if (!TryNormalizeRelativePath(assetPath, out _))
                {
                    incomplete.Add($"Target package {packageKey} contains an unsafe asset reference.");
                    continue;
                }

                if (!files.Contains(assetPath))
                {
                    incomplete.Add($"Target package {packageKey} references an asset absent from its package inventory.");
                }
            }
        }
    }

    private static void AddDuplicatePropertyReasons(JsonElement value, string description, List<string> incomplete)
    {
        var duplicate = value.EnumerateObject()
            .GroupBy(property => property.Name, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .FirstOrDefault();
        if (duplicate is not null)
        {
            incomplete.Add($"Restore evidence contains an ambiguous {description}.");
        }
    }

    private static Dictionary<string, string> ResolvePackageRoots(JsonElement libraries, IReadOnlyList<string> packageFolders, List<string> incomplete)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var library in libraries.EnumerateObject())
        {
            if (!library.Value.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase)) continue;
            var (packageId, version) = SplitPackageKey(library.Name);
            if (!library.Value.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"Library {library.Name} has no valid package path.");
                continue;
            }

            var packageRoot = ResolvePackageRoot(packageId, version, path.GetString()!, packageFolders.ToArray(), incomplete);
            if (packageRoot is not null)
            {
                result[library.Name] = packageRoot;
            }
        }

        return result;
    }

    private static void ValidateGeneratedImportEvidence(
        IReadOnlyList<GeneratedImport> imports,
        IReadOnlyDictionary<string, string> packageRoots,
        JsonElement libraries,
        List<string> incomplete)
    {
        foreach (var import in imports)
        {
            var project = NormalizeText(import.Project);
            if (!project.StartsWith("$(NuGetPackageRoot)/", StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = project["$(NuGetPackageRoot)/".Length..];
            var match = packageRoots.Keys.FirstOrDefault(key => suffix.StartsWith(NormalizeText(key) + "/", StringComparison.OrdinalIgnoreCase));
            if (match is null) continue;
            var relative = suffix[(match.Length + 1)..];
            if (!TryGetPropertyIgnoreCase(libraries, match, out var library))
            {
                incomplete.Add("Generated NuGet import evidence refers to an unrepresented package library.");
                continue;
            }

            var files = ReadLibraryFiles(library, match, incomplete);
            if (!files.Any(file => file.Equals(relative, StringComparison.OrdinalIgnoreCase)))
            {
                incomplete.Add("Generated NuGet import evidence refers to a file absent from the resolved package inventory.");
            }
        }
    }

    private static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > MaxFilesPerLibrary)
        {
            incomplete.Add($"Library {libraryKey} has no files array.");
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            if (file.ValueKind != JsonValueKind.String || !TryNormalizeRelativePath(file.GetString()!, out var normalized))
            {
                incomplete.Add($"Library {libraryKey} contains an invalid relative asset path.");
                continue;
            }

            if (result.Any(existing => existing.Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            {
                incomplete.Add($"{libraryKey} contains a duplicate asset path: {normalized}.");
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string? ResolvePackageRoot(string packageId, string version, string libraryPath, string[] packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {packageId}/{version} has an unsafe cache-relative path.");
            return null;
        }

        foreach (var folder in packageFolders)
        {
            if (!Path.IsPathRooted(folder))
            {
                continue;
            }

            var candidate = Path.GetFullPath(Path.Combine(folder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(candidate) && IsSafeResolvedDirectory(folder, candidate))
            {
                return candidate;
            }
        }

        incomplete.Add($"Package {packageId}/{version} was not found in the resolved package folders.");
        return null;
    }

    private static List<GeneratedImport> ReadGeneratedImports(
        JsonElement root,
        string assetsFile,
        string projectRoot,
        string? selectedProjectPath,
        IReadOnlyDictionary<string, string> packageRoots,
        List<string> incomplete,
        WorkBudget budget,
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

        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!File.Exists(file))
            {
                incomplete.Add($"Expected generated import file {Path.GetFileName(file)} is missing from the assets directory.");
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
                var conditionStack = new List<ConditionClause>();
                var exceededDepth = false;
                while (reader.Read())
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.Depth > MaxXmlDepth)
                        {
                            exceededDepth = true;
                            break;
                        }

                        var elementName = reader.LocalName;
                        var condition = reader.GetAttribute("Condition");
                        if (elementName.Equals("Import", StringComparison.Ordinal) && reader.GetAttribute("Project") is string project)
                        {
                            var conditions = conditionStack
                                .Append(new ConditionClause(elementName, condition))
                                .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                                .ToArray();
                            var applicability = DetermineApplicability(conditions, project, out var reason);
                            if (!applicability.IsKnown)
                            {
                                incomplete.Add($"Generated import file {Path.GetFileName(file)} has an unsupported condition on {reason!.Owner}: {reason.Message}.");
                            }

                            result.Add(new GeneratedImport(Path.GetFileName(file), project, applicability));
                        }

                        if (!reader.IsEmptyElement)
                        {
                            conditionStack.Add(new ConditionClause(elementName, condition));
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && conditionStack.Count > 0)
                    {
                        conditionStack.RemoveAt(conditionStack.Count - 1);
                    }
                }

                if (exceededDepth)
                {
                    incomplete.Add($"Generated import file {Path.GetFileName(file)} exceeds the supported XML depth.");
                }

                foreach (var directImport in result.Where(import => import.SourceFile.Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase)).ToArray())
                {
                    string? resolutionReason = null;
                    if (directImport.Applicability.IsKnown &&
                        TryResolveStaticPackageImport(directImport.Project, file, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason))
                    {
                        WalkNestedImports(nestedPath, nestedRoot, directImport.SourceFile, directImport.Applicability.Condition, packageRoots, result, incomplete, new HashSet<string>(StringComparer.OrdinalIgnoreCase), 0);
                    }
                    else if (resolutionReason is not null && !resolutionReason.Equals("not a package import", StringComparison.Ordinal))
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

    private static void WalkNestedImports(
        string file,
        string packageRoot,
        string sourceFile,
        ConditionNode? inheritedCondition,
        IReadOnlyDictionary<string, string> packageRoots,
        List<GeneratedImport> result,
        List<string> incomplete,
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

            if (!visited.Add(Path.GetFullPath(current.File))) continue;
            if (!IsSafeResolvedFile(current.PackageRoot, current.File) || !File.Exists(current.File))
            {
                incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
                continue;
            }

            try
            {
                EnsureFileWithinLimit(current.File, MaxMetadataFileBytes, "nested package import");
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersFromEntities = 0,
                    MaxCharactersInDocument = MaxXmlCharacters,
                    IgnoreComments = true,
                    IgnoreWhitespace = true
                };
                using var reader = XmlReader.Create(current.File, settings);
                var conditionStack = new List<ConditionClause>();
                var nestedWork = new List<(string File, string PackageRoot, string SourceFile, ConditionNode? Condition, int Depth)>();
                while (reader.Read())
                {
                    if (reader.Depth > MaxXmlDepth)
                    {
                        incomplete.Add("A nested package import exceeds the supported XML depth.");
                        break;
                    }

                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        var condition = reader.GetAttribute("Condition");
                        if (reader.LocalName.Equals("Import", StringComparison.Ordinal) && reader.GetAttribute("Project") is string project)
                        {
                            var clauses = conditionStack
                                .Append(new ConditionClause(reader.LocalName, condition))
                                .Where(clause => !string.IsNullOrWhiteSpace(clause.Expression))
                                .ToArray();
                            var applicability = DetermineApplicability(clauses, project, out var failure);
                            if (!applicability.IsKnown)
                            {
                                incomplete.Add($"Nested package import contains an unsupported condition on {failure!.Owner}: {failure.Message}.");
                            }

                            string? resolutionReason = null;
                            var resolved = TryResolveStaticPackageImport(project, current.File, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason);
                            var combined = applicability.IsKnown
                                ? ImportApplicability.Known(CombineConditionNodes(current.Condition, applicability.Condition))
                                : applicability;
                            result.Add(new GeneratedImport(current.SourceFile, resolved ? nestedPath : project, combined));
                            if (combined.IsKnown && resolved)
                            {
                                nestedWork.Add((nestedPath, nestedRoot, current.SourceFile, combined.Condition, current.Depth + 1));
                            }
                            else if (resolutionReason is not null && !resolutionReason.Equals("not a package import", StringComparison.Ordinal))
                            {
                                incomplete.Add("Nested package import contains an unsupported static import target.");
                            }
                            else if (resolutionReason is "not a package import" && !Path.IsPathRooted(project) && !project.Contains("$(", StringComparison.Ordinal) &&
                                     !IsRelativeImportUnderAnyPackageRoot(current.File, project, packageRoots.Values))
                            {
                                incomplete.Add("Nested package import is outside the resolved package root or is missing from the package inventory.");
                            }
                        }

                        if (!reader.IsEmptyElement)
                        {
                            conditionStack.Add(new ConditionClause(reader.LocalName, condition));
                        }
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && conditionStack.Count > 0)
                    {
                        conditionStack.RemoveAt(conditionStack.Count - 1);
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

    private static bool IsRelativeImportUnderAnyPackageRoot(string currentFile, string importProject, IEnumerable<string> packageRoots)
    {
        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentFile))!;
        var fullFile = Path.GetFullPath(Path.Combine(currentDirectory, importProject.Replace('/', Path.DirectorySeparatorChar)));
        return packageRoots.Any(root =>
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase);
        });
    }

    private static ConditionNode? CombineConditionNodes(ConditionNode? left, ConditionNode? right) =>
        left is null ? right : right is null ? left : new AndCondition(left, right);

    private static bool TryResolveStaticPackageImport(
        string importProject,
        string currentFile,
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

        if (requested.Contains("$(", StringComparison.Ordinal) &&
            !requested.StartsWith("$(NuGetPackageRoot)/", StringComparison.OrdinalIgnoreCase))
        {
            reason = "dynamic import target";
            return false;
        }

        if (requested.StartsWith("$(NuGetPackageRoot)/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested["$(NuGetPackageRoot)/".Length..];
            foreach (var candidate in packageRoots)
            {
                var identity = NormalizeText(candidate.Key);
                if (!suffix.StartsWith(identity + "/", StringComparison.OrdinalIgnoreCase)) continue;
                var relative = suffix[(identity.Length + 1)..];
                if (!TryNormalizeRelativePath(relative, out _))
                {
                    reason = "unsafe package import target";
                    return false;
                }

                var candidatePath = Path.GetFullPath(Path.Combine(candidate.Value, relative.Replace('/', Path.DirectorySeparatorChar)));
                if (!IsSafeResolvedFile(candidate.Value, candidatePath))
                {
                    reason = "unsafe package import target";
                    return false;
                }

                if (!File.Exists(candidatePath))
                {
                    reason = "not a package import";
                    return false;
                }

                resolvedPath = candidatePath;
                packageRoot = candidate.Value;
                return true;
            }

            reason = "unresolved package import target";
            return false;
        }

        if (Path.IsPathRooted(importProject))
        {
            var absolute = Path.GetFullPath(importProject);
            foreach (var candidate in packageRoots.Values.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!IsSafeResolvedFile(candidate, absolute)) continue;
                if (!File.Exists(absolute))
                {
                    reason = "not a package import";
                    return false;
                }
                resolvedPath = absolute;
                packageRoot = candidate;
                return true;
            }

            reason = "not a package import";
            return false;
        }

        var currentDirectory = Path.GetDirectoryName(Path.GetFullPath(currentFile))!;
        var relativePath = Path.GetFullPath(Path.Combine(currentDirectory, importProject.Replace('/', Path.DirectorySeparatorChar)));
        foreach (var candidate in packageRoots.Values.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!IsSafeResolvedFile(candidate, relativePath)) continue;
            if (!File.Exists(relativePath))
            {
                reason = "not a package import";
                return false;
            }
            resolvedPath = relativePath;
            packageRoot = candidate;
            return true;
        }

        reason = "not a package import";
        return false;
    }

    private static bool IsActive(
        CapabilityKind capability,
        string relativePath,
        string packageId,
        string libraryPath,
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
                var referencesAsset = string.Equals(normalizedImport, fullAsset, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedImport, "$(NuGetPackageRoot)/" + NormalizeText(libraryPath) + "/" + relativePath, StringComparison.OrdinalIgnoreCase);
                return IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources) && referencesAsset;
            });
            var imported = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                var referencesAsset = string.Equals(normalizedImport, fullAsset, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedImport, "$(NuGetPackageRoot)/" + NormalizeText(libraryPath) + "/" + relativePath, StringComparison.OrdinalIgnoreCase);
                return IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources) &&
                       referencesAsset &&
                       import.AppliesTo(context, targetFramework, capability == CapabilityKind.BuildMultiTargeting);
            });
            var hasWrongPhaseImport = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                var referencesAsset = string.Equals(normalizedImport, fullAsset, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalizedImport, "$(NuGetPackageRoot)/" + NormalizeText(libraryPath) + "/" + relativePath, StringComparison.OrdinalIgnoreCase);
                return referencesAsset && !IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources);
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
            CapabilityKind.CompilerExtension => IsCompilerExtensionActive(assetName, projectLanguage, targetFramework, analyzerPackages, selectedAnalyzers, packageId, incomplete, libraryKey),
            CapabilityKind.CompileSourceInjection => IsCompileContentFile(targetAssets, assetName, projectLanguage, incomplete, libraryKey),
            CapabilityKind.NativeRuntime => ContainsAsset(targetAssets, "native", assetName, incomplete, libraryKey) || ContainsAsset(targetAssets, "runtime", assetName, incomplete, libraryKey),
            _ => false
        };
    }

    private static bool IsExpectedGeneratedImportSource(
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

    private static IEnumerable<string> GetExpectedGeneratedImportSources(JsonElement root, string projectRoot, string? selectedProjectPath, List<string> incomplete)
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

    private static bool IsBuildCapability(CapabilityKind capability) =>
        capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting;

    private static bool ContainsAsset(JsonElement targetAssets, string propertyName, string assetName, List<string> incomplete, string libraryKey)
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

            if (string.Equals(property.Name.Replace('\\', '/'), assetName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsBuildAsset(JsonElement targetAssets, CapabilityKind capability, string assetName, List<string> incomplete, string libraryKey)
    {
        var propertyName = capability switch
        {
            CapabilityKind.BuildProps => "build",
            CapabilityKind.BuildTargets => "build",
            CapabilityKind.BuildTransitive => "buildTransitive",
            CapabilityKind.BuildMultiTargeting => "buildMultiTargeting",
            _ => string.Empty
        };
        return propertyName.Length > 0 && ContainsAsset(targetAssets, propertyName, assetName, incomplete, libraryKey);
    }

    private static bool RequiresGeneratedImportEvidence(CapabilityKind capability, string assetName, bool isMultiTargetingProject)
    {
        if (capability == CapabilityKind.BuildMultiTargeting && !isMultiTargetingProject) return false;
        var segments = assetName.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length < 2 || !LooksLikeTargetFrameworkFolder(segments[1]);
    }

    private static bool LooksLikeTargetFrameworkFolder(string value) =>
        value.StartsWith("net", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase);

    private static HashSet<string> SelectAnalyzerPaths(
        IReadOnlyList<string> files,
        ProjectLanguage projectLanguage,
        string targetFramework,
        HashSet<string> analyzerPackages,
        string packageId,
        string libraryKey,
        List<string> incomplete)
    {
        var candidates = new List<(string Path, string? Language, Version? RoslynVersion, string? Framework)>();
        foreach (var path in files)
        {
            if (!path.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) || IsAnalyzerSatellite(path))
            {
                continue;
            }

            if (!TryParseAnalyzerPath(path, out var language, out var roslynVersion, out var framework))
            {
                incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
                continue;
            }

            if (framework is not null && !AnalyzerFrameworkApplies(framework, targetFramework))
            {
                continue;
            }

            if (language is not null && projectLanguage != ProjectLanguage.Unknown &&
                !language.Equals(ProjectLanguageCode(projectLanguage), StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (language is not null && projectLanguage == ProjectLanguage.Unknown)
            {
                continue;
            }

            if (analyzerPackages.Contains(packageId))
            {
                candidates.Add((path, language, roslynVersion, framework));
            }
        }

        var selected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in candidates.GroupBy(candidate => candidate.Language ?? string.Empty, StringComparer.OrdinalIgnoreCase))
        {
            var versioned = group.Where(candidate => candidate.RoslynVersion is not null).ToArray();
            var highest = versioned.Length == 0 ? null : versioned.Max(candidate => candidate.RoslynVersion);
            foreach (var candidate in group)
            {
                if (highest is null ? candidate.RoslynVersion is null : candidate.RoslynVersion == highest)
                {
                    selected.Add(candidate.Path);
                }
            }
        }

        return selected;
    }

    private static bool TryParseAnalyzerPath(string path, out string? language, out Version? roslynVersion, out string? framework)
    {
        language = null;
        roslynVersion = null;
        framework = null;
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 3 || !segments[0].Equals("analyzers", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var index = 2;
        if (segments[1].Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
        }
        else if (segments[1].Contains('.', StringComparison.Ordinal) &&
                 (segments[1].StartsWith("net", StringComparison.OrdinalIgnoreCase) ||
                  segments[1].StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
                  segments[1].StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)))
        {
            framework = segments[1];
        }
        else
        {
            return false;
        }

        if (index < segments.Length - 1 && segments[index].StartsWith("roslyn", StringComparison.OrdinalIgnoreCase))
        {
            var versionText = segments[index]["roslyn".Length..];
            if (!Version.TryParse(versionText, out roslynVersion)) return false;
            index++;
        }

        if (index < segments.Length - 1 && IsAnalyzerLanguage(segments[index]))
        {
            language = segments[index].ToLowerInvariant();
            index++;
        }

        return index == segments.Length - 1;
    }

    private static bool AnalyzerFrameworkApplies(string required, string target)
    {
        if (!TryParseFrameworkVersion(required, out var requiredKind, out var requiredVersion) ||
            !TryParseFrameworkVersion(target, out var targetKind, out var targetVersion))
        {
            return false;
        }

        if (requiredKind.Equals("netstandard", StringComparison.OrdinalIgnoreCase))
        {
            return targetKind.StartsWith("net", StringComparison.OrdinalIgnoreCase);
        }

        return requiredKind.Equals(targetKind, StringComparison.OrdinalIgnoreCase) && targetVersion >= requiredVersion;
    }

    private static bool TryParseFrameworkVersion(string value, out string kind, out Version version)
    {
        kind = string.Empty;
        version = new Version(0, 0);
        var separator = -1;
        for (var index = 0; index < value.Length; index++)
        {
            if (!char.IsDigit(value[index])) continue;
            separator = index;
            break;
        }

        if (separator <= 0 || !Version.TryParse(value[separator..], out var parsedVersion) || parsedVersion is null) return false;
        kind = value[..separator];
        version = parsedVersion;
        return true;
    }

    private static bool IsAnalyzerLanguage(string value) =>
        value.Equals("cs", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("vb", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("fs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnalyzerSatellite(string path) =>
        path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompilerExtensionActive(
        string path,
        ProjectLanguage language,
        string targetFramework,
        HashSet<string> analyzerPackages,
        IReadOnlySet<string> selectedAnalyzers,
        string packageId,
        List<string> incomplete,
        string libraryKey)
    {
        if (!Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (IsAnalyzerSatellite(path))
        {
            return false;
        }

        if (!TryParseAnalyzerPath(path, out var analyzerLanguage, out _, out var framework))
        {
            incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
            return false;
        }

        if (framework is not null && !AnalyzerFrameworkApplies(framework, targetFramework))
        {
            return false;
        }

        var languageSpecific = analyzerLanguage is not null;
        if (languageSpecific && language == ProjectLanguage.Unknown)
        {
            incomplete.Add($"{libraryKey}: consuming project language is required to determine compiler-extension applicability.");
            return false;
        }

        if (!analyzerPackages.Contains(packageId) ||
            (analyzerLanguage is not null && !analyzerLanguage.Equals(ProjectLanguageCode(language), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        return selectedAnalyzers.Contains(path);
    }

    private static bool IsCompileContentFile(JsonElement targetAssets, string assetName, ProjectLanguage projectLanguage, List<string> incomplete, string libraryKey)
    {
        if (!targetAssets.TryGetProperty("contentFiles", out var contentFiles))
        {
            return false;
        }

        if (contentFiles.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{libraryKey}: contentFiles metadata has an unsupported shape.");
            return false;
        }

        foreach (var contentFile in contentFiles.EnumerateObject())
        {
            if (!string.Equals(contentFile.Name.Replace('\\', '/'), assetName, StringComparison.OrdinalIgnoreCase)) continue;
            if (contentFile.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"{libraryKey}: content file metadata is missing for {assetName}.");
                return false;
            }

            if (!contentFile.Value.TryGetProperty("buildAction", out var buildAction))
            {
                incomplete.Add($"{libraryKey}: content file buildAction metadata is missing for {assetName}.");
                return false;
            }

            if (buildAction.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"{libraryKey}: content file buildAction metadata is not a string for {assetName}.");
                return false;
            }

            if (!buildAction.GetString()!.Equals("Compile", StringComparison.OrdinalIgnoreCase)) return false;
            var languageName = "any";
            if (contentFile.Value.TryGetProperty("codeLanguage", out var language))
            {
                if (language.ValueKind != JsonValueKind.String)
                {
                    incomplete.Add($"{libraryKey}: content file codeLanguage metadata is not a string for {assetName}.");
                    return false;
                }

                languageName = language.GetString()!;
            }

            if (!languageName.Equals("any", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("cs", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("vb", StringComparison.OrdinalIgnoreCase) &&
                !languageName.Equals("fs", StringComparison.OrdinalIgnoreCase))
            {
                incomplete.Add($"{libraryKey}: content file codeLanguage metadata is unsupported for {assetName}.");
                return false;
            }

            return languageName.Equals("any", StringComparison.OrdinalIgnoreCase) ||
                languageName.Equals(ProjectLanguageCode(projectLanguage), StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    private static string ProjectLanguageCode(ProjectLanguage language) => language switch
    {
        ProjectLanguage.CSharp => "cs",
        ProjectLanguage.VisualBasic => "vb",
        ProjectLanguage.FSharp => "fs",
        _ => string.Empty
    };

    private static bool TryGetCapability(string path, out CapabilityKind capability)
    {
        capability = default;
        var normalized = path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        if (normalized.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildTransitive;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("buildMultiTargeting/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildMultiTargeting;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("build/", StringComparison.OrdinalIgnoreCase))
        {
            capability = extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? CapabilityKind.BuildProps : CapabilityKind.BuildTargets;
            return extension.Equals(".props", StringComparison.OrdinalIgnoreCase) || extension.Equals(".targets", StringComparison.OrdinalIgnoreCase);
        }

        if (normalized.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) &&
            (extension.Equals(".dll", StringComparison.OrdinalIgnoreCase) || extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)))
        {
            capability = CapabilityKind.CompilerExtension;
            return true;
        }

        if (normalized.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.CompileSourceInjection;
            return true;
        }

        if (normalized.StartsWith("runtimes/", StringComparison.OrdinalIgnoreCase) && normalized.Contains("/native/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.NativeRuntime;
            return true;
        }

        if (normalized.StartsWith("tools/", StringComparison.OrdinalIgnoreCase) || normalized.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.ToolOrScriptPresent;
            return true;
        }

        return false;
    }

    private static (string Tfm, string? Rid) SplitTarget(string target)
    {
        var slash = target.IndexOf('/');
        return slash < 0 ? (target, null) : (target[..slash], target[(slash + 1)..]);
    }

    private static (string Id, string Version) SplitPackageKey(string key)
    {
        var slash = key.LastIndexOf('/');
        return slash < 0 ? (key, string.Empty) : (key[..slash], key[(slash + 1)..]);
    }

    private static bool TryNormalizeRelativePath(string value, out string normalized)
    {
        normalized = value.Replace('\\', '/');
        var parts = normalized.Split('/');
        if (string.IsNullOrWhiteSpace(normalized) || normalized.Contains('\0') || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Contains(':') || parts.Any(part => part is "" or "." or ".."))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    private static string NormalizeText(string value) => value.Replace('\\', '/').Trim();

    private static bool TryGetPropertyIgnoreCase(JsonElement objectElement, string propertyName, out JsonElement value)
    {
        if (objectElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in objectElement.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static ImportApplicability DetermineApplicability(
        IReadOnlyList<ConditionClause> conditions,
        string importProject,
        out ConditionFailure? failure)
    {
        failure = null;
        ConditionNode? combined = null;
        foreach (var condition in conditions)
        {
            var parsed = ParseCondition(condition.Expression!, importProject);
            if (!parsed.IsKnown)
            {
                failure = new ConditionFailure(condition.Owner, parsed.Reason!);
                return new ImportApplicability(null, parsed.Reason);
            }

            combined = combined is null ? parsed.Node : new AndCondition(combined, parsed.Node!);
        }

        return ImportApplicability.Known(combined);
    }

    private static ParsedCondition ParseCondition(string expression, string importProject)
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
            return CombineConditions(disjunction, importProject, static (left, right) => new OrCondition(left, right));
        }

        if (TrySplitTopLevel(normalized, "AND", out var conjunction))
        {
            return CombineConditions(conjunction, importProject, static (left, right) => new AndCondition(left, right));
        }

        var comparison = PropertyComparison.Match(normalized);
        if (comparison.Success)
        {
            var property = comparison.Groups["property"].Value;
            var @operator = comparison.Groups["operator"].Value;
            var value = comparison.Groups["value"].Value;
            if (property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
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
            if (TryProveExists(exists.Groups["path"].Value, importProject, out var existsValue, out var reason))
            {
                return ParsedCondition.Known(new ConstantCondition(existsValue));
            }

            return ParsedCondition.Unknown(reason!);
        }

        return ParsedCondition.Unknown("the expression is outside the supported condition grammar");
    }

    private static ParsedCondition CombineConditions(
        IReadOnlyList<string> expressions,
        string importProject,
        Func<ConditionNode, ConditionNode, ConditionNode> combine)
    {
        ConditionNode? combined = null;
        foreach (var expression in expressions)
        {
            var parsed = ParseCondition(expression, importProject);
            if (!parsed.IsKnown)
            {
                return ParsedCondition.Unknown($"a subexpression is unproven: {parsed.Reason}");
            }

            combined = combined is null ? parsed.Node : combine(combined, parsed.Node!);
        }

        return ParsedCondition.Known(combined!);
    }

    private static bool TryProveExists(string requestedPath, string importProject, out bool exists, out string? reason)
    {
        exists = false;
        reason = null;
        var requested = NormalizeText(requestedPath);
        var project = NormalizeText(importProject);
        if (requested.StartsWith("$(NuGetPackageRoot)/", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested["$(NuGetPackageRoot)/".Length..];
            if (suffix.Contains("$(", StringComparison.Ordinal) ||
                !project.EndsWith('/' + suffix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "the Exists expression is not the standard resolved-package-file guard";
                return false;
            }

            // Generated NuGet imports retain $(NuGetPackageRoot) in both the
            // Import and Exists expressions. The resolved package file is
            // checked independently when the asset entry is created.
            exists = true;
            return true;
        }

        if (Path.IsPathRooted(requestedPath) &&
            string.Equals(requested, project, StringComparison.OrdinalIgnoreCase))
        {
            exists = File.Exists(importProject);
            return true;
        }

        reason = "the Exists expression is supported only for the standard resolved-package-file guard";
        return false;
    }

    private static bool IsConditionWithinLimits(string expression)
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

    private static string StripOuterParentheses(string expression)
    {
        while (expression.Length >= 2 && expression[0] == '(' && expression[^1] == ')' && HasSingleOuterPair(expression))
        {
            expression = expression[1..^1].Trim();
        }

        return expression;
    }

    private static bool HasSingleOuterPair(string expression)
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

    private static bool TrySplitTopLevel(string expression, string operatorText, out string[] parts)
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

    private static bool IsConditionBoundary(string expression, int index) =>
        index < 0 || index >= expression.Length || !char.IsLetterOrDigit(expression[index]) && expression[index] != '_';

    private sealed record ConditionClause(string Owner, string? Expression);

    private sealed record ConditionFailure(string Owner, string Message);

    private sealed record ParsedCondition(ConditionNode? Node, string? Reason)
    {
        public bool IsKnown => Reason is null;

        public static ParsedCondition Known(ConditionNode node) => new(node, null);

        public static ParsedCondition Unknown(string reason) => new(null, reason);
    }

    private abstract record ConditionNode
    {
        public abstract bool Evaluate(SurfaceContextKind context, string targetFramework);
    }

    private sealed record ConstantCondition(bool Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) => Value;
    }

    private sealed record PropertyComparisonCondition(string Property, string Operator, string Value) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework)
        {
            var actual = context == SurfaceContextKind.Project ? string.Empty : targetFramework;
            var equal = string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
            return Operator == "==" ? equal : !equal;
        }
    }

    private sealed record AndCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) && Right.Evaluate(context, targetFramework);
    }

    private sealed record OrCondition(ConditionNode Left, ConditionNode Right) : ConditionNode
    {
        public override bool Evaluate(SurfaceContextKind context, string targetFramework) =>
            Left.Evaluate(context, targetFramework) || Right.Evaluate(context, targetFramework);
    }

    private sealed record ImportApplicability(ConditionNode? Condition, string? Failure)
    {
        public bool IsKnown => Failure is null;

        public static ImportApplicability Known(ConditionNode? condition) => new(condition, null);

        public bool AppliesTo(SurfaceContextKind context, string targetFramework) =>
            IsKnown && (Condition?.Evaluate(context, targetFramework) ?? true);
    }

    private static string[] InspectMsBuildXml(string path)
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
        while (reader.Read())
        {
            if (reader.Depth > MaxXmlDepth) throw new InvalidOperationException("XML depth exceeds the supported limit.");
            if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName.Equals("UsingTask", StringComparison.Ordinal))
            {
                if (inlineTasks.Count > 0)
                {
                    var task = inlineTasks.Pop();
                    if (task.IsInlineFactory && task.HasCodeElement) observations.Add("InlineTaskFactory");
                }

                continue;
            }

            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.LocalName)
            {
                case "UsingTask":
                    observations.Add("UsingTask");
                    var factory = reader.GetAttribute("TaskFactory");
                    if (!reader.IsEmptyElement)
                    {
                        inlineTasks.Push(new InlineTaskState(reader.Depth,
                            factory is not null && (factory.Equals("CodeTaskFactory", StringComparison.OrdinalIgnoreCase) || factory.Equals("RoslynCodeTaskFactory", StringComparison.OrdinalIgnoreCase)), false));
                    }
                    break;
                case "Code" when inlineTasks.Count > 0 && reader.Depth > inlineTasks.Peek().Depth:
                    var task = inlineTasks.Pop();
                    inlineTasks.Push(task with { HasCodeElement = true });
                    break;
                case "Exec":
                    observations.Add("Exec");
                    break;
                case "Import":
                    observations.Add("Import");
                    break;
            }
        }

        return observations.Order(StringComparer.Ordinal).ToArray();
    }

    private sealed record InlineTaskState(int Depth, bool IsInlineFactory, bool HasCodeElement);

    private static string ComputeSha256(string path, WorkBudget budget)
    {
        EnsureFileWithinLimit(path, MaxMetadataFileBytes, "asset");
        budget.AddHash(FileLength(path));
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void EnsureFileWithinLimit(string path, long limit, string description)
    {
        if (!File.Exists(path))
        {
            throw new InvalidDataException($"The required {description} is missing.");
        }

        if (new FileInfo(path).Length > limit)
        {
            throw new InvalidDataException($"The {description} exceeds the supported size limit.");
        }
    }

    private static long FileLength(string path) => new FileInfo(path).Length;

    private static bool IsSafeResolvedFile(string packageRoot, string file)
    {
        var fullRoot = Path.GetFullPath(packageRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullFile = Path.GetFullPath(file);
        return fullFile.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase) &&
            !HasReparsePoint(packageRoot, fullFile, includeLeaf: true);
    }

    private static bool IsSafeResolvedDirectory(string packageFolder, string candidate)
    {
        var fullFolder = Path.GetFullPath(packageFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullCandidate = Path.GetFullPath(candidate);
        return fullCandidate.StartsWith(fullFolder, StringComparison.OrdinalIgnoreCase) &&
            !HasReparsePoint(packageFolder, fullCandidate, includeLeaf: false);
    }

    private static bool HasReparsePoint(string root, string path, bool includeLeaf)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var current = new DirectoryInfo(includeLeaf ? Path.GetDirectoryName(path)! : path);
        var reachedRoot = false;
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                return true;
            }

            if (string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
            {
                reachedRoot = true;
                break;
            }

            current = current.Parent;
        }

        if (includeLeaf && File.Exists(path))
        {
            var file = new FileInfo(path);
            return (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null;
        }

        return !reachedRoot;
    }

    private static bool ValidateCompilerMetadata(string path, out string reason)
    {
        reason = string.Empty;
        try
        {
            EnsureFileWithinLimit(path, MaxMetadataFileBytes, "compiler extension");
            using var stream = File.OpenRead(path);
            using var peReader = new PEReader(stream, PEStreamOptions.LeaveOpen);
            if (!peReader.HasMetadata)
            {
                reason = "the file does not contain managed metadata";
                return false;
            }

            MetadataReader metadata = peReader.GetMetadataReader();
            _ = metadata.GetString(metadata.GetAssemblyDefinition().Name);
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            reason = "the compiler-extension metadata could not be read";
            return false;
        }
    }

    private sealed class WorkBudget
    {
        private long _totalBytes;
        private long _totalHashBytes;

        public void Add(long bytes, string description)
        {
            if (bytes < 0 || Interlocked.Add(ref _totalBytes, bytes) > MaxTotalBytes)
            {
                throw new InvalidDataException($"The analysis exceeded the total {description} byte budget.");
            }
        }

        public void AddHash(long bytes)
        {
            if (bytes < 0 || Interlocked.Add(ref _totalHashBytes, bytes) > MaxTotalHashBytes)
            {
                throw new InvalidDataException("The analysis exceeded the total hashing byte budget.");
            }
        }
    }

    private static string? GetString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record PackageAssetRule(bool AnalyzersIncluded);

    private enum ProjectLanguage
    {
        Unknown,
        CSharp,
        VisualBasic,
        FSharp
    }

    private sealed record GeneratedImport(
        string SourceFile,
        string Project,
        ImportApplicability Applicability)
    {
        public bool AppliesTo(SurfaceContextKind context, string targetFramework, bool projectLevelCapability) =>
            context == (projectLevelCapability ? SurfaceContextKind.Project : SurfaceContextKind.Target) &&
            Applicability.AppliesTo(context, targetFramework);
    }
}
