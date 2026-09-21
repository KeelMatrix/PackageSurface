# KeelMatrix.PackageSurface

**PackageSurface tells you when a NuGet dependency starts participating in your build in a new way.** It baselines package-provided MSBuild, compiler-extension, source/content, and native capabilities and fails when that reviewed surface changes.

PackageSurface is not a malware or vulnerability scanner. A reported capability can be completely legitimate; the tool makes the capability change explicit so dependency updates can be reviewed.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

Update with `dotnet tool update --global KeelMatrix.PackageSurface`, or uninstall with `dotnet tool uninstall --global KeelMatrix.PackageSurface`.

## Five-minute workflow

Restore the project first. The tool consumes existing `project.assets.json` and generated NuGet imports; it never restores or queries a package feed.

```powershell
dotnet restore
package-surface scan MySolution.sln
package-surface baseline MySolution.sln --output package-surface.json
package-surface check MySolution.sln --baseline package-surface.json
```

Review and commit the baseline. A later dependency update that changes active build, compiler, source-injection, or native capability produces `PS001`–`PS006` and exit code `1`. Missing or incomplete restore evidence produces `PS007` and exit code `2`.

## Contract

The versioned baseline records project, TFM, RID, package, direct/transitive relationship, capability, normalized package-relative path, present/active state, and optional SHA-256 fingerprints from `--strict-content`. Use `--format text|json|sarif` and `--project <path>` when a solution needs narrowing. `check` never rewrites its input baseline.

The capability classes are `BuildProps`, `BuildTargets`, `BuildTransitive`, `BuildMultiTargeting`, `CompilerExtension`, `CompileSourceInjection`, `NativeRuntime`, and `ToolOrScriptPresent`. Tool/script presence is informational and is not treated as execution capability. Static MSBuild observations are parsed XML facts, not maliciousness or execution proof; text containing `Exec` or the `Using` element name alone is not evidence.

## Safety, privacy, and scope

Analysis does not load dependency assemblies, execute MSBuild or package-provided build code, crawl the global cache, or use the network after restore. XML, archive metadata, file, graph, metadata, and hashing work is bounded. Traversal-looking paths, unsafe links, malformed XML, corrupt metadata, and invalid PE metadata fail closed. The tool targets modern SDK-style `PackageReference` projects on `net8.0`; Windows, Linux, and macOS are intended platforms, with the current repository validation covering Windows and Linux while macOS remains unverified.

Best-effort activation telemetry uses `KeelMatrix.Telemetry` only after a successful baseline or check that classified at least one real resolved `PackageReference` graph. PackageSurface does not pass analyzed dependency package identities or versions, asset names or paths, target names, TFM/RID values, MSBuild or package content, content hashes, diagnostics, baseline contents, or feed information. The shared client may emit the anonymous hashes and other fields documented in the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md). Disable it with `--telemetry off` or `--no-telemetry`; failures never affect the result. See the repository [PRIVACY.md](https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md) for the product-specific boundary.

See the [PackageSurface repository](https://github.com/KeelMatrix/PackageSurface) for the full diagnostic table, limitations, and developer validation guide.

## License

MIT. See the [LICENSE](https://github.com/KeelMatrix/PackageSurface/blob/main/LICENSE) file.
