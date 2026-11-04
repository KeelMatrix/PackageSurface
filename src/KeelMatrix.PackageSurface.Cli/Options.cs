using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

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
        PackageSurface shows when a NuGet dependency starts participating in your build in a new way.

        Usage:
          package-surface scan <path> [options]
          package-surface baseline <path> --output <baseline> [options]
          package-surface check <path> --baseline <baseline> [options]
          package-surface --help
          package-surface --version

        Options:
          --format text|json|sarif       Report format (default: text).
          --output <baseline>            Baseline output path (required by baseline).
          --baseline <baseline>          Baseline input path (required by check).
          --strict-content               Fingerprint present, active build/compiler execution assets (default: off).
          --compiler-api-version <v>     Set the versioned analyzer compatibility context.
          --project <path>               Select one project when a solution contains several projects.
          --no-telemetry                 Suppress the optional activation request for this invocation.

        Exit codes:
          0  Scan/baseline succeeded, or check passed.
          1  Check found a surface or policy difference.
          2  Invalid invocation, missing/unsupported input, incomplete analysis, or environment error.

        Analysis reads existing restore evidence. It does not restore packages, evaluate MSBuild,
        or execute package code. PS007 means analysis is incomplete; reports contain no surface
        entries, and baseline files are not replaced. Supported input and safety details:
        https://github.com/KeelMatrix/PackageSurface/blob/main/docs/restore-and-baseline-safety.md
        Product scope and examples:
        https://github.com/KeelMatrix/PackageSurface/blob/main/README.md
        Telemetry details:
        https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md
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
            return new(ParseResultKind.Invalid, Error: "Unknown command.");
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

            if (argument.StartsWith('-'))
            {
                return new(ParseResultKind.Invalid, Error: "Unknown option.");
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
