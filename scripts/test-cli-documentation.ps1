param(
    [string] $RepositoryRoot,
    [string] $BuiltHelpPath,
    [string] $BuiltHelpTextPath
)

$ErrorActionPreference = 'Stop'
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$referencePath = Join-Path $root 'docs/restore-and-baseline-safety.md'
$rootReadmePath = Join-Path $root 'README.md'
$packedReadmePath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/README.md'
$securityPath = Join-Path $root 'SECURITY.md'
$developerPath = Join-Path $root 'docs/DEV.md'
foreach ($path in @($referencePath, $rootReadmePath, $packedReadmePath, $securityPath, $developerPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Required documentation file '$path' was not found." }
}

function Assert-Contains([string] $Surface, [string] $Text, [string[]] $Required) {
    foreach ($item in $Required) {
        if (-not $Text.Contains($item, [StringComparison]::Ordinal)) {
            throw "Documentation surface '$Surface' is missing '$item'."
        }
    }
}

function Assert-LocalLinks([string] $Path) {
    $text = Get-Content -Raw $Path
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim()
        if ($target.StartsWith('#', [StringComparison]::Ordinal) -or $target -match '^[A-Za-z][A-Za-z0-9+.-]*://') { continue }
        $target = ($target -replace '#.*$', '').Trim()
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($Path)) $target.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Documentation link '$target' in '$Path' does not resolve to a file."
        }
    }
}

$rootReadme = Get-Content -Raw $rootReadmePath
$packedReadme = Get-Content -Raw $packedReadmePath
$security = Get-Content -Raw $securityPath
$developer = Get-Content -Raw $developerPath
$reference = Get-Content -Raw $referencePath
$cliSource = Get-Content -Raw (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs')

if (-not [string]::IsNullOrWhiteSpace($BuiltHelpTextPath)) {
    if (-not [string]::IsNullOrWhiteSpace($BuiltHelpPath)) { throw 'Specify either BuiltHelpPath or BuiltHelpTextPath, not both.' }
    $builtHelp = Get-Content -Raw (Resolve-Path -LiteralPath $BuiltHelpTextPath)
    $builtHelpExitCode = 0
}
else {
    if ([string]::IsNullOrWhiteSpace($BuiltHelpPath)) {
        $BuiltHelpPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
    }
    $resolvedBuiltHelpPath = (Resolve-Path -LiteralPath $BuiltHelpPath -ErrorAction Stop).Path
    if (-not (Test-Path -LiteralPath $resolvedBuiltHelpPath -PathType Leaf)) {
        throw "Built CLI help assembly '$resolvedBuiltHelpPath' was not found. Build the Release CLI before running this validator."
    }
    $builtHelp = @(& dotnet $resolvedBuiltHelpPath --help 2>&1) -join [Environment]::NewLine
    $builtHelpExitCode = $LASTEXITCODE
}
if ($builtHelpExitCode -ne 0) { throw "Built CLI --help invocation failed with exit code $builtHelpExitCode." }

$canonicalUrl = 'https://github.com/KeelMatrix/PackageSurface/blob/main/docs/restore-and-baseline-safety.md'
Assert-Contains 'built --help' $builtHelp @(
    'package-surface scan <path> [options]',
    'package-surface baseline <path> --output <baseline> [options]',
    'package-surface check <path> --baseline <baseline> [options]',
    '--format text|json|sarif', '(default: text)',
    '--output <baseline>', '--baseline <baseline>', '--strict-content',
    '(default: off)',
    '--compiler-api-version <v>', '--project <path>', '--no-telemetry',
    '0  Scan/baseline succeeded', '1  Check found a surface or policy difference',
    '2  Invalid invocation', 'PS007', $canonicalUrl,
    'https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md'
)
Assert-Contains 'CLI help source' $cliSource @('Options:', 'Exit codes:', $canonicalUrl)

Assert-Contains 'root README consumer guide' $rootReadme @(
    'dotnet tool install --global KeelMatrix.PackageSurface',
    'dotnet restore', 'package-surface scan MySolution.sln',
    'package-surface baseline MySolution.sln --output package-surface.json',
    'package-surface check MySolution.sln --baseline package-surface.json',
    '--strict-content', '--compiler-api-version <version>', '--no-telemetry',
    'Exit code `0`', '`1` means a check', '`2` means the invocation',
    '[restore and baseline safety reference](docs/restore-and-baseline-safety.md)'
)
Assert-Contains 'packed README consumer guide' $packedReadme @(
    '## Install', '## Quick start', '## Limitations', '## Privacy', '## Documentation',
    'dotnet tool install --global KeelMatrix.PackageSurface', 'dotnet restore',
    'package-surface scan MySolution.sln', 'package-surface baseline MySolution.sln --output package-surface.json',
    'package-surface check MySolution.sln --baseline package-surface.json', '--no-telemetry',
    'blob/main/docs/restore-and-baseline-safety.md'
)
Assert-Contains 'security policy' $security @(
    '## Reporting a Vulnerability', 'privately', 'public issue tracker',
    'latest published version is the supported security baseline',
    '[restore and baseline safety reference](docs/restore-and-baseline-safety.md)'
)
Assert-Contains 'developer guide' $developer @(
    'pwsh -NoProfile -File scripts/run-local-gate.ps1',
    'SDK 8.0.425, 9.0.121, and 10.0.401',
    '[restore and baseline safety reference](restore-and-baseline-safety.md)'
)
Assert-Contains 'canonical safety reference' $reference @(
    'canonical restore-identity index', 'formats 3 and 4',
    'fallbackFolders', 'SdkAnalysisLevel', 'urn:keelmatrix:packagesurface:extension',
    'root-level MSBuild `ProjectExtensions` child', 'direct child of `Project`', 'direct child of a root `Target`',
    'every reachable package-inventory file', 'global and fallback roots',
    'one or multiple hardlinks', 'direct, interior, or ancestor reparse/symlink aliases',
    'Filesystem path identity follows the filesystem containing the resolved path',
    'project/assets filesystem', 'NuGet package-root filesystem'
)

foreach ($path in @($rootReadmePath, $packedReadmePath, $securityPath, $developerPath, $referencePath)) {
    Assert-LocalLinks $path
}
Write-Output 'CLI_DOCUMENTATION=PASS built help, command contract, audience coverage, canonical reference, and local links.'
