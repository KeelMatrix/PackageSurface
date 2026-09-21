# Platform and package validation

This file records the final controlled validation for the corrected package-source mapping and cross-platform local gate. Validation used fresh isolated package caches on each platform. No remote CI, publish, tag, release, or visibility action was performed.

## Windows

Environment: Windows 10.0.19045, x64, .NET SDK `8.0.425`, PowerShell 7.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/windows-final-restore-2/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 4119

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 131515 (controlled Windows wall duration)
RESULT: PASS. The committed gate completed controlled fixture restore, format verification, Release build, no-code-execution proof, CLI/resource tests, package and symbols inspection, vulnerability audit, isolated tool installation, consumer smoke, deliberate exit-1 policy difference, exit-2 incomplete analysis, no-network scan, and telemetry/privacy checks.
```

Windows gate step results:

| Step | Exit code | Duration (ms) |
| --- | ---: | ---: |
| Controlled restore and permanent fixture suite | 0 | 99172 |
| Format verification | 0 | 16180 |
| Release build | 0 | 2047 |
| No-code-execution proof | 0 | 2725 |
| CLI contract and resource tests | 0 | 2363 |
| Package build and archive inspection | 0 | 2204 |
| Dependency vulnerability audit | 0 | 2877 |
| Isolated tool install | 0 | 610 |
| Installed tool version | 0 | 441 |
| Installed tool scan | 0 | 230 |
| Installed tool baseline | 0 | 251 |
| Deterministic baseline repeat | 0 | 254 |
| Installed tool passing check | 0 | 257 |
| Deliberate capability difference | 1 (expected) | 240 |
| Incomplete restore fail-closed | 2 (expected) | 109 |
| No-network scan with disabled telemetry | 0 | 231 |

## Linux

Environment: Ubuntu 24.04.1 under WSL2, x64, .NET SDK `8.0.425`, PowerShell 7.6.6.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/linux-wsl-final/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 15175

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 686668 (committed gate-reported duration; WSL wall duration 653600 ms)
RESULT: PASS. The committed gate completed the same validation sequence as Windows, including successful Linux tool installation and consumer smoke.
```

Linux gate step results:

| Step | Exit code | Duration (ms) |
| --- | ---: | ---: |
| Controlled restore and permanent fixture suite | 0 | 552520 |
| Format verification | 0 | 45081 |
| Release build | 0 | 16307 |
| No-code-execution proof | 0 | 16390 |
| CLI contract and resource tests | 0 | 7929 |
| Package build and archive inspection | 0 | 19984 |
| Dependency vulnerability audit | 0 | 6374 |
| Isolated tool install | 0 | 1971 |
| Installed tool version | 0 | 171 |
| Installed tool scan | 0 | 2073 |
| Installed tool baseline | 0 | 3147 |
| Deterministic baseline repeat | 0 | 2611 |
| Installed tool passing check | 0 | 2533 |
| Deliberate capability difference | 1 (expected) | 2928 |
| Incomplete restore fail-closed | 2 (expected) | 299 |
| No-network scan with disabled telemetry | 0 | 3334 |

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
package/services/metadata/core-properties/0c52eceaf0924ee7a06bebd3543d1160.psmdcp
README.md
tools/net8.0/any/DotnetToolSettings.xml
tools/net8.0/any/KeelMatrix.PackageSurface.Core.dll
tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb
tools/net8.0/any/KeelMatrix.PackageSurface.deps.json
tools/net8.0/any/KeelMatrix.PackageSurface.dll
tools/net8.0/any/KeelMatrix.PackageSurface.runtimeconfig.json
tools/net8.0/any/KeelMatrix.Telemetry.dll
```

The symbols archive contained the expected PDB and package metadata. The isolated tool smoke passed `scan`, deterministic `baseline`, passing `check`, the deliberate surface-change check with exit `1`, incomplete restore with exit `2`, and the no-network scan.

The pack configuration resolves exactly one required physical icon path: the repository-root `icon.png`, packed as package-root `icon.png`. The founder-provided icon from commit `70f0d9217aaec37795ba88ae786f5dbc55a1111d` remains unchanged; its SHA-256 is `48415f8f6f3dc514169577960f526f97453a91a877cfd49736183276e9e7a214`, and the embedded package icon has the same hash. The successful icon-present pack and metadata are verified. The fail-closed missing-icon rule remains enforced by `ValidatePackageIcon` and the local gate; no founder icon file was removed or altered for this validation.

## macOS residual

No macOS environment is available on this host. macOS execution remains unverified; the intended Windows, Linux, and macOS support statement is unchanged.
