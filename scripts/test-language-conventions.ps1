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

foreach ($case in $cases) {
    $project = Join-Path $root $case.Project
    & dotnet restore $project --configfile $config --force-evaluate
    if ($LASTEXITCODE -ne 0) { throw "Language fixture restore failed for $($case.Name)." }
    $projectRoot = Split-Path -Parent $project
    $assets = Join-Path $projectRoot 'obj/project.assets.json'
    $output = @(& dotnet run --project $probe --configuration Release --no-build -- $assets $projectRoot)
    if ($LASTEXITCODE -ne 0) { throw "Language fixture probe failed for $($case.Name)." }
    $result = ($output -join [Environment]::NewLine) | ConvertFrom-Json
    if (-not $result.isComplete) { throw "Language fixture $($case.Name) was incomplete: $($result.incompleteReasons -join '; ')." }

    foreach ($language in @('cs', 'vb', 'fs')) {
        $suffix = if ($language -eq 'cs') { 'CSharp' } elseif ($language -eq 'vb') { 'VisualBasic' } else { 'FSharp' }
        $entry = Get-Entry $result.entries "analyzers/dotnet/$language/KeelMatrix.Phase0.$suffix.dll"
        $expected = $language -eq $case.Language
        if ([bool]$entry.active -ne $expected) { throw "$($case.Name) analyzer language '$language' expected active=$expected." }
    }

    $optional = Get-Entry $result.entries 'analyzers/dotnet/roslyn4.0/cs/KeelMatrix.Phase0.OptionalCSharp.dll'
    if ([bool]$optional.active -ne ($case.Language -eq 'cs')) { throw "$($case.Name) optional analyzer applicability was incorrect." }
    $any = Get-Entry $result.entries 'contentFiles/any/any/Active.cs'
    if (-not $any.active) { throw "$($case.Name) codeLanguage=any content was not active." }
    foreach ($language in @('cs', 'vb', 'fs')) {
        $extension = if ($language -eq 'cs') { 'cs' } elseif ($language -eq 'vb') { 'vb' } else { 'fs' }
        $entry = Get-Entry $result.entries "contentFiles/$language/any/Active.$extension"
        $expected = $language -eq $case.Language
        if ([bool]$entry.active -ne $expected) { throw "$($case.Name) content language '$language' expected active=$expected." }
    }
}

Write-Output 'LANGUAGE_CONVENTIONS=PASS restored C#, Visual Basic, and F# consumers classify analyzer and contentFiles language identity.'
