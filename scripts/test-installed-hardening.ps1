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
