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

$classifierPath = Join-Path $RepositoryRoot 'src/KeelMatrix.PackageSurface.Probe/ResolvedGraphClassifier.cs'
$classifier = Get-Content -Raw -LiteralPath $classifierPath

$forbidden = @(
    'SplitPackageKey\(',
    'TryGetFrameworkValue\(',
    'packageRoots\.FirstOrDefault',
    'FrameworksMatch\(SplitTarget'
)
foreach ($pattern in $forbidden) {
    if ([regex]::IsMatch($classifier, $pattern)) {
        throw "Independent restore-identity resolution site remains: $pattern"
    }
}

$libraryLookups = @([regex]::Matches($classifier, 'TryGetPropertyIgnoreCase\(libraries'))
if ($libraryLookups.Count -ne 2) {
    throw "Expected exactly two library lookups, both owned by RestoreIdentityIndex; found $($libraryLookups.Count)."
}

if (-not $classifier.Contains('TryGetLibrary(JsonElement libraries', [StringComparison]::Ordinal) -or
    -not $classifier.Contains('TryGetLibraryByCanonicalKey(JsonElement libraries', [StringComparison]::Ordinal) -or
    -not $classifier.Contains('TryGetPackageByRoot', [StringComparison]::Ordinal) -or
    -not $classifier.Contains('TryGetPackageByImportSuffix', [StringComparison]::Ordinal)) {
    throw 'The canonical restore-identity index is missing a required resolution boundary.'
}

Write-Output 'PASS: package, framework, target, package-root, and generated-import identity resolution is routed through RestoreIdentityIndex.'
