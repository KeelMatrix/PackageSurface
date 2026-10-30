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

The command line is:

```text
package-surface scan <path> [options]
package-surface baseline <path> --output <baseline> [options]
package-surface check <path> --baseline <baseline> [options]
--format text|json|sarif       (default: text)
--strict-content
--compiler-api-version <version>
--project <path>
--no-telemetry
```

`scan` reports the current surface. `baseline` writes the reviewed state; `check` compares against it without rewriting it. `--format` defaults to `text`; `--strict-content` is off by default. Exit code `0` means success or a passing check, `1` means a check found a surface or policy difference, and `2` means the invocation or analysis failed. `PS007 AnalysisIncomplete` fails closed. The built `package-surface --help` lists the complete command syntax.

Detailed restore validation, supported XML import contexts, baseline safety, and filesystem identity rules are maintained in the [restore and baseline safety reference](docs/restore-and-baseline-safety.md).

## Safety and privacy

PackageSurface reads only resolved dependency evidence already on disk. It does not restore packages, query a feed, evaluate MSBuild, execute package code, or upload analysis inputs or results. It reports observed capabilities for the selected restore, not a malware or vulnerability verdict. `PS007` indicates incomplete analysis; do not approve a baseline from incomplete evidence. Optional activation telemetry is best effort and sends no analyzed package or build data. Use `--no-telemetry` to suppress the request. See [PRIVACY.md](PRIVACY.md) for the product data boundary.

For the detailed fail-closed rules and supported restore and baseline inputs, see the [restore and baseline safety reference](docs/restore-and-baseline-safety.md).

## Supported scope and limitations

PackageSurface supports Windows, Linux, and macOS for SDK-style PackageReference restore outputs; hosted CI validates the command contract on all three platforms. The tool targets `net8.0`, including coherent `project.assets.json` formats 3 and 4, the formats verified by the shipping fixture and gate. Format 4 framework aliases are mapped to their effective framework; missing or mismatched v4 metadata remains incomplete analysis. Legacy project systems, `packages.config`, automatic restore, feed queries, vulnerability scanning, license analysis, malware detection, decompilation, dynamic sandboxing, and package safety judgments are outside the supported scope.

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
