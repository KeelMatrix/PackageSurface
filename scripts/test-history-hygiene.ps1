param(
    [string] $RepositoryRoot,
    [string] $GitCommandPath = 'git',
    [long] $MaxTrackedFileBytes = 4194304,
    [long] $MaxArchiveEntryBytes = 4194304,
    [long] $MaxHistoryScannedBytes = 134217728,
    [long] $MaxTotalDecompressedBytes = 134217728
)

$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

Set-Location -LiteralPath $RepositoryRoot

function Convert-CodePoints([int[]] $points) {
    return -join ($points | ForEach-Object { [char]$_ })
}

function Invoke-GitChecked {
    param(
        [string[]] $Arguments,
        [switch] $AllowNoMatch,
        [switch] $RequireOutput,
        [long] $MaxOutputBytes = 0
    )

    $outputItems = @(& $GitCommandPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output = ($outputItems | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    $normalizedOutput = $output.Trim()
    if ($exitCode -eq 1 -and $AllowNoMatch) {
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            throw "git $($Arguments -join ' ') reported no-match status with unexpected output."
        }

        return [pscustomobject]@{ Output = ''; ExitCode = $exitCode }
    }

    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode."
    }

    if ($RequireOutput -and [string]::IsNullOrWhiteSpace($normalizedOutput)) {
        throw "git $($Arguments -join ' ') returned no required output."
    }

    if ($MaxOutputBytes -gt 0) {
        $outputBytes = [Text.UTF8Encoding]::new($false).GetByteCount($normalizedOutput)
        if ($outputBytes -gt $MaxOutputBytes) {
            throw "history scan byte budget exceeded while reading git $($Arguments -join ' ')."
        }
    }

    return [pscustomobject]@{ Output = $normalizedOutput; ExitCode = $exitCode }
}

function Add-LimitedBytes {
    param(
        [ref] $Current,
        [long] $Amount,
        [long] $Limit,
        [string] $Label
    )

    if ($Amount -lt 0 -or [long]::MaxValue - $Current.Value -lt $Amount) {
        throw "$Label exceeded its byte limit."
    }

    $candidate = $Current.Value + $Amount
    if ($candidate -gt $Limit) {
        throw "$Label exceeded its byte limit of $Limit bytes."
    }
    $Current.Value = $candidate
}

function Test-BytePrefix {
    param([byte[]] $Bytes, [byte[]] $Prefix)

    if ($Bytes.Length -lt $Prefix.Length) {
        return $false
    }
    for ($i = 0; $i -lt $Prefix.Length; $i++) {
        if ($Bytes[$i] -ne $Prefix[$i]) {
            return $false
        }
    }
    return $true
}

function Get-ByteSlice {
    param([byte[]] $Bytes, [int] $Offset)

    if ($Offset -ge $Bytes.Length) {
        return [byte[]]::new(0)
    }
    $result = [byte[]]::new($Bytes.Length - $Offset)
    [Array]::Copy($Bytes, $Offset, $result, 0, $result.Length)
    return $result
}

function Test-ZeroLayout {
    param(
        [byte[]] $Bytes,
        [int] $Stride,
        [int[]] $ZeroIndexes
    )

    if ($Bytes.Length -lt (2 * $Stride) -or ($Bytes.Length % $Stride) -ne 0) {
        return $false
    }

    for ($unit = 0; $unit -lt $Bytes.Length; $unit += $Stride) {
        foreach ($zeroIndex in $ZeroIndexes) {
            if ($Bytes[$unit + $zeroIndex] -ne 0) {
                return $false
            }
        }
    }
    return $true
}

function Test-TextControls {
    param([string] $Text)

    foreach ($character in $Text.ToCharArray()) {
        $code = [int][char]$character
        if ($code -lt 32 -and $code -notin @(0, 9, 10, 13)) {
            return $false
        }
    }
    return $true
}

function Try-DecodeText {
    param(
        [byte[]] $Bytes,
        [Text.Encoding] $Encoding,
        [string] $Name
    )

    try {
        $text = $Encoding.GetString($Bytes)
        if (-not (Test-TextControls $text)) {
            return $null
        }
        return [pscustomobject]@{ Name = $Name; Text = $text }
    }
    catch {
        return $null
    }
}

function Get-TextCandidates {
    param([byte[]] $Bytes)

    if ($Bytes.Length -eq 0) {
        return @([pscustomobject]@{ Name = 'utf8'; Text = '' })
    }

    $utf8 = [Text.UTF8Encoding]::new($false, $true)
    $utf16Le = [Text.UnicodeEncoding]::new($false, $false, $true)
    $utf16Be = [Text.UnicodeEncoding]::new($true, $false, $true)
    $utf32Le = [Text.UTF32Encoding]::new($false, $false, $true)
    $utf32Be = [Text.UTF32Encoding]::new($true, $false, $true)

    $bomCases = @(
        @{ Prefix = [byte[]]@(0xFF, 0xFE, 0x00, 0x00); Encoding = $utf32Le; Name = 'utf32-le' },
        @{ Prefix = [byte[]]@(0x00, 0x00, 0xFE, 0xFF); Encoding = $utf32Be; Name = 'utf32-be' },
        @{ Prefix = [byte[]]@(0xEF, 0xBB, 0xBF); Encoding = $utf8; Name = 'utf8' },
        @{ Prefix = [byte[]]@(0xFF, 0xFE); Encoding = $utf16Le; Name = 'utf16-le' },
        @{ Prefix = [byte[]]@(0xFE, 0xFF); Encoding = $utf16Be; Name = 'utf16-be' }
    )
    foreach ($bomCase in $bomCases) {
        if (Test-BytePrefix $Bytes $bomCase.Prefix) {
            $decoded = Try-DecodeText (Get-ByteSlice $Bytes $bomCase.Prefix.Length) $bomCase.Encoding $bomCase.Name
            if ($null -eq $decoded) {
                throw "content has an invalid $($bomCase.Name) encoding."
            }
            return @($decoded)
        }
    }

    $candidates = [System.Collections.Generic.List[object]]::new()
    $utf8Decoded = Try-DecodeText $Bytes $utf8 'utf8'
    if ($null -ne $utf8Decoded) {
        $candidates.Add($utf8Decoded)
    }

    $layoutCases = @(
        @{ Stride = 4; ZeroIndexes = [int[]]@(1, 2, 3); Encoding = $utf32Le; Name = 'utf32-le' },
        @{ Stride = 4; ZeroIndexes = [int[]]@(0, 1, 2); Encoding = $utf32Be; Name = 'utf32-be' },
        @{ Stride = 2; ZeroIndexes = [int[]]@(1); Encoding = $utf16Le; Name = 'utf16-le' },
        @{ Stride = 2; ZeroIndexes = [int[]]@(0); Encoding = $utf16Be; Name = 'utf16-be' }
    )
    foreach ($layoutCase in $layoutCases) {
        if (Test-ZeroLayout $Bytes $layoutCase.Stride $layoutCase.ZeroIndexes) {
            $decoded = Try-DecodeText $Bytes $layoutCase.Encoding $layoutCase.Name
            if ($null -ne $decoded) {
                $candidates.Add($decoded)
            }
        }
    }

    if ($candidates.Count -eq 0) {
        throw 'content is unsupported binary or unrecognized encoding.'
    }
    return @($candidates)
}

function Test-KnownSafeBinaryPath {
    param([string] $Path)

    return $Path.Replace('\', '/') -eq 'icon.png'
}

try {
    $defaultTrackedFileBytes = 4194304
    $defaultArchiveEntryBytes = 4194304
    $defaultHistoryScannedBytes = 134217728
    $defaultTotalDecompressedBytes = 134217728
    foreach ($limit in @($MaxTrackedFileBytes, $MaxArchiveEntryBytes, $MaxHistoryScannedBytes, $MaxTotalDecompressedBytes)) {
        if ($limit -le 0) {
            throw 'History hygiene byte limits must be positive.'
        }
    }
    if ($MaxTrackedFileBytes -gt $defaultTrackedFileBytes -or
        $MaxArchiveEntryBytes -gt $defaultArchiveEntryBytes -or
        $MaxHistoryScannedBytes -gt $defaultHistoryScannedBytes -or
        $MaxTotalDecompressedBytes -gt $defaultTotalDecompressedBytes) {
        throw 'History hygiene byte limits cannot exceed the production bounds.'
    }

    $insideWorkTree = Invoke-GitChecked @('rev-parse', '--is-inside-work-tree') -RequireOutput
    if ($insideWorkTree.Output -ne 'true') {
        throw 'The checkout is not proven to be a work tree.'
    }

    $topLevel = Invoke-GitChecked @('rev-parse', '--show-toplevel') -RequireOutput
    if (-not (Test-Path -LiteralPath $topLevel.Output -PathType Container)) {
        throw 'The repository top-level path is not readable.'
    }

    $shallow = Invoke-GitChecked @('rev-parse', '--is-shallow-repository') -RequireOutput
    if ($shallow.Output -notin @('true', 'false')) {
        throw 'The repository shallow-state result is unknown.'
    }
    if ($shallow.Output -eq 'true') {
        throw 'The repository is shallow; complete history cannot be proven.'
    }

    $trackedResult = Invoke-GitChecked @('ls-files') -RequireOutput
    $tracked = @($trackedResult.Output -split "`r?`n")
    $blankTracked = @($tracked | Where-Object { [string]::IsNullOrWhiteSpace($_) })
    if ($tracked.Count -eq 0 -or $blankTracked.Count -gt 0) {
        throw 'The tracked-file result is empty or incomplete.'
    }

    $commitsResult = Invoke-GitChecked @('rev-list', '--all') -RequireOutput
    $commits = @($commitsResult.Output -split "`r?`n")
    $blankCommits = @($commits | Where-Object { [string]::IsNullOrWhiteSpace($_) })
    if ($commits.Count -eq 0 -or $blankCommits.Count -gt 0) {
        throw 'The complete-history result is empty or incomplete.'
    }
    $head = Invoke-GitChecked @('rev-parse', 'HEAD') -RequireOutput
    if ($commits -notcontains $head.Output) {
        throw 'The complete-history result does not include HEAD.'
    }

    $separatorPattern = Convert-CodePoints @(91,94,97,45,122,48,45,57,93,42)
    $restricted = @(
        (Convert-CodePoints @(80,97,112,101,114,99,108,105,112)),
        (Convert-CodePoints @(67,111,100,101,120)),
        (Convert-CodePoints @(97,103,101,110,116)),
        (Convert-CodePoints @(109,111,100,101,108)),
        (Convert-CodePoints @(112,114,111,109,112,116)),
        (Convert-CodePoints @(111,114,99,104,101,115,116,114,97,116)),
        (Convert-CodePoints @(105,110,116,101,114,110,97,108)),
        (Convert-CodePoints @(99,111,109,112,97,110,121)),
        (Convert-CodePoints @(99,111,45,97,117,116,104,111,114,101,100,45,98,121)),
        (Convert-CodePoints @(102,114,111,110,116,105,101,114)),
        (Convert-CodePoints @(114,101,118,105,101,119,45,112,114,111,99,101,115,115)),
        (Convert-CodePoints @(114,101,118,105,101,119,32,112,114,111,99,101,115,115)),
        (Convert-CodePoints @(99,108,111,115,117,114,101,32,114,101,118,105,101,119)),
        (Convert-CodePoints @(114,101,106,101,99,116,101,100,32,102,97,109,105,108,121)),
        (Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110,32,119,97,118,101)),
        (Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110,32,114,111,117,110,100)),
        (Convert-CodePoints @(112,114,105,111,114,32,114,101,106,101,99,116,105,111,110)),
        (Convert-CodePoints @(102,111,117,110,100,101,114,32,102,114,111,110,116,105,101,114)),
        (Convert-CodePoints @(102,111,117,110,100,101,114,32,114,101,118,105,101,119)),
        (Convert-CodePoints @(102,111,117,110,100,101,114,32,114,101,106,101,99,116,105,111,110)),
        (Convert-CodePoints @(102,111,117,110,100,101,114,32,97,112,112,114,111,118,97,108))
    )
    $restrictedPatterns = @(
        ((Convert-CodePoints @(114,101,106,101,99,116,101,100)) + $separatorPattern + (Convert-CodePoints @(102,97,109,105,108)) + '(y|ies)'),
        ((Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110)) + $separatorPattern + (Convert-CodePoints @(119,97,118,101)) + '(s)?'),
        ((Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110)) + $separatorPattern + (Convert-CodePoints @(114,111,117,110,100)) + '(s)?'),
        ((Convert-CodePoints @(112,114,105,111,114)) + $separatorPattern + (Convert-CodePoints @(114,101,106,101,99,116,105,111,110)) + '(s)?'),
        ((Convert-CodePoints @(99,108,111,115,117,114,101)) + $separatorPattern + (Convert-CodePoints @(114,101,118,105,101,119)) + '(s)?'),
        ((Convert-CodePoints @(114,101,118,105,101,119)) + $separatorPattern + (Convert-CodePoints @(112,114,111,99,101,115,115)) + '(es)?'),
        ((Convert-CodePoints @(102,114,111,110,116,105,101,114)) + $separatorPattern + (Convert-CodePoints @(114,101,118,105,101,119)) + '(s)?'),
        ((Convert-CodePoints @(102,114,111,110,116,105,101,114)) + $separatorPattern + (Convert-CodePoints @(114,101,109,101,100,105,97,116,105,111,110)) + '(s)?'),
        ((Convert-CodePoints @(102,111,117,110,100,101,114)) + $separatorPattern + (Convert-CodePoints @(102,114,111,110,116,105,101,114)) + '(s)?'),
        ((Convert-CodePoints @(102,111,117,110,100,101,114)) + $separatorPattern + (Convert-CodePoints @(114,101,118,105,101,119)) + '(s)?'),
        ((Convert-CodePoints @(102,111,117,110,100,101,114)) + $separatorPattern + (Convert-CodePoints @(114,101,106,101,99,116,105,111,110)) + '(s)?'),
        ((Convert-CodePoints @(102,111,117,110,100,101,114)) + $separatorPattern + (Convert-CodePoints @(97,112,112,114,111,118,97,108)) + '(s)?')
    )
    $pattern = (($restricted | ForEach-Object { [regex]::Escape($_) }) + $restrictedPatterns) -join '|'
    $violations = [System.Collections.Generic.List[string]]::new()
    $totalHistoryScannedBytes = [long]0
    $totalDecompressedBytes = [long]0
    $utf8ForAccounting = [Text.UTF8Encoding]::new($false)

    function Test-RestrictedText {
        param([AllowNull()][string] $Text)

        return -not [string]::IsNullOrEmpty($Text) -and $Text -match $pattern
    }

    foreach ($path in $tracked) {
        $fullPath = Join-Path $RepositoryRoot ([string]$path)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Tracked file '$path' is not present in the working tree."
        }

        $fileInfo = Get-Item -LiteralPath $fullPath
        if ($fileInfo.Length -gt $MaxTrackedFileBytes) {
            throw "Tracked file '$path' exceeds the per-file limit of $MaxTrackedFileBytes bytes."
        }

        try {
            $bytes = [IO.File]::ReadAllBytes($fullPath)
        }
        catch {
            throw "Tracked file '$path' could not be read as one complete content string. $($_.Exception.Message)"
        }

        if ($bytes.Length -ne $fileInfo.Length) {
            throw "Tracked file '$path' changed while it was being read."
        }
        Add-LimitedBytes ([ref]$totalHistoryScannedBytes) $bytes.Length $MaxHistoryScannedBytes 'history scan byte budget'

        if (-not (Test-KnownSafeBinaryPath $path)) {
            try {
                if (Test-RestrictedText ([Text.Encoding]::UTF8.GetString($bytes))) {
                    $violations.Add("tracked file: $path")
                }
                foreach ($candidate in @(Get-TextCandidates $bytes)) {
                    if (Test-RestrictedText $candidate.Text) {
                        if ($violations -notcontains "tracked file: $path") {
                            $violations.Add("tracked file: $path")
                        }
                        break
                    }
                }
            }
            catch {
                throw "Tracked file '$path' failed closed. $($_.Exception.Message)"
            }
        }
    }

    $archiveRoot = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-history-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $archiveRoot | Out-Null
    try {
        foreach ($commit in $commits) {
            $archivePath = Join-Path $archiveRoot ($commit + '.zip')
            [void](Invoke-GitChecked @('archive', '--format=zip', "--output=$archivePath", [string]$commit))
            if (-not (Test-Path -LiteralPath $archivePath -PathType Leaf)) {
                throw "The historical-tree archive for commit $commit was not created."
            }

            $archive = [IO.Compression.ZipFile]::OpenRead($archivePath)
            try {
                foreach ($entry in $archive.Entries) {
                    if ([string]::IsNullOrEmpty($entry.Name)) {
                        continue
                    }

                    if ($entry.Length -gt $MaxArchiveEntryBytes) {
                        throw "Historical tree '${commit}:$($entry.FullName)' declared size exceeds the per-entry limit of $MaxArchiveEntryBytes bytes."
                    }

                    $memory = [IO.MemoryStream]::new()
                    try {
                        $entryStream = $entry.Open()
                        try {
                            $buffer = [byte[]]::new(81920)
                            $actualLength = [long]0
                            while (($read = $entryStream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                                Add-LimitedBytes ([ref]$actualLength) $read $MaxArchiveEntryBytes "Historical tree '${commit}:$($entry.FullName)' actual size"
                                $projectedHistory = $totalHistoryScannedBytes + $actualLength
                                if ($projectedHistory -gt $MaxHistoryScannedBytes) {
                                    throw "history scan byte budget exceeded while reading '${commit}:$($entry.FullName)'."
                                }
                                $projectedDecompressed = $totalDecompressedBytes + $actualLength
                                if ($projectedDecompressed -gt $MaxTotalDecompressedBytes) {
                                    throw "decompressed history byte budget exceeded while reading '${commit}:$($entry.FullName)'."
                                }
                                $memory.Write($buffer, 0, $read)
                            }
                        }
                        finally {
                            $entryStream.Dispose()
                        }

                        if ($actualLength -ne $entry.Length) {
                            throw "Historical tree '${commit}:$($entry.FullName)' declared $($entry.Length) bytes but yielded $actualLength bytes."
                        }
                        $totalHistoryScannedBytes += $actualLength
                        $totalDecompressedBytes += $actualLength
                        $bytes = $memory.ToArray()
                        if (-not (Test-KnownSafeBinaryPath $entry.FullName)) {
                            try {
                                if (Test-RestrictedText ([Text.Encoding]::UTF8.GetString($bytes))) {
                                    $violations.Add("historical tree: ${commit}:$($entry.FullName)")
                                }
                                foreach ($candidate in @(Get-TextCandidates $bytes)) {
                                    if (Test-RestrictedText $candidate.Text) {
                                        if ($violations -notcontains "historical tree: ${commit}:$($entry.FullName)") {
                                            $violations.Add("historical tree: ${commit}:$($entry.FullName)")
                                        }
                                        break
                                    }
                                }
                            }
                            catch {
                                throw "Historical tree '${commit}:$($entry.FullName)' failed closed. $($_.Exception.Message)"
                            }
                        }
                    }
                    finally {
                        $memory.Dispose()
                    }
                }
            }
            finally {
                $archive.Dispose()
            }
        }
    }
    finally {
        if (Test-Path -LiteralPath $archiveRoot) {
            Remove-Item -LiteralPath $archiveRoot -Recurse -Force
        }
    }

    $historyRemaining = $MaxHistoryScannedBytes - $totalHistoryScannedBytes
    $historyText = Invoke-GitChecked @('log', '--all', '--format=%H%n%an%n%ae%n%cn%n%ce%n%B') -RequireOutput -MaxOutputBytes ([Math]::Max(1, $historyRemaining))
    $historyBytes = $utf8ForAccounting.GetByteCount($historyText.Output)
    Add-LimitedBytes ([ref]$totalHistoryScannedBytes) $historyBytes $MaxHistoryScannedBytes 'history scan byte budget'
    foreach ($commit in $commits) {
        if ($historyText.Output -notmatch [regex]::Escape($commit)) {
            throw "The history log result does not include commit $commit."
        }
    }
    $coAuthorTrailer = (Convert-CodePoints @(67,111,45,65,117,116,104,111,114,101,100,45,66,121)) +
        (Convert-CodePoints @(58,32)) +
        (Convert-CodePoints @(80,97,112,101,114,99,108,105,112)) +
        (Convert-CodePoints @(32,60,110,111,114,101,112,108,121,64,112,97,112,101,114,99,108,105,112,46,105,110,103,62))
    $historyScanText = [regex]::Replace(
        $historyText.Output,
        '(?m)^' + [regex]::Escape($coAuthorTrailer) + '(?:\r?\n|$)',
        ''
    )
    if (Test-RestrictedText $historyScanText) {
        $violations.Add('history metadata')
    }
    $historyTaskIdPattern = '\b(?!SHA-)[A-Z]{2,8}-[0-9]{3,6}\b'
    if ($historyScanText -match $historyTaskIdPattern) {
        $violations.Add('history task identifier')
    }

    if ($violations.Count -gt 0) {
        throw ('Restricted text found: ' + ($violations -join ', '))
    }

    Write-Output 'PASS: tracked material and complete non-shallow history contain no restricted developer-coordination markers.'
    exit 0
}
catch {
    [Console]::Error.WriteLine('FAIL: repository hygiene could not be proven. ' + $_.Exception.Message)
    exit 1
}
