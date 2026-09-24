param(
    [Parameter(Mandatory)] [string] $ProjectFile
)
$ErrorActionPreference = 'Stop'
$projectFilePath = (Resolve-Path -LiteralPath $ProjectFile).Path
$projectDirectory = Split-Path -Parent $projectFilePath
$root = Split-Path -Parent (Split-Path -Parent $projectDirectory)
$policyPath = Join-Path $root 'SensitiveInputPatterns.props'
$policy = [xml](Get-Content -Raw -LiteralPath $policyPath)
$patterns = @($policy.Project.ItemGroup.SensitivePackInputPattern | ForEach-Object { [string]$_.Pattern })
$exceptions = @($policy.Project.ItemGroup.SensitivePackInputException | ForEach-Object { [string]$_.Pattern })
$packRegex = [string]$policy.Project.PropertyGroup.SensitivePackInputRegex
$exceptionRegex = [string]$policy.Project.PropertyGroup.SensitivePackInputExceptionRegex
$newLine = [string][char]10

function Convert-GlobToRegex([string] $pattern) {
    if ($pattern.EndsWith('/')) {
        return [regex]::Escape($pattern.TrimEnd('/')) + '(?:[\\/].*)?'
    }

    $regex = [regex]::Escape($pattern).Replace('\*', '.*').Replace('\?', '.')
    return $regex
}

$derivedParts = @($patterns | ForEach-Object { Convert-GlobToRegex $_ })
$derivedRegex = '(?i)(^|[\\/])(?:' + ($derivedParts -join '|') + ')$'
$derivedExceptionRegex = '(?i)(^|[\\/])(?:' + (($exceptions | ForEach-Object { Convert-GlobToRegex $_ }) -join '|') + ')$'
$policyCorpus = @(
    '.env', '.env.local', '.env.production', '.env.example', 'nested/.env.example',
    'review.local', 'src/review.local', 'secrets', 'secrets.prod.json', 'secrets/child.json',
    'appsettings.local.json', 'config/appsettings.local.json', 'certificate.pfx', 'ordinary.txt'
)
foreach ($candidate in $policyCorpus) {
    $actual = [regex]::IsMatch($candidate, $packRegex) -and -not [regex]::IsMatch($candidate, $exceptionRegex)
    $derived = [regex]::IsMatch($candidate, $derivedRegex) -and -not [regex]::IsMatch($candidate, $derivedExceptionRegex)
    if ($actual -ne $derived) {
        throw "Sensitive-input policy regex drift for '$candidate'."
    }
}
Write-Output 'PASS: pack regex is equivalent to the canonical SensitiveInputPatterns.props set.'

$expectedBlock = @(
    '# BEGIN GENERATED SENSITIVE INPUT POLICY'
    $patterns
    $exceptions | ForEach-Object { "!$_" }
    '# END GENERATED SENSITIVE INPUT POLICY'
) -join $newLine
$gitignore = [IO.File]::ReadAllText((Join-Path $root '.gitignore')) -replace '\r\n?', $newLine
$blockMatch = [regex]::Match($gitignore, '(?ms)^# BEGIN GENERATED SENSITIVE INPUT POLICY\n.*?^# END GENERATED SENSITIVE INPUT POLICY$')
if (-not $blockMatch.Success -or $blockMatch.Value -ne $expectedBlock) {
    throw 'The .gitignore sensitive-input block is not mechanically synchronized with SensitiveInputPatterns.props.'
}
Write-Output 'PASS: .gitignore sensitive-input block is synchronized with the canonical pattern set.'

$projectText = [IO.File]::ReadAllText($projectFilePath)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-sensitive-pack-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$rejectVariants = @('.env', '.env.local', '.env.production', 'review.local', 'secrets.prod.json', 'secrets/child.json', 'appsettings.local.json', 'certificate.pfx')
$allowVariants = @('.env.example')
try {
    foreach ($variant in $rejectVariants) {
        $path = Join-Path $projectDirectory $variant
        try {
            $parent = Split-Path -Parent $path
            New-Item -ItemType Directory -Force -Path $parent | Out-Null
            [IO.File]::WriteAllText($path, 'LOCAL_SECRET=must-not-pack', [Text.UTF8Encoding]::new($false))
            $include = [string]::Format($newLine + '  <ItemGroup>' + $newLine + '    <None Include="{0}" Pack="true" />' + $newLine + '  </ItemGroup>' + $newLine, $variant)
            [IO.File]::WriteAllText($projectFilePath, $projectText.Replace('</Project>', $include + '</Project>'), [Text.UTF8Encoding]::new($false))
            & dotnet pack $projectFilePath --configuration Release --no-restore --output $scratch 2>$null
            if ($LASTEXITCODE -eq 0) { throw "Pack accepted sensitive/local input '$variant'." }
            Write-Output "PASS: $variant rejected at pack time."
        }
        finally {
            [IO.File]::WriteAllText($projectFilePath, $projectText, [Text.UTF8Encoding]::new($false))
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
            $parent = Split-Path -Parent $path
            if ($parent -ne $projectDirectory -and (Test-Path -LiteralPath $parent)) { Remove-Item -LiteralPath $parent -Recurse -Force }
        }
    }

    foreach ($variant in $allowVariants) {
        $path = Join-Path $projectDirectory $variant
        try {
            [IO.File]::WriteAllText($path, 'EXAMPLE=placeholder', [Text.UTF8Encoding]::new($false))
            $include = [string]::Format($newLine + '  <ItemGroup>' + $newLine + '    <None Include="{0}" Pack="true" />' + $newLine + '  </ItemGroup>' + $newLine, $variant)
            [IO.File]::WriteAllText($projectFilePath, $projectText.Replace('</Project>', $include + '</Project>'), [Text.UTF8Encoding]::new($false))
            & dotnet pack $projectFilePath --configuration Release --no-restore --output $scratch 2>$null
            if ($LASTEXITCODE -ne 0) { throw "Pack rejected allowed exception '$variant'." }
            Write-Output "PASS: $variant allowed as an explicit policy exception."
        }
        finally {
            [IO.File]::WriteAllText($projectFilePath, $projectText, [Text.UTF8Encoding]::new($false))
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
