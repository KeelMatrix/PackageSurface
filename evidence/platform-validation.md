# Platform and package validation

This file records the final local validation for the current repository state. The package gate intentionally stops when the required manually supplied repository-root `icon.png` is absent.

## Windows

Environment: Windows 10.0.19045, x64, .NET SDK `8.0.425`, PowerShell 7.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --no-cache --force
EXIT_CODE: 0
DURATION_MS: 1201

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 1
DURATION_MS: 85745
RESULT: controlled restore, Phase 0, format, Release build, no-execution proof, and CLI contract tests passed. The package build stopped with: "Package icon is required. Place the repository-root icon.png before packing KeelMatrix.PackageSurface."
```

## Linux

Environment: Ubuntu 24.04 under WSL2, x64, .NET SDK `8.0.425`, PowerShell 7.

```text
COMMAND: dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config --no-cache --force
EXIT_CODE: 0
DURATION_MS: 5948

COMMAND: pwsh -NoLogo -NoProfile -File scripts/run-local-gate.ps1
EXIT_CODE: 1
DURATION_MS: 267577
RESULT: controlled restore, Phase 0, format, Release build, no-execution proof, and CLI contract tests passed. The package build stopped with: "Package icon is required. Place the repository-root icon.png before packing KeelMatrix.PackageSurface."
```

The Linux restore uses the explicit public-source mappings in `NuGet.config`, including `Microsoft.WindowsDesktop.App.Ref`; the shipping tool dependency resolves to the public `KeelMatrix.Telemetry` `0.1.0` package.

## Package icon fail-closed proof

The only required physical icon path is the repository-root `icon.png`, resolved by `src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj`. With that file absent, the following command exits `1` and emits the actionable error above:

```text
COMMAND: dotnet pack src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj --configuration Release --no-restore --no-build --output artifacts/missing-icon-proof
EXIT_CODE: 1
DURATION_MS: 626
```

The successful icon-present package archive and embedded icon hash remain unverified until the required icon is placed, committed, and pushed. macOS execution remains unverified on this host.
