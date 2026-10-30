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

internal static class RestoreMetadataReader
{

    internal static readonly HashSet<string> TargetPackagePropertyNames = new(StringComparer.Ordinal)
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


    internal static readonly string[] RestoreRootPropertyNames =
    {
        "version",
        "targets",
        "libraries",
        "packageFolders",
        "project",
        "projectFileDependencyGroups",
        "runtimes"
    };


    internal static readonly string[] ProjectPropertyNames = { "version", "restore", "frameworks", "runtimes" };


    internal static readonly string[] RestorePropertyNames =
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
        "fallbackFolders",
        "sources",
        "warningProperties",
        "compilerApiVersion",
        "SdkAnalysisLevel"
    };


    internal static readonly string[] ProjectFrameworkPropertyNames =
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


    internal static readonly string[] RestoreFrameworkPropertyNames =
    {
        "framework",
        "targetAlias",
        "projectReferences"
    };


    internal static readonly string[] DirectDependencyPropertyNames =
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
        "library",
        "versionOverride",
        "noWarn",
        "aliases"
    };


    internal static readonly string[] WarningPropertyNames = { "allWarningsAsErrors", "noWarn", "warnAsError", "warnNotAsError" };


    internal static readonly string[] RestoreAuditPropertyNames = { "enableAudit", "auditLevel", "auditMode", "suppressedAdvisories" };


    internal static readonly string[] ProjectReferencePropertyNames = { "projectPath", "includeAssets", "excludeAssets", "privateAssets" };


    internal static readonly string[] FrameworkReferencePropertyNames = { "privateAssets" };


    internal static readonly string[] DownloadDependencyPropertyNames = { "name", "version" };


    internal static readonly string[] LibraryPropertyNames = { "sha512", "type", "path", "msbuildProject", "files", "hasTools" };


    internal static readonly string[] ContentFilePropertyNames = { "related", "buildAction", "copyToOutput", "codeLanguage" };


    internal static readonly string[] RuntimeTargetPropertyNames = { "rid", "assetType" };



    internal sealed record RestoreFrameworkIdentity(
        string SourceKey,
        string CanonicalKey,
        string EffectiveFramework,
        string TargetAlias);



    internal sealed record RestoreTargetIdentity(
        string SourceKey,
        string CanonicalKey,
        string FrameworkKey,
        string TargetFramework,
        string TargetAlias,
        string? RuntimeIdentifier);



    internal sealed record RestorePackageIdentity(string SourceKey, PackageIdentity Identity)
    {
        public string CanonicalKey => Identity.CanonicalKey;
        public string Id => Identity.Id;
        public string Version => Identity.Version;
    }



    internal sealed class RestoreIdentityIndex
    {
        public Dictionary<string, RestoreFrameworkIdentity> Frameworks { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RestoreTargetIdentity> Targets { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, RestorePackageIdentity> Libraries { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> PackageRoots { get; } = new(StringComparer.OrdinalIgnoreCase);

        public IReadOnlySet<string> FrameworkContexts => Frameworks.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        public bool IsMultiTargeting => Targets.Values.Select(target => target.FrameworkKey)
            .Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1;

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


    internal sealed record RestoreGraphMetadata(
        JsonElement Root,
        JsonElement Libraries,
        JsonElement Targets,
        string[] PackageFolders,
        RestoreIdentityIndex Identities);


    internal static JsonDocument ReadAssetsDocument(string assetsFile, AnalysisBudget budget)
    {
        EnsureFileWithinLimit(assetsFile, MaxMetadataFileBytes, "project.assets.json");
        budget.Add(FileLength(assetsFile), "metadata");
        using var stream = File.OpenRead(assetsFile);
        return JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = MaxJsonDepth });
    }


    internal static RestoreGraphMetadata? ReadGraphMetadata(
        JsonElement root,
        List<string> incomplete,
        AnalysisBudget budget)
    {
        ValidateRestoreJsonStructure(root, incomplete);
        if (incomplete.Count > 0)
        {
            return null;
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

        var identities = BuildRestoreIdentityIndex(root, libraries, targets, incomplete);
        return new RestoreGraphMetadata(root, libraries, targets, packageFolders, identities);
    }



    internal static bool IsDeclaredPackageFile(
        string packageRoot,
        string relativePath,
        IReadOnlyDictionary<string, HashSet<string>> packageInventories)
    {
        if (!TryNormalizeRelativePath(relativePath, out var normalized)) return false;
        var inventory = packageInventories.FirstOrDefault(pair => FileSystemPathsEqual(pair.Key, packageRoot));
        return inventory.Value is not null && inventory.Value.Contains(normalized);
    }



    internal static bool FrameworksMatch(string left, string right) =>
        NormalizeFrameworkMoniker(left).Equals(NormalizeFrameworkMoniker(right), StringComparison.OrdinalIgnoreCase);



    internal static string NormalizeFrameworkMoniker(string value)
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



    internal static bool IsVersionSuffix(string value)
    {
        if (value.Length == 0) return false;
        foreach (var part in value.Split('.', StringSplitOptions.None))
        {
            if (part.Length == 0 || part.Any(character => !char.IsDigit(character))) return false;
        }

        return true;
    }



    internal static string CanonicalFrameworkMoniker(string family, string version)
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



    internal static void ValidateRestoreJsonStructure(JsonElement root, List<string> incomplete)
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



    internal static void ValidateRestoreJsonSchema(JsonElement root, List<string> incomplete)
    {
        ValidateConsumedPropertySpellings(root, "restore root", RestoreRootPropertyNames, incomplete);

        if (TryGetExactObject(root, "project", out var project))
        {
            ValidateConsumedPropertySpellings(project, "project metadata", ProjectPropertyNames, incomplete);
            if (project.TryGetProperty("frameworks", out var projectFrameworks))
            {
                ValidateFrameworkMap(projectFrameworks, "project framework", ProjectFrameworkPropertyNames, incomplete, restoreFramework: false);
            }

            if (project.TryGetProperty("restore", out var restore))
            {
                if (restore.ValueKind != JsonValueKind.Object)
                {
                    incomplete.Add("Restore metadata is not an object.");
                }
                else
                {
                    ValidateConsumedPropertySpellings(restore, "restore metadata", RestorePropertyNames, incomplete);
                    ValidateRestoreOptionalMetadata(restore, incomplete);
                    if (restore.TryGetProperty("frameworks", out var restoreFrameworks))
                    {
                        ValidateFrameworkMap(restoreFrameworks, "restore framework", RestoreFrameworkPropertyNames, incomplete, restoreFramework: true);
                    }
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



    internal static void ValidateRestoreOptionalMetadata(JsonElement restore, List<string> incomplete)
    {
        ValidateBooleanProperty(restore, "centralPackageVersionsManagementEnabled", "restore metadata", incomplete);
        ValidateStringArrayProperty(restore, "configFilePaths", "restore metadata", incomplete);
        ValidateBooleanProperty(restore, "crossTargeting", "restore metadata", incomplete);
        ValidateStringArrayProperty(restore, "originalTargetFrameworks", "restore metadata", incomplete);
        ValidateStringProperty(restore, "outputPath", "restore metadata", incomplete);
        ValidateStringProperty(restore, "packagesPath", "restore metadata", incomplete);
        ValidateStringProperty(restore, "projectName", "restore metadata", incomplete);
        ValidateStringProperty(restore, "projectPath", "restore metadata", incomplete);
        ValidateStringProperty(restore, "projectStyle", "restore metadata", incomplete);
        ValidateStringProperty(restore, "projectUniqueName", "restore metadata", incomplete);
        ValidateStringArrayProperty(restore, "fallbackFolders", "restore metadata", incomplete);
        ValidateStringProperty(restore, "compilerApiVersion", "restore metadata", incomplete);
        ValidateStringProperty(restore, "SdkAnalysisLevel", "restore metadata", incomplete);

        if (restore.TryGetProperty("sources", out var sources))
        {
            if (sources.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Restore metadata field 'sources' must be an object map.");
            }
            else
            {
                foreach (var source in sources.EnumerateObject())
                {
                    if (source.Value.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add("Restore metadata field 'sources' must map source names to objects.");
                    }
                    else
                    {
                        ValidateConsumedPropertySpellings(source.Value, "source metadata", Array.Empty<string>(), incomplete);
                    }
                }
            }
        }

        if (restore.TryGetProperty("warningProperties", out var warningProperties))
        {
            if (warningProperties.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Restore metadata field 'warningProperties' must be an object.");
            }
            else
            {
                ValidateConsumedPropertySpellings(warningProperties, "warning metadata", WarningPropertyNames, incomplete);
                ValidateBooleanProperty(warningProperties, "allWarningsAsErrors", "warning metadata", incomplete);
                ValidateStringArrayProperty(warningProperties, "noWarn", "warning metadata", incomplete);
                ValidateStringArrayProperty(warningProperties, "warnAsError", "warning metadata", incomplete);
                ValidateStringArrayProperty(warningProperties, "warnNotAsError", "warning metadata", incomplete);
            }
        }

        if (restore.TryGetProperty("restoreAuditProperties", out var auditProperties))
        {
            if (auditProperties.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Restore metadata field 'restoreAuditProperties' must be an object.");
            }
            else
            {
                ValidateConsumedPropertySpellings(auditProperties, "restore audit metadata", RestoreAuditPropertyNames, incomplete);
                ValidateStringProperty(auditProperties, "enableAudit", "restore audit metadata", incomplete);
                ValidateStringProperty(auditProperties, "auditLevel", "restore audit metadata", incomplete);
                ValidateStringProperty(auditProperties, "auditMode", "restore audit metadata", incomplete);
                if (auditProperties.TryGetProperty("suppressedAdvisories", out var suppressedAdvisories))
                {
                    if (suppressedAdvisories.ValueKind != JsonValueKind.Object)
                    {
                        incomplete.Add("Restore audit metadata field 'suppressedAdvisories' must be an object map.");
                    }
                    else
                    {
                        foreach (var advisory in suppressedAdvisories.EnumerateObject())
                        {
                            if (advisory.Value.ValueKind != JsonValueKind.Null)
                            {
                                incomplete.Add("Restore audit metadata field 'suppressedAdvisories' must map advisory names to null.");
                            }
                        }
                    }
                }
            }
        }
    }



    internal static void ValidateFrameworkMap(
        JsonElement frameworks,
        string description,
        IReadOnlyCollection<string> propertyNames,
        List<string> incomplete,
        bool restoreFramework)
    {
        if (frameworks.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{description} map must be an object.");
            return;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (framework.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add($"{description} {framework.Name} must be an object.");
                continue;
            }

            ValidateConsumedPropertySpellings(framework.Value, description + " metadata", propertyNames, incomplete);
            if (restoreFramework)
            {
                ValidateStringProperty(framework.Value, "framework", description + " metadata", incomplete);
                ValidateStringProperty(framework.Value, "targetAlias", description + " metadata", incomplete);
                ValidateProjectReferences(framework.Value, incomplete);
            }
            else
            {
                ValidateProjectFrameworkMetadata(framework.Value, incomplete);
            }
        }
    }



    internal static void ValidateProjectFrameworkMetadata(JsonElement framework, List<string> incomplete)
    {
        ValidateStringProperty(framework, "framework", "project framework metadata", incomplete);
        ValidateStringProperty(framework, "targetAlias", "project framework metadata", incomplete);
        ValidateDependencyMap(framework, incomplete);
        ValidateBooleanProperty(framework, "assetTargetFallback", "project framework metadata", incomplete);
        ValidateStringMapProperty(framework, "centralPackageVersions", "project framework metadata", incomplete);
        ValidateFrameworkReferences(framework, incomplete);
        ValidateStringArrayProperty(framework, "imports", "project framework metadata", incomplete);
        ValidateStringProperty(framework, "runtimeIdentifierGraphPath", "project framework metadata", incomplete);
        ValidateDownloadDependencies(framework, incomplete);
        ValidateBooleanProperty(framework, "warn", "project framework metadata", incomplete);
    }



    internal static void ValidateDependencyMap(JsonElement framework, List<string> incomplete)
    {
        if (!framework.TryGetProperty("dependencies", out var dependencies)) return;
        if (dependencies.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Project framework metadata field 'dependencies' must be an object map.");
            return;
        }

        foreach (var dependency in dependencies.EnumerateObject())
        {
            if (dependency.Value.ValueKind == JsonValueKind.String) continue;
            if (dependency.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Project framework metadata field 'dependencies' must map package names to strings or objects.");
                continue;
            }

            ValidateConsumedPropertySpellings(dependency.Value, "direct dependency metadata", DirectDependencyPropertyNames, incomplete);
            ValidateStringProperty(dependency.Value, "version", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "target", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "include", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "exclude", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "includeAssets", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "excludeAssets", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "privateAssets", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "suppressParent", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "project", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "library", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "versionOverride", "direct dependency metadata", incomplete);
            ValidateStringProperty(dependency.Value, "aliases", "direct dependency metadata", incomplete);
            ValidateBooleanProperty(dependency.Value, "generatePathProperty", "direct dependency metadata", incomplete);
            ValidateBooleanProperty(dependency.Value, "versionCentrallyManaged", "direct dependency metadata", incomplete);
            ValidateBooleanProperty(dependency.Value, "autoReferenced", "direct dependency metadata", incomplete);
            ValidateStringArrayProperty(dependency.Value, "noWarn", "direct dependency metadata", incomplete);
        }
    }



    internal static void ValidateProjectReferences(JsonElement framework, List<string> incomplete)
    {
        if (!framework.TryGetProperty("projectReferences", out var projectReferences)) return;
        if (projectReferences.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Restore framework metadata field 'projectReferences' must be an object map.");
            return;
        }

        foreach (var project in projectReferences.EnumerateObject())
        {
            if (project.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Restore framework metadata field 'projectReferences' must map project names to objects.");
                continue;
            }

            ValidateConsumedPropertySpellings(project.Value, "project reference metadata", ProjectReferencePropertyNames, incomplete);
            ValidateStringProperty(project.Value, "projectPath", "project reference metadata", incomplete);
            ValidateStringProperty(project.Value, "includeAssets", "project reference metadata", incomplete);
            ValidateStringProperty(project.Value, "excludeAssets", "project reference metadata", incomplete);
            ValidateStringProperty(project.Value, "privateAssets", "project reference metadata", incomplete);
        }
    }



    internal static void ValidateFrameworkReferences(JsonElement framework, List<string> incomplete)
    {
        if (!framework.TryGetProperty("frameworkReferences", out var frameworkReferences)) return;
        if (frameworkReferences.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add("Project framework metadata field 'frameworkReferences' must be an object map.");
            return;
        }

        foreach (var reference in frameworkReferences.EnumerateObject())
        {
            if (reference.Value.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Project framework metadata field 'frameworkReferences' must map framework names to objects.");
                continue;
            }

            ValidateConsumedPropertySpellings(reference.Value, "framework reference metadata", FrameworkReferencePropertyNames, incomplete);
            ValidateStringProperty(reference.Value, "privateAssets", "framework reference metadata", incomplete);
        }
    }



    internal static void ValidateDownloadDependencies(JsonElement framework, List<string> incomplete)
    {
        if (!framework.TryGetProperty("downloadDependencies", out var downloads)) return;
        if (downloads.ValueKind != JsonValueKind.Array)
        {
            incomplete.Add("Project framework metadata field 'downloadDependencies' must be an array.");
            return;
        }

        foreach (var download in downloads.EnumerateArray())
        {
            if (download.ValueKind != JsonValueKind.Object)
            {
                incomplete.Add("Project framework metadata field 'downloadDependencies' must contain objects.");
                continue;
            }

            ValidateConsumedPropertySpellings(download, "download dependency metadata", DownloadDependencyPropertyNames, incomplete);
            ValidateStringProperty(download, "name", "download dependency metadata", incomplete);
            ValidateStringProperty(download, "version", "download dependency metadata", incomplete);
        }
    }



    internal static void ValidateStringMapProperty(JsonElement parent, string propertyName, string description, List<string> incomplete)
    {
        if (!parent.TryGetProperty(propertyName, out var value)) return;
        if (value.ValueKind != JsonValueKind.Object)
        {
            incomplete.Add($"{description} field '{propertyName}' must be an object map.");
            return;
        }

        foreach (var entry in value.EnumerateObject())
        {
            if (entry.Value.ValueKind != JsonValueKind.String)
            {
                incomplete.Add($"{description} field '{propertyName}' must map names to strings.");
            }
        }
    }



    internal static void ValidateStringProperty(JsonElement parent, string propertyName, string description, List<string> incomplete)
    {
        if (parent.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.String)
        {
            incomplete.Add($"{description} field '{propertyName}' must be a string.");
        }
    }



    internal static void ValidateBooleanProperty(JsonElement parent, string propertyName, string description, List<string> incomplete)
    {
        if (parent.TryGetProperty(propertyName, out var value) && value.ValueKind != JsonValueKind.True && value.ValueKind != JsonValueKind.False)
        {
            incomplete.Add($"{description} field '{propertyName}' must be a boolean.");
        }
    }



    internal static void ValidateStringArrayProperty(JsonElement parent, string propertyName, string description, List<string> incomplete)
    {
        if (!parent.TryGetProperty(propertyName, out var value)) return;
        if (value.ValueKind != JsonValueKind.Array || value.EnumerateArray().Any(item => item.ValueKind != JsonValueKind.String))
        {
            incomplete.Add($"{description} field '{propertyName}' must be an array of strings.");
        }
    }



    internal static void ValidateTargetPackageSchema(JsonElement package, string packageKey, List<string> incomplete)
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



    internal static void ValidateConsumedPropertySpellings(
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



    internal static bool IsJsonExtensionProperty(string name) => name.StartsWith(JsonExtensionPrefix, StringComparison.Ordinal);



    internal static bool TryGetExactObject(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(propertyName, out value) && value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }



    internal static string? ReadProjectPath(JsonElement root)
    {
        if (root.TryGetProperty("project", out var project) && project.ValueKind == JsonValueKind.Object &&
            project.TryGetProperty("restore", out var restore) && restore.ValueKind == JsonValueKind.Object &&
            restore.TryGetProperty("projectPath", out var projectPath) && projectPath.ValueKind == JsonValueKind.String)
        {
            return projectPath.GetString();
        }

        return null;
    }



    internal static string[] ReadPackageFolders(JsonElement root, List<string> incomplete)
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
                incomplete.Add("Package folder metadata is malformed.");
                continue;
            }

            if (!Path.IsPathRooted(folder.Name))
            {
                incomplete.Add("A package folder is not an absolute path.");
                continue;
            }

            try
            {
                result.Add(Path.GetFullPath(folder.Name));
            }
            catch (ArgumentException)
            {
                incomplete.Add("A package folder is not a valid path.");
            }
            catch (NotSupportedException)
            {
                incomplete.Add("A package folder is not a supported path.");
            }
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("project.assets.json has no resolved package folder.");
        }

        return result.Distinct(FileSystemPathComparer).ToArray();
    }



    internal static int? ValidateAssetsFormat(JsonElement root, List<string> incomplete)
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



    internal static void ValidateAssetsFormatV4(JsonElement root, List<string> incomplete)
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



    internal static bool ValidateAssetsFormatV4Frameworks(JsonElement parent, string propertyName, List<string> incomplete, out JsonElement frameworks)
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



    internal static RestoreIdentityIndex BuildRestoreIdentityIndex(
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



    internal static Dictionary<string, string> ReadPackageIdentityMap(JsonElement value, string description, List<string> incomplete)
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



    internal static Dictionary<string, RestoreFrameworkIdentity> ReadFrameworkIdentityMap(JsonElement value, string description, List<string> incomplete)
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



    internal static Dictionary<string, string> ReadFrameworkKeyMap(JsonElement value, string description, List<string> incomplete)
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



    internal static bool TryReadTargetIdentity(
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



    internal static void AddCanonicalIdentity(
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



    internal static bool TryGetFrameworkIdentity(string value, out string identity)
    {
        identity = NormalizeFrameworkMoniker(value);
        return !string.IsNullOrEmpty(identity) && !identity.Contains('/', StringComparison.Ordinal);
    }



    internal static void ValidateRestoreEvidence(
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
                    if (!TryReadDependencyRequirement(dependency.Value, out var requirement) ||
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
                    if (!TryReadDependencyRequirement(dependency.Value, out var requirement) ||
                        !TryFindMatchingTargetPackage(target.Value, restoreIdentities, dependency.Name, requirement, out _))
                    {
                        incomplete.Add($"Declared framework {framework.Name} refers to an unresolved package requirement {dependency.Name}.");
                    }
                }
            }
        }
    }



    internal static void ValidateTargetPackageAssets(
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
                ? ReadLibraryFiles(library, packageKey, incomplete, budget).ToHashSet(GetPackageAssetStringComparer(packageKey))
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



    internal static void ValidateFormat4DependencyGroups(
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



    internal static bool TryFindMatchingPackageInFrameworkTargets(
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



    internal static bool TryFindMatchingTargetPackage(
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



    internal static bool TryParseFormat4PackageRequirement(
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



    internal static bool IsValidProjectDependencyPath(string value)
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



    internal static bool TryParsePackageVersionRange(string? value, out PackageVersionRange requirement)
    {
        requirement = null!;
        if (string.IsNullOrWhiteSpace(value) || !VersionRange.TryParse(value, out var parsed) || parsed is null || parsed.IsFloating || (!parsed.HasLowerBound && !parsed.HasUpperBound))
        {
            return false;
        }

        requirement = new PackageVersionRange(parsed);
        return true;
    }



    internal sealed record PackageVersionRange(VersionRange Value)
    {
        public bool Matches(string value)
        {
            return PackageIdentity.TryParseNuGetVersion(value, out var candidate) &&
                Value.Satisfies(candidate, VersionComparer.VersionRelease);
        }
    }



    internal static void ValidateStringArrayMetadata(JsonElement value, string propertyName, string packageKey, List<string> incomplete)
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



    internal static void AddDuplicatePropertyReasons(JsonElement value, string description, List<string> incomplete)
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



    internal static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete, AnalysisBudget budget)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array || files.GetArrayLength() > MaxFilesPerLibrary)
        {
            incomplete.Add($"Library {libraryKey} has no files array.");
            return Array.Empty<string>();
        }

        var result = new List<string>();
        var assetComparison = GetPackageAssetComparison(libraryKey);
        foreach (var file in files.EnumerateArray())
        {
            budget.AddLibraryFile();
            if (file.ValueKind != JsonValueKind.String || !TryNormalizeRelativePath(file.GetString()!, out var normalized))
            {
                incomplete.Add($"Library {libraryKey} contains an invalid relative asset path.");
                continue;
            }

            if (result.Any(existing => string.Equals(existing, normalized, assetComparison)))
            {
                incomplete.Add($"{libraryKey} contains a duplicate asset path: {normalized}.");
                continue;
            }

            result.Add(normalized);
        }

        return result;
    }



    internal static bool TrySplitPackageIdentity(string key, out string id, out string version)
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



    internal static bool TryCreatePackageIdentity(string key, out PackageIdentity identity)
    {
        identity = null!;
        return TrySplitPackageIdentity(key, out var id, out var version) &&
            PackageIdentity.TryCreate(id, version, out identity);
    }



    internal static bool IsValidPackageId(string value)
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



    internal static bool IsValidPackageVersion(string value)
        => PackageIdentity.TryCreate("VersionValidation", value, out _);



    internal static bool TryGetPropertyIgnoreCase(JsonElement objectElement, string propertyName, out JsonElement value)
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



    internal static string? GetString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;




    internal static string? ReadRestoreProjectPath(string assetsFile)
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

}
