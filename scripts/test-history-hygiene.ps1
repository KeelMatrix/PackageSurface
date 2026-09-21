$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root

function Convert-CodePoints([int[]] $points) {
    return -join ($points | ForEach-Object { [char]$_ })
}

$restricted = @(
    (Convert-CodePoints @(80,97,112,101,114,99,108,105,112)),
    (Convert-CodePoints @(67,111,100,101,120)),
    (Convert-CodePoints @(97,103,101,110,116)),
    (Convert-CodePoints @(109,111,100,101,108)),
    (Convert-CodePoints @(112,114,111,109,112,116)),
    (Convert-CodePoints @(111,114,99,104,101,115,116,114,97,116)),
    (Convert-CodePoints @(105,110,116,101,114,110,97,108)),
    (Convert-CodePoints @(99,111,109,112,97,110,121)),
    (Convert-CodePoints @(116,97,115,107)),
    (Convert-CodePoints @(99,111,45,97,117,116,104,111,114,101,100,45,98,121))
)
$pattern = ($restricted | ForEach-Object { [regex]::Escape($_) }) -join '|'
$violations = [System.Collections.Generic.List[string]]::new()

$tracked = @(git ls-files)
foreach ($path in $tracked) {
    if (git grep -n -I -i -E $pattern -- $path 2>$null) {
        $violations.Add("working tree: $path")
    }
}

foreach ($commit in @(git rev-list --all)) {
    if (git grep -n -I -i -E $pattern $commit -- 2>$null) {
        $violations.Add("history: $commit")
    }
}

$historyText = git log --all --format='%H%n%an%n%ae%n%cn%n%ce%n%B'
if ($historyText -match $pattern) {
    $violations.Add('history metadata')
}

if ($violations.Count -gt 0) {
    Write-Error ("Restricted text found: " + ($violations -join ', '))
    exit 1
}

Write-Output 'PASS: tracked material and complete history contain no restricted coordination markers.'
