using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeelMatrix.PackageSurface.Probe;
using KeelMatrix.Telemetry;
using Microsoft.Win32.SafeHandles;

namespace KeelMatrix.PackageSurface;

public static class CommandLine
{
    private const int SchemaVersion = 1;
    private const long MaxOutputBytes = 16 * 1024 * 1024;
    private const long MaxInputBytes = 16 * 1024 * 1024;
    private const long MaxTotalInputBytes = 128 * 1024 * 1024;
    private static readonly string[] GenericIncompleteReasons = { "The input, baseline, or restore evidence could not be analyzed completely." };
    public const string ToolVersion = "0.1.0";

    public static int Run(string[] args)
    {
        ParseResult parsed;
        try
        {
            parsed = Options.Parse(args);
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
            return 2;
        }
        if (parsed.Kind == ParseResultKind.Help)
        {
            Console.WriteLine(Options.HelpText);
            return 0;
        }

        if (parsed.Kind == ParseResultKind.Version)
        {
            Console.WriteLine(ToolVersion);
            return 0;
        }

        if (parsed.Kind == ParseResultKind.Invalid)
        {
            Console.Error.WriteLine($"error: {parsed.Error}");
            Console.Error.WriteLine("Run 'package-surface --help' for usage.");
            return 2;
        }

        var options = parsed.Options!;
        try
        {
            var baselinePath = options.Command == CommandKind.Baseline ? Path.GetFullPath(options.OutputPath!) : null;
            BaselineDocument? baseline = null;
            var strictContent = options.StrictContent;
            if (options.Command == CommandKind.Check)
            {
                baseline = BaselineDocument.Read(options.BaselinePath!);
                strictContent |= baseline.StrictContent;
            }

            var selection = ProjectSelection.Resolve(options.InputPath!, options.ProjectPath);
            var baselineTransaction = baselinePath is null
                ? null
                : BaselineFileTransaction.Prepare(baselinePath, options.InputPath!, selection);
            var current = AnalyzeSelection(selection, strictContent, options.CompilerApiVersion);
            var diagnostics = current.IncompleteReasons
                .Select(reason => Diagnostic.Create("PS007", reason))
                .ToList();

            if (options.Command == CommandKind.Check && diagnostics.Count == 0)
            {
                if (options.StrictContent && !baseline!.StrictContent)
                {
                    diagnostics.Add(Diagnostic.Create(
                        "PS007",
                        "--strict-content requires a baseline created with strict-content fingerprints; regenerate the baseline explicitly before checking."));
                }
                else
                {
                    diagnostics.AddRange(DiffEngine.Compare(baseline!.Entries, current.Entries, strictContent));
                }
            }

            var incomplete = diagnostics.Any(diagnostic => diagnostic.Id == "PS007");
            var report = ReportDocument.Create(options.Command, current, diagnostics);
            try
            {
                if (options.Command == CommandKind.Baseline && !incomplete)
                {
                    try
                    {
                        report.EnsureOutputWithinLimit(MaxOutputBytes);
                        baselineTransaction!.Begin();
                        BaselineDocument.Write(options.OutputPath!, current);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or NotSupportedException)
                    {
                        baselineTransaction!.Rollback();
                        diagnostics.Add(Diagnostic.Create("PS007", "The baseline could not be persisted after validating the analyzed surface."));
                        incomplete = true;
                        report = ReportDocument.Create(options.Command, current, diagnostics);
                    }
                }

                WriteOutput(report, options.Format);
                baselineTransaction?.Commit();
            }
            catch when (baselineTransaction is not null)
            {
                baselineTransaction.Rollback();
                throw;
            }

            if (!incomplete &&
                (options.Command is CommandKind.Baseline or CommandKind.Check) &&
                current.ResolvedPackageCount > 0 &&
                options.TelemetryEnabled)
            {
                TrackActivation();
            }

            if (diagnostics.Count > 0)
            {
                return incomplete ? 2 : 1;
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or NotSupportedException or InvalidOperationException)
        {
            var message = SafeFailureMessage(ex);
            var report = ReportDocument.Create(
                options.Command,
                new SurfaceSnapshot(Array.Empty<SurfaceEntry>(), GenericIncompleteReasons, options.StrictContent, 0),
                new[] { Diagnostic.Create("PS007", message) });
            try
            {
                WriteOutput(report, options.Format);
            }
            catch
            {
                Console.Error.WriteLine("error: analysis or baseline input is invalid, missing, or unreadable.");
            }
            return 2;
        }
    }

    private static SurfaceSnapshot AnalyzeSelection(ProjectSelection selection, bool strictContent, string? compilerApiVersion)
    {
        var entries = new List<SurfaceEntry>();
        var reasons = new List<string>();
        var resolvedPackages = 0;
        long totalInputBytes = 0;
        var budget = new AnalysisBudget();
        foreach (var project in selection.Projects)
        {
            budget.AddOperation("selected project");
            if (!File.Exists(project.AssetsPath))
            {
                reasons.Add($"{project.DisplayPath}: project.assets.json is missing; run restore before analysis.");
                continue;
            }

            var inputBytes = new FileInfo(project.AssetsPath).Length;
            if (inputBytes > MaxInputBytes || totalInputBytes > MaxTotalInputBytes - inputBytes)
            {
                reasons.Add($"{project.DisplayPath}: the selected restore inputs exceed the supported invocation byte limit.");
                continue;
            }

            totalInputBytes += inputBytes;

            var result = ResolvedGraphClassifier.Analyze(project.AssetsPath, project.ProjectRoot, strictContent, project.DisplayPath, project.ProjectPath, budget, compilerApiVersion);
            budget.AddEntries(result.Entries.Count);
            entries.AddRange(result.Entries);
            foreach (var reason in result.IncompleteReasons)
            {
                budget.AddOperation("diagnostic");
                reasons.Add($"{project.DisplayPath}: {reason}");
            }
            resolvedPackages += result.ResolvedPackageCount;
        }

        if (selection.Projects.Count == 0)
        {
            reasons.Add("No supported SDK-style project with an existing project.assets.json was found.");
        }

        if (reasons.Count > 0)
        {
            return SurfaceSnapshot.Create(Array.Empty<SurfaceEntry>(), reasons, strictContent, 0);
        }

        return SurfaceSnapshot.Create(entries, reasons, strictContent, resolvedPackages);
    }

    private static string SafeFailureMessage(Exception exception)
    {
        if (exception is InvalidDataException invalid &&
            !invalid.Message.Contains(Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !invalid.Message.Contains(Path.AltDirectorySeparatorChar, StringComparison.Ordinal) &&
            !invalid.Message.Contains(":\\", StringComparison.Ordinal))
        {
            return invalid.Message;
        }

        return "The input, baseline, or restore evidence could not be analyzed completely.";
    }

    private static Action? TelemetryHook { get; set; }

    private static Func<ReportDocument, OutputFormat, string>? OutputSerializer { get; set; }

    private static Action<string>? OutputSink { get; set; }

    private static void TrackActivation()
    {
        try
        {
            if (TelemetryHook is not null)
            {
                TelemetryHook();
                return;
            }

            new Client("PackageSurface", typeof(CommandLine)).TrackActivation();
        }
        catch
        {
        }
    }

    private static void WriteOutput(ReportDocument report, OutputFormat format)
    {
        report.EnsureOutputWithinLimit(MaxOutputBytes);
        var output = OutputSerializer?.Invoke(report, format) ?? format switch
        {
            OutputFormat.Text => report.ToText(),
            OutputFormat.Json => JsonSerializer.Serialize(report, JsonOptions.Indented),
            OutputFormat.Sarif => JsonSerializer.Serialize(report.ToSarif(), JsonOptions.Indented),
            _ => throw new InvalidDataException("Unsupported output format.")
        };
        if (Encoding.UTF8.GetByteCount(output) > MaxOutputBytes)
        {
            throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        if (OutputSink is not null)
        {
            OutputSink(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }

}

public sealed class BaselineFileTransaction
{
    private const long MaxBaselineBytes = 16 * 1024 * 1024;
    private readonly string path;
    private readonly string backupPath;
    private readonly bool existed;
    private bool began;

    private BaselineFileTransaction(string path, bool existed)
    {
        this.path = path;
        this.existed = existed;
        backupPath = path + ".rollback-" + Guid.NewGuid().ToString("N");
    }

    public static BaselineFileTransaction Prepare(string path, string inputPath, ProjectSelection selection)
    {
        var fullPath = Path.GetFullPath(path);
        var candidates = new List<string> { Path.GetFullPath(inputPath) };
        foreach (var project in selection.Projects)
        {
            candidates.Add(project.ProjectPath);
            candidates.Add(project.AssetsPath);
            var generatedDirectory = Path.GetDirectoryName(project.AssetsPath)!;
            var projectName = Path.GetFileName(project.ProjectPath);
            candidates.Add(Path.Combine(generatedDirectory, projectName + ".nuget.g.props"));
            candidates.Add(Path.Combine(generatedDirectory, projectName + ".nuget.g.targets"));
            candidates.AddRange(ResolvedGraphClassifier.GetReachablePackageInputPaths(project.AssetsPath));
        }

        var candidateComparer = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        foreach (var candidate in candidates.Distinct(candidateComparer))
        {
            if (PathsReferToSameFile(fullPath, candidate))
            {
                throw new InvalidDataException("The baseline output must not alias the selected solution, project, restore, generated-import, or reachable package input.");
            }
        }

        var existed = File.Exists(fullPath);
        if (Directory.Exists(fullPath)) throw new InvalidDataException("The baseline output path is a directory.");
        if (existed)
        {
            var info = new FileInfo(fullPath);
            if (info.Length > MaxBaselineBytes) throw new InvalidDataException("The existing baseline output exceeds the supported size limit.");
            if (info.IsReadOnly || (OperatingSystem.IsWindows() ? false : (File.GetUnixFileMode(fullPath) & (UnixFileMode.UserWrite | UnixFileMode.GroupWrite | UnixFileMode.OtherWrite)) == 0))
            {
                throw new UnauthorizedAccessException("The existing baseline output is read-only.");
            }
        }

        return new BaselineFileTransaction(fullPath, existed);
    }

    public void Begin()
    {
        if (began) return;
        if (existed) File.Move(path, backupPath);
        began = true;
    }

    public void Commit()
    {
        if (began && existed && File.Exists(backupPath)) File.Delete(backupPath);
        began = false;
    }

    public void Rollback()
    {
        if (!began) return;
        var restored = false;
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (!existed)
            {
                restored = true;
            }
            else
            {
                File.Move(backupPath, path);
                restored = true;
            }
        }
        finally
        {
            if (restored)
            {
                if (File.Exists(backupPath)) File.Delete(backupPath);
                began = false;
            }
        }
    }

    private static bool PathsReferToSameFile(string left, string right)
    {
        if (string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) return true;
        if (HasReparsePoint(left) || HasReparsePoint(right)) throw new InvalidDataException("The baseline output or an analysis input uses a symlink or reparse-point path.");
        if (!File.Exists(left) || !File.Exists(right)) return false;

        if (!FileIdentity.TryRead(left, out var leftIdentity) || !FileIdentity.TryRead(right, out var rightIdentity))
        {
            throw new InvalidDataException("The baseline alias could not be validated using file identity.");
        }

        return leftIdentity == rightIdentity;
    }

    private readonly record struct FileIdentity(ulong DeviceOrVolume, ulong FileNumber)
    {
        public static bool TryRead(string path, out FileIdentity identity)
        {
            identity = default;
            try
            {
                using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return OperatingSystem.IsWindows()
                    ? TryReadWindows(handle, out identity)
                    : TryReadUnix(handle, out identity);
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (PlatformNotSupportedException)
            {
                return false;
            }
        }

        private static bool TryReadWindows(SafeFileHandle handle, out FileIdentity identity)
        {
            identity = default;
            if (!GetFileInformationByHandle(handle, out var information)) return false;
            identity = new FileIdentity(information.VolumeSerialNumber, ((ulong)information.FileIndexHigh << 32) | information.FileIndexLow);
            return true;
        }

        private static bool TryReadUnix(SafeFileHandle handle, out FileIdentity identity)
        {
            identity = default;
            var buffer = Marshal.AllocHGlobal(256);
            try
            {
                if (FStat(handle.DangerousGetHandle(), buffer) != 0) return false;
                identity = new FileIdentity(
                    unchecked((ulong)Marshal.ReadInt64(buffer, 0)),
                    unchecked((ulong)Marshal.ReadInt64(buffer, 8)));
                return true;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

        [DllImport("libc", EntryPoint = "fstat", SetLastError = true)]
        private static extern int FStat(IntPtr fileDescriptor, IntPtr buffer);

        [StructLayout(LayoutKind.Sequential)]
        private struct ByHandleFileInformation
        {
            public uint FileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
            public uint VolumeSerialNumber;
            public uint FileSizeHigh;
            public uint FileSizeLow;
            public uint NumberOfLinks;
            public uint FileIndexHigh;
            public uint FileIndexLow;
        }
    }

    private static bool HasReparsePoint(string path)
    {
        var full = Path.GetFullPath(path);
        try
        {
            if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0) return true;
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        var currentPath = full;
        while (!File.Exists(currentPath) && !Directory.Exists(currentPath))
        {
            var parentPath = Path.GetDirectoryName(currentPath);
            if (string.IsNullOrWhiteSpace(parentPath) || parentPath == currentPath) break;
            currentPath = parentPath;
        }

        var current = new DirectoryInfo(File.Exists(full) ? Path.GetDirectoryName(full)! : currentPath);
        var pathRoot = Path.GetPathRoot(full);
        if (File.Exists(full))
        {
            var file = new FileInfo(full);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.LinkTarget is not null) return true;
        }

        while (current is not null)
        {
            // macOS exposes /var through the standard /private/var system alias.
            // Caller-controlled root aliases and links below /var remain rejected.
            if (!IsSystemRootAlias(current, pathRoot) &&
                ((current.Attributes & FileAttributes.ReparsePoint) != 0 || current.LinkTarget is not null)) return true;
            current = current.Parent;
        }

        return false;
    }

    private static bool IsSystemRootAlias(DirectoryInfo directory, string? pathRoot)
        => IsSystemRootAlias(directory, pathRoot, OperatingSystem.IsMacOS());

    private static bool IsSystemRootAlias(DirectoryInfo directory, string? pathRoot, bool isMacOs)
    {
        if (!isMacOs || pathRoot is null || directory.Parent is null ||
            !string.Equals(directory.Parent.FullName, pathRoot, StringComparison.Ordinal) ||
            !string.Equals(directory.Name, "var", StringComparison.Ordinal)) return false;

        var target = directory.LinkTarget;
        if (string.IsNullOrWhiteSpace(target)) return false;
        var targetPath = Path.IsPathRooted(target) ? target : Path.Combine(directory.Parent.FullName, target);
        return string.Equals(Path.GetFullPath(targetPath), Path.Combine(pathRoot, "private", directory.Name), StringComparison.Ordinal);
    }
}

public enum CommandKind { Scan, Baseline, Check }
public enum OutputFormat { Text, Json, Sarif }
public enum ParseResultKind { Valid, Help, Version, Invalid }

public sealed record ParseResult(ParseResultKind Kind, Options? Options = null, string? Error = null);

public sealed record Options(
    CommandKind Command,
    string? InputPath,
    string? OutputPath,
    string? BaselinePath,
    string? ProjectPath,
    OutputFormat Format,
    bool StrictContent,
    string? CompilerApiVersion,
    bool TelemetryEnabled)
{
    public static readonly string HelpText = """
        PackageSurface tells you when a NuGet dependency starts participating in your build in a new way.

        Usage:
          package-surface scan <path> [options]
          package-surface baseline <path> --output <baseline> [options]
          package-surface check <path> --baseline <baseline> [options]
          package-surface --help
          package-surface --version

        Options:
          --format text|json|sarif  Report format (default: text).
                                   SARIF scan/baseline output includes one note per classified surface fact.
          --strict-content          Record SHA-256 fingerprints for present, active build/compiler execution assets.
          --compiler-api-version <v>  Declare the consuming compiler API version for versioned analyzer assets.
          --project <path>          Select one project when a solution contains several projects.
          --telemetry on|off        Enable or disable best-effort activation telemetry.
          --no-telemetry             Disable best-effort activation telemetry.

        Telemetry privacy:
          Activation fields: event, tool, tool_version, telemetry_version, schema_version,
          project_hash, installation_hash, runtime, os, ci, timestamp.
          Heartbeat fields: the same common fields plus runtime, os, ci, and week.
          PackageSurface requests activation only and adds no scanned package, asset, path,
          TFM, RID, baseline, or diagnostic data.
          See https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md and the
          shared policy at https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md.

        Exit codes:
          0  Scan/baseline succeeded, or check passed.
          1  Check found a reviewed surface or policy difference.
          2  Invalid invocation, missing restore artifacts, or incomplete analysis.

        Analysis is offline and non-executing. It consumes restore evidence already on disk,
        follows only bounded static package-import chains, and reports PS007 when applicability
        or restore consistency cannot be established. A single direct/project-reference rooted
        package closure is required for each applicable target graph; disconnected package nodes
        or dependency islands fail closed before package inventory inspection. Generated top-level
        and nested imports must resolve to packages in every applicable target graph. An incomplete invocation
        emits no surface entries and never replaces a baseline. Framework, target, dependency-
        group, target/RID, and library identities use one canonical key/effective-framework/
        target-alias relation with case/effective-moniker canonicalization before reachability
        and capability filtering; project/restore and dependency-group sets must be complete in
        both directions, dependency values must use supported restore grammar and match selected
        package versions, and incoherent metadata, duplicate aliases, duplicate or case-variant
        restore JSON properties, or malformed package ID/version keys are PS007. Target-package asset groups use NuGet's exact canonical
        property names; unknown or case-variant groups, malformed packageFolders entries, and
        baseline output aliases are checked before any write. An explicit baseline --output path
        is preflighted against every reachable package-inventory file resolved from packageFolders,
        including global and fallback roots, direct and transitive packages, every TFM/RID, nested
        static imports, and all inventory categories. Baseline inventory coverage includes every
        TFM/RID and all inventory categories. Lexical . and .. aliases, single and multiple
        hardlinks, and direct, interior, and ancestor reparse/symlink aliases are rejected with
        PS007 and controlled exit code 2 before any mutation. A rejection preserves existing output
        and input bytes; genuinely distinct outputs are accepted. The current
        candidate supports Windows, Linux, and macOS
        for SDK-style PackageReference restore outputs; hosted CI validates the command contract
        on all three platforms. On macOS, only the standard root-level /var to /private/var
        alias is ignored; caller-controlled root aliases and links nested below /var remain
        rejected. Baseline JSON uses exact camelCase property names, named string
        enums, and rejects duplicate or unknown members. It does not
        evaluate MSBuild conditions, execute package code, or crawl the global package cache.
        """;

    public static ParseResult Parse(string[] args)
    {
        if (args.Length == 0 || args.Any(argument => argument is "--help" or "-h"))
        {
            return new(ParseResultKind.Help);
        }

        if (args.Length == 1 && (args[0] is "--version" or "-v"))
        {
            return new(ParseResultKind.Version);
        }

        if (args[0] is "--version" or "-v")
        {
            return new(ParseResultKind.Invalid, Error: "--version cannot be combined with another argument.");
        }

        if (!Enum.TryParse<CommandKind>(args[0], ignoreCase: true, out var command) || !Enum.IsDefined(command))
        {
            return new(ParseResultKind.Invalid, Error: $"Unknown command '{args[0]}'.");
        }

        string? input = null;
        string? output = null;
        string? baseline = null;
        string? project = null;
        string? compilerApiVersion = null;
        var format = OutputFormat.Text;
        var strict = false;
        var telemetry = true;
        for (var index = 1; index < args.Length; index++)
        {
            var argument = args[index];
            if (argument == "--strict-content")
            {
                strict = true;
                continue;
            }

            if (argument == "--no-telemetry")
            {
                telemetry = false;
                continue;
            }

            if (IsOption(argument, "--format"))
            {
                var value = ReadOptionValue(args, ref index, argument, "--format");
                if (!Enum.TryParse<OutputFormat>(value, ignoreCase: true, out format) || !Enum.IsDefined(format))
                {
                    return new(ParseResultKind.Invalid, Error: "--format must be text, json, or sarif.");
                }

                continue;
            }

            if (IsOption(argument, "--output"))
            {
                output = ReadOptionValue(args, ref index, argument, "--output");
                continue;
            }

            if (IsOption(argument, "--baseline"))
            {
                baseline = ReadOptionValue(args, ref index, argument, "--baseline");
                continue;
            }

            if (IsOption(argument, "--project"))
            {
                project = ReadOptionValue(args, ref index, argument, "--project");
                continue;
            }

            if (IsOption(argument, "--compiler-api-version"))
            {
                compilerApiVersion = ReadOptionValue(args, ref index, argument, "--compiler-api-version");
                continue;
            }

            if (IsOption(argument, "--telemetry"))
            {
                var value = ReadOptionValue(args, ref index, argument, "--telemetry");
                if (value.Equals("off", StringComparison.OrdinalIgnoreCase)) telemetry = false;
                else if (value.Equals("on", StringComparison.OrdinalIgnoreCase)) telemetry = true;
                else return new(ParseResultKind.Invalid, Error: "--telemetry must be on or off.");
                continue;
            }

            if (argument.StartsWith('-'))
            {
                return new(ParseResultKind.Invalid, Error: $"Unknown option '{argument}'.");
            }

            if (input is not null)
            {
                return new(ParseResultKind.Invalid, Error: "Only one input path is allowed.");
            }

            input = argument;
        }

        if (input is null) return new(ParseResultKind.Invalid, Error: "An input path is required.");
        if (command == CommandKind.Baseline && string.IsNullOrWhiteSpace(output)) return new(ParseResultKind.Invalid, Error: "baseline requires --output <baseline>.");
        if (command != CommandKind.Baseline && output is not null) return new(ParseResultKind.Invalid, Error: "--output is only valid with baseline.");
        if (command == CommandKind.Check && string.IsNullOrWhiteSpace(baseline)) return new(ParseResultKind.Invalid, Error: "check requires --baseline <baseline>.");
        if (command != CommandKind.Check && baseline is not null) return new(ParseResultKind.Invalid, Error: "--baseline is only valid with check.");

        return new(ParseResultKind.Valid, new(command, input, output, baseline, project, format, strict, compilerApiVersion, telemetry));
    }

    private static string ReadOptionValue(string[] args, ref int index, string argument, string option)
    {
        var equals = argument.IndexOf('=');
        if (equals >= 0) return argument[(equals + 1)..];
        if (++index >= args.Length || args[index].StartsWith('-')) throw new ArgumentException($"{option} requires a value.");
        return args[index];
    }

    private static bool IsOption(string argument, string option) =>
        argument.Equals(option, StringComparison.Ordinal) || argument.StartsWith(option + "=", StringComparison.Ordinal);
}

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
    {
        try
        {
            if (!File.Exists(assetsPath) || new FileInfo(assetsPath).Length > MaxInputBytes) return null;
            using var stream = File.OpenRead(assetsPath);
            using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { MaxDepth = 16 });
            return document.RootElement.GetProperty("project").GetProperty("restore").GetProperty("projectPath").GetString();
        }
        catch (Exception ex) when (ex is IOException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

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

    private static bool PathsEqual(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static void EnsureMembershipLimit(int count)
    {
        if (count > MaxProjectMemberships) throw new InvalidDataException("The input contains too many project members to validate safely.");
    }

    private static void EnsureProjectLimit(int count)
    {
        if (count > MaxProjects) throw new InvalidDataException("The input contains more than 128 supported projects; narrow the input with --project.");
    }
}

public sealed record SurfaceSnapshot(
    IReadOnlyList<SurfaceEntry> Entries,
    IReadOnlyList<string> IncompleteReasons,
    bool StrictContent,
    int ResolvedPackageCount)
{
    public static SurfaceSnapshot Create(IEnumerable<SurfaceEntry> entries, IEnumerable<string> reasons, bool strictContent, int resolvedPackageCount) =>
        new(entries.OrderBy(entry => entry.Project, StringComparer.Ordinal)
            .ThenBy(entry => entry.Context)
            .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
            .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
            .ThenBy(entry => entry.PackageId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(entry => entry.Version, StringComparer.Ordinal)
            .ThenBy(entry => entry.Relationship, StringComparer.Ordinal)
            .ThenBy(entry => entry.Capability)
            .ThenBy(entry => entry.PackageRelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray(), reasons.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(), strictContent, resolvedPackageCount);
}

public sealed record BaselineDocument(
    int SchemaVersion,
    string ToolVersion,
    bool StrictContent,
    IReadOnlyList<BaselineEntry> Entries,
    IReadOnlyList<string> IncompleteReasons)
{
    private const long MaxBaselineBytes = 16 * 1024 * 1024;
    private const int MaxBaselineEntries = 50_000;
    private const int MaxBaselineIncompleteReasons = 1_024;
    private const int MaxBaselineTextLength = 4_096;
    private static readonly string[] RootRequiredProperties = { "schemaVersion", "toolVersion", "strictContent", "entries", "incompleteReasons" };
    private static readonly string[] EntryRequiredProperties = { "project", "context", "packageId", "version", "relationship", "capability", "packageRelativePath", "present", "active", "incomplete" };
    private static readonly string[] EntryOptionalProperties = { "targetFramework", "runtimeIdentifier", "sha256", "incompleteReason", "observedPrimitives" };
    private static readonly string[] EntryStringProperties = { "project", "context", "packageId", "version", "relationship", "capability", "packageRelativePath" };
    private static readonly string[] EntryBooleanProperties = { "present", "active", "incomplete" };
    private static readonly string[] EntryNullableTextProperties = { "targetFramework", "runtimeIdentifier", "sha256", "incompleteReason" };

    public static BaselineDocument Read(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            if (!File.Exists(fullPath)) throw new InvalidDataException("Baseline file does not exist.");
            if (new FileInfo(fullPath).Length > MaxBaselineBytes) throw new InvalidDataException("Baseline exceeds the supported size limit.");
            var json = File.ReadAllText(fullPath, Encoding.UTF8);
            using var parsed = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 64 });
            ValidateJsonShape(parsed.RootElement);
            var document = parsed.RootElement.Deserialize<BaselineDocument>(JsonOptions.Default) ?? throw new InvalidDataException("Baseline is empty.");
            Validate(document);
            return document;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            throw new InvalidDataException("Baseline is not a readable valid JSON document.");
        }
    }

    public static void Write(string path, SurfaceSnapshot snapshot)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var baseline = new BaselineDocument(CommandLineSchema.Version, "0.1.0", snapshot.StrictContent, snapshot.Entries.Select(BaselineEntry.From).ToArray(), Array.Empty<string>());
        Validate(baseline);
        EnsureEstimatedSizeWithinLimit(baseline);
        var temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var serialized = JsonSerializer.Serialize(baseline, JsonOptions.Indented).Replace("\r\n", "\n", StringComparison.Ordinal);
            var content = serialized + "\n";
            if (Encoding.UTF8.GetByteCount(content) > MaxBaselineBytes)
            {
                throw new InvalidDataException("The generated baseline exceeds the supported size limit.");
            }

            using (var parsed = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 }))
            {
                ValidateJsonShape(parsed.RootElement);
            }
            File.WriteAllText(temporary, content, new UTF8Encoding(false));
            _ = Read(temporary);
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    private static void EnsureEstimatedSizeWithinLimit(BaselineDocument baseline)
    {
        long estimated = 512;
        foreach (var entry in baseline.Entries)
        {
            estimated += 512L + (entry.Project?.Length ?? 0) + entry.PackageId.Length + entry.Version.Length + entry.PackageRelativePath.Length + (entry.Sha256?.Length ?? 0);
            if (estimated > MaxBaselineBytes) throw new InvalidDataException("The generated baseline exceeds the supported size limit.");
        }
    }

    private static void Validate(BaselineDocument document)
    {
        if (document.SchemaVersion != CommandLineSchema.Version) throw new InvalidDataException("Unsupported baseline schema version.");
        RequireText(document.ToolVersion, "toolVersion");
        if (document.Entries is null) throw new InvalidDataException("Baseline has no entries array.");
        if (document.Entries.Count > MaxBaselineEntries) throw new InvalidDataException("Baseline contains too many entries.");
        if (document.IncompleteReasons is null || document.IncompleteReasons.Count > MaxBaselineIncompleteReasons || document.IncompleteReasons.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("Baseline has invalid incomplete-state markers.");
        }

        foreach (var reason in document.IncompleteReasons) RequireText(reason, "incompleteReason");
        if (document.IncompleteReasons.Count > 0) throw new InvalidDataException("An incomplete analysis cannot be used as an approved baseline.");
        var identities = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance);
        foreach (var entry in document.Entries)
        {
            if (entry is null) throw new InvalidDataException("Baseline contains a null entry.");
            RequireText(entry.PackageId, "packageId");
            RequireText(entry.Version, "version");
            RequireText(entry.Relationship, "relationship");
            RequireText(entry.PackageRelativePath, "packageRelativePath");
            if (entry.PackageId.Contains('/', StringComparison.Ordinal) || entry.PackageId.Contains('\\', StringComparison.Ordinal) || entry.Version.Contains('/', StringComparison.Ordinal) || entry.Version.Contains('\\', StringComparison.Ordinal)) throw new InvalidDataException("Baseline contains an invalid package identity.");
            if (!entry.Relationship.Equals("direct", StringComparison.OrdinalIgnoreCase) && !entry.Relationship.Equals("transitive", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Baseline contains an invalid relationship.");
            if (!Enum.IsDefined(entry.Context) || !Enum.IsDefined(entry.Capability)) throw new InvalidDataException("Baseline contains an unsupported enum value.");
            if (entry.Project is not null) ValidateProjectIdentity(entry.Project);
            if (entry.Project is null) throw new InvalidDataException("Baseline entries require a project name.");
            if (entry.TargetFramework is not null) RequireText(entry.TargetFramework, "targetFramework");
            if (entry.RuntimeIdentifier is not null) RequireText(entry.RuntimeIdentifier, "runtimeIdentifier");
            if (entry.Context == SurfaceContextKind.Target && entry.TargetFramework is null) throw new InvalidDataException("Target baseline entries require a target framework.");
            if (entry.Context == SurfaceContextKind.Project && entry.TargetFramework is not null) throw new InvalidDataException("Project baseline entries cannot specify a target framework.");
            if (entry.Context == SurfaceContextKind.Project && entry.RuntimeIdentifier is not null) throw new InvalidDataException("Project baseline entries cannot specify a runtime identifier.");
            ValidateRelativeText(entry.PackageRelativePath, "packageRelativePath");
            if (entry.Incomplete || entry.IncompleteReason is not null) throw new InvalidDataException("Baseline contains an incomplete entry.");
            if (entry.Active && !entry.Present) throw new InvalidDataException("Baseline contains an active asset that is not present.");
            if (document.StrictContent && CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active) && !IsSha256(entry.Sha256)) throw new InvalidDataException("Strict baseline execution-surface entries require SHA-256 fingerprints.");
            if (document.StrictContent && !CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active) && entry.Sha256 is not null) throw new InvalidDataException("Strict baselines cannot fingerprint ineligible capability entries.");
            if (entry.Sha256 is not null && !IsSha256(entry.Sha256)) throw new InvalidDataException("Baseline contains an invalid SHA-256 fingerprint.");
            if (entry.ObservedPrimitives is not null && (entry.ObservedPrimitives.Count > 16 || entry.ObservedPrimitives.Any(primitive => primitive is not ("Exec" or "Import" or "InlineTaskFactory" or "UsingTask")))) throw new InvalidDataException("Baseline contains invalid observed XML primitives.");
            if (entry.ObservedPrimitives is not null && entry.Capability is not (CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting)) throw new InvalidDataException("Observed XML primitives are only valid for build capability entries.");
            if (!identities.Add(SurfaceIdentityKey.Create(entry))) throw new InvalidDataException("Baseline contains duplicate capability-surface identities.");
        }
    }

    private static void ValidateJsonShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Baseline root must be an object.");
        ValidateObjectProperties(root, RootRequiredProperties, Array.Empty<string>(), "baseline");

        if (root.GetProperty("schemaVersion").ValueKind != JsonValueKind.Number || !root.GetProperty("schemaVersion").TryGetInt32(out _)) throw new InvalidDataException("Baseline schemaVersion must be an integer.");
        if (root.GetProperty("toolVersion").ValueKind != JsonValueKind.String) throw new InvalidDataException("Baseline toolVersion must be a string.");
        if (root.GetProperty("strictContent").ValueKind != JsonValueKind.True && root.GetProperty("strictContent").ValueKind != JsonValueKind.False) throw new InvalidDataException("Baseline strictContent must be a boolean.");
        if (root.GetProperty("incompleteReasons").ValueKind != JsonValueKind.Array) throw new InvalidDataException("Baseline incompleteReasons must be an array.");
        foreach (var reason in root.GetProperty("incompleteReasons").EnumerateArray())
        {
            if (reason.ValueKind != JsonValueKind.String) throw new InvalidDataException("Baseline incompleteReasons must contain only strings.");
        }

        var entries = root.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Baseline entries must be an array.");
        if (entries.GetArrayLength() > MaxBaselineEntries) throw new InvalidDataException("Baseline contains too many entries.");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Baseline contains a non-object entry.");
            ValidateObjectProperties(entry, EntryRequiredProperties, EntryOptionalProperties, "baseline entry");
            foreach (var property in EntryStringProperties)
            {
                if (entry.GetProperty(property).ValueKind != JsonValueKind.String) throw new InvalidDataException($"Baseline entry field '{property}' must be a string.");
            }
            if (!IsCanonicalEnumName(entry.GetProperty("context"), typeof(SurfaceContextKind)) ||
                !IsCanonicalEnumName(entry.GetProperty("capability"), typeof(CapabilityKind)))
            {
                throw new InvalidDataException("Baseline entry enum values must use canonical named strings.");
            }
            foreach (var property in EntryBooleanProperties)
            {
                if (entry.GetProperty(property).ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException($"Baseline entry field '{property}' must be a boolean.");
            }

            foreach (var property in EntryNullableTextProperties)
            {
                if (entry.TryGetProperty(property, out var value) && value.ValueKind is not (JsonValueKind.String or JsonValueKind.Null)) throw new InvalidDataException($"Baseline entry field '{property}' must be a string or null.");
            }

            if (entry.TryGetProperty("observedPrimitives", out var primitives))
            {
                if (primitives.ValueKind is not (JsonValueKind.Array or JsonValueKind.Null)) throw new InvalidDataException("Baseline entry observedPrimitives must be an array or null.");
                if (primitives.ValueKind == JsonValueKind.Array && primitives.EnumerateArray().Any(value => value.ValueKind != JsonValueKind.String)) throw new InvalidDataException("Baseline entry observedPrimitives must contain only strings.");
            }
        }
    }

    private static bool IsCanonicalEnumName(JsonElement value, Type enumType) =>
        value.ValueKind == JsonValueKind.String && Enum.GetNames(enumType).Contains(value.GetString(), StringComparer.Ordinal);

    private static void ValidateObjectProperties(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional, string description)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new InvalidDataException($"{description} contains duplicate property '{property.Name}'.");
            var canonical = required.Concat(optional).FirstOrDefault(candidate => candidate.Equals(property.Name, StringComparison.OrdinalIgnoreCase));
            if (canonical is null) throw new InvalidDataException($"{description} contains unknown property '{property.Name}'.");
            if (!canonical.Equals(property.Name, StringComparison.Ordinal)) throw new InvalidDataException($"{description} contains a non-canonical property '{property.Name}'.");
        }

        foreach (var property in required)
        {
            if (!seen.Contains(property)) throw new InvalidDataException($"{description} is missing required field '{property}'.");
        }
    }

    private static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxBaselineTextLength) throw new InvalidDataException($"Baseline field '{field}' is invalid.");
    }

    private static void ValidateRelativeText(string value, string field)
    {
        RequireText(value, field);
        var normalized = value.Replace('\\', '/');
        if (Path.IsPathRooted(value) || normalized.Length > 0 && normalized[0] == '/' || (normalized.Length >= 2 && normalized[1] == ':') || normalized.Split('/').Any(part => part is ".." or "")) throw new InvalidDataException($"Baseline field '{field}' is not a safe relative value.");
    }

    private static void ValidateProjectIdentity(string value)
    {
        RequireText(value, "project");
        var normalized = value.Replace('\\', '/');
        if (Path.IsPathRooted(value) || normalized.StartsWith('/') ||
            normalized.Length >= 2 && normalized[1] == ':' || normalized.Contains("//", StringComparison.Ordinal) ||
            normalized.Split('/').Any(part => part.Length == 0 || part == "."))
        {
            throw new InvalidDataException("Baseline project identity is not a supported solution-relative value.");
        }
    }

    private static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);
}

public static class CommandLineSchema { public const int Version = 1; }

public sealed record BaselineEntry(
    string? Project,
    SurfaceContextKind Context,
    string? TargetFramework,
    string? RuntimeIdentifier,
    string PackageId,
    string Version,
    string Relationship,
    CapabilityKind Capability,
    string PackageRelativePath,
    bool Present,
    bool Active,
    string? Sha256,
    bool Incomplete,
    string? IncompleteReason,
    IReadOnlyList<string>? ObservedPrimitives = null)
{
    public static BaselineEntry From(SurfaceEntry entry) => new(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath, entry.Present, entry.Active, CapabilityPolicy.IsStrictContentEligible(entry) ? entry.Sha256 : null, entry.Incomplete, entry.IncompleteReason, entry.ObservedPrimitives);

    [JsonIgnore]
    public SurfaceIdentity SurfaceIdentity => SurfaceIdentityKey.Create(this);
}

public static class SurfaceIdentityKey
{
    public static SurfaceIdentity Create(BaselineEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath);
    public static SurfaceIdentity Create(SurfaceEntry entry) => SurfaceIdentity.From(entry);
    public static SurfaceIdentity CreateCategory(BaselineEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, string.Empty, entry.Capability, null);
    public static SurfaceIdentity CreateCategory(SurfaceEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, string.Empty, entry.Capability, null);
}

public sealed record Diagnostic(
    string Id,
    string Name,
    string Message,
    string Severity,
    string? Project,
    string? PackageId,
    string? PackageRelativePath,
    string? Version = null,
    string? Relationship = null,
    SurfaceContextKind? Context = null,
    string? TargetFramework = null,
    string? RuntimeIdentifier = null,
    CapabilityKind? Capability = null)
{
    public static Diagnostic Create(string id, string message, string? project = null, string? packageId = null, string? path = null) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, message, id == "PS007" ? "error" : "warning", project, packageId, path);

    public static Diagnostic ForEntry(string id, string message, SurfaceEntry entry) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, message, id == "PS007" ? "error" : "warning", entry.Project, entry.PackageId, entry.PackageRelativePath, entry.Version, entry.Relationship, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.Capability);

    public static Diagnostic ForBaseline(string id, string message, BaselineEntry entry) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, message, id == "PS007" ? "error" : "warning", entry.Project, entry.PackageId, entry.PackageRelativePath, entry.Version, entry.Relationship, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.Capability);
}

public static class DiffEngine
{
    public static IReadOnlyList<Diagnostic> Compare(IReadOnlyList<BaselineEntry> baseline, IReadOnlyList<SurfaceEntry> current, bool strictContent)
    {
        var approved = baseline.Where(entry => entry.Active).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
        var observed = current.Where(entry => entry.Active).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
        var diagnostics = new List<Diagnostic>();
        foreach (var added in observed.Where(pair => !approved.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            var id = SpecificId(added.Capability);
            if (!baseline.Any(entry => entry.Active && SurfaceIdentityComparer.Instance.Equals(SurfaceIdentityKey.CreateCategory(entry), SurfaceIdentityKey.CreateCategory(added)))) diagnostics.Add(Diagnostic.ForEntry("PS001", FormatEntryMessage("New capability category", added), added));
            diagnostics.Add(Diagnostic.ForEntry("PS002", FormatEntryMessage("New active capability asset", added), added));
            diagnostics.Add(Diagnostic.ForEntry(id, FormatEntryMessage("Capability surface changed", added), added));
        }

        foreach (var removed in approved.Where(pair => !observed.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            diagnostics.Add(Diagnostic.ForBaseline(SpecificId(removed.Capability), FormatEntryMessage("Approved active capability is no longer active", removed), removed));
        }

        if (strictContent)
        {
            var approvedContent = baseline.Where(entry => CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active)).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
            var observedContent = current.Where(CapabilityPolicy.IsStrictContentEligible).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
            foreach (var pair in observedContent)
            {
                if (!approvedContent.TryGetValue(pair.Key, out var prior) || string.Equals(prior.Sha256, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                diagnostics.Add(Diagnostic.ForEntry("PS005", FormatEntryMessage("Approved asset content changed", pair.Value), pair.Value));
            }
        }

        return diagnostics;
    }

    private static string FormatEntryMessage(string prefix, BaselineEntry entry) => $"{prefix}: package {entry.PackageId} version {entry.Version} ({entry.Relationship}), {entry.Capability} {entry.Context} {entry.TargetFramework ?? "project"}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)} at {entry.PackageRelativePath}.";
    private static string FormatEntryMessage(string prefix, SurfaceEntry entry) => $"{prefix}: package {entry.PackageId} version {entry.Version} ({entry.Relationship}), {entry.Capability} {entry.Context} {entry.TargetFramework ?? "project"}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)} at {entry.PackageRelativePath}.";
    private static string SpecificId(CapabilityKind kind) => kind switch { CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting => "PS003", CapabilityKind.CompilerExtension or CapabilityKind.CompileSourceInjection => "PS004", CapabilityKind.NativeRuntime => "PS006", _ => "PS002" };
}

public sealed record ReportDocument(
    int SchemaVersion,
    string Operation,
    bool StrictContent,
    IReadOnlyList<SurfaceEntry> Entries,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> IncompleteReasons)
{
    public static ReportDocument Create(CommandKind command, SurfaceSnapshot snapshot, IReadOnlyList<Diagnostic> diagnostics) => new(CommandLineSchema.Version, command.ToString().ToLowerInvariant(), snapshot.StrictContent, snapshot.Entries, diagnostics, snapshot.IncompleteReasons);

    public void EnsureOutputWithinLimit(long limit)
    {
        long estimated = 512;
        foreach (var entry in Entries)
        {
            estimated += 768L + (entry.Project?.Length ?? 0) + entry.PackageId.Length + entry.Version.Length + entry.PackageRelativePath.Length;
            if (estimated > limit) throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        foreach (var diagnostic in Diagnostics)
        {
            estimated += 512L + diagnostic.Message.Length + (diagnostic.Project?.Length ?? 0) + (diagnostic.PackageId?.Length ?? 0) + (diagnostic.PackageRelativePath?.Length ?? 0);
            if (estimated > limit) throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        estimated += IncompleteReasons.Sum(reason => 128L + reason.Length);
        if (estimated > limit) throw new InvalidDataException("The report exceeds the supported output size limit.");
    }

    public string ToText()
    {
        var builder = new StringBuilder(string.Format(CultureInfo.InvariantCulture, "PackageSurface {0} (schema {1})", Operation, SchemaVersion));
        builder.AppendLine();
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Entries: {0}; strict content: {1}", Entries.Count, StrictContent.ToString().ToLowerInvariant()));
        foreach (var entry in Entries)
        {
            var target = entry.Context == SurfaceContextKind.Project ? "project" : $"{entry.TargetFramework}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)}";
            var primitives = entry.ObservedPrimitives is { Count: > 0 } ? $" primitives={string.Join(",", entry.ObservedPrimitives)}" : string.Empty;
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} present={1} active={2} project={3} context={4} package={5}@{6} relationship={7} target={8} path={9}{10}", entry.Capability, entry.Present, entry.Active, entry.Project ?? "-", entry.Context, entry.PackageId, entry.Version, entry.Relationship, target, entry.PackageRelativePath, primitives));
        }
        foreach (var diagnostic in Diagnostics)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2}", diagnostic.Id, diagnostic.Name, diagnostic.Message));
        }
        return builder.ToString().TrimEnd();
    }

    public SarifDocument ToSarif() => new("2.1.0", "https://json.schemastore.org/sarif-2.1.0.json", new[] { new SarifRun(new SarifTool(new SarifDriver("PackageSurface", CommandLine.ToolVersion)), Diagnostics.Select(SarifResult.Create).Concat(Entries.Select(SarifResult.Create)).ToArray()) });
}

public sealed class SarifDocument
{
    public SarifDocument(string version, string schema, IReadOnlyList<SarifRun> runs)
    {
        Version = version;
        Schema = schema;
        Runs = runs;
    }

    [JsonPropertyName("version")]
    public string Version { get; }

    [JsonPropertyName("$schema")]
    public string Schema { get; }

    [JsonPropertyName("runs")]
    public IReadOnlyList<SarifRun> Runs { get; }
}
public sealed record SarifRun(SarifTool Tool, IReadOnlyList<SarifResult> Results);
public sealed record SarifTool(SarifDriver Driver);
public sealed record SarifDriver(string Name, string Version);
public sealed record SarifResult(string RuleId, SarifMessage Message, string Level, IReadOnlyDictionary<string, string>? Properties = null)
{
    public static SarifResult Create(Diagnostic diagnostic) => new(
        diagnostic.Id,
        new SarifMessage(diagnostic.Message),
        diagnostic.Severity,
        ContextProperties(diagnostic.Project, diagnostic.PackageId, diagnostic.PackageRelativePath, diagnostic.Version, diagnostic.Relationship, diagnostic.Context, diagnostic.TargetFramework, diagnostic.RuntimeIdentifier, diagnostic.Capability, null));

    public static SarifResult Create(SurfaceEntry entry) => new(
        "PS-SURFACE",
        new SarifMessage($"{entry.Capability} present={entry.Present} active={entry.Active} project={entry.Project ?? "-"} context={entry.Context} target={entry.TargetFramework ?? "project"}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)} package={entry.PackageId}@{entry.Version} relationship={entry.Relationship} path={entry.PackageRelativePath}"),
        "note",
        ContextProperties(entry.Project, entry.PackageId, entry.PackageRelativePath, entry.Version, entry.Relationship, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.Capability, entry.ObservedPrimitives));

    private static Dictionary<string, string> ContextProperties(
        string? project,
        string? package,
        string? path,
        string? version,
        string? relationship,
        SurfaceContextKind? context,
        string? targetFramework,
        string? runtimeIdentifier,
        CapabilityKind? capability,
        IReadOnlyList<string>? primitives)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["project"] = project ?? "",
            ["package"] = package ?? "",
            ["path"] = path ?? "",
            ["version"] = version ?? "",
            ["relationship"] = relationship ?? "",
            ["context"] = context?.ToString() ?? "",
            ["targetFramework"] = targetFramework ?? "",
            ["runtimeIdentifier"] = runtimeIdentifier ?? "",
            ["capability"] = capability?.ToString() ?? ""
        };
        if (primitives is { Count: > 0 }) properties["observedPrimitives"] = string.Join(",", primitives);
        return properties;
    }
}
public sealed record SarifMessage(string Text);

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new() { PropertyNameCaseInsensitive = false, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false) } };
    public static readonly JsonSerializerOptions Indented = new(Default) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
