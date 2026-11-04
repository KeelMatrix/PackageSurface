using System.Text.Json.Serialization;
using KeelMatrix.PackageSurface.Probe;

namespace KeelMatrix.PackageSurface;

public sealed record BaselineDocument(
    int SchemaVersion,
    string ToolVersion,
    bool StrictContent,
    IReadOnlyList<BaselineEntry> Entries,
    IReadOnlyList<string> IncompleteReasons)
{
    public static BaselineDocument Read(string path) => BaselineStore.ReadCore(path, validatePathIdentities: true);
    public static BaselineDocument ReadForAnalysis(string path) => BaselineStore.ReadCore(path, validatePathIdentities: false);
    public static void ValidatePathIdentities(BaselineDocument document) => BaselineSchema.ValidatePathIdentities(document);
    public static void Write(string path, SurfaceSnapshot snapshot) => BaselineStore.Write(path, snapshot);
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
    string? IncompleteReason,
    IReadOnlyList<string>? ObservedPrimitives = null)
{
    public static BaselineEntry From(SurfaceEntry entry) => new(entry.Project, entry.Context, entry.TargetFramework, entry.RuntimeIdentifier, entry.PackageId, entry.Version, entry.Relationship, entry.Capability, entry.PackageRelativePath, entry.Present, entry.Active, CapabilityPolicy.IsStrictContentEligible(entry) ? entry.Sha256 : null, entry.Incomplete, entry.IncompleteReason, entry.ObservedPrimitives);

    [JsonIgnore]
    public SurfaceIdentity SurfaceIdentity => SurfaceIdentityKey.Create(this);
}
