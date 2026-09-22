# Changelog

PackageSurface release history.

## [Unreleased]

- Initial consumer workflow: restore normally, then run `package-surface scan`, `baseline`, and `check` against an SDK-style PackageReference project or solution.
- Reports active build props/targets, transitive and multi-targeting imports, compiler extensions, compile-source content, native runtime assets, and informational tool/script presence without executing package code.
- Supports text, JSON, and SARIF reports with stable `PS001`–`PS007` diagnostics and exit codes `0` (success), `1` (reviewed surface difference), and `2` (invalid or incomplete analysis).
- `--strict-content` records SHA-256 fingerprints and requires an explicitly strict baseline; strict baselines also enforce hashing when the flag is omitted.
- Baselines are deterministic, package-relative, bounded, versioned schema documents. Unsupported schema versions, incomplete restore evidence, unsafe paths, malformed XML, invalid hashes, and unsupported project inputs fail closed.
- Static package `.props`/`.targets` inspection reports observed `UsingTask`, `Exec`, inline factories, and `Import` elements as syntax facts only.
- V1 is limited to already-restored modern SDK-style PackageReference graphs; it does not restore, query feeds, execute MSBuild, load analyzers, inspect malware/vulnerabilities, or decide whether a dependency is safe.
- Activation telemetry is best-effort and opt-out; analyzed dependency identities, paths, content, diagnostics, and baseline contents are not sent.
