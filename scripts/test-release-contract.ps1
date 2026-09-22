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
    Write-Output 'PASS: release contract rejects tag-mode Unreleased, finalized mismatch, and permits frontier Unreleased entries.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
