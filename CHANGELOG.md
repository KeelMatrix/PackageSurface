# Changelog

PackageSurface release history.

## [Unreleased]

## [0.1.0] - 2026-10-05

### Added

- Provides `package-surface scan`, `baseline`, and `check` for already-restored SDK-style `PackageReference` projects, with text, JSON, and SARIF reports, stable `PS001`–`PS007` diagnostics, and exit codes `0`, `1`, and `2` for success, surface differences, and invalid or incomplete analysis.
- Distinguishes present from active package build, compiler-extension, source-injection, and native-runtime assets for each project, target framework, and runtime identifier; lists recognized tools and scripts as informational facts. Versioned deterministic `package-surface.json` baselines detect surface changes, with optional `--strict-content` SHA-256 fingerprints for eligible active build/compiler assets.
- Analyzes existing restore evidence without restoring packages, querying feeds, loading dependency assemblies, or executing package or MSBuild code. Incomplete evidence fails closed as `PS007`; capability reports are not malware, vulnerability, or safety verdicts.
- Uses best-effort activation telemetry only after a successful baseline or check of at least one PackageReference graph. `--no-telemetry` suppresses the request, and analyzed package/build data is not sent.
- Supports modern SDK-style `PackageReference` projects targeting .NET 8 on Windows, Linux, and macOS.
