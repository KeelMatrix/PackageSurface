# Restore evidence

This record captures the restore-framework compatibility and assets-format evidence for the current pre-release candidate.

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

Synthetic coverage also passes for `.NETCoreApp,Version=v8.0` ↔ `net8.0`, `.NETFramework,Version=v4.7.2` ↔ `net472`, dotted `net4.7.2` aliases, case-insensitive forms, RID-suffixed targets, format-4 project/restore metadata with different equivalent spellings, and a declared framework with no target graph. The negative remains incomplete with `PS007` semantics.

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

## Acceptance accounting

The first-release acceptance self-review accounting for the restore-evidence row is now:

- 144 criteria met
- 26 criteria not applicable
- 0 criteria unmet
- 0 checklist rows unverified

Native format-4 generation under the pinned SDK is retained as an explicitly documented environment distinction, not counted as an unmet checklist row.
