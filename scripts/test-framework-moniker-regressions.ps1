param(
    [Parameter(Mandatory)]
    [string] $ToolPath
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runRoot = if ([string]::IsNullOrWhiteSpace($env:PACKAGESURFACE_TEST_SCRATCH_DIR)) {
    [IO.Path]::GetTempPath()
} else {
    $env:PACKAGESURFACE_TEST_SCRATCH_DIR
}
$scratch = Join-Path $runRoot ('packagesurface-framework-regressions-' + [Guid]::NewGuid().ToString('N'))
$projectRoot = Join-Path $scratch 'MultiTarget'
$projectFile = Join-Path $projectRoot 'MultiTarget.csproj'
$assetsFile = Join-Path $projectRoot 'obj/project.assets.json'
$baselineFile = Join-Path $scratch 'package-surface.json'
$packages = Join-Path $scratch 'packages'
$nugetConfig = Join-Path $scratch 'NuGet.config'

function Invoke-Expected {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [int] $ExpectedExitCode = 0
    )

    $timer = [Diagnostics.Stopwatch]::StartNew()
    $output = @(& $Action 2>&1)
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    Write-Output "STEP: $Name"
    Write-Output "EXIT_CODE: $exitCode"
    Write-Output "DURATION_MS: $($timer.ElapsedMilliseconds)"
    if ($output.Count -gt 0) { $output | ForEach-Object { Write-Output ([string]$_) } }
    if ($exitCode -ne $ExpectedExitCode) {
        throw "Step '$Name' returned $exitCode; expected $ExpectedExitCode."
    }
}

New-Item -ItemType Directory -Force -Path $projectRoot, $packages | Out-Null
[IO.File]::WriteAllText($nugetConfig, @'
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
'@, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($projectFile, @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;netstandard2.0</TargetFrameworks>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Newtonsoft.Json" Version="13.0.3" />
  </ItemGroup>
</Project>
'@, [Text.UTF8Encoding]::new($false))

Push-Location $root
try {
    Invoke-Expected 'real multi-target restore' {
        & dotnet restore $projectFile --configfile $nugetConfig --packages $packages --no-cache --force --nologo
    }

    $assets = Get-Content -LiteralPath $assetsFile -Raw | ConvertFrom-Json
    if ($assets.version -notin @(3, 4)) {
        throw "The pinned SDK produced an unsupported restore assets format: $($assets.version)."
    }
    $targetNames = @($assets.targets.PSObject.Properties | Select-Object -ExpandProperty Name)
    if ($targetNames -notcontains 'net8.0' -or $targetNames -notcontains '.NETStandard,Version=v2.0') {
        throw "The real multi-target restore did not produce the expected effective target graph keys: $($targetNames -join ', ')."
    }
    Write-Output "REAL_MULTI_TARGET_ASSETS_FORMAT=$($assets.version)"
    Write-Output "REAL_MULTI_TARGET_TARGETS=$($targetNames -join ',')"

    Invoke-Expected 'installed multi-target scan' {
        & $ToolPath scan $projectRoot --format json --no-telemetry
    }
    Invoke-Expected 'installed multi-target baseline' {
        & $ToolPath baseline $projectRoot --output $baselineFile --format json --no-telemetry
    }
    Invoke-Expected 'installed multi-target check' {
        & $ToolPath check $projectRoot --baseline $baselineFile --format json --no-telemetry
    }

    $baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
    if (@($baseline.incompleteReasons).Count -ne 0) {
        throw 'The real multi-target baseline contains incomplete restore evidence.'
    }
    Write-Output 'REAL_MULTI_TARGET_FRAMEWORK_RECONCILIATION=PASS'
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}
