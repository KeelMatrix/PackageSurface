param(
    [Parameter(Mandatory)]
    [string] $ToolPath
)

$ErrorActionPreference = 'Stop'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-installed-hardening-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function Require([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-JsonFile([string] $Path, [object] $Value) {
    $json = ($Value | ConvertTo-Json -Depth 30) + [Environment]::NewLine
    [IO.File]::WriteAllText($Path, $json, [Text.UTF8Encoding]::new($false))
}

function Invoke-Tool([string[]] $Arguments) {
    $output = @(& $ToolPath @Arguments 2>&1)
    [pscustomobject]@{
        Output = ($output -join [Environment]::NewLine)
        ExitCode = $LASTEXITCODE
    }
}

try {
    $root = Join-Path $scratch 'consumer'
    $obj = Join-Path $root 'obj'
    $cache = Join-Path $scratch 'packages'
    $packageRoot = Join-Path $cache 'Nested.Package/1.0.0'
    New-Item -ItemType Directory -Force -Path $obj, (Join-Path $packageRoot 'build') | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'Nested.csproj'), '<Project />', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $packageRoot 'build/Package.targets'), '<Project><Import Project="Helper.targets" /></Project>', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $packageRoot 'build/Helper.targets'), '<Project><Target Name="Helper" /></Project>', [Text.UTF8Encoding]::new($false))

    $packageKey = 'Nested.Package/1.0.0'
    $assets = @{
        version = 3
        targets = @{ 'net8.0' = @{ $packageKey = @{ build = @{ 'build/Package.targets' = @{} } } } }
        libraries = @{ $packageKey = @{ type = 'package'; path = $packageKey; files = @('build/Package.targets', 'build/Helper.targets') } }
        packageFolders = @{ $cache = @{} }
        project = @{
            restore = @{ projectPath = (Join-Path $root 'Nested.csproj') }
            frameworks = @{ 'net8.0' = @{ dependencies = @{ 'Nested.Package' = @{ target = 'Package'; version = '[1.0.0, )' } } } }
        }
    }
    $assetsPath = Join-Path $obj 'project.assets.json'
    Write-JsonFile $assetsPath $assets
    [IO.File]::WriteAllText((Join-Path $obj 'Nested.csproj.nuget.g.props'), '<Project />', [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText((Join-Path $obj 'Nested.csproj.nuget.g.targets'), '<Project><Import Project="$(NuGetPackageRoot)/Nested.Package/1.0.0/build/Package.targets" /></Project>', [Text.UTF8Encoding]::new($false))

    $baselinePath = Join-Path $scratch 'baseline.json'
    $baseline = Invoke-Tool @('baseline', $assetsPath, '--output', $baselinePath, '--strict-content', '--format', 'json', '--no-telemetry')
    Require ($baseline.ExitCode -eq 0) "Installed baseline failed: $($baseline.Output)"

    Add-Content -LiteralPath (Join-Path $packageRoot 'build/Helper.targets') -Value '<!-- changed -->'
    $changed = Invoke-Tool @('check', $assetsPath, '--baseline', $baselinePath, '--strict-content', '--format', 'text', '--no-telemetry')
    Require ($changed.ExitCode -eq 1 -and $changed.Output.Contains('PS005', [StringComparison]::Ordinal) -and $changed.Output.Contains('build/Helper.targets', [StringComparison]::Ordinal)) 'Installed nested helper change did not produce PS005.'

    [IO.File]::WriteAllText((Join-Path $packageRoot 'build/Helper.targets'), '<Project><Import Project="Package.targets" /></Project>', [Text.UTF8Encoding]::new($false))
    $cycle = Invoke-Tool @('scan', $assetsPath, '--format', 'json', '--no-telemetry')
    Require ($cycle.ExitCode -eq 0 -and -not $cycle.Output.Contains('PS007', [StringComparison]::Ordinal)) 'Installed nested import cycle was not bounded as a complete result.'

    [IO.File]::WriteAllText((Join-Path $packageRoot 'build/Helper.targets'), '<Project><Import Project="$(SensitiveDynamicImportMarker)" /></Project>', [Text.UTF8Encoding]::new($false))
    $dynamic = Invoke-Tool @('scan', $assetsPath, '--format', 'json', '--no-telemetry')
    Require ($dynamic.ExitCode -eq 2 -and $dynamic.Output.Contains('PS007', [StringComparison]::Ordinal) -and -not $dynamic.Output.Contains('SensitiveDynamicImportMarker', [StringComparison]::Ordinal)) 'Installed dynamic nested import did not fail closed without disclosure.'

    function Write-CaseImport([string] $Path, [string] $Project, [string] $ConditionPath) {
        $xml = "<Project><Import Project=`"$Project`" Condition=`"Exists('$ConditionPath')`" /></Project>"
        [IO.File]::WriteAllText($Path, $xml, [Text.UTF8Encoding]::new($false))
    }

    function New-CaseScenario([string] $Name, [string] $Project, [string] $ConditionPath) {
        $caseRoot = Join-Path $scratch $Name
        $caseObj = Join-Path $caseRoot 'obj'
        $caseCache = Join-Path $caseRoot 'packages'
        $casePackageRoot = Join-Path $caseCache 'Case.Package/1.0.0'
        New-Item -ItemType Directory -Force -Path $caseObj, (Join-Path $casePackageRoot 'build') | Out-Null
        $caseProject = Join-Path $caseRoot 'Case.csproj'
        [IO.File]::WriteAllText($caseProject, '<Project />', [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $casePackageRoot 'build/Case.targets'), '<Project><Target Name="CaseTarget" /></Project>', [Text.UTF8Encoding]::new($false))

        $caseKey = 'Case.Package/1.0.0'
        $caseAssets = @{
            version = 3
            targets = @{ 'net8.0' = @{ $caseKey = @{ build = @{ 'build/Case.targets' = @{} } } } }
            libraries = @{ $caseKey = @{ type = 'package'; path = $caseKey; files = @('build/Case.targets') } }
            packageFolders = @{ $caseCache = @{} }
            project = @{
                restore = @{ projectPath = $caseProject }
                frameworks = @{ 'net8.0' = @{ dependencies = @{ 'Case.Package' = @{ target = 'Package'; version = '[1.0.0, )' } } } }
            }
        }
        $caseAssetsPath = Join-Path $caseObj 'project.assets.json'
        Write-JsonFile $caseAssetsPath $caseAssets
        [IO.File]::WriteAllText((Join-Path $caseObj 'Case.csproj.nuget.g.props'), '<Project />', [Text.UTF8Encoding]::new($false))
        $caseGeneratedTargets = Join-Path $caseObj 'Case.csproj.nuget.g.targets'
        Write-CaseImport $caseGeneratedTargets $Project $ConditionPath
        [pscustomobject]@{
            AssetsPath = $caseAssetsPath
            GeneratedTargets = $caseGeneratedTargets
            PackageRoot = $casePackageRoot
        }
    }

    function Get-CaseEntry([object] $ScanResult) {
        if ($ScanResult.ExitCode -ne 0) { return $null }
        $document = $ScanResult.Output | ConvertFrom-Json
        return @($document.entries | Where-Object { $_.packageRelativePath -eq 'build/Case.targets' })[0]
    }

    function Assert-CaseScan([object] $Scenario, [string] $Label, [bool] $ExpectedActive) {
        $scan = Invoke-Tool @('scan', $Scenario.AssetsPath, '--format', 'json', '--no-telemetry')
        $entry = Get-CaseEntry $scan
        Require ($scan.ExitCode -eq 0 -and $null -ne $entry -and [bool]$entry.active -eq $ExpectedActive) "$Label did not resolve Exists() with the expected host-filesystem result: $($scan.Output)"
        Write-Output "EXISTS_CASE=$Label PASS active=$([bool]$entry.active)"
    }

    function Assert-CaseUnknown([object] $Scenario, [string] $Label) {
        $scan = Invoke-Tool @('scan', $Scenario.AssetsPath, '--format', 'json', '--no-telemetry')
        Require ($scan.ExitCode -eq 2 -and $scan.Output.Contains('PS007', [StringComparison]::Ordinal)) "$Label was not rejected as an unknown Exists() comparison on this case-insensitive volume: $($scan.Output)"
        Write-Output "EXISTS_CASE=$Label PASS unknown=PS007"
    }

    $wrongRootCondition = '$(NuGetPackageRoot)/Case.Package/1.0.0/build/case.targets'
    $correctRootCondition = '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets'
    $rootScenario = New-CaseScenario 'exists-root-slash' '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets' $wrongRootCondition
    $caseSensitiveVolume = -not (Test-Path -LiteralPath (Join-Path $rootScenario.PackageRoot 'build/case.targets'))
    if ($IsWindows) {
        Assert-CaseScan $rootScenario 'root-slash-wrong-case' $true
    }
    elseif ($caseSensitiveVolume) {
        Assert-CaseScan $rootScenario 'root-slash-wrong-case' $false
    }
    else {
        Assert-CaseUnknown $rootScenario 'root-slash-wrong-case'
    }

    if ($IsWindows -or $caseSensitiveVolume) {
        $normalBaseline = Join-Path $scratch 'exists-normal-baseline.json'
        $normalBaselineResult = Invoke-Tool @('baseline', $rootScenario.AssetsPath, '--output', $normalBaseline, '--format', 'json', '--no-telemetry')
        Require ($normalBaselineResult.ExitCode -eq 0) "Exists() normal baseline failed: $($normalBaselineResult.Output)"
        Write-CaseImport $rootScenario.GeneratedTargets '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets' $correctRootCondition
        $normalTransition = Invoke-Tool @('check', $rootScenario.AssetsPath, '--baseline', $normalBaseline, '--format', 'text', '--no-telemetry')
        if ($IsWindows) {
            Require ($normalTransition.ExitCode -eq 0) "Windows Exists() condition casing changed the active surface unexpectedly: $($normalTransition.Output)"
        }
        else {
            Require ($normalTransition.ExitCode -eq 1 -and $normalTransition.Output.Contains('PS003', [StringComparison]::Ordinal)) 'A case-only Exists() correction did not detect the newly active surface in normal mode.'
        }

        Write-CaseImport $rootScenario.GeneratedTargets '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets' $wrongRootCondition
        $strictBaseline = Join-Path $scratch 'exists-strict-baseline.json'
        $strictBaselineResult = Invoke-Tool @('baseline', $rootScenario.AssetsPath, '--output', $strictBaseline, '--strict-content', '--format', 'json', '--no-telemetry')
        Require ($strictBaselineResult.ExitCode -eq 0) "Exists() strict baseline failed: $($strictBaselineResult.Output)"
        Write-CaseImport $rootScenario.GeneratedTargets '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets' $correctRootCondition
        $strictTransition = Invoke-Tool @('check', $rootScenario.AssetsPath, '--baseline', $strictBaseline, '--strict-content', '--format', 'text', '--no-telemetry')
        if ($IsWindows) {
            Require ($strictTransition.ExitCode -eq 0) "Windows strict Exists() condition casing changed the active surface unexpectedly: $($strictTransition.Output)"
        }
        else {
            Require ($strictTransition.ExitCode -eq 1 -and $strictTransition.Output.Contains('PS003', [StringComparison]::Ordinal)) 'A case-only Exists() correction did not detect the newly active surface in strict-content mode.'
        }
    }
    else {
        Write-Output 'EXISTS_CASE=normal-transition SKIP case-insensitive-volume'
        Write-Output 'EXISTS_CASE=strict-transition SKIP case-insensitive-volume'
    }

    $separatorScenario = New-CaseScenario 'exists-root-no-slash' '$(NuGetPackageRoot)Case.Package\1.0.0\build\Case.targets' '$(NuGetPackageRoot)Case.Package\1.0.0\build\case.targets'
    if ($IsWindows) {
        Assert-CaseScan $separatorScenario 'root-no-slash-separator-variant' $true
    }
    elseif ($caseSensitiveVolume) {
        Assert-CaseScan $separatorScenario 'root-no-slash-separator-variant' $false
    }
    else {
        Assert-CaseUnknown $separatorScenario 'root-no-slash-separator-variant'
    }
    Write-CaseImport $separatorScenario.GeneratedTargets '$(NuGetPackageRoot)Case.Package\1.0.0\build\Case.targets' '$(NuGetPackageRoot)Case.Package\1.0.0\build\Case.targets'
    Assert-CaseScan $separatorScenario 'root-no-slash-correct-case' $true

    $absoluteScenario = New-CaseScenario 'exists-absolute' '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets' '$(NuGetPackageRoot)/Case.Package/1.0.0/build/Case.targets'
    $absoluteProject = Join-Path $absoluteScenario.PackageRoot 'build/Case.targets'
    Write-CaseImport $absoluteScenario.GeneratedTargets $absoluteProject $absoluteProject
    Assert-CaseScan $absoluteScenario 'absolute-correct-case' $true
    $absoluteWrong = Join-Path $absoluteScenario.PackageRoot 'build/case.targets'
    Write-CaseImport $absoluteScenario.GeneratedTargets $absoluteProject $absoluteWrong
    if ($IsWindows) {
        Assert-CaseScan $absoluteScenario 'absolute-wrong-case' $true
    }
    elseif ($caseSensitiveVolume) {
        Assert-CaseScan $absoluteScenario 'absolute-wrong-case' $false
    }
    else {
        Assert-CaseUnknown $absoluteScenario 'absolute-wrong-case'
    }

    $oversized = Join-Path $scratch 'oversized.assets.json'
    $stream = [IO.File]::Open($oversized, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try { $stream.SetLength((16 * 1024 * 1024) + 1) } finally { $stream.Dispose() }
    $large = Invoke-Tool @('scan', $oversized, '--format', 'json', '--no-telemetry')
    Require ($large.ExitCode -eq 2 -and -not $large.Output.Contains('oversized.assets.json', [StringComparison]::Ordinal)) 'Installed CLI read oversized input before applying its public limit.'

    Write-Output 'INSTALLED_HARDENING=PASS nested strict-content, cycle, dynamic-import privacy, and pre-discovery size regressions.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
