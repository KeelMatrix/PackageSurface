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

    function Assert-Rejected([string] $name, [string] $text) {
        [IO.File]::WriteAllText($changelog, $text)
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog 2>$null
        if ($LASTEXITCODE -eq 0) { throw "Negative release-contract case '$name' was accepted." }
    }

    Assert-Rejected 'empty finalized section' "# Changelog`n`n## [0.1.0] - 2026-09-22`n"
    Assert-Rejected 'notes left under Unreleased' "# Changelog`n`n## [Unreleased]`n`n- Still planned.`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n"
    Assert-Rejected 'duplicate target sections' "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n`n## [0.1.0] - 2026-09-23`n`n- Duplicate release.`n"
    Assert-Rejected 'marker prose' "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- This release is planned for publication.`n"
    Assert-Rejected 'malformed date' "# Changelog`n`n## [0.1.0] - 2026-99-99`n`n- Initial release.`n"

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

    $releaseWorkflowPath = Join-Path $workflowDirectory 'release.yml'
    if (-not (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf)) { throw 'Release workflow is missing.' }
    $releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
    foreach ($required in @(
        'global-json-file: global.json',
        'timeout-minutes: 45',
        'actions/download-artifact@v4',
        'KEELMATRIX_TELEMETRY: ''off''',
        'run-local-gate.ps1 -ArtifactDirectory artifacts/release',
        'path: artifacts/release/*',
        'KeelMatrix.PackageSurface.${{ env.PACKAGE_VERSION }}.nupkg',
        'KeelMatrix.PackageSurface.${{ env.PACKAGE_VERSION }}.snupkg')) {
        if (-not $releaseWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "Release workflow is missing '$required'." }
    }
    if ($releaseWorkflow.Contains('run-phase0.ps1', [StringComparison]::Ordinal)) { throw 'Release workflow runs a mutating standalone Phase 0 step.' }
    if ($releaseWorkflow -match '(?m)dotnet\s+run\s+--project\s+tests/') { throw 'Release workflow duplicates console leaf tests beside the canonical gate.' }
    if ($releaseWorkflow -match '(?m)^\s*run:\s*dotnet\s+pack\b') { throw 'Release workflow repacks a separately validated artifact.' }
    if (-not $releaseWorkflow.Contains('--repo "${{ github.repository }}"', [StringComparison]::Ordinal) -and
        -not $releaseWorkflow.Contains('GH_REPO: ${{ github.repository }}', [StringComparison]::Ordinal)) { throw 'GitHub release job has no explicit repository context.' }
    if ($releaseWorkflow.Contains("dotnet-version: 8.0.x", [StringComparison]::Ordinal)) { throw 'Release workflow uses a floating SDK version instead of global.json.' }

    $localGate = Get-Content -LiteralPath (Join-Path $root 'scripts/run-local-gate.ps1') -Raw
    foreach ($requiredGate in @('scripts/test-release-contract.ps1', 'scripts/test-vulnerability-audit.ps1')) {
        if (-not $localGate.Contains($requiredGate, [StringComparison]::Ordinal)) { throw "Canonical local gate does not invoke '$requiredGate'." }
    }
    if ($localGate -notmatch '(?s)finally\s*\{.*packagesurface-gate|finally') { throw 'Canonical local gate does not clean run-owned scratch state in finally.' }
    if (-not $localGate.Contains('validate-package-artifact.ps1', [StringComparison]::Ordinal)) { throw 'Canonical local gate does not invoke the reusable final-artifact validator.' }

    Write-Output 'PASS: release contract rejects tag-mode Unreleased, finalized mismatch, and permits frontier Unreleased entries.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
