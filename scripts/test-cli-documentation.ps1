param(
    [string] $RepositoryRoot,
    [string] $BuiltHelpPath
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$cliSource = Get-Content -Raw (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs')
$cliDocuments = @(
    (Join-Path $root 'README.md'),
    (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/README.md')
)
$parityDocuments = @(
    $cliDocuments,
    (Join-Path $root 'docs/DEV.md'),
    (Join-Path $root 'CHANGELOG.md'),
    (Join-Path $root 'SECURITY.md')
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
    'PS-SURFACE',
    'serializes activation with exactly',
    'does not request a heartbeat',
    'effective-moniker canonicalization',
    'malformed package ID/version',
    'both directions',
    'selected package version'
)
foreach ($document in $cliDocuments) {
    $text = Get-Content -Raw $document
    foreach ($requiredText in $required) {
        if (-not $text.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI documentation '$document' is missing '$requiredText'." }
    }
}

$baselineAliasContract = 'An explicit `baseline --output` path is preflighted against the selected restore/project/generated-import inputs and every reachable package-inventory file resolved from `packageFolders` by lexical path and real file identity, including single and multiple hardlinks, before any file is written. Lexical aliases, hardlinks, and reparse/symlink aliases are rejected with controlled exit code `2` and preserve the existing output/input bytes; genuinely distinct outputs are accepted.'
$normalizedContract = [regex]::Replace($baselineAliasContract, '\s+', ' ').Trim()
foreach ($document in $parityDocuments) {
    $normalizedDocument = [regex]::Replace((Get-Content -Raw $document), '\s+', ' ')
    if (-not $normalizedDocument.Contains($normalizedContract, [StringComparison]::Ordinal)) {
        throw "Documentation '$document' does not state the complete baseline --output alias scope and preservation behavior."
    }
}

if ([string]::IsNullOrWhiteSpace($BuiltHelpPath)) {
    $defaultBuiltHelpPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
    if (Test-Path -LiteralPath $defaultBuiltHelpPath -PathType Leaf) {
        $builtHelp = @(& dotnet $defaultBuiltHelpPath --help 2>&1)
    }
    else {
        $cliProject = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
        $builtHelp = @(& dotnet run --project $cliProject --configuration Release --no-restore -- --help 2>&1)
    }
}
else {
    $resolvedBuiltHelpPath = (Resolve-Path -LiteralPath $BuiltHelpPath).Path
    $builtHelp = @(& dotnet $resolvedBuiltHelpPath --help 2>&1)
}
$builtHelpExitCode = $LASTEXITCODE
if ($builtHelpExitCode -ne 0) {
    throw "Built CLI --help invocation failed with exit code $builtHelpExitCode."
}
$normalizedHelp = [regex]::Replace(($builtHelp -join [Environment]::NewLine), '\s+', ' ').Trim()
foreach ($requiredText in @(
    'malformed packageFolders entries',
    'baseline output aliases including hardlinks to restore, generated-import, or reachable package inputs are PS007 before any write'
)) {
    if (-not $normalizedHelp.Contains($requiredText, [StringComparison]::Ordinal)) {
        throw "Built CLI --help is missing the baseline-alias contract text '$requiredText'."
    }
}

foreach ($requiredText in @('package-surface scan <path>', '--format text|json|sarif', '--strict-content', '--project <path>', '--telemetry on|off', '--no-telemetry', 'Exit codes:', 'effective-moniker canonicalization', 'malformed package', 'ID/version', 'both directions', 'package versions')) {
    if (-not $cliSource.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI help is missing '$requiredText'." }
}
Write-Output 'CLI_DOCUMENTATION=PASS root/package/developer/release/security docs and built --help retain the same baseline-alias and command contract.'
