# Repository state evidence

## Canonical repository and remote

```text
COMMAND: git remote -v
EXIT_CODE: 0
OUTPUT:
origin  https://github.com/KeelMatrix/PackageSurface.git (fetch)
origin  https://github.com/KeelMatrix/PackageSurface.git (push)

COMMAND: git status --short --branch
EXIT_CODE: 0
OUTPUT:
## main...origin/main

COMMAND: git ls-tree -r --name-only HEAD | Select-String "^\.github/"
EXIT_CODE: 0
OUTPUT:
.github/workflows/ci.yml
.github/workflows/release.yml
```

## Repository checks

The canonical checkout is clean, all commits use the `KeelMatrix <keelmatrix@gmail.com>` identity, and tracked material is checked for restricted coordination markers in both the working tree and complete history.

Pinned SDK: `8.0.425`, recorded in `global.json`.
