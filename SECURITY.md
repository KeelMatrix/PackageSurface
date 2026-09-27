# Security Policy

## Reporting a Vulnerability

Please report suspected vulnerabilities privately to `keelmatrix@gmail.com` with a concise description, affected version, reproduction steps, expected and observed behavior, and impact. Do not disclose vulnerability details in a public issue, pull request, package review, or other public channel. This route is for security vulnerabilities; ordinary bugs belong in the public issue tracker, and conduct concerns should use the repository's conduct-reporting route.

## Supported Versions

The latest published version is the supported security baseline. During pre-release development, test the current repository revision and include the exact commit or package version in a private report. Security fixes are evaluated against the current supported version and documented in an appropriate release note after review.

Analysis fails closed on incomplete evidence. A `PS007` caused by a cumulative budget, unreachable generated top-level/nested import, duplicate/case/moniker-equivalent framework or target identity, malformed library/target `ID/version` key, malformed restore data, or unsafe path produces no partial surface entries; `baseline` does not replace an existing file and `check` does not report a clean or policy-difference result. Restore identities are validated before capability filtering, while valid project paths and package requirements in format-4 dependency-group values remain accepted. Package/cache containment is canonicalized and reparse points within the declared root are rejected; ancestor links above that root do not expand the declared containment boundary.

## Telemetry boundary

Optional activation telemetry uses [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). Activation serializes exactly `event`, `tool`, `tool_version`, `telemetry_version`, `schema_version`, `project_hash`, `installation_hash`, `runtime`, `os`, `ci`, and `timestamp`; the shared heartbeat shape contains the common fields plus `runtime`, `os`, `ci`, and `week`. PackageSurface requests activation only and adds no scanned package, asset, path, TFM, RID, baseline, or diagnostic data. The shared client emits only its established anonymous pseudonymous identifiers and fields described in the [shared privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); the product-specific boundary is maintained in [PRIVACY.md](PRIVACY.md). Telemetry failures never affect analysis results.
