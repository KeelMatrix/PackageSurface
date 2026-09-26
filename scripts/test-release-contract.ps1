$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $root 'scripts/validate-release.ps1'
$projectFile = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
$realChangelog = Join-Path $root 'CHANGELOG.md'
$workflowDirectory = Join-Path $root '.github/workflows'
$ciWorkflowPath = Join-Path $workflowDirectory 'ci.yml'
$releaseWorkflowPath = Join-Path $workflowDirectory 'release.yml'
$localGatePath = Join-Path $root 'scripts/run-local-gate.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-release-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function Invoke-ReleaseValidation {
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $ChangelogPath,
        [switch] $RequireFinalized,
        [switch] $FirstRelease
    )

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $validator,
        '-Version',
        $Version,
        '-ProjectFile',
        $projectFile,
        '-ChangelogPath',
        $ChangelogPath
    )
    if ($RequireFinalized) { $arguments += '-RequireFinalized' }
    if ($FirstRelease) { $arguments += '-FirstRelease' }
    $null = & pwsh @arguments 2>$null
    return $LASTEXITCODE
}

function Assert-ValidationPass {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [int] $ExitCode)
    if ($ExitCode -ne 0) { throw "Release-contract case '$Name' failed with exit code $ExitCode." }
}

function Assert-ValidationRejects {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [int] $ExitCode)
    if ($ExitCode -eq 0) { throw "Release-contract case '$Name' was accepted." }
}

try {
    $projectText = Get-Content -LiteralPath $projectFile -Raw
    $versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $versionMatch.Success) { throw 'The real packable project has no explicit package version.' }
    $version = $versionMatch.Groups['version'].Value.Trim()

    if (-not (Test-Path -LiteralPath $ciWorkflowPath -PathType Leaf)) { throw 'CI workflow is missing.' }
    if (-not (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf)) { throw 'Release workflow is missing.' }
    $ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw
    $releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw

    foreach ($required in @(
        'push:',
        'branches:',
        '- main',
        'pull_request:',
        'workflow_dispatch:',
        'commit_sha:',
        'required: false',
        'type: string',
        'inputs.commit_sha || github.sha',
        'fetch-depth: 0',
        'persist-credentials: false',
        'global-json-file: global.json',
        'dotnet restore ./KeelMatrix.PackageSurface.sln --configfile ./NuGet.config --force',
        'pwsh -NoLogo -NoProfile -File ./scripts/run-local-gate.ps1',
        'windows-latest',
        'ubuntu-latest',
        'macos-latest',
        'fail-fast: false',
        'KEELMATRIX_TELEMETRY: ''off''',
        'permissions:',
        'contents: read',
        'cancel-in-progress: true',
        'timeout-minutes: 45',
        'commit_sha must be a full 40-character commit SHA.',
        'git rev-parse HEAD'
    )) {
        if (-not $ciWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "CI workflow is missing '$required'." }
    }

    if ($releaseWorkflow -notmatch "(?ms)^on:\s*\r?\n\s+push:\s*\r?\n\s+tags:\s*\r?\n\s+- 'v\*\.\*\.\*'") {
        throw 'Release workflow must be tag-triggered only for v*.*.*.'
    }
    foreach ($required in @(
        'global-json-file: global.json',
        'timeout-minutes: 45',
        'actions/download-artifact@v4',
        'KEELMATRIX_TELEMETRY: ''off''',
        './scripts/validate-release.ps1 -Version $version -RequireFinalized -FirstRelease',
        './scripts/run-local-gate.ps1 -ArtifactDirectory artifacts/release',
        'path: artifacts/release/*',
        'NuGet/login@v1',
        'user: dmitriyzen',
        'needs: validate',
        'needs: [validate, publish]',
        'dotnet nuget push',
        'gh release create',
        '--verify-tag',
        '--repo "${{ github.repository }}"'
    )) {
        if (-not $releaseWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "Release workflow is missing '$required'." }
    }
    if ($releaseWorkflow.Contains("PACKAGE_VERSION:", [StringComparison]::Ordinal)) { throw 'Release workflow maintains a second hard-coded package-version definition.' }
    if ($releaseWorkflow.Contains('run-phase0.ps1', [StringComparison]::Ordinal)) { throw 'Release workflow runs a mutating standalone Phase 0 step.' }
    if ($releaseWorkflow -match '(?m)dotnet\s+run\s+--project\s+tests/') { throw 'Release workflow duplicates console leaf tests beside the canonical gate.' }
    if ($releaseWorkflow -match '(?m)^\s*run:\s*dotnet\s+pack\b') { throw 'Release workflow repacks a separately validated artifact.' }
    if ($releaseWorkflow -notmatch '(?s)validate:.*outputs:.*version:.*release_contract') { throw 'Release workflow does not pass the validated tag version to downstream jobs.' }
    if ($releaseWorkflow -notmatch '(?s)publish:.*needs:\s*validate.*NuGet/login@v1') { throw 'Publish does not depend on validation and Trusted Publishing.' }
    if ($releaseWorkflow -notmatch '(?s)github-release:.*needs:\s*\[validate, publish\]') { throw 'GitHub Release is not ordered after validation and publication.' }

    $localGate = Get-Content -LiteralPath $localGatePath -Raw
    foreach ($requiredGate in @('scripts/test-release-contract.ps1', 'scripts/test-vulnerability-audit.ps1', 'validate-package-artifact.ps1', 'dotnet pack')) {
        if (-not $localGate.Contains($requiredGate, [StringComparison]::Ordinal)) { throw "Canonical local gate is missing '$requiredGate'." }
    }
    if ($localGate -notmatch '(?s)finally\s*\{.*\$scratch') { throw 'Canonical local gate does not clean run-owned scratch state in finally.' }
    if ($localGate -notmatch 'ReadAllBytes|Read-ZipEntryBytes') { throw 'Canonical local gate does not validate package bytes.' }

    # The real repository is intentionally still a candidate: its Unreleased section may pass
    # pre-tag validation but must never pass the finalized tag/publish contract.
    Assert-ValidationPass 'real pre-tag candidate' (Invoke-ReleaseValidation -Version $version -ChangelogPath $realChangelog)
    Assert-ValidationRejects 'real planned/unreleased tag gate' (Invoke-ReleaseValidation -Version $version -ChangelogPath $realChangelog -RequireFinalized)

    $candidateChangelog = Join-Path $scratch 'CHANGELOG.finalized.md'
    [IO.File]::WriteAllText($candidateChangelog, "# Changelog`n`n## [$version] - 2026-09-22`n`n- Finalized release notes.`n", [Text.UTF8Encoding]::new($false))
    $candidateFirstRelease = "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($candidateChangelog, $candidateFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'real project with finalized version-consistent first-release changelog' (Invoke-ReleaseValidation -Version $version -ChangelogPath $candidateChangelog -RequireFinalized -FirstRelease)

    $wholeWordChangelog = Join-Path $scratch 'CHANGELOG.whole-word.md'
    [IO.File]::WriteAllText($wholeWordChangelog, "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Adds package prefixes and nowhere-only documentation examples.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release whole-word marker boundaries' (Invoke-ReleaseValidation -Version $version -ChangelogPath $wholeWordChangelog -RequireFinalized -FirstRelease)

    foreach ($marker in @('now', 'no longer', 'previously', 'formerly', 'used to', 'fixed', 'fixes', 'corrected', 'resolved', 'addressed', 'this removes', 'this fixes', 'changed from')) {
        $markerChangelog = Join-Path $scratch ('CHANGELOG.marker-' + ($marker -replace '[^A-Za-z0-9]+', '-') + '.md')
        [IO.File]::WriteAllText($markerChangelog, "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- $marker release wording.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release remediation marker '$marker'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $markerChangelog -RequireFinalized -FirstRelease)
    }

    foreach ($category in @('Changed', 'Fixed', 'Deprecated', 'Removed', 'Security', 'Compatibility')) {
        $categoryChangelog = Join-Path $scratch ('CHANGELOG.category-' + $category + '.md')
        [IO.File]::WriteAllText($categoryChangelog, "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n### $category`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release category '$category'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $categoryChangelog -RequireFinalized -FirstRelease)
    }

    $mismatchedChangelog = Join-Path $scratch 'CHANGELOG.mismatched.md'
    [IO.File]::WriteAllText($mismatchedChangelog, "# Changelog`n`n## [9.9.9] - 2026-09-22`n`n- Wrong version.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'changelog/version disagreement' (Invoke-ReleaseValidation -Version $version -ChangelogPath $mismatchedChangelog -RequireFinalized -FirstRelease)

    Write-Output 'PASS: restored CI/release workflows use one shared fail-closed release contract; real candidate, finalized, and version-mismatch cases behave as required.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
