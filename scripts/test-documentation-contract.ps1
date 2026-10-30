param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
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

$rootReadmePath = Join-Path $RepositoryRoot 'README.md'
$packedReadmePath = Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Cli/README.md'
$securityPath = Join-Path $RepositoryRoot 'SECURITY.md'
$privacyPath = Join-Path $RepositoryRoot 'PRIVACY.md'
$contributingPath = Join-Path $RepositoryRoot 'CONTRIBUTING.md'
$developerPath = Join-Path $RepositoryRoot 'docs/DEV.md'
$referencePath = Join-Path $RepositoryRoot 'docs/restore-and-baseline-safety.md'
$changelogPath = Join-Path $RepositoryRoot 'CHANGELOG.md'
$rootReadme = Get-Content -Raw $rootReadmePath
$packedReadme = Get-Content -Raw $packedReadmePath
$security = Get-Content -Raw $securityPath
$developer = Get-Content -Raw $developerPath
$reference = Get-Content -Raw $referencePath
$changelog = Get-Content -Raw $changelogPath

$consumerCommands = @(
    'dotnet tool install --global KeelMatrix.PackageSurface',
    'dotnet restore',
    'package-surface scan MySolution.sln',
    'package-surface baseline MySolution.sln --output package-surface.json',
    'package-surface check MySolution.sln --baseline package-surface.json'
)
Assert-Contains 'root consumer guide' $rootReadme $consumerCommands
Assert-Contains 'packed consumer guide' $packedReadme $consumerCommands
Assert-Contains 'root consumer guide' $rootReadme @('--format text|json|sarif', '--strict-content', '--project <path>', '--no-telemetry')
Assert-Contains 'packed consumer guide' $packedReadme @('## Install', '## Quick start', '## Limitations', '## Privacy', '## Documentation')

$canonicalLocal = '[restore and baseline safety reference](docs/restore-and-baseline-safety.md)'
Assert-Contains 'root README' $rootReadme @($canonicalLocal)
Assert-Contains 'security policy' $security @(
    '## Reporting a Vulnerability', 'keelmatrix@gmail.com',
    'Do not disclose vulnerability details in a public issue',
    'public issue tracker', '[restore and baseline safety reference](docs/restore-and-baseline-safety.md)'
)
$issueRoute = 'https://github.com/KeelMatrix/PackageSurface/issues'
$uri = $null
if (-not [Uri]::TryCreate($issueRoute, [UriKind]::Absolute, [ref]$uri)) { throw 'The ordinary-bug reporting route is not a valid URL.' }
if ($security.Contains('conduct-reporting route', [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SECURITY.md names a conduct-reporting route that is not defined by this repository.'
}
Assert-Contains 'developer guide' $developer @(
    'dotnet restore KeelMatrix.PackageSurface.sln',
    'pwsh -NoProfile -File scripts/run-local-gate.ps1',
    'SDK 8.0.425, 9.0.121, and 10.0.401',
    '[restore and baseline safety reference](restore-and-baseline-safety.md)'
)
Assert-Contains 'canonical safety reference' $reference @(
    '## Restore identity and schema', '## Package XML and imports',
    '## Baseline output path safety', '## Filesystem identity',
    'canonical restore-identity index', 'every reachable package-inventory file'
)
Assert-Contains 'changelog' $changelog @(
    '### Added', 'root-level MSBuild `ProjectExtensions` content as extension data'
)

foreach ($target in @(
    'https://github.com/KeelMatrix/PackageSurface/blob/main/README.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/docs/DEV.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/docs/restore-and-baseline-safety.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/SECURITY.md'
)) {
    if (-not $packedReadme.Contains($target, [StringComparison]::Ordinal)) {
        throw "Packed README is missing a documentation link: '$target'."
    }
}
foreach ($path in @($rootReadmePath, $packedReadmePath, $securityPath, $privacyPath, $contributingPath, $developerPath, $referencePath)) {
    Assert-LocalLinks $path
}
Write-Output 'DOCUMENTATION_CONTRACT=PASS consumer commands, audience coverage, safety reference, changelog, and local links.'
