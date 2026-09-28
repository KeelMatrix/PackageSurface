# Restore compatibility evidence

This record captures restore-framework compatibility and assets-format evidence from controlled candidate runs. The exact
candidate SHA, command durations, and current platform status belong in the candidate handoff; the examples below are
not a claim that a later checkout has the same state.

## Framework reconciliation

The installed tool was packed from the working candidate and tested against a real SDK-style `PackageReference` project declaring `net8.0;netstandard2.0`.

```text
SDK: 8.0.425
Assets format: 3
Resolved target keys: .NETStandard,Version=v2.0, net8.0
Installed scan: exit 0, 584 ms
Installed baseline: exit 0, 196 ms
Installed check: exit 0, 190 ms
Result: REAL_MULTI_TARGET_FRAMEWORK_RECONCILIATION=PASS
```

Synthetic coverage also passes for `.NETCoreApp,Version=v8.0` ↔ `net8.0`, `.NETFramework,Version=v4.7.2` ↔ `net472`, dotted `net4.7.2` aliases, case-insensitive forms, RID-suffixed targets, format-4 project/restore metadata with different equivalent spellings, and a declared framework with no target graph. One canonical restore-identity index validates key/effective/alias coherence across project and restore maps, target/RID keys, dependency-group keys, and package `ID/version` identities before capability filtering. The schema-aware restore boundary rejects exact or case-variant duplicates, unknown members, and non-canonical spellings of consumed JSON/XML members. Only explicit `x-` JSON extension members and `urn:keelmatrix:packagesurface:extension` XML attributes are tolerated. The `NuGet.Versioning` 7.9.0 parser/comparer governs package identities and dependency ranges across package keys, roots, imports, baseline provenance, target dependencies, project dependencies, and format-4 requirements. Project and restore framework sets, dependency-group sets, and resolved target framework members are complete in both directions; project, target-package, and format-4 dependency values use that grammar and selected-version matching. Duplicate, case-folded, conflicting, and effective-moniker-equivalent identities remain incomplete with `PS007`; valid aliases, multi-target/RID selections, dependency-group project paths, and package requirements remain accepted. Generated-import reachability likewise fails closed when an import is absent from an applicable target graph. The negative remains incomplete with `PS007` semantics.

## Native assets format evidence

The repository's pinned SDK and several installed SDKs were tested with a zero-dependency `net8.0` project using the format-evidence command:

```powershell
pwsh -NoLogo -NoProfile -File scripts/test-assets-format.ps1 -SdkVersion 8.0.131
pwsh -NoLogo -NoProfile -File scripts/test-assets-format.ps1 -SdkVersion 8.0.408
pwsh -NoLogo -NoProfile -File scripts/test-assets-format.ps1 -SdkVersion 8.0.425
pwsh -NoLogo -NoProfile -File scripts/test-assets-format.ps1 -SdkVersion 9.0.121
pwsh -NoLogo -NoProfile -File scripts/test-assets-format.ps1 -SdkVersion 10.0.401
```

Observed results were formats 3, 3, 3, 3, and 4 respectively. SDK `10.0.401` therefore produced a real native format-4 assets file with the default restore configuration; its installed-tool scan, baseline, and check each returned exit 0. Native format-4 production is verified with SDK `10.0.401`, while the pinned SDK `8.0.425` produces format 3. No claim is made that every SDK or restore configuration produces format 4.

## Interpretation

Native format-4 generation under the pinned SDK remains an explicitly documented environment distinction. Candidate
acceptance accounting is maintained against the read-only first-release criteria for the exact reviewed SHA and must not be
inferred from this reusable restore evidence alone.
