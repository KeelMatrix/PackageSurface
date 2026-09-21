# Changelog

PackageSurface release history.

## [Unreleased]

- Package and documentation work for the initial release is being validated.
- Clarify the PackageSurface telemetry boundary and link the shared telemetry privacy policy.
- Make package icon packing fail closed when the repository-root `icon.png` is absent, and validate the icon asset and metadata in the local gate.
- Use the public `KeelMatrix.Telemetry` `0.1.0` dependency so clean public-feed restores do not depend on a local package cache.
- Map the complete public SourceLink dependency graph, including its transitive build and hashing packages, for clean restores.
- Make the committed local gate select the installed tool executable correctly on Windows and Linux.
- Make the Linux/macOS installed-tool smoke derive `DOTNET_ROOT` from the active `dotnet` executable when the variable is unset, and record the exact symbols archive file set in the validation evidence.
