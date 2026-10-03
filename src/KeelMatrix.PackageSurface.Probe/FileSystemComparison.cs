namespace KeelMatrix.PackageSurface.Probe;

public enum FileSystemCaseSensitivity
{
    Host,
    Sensitive,
    Insensitive
}

enum ResolvedFileSystemCaseSensitivity
{
    Unknown,
    Sensitive,
    Insensitive
}

/// <summary>
/// Canonical filesystem-identity source for project paths and package-relative assets.
/// Comparisons are resolved against the filesystem containing each registered path root.
/// </summary>
sealed class FileSystemComparisonContext
{
    private static readonly AsyncLocal<FileSystemComparisonContext?> CurrentSlot = new();
    private static readonly Lazy<FileSystemComparisonContext> Default = new(() => new(FileSystemCaseSensitivity.Host, Directory.GetCurrentDirectory()));
    private readonly FileSystemCaseSensitivity requestedMode;
    private readonly string seedPath;
    private readonly string? seedProbeKey;
    private readonly Dictionary<string, FileSystemCaseSensitivity> rootOverrides;
    private readonly Dictionary<string, ResolvedFileSystemCaseSensitivity> probeModes = new(StringComparer.Ordinal);
    private readonly List<ProjectPathBinding> projectPaths = new();
    private readonly List<PackageRootBinding> packageRoots = new();
    private string? activeProjectIdentity;

    private FileSystemComparisonContext(
        FileSystemCaseSensitivity requestedMode,
        string seedPath,
        IReadOnlyDictionary<string, FileSystemCaseSensitivity>? rootOverrides = null,
        FileSystemComparisonContext? inherited = null)
    {
        this.requestedMode = requestedMode;
        this.seedPath = Path.GetFullPath(seedPath);
        seedProbeKey = requestedMode == FileSystemCaseSensitivity.Host ? GetProbeKey(this.seedPath) : null;
        this.rootOverrides = inherited is null
            ? new Dictionary<string, FileSystemCaseSensitivity>(StringComparer.Ordinal)
            : new Dictionary<string, FileSystemCaseSensitivity>(inherited.rootOverrides, StringComparer.Ordinal);
        if (inherited is not null)
        {
            projectPaths.AddRange(inherited.projectPaths);
            packageRoots.AddRange(inherited.packageRoots);
        }

        if (rootOverrides is not null)
        {
            foreach (var (path, mode) in rootOverrides)
            {
                if (mode == FileSystemCaseSensitivity.Host) continue;
                this.rootOverrides[Path.GetFullPath(path)] = mode;
            }
        }
    }

    public static FileSystemComparisonContext Current => CurrentSlot.Value ?? Default.Value;

    public static IDisposable Push(string seedPath, FileSystemCaseSensitivity requestedMode)
    {
        var previous = CurrentSlot.Value;
        var current = new FileSystemComparisonContext(requestedMode, seedPath, inherited: previous);
        CurrentSlot.Value = current;
        return new Scope(previous, current);
    }

    public static IDisposable Push(
        string seedPath,
        FileSystemCaseSensitivity requestedMode,
        IReadOnlyDictionary<string, FileSystemCaseSensitivity> rootOverrides)
    {
        var previous = CurrentSlot.Value;
        var current = new FileSystemComparisonContext(requestedMode, seedPath, rootOverrides, previous);
        CurrentSlot.Value = current;
        return new Scope(previous, current);
    }

    public bool TryPathsEqual(string left, string right, out bool equal)
    {
        if (string.Equals(left, right, System.StringComparison.Ordinal))
        {
            equal = true;
            return true;
        }

        if (!string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase))
        {
            equal = false;
            return true;
        }

        var mode = GetCaseSensitivity(left, right);
        if (mode == ResolvedFileSystemCaseSensitivity.Unknown)
        {
            equal = false;
            return false;
        }

        equal = mode == ResolvedFileSystemCaseSensitivity.Insensitive;
        return true;
    }

    public bool TryFileExists(string path, out bool exists)
    {
        switch (requestedMode)
        {
            case FileSystemCaseSensitivity.Host:
                if (TryGetRootOverride(path, out var hostOverride))
                {
                    var hostComparison = hostOverride == ResolvedFileSystemCaseSensitivity.Insensitive
                        ? System.StringComparison.OrdinalIgnoreCase
                        : System.StringComparison.Ordinal;
                    return TryResolveFilePathBySpelling(path, hostComparison, out _, out exists);
                }
                return TryHostFileExists(path, out exists);
            case FileSystemCaseSensitivity.Sensitive:
                exists = PathExistsWithExactSpelling(path);
                return true;
            case FileSystemCaseSensitivity.Insensitive:
                exists = PathExistsIgnoringCase(path);
                return true;
            default:
                exists = false;
                return false;
        }
    }

    public bool TryResolveFilePath(string path, out string resolvedPath, out bool exists)
    {
        resolvedPath = string.Empty;
        exists = false;
        switch (requestedMode)
        {
            case FileSystemCaseSensitivity.Host:
                if (TryGetRootOverride(path, out var hostOverride))
                {
                    var hostComparison = hostOverride == ResolvedFileSystemCaseSensitivity.Insensitive
                        ? System.StringComparison.OrdinalIgnoreCase
                        : System.StringComparison.Ordinal;
                    return TryResolveFilePathBySpelling(path, hostComparison, out resolvedPath, out exists);
                }
                if (!TryHostFileExists(path, out exists)) return false;
                if (exists) resolvedPath = path;
                return true;
            case FileSystemCaseSensitivity.Sensitive:
                return TryResolveFilePathBySpelling(path, System.StringComparison.Ordinal, out resolvedPath, out exists);
            case FileSystemCaseSensitivity.Insensitive:
                return TryResolveFilePathBySpelling(path, System.StringComparison.OrdinalIgnoreCase, out resolvedPath, out exists);
            default:
                return false;
        }
    }

    public int GetStringHashCode(string? value)
    {
        if (value is null) return 0;
        return GetCaseSensitivity(value) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparer.OrdinalIgnoreCase.GetHashCode(value)
            : System.StringComparer.Ordinal.GetHashCode(value);
    }

    public bool ProjectPathsEqual(string? left, string? right)
    {
        if (string.Equals(left, right, System.StringComparison.Ordinal)) return true;
        if (!string.Equals(left, right, System.StringComparison.OrdinalIgnoreCase)) return false;
        return GetProjectIdentityMode(left, right) == ResolvedFileSystemCaseSensitivity.Insensitive;
    }

    public int GetProjectPathHashCode(string? projectPath)
    {
        if (projectPath is null) return 0;
        return GetProjectIdentityMode(projectPath) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparer.OrdinalIgnoreCase.GetHashCode(projectPath)
            : System.StringComparer.Ordinal.GetHashCode(projectPath);
    }

    public bool PackageAssetPathsEqual(
        string? leftProject,
        PackageIdentity leftPackage,
        string? leftPath,
        string? rightProject,
        PackageIdentity rightPackage,
        string? rightPath)
    {
        if (string.Equals(leftPath, rightPath, System.StringComparison.Ordinal)) return true;
        if (!string.Equals(leftPath, rightPath, System.StringComparison.OrdinalIgnoreCase)) return false;
        return GetPackageAssetMode(leftProject, leftPackage, rightProject, rightPackage) == ResolvedFileSystemCaseSensitivity.Insensitive;
    }

    public static int GetPackageAssetPathHashCode(string? assetPath)
    {
        if (assetPath is null) return 0;
        // Exact asset paths compare equal across package versions and roots, even when those roots
        // have different case behavior. A case-folded hash preserves that equality contract; the
        // comparer still keeps case-distinct assets separate on sensitive roots.
        return System.StringComparer.OrdinalIgnoreCase.GetHashCode(assetPath);
    }

    public StringComparison ProjectStringComparison =>
        GetProjectIdentityMode(activeProjectIdentity) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal;

    public StringComparison GetPackageAssetStringComparison(PackageIdentity package) =>
        GetPackageAssetMode(activeProjectIdentity, package, activeProjectIdentity, package) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal;

    public void RegisterProjectPath(string? projectIdentity, string projectPath)
    {
        if (string.IsNullOrEmpty(projectIdentity))
        {
            activeProjectIdentity = null;
            return;
        }

        var normalizedIdentity = NormalizeIdentityPath(projectIdentity);
        activeProjectIdentity = normalizedIdentity;
        var fullPath = Path.GetFullPath(projectPath);
        if (!projectPaths.Any(binding => binding.Identity.Equals(normalizedIdentity, System.StringComparison.Ordinal) &&
                binding.Path.Equals(fullPath, System.StringComparison.Ordinal)))
        {
            projectPaths.Add(new ProjectPathBinding(normalizedIdentity, fullPath));
        }
    }

    public void RegisterPackageRoots(string? projectIdentity, IReadOnlyDictionary<string, string> roots)
    {
        var normalizedIdentity = string.IsNullOrEmpty(projectIdentity) ? null : NormalizeIdentityPath(projectIdentity);
        foreach (var (key, path) in roots)
        {
            var slash = key.IndexOf('/');
            if (slash <= 0 || !PackageIdentity.TryCreate(key[..slash], key[(slash + 1)..], out var package)) continue;
            var fullPath = Path.GetFullPath(path);
            if (!packageRoots.Any(binding => binding.ProjectIdentity == normalizedIdentity &&
                    binding.Package.Equals(package) &&
                    binding.Path.Equals(fullPath, System.StringComparison.Ordinal)))
            {
                packageRoots.Add(new PackageRootBinding(normalizedIdentity, package, fullPath));
            }
        }
    }

    public IEqualityComparer<string> PathComparer => new FileSystemPathEqualityComparer(this);

    private ResolvedFileSystemCaseSensitivity GetProjectIdentityMode(params string?[] identities)
    {
        var matchedModes = new List<ResolvedFileSystemCaseSensitivity>();
        foreach (var identity in identities.Where(value => value is not null).Select(value => NormalizeIdentityPath(value!)))
        {
            foreach (var binding in projectPaths)
            {
                var mode = GetCaseSensitivity(binding.Path);
                if (NamesEqual(identity, binding.Identity, mode)) matchedModes.Add(mode);
            }
        }

        return ConservativeMode(matchedModes, GetCaseSensitivity(seedPath));
    }

    private ResolvedFileSystemCaseSensitivity GetPackageAssetMode(
        string? leftProject,
        PackageIdentity leftPackage,
        string? rightProject,
        PackageIdentity rightPackage)
    {
        var matchedModes = new List<ResolvedFileSystemCaseSensitivity>();
        AddPackageModes(leftProject, leftPackage, matchedModes);
        AddPackageModes(rightProject, rightPackage, matchedModes);
        return ConservativeMode(matchedModes, GetCaseSensitivity(seedPath));
    }

    private void AddPackageModes(string? projectIdentity, PackageIdentity package, List<ResolvedFileSystemCaseSensitivity> modes)
    {
        var normalizedProject = projectIdentity is null ? null : NormalizeIdentityPath(projectIdentity);
        foreach (var binding in packageRoots)
        {
            if (!binding.Package.Equals(package)) continue;
            if (normalizedProject is null)
            {
                if (binding.ProjectIdentity is not null) continue;
            }
            else
            {
                if (binding.ProjectIdentity is null) continue;
                var projectPath = projectPaths.FirstOrDefault(project =>
                    project.Identity.Equals(binding.ProjectIdentity, System.StringComparison.Ordinal))?.Path;
                var projectMode = projectPath is null ? ResolvedFileSystemCaseSensitivity.Unknown : GetCaseSensitivity(projectPath);
                if (!NamesEqual(normalizedProject, binding.ProjectIdentity, projectMode)) continue;
            }

            modes.Add(GetCaseSensitivity(binding.Path));
        }
    }

    private static ResolvedFileSystemCaseSensitivity ConservativeMode(
        IReadOnlyCollection<ResolvedFileSystemCaseSensitivity> modes,
        ResolvedFileSystemCaseSensitivity fallback)
    {
        if (modes.Count == 0) return fallback;
        return modes.All(mode => mode == ResolvedFileSystemCaseSensitivity.Insensitive)
            ? ResolvedFileSystemCaseSensitivity.Insensitive
            : ResolvedFileSystemCaseSensitivity.Sensitive;
    }

    private static bool NamesEqual(string left, string right, ResolvedFileSystemCaseSensitivity mode) =>
        string.Equals(left, right, mode == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal);

    private static string NormalizeIdentityPath(string path) => path.Replace('\\', '/');

    private ResolvedFileSystemCaseSensitivity GetCaseSensitivity(params string[] paths)
    {
        var pathsToProbe = paths.Length == 0 ? new[] { seedPath } : paths;
        foreach (var path in pathsToProbe)
        {
            if (TryGetRootOverride(path, out var overridden)) return overridden;
            if (requestedMode == FileSystemCaseSensitivity.Sensitive) return ResolvedFileSystemCaseSensitivity.Sensitive;
            if (requestedMode == FileSystemCaseSensitivity.Insensitive) return ResolvedFileSystemCaseSensitivity.Insensitive;

            var probeKey = string.Equals(path, seedPath, System.StringComparison.Ordinal)
                ? seedProbeKey ?? GetProbeKey(path)
                : GetProbeKey(path);
            if (probeModes.TryGetValue(probeKey, out var cached)) return cached;

            var detected = Detect(path);
            if (detected != ResolvedFileSystemCaseSensitivity.Unknown)
            {
                probeModes[probeKey] = detected;
                return detected;
            }
        }

        return ResolvedFileSystemCaseSensitivity.Unknown;
    }

    private bool TryGetRootOverride(string path, out ResolvedFileSystemCaseSensitivity mode)
    {
        mode = ResolvedFileSystemCaseSensitivity.Unknown;
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return false;
        }

        string? selectedRoot = null;
        foreach (var root in rootOverrides.Keys)
        {
            var relative = Path.GetRelativePath(root, fullPath);
            if (Path.IsPathRooted(relative) || relative == ".." ||
                relative.StartsWith(".." + Path.DirectorySeparatorChar, PlatformPathComparison) ||
                relative.StartsWith(".." + Path.AltDirectorySeparatorChar, PlatformPathComparison)) continue;
            if (selectedRoot is null || root.Length > selectedRoot.Length) selectedRoot = root;
        }

        if (selectedRoot is null) return false;
        mode = rootOverrides[selectedRoot] == FileSystemCaseSensitivity.Insensitive
            ? ResolvedFileSystemCaseSensitivity.Insensitive
            : ResolvedFileSystemCaseSensitivity.Sensitive;
        return true;
    }

    private static System.StringComparison PlatformPathComparison => OperatingSystem.IsWindows()
        ? System.StringComparison.OrdinalIgnoreCase
        : System.StringComparison.Ordinal;

    private static ResolvedFileSystemCaseSensitivity Detect(string path)
    {
        try
        {
            var directory = FindExistingDirectory(path);
            while (!string.IsNullOrEmpty(directory))
            {
                var entries = Directory.EnumerateFileSystemEntries(directory)
                    .Select(Path.GetFileName)
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Cast<string>()
                    .ToArray();
                foreach (var entry in entries)
                {
                    var alternateName = ToggleCase(entry);
                    if (alternateName is null || string.Equals(entry, alternateName, System.StringComparison.Ordinal)) continue;
                    if (entries.Any(candidate => string.Equals(candidate, alternateName, System.StringComparison.Ordinal)))
                    {
                        return ResolvedFileSystemCaseSensitivity.Sensitive;
                    }

                    var alternatePath = Path.Combine(directory, alternateName);
                    if (!TryPathExists(alternatePath, out var alternateExists))
                    {
                        return ResolvedFileSystemCaseSensitivity.Unknown;
                    }

                    if (alternateExists)
                    {
                        return ResolvedFileSystemCaseSensitivity.Insensitive;
                    }

                    return ResolvedFileSystemCaseSensitivity.Sensitive;
                }

                var parent = Directory.GetParent(directory)?.FullName;
                if (parent is null || string.Equals(parent, directory, System.StringComparison.Ordinal)) break;
                directory = parent;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
        }

        return ResolvedFileSystemCaseSensitivity.Unknown;
    }

    private static string? FindExistingDirectory(string path)
    {
        var current = Path.GetFullPath(path);
        while (!Directory.Exists(current))
        {
            var parent = Directory.GetParent(current)?.FullName;
            if (parent is null || string.Equals(parent, current, System.StringComparison.Ordinal)) return null;
            current = parent;
        }

        return current;
    }

    private static string? ToggleCase(string value)
    {
        var characters = value.ToCharArray();
        for (var index = 0; index < characters.Length; index++)
        {
            if (char.IsLetter(characters[index]))
            {
                characters[index] = char.IsUpper(characters[index])
                    ? char.ToLowerInvariant(characters[index])
                    : char.ToUpperInvariant(characters[index]);
                return new string(characters);
            }
        }

        return null;
    }

    private static string GetProbeKey(string path)
    {
        try
        {
            return FindExistingDirectory(path) ?? Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            return path;
        }
    }

    private static bool TryHostFileExists(string path, out bool exists)
    {
        exists = false;
        try
        {
            if (File.Exists(path))
            {
                exists = true;
                return true;
            }

            var parent = Path.GetDirectoryName(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(parent)) return true;
            _ = Directory.EnumerateFileSystemEntries(parent).ToArray();
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryPathExists(string path, out bool exists)
    {
        try
        {
            _ = File.GetAttributes(path);
            exists = true;
            return true;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            exists = false;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            exists = false;
            return false;
        }
    }

    private static bool PathExistsWithExactSpelling(string path)
    {
        return TryResolveFilePathBySpelling(path, System.StringComparison.Ordinal, out _, out var exists) && exists;
    }

    private static bool PathExistsIgnoringCase(string path)
    {
        return TryResolveFilePathBySpelling(path, System.StringComparison.OrdinalIgnoreCase, out _, out var exists) && exists;
    }

    private static bool TryResolveFilePathBySpelling(
        string path,
        System.StringComparison comparison,
        out string resolvedPath,
        out bool exists)
    {
        resolvedPath = string.Empty;
        exists = false;
        if (comparison == System.StringComparison.OrdinalIgnoreCase && File.Exists(path))
        {
            resolvedPath = path;
            exists = true;
            return true;
        }

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return false;
            var current = root;
            var remaining = fullPath[root.Length..];
            var components = remaining.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < components.Length; index++)
            {
                var component = components[index];
                var matches = Directory.EnumerateFileSystemEntries(current)
                    .Select(Path.GetFileName)
                    .Where(name => name is not null && string.Equals(name, component, comparison))
                    .Take(2)
                    .ToArray();
                if (matches.Length == 0) return true;
                if (matches.Length != 1) return false;
                current = Path.Combine(current, matches[0]!);
                if (index < components.Length - 1)
                {
                    var attributes = File.GetAttributes(current);
                    if ((attributes & FileAttributes.Directory) == 0) return true;
                }
            }

            resolvedPath = current;
            exists = File.Exists(current);
            return true;
        }
        catch (FileNotFoundException)
        {
            return true;
        }
        catch (DirectoryNotFoundException)
        {
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private void MergeBindingsFrom(FileSystemComparisonContext child)
    {
        foreach (var binding in child.projectPaths)
        {
            if (!projectPaths.Contains(binding)) projectPaths.Add(binding);
        }

        foreach (var binding in child.packageRoots)
        {
            if (!packageRoots.Contains(binding)) packageRoots.Add(binding);
        }
    }

    private sealed record ProjectPathBinding(string Identity, string Path);

    private sealed record PackageRootBinding(string? ProjectIdentity, PackageIdentity Package, string Path);

    private sealed class Scope(FileSystemComparisonContext? previous, FileSystemComparisonContext current) : IDisposable
    {
        private bool disposed;

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            previous?.MergeBindingsFrom(current);
            CurrentSlot.Value = previous;
        }
    }
}

sealed class FileSystemPathEqualityComparer(FileSystemComparisonContext context) : IEqualityComparer<string>
{
    public bool Equals(string? x, string? y)
    {
        if (x is null || y is null) return x is null && y is null;
        return context.TryPathsEqual(x, y, out var equal) && equal;
    }

    public int GetHashCode(string value) => context.GetStringHashCode(value);
}
