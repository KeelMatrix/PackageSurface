using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using NuGet.Versioning;

namespace KeelMatrix.PackageSurface.Probe;

public static class ResolvedGraphClassifier
{
    private const int MaxJsonDepth = 64;
    private const int MaxTargets = AnalysisBudget.MaxTargetGraphs;
    private const int MaxLibraries = AnalysisBudget.MaxLibraries;
    private const int MaxFilesPerLibrary = AnalysisBudget.MaxLibraryFiles;
    private const int MaxGeneratedImportFiles = 256;
    private const int MaxEntries = AnalysisBudget.MaxEntries;
    private const int MaxDependencyNodes = AnalysisBudget.MaxDependencyNodes;
    private const int MaxDependencyDepth = 256;
    private const int MaxConditionLength = 64 * 1024;
    private const int MaxConditionTokens = 2_048;
    private const int MaxConditionDepth = 64;
    private const int MaxNestedImports = 512;
    private const long MaxMetadataFileBytes = 16 * 1024 * 1024;
    private const long MaxXmlCharacters = 8 * 1024 * 1024;
    private const int MaxXmlDepth = 64;
    private const string JsonExtensionPrefix = "x-";
    private const string XmlExtensionNamespace = "urn:keelmatrix:packagesurface:extension";

    private static readonly Regex PropertyComparison = new(
        "^\\s*['\\\"]?\\$\\(\\s*(?<property>[A-Za-z_][A-Za-z0-9_.-]*)\\s*\\)['\\\"]?\\s*(?<operator>==|!=)\\s*['\\\"](?<value>[^'\\\"]*)['\\\"]\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex ExistsCondition = new(
        "^\\s*Exists\\s*\\(\\s*['\\\"](?<path>[^'\\\"]*)['\\\"]\\s*\\)\\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> TargetPackagePropertyNames = new(StringComparer.Ordinal)
    {
        "type",
        "framework",
        "dependencies",
        "frameworkAssemblies",
        "frameworkReferences",
        "compile",
        "runtime",
        "embed",
        "resource",
        "analyzers",
        "native",
        "build",
        "buildTransitive",
        "buildMultiTargeting",
        "contentFiles",
        "runtimeTargets",
        "tools"
    };
    private static readonly string[] RestoreRootPropertyNames =
    {
        "version",
        "targets",
        "libraries",
        "packageFolders",
        "project",
        "projectFileDependencyGroups",
        "runtimes"
    };
    private static readonly string[] ProjectPropertyNames = { "version", "restore", "frameworks", "runtimes" };
    private static readonly string[] RestorePropertyNames =
    {
        "centralPackageVersionsManagementEnabled",
        "configFilePaths",
        "crossTargeting",
        "frameworks",
        "originalTargetFrameworks",
        "outputPath",
        "packagesPath",
        "projectName",
        "projectPath",
        "projectStyle",
        "projectUniqueName",
        "restoreAuditProperties",
        "sources",
        "warningProperties",
        "compilerApiVersion"
    };
    private static readonly string[] ProjectFrameworkPropertyNames =
    {
        "framework",
        "targetAlias",
        "dependencies",
        "assetTargetFallback",
        "centralPackageVersions",
        "frameworkReferences",
        "imports",
        "runtimeIdentifierGraphPath",
        "downloadDependencies",
        "warn"
    };
    private static readonly string[] RestoreFrameworkPropertyNames =
    {
        "framework",
        "targetAlias",
        "projectReferences"
    };
    private static readonly string[] DirectDependencyPropertyNames =
    {
        "version",
        "target",
        "include",
        "exclude",
        "includeAssets",
        "excludeAssets",
        "privateAssets",
        "generatePathProperty",
        "versionCentrallyManaged",
        "autoReferenced",
        "suppressParent",
        "project",
        "library"
    };
    private static readonly string[] LibraryPropertyNames = { "sha512", "type", "path", "msbuildProject", "files", "hasTools" };
    private static readonly string[] ContentFilePropertyNames = { "related", "buildAction", "copyToOutput", "codeLanguage" };
    private static readonly string[] RuntimeTargetPropertyNames = { "rid", "assetType" };
    private static readonly string[] GeneratedImportElementNames = { "Import" };
    private static readonly string[] PackageMsBuildElementNames = { "UsingTask", "Exec", "Import", "Code" };
    private static readonly string[] GeneratedImportAttributeNames = { "Condition", "Include", "Project", "ToolsVersion" };
    private static readonly string[] PackageMsBuildAttributeNames =
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

    // Containment compares canonical identities. Production uses Path.GetFullPath;
    // the test harness injects a deterministic canonicalization function through this
    // private seam to exercise an ancestor link without weakening root reparse checks.
    private static readonly AsyncLocal<Func<string, string>?> PathCanonicalizer = new();

    private sealed record RestoreFrameworkIdentity(
        string SourceKey,
        string CanonicalKey,
        string EffectiveFramework,
        string TargetAlias);

    private sealed record RestoreTargetIdentity(
        string SourceKey,
        string CanonicalKey,
        string FrameworkKey,
        string TargetFramework,
        string TargetAlias,
        string? RuntimeIdentifier);

    private sealed record RestorePackageIdentity(string SourceKey, PackageIdentity Identity)
    {
        public string CanonicalKey => Identity.CanonicalKey;
        public string Id => Identity.Id;
        public string Version => Identity.Version;
    }

    private sealed record ReachablePackageClosure(
        HashSet<string> PackageKeys,
        HashSet<string> DirectPackageKeys,
        HashSet<string> AnalyzerPackageKeys);

    private sealed class RestoreIdentityIndex
    {
        public Dictionary<string, RestoreFrameworkIdentity> Frameworks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RestoreTargetIdentity> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RestorePackageIdentity> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PackageRoots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> FrameworkContexts => Frameworks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        public bool TryGetFramework(string sourceKey, out RestoreFrameworkIdentity identity)
        {
            identity = null!;
            return TryGetFrameworkIdentity(sourceKey, out var canonicalKey) &&
                Frameworks.TryGetValue(canonicalKey, out identity!);
        }

        public bool TryGetTarget(string sourceKey, out RestoreTargetIdentity identity)
        {
            var (tfm, rid) = SplitTarget(sourceKey);
            if (!TryGetFrameworkIdentity(tfm, out var frameworkKey))
            {
                identity = null!;
                return false;
            }

            var canonicalKey = frameworkKey + (rid is null ? string.Empty : "/" + rid.Trim().ToLowerInvariant());
            return Targets.TryGetValue(canonicalKey, out identity!);
        }

        public bool TryGetLibrary(JsonElement libraries, string sourceKey, out RestorePackageIdentity identity, out JsonElement library)
        {
            if (!TryGetPackageKey(sourceKey, out var canonicalKey) ||
                !Libraries.TryGetValue(canonicalKey, out identity!) ||
                !TryGetPropertyIgnoreCase(libraries, identity.SourceKey, out library))
            {
                identity = null!;
                library = default;
                return false;
            }

            return true;
        }

        public bool TryGetLibraryByCanonicalKey(JsonElement libraries, string canonicalKey, out RestorePackageIdentity identity, out JsonElement library)
        {
            if (!TryCreatePackageIdentity(canonicalKey, out var packageIdentity) ||
                !TryGetLibraryByCanonicalKey(libraries, packageIdentity, out identity, out library))
            {
                identity = null!;
                library = default;
                return false;
            }

            return true;
        }

        public bool TryGetLibraryByCanonicalKey(JsonElement libraries, PackageIdentity packageIdentity, out RestorePackageIdentity identity, out JsonElement library)
        {
            if (!Libraries.TryGetValue(packageIdentity.CanonicalKey, out identity!) ||
                !TryGetPropertyIgnoreCase(libraries, identity.SourceKey, out library))
            {
                identity = null!;
                library = default;
                return false;
            }

            return true;
        }

        public bool TryGetPackage(string sourceKey, out RestorePackageIdentity identity)
        {
            if (TryGetPackageKey(sourceKey, out var canonicalKey) && Libraries.TryGetValue(canonicalKey, out identity!))
            {
                return true;
            }

            identity = null!;
            return false;
        }

        public bool TryGetPackageByImportSuffix(string suffix, out RestorePackageIdentity identity, out string relativePath)
        {
            var firstSlash = suffix.IndexOf('/');
            var secondSlash = firstSlash < 0 ? -1 : suffix.IndexOf('/', firstSlash + 1);
            if (firstSlash <= 0 || secondSlash <= firstSlash + 1 || secondSlash == suffix.Length - 1 ||
                !PackageIdentity.TryCreate(suffix[..firstSlash], suffix[(firstSlash + 1)..secondSlash], out var requested))
            {
                identity = null!;
                relativePath = string.Empty;
                return false;
            }

            if (!Libraries.TryGetValue(requested.CanonicalKey, out identity!))
            {
                relativePath = string.Empty;
                return false;
            }

            relativePath = suffix[(secondSlash + 1)..];
            return relativePath.Length > 0;
        }

        public bool TryGetPackageByRoot(string packageRoot, out RestorePackageIdentity identity)
        {
            foreach (var pair in PackageRoots)
            {
                if (FileSystemPathsEqual(pair.Value, packageRoot) && Libraries.TryGetValue(pair.Key, out identity!))
                {
                    return true;
                }
            }

            identity = null!;
            return false;
        }

        public bool TryGetPackageKey(string sourceKey, out string canonicalKey)
        {
            if (TryCreatePackageIdentity(sourceKey, out var identity))
            {
                canonicalKey = identity.CanonicalKey;
                return Libraries.ContainsKey(canonicalKey);
            }

            canonicalKey = string.Empty;
            return false;
        }
    }

    public static ProbeResult Analyze(
        string assetsFile,
        string projectRoot,
        bool strictContent = true,
        string? projectContext = null,
        string? selectedProjectPath = null,
        AnalysisBudget? budget = null,
        string? compilerApiVersion = null) =>
        AnalyzeCore(assetsFile, projectRoot, strictContent, projectContext, selectedProjectPath, budget, compilerApiVersion);

    public static string? ReadRestoreProjectPath(string assetsFile)
    {
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var incomplete = new List<string>();
            ValidateRestoreJsonStructure(document.RootElement, incomplete);
            if (incomplete.Count > 0) return null;

            if (document.RootElement.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
                project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
                restore.TryGetProperty("projectPath", out var projectPath) && projectPath.ValueKind == JsonValueKind.String)
            {
                return projectPath.GetString();
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or InvalidOperationException)
        {
        }

        return null;
    }

    public static IReadOnlyList<string> GetReachablePackageInputPaths(string assetsFile)
    {
        try
        {
            EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
            var root = document.RootElement;
            var incomplete = new List<string>();
            ValidateRestoreJsonStructure(root, incomplete);
            if (incomplete.Count > 0) return Array.Empty<string>();
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            var targets = root.TryGetProperty("targets", out var targetsElement)
                ? targetsElement
                : throw new InvalidDataException("project.assets.json has no targets object.");
            if (libraries.ValueKind != JsonValueKind.Object || targets.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException("project.assets.json has invalid package input maps.");
            }

            var restoreIdentities = BuildRestoreIdentityIndex(root, libraries, targets, incomplete);
            if (incomplete.Count > 0) return Array.Empty<string>();

            var budget = new AnalysisBudget();
            var directAssetRules = ReadDirectPackageAssetRules(root, restoreIdentities, incomplete);
            var reachablePackagesByTarget = BuildReachablePackageClosures(targets, libraries, restoreIdentities, directAssetRules, incomplete, budget);
            if (incomplete.Count > 0) return Array.Empty<string>();
            var reachablePackageKeys = reachablePackagesByTarget.Values
                .SelectMany(value => value.PackageKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageRoots = ResolvePackageRoots(restoreIdentities, libraries, packageFolders, incomplete, reachablePackageKeys);
            var packageInventories = BuildPackageInventories(packageRoots, restoreIdentities, libraries, incomplete, budget);
            if (incomplete.Count > 0) return Array.Empty<string>();

            var result = new HashSet<string>(FileSystemPathComparer);
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object) return Array.Empty<string>();
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var identity, out var library) ||
                        !reachablePackagesByTarget.TryGetValue(restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ? targetIdentity.CanonicalKey : string.Empty, out var closure) ||
                        !closure.PackageKeys.Contains(identity.CanonicalKey) ||
                        !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                        !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                        !packageRoots.TryGetValue(identity.CanonicalKey, out var packageRoot) ||
                        !packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory))
                    {
                        continue;
                    }

                    foreach (var relativePath in inventory)
                    {
                        result.Add(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
                    }
                }
            }

            return result.Order(StringComparer.Ordinal).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException)
        {
            return Array.Empty<string>();
        }
    }

    private static ProbeResult AnalyzeCore(
        string assetsFile,
        string projectRoot,
        bool strictContent,
        string? projectContext,
        string? selectedProjectPath,
        AnalysisBudget? budget,
        string? compilerApiVersion)
    {
        var incomplete = new List<string>();
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
                return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
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
                return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }

            var directAssetRules = ReadDirectPackageAssetRules(root, restoreIdentities, incomplete);
            var reachablePackagesByTarget = BuildReachablePackageClosures(targets, libraries, restoreIdentities, directAssetRules, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }
            compilerApiVersion ??= ReadCompilerApiVersion(root);
            var entries = new List<SurfaceEntry>();
            var projectEntries = new Dictionary<SurfaceIdentity, List<SurfaceEntry>>(SurfaceIdentityComparer.Instance);
            var resolvedPackages = new HashSet<PackageIdentity>(PackageIdentityComparer.Instance);
            var accumulatedEntries = 0;
            var reachablePackageKeys = reachablePackagesByTarget.Values
                .SelectMany(value => value.PackageKeys)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var packageRoots = ResolvePackageRoots(restoreIdentities, libraries, packageFolders, incomplete, reachablePackageKeys);
            var packageInventories = BuildPackageInventories(packageRoots, restoreIdentities, libraries, incomplete, budget);
            ValidateRestoreEvidence(root, libraries, targets, restoreIdentities, packageFolders, packageRoots, packageInventories, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
            }

            var targetFrameworkContexts = restoreIdentities.FrameworkContexts;
            var expectedGeneratedImportSources = new HashSet<string>(FileSystemPathComparer);
            var generatedImports = ReadGeneratedImports(root, assetsFile, projectRoot, selectedProjectPath, restoreIdentities, packageRoots, packageInventories, libraries, targetFrameworkContexts, incomplete, budget, expectedGeneratedImportSources);
            ValidateGeneratedImportEvidence(generatedImports, targets, restoreIdentities, reachablePackagesByTarget, packageRoots, packageInventories, libraries, incomplete, budget);
            if (incomplete.Count > 0)
            {
                return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
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
                var reachablePackages = closure.PackageKeys;
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
            return new ProbeResult(Deduplicate(entries), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), resolvedPackages.Count);
        }
        catch (InvalidDataException ex)
        {
            incomplete.Add(IsSafeFailureText(ex.Message) ? ex.Message : "The resolved restore graph could not be analyzed completely.");
            return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException or InvalidOperationException or ArgumentException or FormatException or OverflowException or KeyNotFoundException)
        {
            incomplete.Add("The resolved restore graph could not be analyzed completely.");
            return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static bool IsSafeFailureText(string message) =>
        !message.Contains(Path.DirectorySeparatorChar) &&
        !message.Contains(Path.AltDirectorySeparatorChar) &&
        !message.Contains(":\\", StringComparison.Ordinal);

    private static void ReadProjectDependencies(
        JsonElement projectNode,
        JsonElement targetAssets,
        string projectKey,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (!projectNode.TryGetProperty("dependencies", out var dependencies)) return;
        if (dependencies.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Project node {projectKey} has invalid dependency metadata.");
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            budget.AddDependencyNode();
            if (dependency.Value.ValueKind != JsonValueKind.String ||
                !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
            {
                incomplete.Add($"Project node {projectKey} has malformed dependency value {dependency.Name}.");
                continue;
            }

            if (!TryFindMatchingTargetPackage(targetAssets, restoreIdentities, dependency.Name, requirement, out _))
            {
                incomplete.Add($"Project node {projectKey} refers to an unresolved package requirement {dependency.Name}.");
            }
        }
    }

    private static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(SurfaceIdentity.From, SurfaceIdentityComparer.Instance)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.Context)
        .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackageId, StringComparer.Ordinal)
            .ThenBy(entry => entry.Capability)
            .ThenBy(entry => entry.PackageRelativePath, StringComparer.Ordinal)
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

    private static Dictionary<string, Dictionary<string, PackageAssetRule>> ReadDirectPackageAssetRules(
        JsonElement root,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete)
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

            if (!restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity))
            {
                throw new InvalidDataException($"Framework {framework.Name} has no canonical restore identity.");
            }

            var frameworkRules = new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.Object)
                {
                    throw new InvalidDataException($"Dependency {dependency.Name} has an invalid metadata object.");
                }

                if (!dependency.Value.TryGetProperty("version", out var version) ||
                    version.ValueKind != JsonValueKind.String ||
                    !TryParsePackageVersionRange(version.GetString(), out var requirement))
                {
                    incomplete.Add($"Dependency {dependency.Name} has an invalid version range.");
                    continue;
                }

                frameworkRules[dependency.Name] = new PackageAssetRule(requirement, ReadAnalyzersIncluded(dependency.Value, dependency.Name));
            }
            result[frameworkIdentity.CanonicalKey] = frameworkRules;
        }

        return result;
    }

    private static IEnumerable<SurfaceEntry> CreateNestedImportEntries(
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
            if (!IsDeclaredPackageFile(import.PackageRoot!, import.PackageRelativePath, packageInventories) ||
                !IsSafeResolvedFile(import.PackageRoot!, physicalPath) || !File.Exists(physicalPath))
            {
                incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
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

    private static bool IsDeclaredPackageFile(
        string packageRoot,
        string relativePath,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories)
    {
        if (!TryNormalizeRelativePath(relativePath, out var normalized)) return false;
        var inventory = packageInventories.FirstOrDefault(pair => FileSystemPathsEqual(pair.Key, packageRoot));
        return inventory.Value is not null && inventory.Value.Contains(normalized);
    }

    private static bool FrameworksMatch(string left, string right) =>
        NormalizeFrameworkMoniker(left).Equals(NormalizeFrameworkMoniker(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFrameworkMoniker(string value)
    {
        var normalized = value.Trim();
        if (normalized.Length == 0)
        {
            return string.Empty;
        }

        var comma = normalized.IndexOf(',');
        if (comma >= 0)
        {
            var family = normalized[..comma].Trim().TrimStart('.');
            var versionMarker = normalized.IndexOf("Version", comma + 1, StringComparison.OrdinalIgnoreCase);
            if (versionMarker >= 0)
            {
                var version = normalized[(versionMarker + "Version".Length)..].Trim().TrimStart('=').Trim().TrimStart('v', 'V');
                if (version.Length > 0)
                {
                    return CanonicalFrameworkMoniker(family, version);
                }
            }
        }

        var lower = normalized.ToLowerInvariant();
        if (lower.StartsWith("netframework", StringComparison.Ordinal))
        {
            return CanonicalFrameworkMoniker("netframework", lower["netframework".Length..]);
        }

        if (lower.StartsWith("netcoreapp", StringComparison.Ordinal) || lower.StartsWith("netstandard", StringComparison.Ordinal))
        {
            return lower;
        }

        if (lower.StartsWith("net", StringComparison.Ordinal) && lower.Length > 3 && char.IsDigit(lower[3]))
        {
            var version = lower[3..];
            var suffixStart = version.IndexOf('-');
            var versionPart = suffixStart >= 0 ? version[..suffixStart] : version;
            if (versionPart.Contains('.', StringComparison.Ordinal) && versionPart[0] == '4')
            {
                lower = "net" + versionPart.Replace(".", string.Empty, StringComparison.Ordinal) +
                    (suffixStart >= 0 ? version[suffixStart..] : string.Empty);
            }
        }

        var platformSeparator = lower.IndexOf('-');
        if (platformSeparator >= 0)
        {
            var platform = lower[(platformSeparator + 1)..];
            foreach (var platformName in new[] { "android", "ios", "maccatalyst", "macos", "tvos", "windows" })
            {
                if (!platform.StartsWith(platformName, StringComparison.Ordinal) ||
                    !IsVersionSuffix(platform[platformName.Length..]))
                {
                    continue;
                }

                lower = lower[..(platformSeparator + 1)] + platformName;
                break;
            }
        }

        return lower;
    }

    private static bool IsVersionSuffix(string value)
    {
        if (value.Length == 0) return false;
        foreach (var part in value.Split('.', StringSplitOptions.None))
        {
            if (part.Length == 0 || part.Any(character => !char.IsDigit(character))) return false;
        }

        return true;
    }

    private static string CanonicalFrameworkMoniker(string family, string version)
    {
        var normalizedFamily = family.Trim().TrimStart('.').ToLowerInvariant();
        var normalizedVersion = version.Trim().TrimStart('v', 'V');
        return normalizedFamily switch
        {
            "netframework" => "net" + normalizedVersion.Replace(".", string.Empty, StringComparison.Ordinal),
            "netcoreapp" when int.TryParse(normalizedVersion.Split('.')[0], out var major) && major >= 5 => "net" + normalizedVersion,
            "netcoreapp" => "netcoreapp" + normalizedVersion,
            "netstandard" => "netstandard" + normalizedVersion,
            "net" or "dotnet" => "net" + normalizedVersion,
            _ => normalizedFamily + normalizedVersion
        };
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

    private static Dictionary<string, ReachablePackageClosure> BuildReachablePackageClosures(
        JsonElement targets,
        JsonElement libraries,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, Dictionary<string, PackageAssetRule>> directAssetRules,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var result = new Dictionary<string, ReachablePackageClosure>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object || !restoreIdentities.TryGetTarget(target.Name, out var targetIdentity))
            {
                continue;
            }

            var packagesById = target.Value.EnumerateObject()
                .Where(package => restoreIdentities.TryGetPackage(package.Name, out _))
                .Select(package => (Property: package, Identity: GetPackageIdentity(restoreIdentities, package.Name)))
                .GroupBy(value => value.Identity.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);
            foreach (var packageGroup in packagesById)
            {
                var versions = packageGroup.Value
                    .Select(value => value.Identity.CanonicalKey)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                if (versions.Length > 1)
                {
                    incomplete.Add($"Target {target.Name} contains multiple resolved versions for package {packageGroup.Key}.");
                }
            }

            if (incomplete.Count > 0)
            {
                continue;
            }

            var work = new Stack<(string SourceKey, int Depth)>();
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var reachable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var directRules = directAssetRules.TryGetValue(targetIdentity.FrameworkKey, out var rules)
                ? rules
                : new Dictionary<string, PackageAssetRule>(StringComparer.OrdinalIgnoreCase);

            foreach (var direct in directRules)
            {
                if (TryFindMatchingPackage(packagesById, direct.Key, direct.Value.Requirement, out var root, incomplete,
                        $"direct dependency {direct.Key}"))
                {
                    work.Push((root.Property.Name, 0));
                    directPackages.Add(root.Identity.CanonicalKey);
                }
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (restoreIdentities.TryGetLibrary(libraries, package.Name, out _, out var library) &&
                    library.TryGetProperty("type", out var type) && type.ValueKind == JsonValueKind.String &&
                    type.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase))
                {
                    work.Push((package.Name, 0));
                }
            }

            while (work.Count > 0)
            {
                var (sourceKey, depth) = work.Pop();
                if (!visited.Add(sourceKey)) continue;
                budget.AddDependencyNode();
                if (depth > MaxDependencyDepth || visited.Count > MaxDependencyNodes)
                {
                    throw new InvalidDataException("The resolved dependency graph exceeds the supported traversal limits.");
                }

                if (!restoreIdentities.TryGetPackage(sourceKey, out var packageIdentity) ||
                    !TryGetPropertyIgnoreCase(target.Value, sourceKey, out var packageNode) || packageNode.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target {target.Name} contains an unresolved package root.");
                    continue;
                }

                if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, packageIdentity.Identity, out _, out var library) ||
                    !library.TryGetProperty("type", out var libraryType) || libraryType.ValueKind != JsonValueKind.String)
                {
                    incomplete.Add($"Target {target.Name} contains a package without valid library metadata.");
                    continue;
                }

                var libraryTypeName = libraryType.GetString()!;
                var isProject = libraryTypeName.Equals("project", StringComparison.OrdinalIgnoreCase);
                var isPackage = libraryTypeName.Equals("package", StringComparison.OrdinalIgnoreCase);
                if (!isProject && !isPackage)
                {
                    incomplete.Add($"Target {target.Name} contains an unsupported library type.");
                    continue;
                }

                if (isPackage) reachable.Add(packageIdentity.CanonicalKey);
                if (!packageNode.TryGetProperty("dependencies", out var dependencies)) continue;
                if (dependencies.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageIdentity.CanonicalKey} has malformed dependency metadata.");
                    continue;
                }

                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
                    {
                        incomplete.Add($"Target package {packageIdentity.CanonicalKey} has an invalid dependency range for {dependency.Name}.");
                        continue;
                    }

                    if (TryFindMatchingPackage(packagesById, dependency.Name, requirement, out var dependencyPackage, incomplete,
                            $"dependency {dependency.Name} of {packageIdentity.CanonicalKey}"))
                    {
                        work.Push((dependencyPackage.Property.Name, depth + 1));
                    }
                }
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var identity, out var library) ||
                    !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                    !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase)) continue;
                if (!reachable.Contains(identity.CanonicalKey))
                {
                    incomplete.Add($"Target {target.Name} contains a package that is not reachable from a project or direct root.");
                }
            }

            var analyzerPackages = DetermineAnalyzerPackages(target.Value, directRules, restoreIdentities, reachable, packagesById, budget, incomplete);
            result[targetIdentity.CanonicalKey] = new ReachablePackageClosure(reachable, directPackages, analyzerPackages);
        }

        return result;
    }

    private static bool TryFindMatchingPackage(
        IReadOnlyDictionary<string, (JsonProperty Property, RestorePackageIdentity Identity)[]> packagesById,
        string packageId,
        PackageVersionRange requirement,
        out (JsonProperty Property, RestorePackageIdentity Identity) match,
        List<string> incomplete,
        string context)
    {
        match = default;
        if (!packagesById.TryGetValue(packageId, out var candidates))
        {
            incomplete.Add($"The resolved graph has no package matching {context}.");
            return false;
        }

        var matching = candidates.Where(candidate => requirement.Matches(candidate.Identity.Version)).ToArray();
        if (matching.Length != 1)
        {
            incomplete.Add(matching.Length == 0
                ? $"The resolved graph has no package version matching {context}."
                : $"The resolved graph has multiple package versions matching {context}.");
            return false;
        }

        match = matching[0];
        return true;
    }

    private static RestorePackageIdentity GetPackageIdentity(RestoreIdentityIndex restoreIdentities, string sourceKey)
    {
        if (!restoreIdentities.TryGetPackage(sourceKey, out var identity))
        {
            throw new InvalidDataException("A target package has an invalid identity.");
        }

        return identity;
    }

    private static void ValidateRestoreJsonStructure(JsonElement root, List<string> incomplete)
    {
        var work = new Stack<JsonElement>();
        work.Push(root);
        while (work.Count > 0)
        {
            var value = work.Pop();
            if (value.ValueKind == JsonValueKind.Object)
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var property in value.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        incomplete.Add($"Restore evidence contains a duplicate or case-variant property: {property.Name}.");
                    }

                    work.Push(property.Value);
                }
            }
            else if (value.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in value.EnumerateArray())
                {
                    work.Push(item);
                }
            }
        }

        ValidateRestoreJsonSchema(root, incomplete);
    }

    private static void ValidateRestoreJsonSchema(JsonElement root, List<string> incomplete)
    {
        ValidateConsumedPropertySpellings(root, "restore root", RestoreRootPropertyNames, incomplete);

        if (TryGetExactObject(root, "project", out var project))
        {
            ValidateConsumedPropertySpellings(project, "project metadata", ProjectPropertyNames, incomplete);
            if (TryGetExactObject(project, "frameworks", out var projectFrameworks))
            {
                ValidateFrameworkMap(projectFrameworks, "project framework", ProjectFrameworkPropertyNames, incomplete);
            }

            if (TryGetExactObject(project, "restore", out var restore))
            {
                ValidateConsumedPropertySpellings(restore, "restore metadata", RestorePropertyNames, incomplete);
                if (TryGetExactObject(restore, "frameworks", out var restoreFrameworks))
                {
                    ValidateFrameworkMap(restoreFrameworks, "restore framework", RestoreFrameworkPropertyNames, incomplete);
                }
            }
        }

        if (TryGetExactObject(root, "libraries", out var libraries))
        {
            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.ValueKind == JsonValueKind.Object)
                {
                    ValidateConsumedPropertySpellings(library.Value, "library metadata", LibraryPropertyNames, incomplete);
                }
            }
        }

        if (TryGetExactObject(root, "targets", out var targets))
        {
            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object) continue;
                foreach (var package in target.Value.EnumerateObject())
                {
                    if (package.Value.ValueKind != JsonValueKind.Object) continue;
                    ValidateTargetPackageSchema(package.Value, package.Name, incomplete);
                }
            }
        }

        if (TryGetExactObject(root, "packageFolders", out var packageFolders))
        {
            foreach (var folder in packageFolders.EnumerateObject())
            {
                if (folder.Value.ValueKind == JsonValueKind.Object)
                {
                    ValidateConsumedPropertySpellings(folder.Value, "package-folder metadata", Array.Empty<string>(), incomplete);
                }
            }
        }
    }

    private static void ValidateFrameworkMap(
        JsonElement frameworks,
        string description,
        IReadOnlyCollection<string> propertyNames,
        List<string> incomplete)
    {
        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object) continue;
            ValidateConsumedPropertySpellings(framework.Value, description + " metadata", propertyNames, incomplete);
            if (!TryGetExactObject(framework.Value, "dependencies", out var dependencies)) continue;
            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind == JsonValueKind.Object)
                {
                    ValidateConsumedPropertySpellings(dependency.Value, "direct dependency metadata", DirectDependencyPropertyNames, incomplete);
                }
            }
        }
    }

    private static void ValidateTargetPackageSchema(JsonElement package, string packageKey, List<string> incomplete)
    {
        ValidateConsumedPropertySpellings(package, $"target package metadata for {packageKey}", TargetPackagePropertyNames, incomplete);
        if (TryGetExactObject(package, "contentFiles", out var contentFiles))
        {
            foreach (var contentFile in contentFiles.EnumerateObject())
            {
                if (contentFile.Value.ValueKind == JsonValueKind.Object)
                {
                    ValidateConsumedPropertySpellings(contentFile.Value, "content-file metadata", ContentFilePropertyNames, incomplete);
                }
            }
        }

        if (TryGetExactObject(package, "runtimeTargets", out var runtimeTargets))
        {
            foreach (var runtimeTarget in runtimeTargets.EnumerateObject())
            {
                if (runtimeTarget.Value.ValueKind == JsonValueKind.Object)
                {
                    ValidateConsumedPropertySpellings(runtimeTarget.Value, "runtime-target metadata", RuntimeTargetPropertyNames, incomplete);
                }
            }
        }
    }

    private static void ValidateConsumedPropertySpellings(
        JsonElement value,
        string description,
        IReadOnlyCollection<string> consumedNames,
        List<string> incomplete)
    {
        if (value.ValueKind != JsonValueKind.Object) return;
        foreach (var property in value.EnumerateObject())
        {
            var canonical = consumedNames.FirstOrDefault(name => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (canonical is not null && !property.Name.Equals(canonical, StringComparison.Ordinal))
            {
                incomplete.Add($"Restore evidence contains a non-canonical spelling of consumed {description} field '{property.Name}'; expected '{canonical}'.");
            }
            else if (canonical is null && !IsJsonExtensionProperty(property.Name))
            {
                incomplete.Add($"Restore evidence contains an unknown {description} field '{property.Name}'.");
            }
        }
    }

    private static bool IsJsonExtensionProperty(string name) => name.StartsWith(JsonExtensionPrefix, StringComparison.Ordinal);

    private static bool TryGetExactObject(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static HashSet<string> DetermineAnalyzerPackages(
        JsonElement targetAssets,
        IReadOnlyDictionary<string, PackageAssetRule> directAssetRules,
        RestoreIdentityIndex restoreIdentities,
        HashSet<string> reachablePackages,
        IReadOnlyDictionary<string, (JsonProperty Property, RestorePackageIdentity Identity)[]> packagesById,
        AnalysisBudget budget,
        List<string> incomplete)
    {
        var excludedAnalyzerPackages = directAssetRules
            .Where(rule => !rule.Value.AnalyzersIncluded)
            .Select(rule => rule.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var work = new Stack<(string Key, int Depth)>();

        foreach (var direct in directAssetRules)
        {
            if (!direct.Value.AnalyzersIncluded || !packagesById.TryGetValue(direct.Key, out var roots))
            {
                continue;
            }

            foreach (var root in roots.Where(root =>
                         reachablePackages.Contains(root.Identity.CanonicalKey) &&
                         direct.Value.Requirement.Matches(root.Identity.Version)))
            {
                work.Push((root.Property.Name, 0));
            }
        }

        foreach (var project in targetAssets.EnumerateObject().Where(package =>
                     package.Value.ValueKind == JsonValueKind.Object &&
                     package.Value.TryGetProperty("type", out var type) &&
                     type.ValueKind == JsonValueKind.String &&
                     type.GetString()!.Equals("project", StringComparison.OrdinalIgnoreCase) &&
                     restoreIdentities.TryGetPackage(package.Name, out var projectIdentity) &&
                     reachablePackages.Contains(projectIdentity.CanonicalKey)))
        {
            work.Push((project.Name, 0));
        }

        while (work.Count > 0)
        {
            var (packageKey, depth) = work.Pop();
            if (!visited.Add(packageKey)) continue;
            budget.AddDependencyNode();
            if (depth > MaxDependencyDepth || visited.Count > MaxDependencyNodes)
            {
                throw new InvalidDataException("The resolved dependency graph exceeds the supported traversal limits.");
            }

            if (!TryGetPropertyIgnoreCase(targetAssets, packageKey, out var package) || package.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} is not an object.");
            }

            if (!restoreIdentities.TryGetPackage(packageKey, out var packageIdentity))
            {
                throw new InvalidDataException($"Target package {packageKey} has no canonical restore identity.");
            }

            var packageId = packageIdentity.Id;
            if (excludedAnalyzerPackages.Contains(packageId)) continue;
            active.Add(packageIdentity.CanonicalKey);
            if (!package.TryGetProperty("dependencies", out var dependencies)) continue;
            if (dependencies.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Target package {packageKey} has an invalid dependencies object.");
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                if (dependency.Value.ValueKind != JsonValueKind.String ||
                    !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement))
                {
                    incomplete.Add($"Target package {packageKey} has an invalid dependency range for {dependency.Name}.");
                    continue;
                }

                if (!packagesById.TryGetValue(dependency.Name, out var dependencyCandidates))
                {
                    incomplete.Add($"Target package {packageKey} refers to missing dependency {dependency.Name}.");
                    continue;
                }

                var dependencyMatches = dependencyCandidates
                    .Where(candidate => reachablePackages.Contains(candidate.Identity.CanonicalKey) &&
                        requirement.Matches(candidate.Identity.Version))
                    .ToArray();
                if (dependencyMatches.Length != 1)
                {
                    incomplete.Add(dependencyMatches.Length == 0
                        ? $"Target package {packageKey} has no reachable dependency version matching {dependency.Name}."
                        : $"Target package {packageKey} has multiple reachable dependency versions matching {dependency.Name}.");
                    continue;
                }

                work.Push((dependencyMatches[0].Property.Name, depth + 1));
            }
        }

        return active;
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

    private static string? ReadCompilerApiVersion(JsonElement root)
    {
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("compilerApiVersion", out var compiler) && compiler.ValueKind == JsonValueKind.String)
        {
            return compiler.GetString();
        }

        return null;
    }

    private static bool PathsEqual(string left, string right) =>
        FileSystemPathsEqual(left, right);

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

        AddDuplicatePropertyReasons(folders, "package folder", incomplete);
        var result = new List<string>();
        foreach (var folder in folders.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(folder.Name))
            {
                incomplete.Add("Restore evidence contains an empty package folder path.");
                continue;
            }

            if (folder.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Package folder '{folder.Name}' has malformed metadata.");
                continue;
            }

            if (!Path.IsPathRooted(folder.Name))
            {
                incomplete.Add($"Package folder '{folder.Name}' is not an absolute path.");
                continue;
            }

            try
            {
                result.Add(Path.GetFullPath(folder.Name));
            }
            catch (ArgumentException)
            {
                incomplete.Add($"Package folder '{folder.Name}' is not a valid path.");
            }
            catch (NotSupportedException)
            {
                incomplete.Add($"Package folder '{folder.Name}' is not a supported path.");
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("project.assets.json has no resolved package folder.");
        }

        return result.Distinct(FileSystemPathComparer).ToArray();
    }

    private static int? ValidateAssetsFormat(JsonElement root, List<string> incomplete)
    {
        if (!root.TryGetProperty("version", out var version) || version.ValueKind != JsonValueKind.Number ||
            !version.TryGetInt32(out var value) || value is < 1 or > 4)
        {
            incomplete.Add("The restore assets format version is missing or unsupported.");
            return null;
        }

        if (value == 4)
        {
            ValidateAssetsFormatV4(root, incomplete);
        }

        return value;
    }

    private static void ValidateAssetsFormatV4(JsonElement root, List<string> incomplete)
    {
        var hasDependencyGroups = root.TryGetProperty("projectFileDependencyGroups", out var dependencyGroups) && dependencyGroups.ValueKind == JsonValueKind.Object;
        if (!hasDependencyGroups)
        {
            incomplete.Add("Assets format 4 has no projectFileDependencyGroups object.");
        }
        else
        {
            AddDuplicatePropertyReasons(dependencyGroups, "format 4 dependency group", incomplete);
            foreach (var dependencyGroup in dependencyGroups.EnumerateObject())
            {
                if (dependencyGroup.Value.ValueKind != JsonValueKind.Array)
                {
                    incomplete.Add($"Assets format 4 dependency group {dependencyGroup.Name} is not an array.");
                    continue;
                }

                var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var dependency in dependencyGroup.Value.EnumerateArray())
                {
                    if (dependency.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.GetString()))
                    {
                        incomplete.Add($"Assets format 4 dependency group {dependencyGroup.Name} contains a non-string dependency.");
                    }
                    else if (!values.Add(dependency.GetString()!))
                    {
                        incomplete.Add($"Assets format 4 dependency group {dependencyGroup.Name} contains a duplicate dependency.");
                    }
                }
            }
        }

        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Assets format 4 has no project object.");
            return;
        }

        _ = ValidateAssetsFormatV4Frameworks(project, "frameworks", incomplete, out _);
        if (!project.TryGetProperty("restore", out var restore) || restore.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Assets format 4 has no project restore object.");
        }
        else
        {
            _ = ValidateAssetsFormatV4Frameworks(restore, "frameworks", incomplete, out _);
        }
    }

    private static bool ValidateAssetsFormatV4Frameworks(JsonElement parent, string propertyName, List<string> incomplete, out JsonElement frameworks)
    {
        if (!parent.TryGetProperty(propertyName, out frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Assets format 4 has no {propertyName} object.");
            return false;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Assets format 4 framework {framework.Name} is not an object.");
                continue;
            }

            if (!framework.Value.TryGetProperty("framework", out var effective) || effective.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(effective.GetString()))
            {
                incomplete.Add($"Assets format 4 framework {framework.Name} has no effective framework name.");
            }

            if (!framework.Value.TryGetProperty("targetAlias", out var alias) || alias.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(alias.GetString()))
            {
                incomplete.Add($"Assets format 4 framework {framework.Name} has no target alias.");
            }
        }

        return true;
    }

    private static RestoreIdentityIndex BuildRestoreIdentityIndex(
        JsonElement root,
        JsonElement libraries,
        JsonElement targets,
        List<string> incomplete)
    {
        var identities = new RestoreIdentityIndex();
        foreach (var pair in ReadPackageIdentityMap(libraries, "library identity", incomplete))
        {
            if (TryCreatePackageIdentity(pair.Value, out var identity))
            {
                identities.Libraries[pair.Key] = new RestorePackageIdentity(pair.Value, identity);
            }
        }

        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore evidence has no project object.");
            return identities;
        }

        if (!project.TryGetProperty("frameworks", out var projectFrameworks))
        {
            incomplete.Add("Restore evidence has no declared framework graph.");
        }
        else
        {
            foreach (var framework in ReadFrameworkIdentityMap(projectFrameworks, "project framework identity", incomplete))
            {
                identities.Frameworks[framework.Key] = framework.Value;
            }
        }

        var restoreFrameworksFound = false;
        if (project.TryGetProperty("restore", out var restore))
        {
            if (restore.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Assets restore metadata is not an object.");
            }
            else if (restore.TryGetProperty("frameworks", out var restoreFrameworks))
            {
                restoreFrameworksFound = true;
                var restoreIdentities = ReadFrameworkIdentityMap(restoreFrameworks, "restore framework identity", incomplete);
                foreach (var framework in identities.Frameworks)
                {
                    if (!restoreIdentities.TryGetValue(framework.Key, out var restoreFramework))
                    {
                        incomplete.Add($"Restore evidence framework {framework.Value.SourceKey} is missing from restore metadata.");
                    }
                    else if (!FrameworksMatch(framework.Value.EffectiveFramework, restoreFramework.EffectiveFramework) ||
                             !FrameworksMatch(framework.Value.TargetAlias, restoreFramework.TargetAlias))
                    {
                        incomplete.Add($"Restore evidence framework {framework.Value.SourceKey} disagrees between project and restore metadata.");
                    }
                }

                foreach (var framework in restoreIdentities)
                {
                    if (!identities.Frameworks.ContainsKey(framework.Key))
                    {
                        incomplete.Add($"Restore evidence framework {framework.Value.SourceKey} has no declared project framework.");
                    }
                }
            }
        }

        if (!restoreFrameworksFound && root.TryGetProperty("version", out var versionElement) &&
            versionElement.ValueKind == JsonValueKind.Number && versionElement.TryGetInt32(out var version) && version == 4)
        {
            incomplete.Add("Assets format 4 has no restore framework identity map.");
        }

        if (root.TryGetProperty("projectFileDependencyGroups", out var dependencyGroups))
        {
            var dependencyGroupIdentities = ReadFrameworkKeyMap(dependencyGroups, "format 4 dependency group", incomplete);
            foreach (var dependencyGroup in dependencyGroupIdentities)
            {
                if (!identities.Frameworks.ContainsKey(dependencyGroup.Key))
                {
                    incomplete.Add($"Assets format 4 dependency group {dependencyGroup.Value} has no declared framework.");
                }
            }

            foreach (var framework in identities.Frameworks)
            {
                if (!dependencyGroupIdentities.ContainsKey(framework.Key))
                {
                    incomplete.Add($"Assets format 4 dependency groups are missing declared framework {framework.Value.SourceKey}.");
                }
            }
        }

        if (targets.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore evidence contains an invalid target graph map.");
            return identities;
        }

        AddDuplicatePropertyReasons(targets, "target identity", incomplete);
        foreach (var target in targets.EnumerateObject())
        {
            if (!TryReadTargetIdentity(target.Name, identities.Frameworks, out var identity, incomplete))
            {
                continue;
            }

            if (identities.Targets.ContainsKey(identity.CanonicalKey))
            {
                incomplete.Add("Restore evidence contains ambiguous target identity aliases.");
                continue;
            }

            identities.Targets[identity.CanonicalKey] = identity;
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target {target.Name} is not an object.");
                continue;
            }

            foreach (var package in ReadPackageIdentityMap(target.Value, "target package identity", incomplete))
            {
                if (!identities.Libraries.ContainsKey(package.Key))
                {
                    incomplete.Add($"Target {target.Name} refers to missing library {package.Value}.");
                }
            }
        }

        return identities;
    }

    private static Dictionary<string, string> ReadPackageIdentityMap(JsonElement value, string description, List<string> incomplete)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Restore evidence contains an invalid {description} map.");
            return result;
        }

        AddDuplicatePropertyReasons(value, description, incomplete);
        foreach (var property in value.EnumerateObject())
        {
            if (!TrySplitPackageIdentity(property.Name, out var packageId, out var version))
            {
                incomplete.Add($"Restore evidence contains an invalid {description}.");
                continue;
            }

            if (PackageIdentity.TryCreate(packageId, version, out var identity))
            {
                AddCanonicalIdentity(result, identity.CanonicalKey, property.Name, description, incomplete);
            }
        }

        return result;
    }

    private static Dictionary<string, RestoreFrameworkIdentity> ReadFrameworkIdentityMap(JsonElement value, string description, List<string> incomplete)
    {
        var result = new Dictionary<string, RestoreFrameworkIdentity>(StringComparer.OrdinalIgnoreCase);
        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Restore evidence contains an invalid {description} map.");
            return result;
        }

        AddDuplicatePropertyReasons(value, description, incomplete);
        var identities = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!TryGetFrameworkIdentity(property.Name, out var keyIdentity))
            {
                incomplete.Add($"Restore evidence contains an invalid {description}.");
                continue;
            }

            var effective = property.Name;
            var alias = property.Name;
            if (property.Value.ValueKind == JsonValueKind.Object)
            {
                if (property.Value.EnumerateObject()
                    .Where(metadata => metadata.Name.Equals("framework", StringComparison.OrdinalIgnoreCase) ||
                                       metadata.Name.Equals("targetAlias", StringComparison.OrdinalIgnoreCase))
                    .GroupBy(metadata => metadata.Name, StringComparer.OrdinalIgnoreCase)
                    .Any(group => group.Count() > 1))
                {
                    incomplete.Add($"Restore evidence contains conflicting duplicate metadata for {description} {property.Name}.");
                }

                foreach (var metadataName in new[] { "framework", "targetAlias" })
                {
                    if (!property.Value.TryGetProperty(metadataName, out var metadata))
                    {
                        continue;
                    }

                    if (metadata.ValueKind != JsonValueKind.String || !TryGetFrameworkIdentity(metadata.GetString() ?? string.Empty, out var metadataIdentity))
                    {
                        incomplete.Add($"Restore evidence contains an invalid {metadataName} for {description} {property.Name}.");
                        continue;
                    }

                    if (!FrameworksMatch(keyIdentity, metadataIdentity))
                    {
                        incomplete.Add($"Restore evidence contains incoherent {description} metadata for {property.Name}: {metadataName} disagrees with the framework key.");
                    }

                    if (metadataName.Equals("framework", StringComparison.Ordinal))
                    {
                        effective = metadata.GetString()!;
                    }
                    else
                    {
                        alias = metadata.GetString()!;
                    }
                }
            }
            else
            {
                incomplete.Add($"Restore evidence contains an invalid {description} entry.");
                continue;
            }

            AddCanonicalIdentity(identities, keyIdentity, property.Name, description, incomplete);
            if (!result.ContainsKey(keyIdentity))
            {
                result[keyIdentity] = new RestoreFrameworkIdentity(property.Name, keyIdentity, effective, alias);
            }
        }

        return result;
    }

    private static Dictionary<string, string> ReadFrameworkKeyMap(JsonElement value, string description, List<string> incomplete)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"Restore evidence contains an invalid {description} map.");
            return result;
        }

        AddDuplicatePropertyReasons(value, description, incomplete);
        foreach (var property in value.EnumerateObject())
        {
            if (!TryGetFrameworkIdentity(property.Name, out var identity))
            {
                incomplete.Add($"Restore evidence contains an invalid {description}.");
                continue;
            }

            AddCanonicalIdentity(result, identity, property.Name, description, incomplete);
        }

        return result;
    }

    private static bool TryReadTargetIdentity(
        string sourceKey,
        IReadOnlyDictionary<string, RestoreFrameworkIdentity> frameworks,
        out RestoreTargetIdentity identity,
        List<string> incomplete)
    {
        var (tfm, rid) = SplitTarget(sourceKey);
        if (!TryGetFrameworkIdentity(tfm, out var frameworkKey) ||
            rid is not null && (string.IsNullOrWhiteSpace(rid) || rid.Contains('/', StringComparison.Ordinal)))
        {
            incomplete.Add("Restore evidence contains an invalid target identity.");
            identity = null!;
            return false;
        }

        if (!frameworks.TryGetValue(frameworkKey, out var framework))
        {
            incomplete.Add($"Resolved target {sourceKey} has no declared framework.");
            identity = null!;
            return false;
        }

        var canonicalKey = frameworkKey + (rid is null ? string.Empty : "/" + rid.Trim().ToLowerInvariant());
        identity = new RestoreTargetIdentity(
            sourceKey,
            canonicalKey,
            frameworkKey,
            framework.EffectiveFramework,
            framework.TargetAlias,
            rid?.Trim().ToLowerInvariant());
        return true;
    }

    private static void AddCanonicalIdentity(
        Dictionary<string, string> identities,
        string identity,
        string propertyName,
        string description,
        List<string> incomplete)
    {
        if (identities.TryGetValue(identity, out var prior) && !prior.Equals(propertyName, StringComparison.Ordinal))
        {
            incomplete.Add($"Restore evidence contains ambiguous {description} aliases.");
            return;
        }

        identities.TryAdd(identity, propertyName);
    }

    private static bool TryGetFrameworkIdentity(string value, out string identity)
    {
        identity = NormalizeFrameworkMoniker(value);
        return !string.IsNullOrEmpty(identity) && !identity.Contains('/', StringComparison.Ordinal);
    }

    private static void ValidateRestoreEvidence(
        JsonElement root,
        JsonElement libraries,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyList<string> packageFolders,
        IReadOnlyDictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (libraries.ValueKind != JsonValueKind.Object || targets.ValueKind != JsonValueKind.Object) return;
        if (!root.TryGetProperty("project", out var project) || project.ValueKind != JsonValueKind.Object ||
            !project.TryGetProperty("frameworks", out var frameworks) || frameworks.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore evidence has no declared framework graph.");
            return;
        }

        var declaredFrameworks = restoreIdentities.Frameworks.Keys.ToArray();
        var targetNames = restoreIdentities.Targets.Keys.ToArray();
        if (declaredFrameworks.Length == 0)
        {
            incomplete.Add("Restore evidence declares no frameworks.");
        }

        if (targetNames.Length == 0)
        {
            incomplete.Add("Restore evidence contains no resolved target graphs.");
        }

        foreach (var framework in declaredFrameworks)
        {
            if (!restoreIdentities.Targets.Values.Any(target => target.FrameworkKey.Equals(framework, StringComparison.OrdinalIgnoreCase)))
            {
                incomplete.Add($"Declared framework {framework} has no resolved target graph.");
            }
        }

        foreach (var target in restoreIdentities.Targets.Values)
        {
            if (!declaredFrameworks.Contains(target.FrameworkKey, StringComparer.OrdinalIgnoreCase))
            {
                incomplete.Add($"Resolved target {target.SourceKey} has no declared framework.");
            }
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object) continue;
            if (!restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity))
            {
                incomplete.Add($"Declared framework {framework.Name} has no canonical restore identity.");
                continue;
            }

            if (framework.Value.TryGetProperty("dependencies", out var dependencies) && dependencies.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Declared framework {framework.Name} has malformed dependency metadata.");
            }
            else if (framework.Value.TryGetProperty("dependencies", out dependencies))
            {
                AddDuplicatePropertyReasons(dependencies, $"declared dependency for {framework.Name}", incomplete);
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    if (dependency.Value.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Declared framework {framework.Name} has malformed dependency value {dependency.Name}.");
                        continue;
                    }

                    if (!dependency.Value.TryGetProperty("version", out var version) ||
                        version.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(version.GetString(), out var requirement) ||
                        !TryFindMatchingPackageInFrameworkTargets(targets, restoreIdentities, frameworkIdentity.CanonicalKey, dependency.Name, requirement))
                    {
                        incomplete.Add($"Declared framework {framework.Name} has an unresolved package requirement {dependency.Name}.");
                    }
                }
            }
        }

        ValidateFormat4DependencyGroups(root, targets, restoreIdentities, incomplete, budget);

        foreach (var target in targets.EnumerateObject())
        {
            if (target.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target {target.Name} is not an object.");
                continue;
            }

            if (!restoreIdentities.TryGetTarget(target.Name, out var targetIdentity))
            {
                continue;
            }

            if (!target.Value.EnumerateObject().Any() && frameworks.EnumerateObject()
                .Where(framework => restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity) &&
                    frameworkIdentity.CanonicalKey.Equals(targetIdentity.FrameworkKey, StringComparison.OrdinalIgnoreCase))
                .Any(framework => framework.Value.ValueKind == JsonValueKind.Object &&
                    framework.Value.TryGetProperty("dependencies", out var dependencies) &&
                    dependencies.ValueKind == JsonValueKind.Object && dependencies.EnumerateObject().Any()))
            {
                incomplete.Add($"Resolved target {target.Name} is empty despite declared framework dependencies.");
            }

            foreach (var package in target.Value.EnumerateObject())
            {
                if (!restoreIdentities.TryGetLibrary(libraries, package.Name, out var packageIdentity, out var library))
                {
                    incomplete.Add($"Target {target.Name} refers to missing library {package.Name}.");
                    continue;
                }

                if (package.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target {target.Name} package {package.Name} has malformed asset metadata.");
                    continue;
                }

                var libraryType = library.TryGetProperty("type", out var libraryTypeElement) && libraryTypeElement.ValueKind == JsonValueKind.String
                    ? libraryTypeElement.GetString()
                    : null;
                if (libraryType is not ("package" or "project"))
                {
                    incomplete.Add($"Target {target.Name} library {package.Name} has an unsupported library type.");
                    continue;
                }

                ValidateTargetPackageAssets(
                    package.Value,
                    library,
                    packageIdentity.CanonicalKey,
                    libraryType,
                    target.Value,
                    restoreIdentities,
                    packageRoots,
                    packageInventories,
                    incomplete,
                    budget);
                if (TryGetPropertyIgnoreCase(package.Value, "dependencies", out var dependencies))
                {
                    if (dependencies.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Target package {package.Name} has malformed dependency metadata.");
                    }
                    else
                    {
                        foreach (var dependency in dependencies.EnumerateObject())
                        {
                            if (dependency.Value.ValueKind != JsonValueKind.String ||
                                !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement) ||
                                !TryFindMatchingTargetPackage(target.Value, restoreIdentities, dependency.Name, requirement, out _))
                            {
                                incomplete.Add($"Target package {package.Name} has an unresolved package requirement {dependency.Name}.");
                            }
                        }
                    }
                }
            }

            foreach (var framework in frameworks.EnumerateObject().Where(framework =>
                         restoreIdentities.TryGetFramework(framework.Name, out var frameworkIdentity) &&
                         frameworkIdentity.CanonicalKey.Equals(targetIdentity.FrameworkKey, StringComparison.OrdinalIgnoreCase)))
            {
                if (!framework.Value.TryGetProperty("dependencies", out var dependencies) || dependencies.ValueKind != JsonValueKind.Object) continue;
                foreach (var dependency in dependencies.EnumerateObject())
                {
                    budget.AddDependencyNode();
                    if (dependency.Value.ValueKind != JsonValueKind.Object ||
                        !dependency.Value.TryGetProperty("version", out var version) ||
                        version.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(version.GetString(), out var requirement) ||
                        !TryFindMatchingTargetPackage(target.Value, restoreIdentities, dependency.Name, requirement, out _))
                    {
                        incomplete.Add($"Declared framework {framework.Name} refers to an unresolved package requirement {dependency.Name}.");
                    }
                }
            }
        }
    }

    private static void ValidateTargetPackageAssets(
        JsonElement package,
        JsonElement library,
        string packageKey,
        string? libraryType,
        JsonElement targetAssets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, string> packageRoots,
        Dictionary<string, HashSet<string>> packageInventories,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var isPackage = string.Equals(libraryType, "package", StringComparison.OrdinalIgnoreCase);
        var files = isPackage && packageRoots.TryGetValue(packageKey, out var packageRoot) &&
                    packageInventories.TryGetValue(Path.GetFullPath(packageRoot), out var inventory)
            ? inventory
            : isPackage
                ? ReadLibraryFiles(library, packageKey, incomplete, budget).ToHashSet(FileSystemPathComparer)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddDuplicatePropertyReasons(package, "target package metadata", incomplete);
        foreach (var group in package.EnumerateObject())
        {
            if (!TargetPackagePropertyNames.Contains(group.Name))
            {
                if (!IsJsonExtensionProperty(group.Name))
                {
                    incomplete.Add($"Target package {packageKey} has an unsupported asset group or metadata property '{group.Name}'.");
                }

                continue;
            }

            if (group.Name.Equals("dependencies", StringComparison.OrdinalIgnoreCase))
            {
                if (group.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageKey} has malformed dependency metadata.");
                    continue;
                }

                AddDuplicatePropertyReasons(group.Value, "target dependency", incomplete);
                foreach (var dependency in group.Value.EnumerateObject())
                {
                    budget.AddDependencyNode();
                    if (dependency.Value.ValueKind != JsonValueKind.String ||
                        !TryParsePackageVersionRange(dependency.Value.GetString(), out var requirement) ||
                        !TryFindMatchingTargetPackage(targetAssets, restoreIdentities, dependency.Name, requirement, out _))
                    {
                        incomplete.Add($"Target package {packageKey} has an unresolved package requirement {dependency.Name}.");
                    }
                }
                continue;
            }

            if (group.Name.Equals("type", StringComparison.OrdinalIgnoreCase) || group.Name.Equals("framework", StringComparison.OrdinalIgnoreCase))
            {
                if (group.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(group.Value.GetString()))
                {
                    incomplete.Add($"Target package {packageKey} has malformed {group.Name} metadata.");
                }
                continue;
            }

            if (group.Name.Equals("frameworkAssemblies", StringComparison.OrdinalIgnoreCase) || group.Name.Equals("frameworkReferences", StringComparison.OrdinalIgnoreCase))
            {
                ValidateStringArrayMetadata(group.Value, group.Name, packageKey, incomplete);
                continue;
            }

            if (group.Value.ValueKind == JsonValueKind.Array)
            {
                incomplete.Add($"Target package {packageKey} has malformed asset group {group.Name}.");
                continue;
            }

            if (group.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"Target package {packageKey} has malformed asset group {group.Name}.");
                continue;
            }

            if (group.Name.Equals("contentFiles", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var content in group.Value.EnumerateObject())
                {
                    if (content.Value.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add($"Target package {packageKey} has malformed content file metadata.");
                        continue;
                    }

                    if (isPackage && !files.Contains(content.Name.Replace('\\', '/')))
                    {
                        incomplete.Add($"Target package {packageKey} references a content file absent from its package inventory.");
                    }
                }
                continue;
            }

            foreach (var asset in group.Value.EnumerateObject())
            {
                if (asset.Value.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add($"Target package {packageKey} has malformed asset metadata.");
                    continue;
                }

                var assetPath = asset.Name.Replace('\\', '/');
                if (!TryNormalizeRelativePath(assetPath, out _))
                {
                    incomplete.Add($"Target package {packageKey} contains an unsafe asset reference.");
                    continue;
                }

                if (isPackage && !files.Contains(assetPath))
                {
                    incomplete.Add($"Target package {packageKey} references an asset absent from its package inventory.");
                }
            }
        }
    }

    private static void ValidateFormat4DependencyGroups(
        JsonElement root,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        if (!root.TryGetProperty("projectFileDependencyGroups", out var groups) || groups.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var group in groups.EnumerateObject())
        {
            if (!restoreIdentities.TryGetFramework(group.Name, out var frameworkIdentity) || group.Value.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var dependency in group.Value.EnumerateArray())
            {
                budget.AddDependencyNode();
                if (dependency.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(dependency.GetString()))
                {
                    continue;
                }

                var value = dependency.GetString()!;
                if (IsValidProjectDependencyPath(value))
                {
                    continue;
                }

                if (!TryParseFormat4PackageRequirement(value, out var packageId, out var requirement) ||
                    !TryFindMatchingPackageInFrameworkTargets(targets, restoreIdentities, frameworkIdentity.CanonicalKey, packageId, requirement))
                {
                    incomplete.Add($"Assets format 4 dependency group {group.Name} has an unresolved package requirement.");
                }
            }
        }
    }

    private static bool TryFindMatchingPackageInFrameworkTargets(
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        string frameworkKey,
        string packageId,
        PackageVersionRange requirement)
    {
        foreach (var target in targets.EnumerateObject())
        {
            if (!restoreIdentities.TryGetTarget(target.Name, out var targetIdentity) ||
                !targetIdentity.FrameworkKey.Equals(frameworkKey, StringComparison.OrdinalIgnoreCase) ||
                target.Value.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (TryFindMatchingTargetPackage(target.Value, restoreIdentities, packageId, requirement, out _))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryFindMatchingTargetPackage(
        JsonElement targetAssets,
        RestoreIdentityIndex restoreIdentities,
        string packageId,
        PackageVersionRange requirement,
        out RestorePackageIdentity identity)
    {
        foreach (var package in targetAssets.EnumerateObject())
        {
            if (restoreIdentities.TryGetPackage(package.Name, out var candidate) &&
                candidate.Id.Equals(packageId, StringComparison.OrdinalIgnoreCase) &&
                requirement.Matches(candidate.Version))
            {
                identity = candidate;
                return true;
            }
        }

        identity = null!;
        return false;
    }

    private static bool TryParseFormat4PackageRequirement(
        string value,
        out string packageId,
        out PackageVersionRange requirement)
    {
        var separator = value.IndexOf(">=", StringComparison.Ordinal);
        if (separator <= 0 || value.IndexOf(">=", separator + 2, StringComparison.Ordinal) >= 0)
        {
            packageId = string.Empty;
            requirement = null!;
            return false;
        }

        packageId = value[..separator].Trim();
        var version = value[(separator + 2)..].Trim();
        if (!IsValidPackageId(packageId) || !TryParsePackageVersionRange($"[{version}, )", out requirement))
        {
            packageId = string.Empty;
            requirement = null!;
            return false;
        }

        return true;
    }

    private static bool IsValidProjectDependencyPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        return normalized.Length > 0 &&
            normalized.Contains('/', StringComparison.Ordinal) &&
            !normalized.StartsWith('/') &&
            !Path.IsPathRooted(normalized) &&
            !normalized.Contains(':', StringComparison.Ordinal) &&
            !normalized.Contains('\0') &&
            normalized.Split('/').All(part => part.Length > 0 && part != ".") &&
            (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryParsePackageVersionRange(string? value, out PackageVersionRange requirement)
    {
        requirement = null!;
        if (string.IsNullOrWhiteSpace(value) || !VersionRange.TryParse(value, out var parsed) || parsed is null || parsed.IsFloating || (!parsed.HasLowerBound && !parsed.HasUpperBound))
        {
            return false;
        }

        requirement = new PackageVersionRange(parsed);
        return true;
    }

    private sealed record PackageVersionRange(VersionRange Value)
    {
        public bool Matches(string value)
        {
            return PackageIdentity.TryParseNuGetVersion(value, out var candidate) &&
                Value.Satisfies(candidate, VersionComparer.VersionRelease);
        }
    }

    private static void ValidateStringArrayMetadata(JsonElement value, string propertyName, string packageKey, List<string> incomplete)
    {
        if (value.ValueKind != JsonValueKind.Array)
        {
            incomplete.Add($"Target package {packageKey} has malformed {propertyName} metadata.");
            return;
        }

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.GetString()))
            {
                incomplete.Add($"Target package {packageKey} has malformed {propertyName} metadata.");
                continue;
            }

            if (!values.Add(item.GetString()!))
            {
                incomplete.Add($"Target package {packageKey} has duplicate {propertyName} metadata.");
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

    private static Dictionary<string, string> ResolvePackageRoots(
        RestoreIdentityIndex restoreIdentities,
        JsonElement libraries,
        IReadOnlyList<string> packageFolders,
        List<string> incomplete,
        HashSet<string>? reachablePackageKeys = null)
    {
        foreach (var package in restoreIdentities.Libraries.Values)
        {
            if (!restoreIdentities.TryGetLibraryByCanonicalKey(libraries, package.Identity, out _, out var library) ||
                !library.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
                !type.GetString()!.Equals("package", StringComparison.OrdinalIgnoreCase) ||
                reachablePackageKeys is not null && !reachablePackageKeys.Contains(package.CanonicalKey)) continue;
            if (!library.TryGetProperty("path", out var path) || path.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"Library {package.SourceKey} has no valid package path.");
                continue;
            }

            var packageRoot = ResolvePackageRoot(package.Identity, path.GetString()!, packageFolders.ToArray(), incomplete);
            if (packageRoot is not null)
            {
                restoreIdentities.PackageRoots[package.CanonicalKey] = packageRoot;
            }
        }

        return restoreIdentities.PackageRoots;
    }

    private static Dictionary<string, HashSet<string>> BuildPackageInventories(
        IReadOnlyDictionary<string, string> packageRoots,
        RestoreIdentityIndex restoreIdentities,
        JsonElement libraries,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        var result = new Dictionary<string, HashSet<string>>(FileSystemPathComparer);
        foreach (var package in packageRoots)
        {
            if (!TryCreatePackageIdentity(package.Key, out var packageIdentity) ||
                !restoreIdentities.TryGetLibraryByCanonicalKey(libraries, packageIdentity, out _, out var library))
            {
                incomplete.Add("A resolved package root has no matching library inventory.");
                continue;
            }

            result[Path.GetFullPath(package.Value)] = ReadLibraryFiles(library, package.Key, incomplete, budget)
                .ToHashSet(FileSystemPathComparer);
        }

        return result;
    }

    private static void ValidateGeneratedImportEvidence(
        IReadOnlyList<GeneratedImport> imports,
        JsonElement targets,
        RestoreIdentityIndex restoreIdentities,
        IReadOnlyDictionary<string, ReachablePackageClosure> reachablePackagesByTarget,
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
                : ReadLibraryFiles(library, match.CanonicalKey, incomplete, budget).ToHashSet(FileSystemPathComparer);
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
                    !closure.PackageKeys.Contains(match.CanonicalKey))
                {
                    incomplete.Add("Generated NuGet import evidence refers to a package that is not reachable from an applicable project/direct root.");
                    break;
                }
            }
        }
    }

    private static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete, AnalysisBudget budget)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > MaxFilesPerLibrary)
        {
            incomplete.Add($"Library {libraryKey} has no files array.");
            return Array.Empty<string>();
        }

        var result = new List<string>();
        foreach (var file in files.EnumerateArray())
        {
            budget.AddLibraryFile();
            if (file.ValueKind != JsonValueKind.String || !TryNormalizeRelativePath(file.GetString()!, out var normalized))
            {
                incomplete.Add($"Library {libraryKey} contains an invalid relative asset path.");
                continue;
            }

            if (result.Any(existing => string.Equals(existing, normalized, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)))
            {
                incomplete.Add($"{libraryKey} contains a duplicate asset path: {normalized}.");
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }

    private static string? ResolvePackageRoot(PackageIdentity identity, string libraryPath, string[] packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {identity.Id}/{identity.Version} has an unsafe cache-relative path.");
            return null;
        }

        var separator = normalizedLibraryPath.IndexOf('/');
        if (separator <= 0 || separator == normalizedLibraryPath.Length - 1 ||
            normalizedLibraryPath.IndexOf('/', separator + 1) >= 0 ||
            !PackageIdentity.TryCreate(normalizedLibraryPath[..separator], normalizedLibraryPath[(separator + 1)..], out var pathIdentity) ||
            !identity.Equals(pathIdentity))
        {
            incomplete.Add($"Package {identity.Id}/{identity.Version} has a cache-relative path that does not belong to that package identity.");
            return null;
        }

        foreach (var folder in packageFolders)
        {
            if (!Path.IsPathRooted(folder))
            {
                continue;
            }

            var candidatePaths = new[]
            {
                Path.Combine(folder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(folder, identity.Id, identity.NormalizedVersion.Replace('/', Path.DirectorySeparatorChar)),
                Path.Combine(folder, identity.Id.ToLowerInvariant(), identity.NormalizedVersion.Replace('/', Path.DirectorySeparatorChar))
            };
            foreach (var candidatePath in candidatePaths.Distinct(FileSystemPathComparer))
            {
                var candidate = Path.GetFullPath(candidatePath);
                if (Directory.Exists(candidate) && IsSafeResolvedDirectory(folder, candidate) && DirectoryInfoMatchesIdentity(candidate, identity))
                {
                    return candidate;
                }
            }
        }

        incomplete.Add($"Package {identity.Id}/{identity.Version} was not found in the resolved package folders.");
        return null;
    }

    private static bool DirectoryInfoMatchesIdentity(string path, PackageIdentity identity)
    {
        var packageDirectory = new DirectoryInfo(path);
        return packageDirectory.Parent is not null &&
            PackageIdentity.TryCreate(packageDirectory.Parent.Name, packageDirectory.Name, out var physicalIdentity) &&
            identity.Equals(physicalIdentity);
    }

    private static List<GeneratedImport> ReadGeneratedImports(
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

        foreach (var file in files.Distinct(FileSystemPathComparer).Order(StringComparer.Ordinal))
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

                        if (HasCaseInsensitiveDuplicateXmlAttributes(reader))
                        {
                            incomplete.Add($"Generated import file {Path.GetFileName(file)} contains duplicate or case-variant attributes.");
                        }

                        var elementName = reader.LocalName;
                        ValidateConsumedXmlElementSpelling(
                            reader,
                            GeneratedImportElementNames,
                            $"generated import file {Path.GetFileName(file)}",
                            incomplete);
                        ValidateConsumedXmlAttributeSpellings(
                            reader,
                            GeneratedImportAttributeNames,
                            $"generated import file {Path.GetFileName(file)}",
                            incomplete);
                        var condition = reader.GetAttribute("Condition");
                        if (elementName.Equals("Import", StringComparison.Ordinal) && reader.GetAttribute("Project") is string project)
                        {
                            budget.AddImportedEdge();
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

                foreach (var directImport in result.Where(import => import.SourceFile.Equals(Path.GetFileName(file), StringComparison.OrdinalIgnoreCase) && !import.IsNested).ToArray())
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

    private static void WalkNestedImports(
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
            if (!visited.Add(Path.GetFullPath(current.File) + "|" + current.Condition + "|" + rootCapability + "|" + rootAssetPath)) continue;
            if (!IsSafeResolvedFile(current.PackageRoot, current.File) || !File.Exists(current.File))
            {
                incomplete.Add("A statically imported package build file is missing or outside its resolved package root.");
                continue;
            }

            var currentRelativePath = Path.GetRelativePath(current.PackageRoot, current.File).Replace(Path.DirectorySeparatorChar, '/');
            if (!IsDeclaredPackageFile(current.PackageRoot, currentRelativePath, packageInventories))
            {
                incomplete.Add("A statically imported package build file is absent from the resolved package inventory.");
                continue;
            }

            try
            {
                budget.AddImportedFile();
                EnsureFileWithinLimit(current.File, MaxMetadataFileBytes, "nested package import");
                budget.Add(FileLength(current.File), "nested-import metadata");
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
                        if (HasCaseInsensitiveDuplicateXmlAttributes(reader))
                        {
                            incomplete.Add("A nested package import contains duplicate or case-variant attributes.");
                        }

                        ValidateConsumedXmlElementSpelling(
                            reader,
                            PackageMsBuildElementNames,
                            "nested package import",
                            incomplete);
                        ValidateConsumedXmlAttributeSpellings(
                            reader,
                            PackageMsBuildAttributeNames,
                            "nested package import",
                            incomplete);
                        var condition = reader.GetAttribute("Condition");
                        if (reader.LocalName.Equals("Import", StringComparison.Ordinal) && reader.GetAttribute("Project") is string project)
                        {
                            budget.AddImportedEdge();
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
                            var resolved = TryResolveStaticPackageImport(project, current.File, restoreIdentities, packageRoots, out var nestedPath, out var nestedRoot, out resolutionReason);
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
                                       !IsRelativeImportUnderAnyPackageRoot(current.File, project, packageRoots.Values))))
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
        return packageRoots.Any(root => IsWithinDirectory(root, fullFile, allowRoot: false));
    }

    private static ConditionNode? CombineConditionNodes(ConditionNode? left, ConditionNode? right) =>
        left is null ? right : right is null ? left : new AndCondition(left, right);

    private static bool TryResolveStaticPackageImport(
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
            if (!IsSafeResolvedFile(candidateRoot, candidatePath))
            {
                reason = "unsafe package import target";
                return false;
            }

            if (!File.Exists(candidatePath))
            {
                reason = "missing package import (package-root reference)";
                return false;
            }

            resolvedPath = candidatePath;
            packageRoot = candidateRoot;
            return true;
        }

        if (Path.IsPathRooted(requested))
        {
            var absolute = Path.GetFullPath(requested);
            foreach (var candidate in packageRoots.Values.Distinct(FileSystemPathComparer))
            {
                if (!IsSafeResolvedFile(candidate, absolute)) continue;
                if (!File.Exists(absolute))
                {
                    reason = "missing package import (absolute reference)";
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
        var relativePath = Path.GetFullPath(Path.Combine(currentDirectory, requested.Replace('/', Path.DirectorySeparatorChar)));
        var sawSafeRoot = false;
        foreach (var candidate in packageRoots.Values.Distinct(FileSystemPathComparer))
        {
            if (!IsSafeResolvedFile(candidate, relativePath)) continue;
            sawSafeRoot = true;
            if (!File.Exists(relativePath))
            {
                reason = "missing package import (relative reference)";
                return false;
            }
            resolvedPath = relativePath;
            packageRoot = candidate;
            return true;
        }

        reason = sawSafeRoot ? "missing package import (relative reference; safe-root)" : "not a package import";
        return false;
    }

    private static bool IsActive(
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

    private static bool IsNestedOrExpectedGeneratedImportSource(
        GeneratedImport import,
        string relativePath,
        IReadOnlySet<string> expectedGeneratedImportSources) =>
        import.IsNested || IsExpectedGeneratedImportSource(import.SourceFile, relativePath, expectedGeneratedImportSources);

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

            if (string.Equals(property.Name.Replace('\\', '/'), assetName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
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
        if (propertyName.Length == 0) return false;
        if (ContainsAsset(targetAssets, propertyName, assetName, incomplete, libraryKey)) return true;
        return capability == CapabilityKind.BuildTransitive &&
            ContainsAsset(targetAssets, "build", assetName, incomplete, libraryKey);
    }

    private static bool RequiresGeneratedImportEvidence(CapabilityKind capability, string assetName, bool isMultiTargetingProject)
    {
        if (capability == CapabilityKind.BuildMultiTargeting && !isMultiTargetingProject) return false;
        return true;
    }

    private static HashSet<string> SelectAnalyzerPaths(
        IReadOnlyList<string> files,
        ProjectLanguage projectLanguage,
        HashSet<string> analyzerPackages,
        string libraryKey,
        List<string> incomplete,
        string? compilerApiVersion)
    {
        var candidates = new List<(string Path, Version? RoslynVersion)>();
        foreach (var path in files)
        {
            if (!path.StartsWith("analyzers/", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase) || IsAnalyzerSatellite(path))
            {
                continue;
            }

            if (!TryParseAnalyzerPath(path, out _, out var roslynVersion))
            {
                incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
                continue;
            }

            if (!IsAnalyzerApplicableToProject(path, projectLanguage, incomplete, libraryKey))
            {
                continue;
            }

            if (analyzerPackages.Contains(libraryKey))
            {
                candidates.Add((path, roslynVersion));
            }
        }

        var selected = new HashSet<string>(FileSystemPathComparer);
        foreach (var candidate in candidates.Where(candidate => candidate.RoslynVersion is null))
        {
            selected.Add(candidate.Path);
        }

        var versionedCandidates = candidates.Where(candidate => candidate.RoslynVersion is not null).ToArray();
        var compilerVersion = new Version(0, 0);
        if (versionedCandidates.Length > 0 && !Version.TryParse(compilerApiVersion, out compilerVersion))
        {
            incomplete.Add($"{libraryKey}: versioned analyzer applicability requires an explicit consuming compiler API version.");
            return selected;
        }

        var highest = versionedCandidates
            .Where(candidate => candidate.RoslynVersion is not null && candidate.RoslynVersion.CompareTo(compilerVersion) <= 0)
            .Select(candidate => candidate.RoslynVersion)
            .OrderByDescending(value => value)
            .FirstOrDefault();
        if (versionedCandidates.Length > 0 && highest is null)
        {
            incomplete.Add($"{libraryKey}: no analyzer version is compatible with the consuming compiler API version.");
            return selected;
        }

        foreach (var candidate in versionedCandidates)
        {
            if (highest is not null && candidate.RoslynVersion == highest)
            {
                selected.Add(candidate.Path);
            }
        }

        return selected;
    }

    private static bool TryParseAnalyzerPath(string path, out string? language, out Version? roslynVersion)
    {
        language = null;
        roslynVersion = null;
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length < 2 || !segments[0].Equals("analyzers", StringComparison.OrdinalIgnoreCase) ||
            !Path.GetExtension(path).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        for (var index = 1; index < segments.Length - 1; index++)
        {
            if (segments[index].StartsWith("roslyn", StringComparison.OrdinalIgnoreCase))
            {
                if (roslynVersion is not null || !Version.TryParse(segments[index]["roslyn".Length..], out roslynVersion))
                {
                    return false;
                }
            }
            else if (IsAnalyzerLanguage(segments[index]))
            {
                if (language is not null)
                {
                    return false;
                }

                language = segments[index].ToLowerInvariant();
            }
        }

        return true;
    }

    private static bool IsAnalyzerLanguage(string value) =>
        value.Equals("cs", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("vb", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("fs", StringComparison.OrdinalIgnoreCase);

    private static bool IsAnalyzerApplicableToProject(string path, ProjectLanguage language, List<string> incomplete, string libraryKey)
    {
        var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var hasCSharpSegment = segments.Any(segment => segment.Equals("cs", StringComparison.OrdinalIgnoreCase));
        var hasVisualBasicSegment = segments.Any(segment => segment.Equals("vb", StringComparison.OrdinalIgnoreCase));
        return language switch
        {
            ProjectLanguage.CSharp => hasCSharpSegment || !hasVisualBasicSegment,
            ProjectLanguage.VisualBasic => hasVisualBasicSegment || !hasCSharpSegment,
            ProjectLanguage.FSharp => false,
            ProjectLanguage.Unknown when !hasCSharpSegment && !hasVisualBasicSegment => true,
            ProjectLanguage.Unknown => AddUnknownAnalyzerLanguageReason(incomplete, libraryKey),
            _ => false
        };
    }

    private static bool AddUnknownAnalyzerLanguageReason(List<string> incomplete, string libraryKey)
    {
        incomplete.Add($"{libraryKey}: consuming project language is required to determine compiler-extension applicability.");
        return false;
    }

    private static bool IsAnalyzerSatellite(string path) =>
        path.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase);

    private static bool IsCompilerExtensionActive(
        string path,
        ProjectLanguage language,
        HashSet<string> analyzerPackages,
        IReadOnlySet<string> selectedAnalyzers,
        string packageKey,
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

        if (!TryParseAnalyzerPath(path, out _, out _))
        {
            incomplete.Add($"{libraryKey}: analyzer applicability is unavailable for the resolved compiler-extension path.");
            return false;
        }

        if (!analyzerPackages.Contains(packageKey) || !IsAnalyzerApplicableToProject(path, language, incomplete, libraryKey))
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
            if (!string.Equals(contentFile.Name.Replace('\\', '/'), assetName,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) continue;
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

    private static bool TrySplitPackageIdentity(string key, out string id, out string version)
    {
        var slash = key.IndexOf('/');
        if (slash <= 0 || slash != key.LastIndexOf('/') || slash == key.Length - 1)
        {
            id = string.Empty;
            version = string.Empty;
            return false;
        }

        id = key[..slash];
        version = key[(slash + 1)..];
        if (!IsValidPackageId(id) || !IsValidPackageVersion(version))
        {
            id = string.Empty;
            version = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryCreatePackageIdentity(string key, out PackageIdentity identity)
    {
        identity = null!;
        return TrySplitPackageIdentity(key, out var id, out var version) &&
            PackageIdentity.TryCreate(id, version, out identity);
    }

    private static bool IsValidPackageId(string value)
    {
        if (value.Length == 0 || value is "." or "..") return false;
        foreach (var character in value)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('.' or '-' or '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidPackageVersion(string value)
        => PackageIdentity.TryCreate("VersionValidation", value, out _);

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

    private static bool TryGetNuGetPackageRootSuffix(string value, out string suffix)
    {
        const string property = "$(NuGetPackageRoot)";
        if (!value.StartsWith(property, StringComparison.OrdinalIgnoreCase))
        {
            suffix = string.Empty;
            return false;
        }

        suffix = value[property.Length..].TrimStart('/');
        return suffix.Length > 0;
    }

    private static bool PackageRootImportEquals(string import, PackageIdentity expectedPackage, string expectedRelativePath)
    {
        if (!TryGetNuGetPackageRootSuffix(import, out var suffix)) return false;
        var firstSlash = suffix.IndexOf('/');
        var secondSlash = firstSlash < 0 ? -1 : suffix.IndexOf('/', firstSlash + 1);
        if (firstSlash <= 0 || secondSlash <= firstSlash + 1 || secondSlash == suffix.Length - 1 ||
            !PackageIdentity.TryCreate(suffix[..firstSlash], suffix[(firstSlash + 1)..secondSlash], out var importedPackage) ||
            !expectedPackage.Equals(importedPackage))
        {
            return false;
        }

        var importedRelativePath = suffix[(secondSlash + 1)..];
        return string.Equals(importedRelativePath.Replace('\\', '/'), expectedRelativePath.Replace('\\', '/'),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }

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

        if (requested.StartsWith("$(NuGetPackageRoot)", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = requested["$(NuGetPackageRoot)".Length..].TrimStart('/');
            var importSuffix = project.StartsWith("$(NuGetPackageRoot)", StringComparison.OrdinalIgnoreCase)
                ? project["$(NuGetPackageRoot)".Length..].TrimStart('/')
                : string.Empty;
            if (suffix.Length == 0 || !importSuffix.Equals(suffix, StringComparison.OrdinalIgnoreCase))
            {
                reason = "the Exists expression is not the standard resolved-package-file guard";
                return false;
            }

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
            var equal = Property.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase)
                ? FrameworksMatch(actual, Value)
                : string.Equals(actual, Value, StringComparison.OrdinalIgnoreCase);
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

    private static bool HasCaseInsensitiveDuplicateXmlAttributes(XmlReader reader)
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

    private static void ValidateConsumedXmlElementSpelling(
        XmlReader reader,
        IReadOnlyCollection<string> consumedNames,
        string description,
        List<string>? incomplete)
    {
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

    private static bool IsNearSpelling(string candidate, string canonical)
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

    private static void ValidateConsumedXmlAttributeSpellings(
        XmlReader reader,
        IReadOnlyCollection<string> allowedNames,
        string description,
        List<string>? incomplete)
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
            var message = canonical is null
                ? $"{description} contains an unknown XML attribute '{reader.Name}'."
                : $"{description} contains a non-canonical spelling of consumed XML attribute '{reader.Name}'; expected '{canonical}'.";
            if (canonical is null || !reader.LocalName.Equals(canonical, StringComparison.Ordinal))
            {
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

    private static bool IsXmlNamespaceDeclaration(XmlReader reader) =>
        reader.Prefix.Equals("xmlns", StringComparison.Ordinal) || reader.Name.Equals("xmlns", StringComparison.Ordinal);

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
            ValidateConsumedXmlElementSpelling(reader, PackageMsBuildElementNames, "package MSBuild XML", null);
            ValidateConsumedXmlAttributeSpellings(
                reader,
                PackageMsBuildAttributeNames,
                "package MSBuild XML",
                null);
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

    private static string ComputeSha256(string path, AnalysisBudget budget)
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
        var fullFile = Path.GetFullPath(file);
        return IsWithinDirectory(packageRoot, fullFile, allowRoot: false) &&
            !HasReparsePoint(packageRoot, fullFile, includeLeaf: true);
    }

    private static bool IsSafeResolvedDirectory(string packageFolder, string candidate)
    {
        var fullCandidate = Path.GetFullPath(candidate);
        return IsWithinDirectory(packageFolder, fullCandidate, allowRoot: true) &&
            !HasReparsePoint(packageFolder, fullCandidate, includeLeaf: false);
    }

    private static bool IsWithinDirectory(string root, string candidate, bool allowRoot)
    {
        var fullRoot = CanonicalizePath(root);
        var fullCandidate = CanonicalizePath(candidate);
        var relative = Path.GetRelativePath(fullRoot, fullCandidate);
        if (allowRoot && relative == ".") return true;
        return !Path.IsPathRooted(relative) &&
            relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static bool FileSystemPathsEqual(string left, string right) =>
        string.Equals(CanonicalizePath(left), CanonicalizePath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static string CanonicalizePath(string path) =>
        PathCanonicalizer.Value?.Invoke(path) ?? Path.GetFullPath(path);

    private static StringComparer FileSystemPathComparer =>
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static bool HasReparsePoint(string root, string path, bool includeLeaf)
    {
        var fullRoot = Path.GetFullPath(root);
        var fullPath = Path.GetFullPath(path);
        if (includeLeaf && File.Exists(fullPath))
        {
            var file = new FileInfo(fullPath);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null)
            {
                return true;
            }
        }

        var current = new DirectoryInfo(includeLeaf ? Path.GetDirectoryName(fullPath)! : fullPath);
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)
            {
                return true;
            }

            if (FileSystemPathsEqual(current.FullName, fullRoot))
            {
                break;
            }

            current = current.Parent;
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

    private static string? GetString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private sealed record PackageAssetRule(PackageVersionRange Requirement, bool AnalyzersIncluded);

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
