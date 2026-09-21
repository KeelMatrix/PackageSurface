# PackageSurface development guide

## Navigation

- `src/KeelMatrix.PackageSurface.Probe/` contains the non-executing resolved-graph classifier.
- `fixtures/packages/` contains the controlled package corpus used by Phase 0.
- `fixtures/consumer/` contains restored single-target, multi-target, and RID-specific projects.
- `scripts/run-phase0.ps1` builds the corpus, restores consumers, runs the probe, and writes evidence.
- `tools/NoExecutionProof/` checks the compiled classifier for forbidden execution, MSBuild, and network references.
- `evidence/phase0.md` is the current per-category comparison and verdict.

## Commands

```powershell
$env:NUGET_PACKAGES = "$PWD\.phase0\packages"
pwsh ./scripts/run-phase0.ps1
dotnet test ./tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj
```

The phase script is the authoritative validation entrypoint. It uses only the local feed produced under `.phase0/feed` and never invokes restore from the classifier.

## Invariants

- Analysis starts at an existing `project.assets.json` and inspects only its reachable package versions.
- The classifier never loads package assemblies, evaluates MSBuild, starts processes, or accesses a network API.
- Present and active are separate facts; unsupported or malformed material is incomplete.
- Package-relative paths are used in evidence; machine cache roots never enter reports.
- Fixture packages are test inputs, not shipping dependencies.

## Validation escalation

Run the phase script after classifier or fixture changes. Use the focused probe tests first, then inspect the generated evidence and package archives. Do not treat a successful restore as proof that classification is correct.

## Change boundaries

Keep changes limited to the classifier, fixture corpus, phase script, tests, or evidence needed to prove Phase 0. Do not add release automation, publishing configuration, or unrelated product features.
