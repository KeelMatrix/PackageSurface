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
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in SingleTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
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
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package, generated props |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
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
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; raw binary hash intentionally not recorded in Phase 0 evidence) | classifier, assets, package |  |
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
COMMAND: dotnet restore .\fixtures\packages\BuildProps\BuildProps.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 1037
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\BuildProps\BuildProps.csproj (in 70 ms).

COMMAND: dotnet pack .\fixtures\packages\BuildProps\BuildProps.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1561
EXIT_CODE: 0
OUTPUT:
  BuildProps -> .\fixtures\packages\BuildProps\bin\Release\net8.0\BuildProps.dll
  The package KeelMatrix.Phase0.BuildProps.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.BuildProps.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\BuildTargets\BuildTargets.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 1141
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\BuildTargets\BuildTargets.csproj (in 81 ms).

COMMAND: dotnet pack .\fixtures\packages\BuildTargets\BuildTargets.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1124
EXIT_CODE: 0
OUTPUT:
  BuildTargets -> .\fixtures\packages\BuildTargets\bin\Release\net8.0\BuildTargets.dll
  The package KeelMatrix.Phase0.BuildTargets.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.BuildTargets.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\BuildBoth\BuildBoth.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 959
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\BuildBoth\BuildBoth.csproj (in 79 ms).

COMMAND: dotnet pack .\fixtures\packages\BuildBoth\BuildBoth.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1266
EXIT_CODE: 0
OUTPUT:
  BuildBoth -> .\fixtures\packages\BuildBoth\bin\Release\net8.0\BuildBoth.dll
  The package KeelMatrix.Phase0.BuildBoth.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.BuildBoth.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 879
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\BuildTransitive\BuildTransitive.csproj (in 64 ms).

COMMAND: dotnet pack .\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1026
EXIT_CODE: 0
OUTPUT:
  BuildTransitive -> .\fixtures\packages\BuildTransitive\bin\Release\net8.0\BuildTransitive.dll
  The package KeelMatrix.Phase0.BuildTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.BuildTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 816
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj (in 57 ms).

COMMAND: dotnet pack .\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 999
EXIT_CODE: 0
OUTPUT:
  BuildMultiTargeting -> .\fixtures\packages\BuildMultiTargeting\bin\Release\net8.0\BuildMultiTargeting.dll
  The package KeelMatrix.Phase0.BuildMultiTargeting.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.BuildMultiTargeting.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 942
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\CompilerExtension\CompilerExtension.csproj (in 61 ms).

COMMAND: dotnet pack .\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 965
EXIT_CODE: 0
OUTPUT:
  CompilerExtension -> .\fixtures\packages\CompilerExtension\bin\Release\net8.0\KeelMatrix.Phase0.SourceGeneratorStyle.dll
  The package KeelMatrix.Phase0.CompilerExtension.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.CompilerExtension.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\ContentInjection\ContentInjection.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 776
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\ContentInjection\ContentInjection.csproj (in 56 ms).

COMMAND: dotnet pack .\fixtures\packages\ContentInjection\ContentInjection.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1095
EXIT_CODE: 0
OUTPUT:
  ContentInjection -> .\fixtures\packages\ContentInjection\bin\Release\net8.0\ContentInjection.dll
  The package KeelMatrix.Phase0.ContentInjection.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.ContentInjection.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 792
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj (in 58 ms).

COMMAND: dotnet pack .\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 943
EXIT_CODE: 0
OUTPUT:
  ManagedRuntime -> .\fixtures\packages\ManagedRuntime\bin\Release\net8.0\KeelMatrix.Phase0.ManagedRuntime.dll
  The package KeelMatrix.Phase0.ManagedRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.ManagedRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 900
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\NativeRuntime\NativeRuntime.csproj (in 87 ms).

COMMAND: dotnet pack .\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1157
EXIT_CODE: 0
OUTPUT:
  NativeRuntime -> .\fixtures\packages\NativeRuntime\bin\Release\net8.0\NativeRuntime.dll
  The package KeelMatrix.Phase0.NativeRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.NativeRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\ToolScript\ToolScript.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 784
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\ToolScript\ToolScript.csproj (in 57 ms).

COMMAND: dotnet pack .\fixtures\packages\ToolScript\ToolScript.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1069
EXIT_CODE: 0
OUTPUT:
  ToolScript -> .\fixtures\packages\ToolScript\bin\Release\net8.0\ToolScript.dll
  The package KeelMatrix.Phase0.ToolScript.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.ToolScript.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 924
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj (in 64 ms).

COMMAND: dotnet pack .\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1183
EXIT_CODE: 0
OUTPUT:
  OrdinaryLibrary -> .\fixtures\packages\OrdinaryLibrary\bin\Release\net8.0\KeelMatrix.Phase0.OrdinaryLibrary.dll
  The package KeelMatrix.Phase0.OrdinaryLibrary.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.OrdinaryLibrary.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 779
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj (in 56 ms).

COMMAND: dotnet pack .\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 895
EXIT_CODE: 0
OUTPUT:
  NativeRuntimeTransitive -> .\fixtures\packages\NativeRuntimeTransitive\bin\Release\net8.0\NativeRuntimeTransitive.dll
  The package KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 1021
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj (in 303 ms).

COMMAND: dotnet pack .\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 1005
EXIT_CODE: 0
OUTPUT:
  TransitiveBundle -> .\fixtures\packages\TransitiveBundle\bin\Release\net8.0\TransitiveBundle.dll
  The package KeelMatrix.Phase0.TransitiveBundle.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.TransitiveBundle.1.0.0.nupkg'.

COMMAND: dotnet restore .\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 988
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj (in 245 ms).

COMMAND: dotnet pack .\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configuration Release --output .\.phase0\feed --no-restore
DURATION_MS: 932
EXIT_CODE: 0
OUTPUT:
  TransitiveRoot -> .\fixtures\packages\TransitiveRoot\bin\Release\net8.0\TransitiveRoot.dll
  The package KeelMatrix.Phase0.TransitiveRoot.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package '.\.phase0\feed\KeelMatrix.Phase0.TransitiveRoot.1.0.0.nupkg'.

COMMAND: dotnet build .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release
DURATION_MS: 1135
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Core -> .\src\KeelMatrix.PackageSurface.Core\bin\Release\net8.0\KeelMatrix.PackageSurface.Core.dll
  KeelMatrix.PackageSurface.Probe -> .\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.95

COMMAND: dotnet restore .\fixtures\consumer\SingleTarget\SingleTarget.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 1385
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\consumer\SingleTarget\SingleTarget.csproj (in 353 ms).

COMMAND: dotnet restore .\fixtures\consumer\MultiTarget\MultiTarget.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 1131
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\consumer\MultiTarget\MultiTarget.csproj (in 219 ms).

COMMAND: dotnet restore .\fixtures\consumer\RidTarget\RidTarget.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 921
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\consumer\RidTarget\RidTarget.csproj (in 232 ms).

COMMAND: dotnet restore .\fixtures\consumer\AnalyzerExcluded\AnalyzerExcluded.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 858
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\consumer\AnalyzerExcluded\AnalyzerExcluded.csproj (in 179 ms).

COMMAND: dotnet restore .\fixtures\consumer\AnalyzerExcludedTransitive\AnalyzerExcludedTransitive.csproj --configfile ./NuGet.config --force-evaluate
DURATION_MS: 903
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored .\fixtures\consumer\AnalyzerExcludedTransitive\AnalyzerExcludedTransitive.csproj (in 230 ms).

COMMAND: dotnet build .\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release
DURATION_MS: 1533
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Core -> .\src\KeelMatrix.PackageSurface.Core\bin\Release\net8.0\KeelMatrix.PackageSurface.Core.dll
  KeelMatrix.PackageSurface.Probe -> .\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll
  KeelMatrix.PackageSurface.Probe.Tests -> .\tests\KeelMatrix.PackageSurface.Probe.Tests\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.29

COMMAND: dotnet run --project .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- .\fixtures\consumer\SingleTarget\obj\project.assets.json .\fixtures\consumer\SingleTarget
DURATION_MS: 603
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "3f7edad522822d363c0a144afe90aa919c763277dbe79a1a0f752bf3e64ca271",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    }
  ],
  "IncompleteReasons": [],
  "ResolvedPackageCount": 14,
  "IsComplete": true
}

COMMAND: dotnet run --project .\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- .\fixtures\consumer\SingleTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 972
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
generated-import filename mismatch: incomplete
{"entries":26,"complete":true}

COMMAND: dotnet run --project .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- .\fixtures\consumer\MultiTarget\obj\project.assets.json .\fixtures\consumer\MultiTarget
DURATION_MS: 585
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "3f7edad522822d363c0a144afe90aa919c763277dbe79a1a0f752bf3e64ca271",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0-windows7.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": true,
      "Sha256": "17866f589de403509af1f243bc10cbb159d2bceaec030065a08267ac369cf60f",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    }
  ],
  "IncompleteReasons": [],
  "ResolvedPackageCount": 12,
  "IsComplete": true
}

COMMAND: dotnet run --project .\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- .\fixtures\consumer\MultiTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 1134
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
generated-import filename mismatch: incomplete
{"entries":40,"complete":true}

COMMAND: dotnet run --project .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- .\fixtures\consumer\RidTarget\obj\project.assets.json .\fixtures\consumer\RidTarget
DURATION_MS: 624
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "3f7edad522822d363c0a144afe90aa919c763277dbe79a1a0f752bf3e64ca271",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "3f7edad522822d363c0a144afe90aa919c763277dbe79a1a0f752bf3e64ca271",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": "win-x64",
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    }
  ],
  "IncompleteReasons": [],
  "ResolvedPackageCount": 11,
  "IsComplete": true
}

COMMAND: dotnet run --project .\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- .\fixtures\consumer\RidTarget\obj\project.assets.json BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent
DURATION_MS: 1238
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
generated-import filename mismatch: incomplete
{"entries":40,"complete":true}

COMMAND: dotnet run --project .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- .\fixtures\consumer\AnalyzerExcluded\obj\project.assets.json .\fixtures\consumer\AnalyzerExcluded
DURATION_MS: 555
EXIT_CODE: 0
OUTPUT:
{
  "Entries": [
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
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "direct",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    }
  ],
  "IncompleteReasons": [],
  "ResolvedPackageCount": 1,
  "IsComplete": true
}

COMMAND: dotnet run --project .\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- .\fixtures\consumer\AnalyzerExcludedTransitive\obj\project.assets.json .\fixtures\consumer\AnalyzerExcludedTransitive
DURATION_MS: 579
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
      "Sha256": "887735e995b1284b3cfce961211eeddc3415e40dff3e7afc3ecfd00a4a3e21be",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "InlineTaskFactory",
        "UsingTask"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": "28fe6b546f2543b8f7fc6f5d7898396d6200af2bee00c5db9c81e321e08732ce",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": [
        "Exec",
        "Import"
      ]
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": true,
      "Sha256": "f7955d35d163f8e5514c83d30f8dbca8a4474fd128e8908eb0e54a83169176c6",
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.BuildTransitive",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildTransitive",
      "PackageRelativePath": "buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": []
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
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": "net8.0",
      "RuntimeIdentifier": null,
      "Context": "Target",
      "PackageId": "KeelMatrix.Phase0.CompilerExtension",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "CompilerExtension",
      "PackageRelativePath": "analyzers/dotnet/vb/KeelMatrix.Phase0.VisualBasicOnly.dll",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    },
    {
      "TargetFramework": null,
      "RuntimeIdentifier": null,
      "Context": "Project",
      "PackageId": "KeelMatrix.Phase0.BuildMultiTargeting",
      "Version": "1.0.0",
      "Relationship": "transitive",
      "Capability": "BuildMultiTargeting",
      "PackageRelativePath": "buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.props",
      "Present": true,
      "Active": false,
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
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
      "Sha256": null,
      "Incomplete": false,
      "IncompleteReason": null,
      "Project": null,
      "ObservedPrimitives": null
    }
  ],
  "IncompleteReasons": [],
  "ResolvedPackageCount": 11,
  "IsComplete": true
}

COMMAND: pwsh -NoProfile -File .\scripts\verify-no-execution.ps1
DURATION_MS: 1613
EXIT_CODE: 0
OUTPUT:
PASS: classifier assembly has no forbidden assembly, process-start, assembly-load, MSBuild, or network references.
PASS: NUGET_PACKAGES=.\.phase0\packages

COMMAND: pwsh -NoProfile -File .\scripts\test-history-hygiene-regressions.ps1
DURATION_MS: 6188
EXIT_CODE: 0
OUTPUT:
CASE: git-call-failure
EXIT_CODE: 1
DURATION_MS: 452
PASS: git-call-failure rejected with the expected diagnostic class 'git rev-parse'.
CASE: empty-tracked-output
EXIT_CODE: 1
DURATION_MS: 398
PASS: empty-tracked-output rejected with the expected diagnostic class 'returned no required output'.
CASE: empty-history-output
EXIT_CODE: 1
DURATION_MS: 407
PASS: empty-history-output rejected with the expected diagnostic class 'returned no required output'.
CASE: incomplete-history-output
EXIT_CODE: 1
DURATION_MS: 400
PASS: incomplete-history-output rejected with the expected diagnostic class 'does not include HEAD'.
CASE: git-grep-failure
EXIT_CODE: 1
DURATION_MS: 493
PASS: git-grep-failure rejected with the expected diagnostic class 'git grep'.
CASE: empty-log-output
EXIT_CODE: 1
DURATION_MS: 530
PASS: empty-log-output rejected with the expected diagnostic class 'git log --all'.
CASE: git-log-failure
EXIT_CODE: 1
DURATION_MS: 670
PASS: git-log-failure rejected with the expected diagnostic class 'git log'.
CASE: shallow-repository
EXIT_CODE: 1
DURATION_MS: 555
PASS: shallow-repository rejected with the expected diagnostic class 'repository is shallow'.
CASE: restricted-marker
EXIT_CODE: 1
DURATION_MS: 704
PASS: restricted-marker rejected with the expected diagnostic class 'Restricted text found'.
PASS: hygiene gate rejects command failure, shallow history, and a tracked restricted marker in disposable repositories.

COMMAND: pwsh -NoProfile -File .\scripts\test-history-hygiene.ps1
DURATION_MS: 5007
EXIT_CODE: 0
OUTPUT:
PASS: tracked material and complete non-shallow history contain no restricted developer-coordination markers.
```

## Residual uncertainty

This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; broader hostile-input and cross-platform gates belong to the next implementation phase.
