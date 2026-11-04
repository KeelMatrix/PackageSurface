using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;


namespace KeelMatrix.PackageSurface.Probe;

internal static class RestoreJsonSchema
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
}
