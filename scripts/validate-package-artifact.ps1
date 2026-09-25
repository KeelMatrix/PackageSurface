param(
    [Parameter(Mandatory)] [string] $PackagePath,
    [Parameter(Mandatory)] [string] $SymbolsPath,
    [string] $ExpectedVersion = '0.1.0',
    [string] $ExpectedCommit
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ExpectedCommit)) { $ExpectedCommit = (& git -C $root rev-parse HEAD).Trim() }

function Open-Zip([string] $path) { return [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $path).Path) }
function Read-ZipText([IO.Compression.ZipArchive] $archive, [string] $name) {
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { throw "Archive entry '$name' is missing." }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { return $reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Read-ZipBytes([IO.Compression.ZipArchive] $archive, [string] $name) {
    $entry = $archive.GetEntry($name)
    if ($null -eq $entry) { throw "Archive entry '$name' is missing." }
    $memory = [IO.MemoryStream]::new()
    try { $stream = $entry.Open(); try { $stream.CopyTo($memory) } finally { $stream.Dispose() }; return $memory.ToArray() }
    finally { $memory.Dispose() }
}
function Metadata-Node([xml] $nuspec, [string] $name) {
    $node = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']/*[local-name()='$name']")
    if ($null -eq $node) { throw "Nuspec metadata '$name' is missing." }
    return $node
}

$expectedToolFiles = @(
    'tools/net8.0/any/DotnetToolSettings.xml',
    'tools/net8.0/any/KeelMatrix.PackageSurface.Core.dll',
    'tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb',
    'tools/net8.0/any/KeelMatrix.PackageSurface.deps.json',
    'tools/net8.0/any/KeelMatrix.PackageSurface.dll',
    'tools/net8.0/any/KeelMatrix.PackageSurface.runtimeconfig.json',
    'tools/net8.0/any/KeelMatrix.Telemetry.dll'
)
$fixedPackageFiles = @('[Content_Types].xml', '_rels/.rels', 'KeelMatrix.PackageSurface.nuspec', 'README.md', 'LICENSE/LICENSE', 'icon.png')
$package = Open-Zip $PackagePath
try {
    $names = @($package.Entries | ForEach-Object FullName | Sort-Object)
    $toolNames = @($names | Where-Object { $_ -like 'tools/net8.0/any/*' } | Sort-Object)
    if (@(Compare-Object ($expectedToolFiles | Sort-Object) $toolNames).Count -ne 0) { throw 'Shipping package tool payload is not exact.' }
    $unexpected = @($names | Where-Object { $_ -notin $fixedPackageFiles -and $_ -notlike 'package/services/metadata/core-properties/*.psmdcp' -and $_ -notin $expectedToolFiles })
    if ($unexpected.Count -gt 0 -or @($names | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' }).Count -ne 1) { throw 'Shipping package file set is not exact.' }

    [xml]$nuspec = Read-ZipText $package 'KeelMatrix.PackageSurface.nuspec'
    $metadata = $nuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($metadata.id -ne 'KeelMatrix.PackageSurface' -or $metadata.version -ne $ExpectedVersion -or $metadata.authors -ne 'KeelMatrix') { throw 'Package identity, version, or authors are incorrect.' }
    if ($metadata.description -ne 'Baselines the execution-capable surface of resolved NuGet dependencies. Detects new or changed MSBuild, compiler-extension, source/content, and native package assets.') { throw 'Package description is incorrect.' }
    if ($metadata.tags -ne 'nuget supply-chain msbuild buildTransitive analyzers source-generators package-security dependencies ci dotnet-tool') { throw 'Package tags are incorrect.' }
    if ($metadata.packageTypes.packageType.name -ne 'DotnetTool' -or $metadata.readme -ne 'README.md' -or $metadata.icon -ne 'icon.png') { throw 'Package tool/readme/icon metadata is incorrect.' }
    if ($metadata.repository.url -ne 'https://github.com/KeelMatrix/PackageSurface' -or $metadata.repository.type -ne 'git' -or $metadata.repository.commit -ne $ExpectedCommit) { throw 'Repository metadata or commit is incorrect.' }
    if ($null -ne $metadata.dependencies -or $metadata.OuterXml -match '(?i)KeelMatrix\.PackageSurface\.Core') { throw 'Shipping package contains an accidental project dependency.' }

    $settings = [xml](Read-ZipText $package 'tools/net8.0/any/DotnetToolSettings.xml')
    $command = $settings.SelectSingleNode("/*[local-name()='DotNetCliTool']/*[local-name()='Commands']/*[local-name()='Command']")
    if ($null -eq $command -or $command.Name -ne 'package-surface' -or $command.EntryPoint -ne 'KeelMatrix.PackageSurface.dll' -or $command.Runner -ne 'dotnet') { throw 'Dotnet tool command metadata is incorrect.' }
    $deps = Read-ZipText $package 'tools/net8.0/any/KeelMatrix.PackageSurface.deps.json'
    if ($metadata.OuterXml -match '(?i)<dependency[^>]+KeelMatrix\.PackageSurface\.Core') { throw 'Nuspec contains an accidental dependency on the non-public Core project.' }
    if ($deps -notmatch '(?i)KeelMatrix\.Telemetry/') { throw 'Shipping dependency graph omits the intended telemetry assembly.' }

    $pdbBytes = Read-ZipBytes $package 'tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb'
    $pdbText = [Text.Encoding]::UTF8.GetString($pdbBytes)
    if ($pdbText -notmatch 'https://raw\.githubusercontent\.com/KeelMatrix/PackageSurface/') { throw 'Shipping PDB has no SourceLink metadata.' }
    $privatePathMarker = -join (@(112,97,112,101,114,99,108,105,112) | ForEach-Object { [char]$_ })
    if ($pdbText -match "(?i)([A-Za-z]:\\|/Users/|/home/|$privatePathMarker|AppData|obj\\|bin\\)") { throw 'Shipping PDB contains a local filesystem path.' }
}
finally { $package.Dispose() }

$symbols = Open-Zip $SymbolsPath
try {
    $symbolNames = @($symbols.Entries | ForEach-Object FullName | Sort-Object)
    $symbolPdb = 'tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb'
    $symbolFixed = @('[Content_Types].xml', '_rels/.rels', 'KeelMatrix.PackageSurface.nuspec', $symbolPdb)
    $unexpectedSymbols = @($symbolNames | Where-Object { $_ -notin $symbolFixed -and $_ -notlike 'package/services/metadata/core-properties/*.psmdcp' })
    if ($unexpectedSymbols.Count -gt 0 -or @($symbolNames | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' }).Count -ne 1) { throw 'Symbols package file set is not exact.' }
    [xml]$symbolNuspec = Read-ZipText $symbols 'KeelMatrix.PackageSurface.nuspec'
    $symbolMetadata = $symbolNuspec.SelectSingleNode("/*[local-name()='package']/*[local-name()='metadata']")
    if ($symbolMetadata.id -ne 'KeelMatrix.PackageSurface' -or $symbolMetadata.version -ne $ExpectedVersion) { throw 'Symbols package identity or version is incorrect.' }
    $package = Open-Zip $PackagePath
    try {
        if ([Convert]::ToHexString((Read-ZipBytes $package $symbolPdb)) -ne [Convert]::ToHexString((Read-ZipBytes $symbols $symbolPdb))) { throw 'Symbols PDB does not match the shipping package PDB.' }
    }
    finally { $package.Dispose() }
}
finally { $symbols.Dispose() }

Write-Output "PACKAGE_VALIDATION=PASS ARTIFACT=$([IO.Path]::GetFileName($PackagePath)) SYMBOLS=$([IO.Path]::GetFileName($SymbolsPath))"
