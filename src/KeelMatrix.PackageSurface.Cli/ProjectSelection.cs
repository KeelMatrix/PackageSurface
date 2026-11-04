using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

public sealed record SelectedProject(string ProjectRoot, string AssetsPath, string DisplayPath, string ProjectPath);

public sealed record ProjectSelection(IReadOnlyList<SelectedProject> Projects)
{
    private const int MaxProjects = 128;
    private const int MaxProjectMemberships = 4_096;
    private const long MaxInputBytes = 16 * 1024 * 1024;

    public static ProjectSelection Resolve(string inputPath, string? projectPath)
    {
        var input = Path.GetFullPath(inputPath);
        if (!File.Exists(input) && !Directory.Exists(input))
        {
            throw new InvalidDataException("The input path does not exist or is not a supported SDK-style project input.");
        }

        if (File.Exists(input) && new FileInfo(input).Length > MaxInputBytes)
        {
            throw new InvalidDataException("The selected input exceeds the supported size limit.");
        }

        if (projectPath is not null)
        {
            var selectedPath = Path.GetFullPath(projectPath);
            if (File.Exists(input) && input.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
            {
                var root = Path.GetDirectoryName(input)!;
                var members = ReadSolutionProjectPaths(input, root, enforceAnalysisLimit: false);
                if (!members.Any(path => PathsEqual(path, selectedPath)))
                {
                    throw new InvalidDataException("The --project path is not a member of the supplied solution.");
                }

                return new(new[] { ResolveProject(selectedPath, root) });
            }

            if (File.Exists(input) && input.EndsWith(".assets.json", StringComparison.OrdinalIgnoreCase))
            {
                var restored = ReadRestoreProjectPath(input);
                if (restored is null || !PathsEqual(restored, selectedPath))
                {
                    throw new InvalidDataException("The --project path does not match the supplied restore artifact.");
                }

                return new(new[] { ResolveAssets(input) });
            }

            if (File.Exists(input) && IsSupportedProjectPath(input))
            {
                if (!PathsEqual(input, selectedPath)) throw new InvalidDataException("The --project path does not match the supplied project input.");
                return new(new[] { ResolveProject(selectedPath, Path.GetDirectoryName(input)!) });
            }

            if (Directory.Exists(input))
            {
                var members = ReadDirectoryProjectPaths(input, enforceAnalysisLimit: false);
                if (!members.Any(path => PathsEqual(path, selectedPath)))
                {
                    throw new InvalidDataException("The --project path is not a member of the supplied directory input.");
                }

                return new(new[] { ResolveProject(selectedPath, input) });
            }

            throw new InvalidDataException("The --project option can only narrow a supported project, solution, directory, or restore artifact.");
        }

        if (File.Exists(input) && IsSupportedProjectPath(input)) return new(new[] { ResolveProject(input, Path.GetDirectoryName(input)!) });
        if (File.Exists(input) && input.EndsWith(".assets.json", StringComparison.OrdinalIgnoreCase)) return new(new[] { ResolveAssets(input) });
        if (File.Exists(input) && input.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(input)!;
            return new(ReadSolutionProjectPaths(input, root, enforceAnalysisLimit: true).Select(path => ResolveProject(path, root)).ToArray());
        }

        if (Directory.Exists(input))
        {
            var direct = Path.Combine(input, "obj", "project.assets.json");
            if (File.Exists(direct)) return new(new[] { ResolveAssets(direct) });
            return new(ReadDirectoryProjectPaths(input, enforceAnalysisLimit: true).Select(path => ResolveProject(path, input)).ToArray());
        }

        throw new InvalidDataException("The input path does not exist or is not a supported SDK-style project input.");
    }

    private static SelectedProject ResolveProject(string projectPath, string displayRoot)
    {
        if (!IsSupportedProjectPath(projectPath)) throw new InvalidDataException("The selected project kind is unsupported.");
        if (!File.Exists(projectPath)) throw new InvalidDataException("The selected project does not exist.");
        var root = Path.GetDirectoryName(projectPath)!;
        var display = NormalizeDisplay(Path.GetRelativePath(displayRoot, projectPath));
        return new(root, Path.Combine(root, "obj", "project.assets.json"), display, projectPath);
    }

    private static SelectedProject ResolveAssets(string assetsPath)
    {
        var obj = Path.GetDirectoryName(assetsPath)!;
        var root = Directory.GetParent(obj)?.FullName ?? obj;
        var projectPath = ReadRestoreProjectPath(assetsPath);
        var display = projectPath is null ? NormalizeDisplay(Path.GetFileName(root) + ".proj") : NormalizeDisplay(Path.GetFileName(projectPath));
        return new(root, assetsPath, display, projectPath ?? Path.Combine(root, display));
    }

    private static string? ReadRestoreProjectPath(string assetsPath)
        => ResolvedGraphClassifier.ReadRestoreProjectPath(assetsPath);

    private static string NormalizeDisplay(string value) => value.Replace('\\', '/');

    private static bool IsSupportedProjectPath(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);

    private static string[] ReadSolutionProjectPaths(string solutionPath, string root, bool enforceAnalysisLimit)
    {
        var paths = new List<string>();
        foreach (var line in File.ReadLines(solutionPath))
        {
            var comma = line.IndexOf(", \"", StringComparison.Ordinal);
            if (comma < 0 || !line.StartsWith("Project(", StringComparison.Ordinal)) continue;
            var start = line.IndexOf('"', comma);
            var end = line.IndexOf('"', start + 1);
            if (start < 0 || end < 0) continue;
            var relativePath = line[(start + 1)..end];
            if (!Path.HasExtension(relativePath)) continue;
            if (!IsSupportedProjectPath(relativePath)) throw new InvalidDataException("The solution contains an unsupported project kind.");
            paths.Add(Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar))));
            EnsureMembershipLimit(paths.Count);
        }

        if (enforceAnalysisLimit) EnsureProjectLimit(paths.Count);
        return paths.ToArray();
    }

    private static string[] ReadDirectoryProjectPaths(string input, bool enforceAnalysisLimit)
    {
        var paths = Directory.EnumerateFiles(input, "*proj", SearchOption.TopDirectoryOnly).Take(MaxProjectMemberships + 1).ToArray();
        if (paths.Any(path => !IsSupportedProjectPath(path))) throw new InvalidDataException("The directory contains an unsupported project kind.");
        EnsureMembershipLimit(paths.Length);
        if (enforceAnalysisLimit) EnsureProjectLimit(paths.Length);
        return paths;
    }

    private static bool PathsEqual(string left, string right) => FileSystemComparisonScope.PathsEqual(left, right);

    private static void EnsureMembershipLimit(int count)
    {
        if (count > MaxProjectMemberships) throw new InvalidDataException("The input contains too many project members to validate safely.");
    }

    private static void EnsureProjectLimit(int count)
    {
        if (count > MaxProjects) throw new InvalidDataException("The input contains more than 128 supported projects; narrow the input with --project.");
    }
}
