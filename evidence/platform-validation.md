# Platform and package validation

This file records the final controlled validation for the corrected package-source mapping and cross-platform local gate. Validation used fresh isolated package caches on each platform. No remote CI, publish, tag, release, or visibility action was performed.

## Windows

Environment: Windows 10.0.19045, x64, .NET SDK `8.0.425`, PowerShell 7.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/windows-final-restore-3/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 2481

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 84931 (committed gate-reported duration; 85352 ms outer Windows wall duration)
RESULT: PASS. The committed gate completed controlled fixture restore, format verification, Release build, no-code-execution proof, CLI/resource tests, package and symbols inspection, vulnerability audit, isolated tool installation, consumer smoke, deliberate exit-1 policy difference, exit-2 incomplete analysis, no-network scan, and telemetry/privacy checks.
```

Windows gate step results:

| Step | Exit code | Duration (ms) |
| --- | ---: | ---: |
| Controlled restore and permanent fixture suite | 0 | 59927 |
| Format verification | 0 | 12314 |
| Release build | 0 | 1519 |
| No-code-execution proof | 0 | 2546 |
| CLI contract and resource tests | 0 | 1817 |
| Package build and archive inspection | 0 | 1802 |
| Dependency vulnerability audit | 0 | 2033 |
| Isolated tool install | 0 | 461 |
| Installed tool version | 0 | 456 |
| Installed tool scan | 0 | 239 |
| Installed tool baseline | 0 | 250 |
| Deterministic baseline repeat | 0 | 254 |
| Installed tool passing check | 0 | 248 |
| Deliberate capability difference | 1 (expected) | 278 |
| Incomplete restore fail-closed | 2 (expected) | 90 |
| No-network scan with disabled telemetry | 0 | 216 |

## Linux

Environment: Ubuntu 24.04.1 under WSL2, x64, .NET SDK `8.0.425`, PowerShell 7.6.6.

```text
COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/linux-qa-recheck-3/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 5347

COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 310025 (committed gate-reported duration; 296287 ms outer WSL wall duration)
RESULT: PASS. A fresh isolated Linux recheck completed the same validation sequence as Windows, including successful Linux tool installation and consumer smoke. The gate derived `DOTNET_ROOT` from the Linux `dotnet` executable because the environment did not provide it; no manual environment mutation was required by the documented command.
```

Linux gate step results:

| Step | Exit code | Duration (ms) |
| --- | ---: | ---: |
| Controlled restore and permanent fixture suite | 0 | 277260 |
| Format verification | 0 | 17843 |
| Release build | 0 | 5996 |
| No-code-execution proof | 0 | 6445 |
| CLI contract and resource tests | 0 | 1588 |
| Package build and archive inspection | 0 | 5500 |
| Dependency vulnerability audit | 0 | 2447 |
| Isolated tool install | 0 | 501 |
| Installed tool version | 0 | 54 |
| Installed tool scan | 0 | 1218 |
| Installed tool baseline | 0 | 1613 |
| Deterministic baseline repeat | 0 | 1527 |
| Installed tool passing check | 0 | 1703 |
| Deliberate capability difference | 1 (expected) | 2114 |
| Incomplete restore fail-closed | 2 (expected) | 134 |
| No-network scan with disabled telemetry | 0 | 1847 |

## Restore mapping

`NuGet.config` maps the shipping graph's public dependencies to `nuget.org`, including the transitive build and hashing packages required by SourceLink. Both isolated public restores completed without `NU1100` mapping errors.

## Package archive and consumer smoke

Both platform gates inspected one `KeelMatrix.PackageSurface.0.1.0.nupkg` and one `.snupkg`. The final Linux archive contained this intended package file set; the core-properties filename is generated package metadata:

```text
_rels/.rels
[Content_Types].xml
icon.png
KeelMatrix.PackageSurface.nuspec
LICENSE/LICENSE
package/services/metadata/core-properties/e7dfbf6870f849de9c27763ce0e582ef.psmdcp
README.md
tools/net8.0/any/DotnetToolSettings.xml
tools/net8.0/any/KeelMatrix.PackageSurface.Core.dll
tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb
tools/net8.0/any/KeelMatrix.PackageSurface.deps.json
tools/net8.0/any/KeelMatrix.PackageSurface.dll
tools/net8.0/any/KeelMatrix.PackageSurface.runtimeconfig.json
tools/net8.0/any/KeelMatrix.Telemetry.dll
```

The symbols archive `KeelMatrix.PackageSurface.0.1.0.snupkg` contained exactly this file set:

```text
_rels/.rels
[Content_Types].xml
KeelMatrix.PackageSurface.nuspec
package/services/metadata/core-properties/ad2008ac02004ab9a5c110d77e8dbf45.psmdcp
tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb
```

The core-properties filename is generated package metadata. The isolated tool smoke passed `scan`, deterministic `baseline`, passing `check`, the deliberate surface-change check with exit `1`, incomplete restore with exit `2`, and the no-network scan.

The pack configuration resolves exactly one required physical icon path: the repository-root `icon.png`, packed as package-root `icon.png`. The founder-provided icon from commit `70f0d9217aaec37795ba88ae786f5dbc55a1111d` remains unchanged; its SHA-256 is `48415f8f6f3dc514169577960f526f97453a91a877cfd49736183276e9e7a214`, and the embedded package icon has the same hash. The successful icon-present pack and metadata are verified. The fail-closed missing-icon rule remains enforced by `ValidatePackageIcon` and the local gate; no founder icon file was removed or altered for this validation.

## macOS residual

No macOS environment is available on this host. macOS execution remains unverified; the intended Windows, Linux, and macOS support statement is unchanged.
