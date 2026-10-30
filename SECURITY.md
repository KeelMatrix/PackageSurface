# Security Policy

## Reporting a Vulnerability

Please report suspected vulnerabilities privately to `keelmatrix@gmail.com` with a concise description, affected version, reproduction steps, expected and observed behavior, and impact. Do not disclose vulnerability details in a public issue, pull request, package review, or other public channel. This route is for security vulnerabilities; ordinary bugs belong in the [public issue tracker](https://github.com/KeelMatrix/PackageSurface/issues).

## Supported Versions

The latest published version is the supported security baseline. During pre-release development, test the current repository revision and include the exact commit or package version in a private report. Security fixes are evaluated against the current supported version and documented in an appropriate release note after review.

PackageSurface reads existing restore evidence and does not execute package or MSBuild code. Incomplete or ambiguous evidence returns `PS007`; it does not emit partial surface entries, replace a baseline, or report a clean check. The [restore and baseline safety reference](docs/restore-and-baseline-safety.md) documents the validated input, XML, path, and baseline boundaries.

## Release workflow source validation

The release workflow source guard tracks effective directories through `env -C`/`--chdir` and `sudo -D`/`--chdir`, including nested wrappers and normalized paths. It parses sudo short-option bundles, consumes required option values before locating the command, and rejects login mode whenever `i` appears as a flag. Unknown or ambiguous sudo options, missing values, chroot mode, and login mode fail closed. Relative script inputs must resolve to the same pinned tracked path from the effective wrapper directory as from the workflow step. Relative executable inputs are checked against downloaded artifact and runner-temp outputs. Dynamic or outside-workspace directories make relative code inputs fail closed.

## Telemetry boundary

The restore and baseline safety rules are maintained in the [canonical reference](docs/restore-and-baseline-safety.md).

Optional activation telemetry uses [`KeelMatrix.Telemetry`](https://github.com/KeelMatrix/Telemetry). PackageSurface requests activation only and passes no analyzed package or build data to the shared client. The shared client owns telemetry fields, opt-out, identity, state, and delivery as described in its [privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md); the product-specific boundary is maintained in [PRIVACY.md](PRIVACY.md). Telemetry cannot affect analysis results.
