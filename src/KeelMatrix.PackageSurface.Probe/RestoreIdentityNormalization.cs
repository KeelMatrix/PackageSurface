using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class RestoreIdentityNormalization
{
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
}
