$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $root
$config = Join-Path $root 'NuGet.config'
$probe = Join-Path $root 'src/KeelMatrix.PackageSurface.Probe/KeelMatrix.PackageSurface.Probe.csproj'
$cases = @(
    @{ Name = 'LanguageCSharp'; Project = 'fixtures/consumer/LanguageCSharp/LanguageCSharp.csproj'; Language = 'cs' },
    @{ Name = 'LanguageVisualBasic'; Project = 'fixtures/consumer/LanguageVisualBasic/LanguageVisualBasic.vbproj'; Language = 'vb' },
    @{ Name = 'LanguageFSharp'; Project = 'fixtures/consumer/LanguageFSharp/LanguageFSharp.fsproj'; Language = 'fs' }
)

function Get-Entry([object[]] $entries, [string] $path) {
    $matches = @($entries | Where-Object { $_.capability -in 'CompilerExtension', 'CompileSourceInjection' -and $_.packageRelativePath -eq $path })
    if ($matches.Count -ne 1) { throw "Expected one entry for '$path', found $($matches.Count)." }
    return $matches[0]
}

function Get-SdkPackagePaths([string] $project, [string] $target, [string] $itemName, [string] $assetsPath) {
    $output = @(& dotnet msbuild $project "-t:$target" "-getItem:$itemName" -nologo)
    if ($LASTEXITCODE -ne 0) { throw "SDK selection query failed for $itemName in $project." }

    try {
        $selection = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    }
    catch {
        throw "SDK selection query for $itemName did not return structured output."
    }

    $assets = Get-Content -Raw -LiteralPath $assetsPath | ConvertFrom-Json
    $paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($item in @($selection.Items.$itemName | Where-Object { $_.NuGetPackageId -and $_.FullPath })) {
        $packageId = [string]$item.NuGetPackageId
        $version = [string]$item.NuGetPackageVersion
        $library = @($assets.libraries.PSObject.Properties | Where-Object {
            $parts = $_.Name -split '/', 2
            $parts.Count -eq 2 -and $parts[0].Equals($packageId, [StringComparison]::OrdinalIgnoreCase) -and $parts[1].Equals($version, [StringComparison]::OrdinalIgnoreCase)
        })
        if ($library.Count -ne 1) { throw "SDK selected $itemName item has no unique restore library: $packageId/$version." }

        $fullPath = [IO.Path]::GetFullPath([string]$item.FullPath)
        $matched = $false
        foreach ($folder in $assets.packageFolders.PSObject.Properties) {
            $packageRoot = [IO.Path]::GetFullPath((Join-Path ([string]$folder.Name) ([string]$library[0].Value.path)))
            $prefix = $packageRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
            if ($fullPath.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
                [void]$paths.Add(([IO.Path]::GetRelativePath($packageRoot, $fullPath) -replace '\\', '/'))
                $matched = $true
                break
            }
        }
        if (-not $matched) { throw "SDK selected $itemName item is outside its resolved package root: $packageId/$version." }
    }

    return @($paths)
}

foreach ($case in $cases) {
    $project = Join-Path $root $case.Project
    & dotnet restore $project --configfile $config --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Language fixture restore failed for $($case.Name)." }
    $projectRoot = Split-Path -Parent $project
    $assets = Join-Path $projectRoot 'obj/project.assets.json'
    $sdkAnalyzerPaths = Get-SdkPackagePaths $project 'ResolveLockFileAnalyzers' 'Analyzer' $assets
    $sdkCompilePaths = Get-SdkPackagePaths $project 'ResolvePackageDependenciesForBuild' 'Compile' $assets
    $output = @(& dotnet run --project $probe --configuration Release --no-build -- $assets $projectRoot)
    if ($LASTEXITCODE -ne 0) { throw "Language fixture probe failed for $($case.Name)." }
    $result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not $result.isComplete) { throw "Language fixture $($case.Name) was incomplete: $($result.incompleteReasons -join '; ')." }

    foreach ($language in @('cs', 'vb', 'fs')) {
        $suffix = if ($language -eq 'cs') { 'CSharp' } elseif ($language -eq 'vb') { 'VisualBasic' } else { 'FSharp' }
        $entry = Get-Entry $result.entries "analyzers/dotnet/$language/KeelMatrix.Phase0.$suffix.dll"
        $expected = $sdkAnalyzerPaths -contains $entry.packageRelativePath
        if ([bool]$entry.active -ne $expected) { throw "$($case.Name) analyzer language '$language' expected active=$expected." }
    }

    $optional = Get-Entry $result.entries 'analyzers/dotnet/roslyn4.0/cs/KeelMatrix.Phase0.OptionalCSharp.dll'
    if ([bool]$optional.active -ne ($sdkAnalyzerPaths -contains $optional.packageRelativePath)) { throw "$($case.Name) optional analyzer applicability was incorrect." }
    $legacy = Get-Entry $result.entries 'analyzers/dotnet/roslyn3.8/cs/KeelMatrix.Phase0.LegacyCSharp.dll'
    if ([bool]$legacy.active -ne ($sdkAnalyzerPaths -contains $legacy.packageRelativePath)) { throw "$($case.Name) selected an older Roslyn analyzer alongside the highest applicable version." }
    $satellite = Get-Entry $result.entries 'analyzers/dotnet/cs/KeelMatrix.Phase0.CSharp.resources.dll'
    if ([bool]$satellite.active -ne ($sdkAnalyzerPaths -contains $satellite.packageRelativePath)) { throw "$($case.Name) classified an analyzer satellite as active." }
    $frameworkOnly = Get-Entry $result.entries 'analyzers/net9.0/KeelMatrix.Phase0.FrameworkOnly.dll'
    if ([bool]$frameworkOnly.active -ne ($sdkAnalyzerPaths -contains $frameworkOnly.packageRelativePath)) { throw "$($case.Name) classified an analyzer for an unavailable target framework as active." }
    $any = Get-Entry $result.entries 'contentFiles/any/any/Active.cs'
    if (-not $any.active) { throw "$($case.Name) codeLanguage=any content was not active." }
    foreach ($language in @('cs', 'vb', 'fs')) {
        $extension = if ($language -eq 'cs') { 'cs' } elseif ($language -eq 'vb') { 'vb' } else { 'fs' }
        $entry = Get-Entry $result.entries "contentFiles/$language/any/Active.$extension"
        $expected = $sdkCompilePaths -contains $entry.packageRelativePath
        if ([bool]$entry.active -ne $expected) { throw "$($case.Name) content language '$language' expected active=$expected." }
    }
}

Write-Output 'LANGUAGE_CONVENTIONS=PASS restored C#, Visual Basic, and F# consumers classify analyzer and contentFiles language identity.'
