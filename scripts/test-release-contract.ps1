$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-release-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    $project = Join-Path $scratch 'test.csproj'
    $changelog = Join-Path $scratch 'CHANGELOG.md'
    [IO.File]::WriteAllText($project, '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText($changelog, "# Changelog`n`n## [Unreleased]`n`n- Candidate capabilities.`n")
    & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -ProjectFile $project -ChangelogPath $changelog
    if ($LASTEXITCODE -ne 0) { throw 'Unreleased candidate validation failed.' }
    & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Tag-mode validation accepted an Unreleased changelog entry.' }

    $changelogText = "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n"
    [IO.File]::WriteAllText($changelog, $changelogText)
    & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog
    if ($LASTEXITCODE -ne 0) { throw 'Finalized release validation failed.' }

    [IO.File]::WriteAllText($changelog, "# Changelog`n`n## [0.2.0] - 2026-09-22`n`n- Wrong version.`n")
    & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -ProjectFile $project -ChangelogPath $changelog 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Version mismatch was accepted.' }

    $workflowDirectory = Join-Path $root '.github/workflows'
    $workflowFiles = @(Get-ChildItem -LiteralPath $workflowDirectory -Filter '*.yml' -File)
    if ($workflowFiles.Count -eq 0) { throw 'No workflow files were found.' }

    $gateJobsFound = 0
    $gateCheckoutsFound = 0
    $checkoutErrors = [Collections.Generic.List[string]]::new()
    foreach ($workflowFile in $workflowFiles) {
        $workflowLines = @(Get-Content -LiteralPath $workflowFile.FullName)
        $inJobs = $false
        $jobs = [Collections.Generic.List[object]]::new()
        $currentJob = $null

        foreach ($line in $workflowLines) {
            if ($line -match '^jobs:\s*$') {
                $inJobs = $true
                continue
            }

            if (-not $inJobs) { continue }

            if ($line -match '^ {2}(?<name>[A-Za-z0-9_-]+):\s*$') {
                if ($null -ne $currentJob) { $jobs.Add($currentJob) }
                $currentJob = [pscustomobject]@{
                    Name = $Matches['name']
                    Lines = [Collections.Generic.List[string]]::new()
                }
                continue
            }

            if ($null -ne $currentJob) { $currentJob.Lines.Add($line) }
        }

        if ($null -ne $currentJob) { $jobs.Add($currentJob) }

        foreach ($job in $jobs) {
            if (-not (($job.Lines -join "`n") -match '(?i)run-local-gate\.ps1')) { continue }
            $gateJobsFound++
            $steps = [Collections.Generic.List[string]]::new()
            $stepLines = $null

            foreach ($line in $job.Lines) {
                if ($line -match '^(?<indent> *)-\s+' -and $Matches['indent'].Length -eq 6) {
                    if ($null -ne $stepLines) { $steps.Add(($stepLines -join "`n")) }
                    $stepLines = [Collections.Generic.List[string]]::new()
                }

                if ($null -ne $stepLines) { $stepLines.Add($line) }
            }
            if ($null -ne $stepLines) { $steps.Add(($stepLines -join "`n")) }

            foreach ($step in $steps) {
                if ($step -notmatch '(?im)^\s*uses:\s*actions/checkout@') { continue }
                $gateCheckoutsFound++
                if ($step -notmatch '(?im)^\s*fetch-depth:\s*["'']?0["'']?\s*(?:#.*)?$') {
                    $checkoutErrors.Add("$($workflowFile.Name)/$($job.Name) requires actions/checkout fetch-depth: 0.")
                }
            }
        }
    }

    if ($gateJobsFound -eq 0) { throw 'No workflow job running the repository validation gate was found.' }
    if ($gateCheckoutsFound -eq 0) { throw 'No actions/checkout step was found in a validation-gate job.' }
    if ($checkoutErrors.Count -gt 0) { throw ($checkoutErrors -join ' ') }

    Write-Output 'PASS: release contract rejects tag-mode Unreleased, finalized mismatch, and permits frontier Unreleased entries.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
