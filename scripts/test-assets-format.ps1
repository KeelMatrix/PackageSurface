param(
    [Parameter(Mandatory)]
    [string] $SdkVersion
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$runRoot = if ([string]::IsNullOrWhiteSpace($env:PACKAGESURFACE_TEST_SCRATCH_DIR)) {
    [IO.Path]::GetTempPath()
} else {
    $env:PACKAGESURFACE_TEST_SCRATCH_DIR
}
$scratch = Join-Path $runRoot ('packagesurface-assets-format-' + $SdkVersion + '-' + [Guid]::NewGuid().ToString('N'))
$project = Join-Path $scratch 'ZeroDependency.csproj'
$assets = Join-Path $scratch 'obj/project.assets.json'

New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$globalJson = @{ sdk = @{ version = $SdkVersion; rollForward = 'disable'; allowPrerelease = $false } } | ConvertTo-Json -Depth 4
[IO.File]::WriteAllText((Join-Path $scratch 'global.json'), $globalJson, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText($project, '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><RestoreIgnoreFailedSources>true</RestoreIgnoreFailedSources></PropertyGroup></Project>', [Text.UTF8Encoding]::new($false))

Push-Location $scratch
try {
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & dotnet restore $project --force --no-cache --nologo
    $exitCode = $LASTEXITCODE
    $timer.Stop()
    if ($exitCode -ne 0) {
        throw "SDK $SdkVersion restore returned exit code $exitCode."
    }

    $document = Get-Content -LiteralPath $assets -Raw | ConvertFrom-Json
    Write-Output "SDK_VERSION=$SdkVersion"
    Write-Output "RESTORE_DURATION_MS=$($timer.ElapsedMilliseconds)"
    Write-Output "ASSETS_FORMAT=$($document.version)"
    Write-Output "TARGETS=$(@($document.targets.PSObject.Properties | Select-Object -ExpandProperty Name) -join ',')"
}
finally {
    Pop-Location
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force -ErrorAction SilentlyContinue
    }
}
