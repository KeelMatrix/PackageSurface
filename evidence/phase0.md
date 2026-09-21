# Phase 0 feasibility evidence

## Verdict

**PASS** — every deliberately seeded supported capability was classified against the restored graph, generated imports, and resolved package contents.

The prototype reads only reachable entries from `project.assets.json`; it does not restore, evaluate MSBuild, load dependency assemblies, start analysis processes, or query a feed.

## Per-category comparison

| Category | Package | Direct/transitive | TFM/RID | Seeded | Classified | `project.assets.json` | `.nuget.g.props` | `.nuget.g.targets` | Package contents | Agreeing sources | Disagreement |
|---|---|---|---|---|---|---|---|---|---|---|---|
| BuildProps | KeelMatrix.Phase0.BuildProps | direct | net8.0/- | active | active | reachable package/version in graph | imported in SingleTarget.csproj.nuget.g.props | not applicable | present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5 | classifier, assets, package, generated imports | none |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | direct | net8.0/- | active | active | reachable package/version in graph | not applicable | imported in SingleTarget.csproj.nuget.g.targets | present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa | classifier, assets, package, generated imports | none |
| BuildProps | KeelMatrix.Phase0.BuildBoth | direct | net8.0/- | active | active | reachable package/version in graph | imported in SingleTarget.csproj.nuget.g.props | not applicable | present; SHA-256=40b734cd013c3c6ec48b6918e657735dcd64999ce7c039397cb36b2d1c5d0564 | classifier, assets, package, generated imports | none |
| BuildTargets | KeelMatrix.Phase0.BuildBoth | direct | net8.0/- | active | active | reachable package/version in graph | not applicable | imported in SingleTarget.csproj.nuget.g.targets | present; SHA-256=8884edf2df19dd99fc4787644731c95991d18039c59b4bba0a5e6b0b753d8b70 | classifier, assets, package, generated imports | none |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | net8.0/- | active | active | reachable package/version in graph | not applicable | imported in SingleTarget.csproj.nuget.g.targets | present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e | classifier, assets, package, generated imports | none |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | net8.0/- | active | active | reachable package/version in graph | not applicable | imported in MultiTarget.csproj.nuget.g.targets | present; SHA-256=e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1 | classifier, assets, package, generated imports | none |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | net8.0/- | active | active | reachable package/version in graph | not applicable | not applicable | present; SHA-256=0096648893869388fb2c3fb18bf1e83c7e28255eead33d31ae3aec6a86b89c65 | classifier, assets, package, generated imports | none |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | net8.0/- | active | active | reachable package/version in graph | not applicable | not applicable | present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109 | classifier, assets, package, generated imports | none |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | net8.0/- | inactive | inactive | reachable package/version in graph | not applicable | not applicable | present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3 | classifier, assets, package, generated imports | none |
| ManagedRuntime | KeelMatrix.Phase0.ManagedRuntime | direct | net8.0/- | active | present/no supported capability | reachable library asset | not applicable | not applicable | present (verified by package file list and resolved folder) | classifier, assets, package, generated imports | none |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | net8.0/win-x64 | active | active | reachable package/version in graph | not applicable | not applicable | present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a | classifier, assets, package, generated imports | none |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | net8.0/win-x64 | inactive | inactive | reachable package/version in graph | not applicable | not applicable | present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2 | classifier, assets, package, generated imports | none |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | direct | net8.0/- | inactive | inactive | reachable package/version in graph | not applicable | not applicable | present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef | classifier, assets, package, generated imports | none |
| OrdinaryLibrary | KeelMatrix.Phase0.OrdinaryLibrary | direct | net8.0/- | active | present/no supported capability | reachable library asset | not applicable | not applicable | present (verified by package file list and resolved folder) | classifier, assets, package, generated imports | none |

Managed runtime and ordinary-library rows are intentionally reported as present/no supported capability: they are resolved graph evidence, not execution-capable classes in this probe.

## Disagreements

None.

## Reproducibility and safety environment

`NUGET_PACKAGES=C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\packages`
Controlled package source: `.phase0/feed`, configured by `NuGet.config`.
The classifier receives an already restored assets path. The phase script performs restore only to create the fixture evidence.

## Recorded commands

```text
COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1291
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj (in 93 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1752
EXIT_CODE: 0
OUTPUT:
  BuildProps -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\bin\Release\net8.0\BuildProps.dll
  The package KeelMatrix.Phase0.BuildProps.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildProps.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 936
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj (in 66 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1067
EXIT_CODE: 0
OUTPUT:
  BuildTargets -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\bin\Release\net8.0\BuildTargets.dll
  The package KeelMatrix.Phase0.BuildTargets.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTargets.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 965
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj (in 72 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1158
EXIT_CODE: 0
OUTPUT:
  BuildBoth -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\bin\Release\net8.0\BuildBoth.dll
  The package KeelMatrix.Phase0.BuildBoth.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildBoth.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1146
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj (in 85 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1401
EXIT_CODE: 0
OUTPUT:
  BuildTransitive -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\bin\Release\net8.0\BuildTransitive.dll
  The package KeelMatrix.Phase0.BuildTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 976
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj (in 65 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1197
EXIT_CODE: 0
OUTPUT:
  BuildMultiTargeting -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\bin\Release\net8.0\BuildMultiTargeting.dll
  The package KeelMatrix.Phase0.BuildMultiTargeting.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildMultiTargeting.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1186
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj (in 82 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1554
EXIT_CODE: 0
OUTPUT:
  CompilerExtension -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\bin\Release\net8.0\KeelMatrix.Phase0.SourceGeneratorStyle.dll
  The package KeelMatrix.Phase0.CompilerExtension.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.CompilerExtension.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1339
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj (in 108 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1488
EXIT_CODE: 0
OUTPUT:
  ContentInjection -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\bin\Release\net8.0\ContentInjection.dll
  The package KeelMatrix.Phase0.ContentInjection.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ContentInjection.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1072
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj (in 72 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1090
EXIT_CODE: 0
OUTPUT:
  ManagedRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\bin\Release\net8.0\KeelMatrix.Phase0.ManagedRuntime.dll
  The package KeelMatrix.Phase0.ManagedRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ManagedRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 917
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj (in 66 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1156
EXIT_CODE: 0
OUTPUT:
  NativeRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\bin\Release\net8.0\NativeRuntime.dll
  The package KeelMatrix.Phase0.NativeRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.NativeRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1004
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj (in 66 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1059
EXIT_CODE: 0
OUTPUT:
  ToolScript -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\bin\Release\net8.0\ToolScript.dll
  The package KeelMatrix.Phase0.ToolScript.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ToolScript.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 904
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj (in 71 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1050
EXIT_CODE: 0
OUTPUT:
  OrdinaryLibrary -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\bin\Release\net8.0\KeelMatrix.Phase0.OrdinaryLibrary.dll
  The package KeelMatrix.Phase0.OrdinaryLibrary.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.OrdinaryLibrary.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 962
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj (in 165 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1008
EXIT_CODE: 0
OUTPUT:
  TransitiveBundle -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\bin\Release\net8.0\TransitiveBundle.dll
  The package KeelMatrix.Phase0.TransitiveBundle.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.TransitiveBundle.1.0.0.nupkg'.

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release
DURATION_MS: 1037
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.83

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 997
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj (in 227 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 992
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj (in 150 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 901
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj (in 140 ms).

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release
DURATION_MS: 1252
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll
  KeelMatrix.PackageSurface.Probe.Tests -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.04

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget
DURATION_MS: 674
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildBoth",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildBoth.props",
      "Present": true,
      "Active": true,
      "Sha256": "40b734cd013c3c6ec48b6918e657735dcd64999ce7c039397cb36b2d1c5d0564",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildBoth",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildBoth.targets",
      "Present": true,
      "Active": true,
      "Sha256": "8884edf2df19dd99fc4787644731c95991d18039c59b4bba0a5e6b0b753d8b70",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": true,
      "Sha256": "db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": true,
      "Sha256": "c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": true,
      "Sha256": "cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "0096648893869388fb2c3fb18bf1e83c7e28255eead33d31ae3aec6a86b89c65",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/any/Active.cs",
      "Present": true,
      "Active": true,
      "Sha256": "b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/net9.0/Inactive.cs",
      "Present": true,
      "Active": false,
      "Sha256": "fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/linux-x64/native/inactive.so",
      "Present": true,
      "Active": false,
      "Sha256": "7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/win-x64/native/active.dll",
      "Present": true,
      "Active": false,
      "Sha256": "90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ToolScript",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "ToolOrScriptPresent",
      "PackageRelativePath": "tools/phase0-tool.ps1",
      "Present": true,
      "Active": false,
      "Sha256": "f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,CompilerExtension,CompileSourceInjection,ToolOrScriptPresent
DURATION_MS: 723
EXIT_CODE: 0
OUTPUT:
{"entries":11,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget
DURATION_MS: 584
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": true,
      "Sha256": "e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "0096648893869388fb2c3fb18bf1e83c7e28255eead33d31ae3aec6a86b89c65",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/any/Active.cs",
      "Present": true,
      "Active": true,
      "Sha256": "b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/net9.0/Inactive.cs",
      "Present": true,
      "Active": false,
      "Sha256": "fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": true,
      "Sha256": "e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "0096648893869388fb2c3fb18bf1e83c7e28255eead33d31ae3aec6a86b89c65",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/any/Active.cs",
      "Present": true,
      "Active": true,
      "Sha256": "b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompileSourceInjection",
      "PackageRelativePath": "contentFiles/cs/net9.0/Inactive.cs",
      "Present": true,
      "Active": false,
      "Sha256": "fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\obj\project.assets.json BuildMultiTargeting,CompilerExtension,CompileSourceInjection
DURATION_MS: 568
EXIT_CODE: 0
OUTPUT:
{"entries":8,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget
DURATION_MS: 595
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": true,
      "Sha256": "cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/linux-x64/native/inactive.so",
      "Present": true,
      "Active": false,
      "Sha256": "7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/win-x64/native/active.dll",
      "Present": true,
      "Active": false,
      "Sha256": "90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": true,
      "Sha256": "cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/linux-x64/native/inactive.so",
      "Present": true,
      "Active": false,
      "Sha256": "7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "PackageId": "KeelMatrix.Phase0.NativeRuntime",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/win-x64/native/active.dll",
      "Present": true,
      "Active": true,
      "Sha256": "90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\obj\project.assets.json BuildTransitive,NativeRuntime
DURATION_MS: 552
EXIT_CODE: 0
OUTPUT:
{"entries":6,"complete":true}

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\verify-no-execution.ps1
DURATION_MS: 1856
EXIT_CODE: 0
OUTPUT:
PASS: classifier assembly has no forbidden assembly, process-start, assembly-load, MSBuild, or network references.
PASS: NUGET_PACKAGES=C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\packages
```

## Residual uncertainty

This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; malformed XML, invalid package metadata, and unsupported assets fail closed in the classifier, but broader hostile-input and cross-platform gates belong to the next implementation phase.
