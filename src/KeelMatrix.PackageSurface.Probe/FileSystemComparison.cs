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

sealed class FileSystemComparisonContext
{
    private static readonly AsyncLocal<FileSystemComparisonContext?> CurrentSlot = new();
    private static readonly Lazy<FileSystemComparisonContext> Default = new(() => new(FileSystemCaseSensitivity.Host, Directory.GetCurrentDirectory()));
    private readonly FileSystemCaseSensitivity requestedMode;
    private readonly string seedPath;
    private readonly Dictionary<string, ResolvedFileSystemCaseSensitivity> probeModes = new(StringComparer.Ordinal);

    private FileSystemComparisonContext(FileSystemCaseSensitivity requestedMode, string seedPath)
    {
        this.requestedMode = requestedMode;
        this.seedPath = seedPath;
    }

    public static FileSystemComparisonContext Current => CurrentSlot.Value ?? Default.Value;

    public static IDisposable Push(string seedPath, FileSystemCaseSensitivity requestedMode)
    {
        var previous = CurrentSlot.Value;
        CurrentSlot.Value = new FileSystemComparisonContext(requestedMode, seedPath);
        return new Scope(previous);
    }

    public StringComparison StringComparison => GetComparison(seedPath);

    public StringComparer StringComparer => GetCaseSensitivity(seedPath) == ResolvedFileSystemCaseSensitivity.Insensitive
        ? System.StringComparer.OrdinalIgnoreCase
        : System.StringComparer.Ordinal;

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

    public int GetStringHashCode(string? value)
    {
        if (value is null) return 0;
        return GetCaseSensitivity(value) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparer.OrdinalIgnoreCase.GetHashCode(value)
            : System.StringComparer.Ordinal.GetHashCode(value);
    }

    public IEqualityComparer<string> PathComparer => new FileSystemPathEqualityComparer(this);

    private StringComparison GetComparison(string path) =>
        GetCaseSensitivity(path) == ResolvedFileSystemCaseSensitivity.Insensitive
            ? System.StringComparison.OrdinalIgnoreCase
            : System.StringComparison.Ordinal;

    private ResolvedFileSystemCaseSensitivity GetCaseSensitivity(params string[] paths)
    {
        if (requestedMode == FileSystemCaseSensitivity.Sensitive) return ResolvedFileSystemCaseSensitivity.Sensitive;
        if (requestedMode == FileSystemCaseSensitivity.Insensitive) return ResolvedFileSystemCaseSensitivity.Insensitive;

        foreach (var path in paths.Append(seedPath))
        {
            var probeKey = GetProbeKey(path);
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
        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return false;
            var current = root;
            var remaining = fullPath[root.Length..];
            foreach (var component in remaining.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(current)) return false;
                var match = Directory.EnumerateFileSystemEntries(current)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, component, System.StringComparison.Ordinal));
                if (match is null) return false;
                current = Path.Combine(current, match);
            }

            return File.Exists(current);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool PathExistsIgnoringCase(string path)
    {
        if (File.Exists(path)) return true;

        try
        {
            var fullPath = Path.GetFullPath(path);
            var root = Path.GetPathRoot(fullPath);
            if (string.IsNullOrEmpty(root)) return false;
            var current = root;
            var remaining = fullPath[root.Length..];
            foreach (var component in remaining.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries))
            {
                if (!Directory.Exists(current)) return false;
                var match = Directory.EnumerateFileSystemEntries(current)
                    .Select(Path.GetFileName)
                    .FirstOrDefault(name => string.Equals(name, component, System.StringComparison.OrdinalIgnoreCase));
                if (match is null) return false;
                current = Path.Combine(current, match);
            }

            return File.Exists(current);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return false;
        }
    }

    private sealed class Scope(FileSystemComparisonContext? previous) : IDisposable
    {
        public void Dispose() => CurrentSlot.Value = previous;
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
