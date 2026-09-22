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

    public static ProbeResult Analyze(string assetsFile, string projectRoot, bool strictContent = true, string? projectContext = null)
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
            var generatedImportSources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var generatedImports = ReadGeneratedImports(projectRoot, incomplete, budget, generatedImportSources);
            var projectLanguage = ReadProjectLanguage(root, projectRoot, incomplete);
            var entries = new List<SurfaceEntry>();
            var projectEntries = new Dictionary<string, SurfaceEntry>(StringComparer.OrdinalIgnoreCase);
            var resolvedPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!root.TryGetProperty("targets", out var targets))
            {
                throw new InvalidDataException("project.assets.json has no targets object.");
            }

            if (targets.ValueKind != JsonValueKind.Object || targets.EnumerateObject().Take(MaxTargets + 1).Count() > MaxTargets)
            {
                throw new InvalidDataException("project.assets.json contains too many target graphs.");
            }
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
                    if (!libraries.TryGetProperty(libraryKey, out var library))
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

                    var packageRoot = ResolvePackageRoot(packageId, version, pathElement.GetString()!, packageFolders, incomplete);
                    var files = ReadLibraryFiles(library, libraryKey, incomplete);
                    var targetAssets = package.Value;
                    foreach (var relativePath in files)
                    {
                        if (!TryGetCapability(relativePath, out var capability))
                        {
                            continue;
                        }

                        var physicalPath = Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                        var present = File.Exists(physicalPath) && IsSafeResolvedFile(packageRoot, physicalPath);
                        var reason = present ? null : $"Reachable asset is missing from resolved package contents: {relativePath}.";
                        if (File.Exists(physicalPath) && !present)
                        {
                            reason = $"Reachable asset resolves through an unsafe package path: {relativePath}.";
                        }
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

                        var active = IsActive(capability, relativePath, packageId, analyzerPackages, packageRoot, targetAssets, context, targetAlias, generatedImports, generatedImportSources, isMultiTargetingProject, projectLanguage, incomplete, libraryKey);
                        var sha = present && strictContent ? ComputeSha256(physicalPath, budget) : null;
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
                            projectEntries[key] = entry;
                        }
                        else
                        {
                            entries.Add(entry);
                        }
                    }
                }
            }

            entries.AddRange(projectEntries.Values);
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

        foreach (var direct in directAssetRules)
        {
            if (!direct.Value.AnalyzersIncluded || !packageKeysById.TryGetValue(direct.Key, out var roots))
            {
                continue;
            }

            foreach (var root in roots)
            {
                Visit(root);
            }
        }

        return active;

        void Visit(string packageKey)
        {
            if (!visited.Add(packageKey))
            {
                return;
            }

            if (!targetAssets.TryGetProperty(packageKey, out var package) || package.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} is not an object.");
            }

            var packageId = SplitPackageKey(packageKey).Id;
            if (excludedAnalyzerPackages.Contains(packageId))
            {
                return;
            }

            active.Add(packageId);
            if (!package.TryGetProperty("dependencies", out var dependencies))
            {
                return;
            }

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
                    Visit(dependencyKey);
                }
            }
        }
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

    private static ProjectLanguage ReadProjectLanguage(JsonElement root, string projectRoot, List<string> incomplete)
    {
        string? projectPath = null;
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("projectPath", out var restoredPath) && restoredPath.ValueKind == JsonValueKind.String)
        {
            projectPath = restoredPath.GetString();
        }

        projectPath ??= Directory.EnumerateFiles(projectRoot, "*proj", SearchOption.TopDirectoryOnly).FirstOrDefault();
        if (projectPath is null || projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.CSharp;
        if (projectPath.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)) return ProjectLanguage.VisualBasic;
        if (projectPath.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase))
        {
            incomplete.Add("F# compiler-extension applicability is unsupported; use a C# or Visual Basic project.");
            return ProjectLanguage.Unknown;
        }

        incomplete.Add("The consuming project language could not be determined for analyzer applicability.");
        return ProjectLanguage.Unknown;
    }

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

            result.Add(normalized);
        }

        return result;
    }

    private static string ResolvePackageRoot(string packageId, string version, string libraryPath, string[] packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {packageId}/{version} has an unsafe cache-relative path.");
            return Path.GetTempPath();
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
        var firstFolder = packageFolders.Length > 0 ? packageFolders[0] : Path.GetTempPath();
        return Path.GetFullPath(Path.Combine(firstFolder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static List<GeneratedImport> ReadGeneratedImports(string projectRoot, List<string> incomplete, WorkBudget budget, HashSet<string> generatedImportSources)
    {
        var result = new List<GeneratedImport>();
        var files = new List<string>();
        foreach (var candidate in Directory.EnumerateFiles(projectRoot, "*.nuget.g.*", SearchOption.TopDirectoryOnly))
        {
            files.Add(candidate);
        }

        var objDirectory = Path.Combine(projectRoot, "obj");
        if (Directory.Exists(objDirectory))
        {
            foreach (var candidate in Directory.EnumerateFiles(objDirectory, "*.nuget.g.*", SearchOption.TopDirectoryOnly))
            {
                files.Add(candidate);
            }
        }

        if (files.Count > MaxGeneratedImportFiles)
        {
            incomplete.Add("The project contains too many generated NuGet import files.");
            files = files.Take(MaxGeneratedImportFiles).ToList();
        }

        foreach (var file in files.Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (!file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            generatedImportSources.Add(Path.GetFileName(file));

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
                                incomplete.Add($"Generated import file {Path.GetFileName(file)} has an unproven condition on {reason!.Owner}: {reason.Message}");
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

    private static bool IsActive(
        CapabilityKind capability,
        string relativePath,
        string packageId,
        HashSet<string> analyzerPackages,
        string packageRoot,
        JsonElement targetAssets,
        SurfaceContextKind context,
        string targetFramework,
        IReadOnlyList<GeneratedImport> generatedImports,
        IReadOnlySet<string> generatedImportSources,
        bool isMultiTargetingProject,
        ProjectLanguage projectLanguage,
        List<string> incomplete,
        string libraryKey)
    {
        if (capability == CapabilityKind.ToolOrScriptPresent)
        {
            return false;
        }

        if (capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting)
        {
            var reachableBuildAsset = ContainsBuildAsset(targetAssets, capability, relativePath);
            var fullAsset = NormalizeText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var imported = generatedImports.Any(import =>
            {
                var normalizedImport = NormalizeText(import.Project);
                return (normalizedImport.Contains(fullAsset, StringComparison.OrdinalIgnoreCase) ||
                        normalizedImport.EndsWith('/' + relativePath, StringComparison.OrdinalIgnoreCase) ||
                        normalizedImport.EndsWith('\\' + relativePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase)) &&
                         import.AppliesTo(context, targetFramework, capability == CapabilityKind.BuildMultiTargeting);
            });
            var importSource = capability == CapabilityKind.BuildProps ? ".props" : ".targets";
            var hasGeneratedImportSource = generatedImportSources.Any(source => source.EndsWith(importSource, StringComparison.OrdinalIgnoreCase));
            if (!imported && reachableBuildAsset && RequiresGeneratedImportEvidence(capability, relativePath, isMultiTargetingProject) && !hasGeneratedImportSource)
            {
                incomplete.Add($"{libraryKey}: required generated NuGet import evidence is missing for {relativePath}.");
            }

            return imported;
        }

        var assetName = relativePath.Replace('\\', '/');
        return capability switch
        {
            CapabilityKind.CompilerExtension => IsConventionalAnalyzerPath(assetName, projectLanguage) && analyzerPackages.Contains(packageId),
            CapabilityKind.CompileSourceInjection => IsCompileContentFile(targetAssets, assetName, incomplete, libraryKey),
            CapabilityKind.NativeRuntime => ContainsAsset(targetAssets, "native", assetName) || ContainsAsset(targetAssets, "runtime", assetName),
            _ => false
        };
    }

    private static bool IsBuildCapability(CapabilityKind capability) =>
        capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting;

    private static bool ContainsAsset(JsonElement targetAssets, string propertyName, string assetName)
    {
        if (!targetAssets.TryGetProperty(propertyName, out var value))
        {
            return false;
        }

        return EnumerateStrings(value).Any(path =>
            string.Equals(path.Replace('\\', '/'), assetName, StringComparison.OrdinalIgnoreCase) ||
            path.Replace('\\', '/').EndsWith('/' + assetName, StringComparison.OrdinalIgnoreCase));
    }

    private static bool ContainsBuildAsset(JsonElement targetAssets, CapabilityKind capability, string assetName)
    {
        var propertyName = capability switch
        {
            CapabilityKind.BuildProps => "build",
            CapabilityKind.BuildTargets => "build",
            CapabilityKind.BuildTransitive => "buildTransitive",
            CapabilityKind.BuildMultiTargeting => "buildMultiTargeting",
            _ => string.Empty
        };
        return propertyName.Length > 0 && ContainsAsset(targetAssets, propertyName, assetName);
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

    private static bool IsConventionalAnalyzerPath(string path, ProjectLanguage language)
    {
        if (path.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase)) return language == ProjectLanguage.CSharp;
        if (path.StartsWith("analyzers/dotnet/vb/", StringComparison.OrdinalIgnoreCase)) return language == ProjectLanguage.VisualBasic;
        return path.StartsWith("analyzers/dotnet/", StringComparison.OrdinalIgnoreCase) && path.Count(character => character == '/') == 2;
    }

    private static bool IsCompileContentFile(JsonElement targetAssets, string assetName, List<string> incomplete, string libraryKey)
    {
        if (!targetAssets.TryGetProperty("contentFiles", out var contentFiles) || contentFiles.ValueKind != JsonValueKind.Object)
        {
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

            return buildAction.ValueKind == JsonValueKind.String &&
                buildAction.GetString()!.Equals("Compile", StringComparison.OrdinalIgnoreCase) &&
                (!contentFile.Value.TryGetProperty("codeLanguage", out var language) ||
                 language.ValueKind != JsonValueKind.String || language.GetString()!.Equals("cs", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static IEnumerable<string> EnumerateStrings(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.String)
        {
            yield return element.GetString()!;
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                foreach (var value in EnumerateStrings(item))
                {
                    yield return value;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                yield return property.Name;
                foreach (var value in EnumerateStrings(property.Value))
                {
                    yield return value;
                }
            }
        }
    }

    private static bool TryGetCapability(string path, out CapabilityKind capability)
    {
        capability = default;
        var normalized = path.Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        if (normalized.StartsWith("buildTransitive/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildTransitive;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("buildMultiTargeting/", StringComparison.OrdinalIgnoreCase))
        {
            capability = CapabilityKind.BuildMultiTargeting;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("build/", StringComparison.OrdinalIgnoreCase))
        {
            capability = extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ? CapabilityKind.BuildProps : CapabilityKind.BuildTargets;
            return extension is ".props" or ".targets";
        }

        if (normalized.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) && extension is ".dll" or ".exe")
        {
            capability = CapabilityKind.CompilerExtension;
            return true;
        }

        if (normalized.StartsWith("contentFiles/", StringComparison.OrdinalIgnoreCase) && extension.Equals(".cs", StringComparison.OrdinalIgnoreCase))
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

            return ParsedCondition.Unknown($"property '{property}' is outside the supported condition grammar");
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
                return ParsedCondition.Unknown($"subexpression '{expression}' is unproven: {parsed.Reason}");
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
                reason = "Exists(...) is not the standard resolved-package-file guard";
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

        reason = "Exists(...) is supported only for the standard resolved-package-file guard";
        return false;
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
            if (reader.NodeType != XmlNodeType.Element) continue;
            switch (reader.LocalName)
            {
                case "UsingTask":
                    observations.Add("UsingTask");
                    var factory = reader.GetAttribute("TaskFactory");
                    if (!string.IsNullOrWhiteSpace(factory)) observations.Add("InlineTaskFactory");
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
        while (current is not null && !string.Equals(current.FullName.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                return true;
            }

            current = current.Parent;
        }

        if (includeLeaf && File.Exists(path))
        {
            var file = new FileInfo(path);
            return (file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null;
        }

        return false;
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
        VisualBasic
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
