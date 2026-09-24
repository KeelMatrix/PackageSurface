using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeelMatrix.PackageSurface.Probe;
using KeelMatrix.Telemetry;

namespace KeelMatrix.PackageSurface;

public static class CommandLine
{
    private const int SchemaVersion = 1;
    public const string ToolVersion = "0.1.0";

    public static int Run(string[] args)
    {
        ParseResult parsed;
        try
        {
            parsed = Options.Parse(args);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine("error: invalid command-line arguments.");
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
            BaselineDocument? baseline = null;
            var strictContent = options.StrictContent;
            if (options.Command == CommandKind.Check)
            {
                baseline = BaselineDocument.Read(options.BaselinePath!);
                strictContent |= baseline.StrictContent;
            }

            var selection = ProjectSelection.Resolve(options.InputPath!, options.ProjectPath);
            var current = AnalyzeSelection(selection, strictContent);
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

            var report = ReportDocument.Create(options.Command, current, diagnostics);
            WriteOutput(report, options.Format);

            var incomplete = diagnostics.Any(diagnostic => diagnostic.Id == "PS007");
            if (options.Command == CommandKind.Baseline && !incomplete)
            {
                BaselineDocument.Write(options.OutputPath!, current);
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
            Console.Error.WriteLine("error: analysis could not be completed because an input or restore artifact was invalid, missing, or unreadable.");
            return 2;
        }
    }

    private static SurfaceSnapshot AnalyzeSelection(ProjectSelection selection, bool strictContent)
    {
        var entries = new List<SurfaceEntry>();
        var reasons = new List<string>();
        var resolvedPackages = 0;
        foreach (var project in selection.Projects)
        {
            if (!File.Exists(project.AssetsPath))
            {
                reasons.Add($"{project.DisplayPath}: project.assets.json is missing; run restore before analysis.");
                continue;
            }

            var result = ResolvedGraphClassifier.Analyze(project.AssetsPath, project.ProjectRoot, strictContent, project.DisplayPath);
            entries.AddRange(result.Entries);
            reasons.AddRange(result.IncompleteReasons.Select(reason => $"{project.DisplayPath}: {reason}"));
            resolvedPackages += result.ResolvedPackageCount;
        }

        if (selection.Projects.Count == 0)
        {
            reasons.Add("No supported SDK-style project with an existing project.assets.json was found.");
        }

        return SurfaceSnapshot.Create(entries, reasons, strictContent, resolvedPackages);
    }

    private static Action? TelemetryHook { get; set; }

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
        switch (format)
        {
            case OutputFormat.Text:
                Console.WriteLine(report.ToText());
                break;
            case OutputFormat.Json:
                Console.WriteLine(JsonSerializer.Serialize(report, JsonOptions.Indented));
                break;
            case OutputFormat.Sarif:
                Console.WriteLine(JsonSerializer.Serialize(report.ToSarif(), JsonOptions.Indented));
                break;
            default:
                throw new InvalidDataException("Unsupported output format.");
        }
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
          --strict-content          Record SHA-256 fingerprints for present, active build/compiler execution assets.
          --project <path>          Select one project when a solution contains several projects.
          --telemetry on|off        Enable or disable best-effort activation telemetry.
          --no-telemetry             Disable best-effort activation telemetry.

        Telemetry privacy:
          PackageSurface passes no analyzed dependency identity or content to telemetry.
          See https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md and the
          shared policy at https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md.

        Exit codes:
          0  Scan/baseline succeeded, or check passed.
          1  Check found a reviewed surface or policy difference.
          2  Invalid invocation, missing restore artifacts, or incomplete analysis.
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

        return new(ParseResultKind.Valid, new(command, input, output, baseline, project, format, strict, telemetry));
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

public sealed record SelectedProject(string ProjectRoot, string AssetsPath, string DisplayPath);

public sealed record ProjectSelection(IReadOnlyList<SelectedProject> Projects)
{
    private const int MaxProjects = 128;

    public static ProjectSelection Resolve(string inputPath, string? projectPath)
    {
        var input = Path.GetFullPath(inputPath);
        if (projectPath is not null)
        {
            var selected = ResolveProject(Path.GetFullPath(projectPath), Path.GetDirectoryName(input)!);
            return new(new[] { selected });
        }

        if (File.Exists(input) && IsSupportedProjectPath(input)) return new(new[] { ResolveProject(input, Path.GetDirectoryName(input)!) });
        if (File.Exists(input) && input.EndsWith(".assets.json", StringComparison.OrdinalIgnoreCase)) return new(new[] { ResolveAssets(input) });
        if (File.Exists(input) && input.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(input)!;
            var projects = new List<SelectedProject>();
            foreach (var line in File.ReadLines(input))
            {
                var comma = line.IndexOf(", \"", StringComparison.Ordinal);
                if (comma < 0 || !line.StartsWith("Project(", StringComparison.Ordinal)) continue;
                var start = line.IndexOf('"', comma);
                var end = line.IndexOf('"', start + 1);
                if (start < 0 || end < 0) continue;
                var relativePath = line[(start + 1)..end];
                if (!Path.HasExtension(relativePath)) continue;
                if (!IsSupportedProjectPath(relativePath))
                {
                    throw new InvalidDataException("The solution contains an unsupported project kind.");
                }

                var path = Path.GetFullPath(Path.Combine(root, relativePath.Replace('\\', Path.DirectorySeparatorChar)));
                projects.Add(ResolveProject(path, root));
            }
            EnsureProjectLimit(projects.Count);
            return new(projects);
        }

        if (Directory.Exists(input))
        {
            var direct = Path.Combine(input, "obj", "project.assets.json");
            if (File.Exists(direct)) return new(new[] { ResolveAssets(direct) });
            var projects = Directory.EnumerateFiles(input, "*proj", SearchOption.TopDirectoryOnly)
                .Where(IsSupportedProjectPath)
                .Select(path => ResolveProject(path, input))
                .ToArray();
            var unsupported = Directory.EnumerateFiles(input, "*proj", SearchOption.TopDirectoryOnly)
                .Where(path => !IsSupportedProjectPath(path))
                .ToArray();
            if (unsupported.Length > 0) throw new InvalidDataException("The directory contains an unsupported project kind.");
            EnsureProjectLimit(projects.Length);
            return new(projects);
        }

        throw new InvalidDataException("The input path does not exist or is not a supported SDK-style project input.");
    }

    private static SelectedProject ResolveProject(string projectPath, string displayRoot)
    {
        if (!IsSupportedProjectPath(projectPath)) throw new InvalidDataException("The selected project kind is unsupported.");
        if (!File.Exists(projectPath)) throw new InvalidDataException("The selected project does not exist.");
        var root = Path.GetDirectoryName(projectPath)!;
        var display = NormalizeDisplay(Path.GetRelativePath(displayRoot, projectPath));
        return new(root, Path.Combine(root, "obj", "project.assets.json"), display);
    }

    private static SelectedProject ResolveAssets(string assetsPath)
    {
        var obj = Path.GetDirectoryName(assetsPath)!;
        var root = Directory.GetParent(obj)?.FullName ?? obj;
        return new(root, assetsPath, NormalizeDisplay(Path.GetFileName(root) + ".csproj"));
    }

    private static string NormalizeDisplay(string value) => value.Replace('\\', '/');

    private static bool IsSupportedProjectPath(string path) =>
        path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase) ||
        path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase);

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
        var temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            var serialized = JsonSerializer.Serialize(baseline, JsonOptions.Indented).Replace("\r\n", "\n", StringComparison.Ordinal);
            File.WriteAllText(temporary, serialized + "\n", new UTF8Encoding(false));
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
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

        if (document.IncompleteReasons.Count > 0) throw new InvalidDataException("An incomplete analysis cannot be used as an approved baseline.");
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
            if (entry.Project is not null) ValidateRelativeText(entry.Project, "project");
            if (entry.Project is null) throw new InvalidDataException("Baseline entries require a project name.");
            if (entry.TargetFramework is not null) RequireText(entry.TargetFramework, "targetFramework");
            if (entry.RuntimeIdentifier is not null) RequireText(entry.RuntimeIdentifier, "runtimeIdentifier");
            if (entry.Context == SurfaceContextKind.Target && entry.TargetFramework is null) throw new InvalidDataException("Target baseline entries require a target framework.");
            if (entry.Context == SurfaceContextKind.Project && entry.TargetFramework is not null) throw new InvalidDataException("Project baseline entries cannot specify a target framework.");
            ValidateRelativeText(entry.PackageRelativePath, "packageRelativePath");
            if (entry.Incomplete || entry.IncompleteReason is not null) throw new InvalidDataException("Baseline contains an incomplete entry.");
            if (entry.Active && !entry.Present) throw new InvalidDataException("Baseline contains an active asset that is not present.");
            if (document.StrictContent && CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active) && !IsSha256(entry.Sha256)) throw new InvalidDataException("Strict baseline execution-surface entries require SHA-256 fingerprints.");
            if (document.StrictContent && !CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active) && entry.Sha256 is not null) throw new InvalidDataException("Strict baselines cannot fingerprint ineligible capability entries.");
            if (entry.Sha256 is not null && !IsSha256(entry.Sha256)) throw new InvalidDataException("Baseline contains an invalid SHA-256 fingerprint.");
            if (entry.ObservedPrimitives is not null && (entry.ObservedPrimitives.Count > 16 || entry.ObservedPrimitives.Any(primitive => primitive is not ("Exec" or "Import" or "InlineTaskFactory" or "UsingTask")))) throw new InvalidDataException("Baseline contains invalid observed XML primitives.");
        }
    }

    private static void ValidateJsonShape(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Baseline root must be an object.");
        foreach (var property in new[] { "schemaVersion", "toolVersion", "strictContent", "entries", "incompleteReasons" })
        {
            if (!root.TryGetProperty(property, out _)) throw new InvalidDataException($"Baseline is missing required field '{property}'.");
        }

        var entries = root.GetProperty("entries");
        if (entries.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Baseline entries must be an array.");
        if (entries.GetArrayLength() > MaxBaselineEntries) throw new InvalidDataException("Baseline contains too many entries.");
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Baseline contains a non-object entry.");
            foreach (var property in new[] { "context", "packageId", "version", "relationship", "capability", "packageRelativePath", "present", "active", "incomplete" })
            {
                if (!entry.TryGetProperty(property, out _)) throw new InvalidDataException($"Baseline entry is missing required field '{property}'.");
            }
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
}

public sealed record Diagnostic(
    string Id,
    string Name,
    string Message,
    string Severity,
    string? Project,
    string? PackageId,
    string? PackageRelativePath)
{
    public static Diagnostic Create(string id, string message, string? project = null, string? packageId = null, string? path = null) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, message, id == "PS007" ? "error" : "warning", project, packageId, path);
}

public static class DiffEngine
{
    public static IReadOnlyList<Diagnostic> Compare(IReadOnlyList<BaselineEntry> baseline, IReadOnlyList<SurfaceEntry> current, bool strictContent)
    {
        var approved = baseline.Where(entry => entry.Active).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var observed = current.Where(entry => entry.Active).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<Diagnostic>();
        foreach (var added in observed.Where(pair => !approved.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            var id = SpecificId(added.Capability);
            if (!baseline.Any(entry => entry.Active && entry.Capability == added.Capability)) diagnostics.Add(Diagnostic.Create("PS001", $"New capability category {added.Capability} is active.", added.Project, added.PackageId, added.PackageRelativePath));
            diagnostics.Add(Diagnostic.Create("PS002", $"New active capability asset: {added.Capability} at {added.PackageRelativePath}.", added.Project, added.PackageId, added.PackageRelativePath));
            diagnostics.Add(Diagnostic.Create(id, $"{added.Capability} surface changed at {added.PackageRelativePath}.", added.Project, added.PackageId, added.PackageRelativePath));
        }

        foreach (var removed in approved.Where(pair => !observed.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            diagnostics.Add(Diagnostic.Create(SpecificId(removed.Capability), $"Approved active capability is no longer active: {removed.PackageRelativePath}.", removed.Project, removed.PackageId, removed.PackageRelativePath));
        }

        if (strictContent)
        {
            var approvedContent = baseline.Where(entry => CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active)).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
            var observedContent = current.Where(CapabilityPolicy.IsStrictContentEligible).ToDictionary(Key, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in observedContent)
            {
                if (!approvedContent.TryGetValue(pair.Key, out var prior) || string.Equals(prior.Sha256, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                diagnostics.Add(Diagnostic.Create("PS005", $"Approved asset content changed at {pair.Value.PackageRelativePath}.", pair.Value.Project, pair.Value.PackageId, pair.Value.PackageRelativePath));
            }
        }

        return diagnostics;
    }

    private static string Key(BaselineEntry entry) => string.Join("|", entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath);
    private static string Key(SurfaceEntry entry) => string.Join("|", entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath);
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

    public string ToText()
    {
        var builder = new StringBuilder(string.Format(CultureInfo.InvariantCulture, "PackageSurface {0} (schema {1})", Operation, SchemaVersion));
        builder.AppendLine();
        builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "Entries: {0}; strict content: {1}", Entries.Count, StrictContent.ToString().ToLowerInvariant()));
        foreach (var entry in Entries)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3} [{4}] {5}", entry.Capability, entry.Active ? "active" : "present", entry.PackageId, entry.Version, entry.Relationship, entry.PackageRelativePath));
        }
        foreach (var diagnostic in Diagnostics)
        {
            builder.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0} {1}: {2}", diagnostic.Id, diagnostic.Name, diagnostic.Message));
        }
        return builder.ToString().TrimEnd();
    }

    public SarifDocument ToSarif() => new("2.1.0", "https://json.schemastore.org/sarif-2.1.0.json", new[] { new SarifRun(new SarifTool(new SarifDriver("PackageSurface", CommandLine.ToolVersion)), Diagnostics.Select(diagnostic => SarifResult.Create(diagnostic)).ToArray()) });
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
public sealed record SarifResult(string RuleId, SarifMessage Message, string Level)
{
    public static SarifResult Create(Diagnostic diagnostic) => new(diagnostic.Id, new SarifMessage(diagnostic.Message), diagnostic.Severity);
}
public sealed record SarifMessage(string Text);

public static class JsonOptions
{
    public static readonly JsonSerializerOptions Default = new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Converters = { new JsonStringEnumConverter() } };
    public static readonly JsonSerializerOptions Indented = new(Default) { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };
}
