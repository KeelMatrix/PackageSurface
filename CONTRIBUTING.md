# Contributing

## Before you begin

Install the .NET SDK selected by `global.json`. Restore with the repository `NuGet.config`; do not add hidden feeds or commit local configuration.

## Making changes

Keep changes focused on the documented CLI, baseline/report contract, classifier, tests, packaging, or developer documentation. Preserve the Phase 0 fixture corpus. Do not execute package code or MSBuild during analysis.

## Validation

Run the focused test while iterating, then the complete local gate once for the final candidate:

```powershell
dotnet restore KeelMatrix.PackageSurface.sln --configfile NuGet.config
dotnet format KeelMatrix.PackageSurface.sln --verify-no-changes --no-restore
dotnet build KeelMatrix.PackageSurface.sln --configuration Release --no-restore
pwsh -NoProfile -File scripts/run-local-gate.ps1
```

Report package and consumer results, not only source compilation. For a vulnerability, follow [`SECURITY.md`](SECURITY.md) instead of opening a public issue.
