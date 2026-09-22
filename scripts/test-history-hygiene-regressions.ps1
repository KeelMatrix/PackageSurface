$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $root 'scripts/test-history-hygiene.ps1'
$pwsh = (Get-Command pwsh -ErrorAction Stop).Source
$scratchParent = [IO.Path]::GetTempPath()
$scratch = Join-Path $scratchParent ('packagesurface-hygiene-' + [Guid]::NewGuid().ToString('N'))

function Invoke-External {
    param([string] $FilePath, [string[]] $Arguments)

    $outputItems = @(& $FilePath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output = ($outputItems | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($exitCode -ne 0) {
        throw "$FilePath $($Arguments -join ' ') failed with exit code $exitCode. $output"
    }

    return $output.Trim()
}

function New-Repository {
    param([string] $Path, [switch] $WithMarker, [switch] $TwoCommits)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-External 'git' @('init', '--quiet', '--', $Path) | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.name', 'Fixture') | Out-Null
    $content = if ($WithMarker) {
        $marker = -join (@(80,97,112,101,114,99,108,105,112) | ForEach-Object { [char]$_ })
        "fixture $marker"
    }
    else {
        'fixture content'
    }
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), $content)
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture') | Out-Null
    if ($TwoCommits) {
        [IO.File]::AppendAllText((Join-Path $Path 'tracked.txt'), [Environment]::NewLine + 'second')
        Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
        Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture-second') | Out-Null
    }
}

function Invoke-Gate {
    param([string] $Path, [string] $PathPrefix, [string] $Scenario)

    $oldPath = $env:PATH
    $oldScenario = $env:HISTORY_HYGIENE_SCENARIO
    try {
        if (-not [string]::IsNullOrWhiteSpace($PathPrefix)) {
            $env:PATH = $PathPrefix + [IO.Path]::PathSeparator + $oldPath
        }
        $env:HISTORY_HYGIENE_SCENARIO = $Scenario

        $gitCommand = if ([string]::IsNullOrWhiteSpace($PathPrefix)) {
            'git'
        }
        else {
            $portableShim = Join-Path $PathPrefix 'git.ps1'
            if (Test-Path -LiteralPath $portableShim) {
                $portableShim
            }
            else {
                $legacyShimName = if ($IsWindows) { 'git.cmd' } else { 'git' }
                Join-Path $PathPrefix $legacyShimName
            }
        }
        $outputItems = @(& $pwsh '-NoLogo' '-NoProfile' '-File' $gate '-RepositoryRoot' $Path '-GitCommandPath' $gitCommand 2>&1)
        $restricted = @(
            (-join (@(80,97,112,101,114,99,108,105,112) | ForEach-Object { [char]$_ })),
            (-join (@(67,111,100,101,120) | ForEach-Object { [char]$_ })),
            (-join (@(97,103,101,110,116) | ForEach-Object { [char]$_ })),
            (-join (@(109,111,100,101,108) | ForEach-Object { [char]$_ })),
            (-join (@(112,114,111,109,112,116) | ForEach-Object { [char]$_ })),
            (-join (@(111,114,99,104,101,115,116,114,97,116) | ForEach-Object { [char]$_ })),
            (-join (@(105,110,116,101,114,110,97,108) | ForEach-Object { [char]$_ })),
            (-join (@(99,111,109,112,97,110,121) | ForEach-Object { [char]$_ })),
            (-join (@(99,111,45,97,117,116,104,111,114,101,100,45,98,121) | ForEach-Object { [char]$_ }))
        )
        $redactionPattern = ($restricted | ForEach-Object { [regex]::Escape($_) }) -join '|'
        $output = (($outputItems | ForEach-Object { [string]$_ }) -join [Environment]::NewLine).Trim()
        $output = [regex]::Replace($output, $redactionPattern, '[restricted-marker]', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        return [pscustomobject]@{
            ExitCode = $LASTEXITCODE
            Output = $output
        }
    }
    finally {
        $env:PATH = $oldPath
        $env:HISTORY_HYGIENE_SCENARIO = $oldScenario
    }
}

function Assert-ExpectedFailure {
    param([string] $Name, [string] $Path, [string] $ExpectedText, [string] $PathPrefix, [string] $Scenario)

    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $result = Invoke-Gate $Path $PathPrefix $Scenario
    $stopwatch.Stop()
    Write-Output "CASE: $Name"
    Write-Output "EXIT_CODE: $($result.ExitCode)"
    Write-Output "DURATION_MS: $($stopwatch.ElapsedMilliseconds)"
    if ($result.ExitCode -eq 0) {
        throw "$Name unexpectedly passed."
    }
    if ($result.Output -notmatch [regex]::Escape($ExpectedText)) {
        throw "$Name did not report '$ExpectedText'."
    }
    Write-Output "PASS: $Name rejected with the expected diagnostic class '$ExpectedText'."
}

New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    $gitFailureRoot = Join-Path $scratch 'git-failure'
    New-Repository $gitFailureRoot
    $shimDirectory = Join-Path $scratch 'git-shim'
    New-Item -ItemType Directory -Force -Path $shimDirectory | Out-Null
    if ($IsWindows) {
        [IO.File]::WriteAllText((Join-Path $shimDirectory 'git.cmd'), "@echo simulated git failure 1>&2`r`n@exit /b 128`r`n")
    }
    else {
        $shim = Join-Path $shimDirectory 'git'
        [IO.File]::WriteAllText($shim, "#!/bin/sh`nexit 128`n")
        Invoke-External 'chmod' @('+x', $shim) | Out-Null
    }
    Assert-ExpectedFailure 'git-call-failure' $gitFailureRoot 'git rev-parse' $shimDirectory

    $outputShimDirectory = Join-Path $scratch 'output-shim'
    New-Item -ItemType Directory -Force -Path $outputShimDirectory | Out-Null
    $outputShim = @'
$Arguments = $args
$scenario = $env:HISTORY_HYGIENE_SCENARIO
switch ($Arguments[0]) {
    'rev-parse' {
        switch ($Arguments[1]) {
            '--is-inside-work-tree' { Write-Output 'true'; exit 0 }
            '--show-toplevel' { Write-Output (Get-Location).Path; exit 0 }
            '--is-shallow-repository' { Write-Output 'false'; exit 0 }
            'HEAD' { Write-Output ('1' * 40); exit 0 }
        }
    }
    'ls-files' {
        if ($scenario -eq 'empty-tracked') { exit 0 }
        Write-Output 'tracked.txt'; exit 0
    }
    'rev-list' {
        if ($scenario -eq 'empty-history') { exit 0 }
        if ($scenario -eq 'incomplete-history') { Write-Output ('2' * 40); exit 0 }
        Write-Output ('1' * 40); exit 0
    }
    'grep' {
        if ($scenario -eq 'error-grep') { exit 128 }
        exit 1
    }
    'log' {
        if ($scenario -eq 'empty-log') { exit 0 }
        if ($scenario -eq 'error-log') { exit 128 }
        Write-Output ('1' * 40); exit 0
    }
}
exit 128
'@
    $outputShimPath = Join-Path $outputShimDirectory 'git.ps1'
    [IO.File]::WriteAllText($outputShimPath, $outputShim)
    $outputCases = @(
        @{ Name = 'empty-tracked-output'; Scenario = 'empty-tracked'; Expected = 'returned no required output' },
        @{ Name = 'empty-history-output'; Scenario = 'empty-history'; Expected = 'returned no required output' },
        @{ Name = 'incomplete-history-output'; Scenario = 'incomplete-history'; Expected = 'does not include HEAD' },
        @{ Name = 'git-grep-failure'; Scenario = 'error-grep'; Expected = 'git grep' },
        @{ Name = 'empty-log-output'; Scenario = 'empty-log'; Expected = 'git log --all' },
        @{ Name = 'git-log-failure'; Scenario = 'error-log'; Expected = 'git log' }
    )
    foreach ($case in $outputCases) {
        Assert-ExpectedFailure $case.Name $gitFailureRoot $case.Expected $outputShimDirectory $case.Scenario
    }

    $sourceRoot = Join-Path $scratch 'source'
    New-Repository $sourceRoot -TwoCommits
    $shallowRoot = Join-Path $scratch 'shallow'
    $sourceUri = if ($IsWindows) { 'file:///' + $sourceRoot.Replace('\', '/') } else { 'file://' + $sourceRoot }
    Invoke-External 'git' @('-c', 'protocol.file.allow=always', 'clone', '--quiet', '--depth', '1', $sourceUri, $shallowRoot) | Out-Null
    Assert-ExpectedFailure 'shallow-repository' $shallowRoot 'repository is shallow' ''

    $markerRoot = Join-Path $scratch 'marker'
    New-Repository $markerRoot -WithMarker
    Assert-ExpectedFailure 'restricted-marker' $markerRoot 'Restricted text found' ''

    Write-Output 'PASS: hygiene gate rejects command failure, shallow history, and a tracked restricted marker in disposable repositories.'
    exit 0
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
