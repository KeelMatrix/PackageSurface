# Phase 0 reproducibility

The probe was run twice on the same checkout and host after the condition-classification and hygiene changes.

## Environment

- OS: Windows 10.0.19045
- Architecture and runtime identifier: x64, `win-x64`
- .NET SDK: `8.0.425` (selected by `global.json`, roll-forward `latestPatch`)
- MSBuild: `17.11.48`
- PowerShell: `7.6.6`
- Culture: `en-US`
- Package fixture source: repository-local `.phase0/feed`
- Package cache: repository-local `.phase0/packages`

## Runs

| Run | Command | Exit code | Duration |
|---|---|---:|---:|
| 1 | `pwsh -NoLogo -NoProfile -File scripts/run-phase0.ps1` | 0 | 62.680 s |
| 2 | `pwsh -NoLogo -NoProfile -File scripts/run-phase0.ps1` | 0 | 87.359 s |

The duration difference is an execution-time variation; both runs used the environment above and produced the same classification and gate outcomes.

## Fixture binary SHA-256

The following values were captured after each run. Run 1 and Run 2 matched for every path.

| Fixture path | SHA-256 |
|---|---|
| `keelmatrix.phase0.compilerextension/1.0.0/analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll` | `4d72e0e6d8b7310a8f5fa988f766b82b8620efd13593a84fe64ecc4697f5f4d0` |
| `keelmatrix.phase0.compilerextension/1.0.0/analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll` | `4d72e0e6d8b7310a8f5fa988f766b82b8620efd13593a84fe64ecc4697f5f4d0` |
| `keelmatrix.phase0.managedruntime/1.0.0/lib/net8.0/KeelMatrix.Phase0.ManagedRuntime.dll` | `a2cb12f6e44dae6831a13ba5d8b9a4be90e231e805ca2d5bd2ae3ec22e34e8b2` |
| `keelmatrix.phase0.managedruntime/1.0.0/lib/net9.0/KeelMatrix.Phase0.ManagedRuntime.Inactive.dll` | `a2cb12f6e44dae6831a13ba5d8b9a4be90e231e805ca2d5bd2ae3ec22e34e8b2` |
| `keelmatrix.phase0.nativeruntime/1.0.0/runtimes/linux-x64/native/inactive.so` | `7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2` |
| `keelmatrix.phase0.nativeruntime/1.0.0/runtimes/win-x64/native/active.dll` | `90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a` |
| `keelmatrix.phase0.nativeruntimetransitive/1.0.0/runtimes/linux-x64/native/inactive.so` | `7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2` |
| `keelmatrix.phase0.nativeruntimetransitive/1.0.0/runtimes/win-x64/native/active.dll` | `90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a` |
| `keelmatrix.phase0.ordinarylibrary/1.0.0/lib/net8.0/KeelMatrix.Phase0.OrdinaryLibrary.dll` | `4087dde1a6200a0e827f39fd79cfb71bed404ca1887f031da41f2f1cfffaf972` |

These hashes are diagnostic reproducibility evidence only. The Phase 0 verdict uses fixture presence, paths, graph relationships, target and runtime context, generated-import state, classifier state, and the safety gates; it does not depend on binary hashes. A future content-baseline check would require its own explicit contract; this evidence creates none.
