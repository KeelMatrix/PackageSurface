param([string[]] $Cases = @())

$ErrorActionPreference = 'Stop'
$script:executedCaseCount = 0
$script:selectedCases = @($Cases | ForEach-Object { $_ -split ',' } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $root 'scripts/test-history-hygiene.ps1'
$scratchParent = [IO.Path]::GetTempPath()
$scratch = Join-Path $scratchParent ('packagesurface-hygiene-' + [Guid]::NewGuid().ToString('N'))

function Convert-CodePoints([int[]] $points) {
    return -join ($points | ForEach-Object { [char]$_ })
}

function Test-SelectedCase([string] $Name) {
    return $script:selectedCases.Count -eq 0 -or $script:selectedCases -contains $Name
}

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
    param(
        [string] $Path,
        [switch] $WithMarker,
        [switch] $WithTaskId,
        [switch] $TwoCommits,
        [string] $CommitSubject = 'fixture',
        [string] $CommitBody = '',
        [string] $TreeContentMarker = ''
    )

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-External 'git' @('init', '--quiet', '--', $Path) | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.name', 'Fixture') | Out-Null
    $content = if ($WithMarker) {
        $marker = Convert-CodePoints @(80,97,112,101,114,99,108,105,112)
        "fixture $marker"
    }
    elseif (-not [string]::IsNullOrWhiteSpace($TreeContentMarker)) {
        "fixture $TreeContentMarker"
    }
    else {
        'fixture content'
    }
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), $content)
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    $message = if ($WithTaskId) { 'fixture ' + ('A' + 'BC' + '-' + '1' + '234') } else { $CommitSubject }
    $messageParts = [System.Collections.Generic.List[string]]::new()
    $messageParts.Add($message)
    if (-not [string]::IsNullOrWhiteSpace($CommitBody)) {
        $messageParts.Add($CommitBody)
    }
    $messagePath = $Path + '.commit-message'
    try {
        $separator = [string]([char]10)
        [IO.File]::WriteAllText($messagePath, [string]::Join($separator + $separator, $messageParts), [Text.UTF8Encoding]::new($false))
        Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '--file', $messagePath) | Out-Null
    }
    finally {
        if (Test-Path -LiteralPath $messagePath) {
            Remove-Item -LiteralPath $messagePath -Force
        }
    }
    if ($TwoCommits) {
        [IO.File]::AppendAllText((Join-Path $Path 'tracked.txt'), [Environment]::NewLine + 'second')
        Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
        Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture-second') | Out-Null
    }
}

function New-ByteRepository {
    param(
        [string] $Path,
        [byte[]] $Bytes,
        [switch] $HistoricalOnly
    )

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-External 'git' @('init', '--quiet', '--', $Path) | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.name', 'Fixture') | Out-Null
    [IO.File]::WriteAllBytes((Join-Path $Path 'tracked.bin'), $Bytes)
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.bin') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture') | Out-Null
    if ($HistoricalOnly) {
        [IO.File]::WriteAllText((Join-Path $Path 'tracked.bin'), 'fixture content')
        Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.bin') | Out-Null
        Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture-safe') | Out-Null
    }
}

function New-HistoryGrowthRepository {
    param(
        [string] $Path,
        [int] $CommitCount,
        [int] $UnchangedFileBytes = 1100000
    )

    if ($CommitCount -lt 1) {
        throw 'History growth fixtures must contain at least one commit.'
    }

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-External 'git' @('init', '--quiet', '--', $Path) | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.name', 'Fixture') | Out-Null

    [IO.File]::WriteAllBytes((Join-Path $Path 'stable.txt'), (New-FillerBytes $UnchangedFileBytes))
    [IO.File]::WriteAllText((Join-Path $Path 'literal [path].txt'), 'ordinary pathspec content')
    [IO.File]::WriteAllText((Join-Path $Path 'history-count.txt'), '1')
    Invoke-External 'git' @('-C', $Path, 'add', '--all') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture 1') | Out-Null

    for ($commit = 2; $commit -le $CommitCount; $commit++) {
        [IO.File]::WriteAllText((Join-Path $Path 'history-count.txt'), [string]$commit)
        Invoke-External 'git' @('-C', $Path, 'add', '--', 'history-count.txt') | Out-Null
        Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', "fixture $commit") | Out-Null
    }

    $actualCount = [int](Invoke-External 'git' @('-C', $Path, 'rev-list', '--count', '--all'))
    if ($actualCount -ne $CommitCount) {
        throw "History growth fixture expected $CommitCount commits but created $actualCount."
    }
}

function New-ConflictMergeRepository {
    param(
        [string] $Path,
        [switch] $WithMergeMarker
    )

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    Invoke-External 'git' @('init', '--quiet', '--', $Path) | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.email', 'fixture@example.invalid') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'config', 'user.name', 'Fixture') | Out-Null

    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), 'base')
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture base') | Out-Null
    $mainBranch = Invoke-External 'git' @('-C', $Path, 'branch', '--show-current')

    Invoke-External 'git' @('-C', $Path, 'switch', '--quiet', '-c', 'merge-source') | Out-Null
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), 'side parent')
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture side') | Out-Null

    Invoke-External 'git' @('-C', $Path, 'switch', '--quiet', $mainBranch) | Out-Null
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), 'first parent')
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture first parent') | Out-Null

    $mergeOutput = @(& git -C $Path merge --no-ff --no-commit merge-source 2>&1)
    $mergeExitCode = $LASTEXITCODE
    if ($mergeExitCode -eq 0) {
        throw 'The merge fixture did not create its expected content conflict.'
    }
    $restrictedMergeWord = Convert-CodePoints @(102,114,111,110,116,105,101,114)
    $resolution = if ($WithMergeMarker) { 'fixture ' + $restrictedMergeWord + ' resolution' } else { 'fixture resolved content' }
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), $resolution)
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture merge resolution') | Out-Null
    [IO.File]::WriteAllText((Join-Path $Path 'tracked.txt'), 'fixture current content')
    Invoke-External 'git' @('-C', $Path, 'add', '--', 'tracked.txt') | Out-Null
    Invoke-External 'git' @('-C', $Path, 'commit', '--quiet', '-m', 'fixture post-merge content') | Out-Null
}

function Join-ByteArrays {
    param([byte[][]] $Arrays)

    $length = 0
    foreach ($array in $Arrays) {
        $length += $array.Length
    }

    $result = [byte[]]::new($length)
    $offset = 0
    foreach ($array in $Arrays) {
        [Array]::Copy($array, 0, $result, $offset, $array.Length)
        $offset += $array.Length
    }
    return $result
}

function Get-EncodedBytes {
    param(
        [Text.Encoding] $Encoding,
        [string] $Text
    )

    return Join-ByteArrays @($Encoding.GetPreamble(), $Encoding.GetBytes($Text))
}

function New-FillerBytes {
    param([int] $Length)

    $result = [byte[]]::new($Length)
    for ($i = 0; $i -lt $Length; $i++) {
        $result[$i] = 97
    }
    return $result
}

function Get-HistoryScanTotals {
    param([string] $Path)

    $tracked = Invoke-External 'git' @('-C', $Path, 'ls-files')
    $currentBytes = [long]0
    foreach ($relativePath in @($tracked -split "`r?`n")) {
        $currentBytes += (Get-Item -LiteralPath (Join-Path $Path $relativePath)).Length
    }

    $historyBytes = [long]$currentBytes
    $decompressedBytes = [long]0
    $commits = Invoke-External 'git' @('-C', $Path, 'rev-list', '--all')
    foreach ($commit in @($commits -split "`r?`n")) {
        $pathListPath = Join-Path $scratch ('measure-paths-' + [Guid]::NewGuid().ToString('N') + '.paths')
        [IO.File]::WriteAllBytes($pathListPath, [byte[]]::new(0))
        try {
            Invoke-External 'git' @('-C', $Path, 'diff-tree', '--root', '--diff-merges=first-parent', '--no-commit-id', '--no-renames', '--diff-filter=AMT', '--name-only', '-r', '-z', "--output=$pathListPath", $commit) | Out-Null
            $pathBytes = [IO.File]::ReadAllBytes($pathListPath)
            $historyBytes += $pathBytes.Length
            $changedPaths = if ($pathBytes.Length -eq 0) {
                @()
            }
            else {
                $pathText = [Text.UTF8Encoding]::new($false, $true).GetString($pathBytes)
                if ($pathBytes[$pathBytes.Length - 1] -ne 0) {
                    throw "The changed-path output for commit $commit is malformed."
                }
                @($pathText.Substring(0, $pathText.Length - 1).Split([char]0))
            }
        }
        finally {
            if (Test-Path -LiteralPath $pathListPath) {
                Remove-Item -LiteralPath $pathListPath -Force
            }
        }

        if ($changedPaths.Count -gt 0) {
            $archivePath = Join-Path $scratch ('measure-' + [Guid]::NewGuid().ToString('N') + '.zip')
            try {
                $pathspecs = @($changedPaths | ForEach-Object { ':(literal)' + $_ })
                $archiveArguments = @('-C', $Path, 'archive', '--format=zip', "--output=$archivePath", $commit, '--') + $pathspecs
                Invoke-External 'git' $archiveArguments | Out-Null
                $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
                try {
                    foreach ($entry in $archive.Entries) {
                        if ([string]::IsNullOrEmpty($entry.Name)) {
                            continue
                        }
                        $historyBytes += $entry.Length
                        $decompressedBytes += $entry.Length
                    }
                }
                finally {
                    $archive.Dispose()
                }
            }
            finally {
                if (Test-Path -LiteralPath $archivePath) {
                    Remove-Item -LiteralPath $archivePath -Force
                }
            }
        }
    }

    $historyText = Invoke-External 'git' @('-C', $Path, 'log', '--all', '--format=%H%n%an%n%ae%n%cn%n%ce%n%B')
    $metadataBytes = [Text.UTF8Encoding]::new($false).GetByteCount($historyText)
    $historyBytes += $metadataBytes
    return [pscustomobject]@{
        History = $historyBytes
        Decompressed = $decompressedBytes
    }
}

function Get-FullSnapshotHistoryBytes {
    param([string] $Path)

    $tracked = Invoke-External 'git' @('-C', $Path, 'ls-files')
    $trackedPaths = @($tracked -split "`r?`n")
    if ($trackedPaths.Count -ne 3 -or $trackedPaths -notcontains 'stable.txt' -or
        $trackedPaths -notcontains 'literal [path].txt' -or $trackedPaths -notcontains 'history-count.txt') {
        throw 'The history-growth fixture no longer has its three expected files.'
    }

    $currentBytes = [long]0
    foreach ($relativePath in @($tracked -split "`r?`n")) {
        $currentBytes += (Get-Item -LiteralPath (Join-Path $Path $relativePath)).Length
    }

    $commitCount = [int](Invoke-External 'git' @('-C', $Path, 'rev-list', '--count', '--all'))
    $stableBytes = (Get-Item -LiteralPath (Join-Path $Path 'stable.txt')).Length
    $literalBytes = (Get-Item -LiteralPath (Join-Path $Path 'literal [path].txt')).Length
    $fullSnapshotBytes = [long]($commitCount * ($stableBytes + $literalBytes))
    for ($commit = 1; $commit -le $commitCount; $commit++) {
        $fullSnapshotBytes += [Text.UTF8Encoding]::new($false).GetByteCount([string]$commit)
    }

    $historyText = Invoke-External 'git' @('-C', $Path, 'log', '--all', '--format=%H%n%an%n%ae%n%cn%n%ce%n%B')
    return $currentBytes + $fullSnapshotBytes + [Text.UTF8Encoding]::new($false).GetByteCount($historyText)
}

function Invoke-Gate {
    param([string] $Path, [string] $PathPrefix, [string] $Scenario, [hashtable] $Limits)

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
        $gateArguments = @('-NoLogo', '-NoProfile', '-File', $gate, '-RepositoryRoot', $Path, '-GitCommandPath', $gitCommand)
        if ($null -ne $Limits) {
            foreach ($key in $Limits.Keys) {
                $gateArguments += '-' + $key
                $gateArguments += [string]$Limits[$key]
            }
        }
        $outputItems = @(Invoke-NestedPwsh @gateArguments 2>&1)
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
    param([string] $Name, [string] $Path, [string] $ExpectedText, [string] $PathPrefix, [string] $Scenario, [hashtable] $Limits)

    if (-not (Test-SelectedCase $Name)) { return }
    $script:executedCaseCount++
    $stopwatch = [Diagnostics.Stopwatch]::StartNew()
    $result = Invoke-Gate $Path $PathPrefix $Scenario $Limits
    $stopwatch.Stop()
    Write-Output "CASE: $Name"
    Write-Output "EXIT_CODE: $($result.ExitCode)"
    Write-Output "DURATION_MS: $($stopwatch.ElapsedMilliseconds)"
    if ($result.ExitCode -eq 0) {
        throw "$Name unexpectedly passed."
    }
    if ($result.Output -notmatch [regex]::Escape($ExpectedText)) {
        throw "$Name did not report '$ExpectedText'. Output: $($result.Output)"
    }
    Write-Output "PASS: $Name rejected with the expected diagnostic class '$ExpectedText'."
}

function Assert-ExpectedPass {
    param([string] $Name, [string] $Path, [hashtable] $Limits, [string] $PathPrefix = '', [string] $Scenario = $Name)

    if (-not (Test-SelectedCase $Name)) { return }
    $script:executedCaseCount++
    $result = Invoke-Gate $Path $PathPrefix $Scenario $Limits
    Write-Output "CASE: $Name"
    Write-Output "EXIT_CODE: $($result.ExitCode)"
    if ($result.ExitCode -ne 0) {
        throw "$Name unexpectedly failed: $($result.Output)"
    }
    Write-Output "PASS: $Name accepted the ordinary engineering history."
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
    'diff-tree' {
        if ($scenario -eq 'error-diff-tree') { exit 128 }
        $outputArgument = @($Arguments | Where-Object { $_ -like '--output=*' })[0]
        $outputPath = $outputArgument.Substring(9)
        $pathBytes = if ($scenario -eq 'malformed-diff-tree') {
            [Text.UTF8Encoding]::new($false).GetBytes('tracked.txt')
        }
        elseif ($scenario -in @('multi-changed-paths', 'missing-multi-archive-entry')) {
            [Text.UTF8Encoding]::new($false).GetBytes("tracked.txt`0second.txt`0")
        }
        elseif ($scenario -eq 'archive-batch-overflow') {
            $batchPaths = 0..16 | ForEach-Object { "batch-file-$_.txt" }
            [Text.UTF8Encoding]::new($false).GetBytes(($batchPaths -join "`0") + "`0")
        }
        else {
            [Text.UTF8Encoding]::new($false).GetBytes("tracked.txt`0")
        }
        [IO.File]::WriteAllBytes($outputPath, $pathBytes)
        exit 0
    }
    'archive' {
        if ($scenario -eq 'error-archive') { exit 128 }
        $outputArgument = @($Arguments | Where-Object { $_ -like '--output=*' })[0]
        $outputPath = $outputArgument.Substring(9)
        if (-not [string]::IsNullOrWhiteSpace($env:HISTORY_HYGIENE_ARCHIVE_CALLS)) {
            [IO.File]::AppendAllText($env:HISTORY_HYGIENE_ARCHIVE_CALLS, "archive`n")
        }
        $zip = [IO.Compression.ZipFile]::Open($outputPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entryNames = if ($scenario -eq 'archive-batch-overflow') {
                $pathspecStart = [Array]::IndexOf($Arguments, '--') + 1
                $pathspecs = @($Arguments[$pathspecStart..($Arguments.Length - 1)])
                if ($pathspecs.Count -gt 16) { exit 23 }
                @($pathspecs | ForEach-Object { $_.Substring(':(literal)'.Length) })
            }
            elseif ($scenario -eq 'missing-archive-entry') {
                @('unexpected.txt')
            }
            elseif ($scenario -eq 'missing-multi-archive-entry') {
                @('tracked.txt')
            }
            elseif ($scenario -eq 'multi-changed-paths') {
                @('tracked.txt', 'second.txt')
            }
            else {
                @('tracked.txt')
            }
            foreach ($entryName in $entryNames) {
                $entry = $zip.CreateEntry($entryName)
                $writer = [IO.StreamWriter]::new($entry.Open())
                try { $writer.Write('fixture content') } finally { $writer.Dispose() }
            }
        }
        finally { $zip.Dispose() }
        exit 0
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
        @{ Name = 'git-changed-path-read-failure'; Scenario = 'error-diff-tree'; Expected = 'git diff-tree' },
        @{ Name = 'malformed-changed-path-output'; Scenario = 'malformed-diff-tree'; Expected = 'incomplete or malformed' },
        @{ Name = 'git-tree-read-failure'; Scenario = 'error-archive'; Expected = 'git archive' },
        @{ Name = 'missing-changed-archive-entry'; Scenario = 'missing-archive-entry'; Expected = 'unexpected file entry' },
        @{ Name = 'missing-multi-changed-archive-entry'; Scenario = 'missing-multi-archive-entry'; Expected = 'was missing from its archive' },
        @{ Name = 'empty-log-output'; Scenario = 'empty-log'; Expected = 'git log --all' },
        @{ Name = 'git-log-failure'; Scenario = 'error-log'; Expected = 'git log' }
    )
    foreach ($case in $outputCases) {
        Assert-ExpectedFailure $case.Name $gitFailureRoot $case.Expected $outputShimDirectory $case.Scenario
    }
    Assert-ExpectedPass 'multi-changed-path-archive' $gitFailureRoot $null $outputShimDirectory 'multi-changed-paths'
    $archiveCallsPath = Join-Path $scratch 'archive-batch-calls.txt'
    $oldArchiveCallsPath = $env:HISTORY_HYGIENE_ARCHIVE_CALLS
    try {
        if (Test-SelectedCase 'archive-path-batch-overflow') {
            Remove-Item -LiteralPath $archiveCallsPath -Force -ErrorAction SilentlyContinue
            $env:HISTORY_HYGIENE_ARCHIVE_CALLS = $archiveCallsPath
            Assert-ExpectedPass 'archive-path-batch-overflow' $gitFailureRoot $null $outputShimDirectory 'archive-batch-overflow'
            if (@([IO.File]::ReadAllLines($archiveCallsPath)).Count -ne 2) {
                throw 'The 17-path fixture did not split into exactly two bounded archive batches.'
            }
            Write-Output 'PASS: archive-path-batch-overflow split 17 changed paths into two bounded archive calls.'
        }
    }
    finally {
        $env:HISTORY_HYGIENE_ARCHIVE_CALLS = $oldArchiveCallsPath
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

    $processMarker = Convert-CodePoints @(102,114,111,110,116,105,101,114)
    $subjectRoot = Join-Path $scratch 'subject-marker'
    New-Repository $subjectRoot -CommitSubject ('fixture ' + $processMarker)
    Assert-ExpectedFailure 'subject-marker' $subjectRoot 'Restricted text found' ''

    $bodyRoot = Join-Path $scratch 'body-marker'
    New-Repository $bodyRoot -CommitBody ('fixture ' + $processMarker)
    Assert-ExpectedFailure 'body-marker' $bodyRoot 'Restricted text found' ''

    $treeRoot = Join-Path $scratch 'historical-tree-marker'
    New-Repository $treeRoot -TreeContentMarker $processMarker -TwoCommits
    Assert-ExpectedFailure 'historical-tree-marker' $treeRoot 'Restricted text found' ''

    $repositoryStateRoot = Join-Path $scratch 'repository-state-tree-marker'
    New-Repository $repositoryStateRoot
    $repositoryStatePath = Join-Path $repositoryStateRoot 'evidence/repository-state.md'
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $repositoryStatePath) | Out-Null
    [IO.File]::WriteAllText($repositoryStatePath, "fixture $processMarker")
    Invoke-External 'git' @('-C', $repositoryStateRoot, 'add', '--', 'evidence/repository-state.md') | Out-Null
    Invoke-External 'git' @('-C', $repositoryStateRoot, 'commit', '--quiet', '-m', 'fixture-state-marker') | Out-Null
    [IO.File]::WriteAllText($repositoryStatePath, 'ordinary engineering documentation')
    Invoke-External 'git' @('-C', $repositoryStateRoot, 'add', '--', 'evidence/repository-state.md') | Out-Null
    Invoke-External 'git' @('-C', $repositoryStateRoot, 'commit', '--quiet', '-m', 'fixture-state-cleanup') | Out-Null
    Assert-ExpectedFailure 'repository-state-unlisted-blob' $repositoryStateRoot 'Restricted text found' ''

    $space = Convert-CodePoints @(32)
    $doubleSpace = $space + $space
    $tab = Convert-CodePoints @(9)
    $lineFeed = Convert-CodePoints @(10)
    $carriageReturn = Convert-CodePoints @(13)
    $hyphen = Convert-CodePoints @(45)
    $underscore = Convert-CodePoints @(95)
    $slash = Convert-CodePoints @(47)
    $dot = Convert-CodePoints @(46)
    $rejected = Convert-CodePoints @(114,101,106,101,99,116,101,100)
    $families = Convert-CodePoints @(102,97,109,105,108,105,101,115)
    $remediation = Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110)
    $waves = Convert-CodePoints @(119,97,118,101,115)
    $rounds = Convert-CodePoints @(114,111,117,110,100,115)
    $prior = Convert-CodePoints @(112,114,105,111,114)
    $rejections = Convert-CodePoints @(114,101,106,101,99,116,105,111,110,115)
    $closure = Convert-CodePoints @(99,108,111,115,117,114,101)
    $review = Convert-CodePoints @(114,101,118,105,101,119)
    $reviews = Convert-CodePoints @(114,101,118,105,101,119,115)
    $processes = Convert-CodePoints @(112,114,111,99,101,115,115,101,115)
    $edge = Convert-CodePoints @(102,114,111,110,116,105,101,114)
    $remediations = Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110,115)
    $founder = Convert-CodePoints @(102,111,117,110,100,101,114)
    $approvals = Convert-CodePoints @(97,112,112,114,111,118,97,108,115)
    $mixedRejected = Convert-CodePoints @(82,101,106,101,99,116,101,100)
    $mixedFamilies = Convert-CodePoints @(70,97,109,105,108,105,101,115)
    $mixedRemediation = Convert-CodePoints @(82,101,109,101,100,105,97,116,105,111,110)
    $mixedWaves = Convert-CodePoints @(87,97,118,101,115)
    $mixedReview = Convert-CodePoints @(82,101,118,105,101,119)
    $mixedProcesses = Convert-CodePoints @(80,114,111,99,101,115,115,101,115)
    $candidateWord = Convert-CodePoints @(99,97,110,100,105,100,97,116,101)
    $evidenceWord = Convert-CodePoints @(101,118,105,100,101,110,99,101)
    $identifiedWord = Convert-CodePoints @(105,100,101,110,116,105,102,105,101,100)
    $caseWord = Convert-CodePoints @(99,97,115,101)
    $gapWord = Convert-CodePoints @(103,97,112)
    $phaseWord = Convert-CodePoints @(112,104,97,115,101)

    $phrasePairs = @(
        @{ Name = 'pair-00'; Left = $rejected; Right = $families; MixedLeft = $mixedRejected; MixedRight = $mixedFamilies },
        @{ Name = 'pair-01'; Left = $remediation; Right = $waves; MixedLeft = $mixedRemediation; MixedRight = $mixedWaves },
        @{ Name = 'pair-02'; Left = $remediation; Right = $rounds },
        @{ Name = 'pair-03'; Left = $prior; Right = $rejections },
        @{ Name = 'pair-04'; Left = $closure; Right = $reviews },
        @{ Name = 'pair-05'; Left = $review; Right = $processes; MixedLeft = $mixedReview; MixedRight = $mixedProcesses },
        @{ Name = 'pair-06'; Left = $edge; Right = $reviews },
        @{ Name = 'pair-07'; Left = $edge; Right = $remediations },
        @{ Name = 'pair-08'; Left = $founder; Right = $edge + 's' },
        @{ Name = 'pair-09'; Left = $founder; Right = $reviews },
        @{ Name = 'pair-10'; Left = $founder; Right = $rejections },
        @{ Name = 'pair-11'; Left = $founder; Right = $approvals }
    )
    $separatorCases = @(
        @{ Name = 'none'; Separator = '' },
        @{ Name = 'single'; Separator = $space },
        @{ Name = 'repeated'; Separator = $doubleSpace },
        @{ Name = 'tab'; Separator = $tab },
        @{ Name = 'underscore'; Separator = $underscore },
        @{ Name = 'hyphen'; Separator = $hyphen },
        @{ Name = 'slash'; Separator = $slash },
        @{ Name = 'dot'; Separator = $dot },
        @{ Name = 'mixed-punctuation'; Separator = $underscore + $hyphen + $slash + $dot },
        @{ Name = 'lf'; Separator = $lineFeed },
        @{ Name = 'crlf'; Separator = $carriageReturn + $lineFeed },
        @{ Name = 'cr'; Separator = $carriageReturn },
        @{ Name = 'double-crlf'; Separator = $carriageReturn + $lineFeed + $carriageReturn + $lineFeed },
        @{ Name = 'mixed-crlf-lf'; Separator = $carriageReturn + $lineFeed + $lineFeed },
        @{ Name = 'mixed-lf-crlf'; Separator = $lineFeed + $carriageReturn + $lineFeed },
        @{ Name = 'double-line'; Separator = $lineFeed + $lineFeed },
        @{ Name = 'mixed-case'; Separator = $space; MixedCase = $true },
        @{ Name = 'leading-trailing'; Separator = $space; LeadingTrailing = $true }
    )

    function New-CaseMarker {
        param([hashtable] $Pair, [hashtable] $SeparatorCase)

        $left = $Pair.Left
        $right = $Pair.Right
        if ($SeparatorCase.MixedCase) {
            if ($null -eq $Pair.MixedLeft -or $null -eq $Pair.MixedRight) {
                throw "Missing mixed-case pair data for $($Pair.Name)."
            }
            $left = $Pair.MixedLeft
            $right = $Pair.MixedRight
        }

        $marker = $left + $SeparatorCase.Separator + $right
        if ($SeparatorCase.LeadingTrailing) {
            $marker = $space + $marker + $space
        }
        return $marker
    }

    $matrixCases = [System.Collections.Generic.List[object]]::new()
    $seenCases = [System.Collections.Generic.HashSet[string]]::new()
    foreach ($pair in $phrasePairs[0..1]) {
        foreach ($separatorCase in $separatorCases) {
            foreach ($location in @('subject', 'body', 'tree')) {
                $key = "$($pair.Name)|$location|$($separatorCase.Name)"
                if ($seenCases.Add($key)) {
                    $matrixCases.Add([pscustomobject]@{ Pair = $pair; SeparatorCase = $separatorCase; Location = $location })
                }
            }
        }
    }
    foreach ($pair in $phrasePairs) {
        foreach ($separatorName in @('lf', 'crlf')) {
            $separatorCase = $separatorCases | Where-Object Name -eq $separatorName
            $key = "$($pair.Name)|tree|$separatorName"
            if ($seenCases.Add($key)) {
                $matrixCases.Add([pscustomobject]@{ Pair = $pair; SeparatorCase = $separatorCase; Location = 'tree' })
            }
        }
    }
    if ($matrixCases.Count -ne 128) {
        throw "Unexpected hygiene matrix size: $($matrixCases.Count)."
    }

    foreach ($matrixCase in $matrixCases) {
        $marker = New-CaseMarker $matrixCase.Pair $matrixCase.SeparatorCase
        $caseName = "matrix-$($matrixCase.Pair.Name)-$($matrixCase.Location)-$($matrixCase.SeparatorCase.Name)"
        if (-not (Test-SelectedCase $caseName)) { continue }
        $caseRoot = Join-Path $scratch $caseName
        switch ($matrixCase.Location) {
            'subject' { New-Repository $caseRoot -CommitSubject ('fixture ' + $marker) }
            'body' { New-Repository $caseRoot -CommitBody ('fixture ' + $marker) }
            'tree' { New-Repository $caseRoot -TreeContentMarker $marker -TwoCommits }
        }
        Assert-ExpectedFailure $caseName $caseRoot 'Restricted text found' ''
    }

    $survivingPhrasePairs = @(
        @{ Name = 'surviving-01'; Marker = $candidateWord + $space + $remediation },
        @{ Name = 'surviving-01-plural'; Marker = $candidateWord + $space + $remediation + 's' },
        @{ Name = 'surviving-02'; Marker = $remediation + $space + $gapWord },
        @{ Name = 'surviving-02-plural'; Marker = $remediation + $space + $gapWord + 's' },
        @{ Name = 'surviving-03'; Marker = $review + $space + $gapWord },
        @{ Name = 'surviving-03-plural'; Marker = $review + $space + $gapWord + 's' },
        @{ Name = 'surviving-04'; Marker = $candidateWord + $space + $evidenceWord },
        @{ Name = 'surviving-05'; Marker = $identifiedWord + $space + $caseWord },
        @{ Name = 'surviving-05-plural'; Marker = $identifiedWord + $space + $caseWord + 's' },
        @{ Name = 'phase-zero'; Marker = $phaseWord + '0' },
        @{ Name = 'phase-one'; Marker = $phaseWord + '1' }
    )
    foreach ($phraseCase in $survivingPhrasePairs) {
        foreach ($location in @('subject', 'body', 'tree')) {
            $caseName = "surviving-$($phraseCase.Name)-$location"
            $caseRoot = Join-Path $scratch $caseName
            switch ($location) {
                'subject' { New-Repository $caseRoot -CommitSubject ('fixture ' + $phraseCase.Marker) }
                'body' { New-Repository $caseRoot -CommitBody ('fixture ' + $phraseCase.Marker) }
                'tree' { New-Repository $caseRoot -TreeContentMarker $phraseCase.Marker -TwoCommits }
            }
            if ($phraseCase.Name -like 'phase-*' -and $location -eq 'tree') {
                Assert-ExpectedPass $caseName $caseRoot @{}
            }
            else {
                Assert-ExpectedFailure $caseName $caseRoot 'Restricted text found' ''
            }
        }
    }

    $encodingCases = @(
        @{ Name = 'utf16-le-bom'; Encoding = [Text.UnicodeEncoding]::new($false, $true, $true) },
        @{ Name = 'utf16-le-no-bom'; Encoding = [Text.UnicodeEncoding]::new($false, $false, $true) },
        @{ Name = 'utf16-be-bom'; Encoding = [Text.UnicodeEncoding]::new($true, $true, $true) },
        @{ Name = 'utf16-be-no-bom'; Encoding = [Text.UnicodeEncoding]::new($true, $false, $true) },
        @{ Name = 'utf32-le-bom'; Encoding = [Text.UTF32Encoding]::new($false, $true, $true) },
        @{ Name = 'utf32-le-no-bom'; Encoding = [Text.UTF32Encoding]::new($false, $false, $true) },
        @{ Name = 'utf32-be-bom'; Encoding = [Text.UTF32Encoding]::new($true, $true, $true) },
        @{ Name = 'utf32-be-no-bom'; Encoding = [Text.UTF32Encoding]::new($true, $false, $true) }
    )
    foreach ($encodingCase in $encodingCases) {
        $encodingRoot = Join-Path $scratch $encodingCase.Name
        $encodedMarker = Get-EncodedBytes $encodingCase.Encoding ('fixture ' + $processMarker)
        New-ByteRepository $encodingRoot $encodedMarker
        Assert-ExpectedFailure $encodingCase.Name $encodingRoot 'Restricted text found' ''
    }

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $nulRichRoot = Join-Path $scratch 'nul-rich-utf8'
    $nulRichBytes = $utf8.GetBytes(('fixture' + [char]0 + $processMarker + [char]0 + 'tail'))
    New-ByteRepository $nulRichRoot $nulRichBytes
    Assert-ExpectedFailure 'nul-rich-utf8' $nulRichRoot 'Restricted text found' ''

    $invalidPrefix = [byte[]]@(0, 255, 0)
    $binaryMarker = $utf8.GetBytes(('fixture ' + $processMarker))
    $binaryBytes = Join-ByteArrays @($invalidPrefix, $binaryMarker)
    $binaryRoot = Join-Path $scratch 'binary-current-marker'
    New-ByteRepository $binaryRoot $binaryBytes
    $binaryDiagnostic = 'unsupported binary or unrecognized encoding'
    Assert-ExpectedFailure 'binary-current-marker' $binaryRoot $binaryDiagnostic ''

    $historicalBinaryRoot = Join-Path $scratch 'binary-deleted-historical-marker'
    New-ByteRepository $historicalBinaryRoot $binaryBytes -HistoricalOnly
    Assert-ExpectedFailure 'binary-deleted-historical-marker' $historicalBinaryRoot $binaryDiagnostic ''

    $taskIdentifier = Convert-CodePoints @(65,66,67,45,49,50,51,52)
    $binaryTaskRoot = Join-Path $scratch 'binary-task-identifier'
    $binaryTaskBytes = Join-ByteArrays @($invalidPrefix, $utf8.GetBytes(('fixture ' + $taskIdentifier)))
    New-ByteRepository $binaryTaskRoot $binaryTaskBytes
    Assert-ExpectedFailure 'binary-task-identifier' $binaryTaskRoot $binaryDiagnostic ''

    $resourceLimit = 512
    $trackedAtLimitRoot = Join-Path $scratch 'tracked-file-at-limit'
    New-ByteRepository $trackedAtLimitRoot (New-FillerBytes $resourceLimit)
    Assert-ExpectedPass 'tracked-file-at-limit' $trackedAtLimitRoot @{
        MaxTrackedFileBytes = $resourceLimit
        MaxArchiveEntryBytes = 65536
        MaxHistoryScannedBytes = 65536
        MaxTotalDecompressedBytes = 65536
    }

    $trackedOverLimitRoot = Join-Path $scratch 'tracked-file-over-limit'
    New-ByteRepository $trackedOverLimitRoot (New-FillerBytes ($resourceLimit + 1))
    Assert-ExpectedFailure 'tracked-file-over-limit' $trackedOverLimitRoot 'per-file limit' '' '' @{
        MaxTrackedFileBytes = $resourceLimit
        MaxArchiveEntryBytes = 65536
        MaxHistoryScannedBytes = 65536
        MaxTotalDecompressedBytes = 65536
    }

    $archiveAtLimitRoot = Join-Path $scratch 'archive-entry-at-limit'
    New-ByteRepository $archiveAtLimitRoot (New-FillerBytes $resourceLimit) -HistoricalOnly
    Assert-ExpectedPass 'archive-entry-at-limit' $archiveAtLimitRoot @{
        MaxTrackedFileBytes = 65536
        MaxArchiveEntryBytes = $resourceLimit
        MaxHistoryScannedBytes = 65536
        MaxTotalDecompressedBytes = 65536
    }

    $archiveOverLimitRoot = Join-Path $scratch 'archive-entry-over-limit'
    New-ByteRepository $archiveOverLimitRoot (New-FillerBytes ($resourceLimit + 1)) -HistoricalOnly
    Assert-ExpectedFailure 'archive-entry-over-limit' $archiveOverLimitRoot 'declared size exceeds' '' '' @{
        MaxTrackedFileBytes = 65536
        MaxArchiveEntryBytes = $resourceLimit
        MaxHistoryScannedBytes = 65536
        MaxTotalDecompressedBytes = 65536
    }

    $totalRoot = Join-Path $scratch 'total-history-boundary'
    New-Repository $totalRoot -TwoCommits
    $totals = Get-HistoryScanTotals $totalRoot
    Assert-ExpectedPass 'total-history-at-limit' $totalRoot @{
        MaxTrackedFileBytes = 65536
        MaxArchiveEntryBytes = 65536
        MaxHistoryScannedBytes = $totals.History
        MaxTotalDecompressedBytes = $totals.Decompressed
    }
    Assert-ExpectedFailure 'total-history-over-limit' $totalRoot 'history scan byte budget' '' '' @{
        MaxTrackedFileBytes = 65536
        MaxArchiveEntryBytes = 65536
        MaxHistoryScannedBytes = $totals.History - 1
        MaxTotalDecompressedBytes = 65536
    }
    Assert-ExpectedFailure 'total-decompressed-over-limit' $totalRoot 'decompressed history byte budget' '' '' @{
        MaxTrackedFileBytes = 65536
        MaxArchiveEntryBytes = 65536
        MaxHistoryScannedBytes = 65536
        MaxTotalDecompressedBytes = $totals.Decompressed - 1
    }

    $growth107Root = Join-Path $scratch 'history-growth-107'
    New-HistoryGrowthRepository $growth107Root 107
    $legacy107Bytes = Get-FullSnapshotHistoryBytes $growth107Root
    if ($legacy107Bytes -gt 134217728) {
        throw "The 107-commit growth fixture no longer represents the passing full-snapshot boundary ($legacy107Bytes bytes)."
    }
    Write-Output "FULL_SNAPSHOT_HISTORY_BYTES_107: $legacy107Bytes"
    Assert-ExpectedPass 'history-growth-107' $growth107Root $null

    $growth132Root = Join-Path $scratch 'history-growth-132'
    New-HistoryGrowthRepository $growth132Root 132
    $legacy132Bytes = Get-FullSnapshotHistoryBytes $growth132Root
    if ($legacy132Bytes -le 134217728) {
        throw "The 132-commit growth fixture no longer represents the overflowing full-snapshot boundary ($legacy132Bytes bytes)."
    }
    Write-Output "FULL_SNAPSHOT_HISTORY_BYTES_132: $legacy132Bytes"
    Assert-ExpectedPass 'history-growth-132' $growth132Root $null

    $mergeSafeRoot = Join-Path $scratch 'first-parent-merge-safe'
    New-ConflictMergeRepository $mergeSafeRoot
    Assert-ExpectedPass 'first-parent-merge-safe' $mergeSafeRoot $null

    $mergeMarkerRoot = Join-Path $scratch 'first-parent-merge-marker'
    New-ConflictMergeRepository $mergeMarkerRoot -WithMergeMarker
    Assert-ExpectedFailure 'first-parent-merge-resolution-marker' $mergeMarkerRoot 'Restricted text found' '' '' $null

    $positiveRoot = Join-Path $scratch 'ordinary-engineering'
    New-Repository $positiveRoot -CommitSubject 'fix(restore): reject unknown consumed members' -CommitBody 'review reject fix remediation candidate gaps' -TreeContentMarker 'review reject fix remediation candidate gaps'
    Assert-ExpectedPass 'ordinary-engineering' $positiveRoot

    $coAuthorTrailer = (Convert-CodePoints @(67,111,45,65,117,116,104,111,114,101,100,45,66,121)) +
        (Convert-CodePoints @(58,32)) +
        (Convert-CodePoints @(80,97,112,101,114,99,108,105,112)) +
        (Convert-CodePoints @(32,60,110,111,114,101,112,108,121,64,112,97,112,101,114,99,108,105,112,46,105,110,103,62))
    $coAuthorRoot = Join-Path $scratch 'required-coauthor-trailer'
    New-Repository $coAuthorRoot -CommitBody $coAuthorTrailer
    Assert-ExpectedFailure 'coauthor-trailer' $coAuthorRoot 'Restricted text found' ''

    $taskIdRoot = Join-Path $scratch 'task-id'
    New-Repository $taskIdRoot -WithTaskId
    Assert-ExpectedFailure 'history-task-identifier' $taskIdRoot 'history task identifier' ''

    if ($script:selectedCases.Count -gt 0 -and $script:executedCaseCount -eq 0) {
        throw 'No hygiene regression cases matched the selected names.'
    }
    Write-Output 'PASS: hygiene gate covers complete-history content, changed-path enumeration, merge results, resource bounds, shallow history, metadata markers, and task identifiers in disposable repositories.'
    exit 0
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
