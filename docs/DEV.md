# PackageSurface development

This guide describes reproducible local validation for the repository. Consumer installation and the five-minute workflow are documented in the root and package READMEs.

## Prerequisites

- .NET SDK `8.0.425`, selected by `global.json`.
- PowerShell 7 or later.
- Network access only for the first controlled restore of public build dependencies; analysis itself is offline after restore.

## Validation sequence

```powershell
dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config
dotnet format KeelMatrix.PackageSurface.sln --verify-no-changes --no-restore
dotnet build KeelMatrix.PackageSurface.sln --configuration Release --no-restore
dotnet run --project tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- fixtures/consumer/SingleTarget/obj/project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
pwsh -NoProfile -File scripts/run-local-gate.ps1
```

The gate performs the controlled fixture restore, Release build, permanent corpus checks, hostile-input checks, no-code-execution and no-network proofs, package inspection, dependency audit, isolated tool smoke, and telemetry suppression checks. Its telemetry/privacy assertion checks that the CLI passes no analyzed dependency identity or package content to the shared client; it does not claim that the shared client omits its documented anonymous hashes. The product-specific boundary is in [PRIVACY.md](../PRIVACY.md), with shared event fields and retention in the [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md). Record the exact command, duration, exit code, and result with the validation evidence for each completed candidate.

## Project roles

- `src/KeelMatrix.PackageSurface.Cli` is the only packable project and produces the `KeelMatrix.PackageSurface` .NET tool.
- `src/KeelMatrix.PackageSurface.Core` contains the non-executing graph classifier and is not packable on its own.
- `src/KeelMatrix.PackageSurface.Probe` preserves the Phase 0 executable probe.
- `fixtures`, `tests`, and `tools` are validation inputs and are non-packable.

No restore, MSBuild evaluation, dependency assembly loading, process execution, or package-feed access is performed by the classifier.
