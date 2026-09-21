using System.Security.Cryptography;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace KeelMatrix.PackageSurface.Probe;

public static class ResolvedGraphClassifier
{
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    public static ProbeResult Analyze(string assetsFile, string projectRoot)
    {
        var incomplete = new List<string>();
        try
        {
            using var stream = File.OpenRead(assetsFile);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 128 });
            var root = document.RootElement;
            var packageFolders = ReadPackageFolders(root, incomplete);
            var libraries = root.TryGetProperty("libraries", out var librariesElement)
                ? librariesElement
                : throw new InvalidDataException("project.assets.json has no libraries object.");
            var directPackages = ReadDirectPackages(root);
            var generatedImports = ReadGeneratedImports(projectRoot, incomplete);
            var entries = new List<SurfaceEntry>();

            if (!root.TryGetProperty("targets", out var targets))
            {
                throw new InvalidDataException("project.assets.json has no targets object.");
            }

            foreach (var target in targets.EnumerateObject())
            {
                var (tfm, rid) = SplitTarget(target.Name);
                foreach (var package in target.Value.EnumerateObject())
                {
                    var (packageId, version) = SplitPackageKey(package.Name);
                    var libraryKey = packageId + "/" + version;
                    if (!libraries.TryGetProperty(libraryKey, out var library))
                    {
                        incomplete.Add($"Target {target.Name} refers to missing library {libraryKey}.");
                        continue;
                    }

                    if (!library.TryGetProperty("path", out var pathElement))
                    {
                        incomplete.Add($"Library {libraryKey} has no package path.");
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
                        var present = File.Exists(physicalPath);
                        var reason = present ? null : $"Reachable asset is missing from resolved package contents: {relativePath}.";
                        if (!present)
                        {
                            incomplete.Add($"{libraryKey}: {reason}");
                        }

                        var active = IsActive(capability, relativePath, packageRoot, targetAssets, generatedImports);
                        var sha = present ? ComputeSha256(physicalPath) : null;
                        entries.Add(new SurfaceEntry(
                            tfm,
                            rid,
                            packageId,
                            version,
                            directPackages.Contains(packageId) ? "direct" : "transitive",
                            capability,
                            relativePath,
                            present,
                            active,
                            sha,
                            !present,
                            reason));
                    }
                }
            }

            return new ProbeResult(Deduplicate(entries), incomplete.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
        {
            incomplete.Add(ex.Message);
            return new ProbeResult(Array.Empty<SurfaceEntry>(), incomplete.Distinct(StringComparer.Ordinal).ToArray());
        }
    }

    private static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(entry => string.Join("|", entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Capability, entry.PackageRelativePath), StringComparer.OrdinalIgnoreCase)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
        .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
        .ThenBy(entry => entry.Capability)
        .ThenBy(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static HashSet<string> ReadDirectPackages(JsonElement root)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!root.TryGetProperty("project", out var project) || !project.TryGetProperty("frameworks", out var frameworks))
        {
            return result;
        }

        foreach (var framework in frameworks.EnumerateObject())
        {
            if (!framework.Value.TryGetProperty("dependencies", out var dependencies))
            {
                continue;
            }

            foreach (var dependency in dependencies.EnumerateObject())
            {
                result.Add(dependency.Name);
            }
        }

        return result;
    }

    private static string[] ReadPackageFolders(JsonElement root, List<string> incomplete)
    {
        if (!root.TryGetProperty("packageFolders", out var folders))
        {
            incomplete.Add("project.assets.json has no packageFolders object.");
            return Array.Empty<string>();
        }

        return folders.EnumerateObject().Select(folder => folder.Name).ToArray();
    }

    private static IReadOnlyList<string> ReadLibraryFiles(JsonElement library, string libraryKey, List<string> incomplete)
    {
        if (!library.TryGetProperty("files", out var files) || files.ValueKind != JsonValueKind.Array)
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

    private static string ResolvePackageRoot(string packageId, string version, string libraryPath, IReadOnlyList<string> packageFolders, List<string> incomplete)
    {
        if (!TryNormalizeRelativePath(libraryPath, out var normalizedLibraryPath))
        {
            incomplete.Add($"Package {packageId}/{version} has an unsafe cache-relative path.");
            return Path.GetTempPath();
        }

        foreach (var folder in packageFolders)
        {
            var candidate = Path.GetFullPath(Path.Combine(folder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
            if (Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        incomplete.Add($"Package {packageId}/{version} was not found in the resolved package folders.");
        var firstFolder = packageFolders.Count > 0 ? packageFolders[0] : Path.GetTempPath();
        return Path.GetFullPath(Path.Combine(firstFolder, normalizedLibraryPath.Replace('/', Path.DirectorySeparatorChar)));
    }

    private static Dictionary<string, List<string>> ReadGeneratedImports(string projectRoot, List<string> incomplete)
    {
        var result = new Dictionary<string, List<string>>(PathComparer);
        foreach (var file in Directory.EnumerateFiles(projectRoot, "*.nuget.g.*", SearchOption.AllDirectories))
        {
            if (!file.EndsWith(".props", StringComparison.OrdinalIgnoreCase) && !file.EndsWith(".targets", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, MaxCharactersFromEntities = 0, IgnoreComments = true, IgnoreWhitespace = true };
                using var reader = XmlReader.Create(file, settings);
                var xml = XDocument.Load(reader, LoadOptions.None);
                foreach (var import in xml.Descendants().Where(element => element.Name.LocalName == "Import"))
                {
                    var project = import.Attribute("Project")?.Value;
                    if (project is not null)
                    {
                        result.GetOrAdd(Path.GetFileName(file), _ => new List<string>()).Add(project);
                    }
                }
            }
            catch (XmlException ex)
            {
                incomplete.Add($"Generated import file {Path.GetFileName(file)} is malformed: {ex.Message}");
            }
        }

        return result;
    }

    private static bool IsActive(CapabilityKind capability, string relativePath, string packageRoot, JsonElement targetAssets, Dictionary<string, List<string>> generatedImports)
    {
        if (capability == CapabilityKind.ToolOrScriptPresent)
        {
            return false;
        }

        if (capability is CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting)
        {
            var fullAsset = NormalizeText(Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            return generatedImports.Values.SelectMany(values => values).Any(import =>
            {
                var normalizedImport = NormalizeText(import);
                return normalizedImport.Contains(fullAsset, StringComparison.OrdinalIgnoreCase) ||
                       normalizedImport.EndsWith('/' + relativePath, StringComparison.OrdinalIgnoreCase) ||
                       normalizedImport.EndsWith('\\' + relativePath.Replace('/', '\\'), StringComparison.OrdinalIgnoreCase);
            });
        }

        var assetName = relativePath.Replace('\\', '/');
        return capability switch
        {
            CapabilityKind.CompilerExtension => IsConventionalAnalyzerPath(assetName) && IsReachablePackage(targetAssets),
            CapabilityKind.CompileSourceInjection => ContainsAsset(targetAssets, "contentFiles", assetName),
            CapabilityKind.NativeRuntime => ContainsAsset(targetAssets, "native", assetName) || ContainsAsset(targetAssets, "runtime", assetName),
            _ => false
        };
    }

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

    private static bool IsConventionalAnalyzerPath(string path) =>
        path.StartsWith("analyzers/dotnet/cs/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("analyzers/dotnet/vb/", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("analyzers/dotnet/", StringComparison.OrdinalIgnoreCase) && path.Count(character => character == '/') == 2;

    private static bool IsReachablePackage(JsonElement targetAssets) => targetAssets.ValueKind == JsonValueKind.Object;

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
        if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') || Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part == ".."))
        {
            normalized = string.Empty;
            return false;
        }

        return true;
    }

    private static string NormalizeText(string value) => value.Replace('\\', '/').Trim();

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static string? GetString(this JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static TValue GetOrAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, Func<TKey, TValue> factory) where TKey : notnull
    {
        if (!dictionary.TryGetValue(key, out var value))
        {
            value = factory(key);
            dictionary.Add(key, value);
        }

        return value;
    }
}
