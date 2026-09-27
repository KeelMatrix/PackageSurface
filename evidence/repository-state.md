# Repository state verification

This document defines the repository-state checks used at repository handoff. The handoff records the exact commit, remote
ref, command output, and residual platform evidence for that candidate; this file is not a substitute for that
version-specific record.

## Required checks

```text
COMMAND: git remote -v
EXPECTED: the canonical PackageSurface remote is the only reviewed remote.

COMMAND: git status --short --branch
EXPECTED: the candidate checkout is clean and equal to its reviewed remote branch.

COMMAND: git ls-tree -r --name-only HEAD | Select-String "^\.github/"
EXPECTED: no hosted workflow files are present.
```

The committed release contract also rejects a `.github` directory or tracked files beneath it. Run the command below from
the canonical checkout and record its result with the exact validation data:

```powershell
pwsh -NoLogo -NoProfile -File scripts/test-release-contract.ps1
```

The history hygiene gate checks complete non-shallow history, KeelMatrix authorship, and restricted developer-coordination
markers, including task identifiers in commit metadata.

Pinned SDK: `8.0.425`, recorded in `global.json`.
