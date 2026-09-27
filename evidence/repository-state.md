# Repository state verification

This document defines the repository-state checks used at candidate handoff. The handoff records the exact commit,
remote ref, command output, and residual platform evidence for that candidate; this file is not a substitute for that
candidate-specific record.

## Canonical repository and hosted validation

The canonical repository is the public `KeelMatrix/PackageSurface` GitHub repository. Hosted validation is defined by:

- `.github/workflows/ci.yml`: push to `main`, pull requests, and manual exact-SHA dispatch on `windows-latest`,
  `ubuntu-latest`, and `macos-latest`.
- `.github/workflows/release.yml`: tag-only `v*.*.*` validation, publication, and GitHub Release chain.
- `scripts/validate-release.ps1`: the shared version and changelog contract used by candidate checks and the release
  workflow.

Both workflows disable telemetry and use least-privilege job permissions. The local gate remains the authoritative
pre-handoff validation; hosted CI supplies independent platform evidence for the exact pushed commit.

## Required checks

```text
COMMAND: git remote -v
EXPECTED: the canonical PackageSurface remote is the only reviewed remote.

COMMAND: git status --short --branch
EXPECTED: the candidate checkout is clean and equal to its reviewed remote branch.

COMMAND: git ls-tree -r --name-only HEAD | Select-String "^\.github/workflows/"
EXPECTED:
    .github/workflows/ci.yml
    .github/workflows/release.yml

COMMAND: git tag | Measure-Object | Select-Object -ExpandProperty Count
EXPECTED: 0 before an approved release tag exists.

COMMAND: pwsh -NoLogo -NoProfile -File scripts/test-release-contract.ps1
EXPECTED: PASS for the workflow shape and shared fail-closed release-contract regression cases.
```

The exact command output for the candidate, including `git rev-parse HEAD`, `git rev-parse origin/main`, and
`git ls-remote origin refs/heads/main`, belongs in the candidate handoff report.

The history hygiene gate checks complete non-shallow history, KeelMatrix authorship, and restricted developer-facing
coordination markers in tracked material and commit metadata.

Pinned SDK: `8.0.425`, recorded in `global.json`.
