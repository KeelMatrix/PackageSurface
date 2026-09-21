# Platform and package validation

This file records the cache-invariant cross-platform local gate for candidate commit `051e5b5cca6cbf0f3d2773f466ee0c773e66a61f`. Validation used fresh isolated package caches for every restore. No remote CI, publish, tag, release, or visibility action was performed.

## Windows

Environment: Windows 10.0.19045, x64, .NET SDK `8.0.425`, PowerShell 7.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/windows-repeat-final/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 2435 (outer Windows wall duration)

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 77509 (gate-reported; 77848 ms outer Windows wall duration)
RESULT: PASS. The committed gate completed its own shipping restore, controlled fixture restore, format verification, Release build, no-code-execution proof, CLI/resource tests, package and symbols inspection, vulnerability audit, isolated tool installation, consumer smoke, deliberate exit-1 policy difference, exit-2 incomplete analysis, no-network scan, and telemetry/privacy checks.
```

## Linux

Environment: Ubuntu 24.04.1 under WSL2, x64, .NET SDK `8.0.425`, PowerShell 7.6.6. `DOTNET_ROOT` was unset for both runs; the gate derived it from the `dotnet` executable for the installed-tool smoke.

The two clean repeats ran at the candidate commit with different fresh run-owned package caches:

```text
COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/linux-repeat-1/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 4842 (outer WSL wall duration)

COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 308878 (gate-reported; 292336 ms outer WSL wall duration)

COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --packages <run-scratch>/linux-repeat-2/packages --no-cache --force
EXIT_CODE: 0
DURATION_MS: 4631 (outer WSL wall duration)

COMMAND: export PATH="/home/rdime/.dotnet:/home/rdime/bin:$PATH"; unset DOTNET_ROOT; pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 0
DURATION_MS: 290505 (gate-reported; 274407 ms outer WSL wall duration)
```

Both Linux repeats completed the full gate with exit `0`.

## Gate step exit matrix

| Step | Windows | Linux repeat 1 | Linux repeat 2 |
| --- | ---: | ---: | ---: |
| Shipping restore | 0 | 0 | 0 |
| Controlled restore and permanent fixture suite | 0 | 0 | 0 |
| Format verification | 0 | 0 | 0 |
| Release build | 0 | 0 | 0 |
| No-code-execution proof | 0 | 0 | 0 |
| CLI contract and resource tests | 0 | 0 | 0 |
| Package build and archive inspection | 0 | 0 | 0 |
| Dependency vulnerability audit | 0 | 0 | 0 |
| Isolated tool install | 0 | 0 | 0 |
| Installed tool version | 0 | 0 | 0 |
| Installed tool scan | 0 | 0 | 0 |
| Installed tool baseline | 0 | 0 | 0 |
| Deterministic baseline repeat | 0 | 0 | 0 |
| Installed tool passing check | 0 | 0 | 0 |
| Deliberate capability difference | 1 (expected) | 1 (expected) | 1 (expected) |
| Incomplete restore fail-closed | 2 (expected) | 2 (expected) | 2 (expected) |
| No-network scan with disabled telemetry | 0 | 0 | 0 |

## Cache invariant

The gate restores the shipping solution into a unique run-owned `shipping-packages` directory before Phase 0, and restores that cache selection before every later `--no-restore` build or pack step. Phase 0 continues to use its disposable repository-local `.phase0/packages` cache and may delete it without affecting the shipping graph. The clean repeats above passed from two different caller-provided restore caches, including the standalone gate's own shipping restore.

## Restore mapping

`NuGet.config` maps the shipping graph's public dependencies to `nuget.org`, including the transitive build and hashing packages required by SourceLink. All three isolated public restores completed without `NU1100` mapping errors.

## Package archive and consumer smoke

All three platform gate runs inspected one `KeelMatrix.PackageSurface.0.1.0.nupkg` and one `.snupkg`. The package contained the intended tool payload, README, MIT license, repository metadata, and root `icon.png`; the symbols archive contained the expected PDB. The isolated tool smoke passed `scan`, deterministic `baseline`, passing `check`, the deliberate surface-change check with exit `1`, incomplete restore with exit `2`, and the no-network scan.

The pack configuration resolves exactly one required physical icon path: the repository-root `icon.png`, packed as package-root `icon.png`. The founder-provided icon from commit `70f0d9217aaec37795ba88ae786f5dbc55a1111d` remains unchanged; its SHA-256 is `48415f8f6f3dc514169577960f526f97453a91a877cfd49736183276e9e7a214`, and the embedded package icon has the same hash. The successful icon-present pack and metadata are verified. The fail-closed missing-icon rule remains enforced by `ValidatePackageIcon` and the local gate; no founder icon file was removed or altered for this validation.

## macOS residual

No macOS environment is available on this host. macOS execution remains unverified; the intended Windows, Linux, and macOS support statement is unchanged.
