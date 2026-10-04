$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $root 'scripts/validate-release.ps1'
$projectFile = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
$realChangelog = Join-Path $root 'CHANGELOG.md'
$workflowDirectory = Join-Path $root '.github/workflows'
$ciWorkflowPath = Join-Path $workflowDirectory 'ci.yml'
$releaseWorkflowPath = Join-Path $workflowDirectory 'release.yml'
$localGatePath = Join-Path $root 'scripts/run-local-gate.ps1'
$builtCliPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
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
    $null = Invoke-NestedPwsh -ArgumentList $arguments 2>$null
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

    $missingUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.missing-unreleased.md'
    $missingUnreleasedFirstRelease = "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($missingUnreleasedChangelog, $missingUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release missing Unreleased section' (Invoke-ReleaseValidation -Version $version -ChangelogPath $missingUnreleasedChangelog -RequireFinalized -FirstRelease)

    $alternateUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.alternate-unreleased.md'
    $alternateUnreleasedFirstRelease = "# Changelog`n`n## [Next]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($alternateUnreleasedChangelog, $alternateUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release alternate Unreleased heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $alternateUnreleasedChangelog -RequireFinalized -FirstRelease)

    $nestedReleaseChangelog = Join-Path $scratch 'CHANGELOG.nested-release.md'
    $nestedReleaseFirstRelease = "# Changelog`n`n## [Unreleased]`n`n### [$version] - 2026-09-22`n`n#### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($nestedReleaseChangelog, $nestedReleaseFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release entry nested under Unreleased' (Invoke-ReleaseValidation -Version $version -ChangelogPath $nestedReleaseChangelog -RequireFinalized -FirstRelease)

    $candidateChangelog = Join-Path $scratch 'CHANGELOG.finalized.md'
    $filesystemIdentityContractMatch = [regex]::Match(
        (Get-Content -LiteralPath $realChangelog -Raw),
        '(?ms)^- (?<contract>Filesystem path identity follows.*?)(?=\r?\n\r?\n)')
    if (-not $filesystemIdentityContractMatch.Success) {
        throw 'Real changelog is missing the filesystem-identity contract needed by the composed documentation fixture.'
    }
    $filesystemIdentityContract = [regex]::Replace($filesystemIdentityContractMatch.Groups['contract'].Value, '\s+', ' ').Trim()
    $candidateFirstRelease = "# Changelog`n`n<!-- Documentation parity fixture: $filesystemIdentityContract -->`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($candidateChangelog, $candidateFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'real project with finalized version-consistent first-release changelog' (Invoke-ReleaseValidation -Version $version -ChangelogPath $candidateChangelog -RequireFinalized -FirstRelease)

    # Compose the same documentation and release-contract checks used by the
    # publication gate against a concise finalized first-release entry. The
    # changelog is intentionally not copied into fixed-sentence parity: release
    # validation owns its semantic/version contract.
    if (-not (Test-Path -LiteralPath $builtCliPath -PathType Leaf)) { throw 'Built CLI is missing for the composed release-gate test.' }
    $helpPath = Join-Path $scratch 'built-help.txt'
    $help = @(& dotnet $builtCliPath --help 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Built CLI help failed with exit code $LASTEXITCODE." }
    [IO.File]::WriteAllText($helpPath, ($help -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

    $composedRoot = Join-Path $scratch 'composed-release-gate'
    foreach ($relative in @(
        'README.md',
        'CHANGELOG.md',
        'SECURITY.md',
        'docs/DEV.md',
        'src/KeelMatrix.PackageSurface.Cli/README.md',
        'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs'
    )) {
        $destination = Join-Path $composedRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $destination
    }
    Copy-Item -LiteralPath $candidateChangelog -Destination (Join-Path $composedRoot 'CHANGELOG.md') -Force
    $documentationOutput = @(Invoke-NestedPwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-cli-documentation.ps1') -RepositoryRoot $composedRoot -BuiltHelpTextPath $helpPath 2>&1)
    $documentationExitCode = $LASTEXITCODE
    if ($documentationExitCode -ne 0) {
        throw "Composed release documentation gate failed with exit code ${documentationExitCode}: $($documentationOutput -join [Environment]::NewLine)"
    }
    Assert-ValidationPass 'composed finalized first-release gate' (Invoke-ReleaseValidation -Version $version -ChangelogPath (Join-Path $composedRoot 'CHANGELOG.md') -RequireFinalized -FirstRelease)

    $wholeWordChangelog = Join-Path $scratch 'CHANGELOG.whole-word.md'
    [IO.File]::WriteAllText($wholeWordChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Adds package prefixes and nowhere-only documentation examples.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release whole-word marker boundaries' (Invoke-ReleaseValidation -Version $version -ChangelogPath $wholeWordChangelog -RequireFinalized -FirstRelease)

    foreach ($marker in @('now', 'no longer', 'previously', 'formerly', 'used to', 'fixed', 'fixes', 'corrected', 'resolved', 'addressed', 'this removes', 'this fixes', 'changed from')) {
        $markerChangelog = Join-Path $scratch ('CHANGELOG.marker-' + ($marker -replace '[^A-Za-z0-9]+', '-') + '.md')
        [IO.File]::WriteAllText($markerChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- $marker release wording.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release remediation marker '$marker'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $markerChangelog -RequireFinalized -FirstRelease)
    }

    foreach ($category in @('Changed', 'Fixed', 'Deprecated', 'Removed', 'Security', 'Compatibility')) {
        $categoryChangelog = Join-Path $scratch ('CHANGELOG.category-' + $category + '.md')
        [IO.File]::WriteAllText($categoryChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n### $category`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release category '$category'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $categoryChangelog -RequireFinalized -FirstRelease)
    }
    foreach ($heading in @('Notes', 'Metadata')) {
        $headingChangelog = Join-Path $scratch ('CHANGELOG.heading-' + $heading + '.md')
        [IO.File]::WriteAllText($headingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n#### $heading`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release unknown heading '$heading'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $headingChangelog -RequireFinalized -FirstRelease)
    }
    $h1HeadingChangelog = Join-Path $scratch 'CHANGELOG.h1-heading.md'
    [IO.File]::WriteAllText($h1HeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n# Notes`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release unknown level-one heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1HeadingChangelog -RequireFinalized -FirstRelease)
    $h1AddedChangelog = Join-Path $scratch 'CHANGELOG.h1-added.md'
    [IO.File]::WriteAllText($h1AddedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n# Added`n`n- Initial release capability review.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release level-one Added heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1AddedChangelog -RequireFinalized -FirstRelease)

    $setextHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-heading.md'
    [IO.File]::WriteAllText($setextHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`nNotes`n-----`n`n- This heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext category heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextHeadingChangelog -RequireFinalized -FirstRelease)

    $setextDocumentHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-document-heading.md'
    [IO.File]::WriteAllText($setextDocumentHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`nNotes`n=====`n`n- This document heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext document heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextDocumentHeadingChangelog -RequireFinalized -FirstRelease)

    $fencedHeadingChangelog = Join-Path $scratch 'CHANGELOG.fenced-heading.md'
    [IO.File]::WriteAllText($fencedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n~~~markdown`n# Notes`n`nNotes`n-----`n~~~`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release fenced headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $fencedHeadingChangelog -RequireFinalized -FirstRelease)

    $indentedHeadingChangelog = Join-Path $scratch 'CHANGELOG.indented-heading.md'
    [IO.File]::WriteAllText($indentedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n    # Notes`n    Notes`n    -----`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release indented code headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $indentedHeadingChangelog -RequireFinalized -FirstRelease)

    $mismatchedChangelog = Join-Path $scratch 'CHANGELOG.mismatched.md'
    [IO.File]::WriteAllText($mismatchedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [9.9.9] - 2026-09-22`n`n- Wrong version.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'changelog/version disagreement' (Invoke-ReleaseValidation -Version $version -ChangelogPath $mismatchedChangelog -RequireFinalized -FirstRelease)

    Write-Output 'PASS: restored CI/release workflows use one shared fail-closed release contract; real candidate, finalized, and version-mismatch cases behave as required.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
