# PackageSurface development guide

- The shipping .NET tool is in `src/KeelMatrix.PackageSurface.Cli`; the non-packable classifier is in `src/KeelMatrix.PackageSurface.Core`.
- Feature tests are in `tests`; controlled package and consumer fixtures are in `fixtures`.
- Consumer contracts are documented in `README.md` and `docs/restore-and-baseline-safety.md`; validation commands are in `docs/DEV.md`.
- Run focused checks while iterating, then `pwsh -NoProfile -File scripts/run-local-gate.ps1` for the final candidate.
- `pwsh -NoProfile -File scripts/test-website-metadata.ps1` checks classification and failure propagation through local, CI, and pack entry points.
- Keep `keelmatrix.website.json` aligned with evaluated shipping package metadata. Fixture packages are local test inputs, not published artifacts.
- Preserve offline, non-executing analysis, fail-closed incomplete results, and the stable CLI, baseline, diagnostic, and report contracts.
- Keep source, tests, package contents, and documentation aligned; report package and isolated consumer evidence with changes.
