param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $RequireFinalized,
    [switch] $FirstRelease,
    [string] $ProjectFile = (Join-Path $PSScriptRoot '..\src\KeelMatrix.PackageSurface.Cli\KeelMatrix.PackageSurface.Cli.csproj'),
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)
$ErrorActionPreference = 'Stop'

if ($FirstRelease -and -not $RequireFinalized) {
    throw 'First-release validation requires a finalized changelog entry.'
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw 'Release version must be a semantic three-part version.'
}

$projectText = Get-Content -LiteralPath $ProjectFile -Raw
$versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $versionMatch.Success -or $versionMatch.Groups['version'].Value.Trim() -ne $Version) {
    throw 'Project package version does not match the requested release version.'
}

foreach ($propertyName in @('PackageVersion', 'VersionPrefix')) {
    $match = [regex]::Match($projectText, "<$propertyName>\s*(?<version>[^<]+?)\s*</$propertyName>", [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success -and $match.Groups['version'].Value.Trim() -ne $Version) {
        throw "$propertyName does not match the requested release version."
    }
}

$packageId = [regex]::Match($projectText, '<PackageId>\s*(?<id>[^<]+?)\s*</PackageId>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($packageId.Success -and $packageId.Groups['id'].Value.Trim() -ne 'KeelMatrix.PackageSurface') {
    throw 'Project package ID does not match the intended package.'
}

$commandLinePath = Join-Path (Split-Path -Parent $ProjectFile) 'CommandLine.cs'
if (Test-Path -LiteralPath $commandLinePath -PathType Leaf) {
    $commandLineText = Get-Content -LiteralPath $commandLinePath -Raw
    $toolVersion = [regex]::Match($commandLineText, 'ToolVersion\s*=\s*"(?<version>[^"]+)"')
    if ($toolVersion.Success -and $toolVersion.Groups['version'].Value -ne $Version) {
        throw 'CLI tool version does not match the requested release version.'
    }
}

$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$headerPattern = '(?m)^##\s+\[(?<label>[^\]\r\n]+)\](?<suffix>\s+-\s+(?<date>[^\s]+))?\s*$'
$headers = @([regex]::Matches($changelog, $headerPattern))
$sections = for ($index = 0; $index -lt $headers.Count; $index++) {
    $header = $headers[$index]
    $bodyStart = $header.Index + $header.Length
    $bodyEnd = if ($index + 1 -lt $headers.Count) { $headers[$index + 1].Index } else { $changelog.Length }
    [pscustomobject]@{
        Label = $header.Groups['label'].Value.Trim()
        Date = if ($header.Groups['date'].Success) { $header.Groups['date'].Value.Trim() } else { $null }
        Body = $changelog.Substring($bodyStart, $bodyEnd - $bodyStart)
    }
}

function Get-MeaningfulReleaseNotes([string] $Body) {
    @($Body -split "`r?`n" |
        ForEach-Object { $_.Trim() } |
        Where-Object { $_ -match '^(?:[-*+]\s+|\d+[.)]\s+)\S' -and $_ -notmatch '^<!--' })
}

$targetSections = @($sections | Where-Object { $_.Label -ceq $Version })
$unreleasedSections = @($sections | Where-Object { $_.Label -ceq 'Unreleased' })
if ($unreleasedSections.Count -gt 1) {
    throw 'Changelog contains duplicate Unreleased sections.'
}

if ($RequireFinalized) {
    if ($targetSections.Count -ne 1) {
        throw 'Exactly one finalized target release section is required.'
    }

    $target = $targetSections[0]
    if ([string]::IsNullOrWhiteSpace($target.Date) -or $target.Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
        throw 'Finalized release changelog entry with an ISO date is required.'
    }

    $parsedDate = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($target.Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) {
        throw 'Finalized release changelog date is not a valid calendar date.'
    }

    if (@(Get-MeaningfulReleaseNotes $target.Body).Count -eq 0) {
        throw 'Finalized target release section must contain externally meaningful release notes.'
    }

    if ($target.Body -match '(?i)\b(?:planned|tbd|unreleased|not[\s-]+yet[\s-]+published|not[\s-]+published)\b') {
        throw 'Finalized target release section contains an unfinalized release marker.'
    }

    if ($FirstRelease) {
        if ($target.Body -notmatch '(?im)^\s*#{2,6}\s+Added\s*:?[ \t]*$') {
            throw 'First-release notes must contain an Added section.'
        }

        $prohibitedMarkers = @(
            '\bnow\b',
            '\bno\s+longer\b',
            '\bpreviously\b',
            '\bformerly\b',
            '\bused\s+to\b',
            '\bfixed\b',
            '\bfixes\b',
            '\bcorrected\b',
            '\bresolved\b',
            '\baddressed\b',
            '\bthis\s+removes\b',
            '\bthis\s+fixes\b',
            '\bchanged\s+from\b'
        )
        foreach ($marker in $prohibitedMarkers) {
            if ($target.Body -match "(?i)$marker") {
                throw "First-release notes contain prohibited remediation wording: $marker."
            }
        }

        $prohibitedCategories = [regex]::Matches(
            $target.Body,
            '(?im)^\s*#{2,6}\s+(?<category>Changed|Fixed|Deprecated|Removed|Security|Compatibility)\s*:?[ \t]*$') |
            ForEach-Object { $_.Groups['category'].Value }
        if ($prohibitedCategories.Count -gt 0) {
            throw "First-release notes contain non-Added release categories: $($prohibitedCategories -join ', ')."
        }
    }

    if ($unreleasedSections.Count -eq 1 -and @(Get-MeaningfulReleaseNotes $unreleasedSections[0].Body).Count -gt 0) {
        throw 'Release notes for the finalized target remain under Unreleased.'
    }
}
elseif ($targetSections.Count -eq 0 -and $unreleasedSections.Count -eq 0) {
    throw 'Changelog must contain either the requested version entry or an Unreleased entry during candidate review.'
}

Write-Output "RELEASE_CONTRACT=PASS VERSION=$Version FINALIZED=$RequireFinalized"
