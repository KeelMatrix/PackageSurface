# Changelog

PackageSurface release history.

## [Unreleased]

### Added

- Initial consumer workflow: restore normally, then run `package-surface scan`, `baseline`, and `check` against an SDK-style PackageReference project or solution.
- Reports active build props/targets, transitive and multi-targeting imports, compiler extensions, compile-source content, native runtime assets, and informational tool/script presence without executing package code.
- Supports text, JSON, and SARIF reports with stable `PS001`–`PS007` diagnostics and exit codes `0` (success), `1` (reviewed surface difference), and `2` (invalid or incomplete analysis).
- `--strict-content` records SHA-256 fingerprints for present, active build/compiler execution assets and requires an explicitly strict baseline; strict baselines also enforce hashing when the flag is omitted.
- Baselines are deterministic, package-relative, bounded, versioned schema documents. Unsupported schema versions, incomplete restore evidence, unsafe paths, malformed XML, invalid hashes, and unsupported project inputs fail closed.
- Multi-target restore evidence reconciles equivalent framework monikers and effective aliases, including RID-qualified target names, while genuinely missing or incoherent graphs remain fail-closed.
- Static package `.props`/`.targets` inspection reports observed `UsingTask`, `Exec`, inline factories, and `Import` elements as syntax facts only.
- Static package imports are followed only within bounded, provable package-relative chains; cycles are bounded and dynamic conditions/imports fail closed as `PS007`.
- Restore evidence is reconciled across declared frameworks, target graphs, dependency closure, package inventories, content metadata, and generated imports before active filtering.
- V1 is limited to already-restored modern SDK-style PackageReference graphs; it does not restore, query feeds, execute MSBuild, load analyzers, inspect malware/vulnerabilities, or decide whether a dependency is safe.

### Changed

- Restore validation now fails closed for malformed typed metadata and shares structural, import-edge, and dependency-traversal budgets across the complete invocation.
- Nested package imports inherit their importing capability and phase context, generated-import edges consume the cumulative edge budget, and baseline replacement is coupled transactionally to final report generation.
- Incomplete multi-project selections are transactional: every `PS007`/structural-budget failure discards accumulated entries, emits no clean-looking text/JSON/SARIF result, and preserves an existing baseline so `--project` narrowing can recover.
- Generated top-level and nested imports are validated against the case-insensitive package identities reachable in every applicable target graph, including conditional, phase/diamond, TFM/RID, and orphan-package cases; format-4 dependency-group identities are also case-insensitive.
- Restore identity reconciliation now uses one canonical index for project/restore framework maps, effective frameworks, target aliases, target/RID keys, dependency groups, and package `ID/version` identities; key/effective/alias incoherence, duplicate, case-folded, or effective-moniker-equivalent identities fail closed before reachability and capability filtering, while valid aliases, multi-target/RID selections, dependency-group project paths, and package requirements remain accepted.
- Filesystem containment uses a canonicalization seam for deterministic ancestor-link regression coverage while continuing to reject reparse points within the declared package/cache root.
- Activation telemetry remains best-effort and opt-out. The shared payload contract is explicit: activation serializes `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`; PackageSurface adds no scanned package, asset, path, TFM, RID, baseline, or diagnostic data.
