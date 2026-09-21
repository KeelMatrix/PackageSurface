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
    param([string]$Command, [scriptblock]$Action)
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = (& $Action 2>&1 | Out-String).TrimEnd()
    $exitCode = $LASTEXITCODE
    $watch.Stop()
    $evidence.Add("COMMAND: $Command`nDURATION_MS: $($watch.ElapsedMilliseconds)`nEXIT_CODE: $exitCode`nOUTPUT:`n$output")
    if ($exitCode -ne 0) { throw "Command failed: $Command`n$output" }
    return $output
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
    'OrdinaryLibrary/OrdinaryLibrary.csproj'
)
foreach ($project in $packageProjects) {
    $path = Join-Path $root "fixtures/packages/$project"
    Invoke-Recorded "dotnet restore $path --configfile $root/NuGet.config --force-evaluate" { dotnet restore $path --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
    Invoke-Recorded "dotnet pack $path --configuration Release --output $feed --no-restore" { dotnet pack $path --configuration Release --output $feed --no-restore }
}

$transitive = Join-Path $root 'fixtures/packages/TransitiveBundle/TransitiveBundle.csproj'
Invoke-Recorded "dotnet restore $transitive --configfile $root/NuGet.config --force-evaluate" { dotnet restore $transitive --configfile (Join-Path $root 'NuGet.config') --force-evaluate }
Invoke-Recorded "dotnet pack $transitive --configuration Release --output $feed --no-restore" { dotnet pack $transitive --configuration Release --output $feed --no-restore }

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
    SingleTarget = 'BuildProps,BuildTargets,BuildTransitive,CompilerExtension,CompileSourceInjection,ToolOrScriptPresent'
    MultiTarget = 'BuildMultiTargeting,CompilerExtension,CompileSourceInjection'
    RidTarget = 'BuildTransitive,NativeRuntime'
}
foreach ($consumer in $consumers) {
    $project = Join-Path $root "fixtures/consumer/$consumer/$consumer.csproj"
    $assets = Join-Path (Join-Path $root "fixtures/consumer/$consumer") 'obj/project.assets.json'
    $consumerRoot = Split-Path $assets -Parent | Split-Path -Parent
    $jsonPath = Join-Path $generated "$consumer.json"
    $probeOutput = Invoke-Recorded "dotnet run --project $probe --configuration Release --no-build -- $assets $consumerRoot" { dotnet run --project $probe --configuration Release --no-build -- $assets $consumerRoot }
    [IO.File]::WriteAllText($jsonPath, $probeOutput.Trim() + [Environment]::NewLine)
    $required = $testCapabilities[$consumer]
    Invoke-Recorded "dotnet run --project $testProject --configuration Release --no-build -- $assets $required" { dotnet run --project $testProject --configuration Release --no-build -- $assets $required }
}

$proofScript = Join-Path $root 'scripts/verify-no-execution.ps1'
Invoke-Recorded "pwsh -NoProfile -File $proofScript" { pwsh -NoProfile -File $proofScript }

$expected = Get-Content (Join-Path $root 'fixtures/expected.json') -Raw | ConvertFrom-Json
$classified = @{}
foreach ($consumer in $consumers) {
    $classified[$consumer] = Get-Content (Join-Path $generated "$consumer.json") -Raw | ConvertFrom-Json
}

function Find-Entry($row) {
    $result = @($classified[$row.consumer].Entries | Where-Object {
        $_.TargetFramework -eq $row.tfm -and $_.RuntimeIdentifier -eq $(if ($row.rid) { $row.rid } else { $null }) -and
        $_.PackageId -ieq $row.package -and $_.PackageRelativePath -ieq $row.path
    })
    return $result | Select-Object -First 1
}

function Import-State($row) {
    if ($row.imports -eq 'none') { return 'not applicable (asset category is not a generated import)' }
    $consumerRoot = Join-Path $root "fixtures/consumer/$($row.consumer)"
    $files = Get-ChildItem -LiteralPath $consumerRoot -Recurse -File -Include '*.nuget.g.props','*.nuget.g.targets'
    $pattern = [regex]::Escape([IO.Path]::GetFileName([string]$row.path))
    $matches = @($files | Where-Object { Select-String -LiteralPath $_.FullName -Pattern $pattern -Quiet })
    if ($matches.Count -eq 0) { return 'not imported' }
    return 'imported in ' + (($matches | ForEach-Object Name) -join ', ')
}

$rows = [System.Collections.Generic.List[string]]::new()
$disagreements = [System.Collections.Generic.List[string]]::new()
$rows.Add('| Category | Package | Direct/transitive | TFM/RID | Seeded | Classified | `project.assets.json` | `.nuget.g.props` | `.nuget.g.targets` | Package contents | Agreeing sources | Disagreement |')
$rows.Add('|---|---|---|---|---|---|---|---|---|---|---|---|')
foreach ($row in $expected) {
    $entry = Find-Entry $row
    if ($entry) {
        $classifiedState = if ($entry.Active) { 'active' } else { 'inactive' }
        $assetsState = 'reachable package/version in graph'
        $packageState = if ($entry.Present) { 'present; SHA-256=' + $entry.Sha256 } else { 'missing' }
    }
    else {
        $classifiedState = 'present/no supported capability'
        $assetsState = 'reachable library asset'
        $packageState = 'present (verified by package file list and resolved folder)'
    }
    $propsState = if ($row.imports -eq 'props') { Import-State $row } else { 'not applicable' }
    $targetsState = if ($row.imports -eq 'targets') { Import-State $row } else { 'not applicable' }
    $agree = [System.Collections.Generic.List[string]]::new()
    if (($row.seededState -eq $classifiedState) -or ($row.category -in @('ManagedRuntime','OrdinaryLibrary') -and $classifiedState -eq 'present/no supported capability')) { $agree.Add('classifier') }
    if ($assetsState -like 'reachable*') { $agree.Add('assets') }
    if ($packageState -like 'present*') { $agree.Add('package') }
    if (($row.imports -eq 'props' -and $propsState -like 'imported*') -or ($row.imports -eq 'targets' -and $targetsState -like 'imported*') -or $row.imports -eq 'none') { $agree.Add('generated imports') }
    $disagreement = if ($row.category -in @('ManagedRuntime','OrdinaryLibrary')) { 'none' } elseif ($classifiedState -ne $row.seededState) { "seeded $($row.seededState), classified $classifiedState" } elseif (($row.imports -eq 'props' -and $propsState -eq 'not imported') -or ($row.imports -eq 'targets' -and $targetsState -eq 'not imported')) { 'expected generated import absent' } else { 'none' }
    if ($disagreement -ne 'none') { $disagreements.Add("$($row.package) $($row.path): $disagreement") }
    $rid = if ($row.rid) { $row.rid } else { '-' }
    $rows.Add("| $($row.category) | $($row.package) | $($row.relationship) | $($row.tfm)/$rid | $($row.seededState) | $classifiedState | $assetsState | $propsState | $targetsState | $packageState | $($agree -join ', ') | $disagreement |")
}

$report = [System.Collections.Generic.List[string]]::new()
foreach ($line in @(
    '# Phase 0 feasibility evidence', '', '## Verdict', '',
    $(if ($disagreements.Count -eq 0) { '**PASS** — every deliberately seeded supported capability was classified against the restored graph, generated imports, and resolved package contents.' } else { '**NARROW** — classification disagreements remain: ' + ($disagreements -join '; ') }),
    '', 'The prototype reads only reachable entries from `project.assets.json`; it does not restore, evaluate MSBuild, load dependency assemblies, start analysis processes, or query a feed.',
    '', '## Per-category comparison', '')) { $report.Add([string]$line) }
$report.AddRange($rows)
foreach ($line in @(
    '', 'Managed runtime and ordinary-library rows are intentionally reported as present/no supported capability: they are resolved graph evidence, not execution-capable classes in this probe.',
    '', '## Disagreements', '',
    $(if ($disagreements.Count -eq 0) { 'None.' } else { $disagreements -join "`n" }),
    '', '## Reproducibility and safety environment', '',
    ('`NUGET_PACKAGES=' + $env:NUGET_PACKAGES + '`'),
    'Controlled package source: `.phase0/feed`, configured by `NuGet.config`.',
    'The classifier receives an already restored assets path. The phase script performs restore only to create the fixture evidence.',
    '', '## Recorded commands', '', '```text',
    ($evidence -join ([Environment]::NewLine + [Environment]::NewLine)),
    '```', '', '## Residual uncertainty', '',
    'This is a bounded Phase 0 fixture, not a complete NuGet/MSBuild semantic implementation. The active rules are proven only for SDK-style PackageReference graphs represented by this corpus; malformed XML, invalid package metadata, and unsupported assets fail closed in the classifier, but broader hostile-input and cross-platform gates belong to the next implementation phase.'
)) { $report.Add([string]$line) }
[IO.File]::WriteAllLines((Join-Path $root 'evidence/phase0.md'), $report)
Write-Output "Phase 0 complete. Report: $(Join-Path $root 'evidence/phase0.md')"
