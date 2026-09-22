param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $ArtifactDirectory
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$requiredToolFiles = @(
    'tools/net8.0/any/DotnetToolSettings.xml',
    'tools/net8.0/any/KeelMatrix.PackageSurface.Core.dll',
    'tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb',
    'tools/net8.0/any/KeelMatrix.PackageSurface.deps.json',
    'tools/net8.0/any/KeelMatrix.PackageSurface.dll',
    'tools/net8.0/any/KeelMatrix.PackageSurface.runtimeconfig.json',
    'tools/net8.0/any/KeelMatrix.Telemetry.dll'
)

function Get-PackageNames([string] $Path) {
    $archive = [IO.Compression.ZipFile]::OpenRead($Path)
    try { return @($archive.Entries | ForEach-Object FullName | Sort-Object) }
    finally { $archive.Dispose() }
}

function Assert-Package([string] $Path) {
    $names = @(Get-PackageNames $Path)
    $toolNames = @($names | Where-Object { $_ -like 'tools/net8.0/any/*' } | Sort-Object)
    if (@(Compare-Object ($requiredToolFiles | Sort-Object) $toolNames).Count -ne 0) { throw 'Package runtime payload is not exact.' }
    $unexpected = @($names | Where-Object { $_ -notin @('[Content_Types].xml', '_rels/.rels', 'KeelMatrix.PackageSurface.nuspec', 'README.md', 'LICENSE/LICENSE', 'icon.png') -and $_ -notlike 'package/services/metadata/core-properties/*.psmdcp' -and $_ -notin $requiredToolFiles })
    if ($unexpected.Count -gt 0) { throw 'Package contains unexpected files.' }
    if (@($names | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' }).Count -ne 1) { throw 'Package metadata file set is not exact.' }
}

function Expect-Rejected([string] $Path) {
    try { Assert-Package $Path; throw 'Mutated package was accepted.' } catch [InvalidOperationException] { throw }
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-package-content-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
try {
    $extraTool = Join-Path $scratch 'extra-tool.nupkg'
    Copy-Item -LiteralPath $PackagePath -Destination $extraTool
    $archive = [IO.Compression.ZipFile]::Open($extraTool, [IO.Compression.ZipArchiveMode]::Update)
    try { $archive.CreateEntry('tools/net8.0/any/unexpected.dll').Open().Dispose() } finally { $archive.Dispose() }
    try { Assert-Package $extraTool; throw 'Unexpected tool payload was accepted.' } catch [System.Exception] { if ($_.Exception.Message -eq 'Unexpected tool payload was accepted.') { throw } }

    $missingTool = Join-Path $scratch 'missing-tool.nupkg'
    Copy-Item -LiteralPath $PackagePath -Destination $missingTool
    $archive = [IO.Compression.ZipFile]::Open($missingTool, [IO.Compression.ZipArchiveMode]::Update)
    try { $archive.GetEntry($requiredToolFiles[0]).Delete() } finally { $archive.Dispose() }
    try { Assert-Package $missingTool; throw 'Missing required package file was accepted.' } catch [System.Exception] { if ($_.Exception.Message -eq 'Missing required package file was accepted.') { throw } }

    $extraPackage = Join-Path $ArtifactDirectory 'KeelMatrix.PackageSurface.0.1.0.extra.nupkg'
    Copy-Item -LiteralPath $PackagePath -Destination $extraPackage
    $artifactNames = @(Get-ChildItem -LiteralPath $ArtifactDirectory -File | Where-Object Extension -eq '.nupkg')
    if ($artifactNames.Count -ne 2) { throw 'Extra package artifact was not detected.' }
    Remove-Item -LiteralPath $extraPackage -Force
    Write-Output 'PASS: package-content gate rejects unexpected tool payloads, missing required files, and extra packages.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
    $extraPackage = Join-Path $ArtifactDirectory 'KeelMatrix.PackageSurface.0.1.0.extra.nupkg'
    if (Test-Path -LiteralPath $extraPackage) { Remove-Item -LiteralPath $extraPackage -Force }
}
