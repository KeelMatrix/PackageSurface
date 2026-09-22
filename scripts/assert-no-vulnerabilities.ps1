param(
    [Parameter(Mandatory)] [string] $InputPath
)
$ErrorActionPreference = 'Stop'

try { $document = Get-Content -LiteralPath $InputPath -Raw | ConvertFrom-Json }
catch { Write-Error 'Vulnerability audit did not return valid structured JSON.'; exit 2 }

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
    Write-Error ('Applicable package vulnerabilities were reported: ' + ($vulnerabilities | ConvertTo-Json -Compress))
    exit 1
}

Write-Output 'VULNERABILITY_REPORT=PASS'
exit 0
