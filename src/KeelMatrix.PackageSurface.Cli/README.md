# KeelMatrix.PackageSurface

PackageSurface shows when a NuGet dependency adds or changes build, compiler, source, content, or native capabilities. Use its reviewed baseline to make dependency changes visible during updates. It is not a vulnerability or malware scanner.

## Install

```powershell
dotnet tool install --global KeelMatrix.PackageSurface
```

## Quick start

Restore your project first. PackageSurface reads the existing restore output and does not restore packages for you.

```powershell
dotnet restore
package-surface scan MySolution.sln
package-surface baseline MySolution.sln --output package-surface.json
package-surface check MySolution.sln --baseline package-surface.json
```

Review and commit the baseline. If a later check reports a difference, review it before updating the baseline. Exit code `0` means the command succeeded or the check passed; exit code `1` means the check found a difference; exit code `2` means the invocation is invalid, the input is unsupported, the analysis is incomplete, or an environment error occurred.

## Commands

```text
package-surface scan <path>
package-surface baseline <path> --output <baseline>
package-surface check <path> --baseline <baseline>
--format text|json|sarif
--strict-content
--project <path>
--no-telemetry
```

## Limitations

PackageSurface supports Windows, Linux, and macOS for SDK-style PackageReference restore outputs; hosted CI validates the command contract on all three platforms. It requires existing `project.assets.json` restore output. It does not evaluate MSBuild, run package code, query package feeds, scan vulnerabilities, or decide whether a reported capability is malicious.

## Privacy

Package and build analysis data is not sent. Optional activation telemetry can be disabled for one command with `--no-telemetry`. Read the [PackageSurface privacy policy](https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md) and the [shared telemetry privacy policy](https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md).

## More information

See the [repository README](https://github.com/KeelMatrix/PackageSurface/blob/main/README.md) for the full command and report reference, [SECURITY.md](https://github.com/KeelMatrix/PackageSurface/blob/main/SECURITY.md) for security reporting, and [docs/DEV.md](https://github.com/KeelMatrix/PackageSurface/blob/main/docs/DEV.md) for developer validation details.

MIT licensed. See [LICENSE](https://github.com/KeelMatrix/PackageSurface/blob/main/LICENSE).
