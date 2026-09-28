# Restore identity resolution

The canonical source of truth for restore identity is `RestoreIdentityIndex` in `src/KeelMatrix.PackageSurface.Probe/ResolvedGraphClassifier.cs`. It owns the normalized identity set used before reachability and capability filtering.

## Resolution inventory

| Evidence surface | Canonical boundary |
| --- | --- |
| Project and restore framework maps | `ReadFrameworkIdentityMap` and `RestoreIdentityIndex.TryGetFramework` |
| Target and RID graph keys | `TryReadTargetIdentity` and `RestoreIdentityIndex.TryGetTarget` |
| Library and target package `ID/version` keys | `ReadPackageIdentityMap`, `RestoreIdentityIndex.TryGetLibrary`, and `RestoreIdentityIndex.TryGetPackage` |
| Package cache roots | `ResolvePackageRoots`, which populates `RestoreIdentityIndex.PackageRoots` |
| Generated package-root imports | `RestoreIdentityIndex.TryGetPackageByImportSuffix` and `TryGetPackageByRoot` |
| Direct package asset rules | Canonical framework keys returned by `RestoreIdentityIndex.TryGetFramework` |
| Project, target-package, and format-4 requirements | Canonical package identities plus the restore requirement parser and selected-version match |

The remaining direct JSON property lookups are schema traversal inside the canonical index or ordinary metadata inspection. They do not parse or resolve an independent package, framework, target, or package-root identity. Static import path resolution still performs filesystem containment checks, but its package-root references are first resolved by the canonical index.

The checked-in `scripts/test-restore-identity-canonicalization.ps1` search guard fails if a raw package-key splitter, framework-map fallback, package-root scan, or target/framework relation is reintroduced at a call site. It also requires the library lookups to remain inside the canonical boundary.

The same schema boundary owns consumed-member spelling and unknown-member handling. Exact canonical JSON names are required at every consumed object level; only members with the explicit `x-` prefix are extension metadata. Consumed XML attributes require canonical names, with extension attributes limited to the `urn:keelmatrix:packagesurface:extension` namespace. Unknown or near-spelled consumed members fail closed as `PS007` before identity or capability decisions.

## Completeness and requirement grammar

Before classification, declared and restore framework sets are compared in both directions, format-4 dependency-group keys are compared in both directions, and every resolved target/RID framework must belong to the canonical framework set. Case-folded and equivalent framework monikers share the same canonical key.

Dependency values accept only restore forms observed in SDK output and the corresponding bounded interval syntax: resolved versions such as `1.0.0`, inclusive/exclusive interval forms such as `[1.0.0, )`, and format-4 package requirements such as `PackageId >= 1.0.0`. A format-4 project dependency is accepted only as a relative project path ending in `.csproj`, `.fsproj`, or `.vbproj`. Every package requirement is matched against the selected package version in the applicable target graph; malformed, ambiguous, unsupported, or unmatched values fail closed.
