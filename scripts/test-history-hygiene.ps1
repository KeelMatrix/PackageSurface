param(
    [string] $RepositoryRoot,
    [string] $GitCommandPath = 'git'
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
        [switch] $RequireOutput
    )

    $outputItems = @(& $GitCommandPath @Arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $output = ($outputItems | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if ($exitCode -eq 1 -and $AllowNoMatch) {
        if (-not [string]::IsNullOrWhiteSpace($output)) {
            throw "git $($Arguments -join ' ') reported no-match status with unexpected output."
        }

        return [pscustomobject]@{ Output = ''; ExitCode = $exitCode }
    }

    if ($exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed with exit code $exitCode."
    }

    if ($RequireOutput -and [string]::IsNullOrWhiteSpace($output)) {
        throw "git $($Arguments -join ' ') returned no required output."
    }

    return [pscustomobject]@{ Output = $output.Trim(); ExitCode = $exitCode }
}

try {
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

    function Test-RestrictedText {
        param([AllowNull()][string] $Text)

        return -not [string]::IsNullOrEmpty($Text) -and $Text -match $pattern
    }

    foreach ($path in $tracked) {
        $fullPath = Join-Path $RepositoryRoot ([string]$path)
        if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Tracked file '$path' is not present in the working tree."
        }

        try {
            $bytes = [IO.File]::ReadAllBytes($fullPath)
        }
        catch {
            throw "Tracked file '$path' could not be read as one complete content string. $($_.Exception.Message)"
        }

        if ($bytes -contains 0) {
            continue
        }

        $content = [Text.Encoding]::UTF8.GetString($bytes)
        if (Test-RestrictedText $content) {
            $violations.Add("tracked file: $path")
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

                    $memory = [IO.MemoryStream]::new()
                    try {
                        $entryStream = $entry.Open()
                        try {
                            $entryStream.CopyTo($memory)
                        }
                        finally {
                            $entryStream.Dispose()
                        }

                        $bytes = $memory.ToArray()
                        if (($bytes -contains 0) -or $bytes.Length -eq 0) {
                            continue
                        }
                        $content = [Text.Encoding]::UTF8.GetString($bytes)
                        if (Test-RestrictedText $content) {
                            $violations.Add("historical tree: ${commit}:$($entry.FullName)")
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

    $historyText = Invoke-GitChecked @('log', '--all', '--format=%H%n%an%n%ae%n%cn%n%ce%n%B') -RequireOutput
    foreach ($commit in $commits) {
        if ($historyText.Output -notmatch [regex]::Escape($commit)) {
            throw "The history log result does not include commit $commit."
        }
    }
    if (Test-RestrictedText $historyText.Output) {
        $violations.Add('history metadata')
    }
    $historyTaskIdPattern = '\b(?!SHA-)[A-Z]{2,8}-[0-9]{3,6}\b'
    if ($historyText.Output -match $historyTaskIdPattern) {
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
