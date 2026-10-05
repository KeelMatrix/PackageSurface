param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Version,
    [Parameter(Mandatory)] [string] $ArtifactDirectory
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($Version)) { throw 'Release version is required for artifact validation.' }
if ($Version -cne $Version.Trim()) { throw 'Release version cannot include surrounding whitespace.' }
if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw 'Release version must be a semantic three-part version.'
}
if (-not (Test-Path -LiteralPath $ArtifactDirectory -PathType Container)) {
    throw "Release artifact directory does not exist: $ArtifactDirectory"
}

$expectedNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
[void]$expectedNames.Add("KeelMatrix.PackageSurface.$Version.nupkg")
[void]$expectedNames.Add("KeelMatrix.PackageSurface.$Version.snupkg")

$entries = @(Get-ChildItem -LiteralPath $ArtifactDirectory -Force)
$actualNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($entry in $entries) {
    if ($entry.PSIsContainer -or (($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0)) {
        throw "Release artifact directory contains a non-regular artifact entry: $($entry.Name)"
    }
    [void]$actualNames.Add($entry.Name)
}

if ($entries.Count -ne 2 -or -not $expectedNames.SetEquals($actualNames)) {
    $actual = @($entries | ForEach-Object Name | Sort-Object) -join ', '
    throw "Release artifact set for version '$Version' is not exact: $actual"
}

Write-Output "RELEASE_ARTIFACT_SET=PASS VERSION=$Version"
