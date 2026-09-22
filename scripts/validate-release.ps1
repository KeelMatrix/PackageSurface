param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $RequireFinalized,
    [string] $ProjectFile = (Join-Path $PSScriptRoot '..\src\KeelMatrix.PackageSurface.Cli\KeelMatrix.PackageSurface.Cli.csproj'),
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)
$ErrorActionPreference = 'Stop'

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw 'Release version must be a semantic three-part version.'
}

$projectText = Get-Content -LiteralPath $ProjectFile -Raw
$versionMatch = [regex]::Match($projectText, '<Version>(?<version>[^<]+)</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $versionMatch.Success -or $versionMatch.Groups['version'].Value -ne $Version) {
    throw 'Project package version does not match the requested release version.'
}

$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$versionHeader = [regex]::Match($changelog, "(?m)^## \[$([regex]::Escape($Version))\](?:\s+-\s+\d{4}-\d{2}-\d{2})?\s*$")
$unreleasedHeader = [regex]::Match($changelog, '(?m)^## \[Unreleased\]\s*$')
if ($RequireFinalized) {
    if (-not $versionHeader.Success -or $versionHeader.Value -notmatch '\d{4}-\d{2}-\d{2}') {
        throw 'Finalized release changelog entry with an ISO date is required.'
    }
}
elseif (-not $versionHeader.Success -and -not $unreleasedHeader.Success) {
    throw 'Changelog must contain either the requested version entry or an Unreleased entry during candidate review.'
}

if ($versionHeader.Success -and $changelog -match '(?m)^## \[(?:Planned|TBD|not yet published)\]') {
    throw 'Changelog contains an unfinalized release marker.'
}

Write-Output "RELEASE_CONTRACT=PASS VERSION=$Version FINALIZED=$RequireFinalized"
