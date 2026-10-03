# KeelMatrix.PackageSurface

**PackageSurface tells you when a NuGet dependency starts participating in your build in a new way.** It baselines package-provided MSBuild, compiler-extension, source/content, and native capabilities and fails when that reviewed surface changes.

PackageSurface is not a malware or vulnerability scanner. A reported capability can be completely legitimate; the tool makes the capability change explicit so dependency updates can be reviewed.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

Update with `dotnet tool update --global KeelMatrix.PackageSurface`, or uninstall with `dotnet tool uninstall --global KeelMatrix.PackageSurface`.

## Five-minute workflow

Restore the project first. The tool consumes existing `project.assets.json` and generated NuGet imports; coherent SDK-style assets formats 3 and 4 are supported and verified by the shipping fixture and gate, with one canonical identity index reconciling framework keys, effective frameworks, target aliases, target/RID keys, dependency groups, and package identities using case/effective-moniker canonicalization before filtering. Framework and dependency-group sets must be complete in both directions, and dependency values must use SDK-shaped restore grammar and match selected package versions. Format 4 effective-framework/target-alias metadata is validated fail closed. It never restores or queries a package feed.

```powershell
dotnet restore
package-surface scan MySolution.sln
package-surface baseline MySolution.sln --output package-surface.json
package-surface check MySolution.sln --baseline package-surface.json
```

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

Supported options are `--format text|json|sarif`, `--strict-content`, `--compiler-api-version <version>`, `--project <path>`, `--telemetry on|off`, and `--no-telemetry`. SARIF scan and baseline output contains `PS-SURFACE` note results for classified facts; check output contains diagnostics. Exit code `0` means success, `1` means a check found a reviewed surface or policy difference, and `2` means invalid invocation or incomplete analysis.

Exit code `0` means a scan or baseline succeeded, or a check passed. A check difference returns exit code `1`; invalid invocation, missing restore artifacts, unsupported input, incomplete analysis, or an environment error returns exit code `2`.

Fail-closed analysis is transactional across the complete selection: a `PS007` result emits no surface entries or success-looking report, `baseline` preserves its existing file, and `check` does not report a policy difference. Use `--project` to recover from an over-budget or ambiguous multi-project selection. Generated top-level and nested imports must resolve to packages reachable in every applicable target graph; orphan, case-variant, cross-TFM/RID, or incomplete import evidence is `PS007`. The same direct/project-reference rooted package closure roots every direct/transitive classification, resolved-package count, capability decision, generated-import check, and package-input preflight; disconnected target packages and dependency islands are rejected before inventory inspection.

Restore identity validation runs before capability filtering. One canonical identity index reconciles `project.frameworks`, `project.restore.frameworks`, format-4 dependency-group keys, RID-qualified target-graph keys, and package `ID/version` identities. A schema-aware restore-JSON layer runs first and rejects exact duplicates, case-folded duplicates, unknown members of consumed JSON objects, and non-canonical spellings of every consumed JSON/XML member at every consumed object level, including root/project/restore/framework, dependency, library, target-package, package-folder, and generated-import metadata. JSON extensions are tolerated only with the explicit `x-` prefix. Package XML strictly validates the consumed `Import`, `UsingTask`, `Exec`, and `Code` contract and tolerates standard unconsumed MSBuild elements and attributes, including target `Inputs`/`Outputs`/`Returns`/`KeepDuplicateOutputs`, container `Label`, and item `Include`/`Update`/`Remove`; consumed exact/case/near-spelling and duplicate ambiguity remains `PS007`. XML extensions require the explicit `urn:keelmatrix:packagesurface:extension` namespace. Restore metadata accepts SDK-emitted `fallbackFolders` only as an array of strings and `SdkAnalysisLevel` only as a string. One `NuGet.Versioning` 7.9.0 parser/comparer governs package identities, dependency ranges, package roots, imports, baseline provenance, and format-4 requirements. Each framework key, effective framework, and target alias must resolve to the same canonical identity; project and restore sets, dependency-group sets, and resolved target framework members must be complete in both directions. A target graph containing more than one distinct version for one package ID is incoherent and fails closed. Dependency values are parsed as bounded restore requirements and matched against exactly one selected package version; malformed ranges, unsupported operators/forms, project-path ambiguity, and unmatched or ambiguous versions return `PS007`. Conflicting metadata, effective-moniker aliases such as `net8.0` and `.NETCoreApp,Version=v8.0`, and malformed package ID/version keys also return `PS007`; valid project paths, aliases, multi-target/RID selections, and SDK-shaped package requirements remain supported.

Target-package restore groups use NuGet's exact canonical property names (`build`, `buildTransitive`, `buildMultiTargeting`, `runtime`, `compile`, `contentFiles`, `analyzers`, `native`, `resource`, and the other schema-defined groups); unknown, case-variant, null, or malformed groups fail closed as `PS007`. `packageFolders` must contain absolute folder paths with object values, and valid fallback folders remain supported. An explicit `baseline --output` path is preflighted against every reachable package-inventory file resolved from packageFolders, including global and fallback roots, direct and transitive packages, every TFM/RID, nested static imports, and all inventory categories. Lexical . and .. aliases, single and multiple hardlinks, and direct, interior, and ancestor reparse/symlink aliases are rejected with PS007 and controlled exit code 2 before any mutation. A rejection preserves existing output and input bytes; genuinely distinct outputs are accepted.

Review and commit the baseline. A later dependency update that changes active build, compiler, source-injection, or native capability produces `PS001`–`PS006` and exit code `1`. Missing or incomplete restore evidence produces `PS007` and exit code `2`.

## Contract

The `compilerApiVersion` restore member is required to be a string when present; a wrong primitive is `PS007`, never an absent-value fallback.

Every admitted `project.restore`, `project.frameworks`, and `project.restore.frameworks` member is validated against its native SDK shape before graph closure: strings, booleans, string arrays, string- or object-valued maps, nullable `suppressedAdvisories` values, and `{name,version}` download-dependency objects are accepted; wrong primitives, nulls, array elements, map values, and malformed nested metadata return `PS007`, while absent members remain valid. Both string and expanded-object dependency values are supported. SDK 8.0.425, 9.0.121, and 10.0.401 format-3/format-4 assets exercise this contract.

The versioned baseline records project, TFM, RID, package, direct/transitive relationship, capability, normalized package-relative path, present/active state, and optional SHA-256 fingerprints from `--strict-content`. Use `--format text|json|sarif` and `--project <path>` when a solution needs narrowing. `check` never rewrites its input baseline. Schema version `1` uses exact camelCase property names, named string enums, and a reject-unknown-member policy; duplicate or case-variant members, null entries, unsafe paths, invalid hashes, incomplete markers, oversized input, and unknown values return exit `2`.

The capability classes are `BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, `CompileSourceInjection`, `NativeRuntime`, and `ToolOrScriptPresent`. Tool/script presence is informational and is not treated as execution capability. Analyzer applicability follows the resolved PackageReference asset contract, including language-neutral/language-specific and highest applicable versioned paths; satellites and excluded analyzer assets remain inactive. Content applicability is driven by resolved `contentFiles` metadata such as `buildAction=Compile`, including preprocessed source names. Static MSBuild observations are parsed XML facts (`UsingTask`, `Exec`, inline task factories, and `Import`), not maliciousness or execution proof; bounded static package imports are followed for strict-content coverage. The bounded condition evaluator preserves written MSBuild string semantics for supported `TargetFramework` equality/inequality expressions; unsupported expansions and syntax remain unknown and produce `PS007` rather than a guessed branch result.

Filesystem-bearing `Exists(...)` guards follow the actual resolved filesystem semantics of the paths involved. Casing may be sensitive or insensitive per the resolved filesystem/volume; inaccessible or ambiguous resolution remains unknown and produces `PS007` rather than a known-true result. NuGet package ID/version identity matching remains case-insensitive and separate.

Schema version `1` requires `schemaVersion`, `toolVersion`, `strictContent`, `entries`, and `incompleteReasons`. Each entry requires its context, project identity, target framework when target-scoped, package identity, relationship, capability, safe package-relative path, presence/active flags, and valid incomplete/hash markers. Null entries, duplicate identities, incomplete baselines, invalid hashes, active-but-missing assets, oversized input/output, unsafe package paths, inconsistent restore evidence, and unsupported schema values return exit `2`; versions other than `1` are not upgraded. `--strict-content` requires a baseline created with strict fingerprints; a strict baseline enables strict hashing even when the check flag is omitted.

Strict-content eligibility is explicit and identical during scanning, baseline validation, and comparison: only present and active `BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, and `CompileSourceInjection` entries carry fingerprints. Inactive build assets, native runtime assets, and informational `ToolOrScriptPresent` entries remain visible as classified facts but are not hashed or compared by `PS005`.

Versioned analyzer folders require an explicit consuming compiler API version. `--compiler-api-version <version>` selects the highest compatible folder; the tool does not infer compatibility from the review machine. Missing context or no compatible version is `PS007`. The bounded static import graph includes active package-relative helpers in arbitrary folders and with nonstandard XML filenames, so strict-content comparison also covers helper-only changes. Top-level generated NuGet imports use the automatic import phase rules; nested imports retain the importing asset's capability and context.

## Safety, privacy, and scope

Analysis does not load dependency assemblies, execute MSBuild or package-provided build code, crawl the global cache, or use the network after restore. XML, file, graph, condition, static-import, metadata, output, and hashing work is bounded. Traversal-looking paths, unsafe links, malformed XML, inconsistent restore documents, corrupt metadata, and invalid PE metadata fail closed. Containment is evaluated after path canonicalization, with reparse points inside the declared package/cache root rejected and ancestor links above that root allowed without weakening containment. On macOS, only the standard root-level `/var` to `/private/var` alias is ignored; caller-controlled root aliases and links nested below `/var` remain rejected. The current candidate supports Windows, Linux, and macOS for SDK-style PackageReference restore outputs; hosted CI validates the command contract on all three platforms. The tool targets modern `net8.0` projects.

Repository validation has a separate history-hygiene contract. Its implementation source of truth is [`scripts/test-history-hygiene.ps1`](https://github.com/KeelMatrix/PackageSurface/blob/main/scripts/test-history-hygiene.ps1), with [`scripts/test-history-hygiene-regressions.ps1`](https://github.com/KeelMatrix/PackageSurface/blob/main/scripts/test-history-hygiene-regressions.ps1) as the regression contract. The gate scans every tracked file and reachable historical blob as strict UTF-8, UTF-16, or UTF-32 text; unsupported or binary content fails closed, except for the confirmed root `icon.png` binary allowlist entry. Per-tracked-file and per-archive-entry limits are 4 MiB; cumulative scanned history and decompressed history are each limited to 128 MiB. Incomplete or shallow history, Git read failures, task identifiers, and co-author trailers fail closed.

Best-effort activation telemetry uses `KeelMatrix.Telemetry` only after a successful baseline or check that classified at least one real resolved `PackageReference` graph. The shared client serializes activation with exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`; its heartbeat shape is the common fields `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, and `installation_hash` plus `runtime`, `os`, `ci`, and `week`. PackageSurface requests activation only and does not request a heartbeat. PackageSurface does not add scanned package, asset, path, TFM, RID, baseline, or diagnostic data, nor dependency identities, content, or feed information. `project_hash` and `installation_hash` are the shared client's pseudonymous identifiers. Disable it with `--telemetry off` or `--no-telemetry`; failures never affect the result. See the repository [PRIVACY.md](https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md) and the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the product-specific and shared boundaries.

See the [PackageSurface repository](https://github.com/KeelMatrix/PackageSurface) for the full diagnostic table, limitations, and developer validation guide.

## License

MIT. See the [LICENSE](https://github.com/KeelMatrix/PackageSurface/blob/main/LICENSE) file.
