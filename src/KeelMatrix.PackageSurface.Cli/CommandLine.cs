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
            var selection = ProjectSelection.Resolve(options.InputPath!, options.ProjectPath);
            var current = AnalyzeSelection(selection, options.StrictContent);
            var diagnostics = current.IncompleteReasons
                .Select(reason => Diagnostic.Create("PS007", reason))
                .ToList();

            if (options.Command == CommandKind.Check && diagnostics.Count == 0)
            {
                var baseline = BaselineDocument.Read(options.BaselinePath!);
                if (baseline.SchemaVersion != SchemaVersion)
                {
                    throw new InvalidDataException($"Unsupported baseline schema version {baseline.SchemaVersion}.");
                }

                if (baseline.StrictContent && !current.StrictContent)
                {
                    current = AnalyzeSelection(selection, strictContent: true);
                }

                diagnostics.AddRange(DiffEngine.Compare(baseline.Entries, current.Entries, baseline.StrictContent));
            }

            var report = ReportDocument.Create(options.Command, current, diagnostics);
            WriteOutput(report, options.Format);

            if (diagnostics.Count > 0)
            {
                return diagnostics.Any(diagnostic => diagnostic.Id == "PS007") ? 2 : 1;
            }

            if (options.Command == CommandKind.Baseline)
            {
                BaselineDocument.Write(options.OutputPath!, current);
            }

            if ((options.Command is CommandKind.Baseline or CommandKind.Check) && current.ResolvedPackageCount > 0 && options.TelemetryEnabled)
            {
                TrackActivation();
            }

            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or NotSupportedException)
        {
            Console.Error.WriteLine($"error: {ex.Message}");
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

    private static void TrackActivation()
    {
        try
        {
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
          --strict-content          Record SHA-256 content fingerprints for classified assets.
          --project <path>          Select one project when a solution contains several projects.
          --telemetry on|off        Enable or disable best-effort activation telemetry.
          --no-telemetry             Disable best-effort activation telemetry.

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
    public static ProjectSelection Resolve(string inputPath, string? projectPath)
    {
        var input = Path.GetFullPath(inputPath);
        if (projectPath is not null)
        {
            var selected = ResolveProject(Path.GetFullPath(projectPath), Path.GetDirectoryName(input)!);
            return new(new[] { selected });
        }

        if (File.Exists(input) && input.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return new(new[] { ResolveProject(input, Path.GetDirectoryName(input)!) });
        if (File.Exists(input) && input.EndsWith(".assets.json", StringComparison.OrdinalIgnoreCase)) return new(new[] { ResolveAssets(input) });
        if (File.Exists(input) && input.EndsWith(".sln", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.GetDirectoryName(input)!;
            var projects = new List<SelectedProject>();
            foreach (var line in File.ReadLines(input))
            {
                var comma = line.IndexOf(", \"", StringComparison.Ordinal);
                if (comma < 0 || !line.Contains(".csproj\"", StringComparison.OrdinalIgnoreCase)) continue;
                var start = line.IndexOf('"', comma);
                var end = line.IndexOf('"', start + 1);
                if (start < 0 || end < 0) continue;
                var path = Path.GetFullPath(Path.Combine(root, line[(start + 1)..end].Replace('\\', Path.DirectorySeparatorChar)));
                projects.Add(ResolveProject(path, root));
            }
            return new(projects);
        }

        if (Directory.Exists(input))
        {
            var direct = Path.Combine(input, "obj", "project.assets.json");
            if (File.Exists(direct)) return new(new[] { ResolveAssets(direct) });
            var projects = Directory.EnumerateFiles(input, "*.csproj", SearchOption.TopDirectoryOnly)
                .Take(128)
                .Select(path => ResolveProject(path, input))
                .ToArray();
            return new(projects);
        }

        throw new FileNotFoundException("The input path does not exist.", input);
    }

    private static SelectedProject ResolveProject(string projectPath, string displayRoot)
    {
        if (!File.Exists(projectPath)) throw new FileNotFoundException("The selected project does not exist.", projectPath);
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
    public static BaselineDocument Read(string path)
    {
        using var stream = File.OpenRead(path);
        var document = JsonSerializer.Deserialize<BaselineDocument>(stream, JsonOptions.Default) ?? throw new InvalidDataException("Baseline is empty.");
        if (document.Entries is null)
        {
            throw new InvalidDataException("Baseline has no entries array.");
        }

        return document;
    }

    public static void Write(string path, SurfaceSnapshot snapshot)
    {
        var full = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        var baseline = new BaselineDocument(CommandLineSchema.Version, "0.1.0", snapshot.StrictContent, snapshot.Entries.Select(BaselineEntry.From).ToArray(), Array.Empty<string>());
        var temporary = full + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(temporary, JsonSerializer.Serialize(baseline, JsonOptions.Indented) + Environment.NewLine, new UTF8Encoding(false));
        File.Move(temporary, full, overwrite: true);
    }
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
    string? IncompleteReason)
{
    public static BaselineEntry From(SurfaceEntry entry) => new(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath, entry.Present, entry.Active, entry.Sha256, entry.Incomplete, entry.IncompleteReason);
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
            foreach (var pair in observed)
            {
                if (!approved.TryGetValue(pair.Key, out var prior) || string.Equals(prior.Sha256, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
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
