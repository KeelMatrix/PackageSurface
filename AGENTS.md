# PackageSurface development guide

## Navigation

- `src/KeelMatrix.PackageSurface.Probe/` contains the non-executing resolved-graph classifier.
- `src/KeelMatrix.PackageSurface.Cli/` contains the installed tool command line, baseline schema, diff engine, reports, and package README.
- `fixtures/packages/` and `fixtures/consumer/` contain the controlled restored package corpus and consumer projects.
- `scripts/run-phase0.ps1` builds the permanent fixture corpus, restores consumers, runs the classifier, and writes evidence.
- `scripts/run-local-gate.ps1` is the validation entrypoint for restore, fixture coverage, formatting, Release build, no-execution proof, package inspection, vulnerability policy, installed-tool smoke, and consumer regressions.
- `scripts/validate-release.ps1` is the shared package-version/changelog contract used by local checks before an approved release.
- scripts/ReleaseWorkflowSourceGuard.ps1 is the source of truth for the pinned workspace-source contract on release artifact jobs that invoke tracked scripts. It checks the source before the last preceding artifact download and at every guarded script invocation. Shell redirection, Git output options, common direct file mutations, and response/file sinks invalidate that proof when they can write inside the workspace. Coverage includes curl short-option bundles and output, dump-header, stderr, trace, cookie-jar, alt-svc, HSTS, ETag-save, and write-out file destinations; wget document, log, cookie, and WARC outputs; and PowerShell Invoke-WebRequest/Invoke-RestMethod OutFile options, including aliases, module-qualified commands, and unambiguous parameter prefixes. Bash backslash and PowerShell backtick line continuations are analyzed as one command. Unsupported or dynamic destinations fail closed unless they use the immutable ${{ runner.temp }} expression, which is proven outside the workspace with a suffix that stays within the temporary directory and no earlier recognized path redirect; mutable shell RUNNER_TEMP values are never trusted. Explicit curl --config/-K inputs, nested shell or evaluator commands (including Bash/sh -c, PowerShell -Command/Invoke-Expression, encoded commands, and dynamic evaluators), and recognized symlink, junction, hard-link, or reparse-point creation fail closed. After an unsupported evaluator or path-link step, later runner-temp paths are not treated as external, even after a pinned checkout. A pinned shell git checkout or git switch --detach preserves an already-valid source state but does not restore modified tracked bytes. Only a valid unconditional root actions/checkout pinned to github.sha, with clean omitted or explicitly true and without continue-on-error, reestablishes source. A conditional same-source checkout can preserve valid state but cannot restore invalid state. Current downstream release artifact jobs validate downloaded files inline and do not invoke tracked repository scripts.
- `tools/NoExecutionProof/` checks the compiled classifier for forbidden execution, MSBuild, and network references.

The release workflow source guard also rejects interpreter stdin execution through wrapper chains and options such as `env --`, `env -S`, and GNU `timeout` or `gtimeout` (including qualified executable paths), with the timeout `--` option terminator before its duration; indentation-preserving block-scalar script loaders; and downloaded runner-temp executables invoked by absolute or relative path, after a directory change, or through PATH. Bash tracked-script paths must use forward slashes; PowerShell script paths may use backslashes. Unsafe execution or link creation keeps later runner-temp paths untrusted across shell checkout or switch. Ordinary runner-temp output remains allowed while that proof is intact.

The guard parses `exec -a` and GNU `time -o`/`--output` wrapper options, and recognizes GNU `timeout` and `gtimeout` kill-after and signal values in separate, attached, and `=` forms. Timeout parsing consumes the duration after `--` and fails closed when a required option value, duration, or command is missing. The guard rejects `env -S` values containing environment expansion, rejects variable-expanded executable names (including Bash variables that name downloaded runner-temp helpers), and fails closed when wrapper parsing reaches its depth bound. GNU time output destinations are checked as file sinks; immutable `runner.temp` expression paths with safe suffixes remain allowed while no path redirect is active.

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

The local gate is the authoritative validation entrypoint. It uses a controlled package cache, disables telemetry, checks the exact .nupkg/.snupkg set using the packable project's evaluated version (or an explicit -ExpectedVersion supplied by release validation), installs the packed tool in isolation, and exercises passing, changed-surface, incomplete-input, baseline-schema, strict-retry, and real dependency-change cases. The release workflow checks the built artifact set before upload, then checks each downloaded set inline before NuGet OIDC authentication or GitHub Release creation; those downstream jobs do not invoke tracked repository scripts. If a future downstream job needs a tracked script, it must first check out the workflow's exact commit at the workspace root. The workflow source guard accepts only an omitted action checkout path or the portable root scalars ., ./, and ./. (quoted or plain); it rejects backslash or mixed-separator paths and subdirectories. Shell redirection, Git output options, common direct file mutations, and response/file sinks invalidate source state when they can write inside the workspace. Coverage includes curl short-option bundles and output, dump-header, stderr, trace, cookie-jar, alt-svc, HSTS, ETag-save, and write-out file destinations; wget document, log, cookie, and WARC outputs; and PowerShell Invoke-WebRequest/Invoke-RestMethod OutFile options, including aliases, module-qualified commands, and unambiguous parameter prefixes. Bash backslash and PowerShell backtick line continuations are analyzed as one command. Unsupported or dynamic write destinations fail closed unless proven outside by the immutable ${{ runner.temp }} expression with a suffix that stays within the temporary directory and no earlier recognized path redirect; shell RUNNER_TEMP is mutable and is never proof of an external path. Explicit curl --config/-K inputs, nested shell or evaluator commands (including Bash/sh -c, PowerShell -Command/Invoke-Expression, encoded commands, and dynamic evaluators), and recognized symlink, junction, hard-link, or reparse-point creation fail closed. After an unsupported evaluator or path-link step, later runner-temp paths are not treated as external, even after a pinned checkout. Unredirected read-only Git inspection remains allowed. A pinned shell checkout or switch preserves valid source but cannot restore modified tracked bytes. Only an unconditional valid root checkout action pinned to the workflow SHA, with clean omitted or explicitly true and without continue-on-error, restores invalid source state.

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
