using System.Text.Json;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

internal static class BaselineSchema
{
    internal const long MaxBaselineBytes = 16 * 1024 * 1024;

    internal const int MaxBaselineEntries = 50_000;

    internal const int MaxBaselineIncompleteReasons = 1_024;

    internal const int MaxBaselineTextLength = 4_096;

    internal static readonly string[] RootRequiredProperties = { "schemaVersion", "toolVersion", "strictContent", "entries", "incompleteReasons" };

    internal static readonly string[] EntryRequiredProperties = { "project", "context", "packageId", "version", "relationship", "capability", "packageRelativePath", "present", "active", "incomplete" };

    internal static readonly string[] EntryOptionalProperties = { "targetFramework", "runtimeIdentifier", "sha256", "incompleteReason", "observedPrimitives" };

    internal static readonly string[] EntryStringProperties = { "project", "context", "packageId", "version", "relationship", "capability", "packageRelativePath" };

    internal static readonly string[] EntryBooleanProperties = { "present", "active", "incomplete" };

    internal static readonly string[] EntryNullableTextProperties = { "targetFramework", "runtimeIdentifier", "sha256", "incompleteReason" };

    internal static void Validate(BaselineDocument document, bool validatePathIdentities = true)
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
        }

        if (validatePathIdentities)
        {
            ValidatePathIdentities(document);
        }
    }

    internal static void ValidateJsonShape(JsonElement root)
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

    internal static bool IsCanonicalEnumName(JsonElement value, Type enumType) =>
        value.ValueKind == JsonValueKind.String && Enum.GetNames(enumType).Contains(value.GetString(), StringComparer.Ordinal);

    internal static void ValidateObjectProperties(JsonElement value, IReadOnlyCollection<string> required, IReadOnlyCollection<string> optional, string description)
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

    internal static void RequireText(string? value, string field)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxBaselineTextLength) throw new InvalidDataException($"Baseline field '{field}' is invalid.");
    }

    internal static void ValidateRelativeText(string value, string field)
    {
        RequireText(value, field);
        var normalized = value.Replace('\\', '/');
        if (Path.IsPathRooted(value) || normalized.Length > 0 && normalized[0] == '/' || (normalized.Length >= 2 && normalized[1] == ':') || normalized.Split('/').Any(part => part is ".." or "")) throw new InvalidDataException($"Baseline field '{field}' is not a safe relative value.");
    }

    internal static void ValidateProjectIdentity(string value)
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

    internal static bool IsSha256(string? value) => value is not null && value.Length == 64 && value.All(Uri.IsHexDigit);

    internal static void ValidatePathIdentities(BaselineDocument document)
    {
        var identities = new HashSet<SurfaceIdentity>(SurfaceIdentityComparer.Instance);
        foreach (var entry in document.Entries)
        {
            if (!identities.Add(SurfaceIdentityKey.Create(entry)))
            {
                throw new InvalidDataException("Baseline contains duplicate capability-surface identities.");
            }
        }
    }

}
