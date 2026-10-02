# Repository state verification

This document defines the repository-state checks used to verify a checkout. A validation record should include the exact
commit, remote ref, command output, and residual platform evidence for that checkout; this file is not a substitute for
that checkout-specific record.

## Canonical repository and hosted validation

The canonical repository is the public `KeelMatrix/PackageSurface` GitHub repository. Hosted validation is defined by:

- `.github/workflows/ci.yml`: push to `main`, pull requests, and manual exact-SHA dispatch on `windows-latest`,
  `ubuntu-latest`, and `macos-latest`.
- `.github/workflows/release.yml`: tag-only `v*.*.*` validation, publication, and GitHub Release chain.
- `scripts/validate-release.ps1`: the shared version and changelog contract used by candidate checks and the release
  workflow.

Both workflows disable telemetry and use least-privilege job permissions. The local gate is the authoritative repository
validation entrypoint; hosted CI supplies independent platform evidence for the exact pushed commit.

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

The exact command output for the checkout, including `git rev-parse HEAD`, `git rev-parse origin/main`, and
`git ls-remote origin refs/heads/main`, belongs in its validation record.

The history hygiene contract is defined by `scripts/test-history-hygiene.ps1`, with disposable adversarial coverage in
`scripts/test-history-hygiene-regressions.ps1`. It checks complete non-shallow history, KeelMatrix authorship, and the
bounded developer-coordination vocabulary, treating every maximal run of non-alphanumeric separator characters as
equivalent between phrase tokens, in tracked material, historical trees, and commit metadata.

Pinned SDK: `8.0.425`, recorded in `global.json`.
