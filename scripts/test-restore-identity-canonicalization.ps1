param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$metadataReaderPath = Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Probe/RestoreMetadataReader.cs'
$metadataReader = Get-Content -Raw -LiteralPath $metadataReaderPath
$probeSource = (Get-ChildItem -LiteralPath (Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Probe') -Filter '*.cs' -File |
    ForEach-Object { Get-Content -Raw -LiteralPath $_.FullName }) -join "`n"

$forbidden = @(
    'SplitPackageKey\(',
    'TryGetFrameworkValue\(',
    'packageRoots\.FirstOrDefault',
    'FrameworksMatch\(SplitTarget'
)
foreach ($pattern in $forbidden) {
    if ([regex]::IsMatch($probeSource, $pattern)) {
        throw "Independent restore-identity resolution site remains: $pattern"
    }
}

$libraryLookups = @([regex]::Matches($metadataReader, 'TryGetPropertyIgnoreCase\(libraries'))
if ($libraryLookups.Count -ne 2) {
    throw "Expected exactly two library lookups, both owned by RestoreIdentityIndex; found $($libraryLookups.Count)."
}

if (-not $metadataReader.Contains('TryGetLibrary(JsonElement libraries', [StringComparison]::Ordinal) -or
    -not $metadataReader.Contains('TryGetLibraryByCanonicalKey(JsonElement libraries', [StringComparison]::Ordinal) -or
    -not $metadataReader.Contains('TryGetPackageByRoot', [StringComparison]::Ordinal) -or
    -not $metadataReader.Contains('TryGetPackageByImportSuffix', [StringComparison]::Ordinal)) {
    throw 'The canonical restore-identity index is missing a required resolution boundary.'
}

Write-Output 'PASS: package, framework, target, package-root, and generated-import identity resolution is routed through RestoreIdentityIndex.'
