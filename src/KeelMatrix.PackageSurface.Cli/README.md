# KeelMatrix.PackageSurface

**PackageSurface tells you when a NuGet dependency starts participating in your build in a new way.** It baselines package-provided MSBuild, compiler-extension, source/content, and native capabilities and fails when that reviewed surface changes.

PackageSurface is not a malware or vulnerability scanner. A reported capability can be completely legitimate; the tool makes the capability change explicit so dependency updates can be reviewed.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

Update with `dotnet tool update --global KeelMatrix.PackageSurface`, or uninstall with `dotnet tool uninstall --global KeelMatrix.PackageSurface`.

## Five-minute workflow

Restore the project first. The tool consumes existing `project.assets.json` and generated NuGet imports; coherent SDK-style assets formats 1 through 4 are supported, with format 4 effective-framework/target-alias metadata validated fail closed. It never restores or queries a package feed.

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

Review and commit the baseline. A later dependency update that changes active build, compiler, source-injection, or native capability produces `PS001`–`PS006` and exit code `1`. Missing or incomplete restore evidence produces `PS007` and exit code `2`.

## Contract

The versioned baseline records project, TFM, RID, package, direct/transitive relationship, capability, normalized package-relative path, present/active state, and optional SHA-256 fingerprints from `--strict-content`. Use `--format text|json|sarif` and `--project <path>` when a solution needs narrowing. `check` never rewrites its input baseline. Schema version `1` rejects null entries, unsafe paths, invalid hashes, incomplete markers, oversized input, and unknown values with exit `2`.

The capability classes are `BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, `CompileSourceInjection`, `NativeRuntime`, and `ToolOrScriptPresent`. Tool/script presence is informational and is not treated as execution capability. Analyzer applicability follows the resolved PackageReference asset contract, including language-neutral/language-specific and highest applicable versioned paths; satellites and excluded analyzer assets remain inactive. Content applicability is driven by resolved `contentFiles` metadata such as `buildAction=Compile`, including preprocessed source names. Static MSBuild observations are parsed XML facts (`UsingTask`, `Exec`, inline task factories, and `Import`), not maliciousness or execution proof; bounded static package imports are followed for strict-content coverage, while dynamic conditions/imports produce `PS007`.

Schema version `1` requires `schemaVersion`, `toolVersion`, `strictContent`, `entries`, and `incompleteReasons`. Each entry requires its context, project identity, target framework when target-scoped, package identity, relationship, capability, safe package-relative path, presence/active flags, and valid incomplete/hash markers. Null entries, duplicate identities, incomplete baselines, invalid hashes, active-but-missing assets, oversized input/output, unsafe package paths, inconsistent restore evidence, and unsupported schema values return exit `2`; versions other than `1` are not upgraded. `--strict-content` requires a baseline created with strict fingerprints; a strict baseline enables strict hashing even when the check flag is omitted.

Strict-content eligibility is explicit and identical during scanning, baseline validation, and comparison: only present and active `BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, and `CompileSourceInjection` entries carry fingerprints. Inactive build assets, native runtime assets, and informational `ToolOrScriptPresent` entries remain visible as classified facts but are not hashed or compared by `PS005`.

Versioned analyzer folders require an explicit consuming compiler API version. `--compiler-api-version <version>` selects the highest compatible folder; the tool does not infer compatibility from the review machine. Missing context or no compatible version is `PS007`. The bounded static import graph includes active package-relative helpers in arbitrary folders and with nonstandard XML filenames, so strict-content comparison also covers helper-only changes. Top-level generated NuGet imports use the automatic import phase rules; nested imports retain the importing asset's capability and context.

## Safety, privacy, and scope

Analysis does not load dependency assemblies, execute MSBuild or package-provided build code, crawl the global cache, or use the network after restore. XML, file, graph, condition, static-import, metadata, output, and hashing work is bounded. Traversal-looking paths, unsafe links, malformed XML, inconsistent restore documents, corrupt metadata, and invalid PE metadata fail closed. The tool targets modern SDK-style `PackageReference` projects on `net8.0`; Windows, Linux, and macOS are intended platforms, with platform coverage outside the current machine recorded as an explicit verification assumption.

Best-effort activation telemetry uses `KeelMatrix.Telemetry` only after a successful baseline or check that classified at least one real resolved `PackageReference` graph. The shared client serializes activation with exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`; its heartbeat shape is the common fields `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, and `installation_hash` plus `runtime`, `os`, `ci`, and `week`. PackageSurface requests activation only and does not request a heartbeat. PackageSurface does not add scanned package, asset, path, TFM, RID, baseline, or diagnostic data, nor dependency identities, content, or feed information. `project_hash` and `installation_hash` are the shared client's pseudonymous identifiers. Disable it with `--telemetry off` or `--no-telemetry`; failures never affect the result. See the repository [PRIVACY.md](https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md) and the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) for the product-specific and shared boundaries.

See the [PackageSurface repository](https://github.com/KeelMatrix/PackageSurface) for the full diagnostic table, limitations, and developer validation guide.

## License

MIT. See the [LICENSE](https://github.com/KeelMatrix/PackageSurface/blob/main/LICENSE) file.
