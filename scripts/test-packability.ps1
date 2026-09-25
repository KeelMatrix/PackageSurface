$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$allowed = @(
    'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj',
    'fixtures/packages/BuildBoth/BuildBoth.csproj',
    'fixtures/packages/BuildMultiTargeting/BuildMultiTargeting.csproj',
    'fixtures/packages/BuildProps/BuildProps.csproj',
    'fixtures/packages/BuildTargets/BuildTargets.csproj',
    'fixtures/packages/BuildTransitive/BuildTransitive.csproj',
    'fixtures/packages/CompilerExtension/CompilerExtension.csproj',
    'fixtures/packages/CompilerExtensionLanguages/CompilerExtensionLanguages.csproj',
    'fixtures/packages/ContentInjection/ContentInjection.csproj',
    'fixtures/packages/ContentInjectionLanguages/ContentInjectionLanguages.csproj',
    'fixtures/packages/ManagedRuntime/ManagedRuntime.csproj',
    'fixtures/packages/NativeRuntime/NativeRuntime.csproj',
    'fixtures/packages/NativeRuntimeTransitive/NativeRuntimeTransitive.csproj',
    'fixtures/packages/OrdinaryLibrary/OrdinaryLibrary.csproj',
    'fixtures/packages/ToolScript/ToolScript.csproj',
    'fixtures/packages/TransitiveBundle/TransitiveBundle.csproj',
    'fixtures/packages/TransitiveRoot/TransitiveRoot.csproj'
) | ForEach-Object { $_.Replace('\', '/') }

$projects = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.Extension -in '.csproj', '.vbproj', '.fsproj' } |
    Where-Object { $_.FullName -notmatch '[\\/]?(bin|obj|artifacts|\.phase0)[\\/]' })
$actual = @($projects | ForEach-Object { $_.FullName.Substring($root.Length + 1).Replace('\', '/') } | Sort-Object)
foreach ($expected in $allowed) {
    if ($actual -notcontains $expected) { throw "Packable project allowlist names missing project '$expected'." }
}

foreach ($project in $projects) {
    $relative = $project.FullName.Substring($root.Length + 1).Replace('\', '/')
    $xml = [xml](Get-Content -LiteralPath $project.FullName -Raw)
    $values = @($xml.Project.PropertyGroup.IsPackable | Where-Object { $_ -ne $null } | ForEach-Object { ([string]$_).Trim().ToLowerInvariant() })
    if ($allowed -contains $relative) {
        if ($values -notcontains 'true') { throw "Allowed package fixture '$relative' is not explicitly packable." }
    }
    elseif ($values -notcontains 'false') {
        throw "Non-shipping project '$relative' is not explicitly non-packable."
    }
}

Write-Output 'PACKABILITY=PASS explicit allowlist matches the repository project graph.'
