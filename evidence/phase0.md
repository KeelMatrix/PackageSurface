# Phase 0 feasibility evidence

## Verdict

**FAIL** — source ledger, classifier completeness, matrix coverage, or repository hygiene checks require correction.

The probe reads only reachable entries from `project.assets.json`; it does not restore, evaluate MSBuild, load dependency assemblies, start analysis processes, or query a feed.

## Per-category comparison

| Capability | Package | Relationship | Context | TFM/RID | Seeded | Classified | `project.assets.json` | `.nuget.g.props` | `.nuget.g.targets` | Package contents | Proven sources | Disagreement |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| BuildProps | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in SingleTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=40b734cd013c3c6ec48b6918e657735dcd64999ce7c039397cb36b2d1c5d0564) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; SHA-256=8884edf2df19dd99fc4787644731c95991d18039c59b4bba0a5e6b0b753d8b70) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildBoth | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildProps | KeelMatrix.Phase0.BuildProps | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in SingleTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in SingleTarget.csproj.nuget.g.targets) | proven (present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in MultiTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | direct | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0-windows7.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in MultiTarget.csproj.nuget.g.targets) | proven (present; SHA-256=e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1) | classifier, assets, package, generated targets |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | direct | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/- | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef) | classifier, assets, package |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | proven (imported in RidTarget.csproj.nuget.g.props) | not applicable | proven (present; SHA-256=db0af6ab0edb06f7a069ad9abfa54cdb41ab434ffebd150499a0e2153c1699d5) | classifier, assets, package, generated props |  |
| BuildProps | KeelMatrix.Phase0.BuildProps | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | missing (expected import is absent from generated props files) | not applicable | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.props: missing (expected import is absent from generated props files) |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; SHA-256=c202e068f459689b75cbeb6df7183ebf96e1da8b24cf40b46a4ccdb1563dc9fa) | classifier, assets, package, generated targets |  |
| BuildTargets | KeelMatrix.Phase0.BuildTargets | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | proven (imported in RidTarget.csproj.nuget.g.targets) | proven (present; SHA-256=cec825bcce6fd349db6074d71a5037b5dcb7b3afd9c967b2cee8589967b3817e) | classifier, assets, package, generated targets |  |
| BuildTransitive | KeelMatrix.Phase0.BuildTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompilerExtension | KeelMatrix.Phase0.CompilerExtension | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b57ce7a5f35276398b14de01fb6a391d2350aac38c2c46e0f2788dbeeed0d109) | classifier, assets, package |  |
| CompileSourceInjection | KeelMatrix.Phase0.ContentInjection | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=fc36196931f5436d6edfc6c5c868c9023ba6343777b37afbcb7c9d67da4310c3) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntime | direct | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=7684ab6de8fcf1723cfdaad2a6740e8bee7d0aa246a71bbf54446028421ac7e2) | classifier, assets, package |  |
| NativeRuntime | KeelMatrix.Phase0.NativeRuntimeTransitive | transitive | Target | net8.0/win-x64 | active | active | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=90699fe43727b321e3f9552559f93d213499ae7d5f44ed18cfa5440652e53d5a) | classifier, assets, package |  |
| ToolOrScriptPresent | KeelMatrix.Phase0.ToolScript | transitive | Target | net8.0/win-x64 | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=f9261d0928c26cd0a92454e4e34d2ce9f8eda8a3c480faaa779c515bf3fa73ef) | classifier, assets, package |  |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=e6698f2eee309f4b348053bd1d292574696ebc7ed140dfc32df96da7de37bff1) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| BuildMultiTargeting | KeelMatrix.Phase0.BuildMultiTargeting | transitive | Project | -/- | inactive | inactive | proven (reachable package/version and file in graph) | not applicable | missing (expected import is absent from generated targets files) | proven (present; SHA-256=17284cb74605517cbec8a8153505da71a685b28694b67b5ef80608cd79b1c0ea) | classifier, assets, package | .nuget.g.targets: missing (expected import is absent from generated targets files) |
| ManagedRuntime | KeelMatrix.Phase0.ManagedRuntime | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b4dcd2fa2bdc0befc026ffe61a28a9e1efcf7db92271f4af1da0c096e93186c4) | assets, package |  |
| ManagedRuntime | KeelMatrix.Phase0.ManagedRuntime | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=b4dcd2fa2bdc0befc026ffe61a28a9e1efcf7db92271f4af1da0c096e93186c4) | assets, package |  |
| OrdinaryLibrary | KeelMatrix.Phase0.OrdinaryLibrary | direct | Target | net8.0/- | absent | not applicable | proven (reachable package/version and file in graph) | not applicable | not applicable | proven (present; SHA-256=e3342b2e5955a9aa96f0cb45484a4b09eca318e08cb0c878da76a9e6c91a71bc) | assets, package |  |

## Missing classifier entries

None.

## Unexpected classifier entries

None.

## Disagreements

SingleTarget KeelMatrix.Phase0.BuildBoth build/net9.0/KeelMatrix.Phase0.BuildBoth.props: .nuget.g.props: missing (expected import is absent from generated props files)
SingleTarget KeelMatrix.Phase0.BuildBoth build/net9.0/KeelMatrix.Phase0.BuildBoth.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
SingleTarget KeelMatrix.Phase0.BuildProps build/net9.0/KeelMatrix.Phase0.BuildProps.props: .nuget.g.props: missing (expected import is absent from generated props files)
SingleTarget KeelMatrix.Phase0.BuildTargets build/net9.0/KeelMatrix.Phase0.BuildTargets.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
SingleTarget KeelMatrix.Phase0.BuildTransitive buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
SingleTarget KeelMatrix.Phase0.BuildMultiTargeting buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
SingleTarget KeelMatrix.Phase0.BuildMultiTargeting buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
MultiTarget KeelMatrix.Phase0.BuildProps build/net9.0/KeelMatrix.Phase0.BuildProps.props: .nuget.g.props: missing (expected import is absent from generated props files)
MultiTarget KeelMatrix.Phase0.BuildTargets build/net9.0/KeelMatrix.Phase0.BuildTargets.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
MultiTarget KeelMatrix.Phase0.BuildTransitive buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
MultiTarget KeelMatrix.Phase0.BuildProps build/net9.0/KeelMatrix.Phase0.BuildProps.props: .nuget.g.props: missing (expected import is absent from generated props files)
MultiTarget KeelMatrix.Phase0.BuildTargets build/net9.0/KeelMatrix.Phase0.BuildTargets.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
MultiTarget KeelMatrix.Phase0.BuildTransitive buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
MultiTarget KeelMatrix.Phase0.BuildMultiTargeting buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildProps build/net9.0/KeelMatrix.Phase0.BuildProps.props: .nuget.g.props: missing (expected import is absent from generated props files)
RidTarget KeelMatrix.Phase0.BuildTargets build/net9.0/KeelMatrix.Phase0.BuildTargets.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildTransitive buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildProps build/net9.0/KeelMatrix.Phase0.BuildProps.props: .nuget.g.props: missing (expected import is absent from generated props files)
RidTarget KeelMatrix.Phase0.BuildTargets build/net9.0/KeelMatrix.Phase0.BuildTargets.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildTransitive buildTransitive/net9.0/KeelMatrix.Phase0.BuildTransitive.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildMultiTargeting buildMultiTargeting/KeelMatrix.Phase0.BuildMultiTargeting.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
RidTarget KeelMatrix.Phase0.BuildMultiTargeting buildMultiTargeting/net9.0/KeelMatrix.Phase0.BuildMultiTargeting.targets: .nuget.g.targets: missing (expected import is absent from generated targets files)
corpus matrix is missing BuildMultiTargeting/transitive/active

## Corpus matrix

The committed combination matrix is [fixtures/corpus-matrix.md](../fixtures/corpus-matrix.md).

## Reproducibility and safety environment

`NUGET_PACKAGES=.phase0/packages`
Controlled package source: `.phase0/feed`, configured by `NuGet.config`.
The classifier receives already restored assets and generated import files. The phase script performs restore only to create fixture evidence.

## Recorded commands

```text
COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 2233
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj (in 182 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\BuildProps.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2614
EXIT_CODE: 0
OUTPUT:
  BuildProps -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildProps\bin\Release\net8.0\BuildProps.dll
  The package KeelMatrix.Phase0.BuildProps.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildProps.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1780
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj (in 131 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\BuildTargets.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2265
EXIT_CODE: 0
OUTPUT:
  BuildTargets -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTargets\bin\Release\net8.0\BuildTargets.dll
  The package KeelMatrix.Phase0.BuildTargets.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTargets.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 2046
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj (in 202 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\BuildBoth.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2794
EXIT_CODE: 0
OUTPUT:
  BuildBoth -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildBoth\bin\Release\net8.0\BuildBoth.dll
  The package KeelMatrix.Phase0.BuildBoth.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildBoth.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1757
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj (in 132 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\BuildTransitive.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2209
EXIT_CODE: 0
OUTPUT:
  BuildTransitive -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildTransitive\bin\Release\net8.0\BuildTransitive.dll
  The package KeelMatrix.Phase0.BuildTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1629
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj (in 106 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\BuildMultiTargeting.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2201
EXIT_CODE: 0
OUTPUT:
  BuildMultiTargeting -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\BuildMultiTargeting\bin\Release\net8.0\BuildMultiTargeting.dll
  The package KeelMatrix.Phase0.BuildMultiTargeting.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.BuildMultiTargeting.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1705
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj (in 145 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\CompilerExtension.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2594
EXIT_CODE: 0
OUTPUT:
  CompilerExtension -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\CompilerExtension\bin\Release\net8.0\KeelMatrix.Phase0.SourceGeneratorStyle.dll
  The package KeelMatrix.Phase0.CompilerExtension.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.CompilerExtension.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1952
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj (in 130 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\ContentInjection.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2386
EXIT_CODE: 0
OUTPUT:
  ContentInjection -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ContentInjection\bin\Release\net8.0\ContentInjection.dll
  The package KeelMatrix.Phase0.ContentInjection.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ContentInjection.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1643
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj (in 119 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\ManagedRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2751
EXIT_CODE: 0
OUTPUT:
  ManagedRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ManagedRuntime\bin\Release\net8.0\KeelMatrix.Phase0.ManagedRuntime.dll
  The package KeelMatrix.Phase0.ManagedRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ManagedRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1510
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj (in 101 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\NativeRuntime.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 2030
EXIT_CODE: 0
OUTPUT:
  NativeRuntime -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntime\bin\Release\net8.0\NativeRuntime.dll
  The package KeelMatrix.Phase0.NativeRuntime.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.NativeRuntime.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1628
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj (in 121 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\ToolScript.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1959
EXIT_CODE: 0
OUTPUT:
  ToolScript -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\ToolScript\bin\Release\net8.0\ToolScript.dll
  The package KeelMatrix.Phase0.ToolScript.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.ToolScript.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1433
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj (in 108 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\OrdinaryLibrary.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1794
EXIT_CODE: 0
OUTPUT:
  OrdinaryLibrary -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\OrdinaryLibrary\bin\Release\net8.0\KeelMatrix.Phase0.OrdinaryLibrary.dll
  The package KeelMatrix.Phase0.OrdinaryLibrary.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.OrdinaryLibrary.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1517
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj (in 133 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\NativeRuntimeTransitive.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1895
EXIT_CODE: 0
OUTPUT:
  NativeRuntimeTransitive -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\NativeRuntimeTransitive\bin\Release\net8.0\NativeRuntimeTransitive.dll
  The package KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.NativeRuntimeTransitive.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1704
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj (in 332 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\TransitiveBundle.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1543
EXIT_CODE: 0
OUTPUT:
  TransitiveBundle -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveBundle\bin\Release\net8.0\TransitiveBundle.dll
  The package KeelMatrix.Phase0.TransitiveBundle.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.TransitiveBundle.1.0.0.nupkg'.

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1745
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj (in 340 ms).

COMMAND: dotnet pack C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\TransitiveRoot.csproj --configuration Release --output C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed --no-restore
DURATION_MS: 1994
EXIT_CODE: 0
OUTPUT:
  TransitiveRoot -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\packages\TransitiveRoot\bin\Release\net8.0\TransitiveRoot.dll
  The package KeelMatrix.Phase0.TransitiveRoot.1.0.0 is missing a readme. Go to https://aka.ms/nuget/authoring-best-practices/readme to learn why package readmes are important.
  Successfully created package 'C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\feed\KeelMatrix.Phase0.TransitiveRoot.1.0.0.nupkg'.

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release
DURATION_MS: 1939
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:01.55

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 1741
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\SingleTarget.csproj (in 418 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 2590
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\MultiTarget.csproj (in 351 ms).

COMMAND: dotnet restore C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj --configfile C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface/NuGet.config --force-evaluate
DURATION_MS: 2059
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  Restored C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\RidTarget.csproj (in 338 ms).

COMMAND: dotnet build C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release
DURATION_MS: 3142
EXIT_CODE: 0
OUTPUT:
  Determining projects to restore...
  All projects are up-to-date for restore.
  KeelMatrix.PackageSurface.Probe -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.dll
  KeelMatrix.PackageSurface.Probe.Tests -> C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\tests\KeelMatrix.PackageSurface.Probe.Tests\bin\Release\net8.0\KeelMatrix.PackageSurface.Probe.Tests.dll

Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:02.70

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\SingleTarget
DURATION_MS: 1998
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
DURATION_MS: 1772
EXIT_CODE: 0
OUTPUT:
{"entries":21,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\MultiTarget
DURATION_MS: 1381
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
DURATION_MS: 1150
EXIT_CODE: 0
OUTPUT:
{"entries":32,"complete":true}

COMMAND: dotnet run --project C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\src\KeelMatrix.PackageSurface.Probe\KeelMatrix.PackageSurface.Probe.csproj --configuration Release --no-build -- C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget\obj\project.assets.json C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\fixtures\consumer\RidTarget
DURATION_MS: 1131
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
      "Sha256": "5072c2bc6d36392bf9cee1fbfb9a09d8d54d1cbe6048ae38fbe3e444892cbc6f",
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
DURATION_MS: 1180
EXIT_CODE: 0
OUTPUT:
{"entries":32,"complete":true}

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\verify-no-execution.ps1
DURATION_MS: 3325
EXIT_CODE: 0
OUTPUT:
PASS: classifier assembly has no forbidden assembly, process-start, assembly-load, MSBuild, or network references.
PASS: NUGET_PACKAGES=C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\.phase0\packages

COMMAND: pwsh -NoProfile -File C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\test-history-hygiene.ps1
DURATION_MS: 4524
EXIT_CODE: 1
OUTPUT:
Write-Error: C:\Users\rdime\Documents\Programming\vibe\NuGet\NuGet-projects\KeelMatrix.PackageSurface\KeelMatrix.PackageSurface\scripts\test-history-hygiene.ps1:44
Line |
  44 |      Write-Error ("Restricted text found: " + ($violations -join ', ') …
     |      ~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~~
     | Restricted text found: history: ef50ce647241ec89f2487b992fc998b2f74c14e0, history:
     | c2dd4e425e75b35e03330e4caf13171261dc9cce, history: 4092b65e328934d70ebacd9762e1b09191d1a855
```

## Residual uncertainty

This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; broader hostile-input and cross-platform gates belong to the next implementation phase.
