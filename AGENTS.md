# PackageSurface development guide

## Navigation

- `src/KeelMatrix.PackageSurface.Probe/` contains the non-executing resolved-graph classifier.
- `src/KeelMatrix.PackageSurface.Cli/` contains the installed tool command line, baseline schema, diff engine, reports, and package README.
- `fixtures/packages/` and `fixtures/consumer/` contain the controlled restored package corpus and consumer projects.
- `scripts/run-phase0.ps1` builds the permanent fixture corpus, restores consumers, runs the classifier, and writes evidence.
- `scripts/run-local-gate.ps1` is the validation entrypoint for restore, fixture coverage, formatting, Release build, no-execution proof, package inspection, vulnerability policy, installed-tool smoke, and consumer regressions.
- `scripts/validate-release.ps1` is the shared package-version/changelog contract used by local checks and the tag-triggered release workflow.
- `tools/NoExecutionProof/` checks the compiled classifier for forbidden execution, MSBuild, and network references.

## Commands

```powershell
pwsh ./scripts/run-local-gate.ps1
pwsh ./scripts/test-release-contract.ps1
pwsh ./scripts/test-vulnerability-audit.ps1
dotnet build ./KeelMatrix.PackageSurface.sln --configuration Release
dotnet run --project ./tests/KeelMatrix.PackageSurface.Cli.Tests/KeelMatrix.PackageSurface.Cli.Tests.csproj --configuration Release --no-build
dotnet run --project ./tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- <project.assets.json> <capability-list>
```

The local gate is the authoritative validation entrypoint. It uses a controlled package cache, disables telemetry, checks the exact `.nupkg`/`.snupkg` set, installs the packed tool in isolation, and exercises passing, changed-surface, incomplete-input, baseline-schema, strict-retry, and real dependency-change cases.

## Invariants

- Analysis starts at an existing `project.assets.json` and inspects only reachable package versions.
- The classifier never loads package assemblies, evaluates MSBuild, starts processes, or accesses a network API.
- Present and active are separate facts; unsupported or malformed material is incomplete.
- Package-relative paths are used in evidence; machine cache roots never enter reports or baseline files.
- Direct/transitive rules and analyzer applicability are evaluated per target framework and consuming language.
- Fixture packages are test inputs, not shipping dependencies.
- The root `icon.png` is founder-owned and must not be created, copied, edited, moved, or deleted.

## Validation escalation

Run the focused console tests after classifier or CLI changes, then run `scripts/run-local-gate.ps1` before handoff. Inspect the generated package archives and installed-tool output; a successful restore alone is not proof of classification or package correctness.

## Change boundaries

Keep changes limited to the classifier, CLI/schema/report structures, fixture corpus, tests, release validation, package-content enforcement, local gate, documentation, or release workflow required by the approved product scope. Do not publish packages, create tags/releases, or trigger private GitHub Actions from local engineering work.
