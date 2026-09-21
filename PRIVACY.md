# Privacy

PackageSurface performs local analysis of an already-restored NuGet graph and package metadata. Analysis itself does not access the network, restore packages, or upload its inputs or results.

## Product-specific telemetry boundary

Optional activation telemetry is provided by [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). Its complete event fields, including the documented anonymous `project_hash` and `installation_hash`, opt-out precedence, local storage, HTTPS endpoint, and 90-day server retention are defined by the shared [`KeelMatrix.Telemetry PRIVACY.md`](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md). That document is the source of truth for shared-client behavior.

PackageSurface supplies the shared client only with its fixed tool name and entry-point type. It does not pass analyzed dependency package IDs or versions, asset names or paths, target names, TFM/RID values, MSBuild or package content, content hashes, diagnostics, baseline contents, or source-feed information. It does not provide raw project or repository identity; the shared client may emit the anonymous hashes documented in its policy.

PackageSurface requests an activation event only after a successful baseline creation or comparison that classified at least one real resolved `PackageReference` graph. The request is best effort, and telemetry failure cannot change classification, baseline, or check results. Use `--telemetry off` or `--no-telemetry` to disable the request. Local validation disables telemetry.
