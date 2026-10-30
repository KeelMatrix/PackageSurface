# Restore and baseline safety reference

This document is the detailed contract for restore evidence, package XML, baseline files, and filesystem identity. The [README](../README.md) covers the consumer workflow; `package-surface --help` lists the command syntax and exit codes.

## Supported input

PackageSurface analyzes existing `project.assets.json` output for SDK-style `PackageReference` projects. It does not restore packages, query feeds, evaluate MSBuild, load dependency assemblies, execute package or MSBuild code, or crawl the global package cache. Coherent restore formats 3 and 4 are supported. The classifier reads only package files reachable from the resolved graph and package inventory.

The selected evidence describes the restore that was exercised. It does not prove a production maximum, estimate observability cost, or determine whether a package is malicious or vulnerable.

## Restore identity and schema

One canonical restore-identity index validates project and restore framework maps, effective frameworks, target aliases, format-4 dependency-group keys, RID-qualified target keys, and package `ID/version` identities before reachability or capability classification. Framework keys, effective frameworks, and aliases must resolve to the same canonical identity. Project and restore framework sets, dependency-group sets, and resolved target framework members must agree in both directions. Equivalent short and long framework monikers are reconciled using effective-framework metadata; RID-qualified target names are split before matching.

For each applicable target graph, one direct/project-reference-rooted closure is built. Every target package must belong to that closure. Disconnected package nodes or islands, multiple distinct versions of one package ID in a target graph, malformed identities, unsupported dependency requirements, or a requirement that does not match exactly one selected version produce `PS007` before package inventories or surface entries are produced. Project, target-package, and format-4 dependency values use the same NuGet-compatible requirement grammar. `NuGet.Versioning` 7.9.0 supplies the parser and comparer for package identities, ranges, package roots, imports, baseline provenance, and format-4 requirements.

The schema-aware restore boundary rejects exact and case-variant duplicate properties, unknown members of consumed JSON objects, and non-canonical spellings of consumed JSON/XML members. JSON extensions use the explicit `x-` prefix. Target-package groups use NuGet's exact canonical names; unknown, case-variant, null, or malformed groups produce `PS007`. `packageFolders` entries must be absolute paths with object values; multiple valid fallback folders are supported. `fallbackFolders` must be an array of strings. `SdkAnalysisLevel` and `compilerApiVersion`, when present, must be strings.

Every admitted `project.restore`, `project.frameworks`, and `project.restore.frameworks` member is checked against its native SDK shape before graph closure. Supported shapes include strings, booleans, string arrays, string- or object-valued maps, nullable `suppressedAdvisories` values, and `{name,version}` download-dependency objects. Both string and expanded-object dependency values are supported. Wrong primitives, invalid nulls, wrong array elements or map values, and malformed nested metadata produce `PS007`; absent optional members remain valid. The shape matrix covers SDK 8.0.425, 9.0.121, and 10.0.401 assets in formats 3 and 4.

## Package XML and imports

Package XML is parsed with DTD processing and external entities disabled and with bounded input size and depth. The root must be an MSBuild `Project`; consumed elements may use the supported empty or standard MSBuild namespace. Foreign namespaces fail closed, except elements in the explicit `urn:keelmatrix:packagesurface:extension` namespace, which are ignored as extension data. A root-level `ProjectExtensions` child is also ignored.

The classifier recognizes `Import` only as a direct child of `Project` or a direct child of a root `ImportGroup`. An `ImportGroup` must be a direct child of `Project`. `UsingTask` must be a direct child of `Project`; inline task `Code` must be a direct child of `Task`, which must be a direct child of `UsingTask`. `Exec` must be a direct child of a root `Target`. A root-level MSBuild `ProjectExtensions` child and the explicit XML extension namespace are ignored as extension data. Namespace or parent mismatches, missing required import targets, and unrecognized contexts containing a consumed element fail closed as `PS007`; such content does not activate assets, recurse into imports, or count as an observed primitive.

Generated top-level imports and nested package imports are evidence only when their package and file are present in the resolved inventory and reachable in every applicable target graph. Nested static imports are bounded, package-relative, inherit their importing capability and project context, and are included in strict-content fingerprints when active. Conditions on `Project`, `ImportGroup`, and `Import` are combined. The bounded condition evaluator supports `TargetFramework` equality and inequality and preserves the written MSBuild string semantics; it does not reuse restore framework alias normalization. Unsupported property expansion or syntax remains unknown and produces `PS007`; full MSBuild evaluation is not attempted. The four consumed package XML elements are `Import`, `UsingTask`, `Exec`, and `Code`, with their documented consumed attributes. Standard unconsumed MSBuild elements and attributes are tolerated. Consumed exact, case-variant, near-spelled, or duplicate members are incomplete evidence.

## Incomplete analysis and reports

`PS007 AnalysisIncomplete` means PackageSurface could not establish the selected surface with the supported evidence and bounds. An incomplete invocation emits no surface entries in text, JSON, or SARIF. `baseline` does not replace an existing file, and `check` does not turn incomplete evidence into a clean result or a policy difference. Structural budgets apply across the complete selected invocation; narrow a large or ambiguous selection with `--project` and rerun.

Stable diagnostics are `PS001` through `PS007`: new capability, new active asset, build surface change, compiler surface change, strict-content fingerprint change, native surface change, and incomplete analysis. JSON and SARIF schemas are versioned. SARIF scan and baseline reports use `PS-SURFACE` note results for classified facts; check reports contain diagnostics.

## Baseline format and content fingerprints

Schema version `1` is the only supported baseline version. A baseline requires `schemaVersion`, `toolVersion`, `strictContent`, `entries`, and `incompleteReasons`. Each entry requires context, package identity, relationship, capability, package-relative path, present/active state, incomplete state, and a null or valid 64-character SHA-256 value. Project, TFM, and RID fields are nullable only when the entry context does not apply. Package paths cannot be rooted or contain `..`; project identities are stable solution-relative anchors.

Baseline JSON uses exact camelCase property names and named string enums. Duplicate or case-variant members, unknown members, integer enums, null substitutions, null entries, incomplete markers, oversized documents, unknown schema or enum values, invalid hashes, active-but-missing assets, duplicate identities, and incomplete baselines are rejected with controlled exit code `2`. Unknown or future schema versions are not upgraded.

`check` never rewrites a baseline. `--strict-content` fingerprints only present, active build/compiler execution assets. It requires a baseline created with strict-content fingerprints. A strict baseline enables fingerprinting even if `check` omits the option. Inactive assets, native runtime assets, and informational `ToolOrScriptPresent` entries are not eligible. Fingerprints do not include machine cache paths.

## Baseline output path safety

Before writing, an explicit `baseline --output` path is checked against every reachable package-inventory file resolved from `packageFolders`: global and fallback roots, direct and transitive packages, every TFM/RID, nested static imports, and all inventory categories are included. Lexical `.` and `..` aliases, one or multiple hardlinks, and direct, interior, or ancestor reparse/symlink aliases are rejected with `PS007` and exit code `2` before mutation. Rejection preserves existing output and input bytes; a genuinely distinct output is accepted.

On macOS, the standard root-level `/var` to `/private/var` alias is normalized. Caller-controlled root aliases and links nested below `/var` remain rejected.

## Filesystem identity

Filesystem path identity follows the filesystem containing the resolved path. Project-path identities use the project/assets filesystem. Package-relative asset identities and package-file `Exists(...)` checks use the NuGet package-root filesystem. Those roots can differ in case sensitivity, including across volumes. NuGet package ID/version identity remains case-insensitive. Inaccessible or ambiguous resolution fails closed with `PS007`.

Containment is checked after path canonicalization. Reparse points within the declared package/cache root are rejected; ancestor links above that root do not expand the declared containment boundary.
