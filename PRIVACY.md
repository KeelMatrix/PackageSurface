# Privacy

PackageSurface performs local analysis of already-restored NuGet graph and package metadata. It does not upload package identities, versions, asset names or paths, project or repository identity, source content, MSBuild content, hashes, baselines, diagnostic text, or feed information.

After a successful baseline creation or comparison on a resolved graph, the tool may request one best-effort anonymous activation event through `KeelMatrix.Telemetry`. The shared telemetry component controls its bounded runtime, operating-system, CI, and anonymous installation/project fields. Use `--telemetry off` or `--no-telemetry` to disable the request. Local validation disables telemetry.

Telemetry is optional and cannot change classification, baseline, or check results. The tool performs no network access for analysis itself and does not restore packages.
