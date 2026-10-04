param(
    [string] $RepositoryRoot,
    [string] $BuiltHelpPath,
    [string] $BuiltHelpTextPath
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
    $cliDocuments[0],
    $cliDocuments[1],
    (Join-Path $root 'docs/DEV.md'),
    (Join-Path $root 'SECURITY.md')
)
$filesystemContractDocuments = @(
    (Join-Path $root 'README.md'),
    (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/README.md'),
    (Join-Path $root 'CHANGELOG.md'),
    (Join-Path $root 'SECURITY.md'),
    (Join-Path $root 'docs/DEV.md')
)
$filesystemContract = 'Filesystem path identity follows the filesystem containing the resolved path: project-path identities use the project/assets filesystem, while package-relative asset identities and package-file `Exists(...)` checks use the NuGet package-root filesystem. Those roots can differ in case sensitivity, including across volumes. NuGet package ID/version identity remains case-insensitive. Inaccessible or ambiguous resolution fails closed with `PS007`.'
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
    'PackageSurface requests activation only',
    'does not request a heartbeat',
    'passes no analyzed package or build data to the shared client',
    'effective-moniker canonicalization',
    'malformed package ID/version',
    'both directions',
    'selected package version',
    'direct/project-reference rooted',
    'schema-aware restore',
    'unknown members',
    'standard unconsumed MSBuild elements and attributes',
    'fallbackFolders',
    'array of strings',
    'SdkAnalysisLevel',
    'only as a string',
    'native SDK shape',
    'malformed nested',
    'compilerApiVersion',
    'non-canonical spellings',
    'x-` prefix',
    'urn:keelmatrix:packagesurface:extension',
    'NuGet.Versioning'
)
foreach ($document in $cliDocuments) {
    $text = Get-Content -Raw $document
    foreach ($requiredText in $required) {
        if (-not $text.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI documentation '$document' is missing '$requiredText'." }
    }
}

$baselineAliasClauses = [ordered]@{
    'preflight' = 'baseline --output path is preflighted against'
    'reachable package inventory' = 'every reachable package-inventory file resolved from packageFolders'
    'global and fallback roots' = 'global and fallback roots'
    'direct and transitive packages' = 'direct and transitive packages'
    'TFM and RID coverage' = 'every TFM/RID'
    'nested static imports' = 'nested static imports'
    'inventory categories' = 'all inventory categories'
    'lexical aliases' = 'Lexical . and .. aliases'
    'single and multiple hardlinks' = 'single and multiple hardlinks'
    'reparse and symlink aliases' = 'direct, interior, and ancestor reparse/symlink aliases'
    'controlled rejection' = 'PS007 and controlled exit code 2 before any mutation'
    'byte preservation' = 'preserves existing output and input bytes'
    'distinct output acceptance' = 'genuinely distinct outputs are accepted'
}

function Normalize-ContractText([string] $Text) {
    return ([regex]::Replace($Text, '\s+', ' ').Trim()).Replace('`', '')
}

function Assert-BaselineAliasClauses([string] $Surface, [string] $Text) {
    $normalized = Normalize-ContractText $Text
    foreach ($clause in $baselineAliasClauses.GetEnumerator()) {
        if (-not $normalized.Contains($clause.Value, [StringComparison]::Ordinal)) {
            throw "Documentation surface '$Surface' is missing baseline --output clause '$($clause.Key)': '$($clause.Value)'."
        }
    }
}

foreach ($document in $filesystemContractDocuments) {
    $normalized = Normalize-ContractText (Get-Content -Raw $document)
    if (-not $normalized.Contains((Normalize-ContractText $filesystemContract), [StringComparison]::Ordinal)) {
        throw "Documentation surface '$document' is missing the filesystem-identity contract."
    }
}
if (-not (Normalize-ContractText $cliSource).Contains((Normalize-ContractText $filesystemContract), [StringComparison]::Ordinal)) {
    throw 'Built --help source is missing the filesystem-identity contract.'
}

foreach ($document in $parityDocuments) {
    Assert-BaselineAliasClauses $document (Get-Content -Raw $document)
}

if (-not [string]::IsNullOrWhiteSpace($BuiltHelpTextPath)) {
    if (-not [string]::IsNullOrWhiteSpace($BuiltHelpPath)) {
        throw 'Specify either BuiltHelpPath or BuiltHelpTextPath, not both.'
    }
    $builtHelp = @(Get-Content -Raw (Resolve-Path -LiteralPath $BuiltHelpTextPath))
    $builtHelpExitCode = 0
}
else {
    $defaultBuiltHelpPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
    if ([string]::IsNullOrWhiteSpace($BuiltHelpPath)) {
        $BuiltHelpPath = $defaultBuiltHelpPath
    }
    $resolvedBuiltHelpPath = (Resolve-Path -LiteralPath $BuiltHelpPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolvedBuiltHelpPath -PathType Leaf)) {
        throw "Built CLI help assembly '$resolvedBuiltHelpPath' was not found. Build the Release CLI before running this validator."
    }
    $builtHelp = @(& dotnet $resolvedBuiltHelpPath --help 2>&1)
    $builtHelpExitCode = $LASTEXITCODE
}
if ($builtHelpExitCode -ne 0) {
    throw "Built CLI --help invocation failed with exit code $builtHelpExitCode."
}
Assert-BaselineAliasClauses 'built --help' ($builtHelp -join [Environment]::NewLine)
if (-not (Normalize-ContractText ($builtHelp -join [Environment]::NewLine)).Contains((Normalize-ContractText $filesystemContract), [StringComparison]::Ordinal)) {
    throw 'Built --help is missing the filesystem-identity contract.'
}

foreach ($requiredText in @('package-surface scan <path>', '--format text|json|sarif', '--strict-content', '--project <path>', '--telemetry on|off', '--no-telemetry', 'Exit codes:', 'effective-moniker canonicalization', 'malformed package', 'ID/version', 'both directions', 'package versions', 'direct/project-reference rooted', 'disconnected package nodes', 'duplicate or case-variant', 'unknown members', 'standard unconsumed MSBuild elements and attributes', 'native SDK shape', 'malformed nested', 'compilerApiVersion', 'non-canonical spellings of consumed JSON/XML members', 'x- prefix', 'urn:keelmatrix:packagesurface:extension', 'NuGet.Versioning 7.9.0 parser/comparer')) {
    if (-not $cliSource.Contains($requiredText, [StringComparison]::Ordinal)) { throw "CLI help is missing '$requiredText'." }
}
Write-Output 'CLI_DOCUMENTATION=PASS root/package/developer/release/security docs and built --help retain the same baseline-alias and command contract.'
