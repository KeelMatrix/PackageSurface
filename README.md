# KeelMatrix.PackageSurface

**PackageSurface tells you when a NuGet dependency starts participating in your build in a new way.** It baselines package-provided MSBuild, compiler-extension, source/content, and native capabilities and fails when that reviewed surface changes.

PackageSurface is not a malware or vulnerability scanner. A reported capability can be completely legitimate; the tool makes the capability change explicit so dependency updates can be reviewed.

It is a .NET global tool for SDK-style `PackageReference` projects that already have restore output.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

Update with `dotnet tool update --global KeelMatrix.PackageSurface`, or remove it with `dotnet tool uninstall --global KeelMatrix.PackageSurface`.

## Quick Start

Restore first, then create and review a baseline:

```powershell
dotnet restore
package-surface scan MySolution.sln
package-surface baseline MySolution.sln --output package-surface.json
package-surface check MySolution.sln --baseline package-surface.json
```

PackageSurface never restores packages, evaluates MSBuild, loads dependency assemblies, runs package code, or queries a feed. The restore artifacts must already exist. Commit `package-surface.json` after review and update it explicitly when a capability change is approved.

## What it reports

The versioned `package-surface.json` baseline records project context, TFM, RID, package identity, direct or transitive relationship, capability category, normalized package-relative asset path, and whether the asset is present and active. `--strict-content` adds SHA-256 fingerprints only for present, active build/compiler execution assets (`BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, and `CompileSourceInjection`). Inactive assets, native runtime assets, and informational `ToolOrScriptPresent` entries are not strict-content eligible; fingerprints do not include machine cache paths.

The taxonomy is:

- `BuildProps`, `BuildTargets`, `BuildTransitive`, and `BuildMultiTargeting` for package MSBuild imports.
- `CompilerExtension` for conventional analyzer/compiler-extension assets.
- `CompileSourceInjection` for validated `contentFiles` Compile assets applicable to C#, Visual Basic, F#, or `any` language selectors.
- `NativeRuntime` for native runtime assets applicable to a restored RID.
- `ToolOrScriptPresent` for recognized tools or scripts that are present but are not treated as automatically active.

Present and active are separate facts. Active means the resolved project/TFM/RID evidence shows applicability. Direct and transitive relationships are retained, and multi-target/RID entries are evaluated independently. Static `.props` and `.targets` inspection reports observed XML primitives such as `UsingTask`, `Exec`, inline task factories, and `Import`. These are syntax facts, not proof of execution, maliciousness, or a vulnerability; comments and text containing an element name alone are not enough.

## Commands and reports

Use `scan <path>` to report facts. Use `baseline <path> --output <file>` to write an approved state. Use `check <path> --baseline <file>` to compare without rewriting the baseline. Solutions, projects, directories containing an existing `obj/project.assets.json`, and individual `project.assets.json` files are supported; `--project <path>` selects one project when a solution is ambiguous.

The command contract is:

```text
package-surface scan <path>
package-surface baseline <path> --output <baseline>
package-surface check <path> --baseline <baseline>
--format text|json|sarif
--strict-content
--compiler-api-version <version>
--project <path>
--telemetry on|off
--no-telemetry
```

Exit code `0` means a scan or baseline succeeded, or a check passed. A check difference returns exit code `1`; invalid invocation, missing restore artifacts, unsupported input, incomplete analysis, or an environment error returns exit code `2`.

Reports support `--format text|json|sarif`. JSON and SARIF report schemas are versioned; SARIF scan and baseline output contains `PS-SURFACE` note results for classified facts, while check output contains diagnostics. Exit code `0` means a successful scan/baseline or passing check, `1` means a check found a surface or policy difference, and `2` means invalid invocation, missing restore artifacts, unsupported input, incomplete analysis, or an environment error. `PS007 AnalysisIncomplete` always fails closed; it is never a clean check.

Fail-closed analysis is transactional across the complete selection: when any selected project reaches a structural budget or other `PS007` condition, text/JSON/SARIF output contains no surface entries or success-looking result, `baseline` leaves its existing file unchanged, and `check` does not emit a policy-difference result. Narrow an ambiguous or over-budget selection with `--project` and rerun. Generated top-level and nested imports are evidence only when their package/library is reachable in every applicable target graph; orphan, casing-mismatched, cross-TFM/RID, or otherwise incomplete import evidence returns `PS007`.

Restore identity validation is fail-closed before capability filtering. One canonical restore-identity index reconciles project and restore framework maps, format-4 dependency-group keys, RID-qualified target-graph keys, and package `ID/version` identities using case/effective-moniker canonicalization before reachability or capability classification. Each framework key, effective framework, and target alias must resolve to the same canonical identity; project and restore sets, dependency-group sets, and resolved target framework members must be complete in both directions. A single direct/project-reference rooted package closure is built per applicable target graph; every target package must belong to that closure, a target graph with more than one distinct version for one package ID is incoherent, and disconnected nodes or islands return `PS007` before package inventories or surface entries are produced. Dependency values are parsed as bounded restore requirements and matched against exactly one selected package version; malformed ranges, unsupported operators/forms, project-path ambiguity, and unmatched or ambiguous versions return `PS007`. The schema-aware restore boundary rejects exact duplicates, case-folded duplicates, unknown members of consumed JSON objects, and non-canonical spellings of consumed JSON/XML members at every consumed object level, including root, project/restore/framework, dependency, library, target-package, package-folder, and generated-import metadata. JSON extensions are tolerated only with the explicit `x-` prefix. Package XML strictly validates the consumed `Import`, `UsingTask`, `Exec`, and `Code` elements and attributes (`AfterTargets`, `AssemblyFile`, `BeforeTargets`, `Command`, `Condition`, `DependsOnTargets`, `Include`, `Language`, `Name`, `Project`, `TaskFactory`, `TaskName`, `ToolsVersion`, and `Type`); standard unconsumed MSBuild elements and attributes such as `Target`/`Inputs`/`Outputs`/`Returns`/`KeepDuplicateOutputs`, container `Label`, and item `Include`/`Update`/`Remove` are tolerated. Consumed exact/case/near-spelling and duplicate ambiguity remains `PS007`, while XML extensions require the explicit `urn:keelmatrix:packagesurface:extension` namespace. Restore metadata accepts SDK-emitted `fallbackFolders` only as an array of strings and `SdkAnalysisLevel` only as a string. One `NuGet.Versioning` 7.9.0 parser/comparer governs package identities, dependency ranges, package roots, imports, baseline provenance, and format-4 requirements. Conflicting metadata, effective-moniker aliases such as `net8.0` and `.NETCoreApp,Version=v8.0`, and malformed package ID/version keys also return `PS007`; valid project paths, aliases, multi-target/RID selections, and SDK-shaped package requirements remain supported.

Target-package restore groups use NuGet's exact canonical property names (`build`, `buildTransitive`, `buildMultiTargeting`, `runtime`, `compile`, `contentFiles`, `analyzers`, `native`, `resource`, and the other schema-defined groups); unknown, case-variant, null, or malformed groups fail closed as `PS007`. `packageFolders` must contain absolute folder paths with object values, and valid fallback folders remain supported. An explicit `baseline --output` path is preflighted against every reachable package-inventory file resolved from packageFolders, including global and fallback roots, direct and transitive packages, every TFM/RID, nested static imports, and all inventory categories. Lexical . and .. aliases, single and multiple hardlinks, and direct, interior, and ancestor reparse/symlink aliases are rejected with PS007 and controlled exit code 2 before any mutation. A rejection preserves existing output and input bytes; genuinely distinct outputs are accepted.

When a package contains versioned analyzer folders such as `roslyn4.0`, the consuming compiler API version must be explicit. Pass `--compiler-api-version <version>` (or provide the supported restore context) to select the highest compatible version; the tool does not guess from the review machine. Missing context or no compatible version produces `PS007`. Static nested imports are part of the analyzed surface: declared package-relative helpers are followed across arbitrary package folders and XML filenames, and strict-content fingerprints include active imported helpers.

### Baseline contract

The `compilerApiVersion` restore member is required to be a string when present; a wrong primitive is `PS007`, never an absent-value fallback.

Every admitted `project.restore`, `project.frameworks`, and `project.restore.frameworks` member is validated against its native SDK shape before graph closure: strings, booleans, string arrays, string- or object-valued maps, nullable `suppressedAdvisories` values, and `{name,version}` download-dependency objects are accepted; wrong primitives, nulls, array elements, map values, and malformed nested metadata return `PS007`, while absent members remain valid. Both string and expanded-object dependency values are supported. The contract is exercised against SDK 8.0.425, 9.0.121, and 10.0.401 assets in formats 3 and 4.

Schema version `1` requires `schemaVersion`, `toolVersion`, `strictContent`, `entries`, and `incompleteReasons`. Each entry requires `context`, `packageId`, `version`, `relationship`, `capability`, `packageRelativePath`, `present`, `active`, `incomplete`, and a null or valid 64-character SHA-256 value. Project/TFM/RID values are nullable only where the context does not apply. Package paths are package-relative and cannot be rooted or contain `..`; project identities are stable solution-relative anchors and may contain a bounded `../` sibling segment. Baseline JSON uses exact camelCase property names, named string enums, and a reject-unknown-member policy; duplicate or case-variant members, integer enums, null substitutions, null entries, incomplete markers, oversized documents, unknown schema/enum values, invalid hashes, active-but-missing assets, duplicate identities, and incomplete baselines are rejected with controlled exit `2`.

Baselines are never rewritten by `check`. Schema version `1` is the only supported version; unknown or future versions are rejected rather than upgraded. A non-strict baseline can be checked normally; `--strict-content` requests strict hashing and therefore requires a baseline that was explicitly created with `--strict-content`. A strict baseline automatically enables strict hashing even when the check command omits the flag. Recreate the baseline after reviewing a deliberate content change.

The stable diagnostics are:

| ID | Meaning |
| --- | --- |
| `PS001 NewCapability` | An active capability category is not approved by the baseline. |
| `PS002 NewActiveAsset` | A new execution-capable asset is active for a project/target. |
| `PS003 BuildSurfaceChanged` | Build props, targets, transitive, or multi-targeting surface changed. |
| `PS004 CompilerSurfaceChanged` | Compiler-extension or compile-source-injection surface changed. |
| `PS005 ContentFingerprintChanged` | Strict-content review found changed content at an approved asset path. |
| `PS006 NativeSurfaceChanged` | Native runtime capability changed. |
| `PS007 AnalysisIncomplete` | Applicability or artifact integrity could not be determined confidently. |

## Safety and privacy

The analyzer reads only packages reachable from the supplied resolved graph. It does not crawl the global package cache, access the network after restore, or execute MSBuild or package-provided build code. XML parsing prohibits DTDs and external entities. File sizes, graph size, XML depth, condition complexity, nested static-import depth, metadata inspection, output size, and hashing work are bounded. Static imports are followed only when their package-relative target and conditions are provable; the supported condition evaluator preserves written MSBuild string semantics for its bounded `TargetFramework` equality/inequality grammar, while unsupported expansions remain unknown and produce `PS007`; full MSBuild evaluation is not attempted. Traversal-looking paths, unsafe links, malformed or inconsistent restore evidence, corrupt metadata, and invalid compiler-extension PE metadata fail closed. Containment is evaluated after path canonicalization, while reparse points within the declared package/cache root remain rejected; ancestor links above that declared root do not weaken the root boundary. On macOS, only the standard root-level `/var` to `/private/var` alias is ignored; caller-controlled root aliases and links nested below /var remain rejected.

Activation telemetry is best effort and occurs only after a successful baseline creation or comparison that classified at least one real resolved `PackageReference` graph. The shared client serializes activation with exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`; its heartbeat shape is the common fields `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, and `installation_hash` plus `runtime`, `os`, `ci`, and `week`. PackageSurface requests activation only and does not request a heartbeat. PackageSurface does not add scanned package, asset, path, TFM, RID, baseline, or diagnostic data, nor dependency identities, content, or feed information. `project_hash` and `installation_hash` are the shared client's pseudonymous identifiers. Use `--telemetry off` or `--no-telemetry` to opt out. Telemetry failure cannot change analysis results. See [PRIVACY.md](PRIVACY.md) and the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the product-specific and shared boundaries; local validation sets telemetry off.

## Supported scope and limitations

The current candidate supports Windows, Linux, and macOS for SDK-style PackageReference restore outputs; hosted CI validates the command contract on all three platforms. The tool targets `net8.0`, including coherent `project.assets.json` formats 3 and 4, the formats verified by the shipping fixture and gate. Format 4 framework aliases are mapped to their effective framework; missing or mismatched v4 metadata remains incomplete analysis. Legacy project systems, `packages.config`, automatic restore, feed queries, vulnerability scanning, license analysis, malware detection, decompilation, dynamic sandboxing, and package safety judgments are outside the supported scope.

## Troubleshooting

- Missing `project.assets.json`: run the normal project restore, then rerun the command. PackageSurface does not restore for you.
- `PS007`: inspect the incomplete-analysis text, correct the restore or input, and rerun. Do not approve a baseline from incomplete material.
- Corrupt XML, package metadata, or compiler-extension files: restore a valid package and rerun; the tool will not execute the material to recover from corruption.
- Different TFMs or RIDs: run the check against the same project graph used for the baseline, or review the separate entries and explicitly create a new baseline after approval.
- More than 128 projects in a directory or solution: narrow the input with `--project`; membership validation is bounded separately so a valid selected member can still be analyzed without silently dropping projects.

## Documentation

Developer validation commands are in [`docs/DEV.md`](docs/DEV.md). The package README contains the same consumer workflow when rendered on NuGet.org.

## License

MIT. See [`LICENSE`](LICENSE).
