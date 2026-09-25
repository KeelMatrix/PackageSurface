$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$cliSource = Get-Content -Raw (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs')
$documents = @(
    (Join-Path $root 'README.md'),
    (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/README.md')
)
$required = @(
    'package-surface scan <path>',
    'package-surface baseline <path> --output <baseline>',
    'package-surface check <path> --baseline <baseline>',
    '--format text|json|sarif',
    '--strict-content',
    '--project <path>',
    '--telemetry on|off',
    '--no-telemetry',
    'Exit code `0`',
    'exit code `1`',
    'exit code `2`',
    'PS-SURFACE'
)
foreach ($document in $documents) {
    $text = Get-Content -Raw $document
    foreach ($requiredText in $required) {
        if (-not $text.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI documentation '$document' is missing '$requiredText'." }
    }
}
foreach ($requiredText in @('package-surface scan <path>', '--format text|json|sarif', '--strict-content', '--project <path>', '--telemetry on|off', '--no-telemetry', 'Exit codes:')) {
    if (-not $cliSource.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI help is missing '$requiredText'." }
}
Write-Output 'CLI_DOCUMENTATION=PASS README, package README, and --help retain the same command/options/exit contract.'
