# PackageSurface development guide

## Navigation

- `src/KeelMatrix.PackageSurface.Probe/` contains the non-executing resolved-graph classifier.
- `src/KeelMatrix.PackageSurface.Cli/` contains the installed tool command line, baseline schema, diff engine, reports, and package README.
- `fixtures/packages/` and `fixtures/consumer/` contain the controlled restored package corpus and consumer projects.
- `scripts/run-phase0.ps1` builds the permanent fixture corpus, restores consumers, runs the classifier, and writes evidence.
- `scripts/run-local-gate.ps1` is the validation entrypoint for restore, fixture coverage, formatting, Release build, no-execution proof, package inspection, vulnerability policy, installed-tool smoke, and consumer regressions.
- `scripts/validate-release.ps1` is the shared package-version/changelog contract used by local checks before an approved release.
- `scripts/ReleaseWorkflowSourceGuard.ps1` is the source of truth for the pinned workspace-source contract on release artifact jobs that invoke tracked scripts. It checks the source before the last preceding artifact download and at every guarded script invocation. Shell redirection, Git output options, and common direct file mutations invalidate that proof when they can write inside the workspace; unsupported or dynamic destinations fail closed unless they are proven outside the workspace. Unredirected read-only Git inspection preserves source state. An unconditional valid root `actions/checkout` or a standalone root shell `git checkout`/detached `git switch` pinned to `github.sha` (or the matching SHA environment variable in an explicitly supported shell), without `continue-on-error`, restores invalid state. A conditional same-source checkout can preserve valid state but cannot restore invalid state. Current downstream release artifact jobs validate downloaded files inline and do not invoke tracked repository scripts.
- `tools/NoExecutionProof/` checks the compiled classifier for forbidden execution, MSBuild, and network references.

## Commands

```powershell
pwsh ./scripts/run-local-gate.ps1
pwsh ./scripts/test-release-contract.ps1 -SourceGuardOnly
pwsh ./scripts/test-release-contract.ps1
pwsh ./scripts/test-vulnerability-audit.ps1
dotnet build ./KeelMatrix.PackageSurface.sln --configuration Release
dotnet run --project ./tests/KeelMatrix.PackageSurface.Cli.Tests/KeelMatrix.PackageSurface.Cli.Tests.csproj --configuration Release --no-build
dotnet run --project ./tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj --configuration Release --no-build -- <project.assets.json> <capability-list>
```

The local gate is the authoritative validation entrypoint. It uses a controlled package cache, disables telemetry, checks the exact `.nupkg`/`.snupkg` set using the packable project's evaluated version (or an explicit `-ExpectedVersion` supplied by release validation), installs the packed tool in isolation, and exercises passing, changed-surface, incomplete-input, baseline-schema, strict-retry, and real dependency-change cases. The release workflow checks the built artifact set before upload, then checks each downloaded set inline before NuGet OIDC authentication or GitHub Release creation; those downstream jobs do not invoke tracked repository scripts. If a future downstream job needs a tracked script, it must first check out the workflow's exact commit at the workspace root. The workflow source guard accepts only an omitted action checkout path or the portable root scalars `.`, `./`, and `./.` (quoted or plain); it rejects backslash or mixed-separator paths and subdirectories. Shell redirection, Git output options, and common direct file mutations invalidate source state when they can write inside the workspace. Unsupported or dynamic destinations fail closed unless proven outside the workspace. Unredirected read-only Git inspection remains allowed. An unconditional valid root checkout action or standalone root Git checkout/switch pinned to the workflow SHA, without `continue-on-error`, restores invalid source state.

## Invariants

- Analysis starts at an existing `project.assets.json` and inspects only reachable package versions.
- The classifier never loads package assemblies, evaluates MSBuild, starts processes, or accesses a network API.
- Present and active are separate facts; unsupported or malformed material is incomplete.
- One canonical restore-identity index reconciles framework keys, effective frameworks, target aliases, target/RID keys, dependency groups, and package `ID/version` identities before reachability and capability filtering; both-direction set differences and incoherent or duplicate identities are incomplete. Project, target-package, and format-4 dependency values must use supported restore grammar and match selected package versions.
- Package-relative paths are used in evidence; machine cache roots never enter reports or baseline files.
- Direct/transitive rules and analyzer applicability are evaluated per target framework and consuming language.
- Fixture packages are test inputs, not shipping dependencies.
- The root `icon.png` is a protected repository asset and must not be created, copied, edited, moved, or deleted.

## Validation escalation

Run the focused console tests after classifier or CLI changes, then run `scripts/run-local-gate.ps1` before handoff. Inspect the generated package archives and installed-tool output; a successful restore alone is not proof of classification or package correctness.

## Change boundaries

Keep changes limited to the classifier, CLI/schema/report structures, fixture corpus, tests, release validation, package-content enforcement, local gate, or documentation required by the approved product scope. This public repository has hosted GitHub Actions CI; use the local gate as the authoritative validation entrypoint and hosted CI for Windows, Linux, and macOS evidence. Do not publish packages, create tags/releases, or trigger deployments from local engineering work.
