param(
    [Parameter(Mandatory)] [string] $InputPath,
    [string] $CoveragePath,
    [string[]] $ExpectedProjectPath,
    [string[]] $ExpectedFramework = @('net8.0')
)
$ErrorActionPreference = 'Stop'

try { $document = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json }
catch { [Console]::Error.WriteLine('Vulnerability audit did not return valid structured JSON.'); exit 2 }

if ($null -eq $document -or $document -isnot [PSCustomObject] -or $null -eq $document.projects -or $document.projects -isnot [System.Array] -or $document.projects.Count -eq 0) {
    [Console]::Error.WriteLine('Vulnerability audit report has no recognized project graph.')
    exit 2
}

if ([string]::IsNullOrWhiteSpace($CoveragePath)) { $CoveragePath = $InputPath }
try { $coverage = Get-Content -LiteralPath $CoveragePath -Raw | ConvertFrom-Json }
catch { [Console]::Error.WriteLine('Vulnerability coverage report did not return valid structured JSON.'); exit 2 }
if ($null -eq $coverage -or $coverage -isnot [PSCustomObject] -or $null -eq $coverage.projects -or $coverage.projects -isnot [System.Array] -or $coverage.projects.Count -eq 0) {
    [Console]::Error.WriteLine('Vulnerability coverage report has no recognized project graph.')
    exit 2
}

if ($null -eq $ExpectedProjectPath -or $ExpectedProjectPath.Count -eq 0) {
    $ExpectedProjectPath = @($coverage.projects | ForEach-Object { $_.path })
}
$expectedProjectValues = if ($ExpectedProjectPath.Count -eq 1 -and $ExpectedProjectPath[0].Contains('|', [StringComparison]::Ordinal)) { $ExpectedProjectPath[0].Split('|', [StringSplitOptions]::RemoveEmptyEntries) } else { $ExpectedProjectPath }
$expectedProjects = @($expectedProjectValues | ForEach-Object { [IO.Path]::GetFullPath($_) })
$coverageByPath = @{}
foreach ($project in @($coverage.projects)) {
    if ($null -eq $project.path -or [string]::IsNullOrWhiteSpace([string]$project.path) -or $null -eq $project.frameworks -or $project.frameworks -isnot [System.Array] -or $project.frameworks.Count -eq 0) {
        [Console]::Error.WriteLine('Vulnerability coverage contains a project without framework records.')
        exit 2
    }
    try { $normalized = [IO.Path]::GetFullPath([string]$project.path) } catch { [Console]::Error.WriteLine('Vulnerability coverage contains an invalid project path.'); exit 2 }
    $coverageByPath[$normalized] = $project
    foreach ($framework in @($project.frameworks)) {
        if ($null -eq $framework.framework -or [string]::IsNullOrWhiteSpace([string]$framework.framework)) {
            [Console]::Error.WriteLine('Vulnerability coverage contains an unrecognized framework record.')
            exit 2
        }
    }
}
foreach ($expected in $expectedProjects) {
    if (-not $coverageByPath.ContainsKey($expected)) { [Console]::Error.WriteLine("Vulnerability coverage is missing expected project '$expected'."); exit 2 }
    foreach ($framework in $ExpectedFramework) {
        if (-not @($coverageByPath[$expected].frameworks | Where-Object { $_.framework -eq $framework })) { [Console]::Error.WriteLine("Vulnerability coverage is missing framework '$framework' for '$expected'."); exit 2 }
    }
}

$reportedProjects = @($document.projects | ForEach-Object { $_.path } | Where-Object { $_ })
$reportedByPath = @{}
foreach ($reported in $reportedProjects) {
    try { $normalized = [IO.Path]::GetFullPath([string]$reported) } catch { [Console]::Error.WriteLine('Vulnerability report contains an invalid project path.'); exit 2 }
    if (-not $coverageByPath.ContainsKey($normalized)) { [Console]::Error.WriteLine("Vulnerability report names an unexpected project '$reported'."); exit 2 }
    $reportedByPath[$normalized] = $true
}
foreach ($expected in $expectedProjects) {
    if (-not $reportedByPath.ContainsKey($expected)) { [Console]::Error.WriteLine("Vulnerability report is missing expected project '$expected'."); exit 2 }
}

function Find-Vulnerabilities($Value) {
    if ($null -eq $Value) { return @() }
    if ($Value -is [System.Array]) {
        $found = @(); foreach ($item in $Value) { $found += Find-Vulnerabilities $item }; return $found
    }
    if ($Value -is [PSCustomObject]) {
        $found = @()
        foreach ($property in $Value.PSObject.Properties) {
            if ($property.Name -eq 'vulnerabilities' -and $null -ne $property.Value -and @($property.Value).Count -gt 0) { $found += @($property.Value) }
            $found += Find-Vulnerabilities $property.Value
        }
        return $found
    }
    return @()
}

$vulnerabilities = @(Find-Vulnerabilities $document)
if ($vulnerabilities.Count -gt 0) {
    [Console]::Error.WriteLine('Applicable package vulnerabilities were reported: ' + ($vulnerabilities | ConvertTo-Json -Compress))
    exit 1
}

Write-Output 'VULNERABILITY_REPORT=PASS'
exit 0
