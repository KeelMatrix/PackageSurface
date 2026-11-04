using System.Text;
using System.Text.Json;
using KeelMatrix.PackageSurface.Probe;

using static KeelMatrix.PackageSurface.BaselineSchema;

namespace KeelMatrix.PackageSurface;

internal static class BaselineStore
{
    internal static BaselineDocument ReadCore(string path, bool validatePathIdentities)
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
            Validate(document, validatePathIdentities);
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
            _ = ReadCore(temporary, validatePathIdentities: true);
            File.Move(temporary, full, overwrite: true);
        }
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
        }
    }

    internal static void EnsureEstimatedSizeWithinLimit(BaselineDocument baseline)
    {
        long estimated = 512;
        foreach (var entry in baseline.Entries)
        {
            estimated += 512L + (entry.Project?.Length ?? 0) + entry.PackageId.Length + entry.Version.Length + entry.PackageRelativePath.Length + (entry.Sha256?.Length ?? 0);
            if (estimated > MaxBaselineBytes) throw new InvalidDataException("The generated baseline exceeds the supported size limit.");
        }
    }
}
