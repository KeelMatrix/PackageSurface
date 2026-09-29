# Restore compatibility evidence

This record captures restore-framework compatibility and assets-format evidence from controlled runs. The exact commit,
command durations, and current platform status are run-specific metadata; the examples below describe the controlled
inputs and observed results and do not claim that a later checkout has the same state.

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

Synthetic coverage also passes for `.NETCoreApp,Version=v8.0` ↔ `net8.0`, `.NETFramework,Version=v4.7.2` ↔ `net472`, dotted `net4.7.2` aliases, case-insensitive forms, RID-suffixed targets, format-4 project/restore metadata with different equivalent spellings, and a declared framework with no target graph. One canonical restore-identity index validates key/effective/alias coherence across project and restore maps, target/RID keys, dependency-group keys, and package `ID/version` identities before capability filtering. The schema-aware restore boundary rejects exact or case-variant duplicates, unknown members of consumed JSON objects, and non-canonical spellings of consumed JSON/XML members. Standard SDK restore metadata accepts typed `fallbackFolders` and `SdkAnalysisLevel`; package XML tolerates standard unconsumed MSBuild elements and attributes while keeping consumed-member ambiguity strict. Explicit `x-` JSON extension members and `urn:keelmatrix:packagesurface:extension` XML attributes remain available for unrelated metadata. The `NuGet.Versioning` 7.9.0 parser/comparer governs package identities and dependency ranges across package keys, roots, imports, baseline provenance, target dependencies, project dependencies, and format-4 requirements. Project and restore framework sets, dependency-group sets, and resolved target framework members are complete in both directions; project, target-package, and format-4 dependency values use that grammar and selected-version matching. Duplicate, case-folded, conflicting, and effective-moniker-equivalent identities remain incomplete with `PS007`; valid aliases, multi-target/RID selections, dependency-group project paths, and package requirements remain accepted. Generated-import reachability likewise fails closed when an import is absent from an applicable target graph. The negative remains incomplete with `PS007` semantics.

## Native assets format evidence

The `compilerApiVersion` restore member is required to be a string when present; a wrong primitive is `PS007`, never an absent-value fallback.

The admitted restore-metadata contract is validated before graph closure. Native SDK evidence covers `project.restore`, `project.frameworks`, and `project.restore.frameworks` strings, booleans, string arrays, string-valued maps, object-valued maps, nullable `suppressedAdvisories` values, and `{name,version}` download-dependency objects; dependency values accept both native string and expanded-object forms. Wrong primitives, nulls where non-null, wrong array elements, map values, and malformed nested metadata produce `PS007`, while absent members remain valid. The deterministic regression matrix runs scan, baseline, and check for formats 3 and 4 and exercises SDKs 8.0.425, 9.0.121, and 10.0.401.

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

Native format-4 generation under the pinned SDK remains an explicitly documented environment distinction. This reusable
restore evidence should be considered together with the exact repository state and current validation results; it is not
by itself evidence about a later checkout.
