$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$gate = Join-Path $root 'scripts/test-history-hygiene.ps1'
$scratchParent = [IO.Path]::GetTempPath()
$scratch = Join-Path $scratchParent ('packagesurface-hygiene-' + [Guid]::NewGuid().ToString('N'))

function Convert-CodePoints([int[]] $points) {
    return -join ($points | ForEach-Object { [char]$_ })
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
    $commitArguments = @('-C', $Path, 'commit', '--quiet', '-m', $message)
    if (-not [string]::IsNullOrWhiteSpace($CommitBody)) {
        $commitArguments += @('-m', $CommitBody)
    }
    Invoke-External 'git' $commitArguments | Out-Null
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

    $archiveBytes = [long]0
    $decompressedBytes = [long]0
    $commits = Invoke-External 'git' @('-C', $Path, 'rev-list', '--all')
    foreach ($commit in @($commits -split "`r?`n")) {
        $archivePath = Join-Path $scratch ('measure-' + [Guid]::NewGuid().ToString('N') + '.zip')
        try {
            Invoke-External 'git' @('-C', $Path, 'archive', '--format=zip', "--output=$archivePath", $commit) | Out-Null
            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                foreach ($entry in $archive.Entries) {
                    if ([string]::IsNullOrEmpty($entry.Name)) {
                        continue
                    }
                    $archiveBytes += $entry.Length
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

    $historyText = Invoke-External 'git' @('-C', $Path, 'log', '--all', '--format=%H%n%an%n%ae%n%cn%n%ce%n%B')
    $metadataBytes = [Text.UTF8Encoding]::new($false).GetByteCount($historyText)
    return [pscustomobject]@{
        History = $currentBytes + $archiveBytes + $metadataBytes
        Decompressed = $decompressedBytes
    }
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
    param([string] $Name, [string] $Path, [hashtable] $Limits)

    $result = Invoke-Gate $Path '' $Name $Limits
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
    'archive' {
        if ($scenario -eq 'error-archive') { exit 128 }
        $outputArgument = @($Arguments | Where-Object { $_ -like '--output=*' })[0]
        $outputPath = $outputArgument.Substring(9)
        $zip = [IO.Compression.ZipFile]::Open($outputPath, [IO.Compression.ZipArchiveMode]::Create)
        try {
            $entry = $zip.CreateEntry('tracked.txt')
            $writer = [IO.StreamWriter]::new($entry.Open())
            try { $writer.Write('fixture content') } finally { $writer.Dispose() }
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
        @{ Name = 'git-tree-read-failure'; Scenario = 'error-archive'; Expected = 'git archive' },
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
    if ($matrixCases.Count -ne 104) {
        throw "Unexpected hygiene matrix size: $($matrixCases.Count)."
    }

    foreach ($matrixCase in $matrixCases) {
        $marker = New-CaseMarker $matrixCase.Pair $matrixCase.SeparatorCase
        $caseName = "matrix-$($matrixCase.Pair.Name)-$($matrixCase.Location)-$($matrixCase.SeparatorCase.Name)"
        $caseRoot = Join-Path $scratch $caseName
        switch ($matrixCase.Location) {
            'subject' { New-Repository $caseRoot -CommitSubject ('fixture ' + $marker) }
            'body' { New-Repository $caseRoot -CommitBody ('fixture ' + $marker) }
            'tree' { New-Repository $caseRoot -TreeContentMarker $marker -TwoCommits }
        }
        Assert-ExpectedFailure $caseName $caseRoot 'Restricted text found' ''
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

    $positiveRoot = Join-Path $scratch 'ordinary-engineering'
    New-Repository $positiveRoot -CommitSubject 'fix(restore): reject unknown consumed members' -CommitBody 'review reject fix remediation' -TreeContentMarker 'review reject fix remediation'
    Assert-ExpectedPass 'ordinary-engineering' $positiveRoot

    $coAuthorTrailer = (Convert-CodePoints @(67,111,45,65,117,116,104,111,114,101,100,45,66,121)) +
        (Convert-CodePoints @(58,32)) +
        (Convert-CodePoints @(80,97,112,101,114,99,108,105,112)) +
        (Convert-CodePoints @(32,60,110,111,114,101,112,108,121,64,112,97,112,101,114,99,108,105,112,46,105,110,103,62))
    $coAuthorRoot = Join-Path $scratch 'required-coauthor-trailer'
    New-Repository $coAuthorRoot -CommitBody $coAuthorTrailer
    Assert-ExpectedPass 'required-coauthor-trailer' $coAuthorRoot

    $taskIdRoot = Join-Path $scratch 'task-id'
    New-Repository $taskIdRoot -WithTaskId
    Assert-ExpectedFailure 'history-task-identifier' $taskIdRoot 'history task identifier' ''

    Write-Output 'PASS: hygiene gate rejects command failure, shallow history, metadata markers, historical-tree markers, and task identifiers in disposable repositories.'
    exit 0
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
