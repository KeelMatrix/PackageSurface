$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
$env:NUGET_PACKAGES = Join-Path $root '.phase0/packages'
$feed = Join-Path $root '.phase0/feed'
$generated = Join-Path $root 'evidence/generated'
if (Test-Path (Join-Path $root '.phase0')) { Remove-Item -LiteralPath (Join-Path $root '.phase0') -Recurse -Force }
New-Item -ItemType Directory -Force -Path $env:NUGET_PACKAGES, $feed, $generated | Out-Null

$evidence = [System.Collections.Generic.List[string]]::new()
function Invoke-Recorded {
    param([string]$Command, [scriptblock]$Action, [switch]$AllowFailure)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = (& $Action 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $watch.Stop()
    $evidence.Add("COMMAND: $Command`nDURATION_MS: $($watch.ElapsedMilliseconds)`nEXIT_CODE: $exitCode`nOUTPUT:`n$output")
    if ($exitCode -ne 0 -and -not $AllowFailure) { throw "Command failed: $Command`n$output" }
    return [pscustomobject]@{ Output = $output; ExitCode = $exitCode }
}

$packageProjects = @(
    'BuildProps/BuildProps.csproj',
    'BuildTargets/BuildTargets.csproj',
    'BuildBoth/BuildBoth.csproj',
    'BuildTransitive/BuildTransitive.csproj',
    'BuildMultiTargeting/BuildMultiTargeting.csproj',
    'CompilerExtension/CompilerExtension.csproj',
    'ContentInjection/ContentInjection.csproj',
    'ManagedRuntime/ManagedRuntime.csproj',
    'NativeRuntime/NativeRuntime.csproj',
    'ToolScript/ToolScript.csproj',
    'OrdinaryLibrary/OrdinaryLibrary.csproj',
    'NativeRuntimeTransitive/NativeRuntimeTransitive.csproj'
)
foreach ($project in $packageProjects) {
    $path = Join-Path $root "fixtures/packages/$project"
    Invoke-Recorded "dotnet restore $path --configfile $root/NuGet.config --force-evaluate" { dotnet restore $path --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
    Invoke-Recorded "dotnet pack $path --configuration Release --output $feed --no-restore" { dotnet pack $path --configuration Release --output $feed --no-restore }
}

$transitive = Join-Path $root 'fixtures/packages/TransitiveBundle/TransitiveBundle.csproj'
Invoke-Recorded "dotnet restore $transitive --configfile $root/NuGet.config --force-evaluate" { dotnet restore $transitive --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
Invoke-Recorded "dotnet pack $transitive --configuration Release --output $feed --no-restore" { dotnet pack $transitive --configuration Release --output $feed --no-restore }
$transitiveRoot = Join-Path $root 'fixtures/packages/TransitiveRoot/TransitiveRoot.csproj'
Invoke-Recorded "dotnet restore $transitiveRoot --configfile $root/NuGet.config --force-evaluate" { dotnet restore $transitiveRoot --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
Invoke-Recorded "dotnet pack $transitiveRoot --configuration Release --output $feed --no-restore" { dotnet pack $transitiveRoot --configuration Release --output $feed --no-restore }

$probe = Join-Path $root 'src/KeelMatrix.PackageSurface.Probe/KeelMatrix.PackageSurface.Probe.csproj'
Invoke-Recorded "dotnet build $probe --configuration Release" { dotnet build $probe --configuration Release }
$consumers = @('SingleTarget', 'MultiTarget', 'RidTarget')
foreach ($consumer in $consumers) {
    $project = Join-Path $root "fixtures/consumer/$consumer/$consumer.csproj"
    Invoke-Recorded "dotnet restore $project --configfile $root/NuGet.config --force-evaluate" { dotnet restore $project --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
}

$testProject = Join-Path $root 'tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj'
Invoke-Recorded "dotnet build $testProject --configuration Release" { dotnet build $testProject --configuration Release }
$testCapabilities = @{
    SingleTarget = 'BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent'
    MultiTarget = 'BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent'
    RidTarget = 'BuildProps,BuildTargets,BuildTransitive,BuildMultiTargeting,CompilerExtension,CompileSourceInjection,NativeRuntime,ToolOrScriptPresent'
}
foreach ($consumer in $consumers) {
    $project = Join-Path $root "fixtures/consumer/$consumer/$consumer.csproj"
    $assets = Join-Path (Join-Path $root "fixtures/consumer/$consumer") 'obj/project.assets.json'
    $consumerRoot = Split-Path $assets -Parent | Split-Path -Parent
    $jsonPath = Join-Path $generated "$consumer.json"
    $probeOutput = Invoke-Recorded "dotnet run --project $probe --configuration Release --no-build -- $assets $consumerRoot" { dotnet run --project $probe --configuration Release --no-build -- $assets $consumerRoot }
    [IO.File]::WriteAllText($jsonPath, $probeOutput.Output.Trim() + [Environment]::NewLine)
    Invoke-Recorded "dotnet run --project $testProject --configuration Release --no-build -- $assets $($testCapabilities[$consumer])" { dotnet run --project $testProject --configuration Release --no-build -- $assets $($testCapabilities[$consumer]) }
}

$proofScript = Join-Path $root 'scripts/verify-no-execution.ps1'
Invoke-Recorded "pwsh -NoProfile -File $proofScript" { pwsh -NoProfile -File $proofScript }
$historyScript = Join-Path $root 'scripts/test-history-hygiene.ps1'
$historyResult = Invoke-Recorded "pwsh -NoProfile -File $historyScript" { pwsh -NoProfile -File $historyScript } -AllowFailure

$expected = @(Get-Content (Join-Path $root 'fixtures/expected.json') -Raw | ConvertFrom-Json)
$classified = @{}
foreach ($consumer in $consumers) {
    $classified[$consumer] = Get-Content (Join-Path $generated "$consumer.json") -Raw | ConvertFrom-Json
}

function Value-Key($value) {
    if ($null -eq $value -or [string]::IsNullOrEmpty([string]$value)) { return '<none>' }
    return ([string]$value).ToLowerInvariant()
}

function Entry-Key($consumer, $entry) {
    return @(
        $consumer,
        (Value-Key $entry.Context),
        (Value-Key $entry.TargetFramework),
        (Value-Key $entry.RuntimeIdentifier),
        (Value-Key $entry.PackageId),
        (Value-Key $entry.Version),
        (Value-Key $entry.Relationship),
        (Value-Key $entry.Capability),
        (Value-Key $entry.PackageRelativePath)
    ) -join '|'
}

function Expected-Key($row) {
    $context = if ($row.context) { $row.context } else { 'Target' }
    $version = if ($row.version) { $row.version } else { '1.0.0' }
    return @(
        $row.consumer,
        (Value-Key $context),
        (Value-Key $row.tfm),
        (Value-Key $row.rid),
        (Value-Key $row.package),
        (Value-Key $version),
        (Value-Key $row.relationship),
        (Value-Key $row.category),
        (Value-Key $row.path)
    ) -join '|'
}

function Get-TargetNames($row, $assets) {
    if (($row.context ?? 'Target') -eq 'Project') {
        return @($assets.targets.psobject.Properties.Name)
    }

    $target = [string]$row.tfm
    if ($row.rid) { $target += '/' + [string]$row.rid }
    return @($target)
}

function Get-AssetsState($row, $assets) {
    $libraryKey = "$($row.package)/$($(if ($row.version) { $row.version } else { '1.0.0' }))"
    $libraryProperty = $assets.libraries.psobject.Properties[$libraryKey]
    if ($null -eq $libraryProperty) { return 'missing (package/version is absent from the graph)' }
    $library = $libraryProperty.Value
    $fileFound = @($library.files) -contains [string]$row.path
    $targetFound = $false
    foreach ($targetName in @(Get-TargetNames $row $assets)) {
        $targetProperty = $assets.targets.psobject.Properties[$targetName]
        if ($null -ne $targetProperty -and $null -ne $targetProperty.Value.psobject.Properties[$libraryKey]) { $targetFound = $true }
    }
    if (-not $targetFound -or -not $fileFound) { return 'missing (asset is not reachable at the seeded context)' }
    return 'proven (reachable package/version and file in graph)'
}

function Get-PackageState($row, $assets) {
    $libraryKey = "$($row.package)/$($(if ($row.version) { $row.version } else { '1.0.0' }))"
    $libraryProperty = $assets.libraries.psobject.Properties[$libraryKey]
    if ($null -eq $libraryProperty) { return 'missing (package/version is absent from the graph)' }
    $library = $libraryProperty.Value
    $folder = @($assets.packageFolders.psobject.Properties.Name | Select-Object -First 1)
    if ($folder.Count -eq 0) { return 'missing (no resolved package folder)' }
    $path = Join-Path (Join-Path $folder[0] ([string]$library.path).Replace('/', '\')) ([string]$row.path.Replace('/', '\'))
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return 'missing (resolved package file is absent)' }
    $hash = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
    return "proven (present; SHA-256=$hash)"
}

function Get-ImportState($row, $consumerRoot, $source) {
    $expectedSource = [string]$row.imports
    if ($expectedSource -eq 'none' -or $expectedSource -ne $source) { return 'not applicable' }
    $path = ([string]$row.path).Replace('\', '/')
    $packageToken = '/' + ([string]$row.package).ToLowerInvariant() + '/' + ([string]$(if ($row.version) { $row.version } else { '1.0.0' })).ToLowerInvariant() + '/'
    $matches = [System.Collections.Generic.List[string]]::new()
    foreach ($file in @(Get-ChildItem -LiteralPath $consumerRoot -Recurse -File | Where-Object { $_.Name -like "*.nuget.g.$source" })) {
        try {
            $document = [xml](Get-Content -LiteralPath $file.FullName -Raw)
            foreach ($import in @($document.SelectNodes("//*[local-name()='Import']"))) {
                $project = ([string]$import.Project).Replace('\', '/').ToLowerInvariant()
                if ($project.Contains($packageToken) -and $project.EndsWith('/' + $path.ToLowerInvariant())) {
                    $matches.Add($file.Name)
                }
            }
        }
        catch {
            return "missing (generated $source file is malformed)"
        }
    }
    if ($matches.Count -eq 0) { return "missing (expected import is absent from generated $source files)" }
    return 'proven (imported in ' + (($matches | Select-Object -Unique) -join ', ') + ')'
}

$actualByKey = @{}
$expectedSupportedKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$missingEntries = [System.Collections.Generic.List[string]]::new()
$unexpectedEntries = [System.Collections.Generic.List[string]]::new()
$disagreements = [System.Collections.Generic.List[string]]::new()
$rows = [System.Collections.Generic.List[string]]::new()
$rows.Add('| Capability | Package | Relationship | Context | TFM/RID | Seeded | Classified | `project.assets.json` | `.nuget.g.props` | `.nuget.g.targets` | Package contents | Proven sources | Disagreement |')
$rows.Add('|---|---|---|---|---|---|---|---|---|---|---|---|---|')

foreach ($consumer in $consumers) {
    foreach ($entry in @($classified[$consumer].Entries)) {
        $key = Entry-Key $consumer $entry
        if ($actualByKey.ContainsKey($key)) { $disagreements.Add("duplicate classifier entry: $key") }
        $actualByKey[$key] = $entry
    }
}

foreach ($row in $expected) {
    $key = Expected-Key $row
    $classifierState = if ($row.classifierState) { [string]$row.classifierState } elseif ($row.seededState) { [string]$row.seededState } else { 'absent' }
    $isSupported = $classifierState -ne 'absent'
    if ($isSupported) { [void]$expectedSupportedKeys.Add($key) }
    $entry = if ($actualByKey.ContainsKey($key)) { $actualByKey[$key] } else { $null }
    $assets = Get-Content (Join-Path $root "fixtures/consumer/$($row.consumer)/obj/project.assets.json") -Raw | ConvertFrom-Json
    $assetsState = Get-AssetsState $row $assets
    $packageState = Get-PackageState $row $assets
    $propsState = Get-ImportState $row (Join-Path $root "fixtures/consumer/$($row.consumer)") 'props'
    $targetsState = Get-ImportState $row (Join-Path $root "fixtures/consumer/$($row.consumer)") 'targets'
    $expectedImportState = if ($row.expectedImportState) { [string]$row.expectedImportState } elseif ($row.imports -eq 'none') { 'not-applicable' } elseif ($classifierState -eq 'active') { 'proven' } else { 'missing' }
    $classifiedState = if (-not $isSupported) { 'not applicable' } elseif ($null -eq $entry) { 'missing' } elseif ($entry.Active) { 'active' } else { 'inactive' }
    $proven = [System.Collections.Generic.List[string]]::new()
    $rowDisagreements = [System.Collections.Generic.List[string]]::new()
    if ($isSupported) {
        if ($null -eq $entry) { $missingEntries.Add($key); $rowDisagreements.Add('classifier entry missing') }
        elseif ($classifiedState -ne $classifierState) { $rowDisagreements.Add("classifier expected $classifierState but observed $classifiedState") }
        elseif (-not $entry.Present -or $entry.Incomplete) { $rowDisagreements.Add('classifier did not prove resolved package contents') }
        else { $proven.Add('classifier') }
    }
    if ($assetsState.StartsWith('proven')) { $proven.Add('assets') } else { $rowDisagreements.Add('project.assets.json: ' + $assetsState) }
    if ($packageState.StartsWith('proven')) { $proven.Add('package') } else { $rowDisagreements.Add('package contents: ' + $packageState) }
    if ($row.imports -eq 'props') {
        if ($expectedImportState -eq 'proven' -and $propsState.StartsWith('proven')) { $proven.Add('generated props') }
        elseif ($expectedImportState -eq 'missing' -and $propsState.StartsWith('missing')) { }
        else { $rowDisagreements.Add('.nuget.g.props expected ' + $expectedImportState + ': ' + $propsState) }
    }
    if ($row.imports -eq 'targets') {
        if ($expectedImportState -eq 'proven' -and $targetsState.StartsWith('proven')) { $proven.Add('generated targets') }
        elseif ($expectedImportState -eq 'missing' -and $targetsState.StartsWith('missing')) { }
        else { $rowDisagreements.Add('.nuget.g.targets expected ' + $expectedImportState + ': ' + $targetsState) }
    }
    if ($rowDisagreements.Count -gt 0) { $disagreements.Add("$($row.consumer) $($row.package) $($row.path): $($rowDisagreements -join '; ')") }
    $context = if ($row.context) { $row.context } else { 'Target' }
    $tfm = if ($row.tfm) { [string]$row.tfm } else { '-' }
    $rid = if ($row.rid) { [string]$row.rid } else { '-' }
    $rows.Add("| $($row.category) | $($row.package) | $($row.relationship) | $context | $tfm/$rid | $classifierState | $classifiedState | $assetsState | $propsState | $targetsState | $packageState | $($proven -join ', ') | $($rowDisagreements -join '; ') |")
}

foreach ($consumer in $consumers) {
    foreach ($entry in @($classified[$consumer].Entries)) {
        $key = Entry-Key $consumer $entry
        if (-not $expectedSupportedKeys.Contains($key)) { $unexpectedEntries.Add($key) }
    }
}
foreach ($key in $unexpectedEntries) { $disagreements.Add("unexpected classifier entry: $key") }

$requiredCategories = @('BuildProps', 'BuildTargets', 'BuildTransitive', 'BuildMultiTargeting', 'CompilerExtension', 'CompileSourceInjection', 'NativeRuntime', 'ToolOrScriptPresent')
foreach ($category in $requiredCategories) {
    $categoryRows = @($expected | Where-Object { $_.category -eq $category })
    if ($categoryRows.Count -eq 0) { $disagreements.Add("corpus matrix has no $category rows"); continue }
    if ($category -eq 'ToolOrScriptPresent') {
        if (@($categoryRows | Where-Object { $_.seededState -ne 'inactive' }).Count -gt 0) { $disagreements.Add('ToolOrScriptPresent matrix contains an active row') }
        continue
    }
    foreach ($relationship in @('direct', 'transitive')) {
        foreach ($state in @('active', 'inactive')) {
            if (@($categoryRows | Where-Object { $_.relationship -eq $relationship -and $_.seededState -eq $state }).Count -eq 0) {
                $disagreements.Add("corpus matrix is missing $category/$relationship/$state")
            }
        }
    }
}

$matrix = [System.Collections.Generic.List[string]]::new()
$matrix.Add('# Phase 0 corpus matrix')
$matrix.Add('')
$matrix.Add('Each supported capability has direct and transitive active/inactive evidence. `ToolOrScriptPresent` is intentionally informational and inactive in every row.')
$matrix.Add('')
$matrix.Add('| Capability | Relationship | State | Context | TFM/RID | Fixture |')
$matrix.Add('|---|---|---|---|---|---|')
foreach ($row in $expected) {
    if ($row.category -in $requiredCategories) {
        $context = if ($row.context) { $row.context } else { 'Target' }
        $tfm = if ($row.tfm) { [string]$row.tfm } else { '-' }
        $rid = if ($row.rid) { [string]$row.rid } else { '-' }
        $matrix.Add("| $($row.category) | $($row.relationship) | $($row.seededState) | $context | $tfm/$rid | fixtures/consumer/$($row.consumer)/$($row.package):$($row.path) |")
    }
}
[IO.File]::WriteAllLines((Join-Path $root 'fixtures/corpus-matrix.md'), $matrix)

$allComplete = @($classified.Values | Where-Object { -not $_.IsComplete }).Count -eq 0
$verdict = if ($disagreements.Count -eq 0 -and $allComplete -and $historyResult.ExitCode -eq 0) { '**PASS** — the complete classifier output agrees with the restored graph, both generated import files, package contents, and the committed corpus matrix.' } else { '**FAIL** — source ledger, classifier completeness, matrix coverage, or repository hygiene checks require correction.' }
$unexpectedText = if ($unexpectedEntries.Count -eq 0) { 'None.' } else { $unexpectedEntries -join "`n" }
$missingText = if ($missingEntries.Count -eq 0) { 'None.' } else { $missingEntries -join "`n" }
$disagreementText = if ($disagreements.Count -eq 0) { 'None.' } else { $disagreements -join "`n" }
$report = [System.Collections.Generic.List[string]]::new()
foreach ($line in @(
    '# Phase 0 feasibility evidence', '', '## Verdict', '', $verdict,
    '', 'The probe reads only reachable entries from `project.assets.json`; it does not restore, evaluate MSBuild, load dependency assemblies, start analysis processes, or query a feed.',
    '', '## Per-category comparison', '')) { $report.Add([string]$line) }
$report.AddRange($rows)
foreach ($line in @(
    '', '## Missing classifier entries', '', $missingText,
    '', '## Unexpected classifier entries', '', $unexpectedText,
    '', '## Disagreements', '', $disagreementText,
    '', '## Corpus matrix', '', 'The committed combination matrix is [fixtures/corpus-matrix.md](../fixtures/corpus-matrix.md).',
    '', '## Reproducibility and safety environment', '', '`NUGET_PACKAGES=.phase0/packages`',
    'Controlled package source: `.phase0/feed`, configured by `NuGet.config`.',
    'The classifier receives already restored assets and generated import files. The phase script performs restore only to create fixture evidence.',
    '', '## Recorded commands', '', '```text', ($evidence -join ([Environment]::NewLine + [Environment]::NewLine)), '```',
    '', '## Residual uncertainty', '',
    'This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; broader hostile-input and cross-platform gates belong to the next implementation phase.'
)) { $report.Add([string]$line) }
[IO.File]::WriteAllLines((Join-Path $root 'evidence/phase0.md'), $report)
Write-Output "Phase 0 complete. Report: $(Join-Path $root 'evidence/phase0.md')"
if ($historyResult.ExitCode -ne 0) { throw 'Repository hygiene gate failed.' }
if ($disagreements.Count -ne 0 -or -not $allComplete) { throw 'Phase 0 comparison failed.' }
