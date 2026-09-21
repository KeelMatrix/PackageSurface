# Security Policy

## Reporting a Vulnerability

Please report suspected vulnerabilities privately to `keelmatrix@gmail.com` with a concise description, affected version, reproduction steps, expected and observed behavior, and impact. Do not disclose vulnerability details in a public issue, pull request, package review, or other public channel. This route is for security vulnerabilities; ordinary bugs belong in the public issue tracker, and conduct concerns should use the repository's conduct-reporting route.

## Supported Versions

The latest published version is the supported security baseline. During pre-release development, test the current repository revision and include the exact commit or package version in a private report. Security fixes are evaluated against the current supported version and documented in an appropriate release note after review.

## Telemetry boundary

Optional activation telemetry uses [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). PackageSurface does not pass analyzed dependency package identities or content, including package IDs or versions, asset names or paths, MSBuild or package content, content hashes, diagnostics, baseline contents, or feed information. The shared client may emit the anonymous hashes and other fields described in the [shared privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); the product-specific boundary is maintained in [PRIVACY.md](PRIVACY.md). Telemetry failures never affect analysis results.
