# KeelMatrix.PackageSurface

PackageSurface shows when a resolved NuGet dependency adds or changes build capabilities, so you can review dependency updates against an approved baseline.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

Update with `dotnet tool update --global KeelMatrix.PackageSurface`, or remove it with `dotnet tool uninstall --global KeelMatrix.PackageSurface`.

## Quick start

Restore your solution first. Then inspect the current dependency surface, create and review a baseline, and compare future restores:

```powershell
dotnet restore
package-surface scan MySolution.sln
package-surface baseline MySolution.sln --output package-surface.json
package-surface check MySolution.sln --baseline package-surface.json
```

Review `package-surface.json` before committing it. The `check` command reports differences without rewriting the baseline.

## Limitations

PackageSurface supports SDK-style `PackageReference` projects with existing restore output. It does not restore packages, evaluate MSBuild, load dependency assemblies, run package code, or query a feed. A reported capability is not proof of malicious behavior or a vulnerability.

## Privacy

Analysis is local and does not upload its inputs or results. Optional activation telemetry is best effort and is requested only after a successful baseline or check that classified a real resolved dependency graph. It sends no analyzed dependency or build data. Use `--no-telemetry` to suppress it for one invocation.

## Documentation

- [Consumer command examples and product scope](https://github.com/KeelMatrix/PackageSurface/blob/main/README.md)
- [Detailed restore and baseline safety reference](https://github.com/KeelMatrix/PackageSurface/blob/main/docs/restore-and-baseline-safety.md)
- [Developer validation guide](https://github.com/KeelMatrix/PackageSurface/blob/main/docs/DEV.md)
- [Product privacy details](https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md)
- [Security reporting](https://github.com/KeelMatrix/PackageSurface/blob/main/SECURITY.md)
