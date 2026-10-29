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

$rootReadme = Get-Content -Raw (Join-Path $RepositoryRoot 'README.md')
$packedReadme = Get-Content -Raw (Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Cli/README.md')
$platformContract = 'PackageSurface supports Windows, Linux, and macOS for SDK-style PackageReference restore outputs; hosted CI validates the command contract on all three platforms.'
if (-not $rootReadme.Contains($platformContract, [StringComparison]::Ordinal) -or
    -not $packedReadme.Contains($platformContract, [StringComparison]::Ordinal)) {
    throw 'Root and packed README files do not state the same evidenced platform contract.'
}

$consumerCommands = @(
    'dotnet tool install --global KeelMatrix.PackageSurface',
    'dotnet tool update --global KeelMatrix.PackageSurface',
    'dotnet tool uninstall --global KeelMatrix.PackageSurface',
    'dotnet restore',
    'package-surface scan MySolution.sln',
    'package-surface baseline MySolution.sln --output package-surface.json',
    'package-surface check MySolution.sln --baseline package-surface.json'
)
foreach ($command in $consumerCommands) {
    if (-not $rootReadme.Contains($command, [StringComparison]::Ordinal) -or
        -not $packedReadme.Contains($command, [StringComparison]::Ordinal)) {
        throw "Root and packed README files must keep this consumer command in sync: '$command'."
    }
}

foreach ($section in @('## Install', '## Quick start', '## Limitations', '## Privacy', '## Documentation')) {
    if (-not $packedReadme.Contains($section, [StringComparison]::Ordinal)) {
        throw "Packed README is missing its consumer section '$section'."
    }
}
foreach ($target in @(
    'https://github.com/KeelMatrix/PackageSurface/blob/main/README.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/docs/DEV.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/PRIVACY.md',
    'https://github.com/KeelMatrix/PackageSurface/blob/main/SECURITY.md'
)) {
    if (-not $packedReadme.Contains($target, [StringComparison]::Ordinal)) {
        throw "Packed README is missing a canonical documentation link: '$target'."
    }
}
if (-not $packedReadme.Contains('--no-telemetry', [StringComparison]::Ordinal) -or
    -not $packedReadme.Contains('does not restore packages, evaluate MSBuild, load dependency assemblies', [StringComparison]::Ordinal)) {
    throw 'Packed README must state the privacy option and key analysis limitations.'
}

$securityPath = Join-Path $RepositoryRoot 'SECURITY.md'
$security = Get-Content -Raw $securityPath
if ($security.Contains("conduct-reporting route", [StringComparison]::OrdinalIgnoreCase)) {
    throw 'SECURITY.md names a conduct-reporting route that is not defined by this repository.'
}
$issueRoute = 'https://github.com/KeelMatrix/PackageSurface/issues'
$uri = $null
if (-not $security.Contains($issueRoute, [StringComparison]::Ordinal) -or -not [Uri]::TryCreate($issueRoute, [UriKind]::Absolute, [ref]$uri)) {
    throw 'SECURITY.md does not define a resolvable ordinary-bug reporting route.'
}

$documents = @(
    (Join-Path $RepositoryRoot 'README.md'),
    (Join-Path $RepositoryRoot 'SECURITY.md'),
    (Join-Path $RepositoryRoot 'PRIVACY.md'),
    (Join-Path $RepositoryRoot 'CONTRIBUTING.md'),
    (Join-Path $RepositoryRoot 'docs/DEV.md'),
    (Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Cli/README.md')
)
foreach ($document in $documents) {
    $text = Get-Content -Raw $document
    foreach ($match in [regex]::Matches($text, '\[[^\]]+\]\((?<target>[^)]+)\)')) {
        $target = $match.Groups['target'].Value.Trim()
        if ($target.StartsWith('#', [StringComparison]::Ordinal) -or $target -match '^[A-Za-z][A-Za-z0-9+.-]*://') { continue }
        $target = $target.Split('#', 2)[0]
        if ([string]::IsNullOrWhiteSpace($target)) { continue }
        $resolved = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetDirectoryName($document)) $target.Replace('/', [IO.Path]::DirectorySeparatorChar)))
        if (-not (Test-Path -LiteralPath $resolved -PathType Leaf)) {
            throw "Documentation link '$target' in '$document' does not resolve to a file."
        }
    }
}

Write-Output 'DOCUMENTATION_CONTRACT=PASS platform wording, reporting routes, and repository links are resolvable.'
