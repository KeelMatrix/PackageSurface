using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using KeelMatrix.PackageSurface;
using KeelMatrix.PackageSurface.Probe;


namespace KeelMatrix.PackageSurface.Cli.Tests;

internal static class CliInvocation
{
    internal static void SetTelemetryHook(Action? hook)
    {
        var property = typeof(CommandLine).GetProperty("TelemetryHook", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Telemetry test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static void SetOutputSerializer(Func<ReportDocument, OutputFormat, string>? hook)
    {
        var property = typeof(CommandLine).GetProperty("OutputSerializer", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Output serializer test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static void SetOutputSink(Action<string>? hook)
    {
        var property = typeof(CommandLine).GetProperty("OutputSink", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new InvalidOperationException("Output sink test hook was not found.");
        property.SetValue(null, hook);
    }

    internal static (int ExitCode, string Output) CaptureCommand(params string[] args)
    {
        var output = new StringWriter(CultureInfo.InvariantCulture);
        var error = new StringWriter(CultureInfo.InvariantCulture);
        var priorOutput = Console.Out;
        var priorError = Console.Error;
        try
        {
            Console.SetOut(output);
            Console.SetError(error);
            var exitCode = CommandLine.Run(args);
            return (exitCode, output.ToString() + error.ToString());
        }
        finally
        {
            Console.SetOut(priorOutput);
            Console.SetError(priorError);
        }
    }
}
