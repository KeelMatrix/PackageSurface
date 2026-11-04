using System.Text.Json;
using NuGet.Versioning;
using static KeelMatrix.PackageSurface.Probe.RestoreIdentityNormalization;
using static KeelMatrix.PackageSurface.Probe.RestoreJsonSchema;

namespace KeelMatrix.PackageSurface.Probe;

internal static class DependencyRequirements
{
    internal static bool TryParseFormat4PackageRequirement(
        string value,
        out string packageId,
        out PackageVersionRange requirement)
    {
        var separator = value.IndexOf(">=", StringComparison.Ordinal);
        if (separator <= 0 || value.IndexOf(">=", separator + 2, StringComparison.Ordinal) >= 0)
        {
            packageId = string.Empty;
            requirement = null!;
            return false;
        }

        packageId = value[..separator].Trim();
        var version = value[(separator + 2)..].Trim();
        if (!IsValidPackageId(packageId) || !TryParsePackageVersionRange($"[{version}, )", out requirement))
        {
            packageId = string.Empty;
            requirement = null!;
            return false;
        }

        return true;
    }

    internal static bool IsValidProjectDependencyPath(string value)
    {
        var normalized = value.Trim().Replace('\\', '/');
        var extension = Path.GetExtension(normalized);
        return normalized.Length > 0 &&
            normalized.Contains('/', StringComparison.Ordinal) &&
            !normalized.StartsWith('/') &&
            !Path.IsPathRooted(normalized) &&
            !normalized.Contains(':', StringComparison.Ordinal) &&
            !normalized.Contains('\0') &&
            normalized.Split('/').All(part => part.Length > 0 && part != ".") &&
            (extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".fsproj", StringComparison.OrdinalIgnoreCase) ||
             extension.Equals(".vbproj", StringComparison.OrdinalIgnoreCase));
    }

    internal static bool TryParsePackageVersionRange(string? value, out PackageVersionRange requirement)
    {
        requirement = null!;
        if (string.IsNullOrWhiteSpace(value) || !VersionRange.TryParse(value, out var parsed) || parsed is null || parsed.IsFloating || (!parsed.HasLowerBound && !parsed.HasUpperBound))
        {
            return false;
        }

        requirement = new PackageVersionRange(parsed);
        return true;
    }

    internal sealed record PackageVersionRange(VersionRange Value)
    {
        public bool Matches(string value)
        {
            return PackageIdentity.TryParseNuGetVersion(value, out var candidate) &&
                Value.Satisfies(candidate, VersionComparer.VersionRelease);
        }
    }

    internal static bool TryReadDependencyRequirement(JsonElement value, out PackageVersionRange requirement)
    {
        if (value.ValueKind == JsonValueKind.String)
        {
            return TryParsePackageVersionRange(value.GetString(), out requirement);
        }

        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty("version", out var version) &&
            version.ValueKind == JsonValueKind.String)
        {
            return TryParsePackageVersionRange(version.GetString(), out requirement);
        }

        requirement = null!;
        return false;
    }
}
