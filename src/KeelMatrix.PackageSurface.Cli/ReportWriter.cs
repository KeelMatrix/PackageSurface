using System.Text;
using System.Text.Json;

namespace KeelMatrix.PackageSurface;

internal static class ReportWriter
{
    internal const long MaximumBytes = 16 * 1024 * 1024;

    internal static void Write(ReportDocument report, OutputFormat format, Func<ReportDocument, OutputFormat, string>? serializer = null, Action<string>? sink = null)
    {
        report.EnsureOutputWithinLimit(MaximumBytes);
        var output = serializer?.Invoke(report, format) ?? format switch
        {
            OutputFormat.Text => report.ToText(),
            OutputFormat.Json => JsonSerializer.Serialize(report, JsonOptions.Indented),
            OutputFormat.Sarif => JsonSerializer.Serialize(report.ToSarif(), JsonOptions.Indented),
            _ => throw new InvalidDataException("Unsupported output format.")
        };
        if (Encoding.UTF8.GetByteCount(output) > MaximumBytes)
        {
            throw new InvalidDataException("The report exceeds the supported output size limit.");
        }

        if (sink is not null)
        {
            sink(output);
        }
        else
        {
            Console.WriteLine(output);
        }
    }
}
