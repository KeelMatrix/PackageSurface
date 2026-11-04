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
    private const long MaxInputBytes = 16 * 1024 * 1024;
    private const long MaxTotalInputBytes = 128 * 1024 * 1024;
    private static readonly string[] GenericIncompleteReasons = { DiagnosticDataPolicy.GenericIncompleteReason };
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
            Console.Error.WriteLine($"error: {DiagnosticDataPolicy.SafeParserMessage(ex.Message)}");
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
            Console.Error.WriteLine($"error: {DiagnosticDataPolicy.SafeParserMessage(parsed.Error)}");
            Console.Error.WriteLine("Run 'package-surface --help' for usage.");
            return 2;
        }

        var options = parsed.Options!;
        try
        {
            var baselinePath = options.Command == CommandKind.Baseline ? Path.GetFullPath(options.OutputPath!) : null;
            BaselineDocument? baseline = null;
            var strictContent = options.StrictContent;
            using var fileSystemScope = FileSystemComparisonScope.Push(options.InputPath!);
            var selection = ProjectSelection.Resolve(options.InputPath!, options.ProjectPath);
            if (options.Command == CommandKind.Check)
            {
                baseline = BaselineDocument.ReadForAnalysis(options.BaselinePath!);
                strictContent |= baseline.StrictContent;
            }

            var baselineTransaction = baselinePath is null
                ? null
                : BaselineFileTransaction.Prepare(baselinePath, options.InputPath!, selection);
            var current = AnalyzeSelection(selection, strictContent, options.CompilerApiVersion);
            if (baseline is not null)
            {
                BaselineDocument.ValidatePathIdentities(baseline);
            }

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
            try
            {
                if (options.Command == CommandKind.Baseline && !incomplete)
                {
                    try
                    {
                        ReportDocument.EnsureOutputWithinLimit(current, diagnostics, ReportWriter.MaximumBytes);
                        baselineTransaction!.Begin();
                        BaselineDocument.Write(options.OutputPath!, current);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or JsonException or NotSupportedException)
                    {
                        baselineTransaction!.Rollback();
                        diagnostics.Add(Diagnostic.Create("PS007", "The baseline could not be persisted after validating the analyzed surface."));
                        incomplete = true;
                    }
                }

                // The terminal result is normalized only after every command
                // status mutation, including persistence and strict-policy
                // failures, has completed.
                var report = ReportDocument.Create(options.Command, current, diagnostics);
                ReportWriter.Write(report, options.Format, OutputSerializer, OutputSink);
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
            var message = DiagnosticDataPolicy.SafeExceptionMessage(ex);
            var report = ReportDocument.Create(
                options.Command,
                new SurfaceSnapshot(Array.Empty<SurfaceEntry>(), GenericIncompleteReasons, options.StrictContent, 0),
                new[] { Diagnostic.Create("PS007", message) });
            try
            {
                ReportWriter.Write(report, options.Format, OutputSerializer, OutputSink);
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

    private static Action? TelemetryHook { get; set; }

    private static Func<ReportDocument, OutputFormat, string>? OutputSerializer { get; set; }

    private static Action<string>? OutputSink { get; set; }

    private static void TrackActivation()
    {
        if (TelemetryHook is not null)
        {
            TelemetryHook();
            return;
        }

        new Client("PackageSurface", typeof(CommandLine)).TrackActivation();
    }

}
