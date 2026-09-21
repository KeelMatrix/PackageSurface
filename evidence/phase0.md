# Phase 0 feasibility evidence

## Verdict

**PASS** — the complete classifier output agrees with the restored graph, both generated import files, package contents, and the committed corpus matrix.

The probe reads only reachable entries from `project.assets.json`; it does not restore, evaluate MSBuild, load dependency assemblies, start analysis processes, or query a feed.

## Per-category comparison

| Capability | Package | Relationship | Context | TFM/RID | Seeded | Classified | `project.assets.json` | `.nuget.g.props` | `.nuget.g.targets` | Package contents | Proven sources | Disagreement |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| BuildProps | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in SingleTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in SingleTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ManagedRuntime | KeelMatrix.Phase0.ManagedRuntime | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | assets, package |  |
| ManagedRuntime | KeelMatrix.Phase0.ManagedRuntime | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | assets, package |  |
| OrdinaryLibrary | KeelMatrix.Phase0.OrdinaryLibrary | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | assets, package |  |

## Missing classifier entries

None.

## Unexpected classifier entries

None.

## Disagreements

None.

## Corpus matrix

The committed combination matrix is [fixtures/corpus-matrix.md](../fixtures/corpus-matrix.md).

## Reproducibility and safety environment

`NUGET_PACKAGES=.phase0/packages`
Controlled package source: `.phase0/feed`, configured by `NuGet.config`.
The classifier receives already restored assets and generated import files. The phase script performs restore only to create fixture evidence.

## Generated-import condition grammar

The classifier proves unconditional imports, `$(TargetFramework)` equality/inequality comparisons including empty and non-empty string values, and boolean `AND`/`OR` composition with parentheses.
It proves the standard `$(ExcludeRestorePackageImports) != 'true'` restore guard and the standard `Exists('$(NuGetPackageRoot)/<resolved-package-suffix>')` package-file guard when the import path matches the reachable asset.
Conditions on `ImportGroup` and `Import` elements, including nested groups, are combined as a conjunction and evaluated for each target framework or project context.
Conditions on arbitrary properties such as `Configuration`, unsupported `Exists(...)` expressions, unknown functions, malformed expressions, and any other clause outside this grammar are unproven, carry a specific reason, and make analysis incomplete. Incomplete analysis exits 2 from the probe and cannot be a clean result.
Raw fixture binary hashes are intentionally not recorded: compiler/packaging outputs can vary with host and SDK details. Deterministic evidence is the pinned SDK, resolved package/file presence, package-relative paths, direct/transitive relationships, target/RID context, active/inactive state, generated-import comparison, and gate verdict.

## Recorded commands

```text
COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 937
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj (in 66 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1221
EXIT_CODE: 0
OUTPUT:
  BuildProps -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\bin\Release\net8.0\BuildProps.dll
  The package KeelMatrix.Phase0.BuildProps.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildProps.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 884
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj (in 67 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1153
EXIT_CODE: 0
OUTPUT:
  BuildTargets -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\bin\Release\net8.0\BuildTargets.dll
  The package KeelMatrix.Phase0.BuildTargets.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTargets.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 905
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj (in 67 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1165
EXIT_CODE: 0
OUTPUT:
  BuildBoth -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\bin\Release\net8.0\BuildBoth.dll
  The package KeelMatrix.Phase0.BuildBoth.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildBoth.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 976
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj (in 69 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1180
EXIT_CODE: 0
OUTPUT:
  BuildTransitive -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\bin\Release\net8.0\BuildTransitive.dll
  The package KeelMatrix.Phase0.BuildTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 937
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj (in 68 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1282
EXIT_CODE: 0
OUTPUT:
  BuildMultiTargeting -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\bin\Release\net8.0\BuildMultiTargeting.dll
  The package KeelMatrix.Phase0.BuildMultiTargeting.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildMultiTargeting.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 962
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj (in 66 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1267
EXIT_CODE: 0
OUTPUT:
  CompilerExtension -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\bin\Release\net8.0\KeelMatrix.Phase0.SourceGeneratorStyle.dll
  The package KeelMatrix.Phase0.CompilerExtension.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.CompilerExtension.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 915
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj (in 67 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1284
EXIT_CODE: 0
OUTPUT:
  ContentInjection -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\bin\Release\net8.0\ContentInjection.dll
  The package KeelMatrix.Phase0.ContentInjection.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ContentInjection.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 946
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj (in 67 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1231
EXIT_CODE: 0
OUTPUT:
  ManagedRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\bin\Release\net8.0\KeelMatrix.Phase0.ManagedRuntime.dll
  The package KeelMatrix.Phase0.ManagedRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ManagedRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 955
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj (in 69 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1224
EXIT_CODE: 0
OUTPUT:
  NativeRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\bin\Release\net8.0\NativeRuntime.dll
  The package KeelMatrix.Phase0.NativeRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.NativeRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 980
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj (in 91 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1446
EXIT_CODE: 0
OUTPUT:
  ToolScript -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\bin\Release\net8.0\ToolScript.dll
  The package KeelMatrix.Phase0.ToolScript.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ToolScript.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1047
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj (in 78 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1374
EXIT_CODE: 0
OUTPUT:
  OrdinaryLibrary -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\bin\Release\net8.0\KeelMatrix.Phase0.OrdinaryLibrary.dll
  The package KeelMatrix.Phase0.OrdinaryLibrary.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.OrdinaryLibrary.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1071
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj (in 79 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1456
EXIT_CODE: 0
OUTPUT:
  NativeRuntimeTransitive -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\bin\Release\net8.0\NativeRuntimeTransitive.dll
  The package KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1328
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj (in 306 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 3578
EXIT_CODE: 0
OUTPUT:
  TransitiveBundle -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\bin\Release\net8.0\TransitiveBundle.dll
  The package KeelMatrix.Phase0.TransitiveBundle.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.TransitiveBundle.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1136
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj (in 211 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 3260
EXIT_CODE: 0
OUTPUT:
  TransitiveRoot -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\bin\Release\net8.0\TransitiveRoot.dll
  The package KeelMatrix.Phase0.TransitiveRoot.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.TransitiveRoot.1.0.0.nupkg'.

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release
DURATION_MS: 2223
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.98

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1070
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj (in 218 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1168
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj (in 164 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1016
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj (in 176 ms).

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release
DURATION_MS: 1913
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll
  KeelMatrix.PackageSurface.Probe.Tests -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.69

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget
DURATION_MS: 918
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildBoth",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildBoth.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildBoth",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildBoth.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "direct",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": false,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
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
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": false,
      "Sha256": "e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 1304
EXIT_CODE: 0
OUTPUT:
condition-case unconditional import: complete=True; active=net8.0
condition-case TFM equality: complete=True; active=net8.0
condition-case TFM inequality: complete=True; active=
condition-case empty string equality: complete=True; active=
condition-case non-empty string inequality: complete=True; active=net8.0
condition-case AND composition: complete=True; active=net8.0
condition-case repeated TFM comparisons: complete=True; active=net8.0
condition-case OR composition: complete=True; active=net8.0
condition-case restore guard: complete=True; active=net8.0
condition-case standard Exists guard: complete=True; active=net8.0
condition-case ImportGroup and Import conditions: complete=True; active=net8.0
condition-case nested groups: complete=True; active=net8.0
condition-case arbitrary property: complete=False; active=
condition-case additional arbitrary clause: complete=False; active=
condition-case nested arbitrary condition: complete=False; active=
condition-case unproven Exists: complete=False; active=
condition-case empty TFM project context: complete=True; active=True
condition-exit incomplete analysis: 2
{"entries":21,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget
DURATION_MS: 677
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": false,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ToolScript",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "ToolOrScriptPresent",
      "PackageRelativePath": "tools/phase0-tool.ps1",
      "Present": true,
      "Active": false,
      "Sha256": "f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": true,
      "Sha256": "db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": true,
      "Sha256": "c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": false,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ToolScript",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "ToolOrScriptPresent",
      "PackageRelativePath": "tools/phase0-tool.ps1",
      "Present": true,
      "Active": false,
      "Sha256": "f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
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
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 978
EXIT_CODE: 0
OUTPUT:
condition-case unconditional import: complete=True; active=net8.0,net8.0-windows7.0
condition-case TFM equality: complete=True; active=net8.0
condition-case TFM inequality: complete=True; active=net8.0-windows7.0
condition-case empty string equality: complete=True; active=
condition-case non-empty string inequality: complete=True; active=net8.0,net8.0-windows7.0
condition-case AND composition: complete=True; active=net8.0
condition-case repeated TFM comparisons: complete=True; active=net8.0
condition-case OR composition: complete=True; active=net8.0,net8.0-windows7.0
condition-case restore guard: complete=True; active=net8.0,net8.0-windows7.0
condition-case standard Exists guard: complete=True; active=net8.0,net8.0-windows7.0
condition-case ImportGroup and Import conditions: complete=True; active=net8.0
condition-case nested groups: complete=True; active=net8.0
condition-case arbitrary property: complete=False; active=
condition-case additional arbitrary clause: complete=False; active=
condition-case nested arbitrary condition: complete=False; active=
condition-case unproven Exists: complete=False; active=
condition-case empty TFM project context: complete=True; active=True
condition-exit incomplete analysis: 2
{"entries":32,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget
DURATION_MS: 686
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": false,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "Context": "Target",
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
      "Context": "Target",
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
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ToolScript",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "ToolOrScriptPresent",
      "PackageRelativePath": "tools/phase0-tool.ps1",
      "Present": true,
      "Active": false,
      "Sha256": "f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildProps",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildProps",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildProps.props",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTargets",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTargets",
      "PackageRelativePath": "build/net9.0/KeelMatrix.Phase0.BuildTargets.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
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
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/cs/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": true,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/net9.0/KeelMatrix.Phase0.SourceGeneratorStyle.dll",
      "Present": true,
      "Active": false,
      "Sha256": "dc8c76124fa770e0be4c1255add21b51fd8c7a03b82136b55e277e02b241120e",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ContentInjection",
      "Version": "1.0.0",
      "Relationship": "transitive",
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
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
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
      "Context": "Target",
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
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
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
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.NativeRuntimeTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "NativeRuntime",
      "PackageRelativePath": "runtimes/win-x64/native/active.dll",
      "Present": true,
      "Active": true,
      "Sha256": "90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.ToolScript",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "ToolOrScriptPresent",
      "PackageRelativePath": "tools/phase0-tool.ps1",
      "Present": true,
      "Active": false,
      "Sha256": "f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": false,
      "Sha256": "e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1",
      "Incomplete": false,
      "IncompleteReason": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets",
      "Present": true,
      "Active": false,
      "Sha256": "17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea",
      "Incomplete": false,
      "IncompleteReason": null
    }
  ],
  "IncompleteReasons": [],
  "IsComplete": true
}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 1012
EXIT_CODE: 0
OUTPUT:
condition-case unconditional import: complete=True; active=net8.0
condition-case TFM equality: complete=True; active=net8.0
condition-case TFM inequality: complete=True; active=
condition-case empty string equality: complete=True; active=
condition-case non-empty string inequality: complete=True; active=net8.0
condition-case AND composition: complete=True; active=net8.0
condition-case repeated TFM comparisons: complete=True; active=net8.0
condition-case OR composition: complete=True; active=net8.0
condition-case restore guard: complete=True; active=net8.0
condition-case standard Exists guard: complete=True; active=net8.0
condition-case ImportGroup and Import conditions: complete=True; active=net8.0
condition-case nested groups: complete=True; active=net8.0
condition-case arbitrary property: complete=False; active=
condition-case additional arbitrary clause: complete=False; active=
condition-case nested arbitrary condition: complete=False; active=
condition-case unproven Exists: complete=False; active=
condition-case empty TFM project context: complete=True; active=True
condition-exit incomplete analysis: 2
{"entries":32,"complete":true}

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\verify-no-execution.ps1
DURATION_MS: 2387
EXIT_CODE: 0
OUTPUT:
PASS: classifier assembly has no forbidden assembly, process-start, assembly-load, MSBuild, or network references.
PASS: NUGET_PACKAGES=C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\packages

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\test-history-hygiene-regressions.ps1
DURATION_MS: 8380
EXIT_CODE: 0
OUTPUT:
CASE: git-call-failure
EXIT_CODE: 1
DURATION_MS: 717
PASS: git-call-failure rejected with the expected diagnostic class 'git rev-parse'.
CASE: empty-tracked-output
EXIT_CODE: 1
DURATION_MS: 644
PASS: empty-tracked-output rejected with the expected diagnostic class 'returned no required output'.
CASE: empty-history-output
EXIT_CODE: 1
DURATION_MS: 715
PASS: empty-history-output rejected with the expected diagnostic class 'returned no required output'.
CASE: incomplete-history-output
EXIT_CODE: 1
DURATION_MS: 711
PASS: incomplete-history-output rejected with the expected diagnostic class 'does not include HEAD'.
CASE: git-grep-failure
EXIT_CODE: 1
DURATION_MS: 701
PASS: git-grep-failure rejected with the expected diagnostic class 'git grep'.
CASE: empty-log-output
EXIT_CODE: 1
DURATION_MS: 713
PASS: empty-log-output rejected with the expected diagnostic class 'git log --all'.
CASE: git-log-failure
EXIT_CODE: 1
DURATION_MS: 697
PASS: git-log-failure rejected with the expected diagnostic class 'git log'.
CASE: shallow-repository
EXIT_CODE: 1
DURATION_MS: 719
PASS: shallow-repository rejected with the expected diagnostic class 'repository is shallow'.
CASE: restricted-marker
EXIT_CODE: 1
DURATION_MS: 904
PASS: restricted-marker rejected with the expected diagnostic class 'Restricted text found'.
PASS: hygiene gate rejects command failure, shallow history, and a tracked restricted marker in disposable repositories.

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\test-history-hygiene.ps1
DURATION_MS: 7753
EXIT_CODE: 0
OUTPUT:
PASS: tracked material and complete non-shallow history contain no restricted developer-coordination markers.
```

## Residual uncertainty

This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; broader hostile-input and cross-platform gates belong to the next implementation phase.
