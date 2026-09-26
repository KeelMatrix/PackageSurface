$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-release-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    $project = Join-Path $scratch 'test.csproj'
    $changelog = Join-Path $scratch 'CHANGELOG.md'
    [IO.File]::WriteAllText($project, '<Project><PropertyGroup><Version>0.1.0</Version></PropertyGroup></Project>')
    [IO.File]::WriteAllText($changelog, "# Changelog`n`n## [Unreleased]`n`n- Candidate capabilities.`n")
    & pwsh -NoLogo -NoProfile -WindowStyle Hidden -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -ProjectFile $project -ChangelogPath $changelog
    if ($LASTEXITCODE -ne 0) { throw 'Unreleased candidate validation failed.' }
    & pwsh -NoLogo -NoProfile -WindowStyle Hidden -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Tag-mode validation accepted an Unreleased changelog entry.' }

    $changelogText = "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n"
    [IO.File]::WriteAllText($changelog, $changelogText)
    & pwsh -NoLogo -NoProfile -WindowStyle Hidden -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog
    if ($LASTEXITCODE -ne 0) { throw 'Finalized release validation failed.' }

    function Assert-Rejected([string] $name, [string] $text) {
        [IO.File]::WriteAllText($changelog, $text)
        & pwsh -NoLogo -NoProfile -WindowStyle Hidden -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -RequireFinalized -ProjectFile $project -ChangelogPath $changelog 2>$null
        if ($LASTEXITCODE -eq 0) { throw "Negative release-contract case '$name' was accepted." }
    }

    Assert-Rejected 'empty finalized section' "# Changelog`n`n## [0.1.0] - 2026-09-22`n"
    Assert-Rejected 'notes left under Unreleased' "# Changelog`n`n## [Unreleased]`n`n- Still planned.`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n"
    Assert-Rejected 'duplicate target sections' "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- Initial release.`n`n## [0.1.0] - 2026-09-23`n`n- Duplicate release.`n"
    Assert-Rejected 'marker prose' "# Changelog`n`n## [0.1.0] - 2026-09-22`n`n- This release is planned for publication.`n"
    Assert-Rejected 'malformed date' "# Changelog`n`n## [0.1.0] - 2026-99-99`n`n- Initial release.`n"

    [IO.File]::WriteAllText($changelog, "# Changelog`n`n## [0.2.0] - 2026-09-22`n`n- Wrong version.`n")
    & pwsh -NoLogo -NoProfile -WindowStyle Hidden -File (Join-Path $root 'scripts/validate-release.ps1') -Version 0.1.0 -ProjectFile $project -ChangelogPath $changelog 2>$null
    if ($LASTEXITCODE -eq 0) { throw 'Version mismatch was accepted.' }

    if (Test-Path -LiteralPath (Join-Path $root '.github')) { throw 'This private repository must not contain a .github directory.' }

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
