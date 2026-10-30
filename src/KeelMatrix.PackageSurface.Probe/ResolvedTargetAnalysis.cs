using System.Text.Json;
using static KeelMatrix.PackageSurface.Probe.AnalysisLimits;
using static KeelMatrix.PackageSurface.Probe.CapabilityAssetClassifier;
using static KeelMatrix.PackageSurface.Probe.GeneratedImportAnalyzer;
using static KeelMatrix.PackageSurface.Probe.IncompleteAnalysis;
using static KeelMatrix.PackageSurface.Probe.PackageFileSystemSafety;
using static KeelMatrix.PackageSurface.Probe.PackageGraphAnalyzer;
using static KeelMatrix.PackageSurface.Probe.RestoreMetadataReader;

namespace KeelMatrix.PackageSurface.Probe;

internal static class ResolvedTargetAnalysis
{
    internal static ProbeResult Analyze(
        RestoreGraphMetadata metadata,
        ResolvedTargets targets,
        PackageInventorySnapshot packageInventory,
        GeneratedImportSnapshot generatedImports,
        ProjectCapabilityContext capabilityContext,
        bool strictContent,
        string? projectContext,
        AnalysisBudget budget,
        List<string> incomplete)
    {
        var results = new SurfaceResultBuilder(strictContent, projectContext, budget, incomplete);
        foreach (var target in targets.Targets)
        {
            var targetFramework = target.Identity.TargetFramework;
            var targetAlias = target.Identity.TargetAlias;
            var runtimeIdentifier = target.Identity.RuntimeIdentifier;
            foreach (var package in target.Packages)
            {
                var files = GetPackageAssetFiles(
                    package.PackageRoot,
                    package.LibraryKey,
                    package.Library,
                    packageInventory.PackageInventories,
                    incomplete,
                    budget);
                var selectedAnalyzers = SelectAnalyzerPaths(
                    files,
                    capabilityContext.Language,
                    target.AnalyzerPackageKeys,
                    package.LibraryKey,
                    incomplete,
                    capabilityContext.CompilerApiVersion);
                foreach (var relativePath in files)
                {
                    if (!TryClassifyResolvedAsset(relativePath, budget, out var asset))
                    {
                        continue;
                    }

                    var presence = InspectAssetPresence(
                        package.PackageRoot,
                        relativePath,
                        package.LibraryKey,
                        incomplete,
                        budget);
                    var reason = presence.Reason;
                    IReadOnlyList<string>? observedPrimitives = null;
                    ValidateCompilerExtensionMetadata(
                        asset.Capability,
                        presence.Present,
                        presence.PhysicalPath,
                        relativePath,
                        package.LibraryKey,
                        incomplete,
                        ref reason);
                    observedPrimitives = InspectPackageBuildMetadata(
                        asset.Capability,
                        presence.Present,
                        presence.PhysicalPath,
                        relativePath,
                        package.LibraryKey,
                        incomplete,
                        ref reason);

                    var active = IsActive(
                        asset.Capability,
                        relativePath,
                        package.Identity.Identity,
                        target.AnalyzerPackageKeys,
                        selectedAnalyzers,
                        package.PackageRoot,
                        package.TargetAssets,
                        asset.Context,
                        targetAlias,
                        generatedImports.Imports,
                        generatedImports.ExpectedGeneratedImportSources,
                        capabilityContext.IsMultiTargeting,
                        capabilityContext.Language,
                        ref reason,
                        incomplete,
                        package.LibraryKey);
                    results.AddPackageAsset(
                        target,
                        package,
                        asset,
                        relativePath,
                        presence,
                        active,
                        reason,
                        observedPrimitives);
                }
            }

            foreach (var nestedImport in FindNestedImportAssets(
                         generatedImports.Imports,
                         metadata.Identities,
                         metadata.Libraries,
                         targetFramework,
                         runtimeIdentifier,
                         incomplete))
            {
                if (!TryResolveNestedImportAsset(
                        nestedImport.PackageRoot,
                        nestedImport.RelativePath,
                        packageInventory.PackageInventories,
                        incomplete,
                        out var physicalPath))
                {
                    continue;
                }

                var observedPrimitives = InspectNestedBuildMetadata(
                    physicalPath,
                    nestedImport.Package.CanonicalKey,
                    nestedImport.RelativePath,
                    budget,
                    incomplete);
                results.AddNestedImport(target, nestedImport, physicalPath, observedPrimitives);
            }
        }

        return results.Build(targets.ResolvedPackages.Count);
    }


    private sealed class SurfaceResultBuilder(
        bool strictContent,
        string? projectContext,
        AnalysisBudget budget,
        List<string> incomplete)
    {
        private readonly List<SurfaceEntry> entries = new();
        private readonly Dictionary<SurfaceIdentity, List<SurfaceEntry>> projectEntries = new(SurfaceIdentityComparer.Instance);
        private int accumulatedEntries;

        public void AddPackageAsset(
            ResolvedTarget target,
            ResolvedTargetPackage package,
            CapabilityAssetPath asset,
            string relativePath,
            PackageAssetPresence presence,
            bool active,
            string? reason,
            IReadOnlyList<string>? observedPrimitives)
        {
            var sha = presence.Present && strictContent && IsStrictContentHashEligible(asset.Capability, presence.Present, active)
                ? ComputeSha256(presence.PhysicalPath, budget)
                : null;
            Add(new SurfaceEntry(
                asset.Context == SurfaceContextKind.Project ? null : target.Identity.TargetFramework,
                asset.Context == SurfaceContextKind.Project ? null : target.Identity.RuntimeIdentifier,
                asset.Context,
                package.Identity.Id,
                package.Identity.Version,
                package.IsDirect ? "direct" : "transitive",
                asset.Capability,
                relativePath,
                presence.Present,
                active,
                sha,
                reason is not null,
                reason,
                projectContext,
                observedPrimitives));
        }

        public void AddNestedImport(
            ResolvedTarget target,
            NestedImportAsset nestedImport,
            string physicalPath,
            IReadOnlyList<string>? observedPrimitives)
        {
            var sha = strictContent && IsStrictContentHashEligible(nestedImport.Capability, true, true)
                ? ComputeSha256(physicalPath, budget)
                : null;
            Add(new SurfaceEntry(
                nestedImport.Context == SurfaceContextKind.Project ? null : nestedImport.TargetFramework,
                nestedImport.Context == SurfaceContextKind.Project ? null : nestedImport.RuntimeIdentifier,
                nestedImport.Context,
                nestedImport.Package.Id,
                nestedImport.Package.Version,
                target.DirectPackageKeys.Contains(nestedImport.Package.CanonicalKey) ? "direct" : "transitive",
                nestedImport.Capability,
                nestedImport.RelativePath,
                true,
                true,
                sha,
                false,
                null,
                projectContext,
                observedPrimitives));
        }

        public ProbeResult Build(int resolvedPackageCount)
        {
            entries.AddRange(projectEntries.Values.Select(AggregateProjectEntries));
            return CreateResult(Deduplicate(entries), incomplete, resolvedPackageCount);
        }

        private void Add(SurfaceEntry entry)
        {
            if (++accumulatedEntries > MaxEntries)
            {
                throw new InvalidDataException("The resolved capability surface exceeds the supported entry limit.");
            }

            if (entry.Context == SurfaceContextKind.Project)
            {
                var key = SurfaceIdentity.ForProjectAggregation(entry);
                if (!projectEntries.TryGetValue(key, out var candidates))
                {
                    candidates = new List<SurfaceEntry>();
                    projectEntries.Add(key, candidates);
                }

                candidates.Add(entry);
            }
            else
            {
                entries.Add(entry);
            }
        }
    }


    private static SurfaceEntry[] Deduplicate(IEnumerable<SurfaceEntry> entries) => entries
        .GroupBy(SurfaceIdentity.From, SurfaceIdentityComparer.Instance)
        .Select(group => group.OrderByDescending(entry => entry.Active).First())
        .OrderBy(entry => entry.Context)
        .ThenBy(entry => entry.TargetFramework, StringComparer.Ordinal)
        .ThenBy(entry => entry.RuntimeIdentifier, StringComparer.Ordinal)
        .ThenBy(entry => entry.PackageId, StringComparer.Ordinal)
        .ThenBy(entry => entry.Capability)
        .ThenBy(entry => entry.PackageRelativePath, StringComparer.Ordinal)
        .ToArray();


    private static SurfaceEntry AggregateProjectEntries(IEnumerable<SurfaceEntry> candidates)
    {
        var ordered = candidates
            .OrderByDescending(entry => entry.Relationship.Equals("direct", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(entry => entry.Active)
            .ThenByDescending(entry => entry.Present)
            .ThenBy(entry => entry.Sha256, StringComparer.Ordinal)
            .ThenBy(entry => entry.IncompleteReason, StringComparer.Ordinal)
            .ToArray();
        var first = ordered[0];
        var isIncomplete = ordered.Any(entry => entry.Incomplete);
        var reason = ordered.Select(entry => entry.IncompleteReason).FirstOrDefault(value => value is not null);
        var eligible = IsStrictContentHashEligible(first.Capability, ordered.All(entry => entry.Present), ordered.Any(entry => entry.Active));
        var hash = eligible ? ordered.Select(entry => entry.Sha256).FirstOrDefault(value => value is not null) : null;
        var primitives = ordered
            .SelectMany(entry => entry.ObservedPrimitives ?? Array.Empty<string>())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return first with
        {
            Relationship = ordered.Any(entry => entry.Relationship.Equals("direct", StringComparison.OrdinalIgnoreCase)) ? "direct" : "transitive",
            Present = ordered.All(entry => entry.Present),
            Active = ordered.Any(entry => entry.Active),
            Sha256 = hash,
            Incomplete = isIncomplete,
            IncompleteReason = reason,
            ObservedPrimitives = primitives.Length == 0 ? null : primitives
        };
    }
}
