using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

public sealed record ReportDocument(
    int SchemaVersion,
    string Operation,
    bool StrictContent,
    IReadOnlyList<SurfaceEntry> Entries,
    IReadOnlyList<Diagnostic> Diagnostics,
    IReadOnlyList<string> IncompleteReasons)
{
    public static ReportDocument Create(CommandKind command, SurfaceSnapshot snapshot, IReadOnlyList<Diagnostic> diagnostics)
    {
        var normalizedDiagnostics = diagnostics
            .Select(diagnostic => diagnostic with { Message = DiagnosticDataPolicy.SanitizeReason(diagnostic.Message) })
            .ToArray();
        var incompleteDiagnostics = normalizedDiagnostics
            .Where(diagnostic => diagnostic.Id == "PS007")
            .ToArray();
        if (incompleteDiagnostics.Length > 0)
        {
            var incompleteReasons = DiagnosticDataPolicy.NormalizeReasons(
                snapshot.IncompleteReasons.Concat(incompleteDiagnostics.Select(diagnostic => diagnostic.Message)));
            return new(CommandLineSchema.Version, command.ToString().ToLowerInvariant(), snapshot.StrictContent,
                Array.Empty<SurfaceEntry>(), incompleteDiagnostics, incompleteReasons);
        }

        return new(CommandLineSchema.Version, command.ToString().ToLowerInvariant(), snapshot.StrictContent,
            snapshot.Entries, normalizedDiagnostics, DiagnosticDataPolicy.NormalizeReasons(snapshot.IncompleteReasons));
    }

    public static void EnsureOutputWithinLimit(SurfaceSnapshot snapshot, IReadOnlyList<Diagnostic> diagnostics, long limit) =>
        EnsureOutputWithinLimit(snapshot.Entries, diagnostics, snapshot.IncompleteReasons, limit);

    public void EnsureOutputWithinLimit(long limit) =>
        EnsureOutputWithinLimit(Entries, Diagnostics, IncompleteReasons, limit);

    private static void EnsureOutputWithinLimit(
        IReadOnlyList<SurfaceEntry> entries,
        IReadOnlyList<Diagnostic> diagnostics,
        IReadOnlyList<string> incompleteReasons,
        long limit)
    {
        long estimated = 512;
        foreach (var entry in entries)
        {
            estimated += 768L + (entry.Project?.Length ?? 0) + entry.PackageId.Length + entry.Version.Length + entry.PackageRelativePath.Length;
            if (estimated > limit) throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        foreach (var diagnostic in diagnostics)
        {
            estimated += 512L + diagnostic.Message.Length + (diagnostic.Project?.Length ?? 0) + (diagnostic.PackageId?.Length ?? 0) + (diagnostic.PackageRelativePath?.Length ?? 0);
            if (estimated > limit) throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        estimated += incompleteReasons.Sum(reason => 128L + reason.Length);
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
