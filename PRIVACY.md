# Privacy

PackageSurface performs local analysis of an already-restored NuGet graph and package metadata. Analysis itself does not access the network, restore packages, or upload its inputs or results.

## Product-specific telemetry boundary

Optional activation telemetry uses [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). PackageSurface requests activation only and does not request a heartbeat. The shared [KeelMatrix.Telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md) is the source of truth for shared payload fields, opt-out precedence, local storage, delivery, and retention.

PackageSurface calls the shared client with only its stable product name and the CLI type. It does not pass analyzed dependency package IDs or versions, scanned asset names or paths, target names, TFM/RID values, MSBuild or package content, content hashes, diagnostics, baseline contents, or source-feed information. The shared client owns the payload fields and anonymous identifiers described in its privacy policy.

PackageSurface requests activation only after a successful baseline creation or comparison that classified at least one real resolved `PackageReference` graph. The request is best effort, and telemetry cannot change classification, baseline, or check results. Use `--no-telemetry` to suppress the request for one invocation. The shared client resolves process and repository opt-outs as described in its privacy policy. Local validation disables telemetry.
