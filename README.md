# KeelMatrix.PackageSurface

PackageSurface makes dependency-provided build and compiler behavior visible during dependency review. It is a .NET global tool for SDK-style `PackageReference` projects that already have restore output.

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

PackageSurface is not a malware or vulnerability scanner. A reported capability can be completely legitimate; the tool makes the capability change explicit so dependency updates can be reviewed.

## What it reports

The versioned `package-surface.json` baseline records project context, TFM, RID, package identity, direct or transitive relationship, capability category, normalized package-relative asset path, and whether the asset is present and active. `--strict-content` adds SHA-256 fingerprints for classified assets; fingerprints do not include archive timestamps or machine cache paths.

The taxonomy is:

- `BuildProps`, `BuildTargets`, `BuildTransitive`, and `BuildMultiTargeting` for package MSBuild imports.
- `CompilerExtension` for conventional analyzer/compiler-extension assets.
- `CompileSourceInjection` for `contentFiles` C# source assets.
- `NativeRuntime` for native runtime assets applicable to a restored RID.
- `ToolOrScriptPresent` for recognized tools or scripts that are present but are not treated as automatically active.

Present and active are separate facts. Active means the resolved project/TFM/RID evidence shows applicability. Direct and transitive relationships are retained, and multi-target/RID entries are evaluated independently. Static `.props` and `.targets` inspection reports observed XML primitives such as the `Using` element, `Exec`, inline factories, and imports. These are syntax facts, not proof of execution, maliciousness, or a vulnerability; a string containing an element name alone is not enough.

## Commands and reports

Use `scan <path>` to report facts. Use `baseline <path> --output <file>` to write an approved state. Use `check <path> --baseline <file>` to compare without rewriting the baseline. Solutions, projects, directories containing an existing `obj/project.assets.json`, and individual `project.assets.json` files are supported; `--project <path>` selects one project when a solution is ambiguous.

Reports support `--format text|json|sarif`. JSON and SARIF report schemas are versioned. Exit code `0` means a successful scan/baseline or passing check, `1` means a check found a surface or policy difference, and `2` means invalid invocation, missing restore artifacts, unsupported input, incomplete analysis, or an environment error. `PS007 AnalysisIncomplete` always fails closed; it is never a clean check.

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

The analyzer reads only packages reachable from the supplied resolved graph. It does not crawl the global package cache, access the network after restore, or execute MSBuild or package-provided build code. XML parsing prohibits DTDs and external entities. File sizes, graph size, XML depth, archive metadata, metadata inspection, and hashing work are bounded. Traversal-looking paths, unsafe links, malformed XML, corrupt metadata, and invalid compiler-extension PE metadata fail closed.

Activation telemetry is best effort and occurs only after a successful baseline creation or comparison for a resolved graph. It is supplied by `KeelMatrix.Telemetry` and does not contain package IDs, versions, asset names or paths, target names, TFM/RID values, MSBuild or package content, repository identity, hashes, diagnostics, baselines, or feed information. Use `--telemetry off` or `--no-telemetry` to opt out. Telemetry failure cannot change analysis results. Local validation sets telemetry off.

## Supported scope and limitations

The tool targets `net8.0` and SDK-style `PackageReference` restore outputs. Windows, Linux, and macOS are the intended platforms; this candidate has been exercised on Windows, while Linux and macOS verification remain unverified. Legacy project systems, `packages.config`, automatic restore, feed queries, vulnerability scanning, license analysis, malware detection, decompilation, dynamic sandboxing, and package safety judgments are outside the supported scope.

## Troubleshooting

- Missing `project.assets.json`: run the normal project restore, then rerun the command. PackageSurface does not restore for you.
- `PS007`: inspect the incomplete-analysis text, correct the restore or input, and rerun. Do not approve a baseline from incomplete material.
- Corrupt XML, package metadata, or compiler-extension files: restore a valid package and rerun; the tool will not execute the material to recover from corruption.
- Different TFMs or RIDs: run the check against the same project graph used for the baseline, or review the separate entries and explicitly create a new baseline after approval.

## Documentation

Developer validation commands are in [`docs/DEV.md`](docs/DEV.md). The package README contains the same consumer workflow when rendered on NuGet.org.

## License

MIT. See [`LICENSE`](LICENSE).
