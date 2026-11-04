using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

public static class SurfaceIdentityKey
{
    public static SurfaceIdentity Create(BaselineEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath);
    public static SurfaceIdentity Create(SurfaceEntry entry) => SurfaceIdentity.From(entry);
    public static SurfaceIdentity CreateCategory(BaselineEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, string.Empty, entry.Capability, null);
    public static SurfaceIdentity CreateCategory(SurfaceEntry entry) => SurfaceIdentity.Create(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, string.Empty, entry.Capability, null);
}

public sealed record Diagnostic(
    string Id,
    string Name,
    string Message,
    string Severity,
    string? Project,
    string? PackageId,
    string? PackageRelativePath,
    string? Version = null,
    string? Relationship = null,
    SurfaceContextKind? Context = null,
    string? TargetFramework = null,
    string? RuntimeIdentifier = null,
    CapabilityKind? Capability = null)
{
    public static Diagnostic Create(string id, string message, string? project = null, string? packageId = null, string? path = null) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, DiagnosticDataPolicy.SanitizeReason(message), id == "PS007" ? "error" : "warning", project, packageId, path);

    public static Diagnostic ForEntry(string id, string message, SurfaceEntry entry) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, DiagnosticDataPolicy.SanitizeReason(message), id == "PS007" ? "error" : "warning", entry.Project, entry.PackageId, entry.PackageRelativePath, entry.Version, entry.Relationship, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.Capability);

    public static Diagnostic ForBaseline(string id, string message, BaselineEntry entry) =>
        new(id, id switch { "PS001" => "NewCapability", "PS002" => "NewActiveAsset", "PS003" => "BuildSurfaceChanged", "PS004" => "CompilerSurfaceChanged", "PS005" => "ContentFingerprintChanged", "PS006" => "NativeSurfaceChanged", _ => "AnalysisIncomplete" }, DiagnosticDataPolicy.SanitizeReason(message), id == "PS007" ? "error" : "warning", entry.Project, entry.PackageId, entry.PackageRelativePath, entry.Version, entry.Relationship, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.Capability);
}

public static class DiffEngine
{
    public static IReadOnlyList<Diagnostic> Compare(IReadOnlyList<BaselineEntry> baseline, IReadOnlyList<SurfaceEntry> current, bool strictContent)
    {
        var approved = baseline.Where(entry => entry.Active).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
        var observed = current.Where(entry => entry.Active).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
        var diagnostics = new List<Diagnostic>();
        foreach (var added in observed.Where(pair => !approved.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            var id = SpecificId(added.Capability);
            if (!baseline.Any(entry => entry.Active && SurfaceIdentityComparer.Instance.Equals(SurfaceIdentityKey.CreateCategory(entry), SurfaceIdentityKey.CreateCategory(added)))) diagnostics.Add(Diagnostic.ForEntry("PS001", FormatEntryMessage("New capability category", added), added));
            diagnostics.Add(Diagnostic.ForEntry("PS002", FormatEntryMessage("New active capability asset", added), added));
            diagnostics.Add(Diagnostic.ForEntry(id, FormatEntryMessage("Capability surface changed", added), added));
        }

        foreach (var removed in approved.Where(pair => !observed.ContainsKey(pair.Key)).Select(pair => pair.Value))
        {
            diagnostics.Add(Diagnostic.ForBaseline(SpecificId(removed.Capability), FormatEntryMessage("Approved active capability is no longer active", removed), removed));
        }

        if (strictContent)
        {
            var approvedContent = baseline.Where(entry => CapabilityPolicy.IsStrictContentEligible(entry.Capability, entry.Present, entry.Active)).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
            var observedContent = current.Where(CapabilityPolicy.IsStrictContentEligible).ToDictionary(SurfaceIdentityKey.Create, SurfaceIdentityComparer.Instance);
            foreach (var pair in observedContent)
            {
                if (!approvedContent.TryGetValue(pair.Key, out var prior) || string.Equals(prior.Sha256, pair.Value.Sha256, StringComparison.OrdinalIgnoreCase)) continue;
                diagnostics.Add(Diagnostic.ForEntry("PS005", FormatEntryMessage("Approved asset content changed", pair.Value), pair.Value));
            }
        }

        return diagnostics;
    }

    private static string FormatEntryMessage(string prefix, BaselineEntry entry) => $"{prefix}: package {entry.PackageId} version {entry.Version} ({entry.Relationship}), {entry.Capability} {entry.Context} {entry.TargetFramework ?? "project"}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)} at {entry.PackageRelativePath}.";
    private static string FormatEntryMessage(string prefix, SurfaceEntry entry) => $"{prefix}: package {entry.PackageId} version {entry.Version} ({entry.Relationship}), {entry.Capability} {entry.Context} {entry.TargetFramework ?? "project"}{(entry.RuntimeIdentifier is null ? string.Empty : "/" + entry.RuntimeIdentifier)} at {entry.PackageRelativePath}.";
    private static string SpecificId(CapabilityKind kind) => kind switch { CapabilityKind.BuildProps or CapabilityKind.BuildTargets or CapabilityKind.BuildTransitive or CapabilityKind.BuildMultiTargeting => "PS003", CapabilityKind.CompilerExtension or CapabilityKind.CompileSourceInjection => "PS004", CapabilityKind.NativeRuntime => "PS006", _ => "PS002" };
}
