# Privacy

PackageSurface performs local analysis of an already-restored NuGet graph and package metadata. Analysis itself does not access the network, restore packages, or upload its inputs or results.

## Product-specific telemetry boundary

Optional activation telemetry is provided by [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). The shared client serializes activation with exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`. Its heartbeat shape contains exactly the common fields `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, and `installation_hash`, plus `runtime`, `os`, `ci`, and `week`. PackageSurface requests activation only; it does not request a heartbeat. The shared [`KeelMatrix.Telemetry PRIVACY.md`](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) is the source of truth for opt-out precedence, local storage, HTTPS delivery, and retention.

The shared client emits the established anonymous `project_hash` and `installation_hash` pseudonymous identifiers; raw project, repository, and installation identifiers are not sent. PackageSurface does not add analyzed dependency package IDs or versions, scanned asset names or paths, target names, TFM/RID values, MSBuild or package content, content hashes, diagnostics, baseline contents, or source-feed information to either payload. No scanned package, asset, path, TFM, RID, baseline, or diagnostic data is added.

PackageSurface requests activation only after a successful baseline creation or comparison that classified at least one real resolved `PackageReference` graph. The request is best effort, and telemetry failure cannot change classification, baseline, or check results. Use `--telemetry off` or `--no-telemetry` to disable the request. Local validation disables telemetry.
