param(
    [Parameter(Mandatory)] [string] $ProjectFile
)
$ErrorActionPreference = 'Stop'
$projectDirectory = Split-Path -Parent (Resolve-Path -LiteralPath $ProjectFile)
$projectText = [IO.File]::ReadAllText((Resolve-Path -LiteralPath $ProjectFile))
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-sensitive-pack-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$variants = @('.env', '.env.local', '.env.production', 'secrets.json', 'appsettings.local.json')
try {
    foreach ($variant in $variants) {
        $path = Join-Path $projectDirectory $variant
        try {
            [IO.File]::WriteAllText($path, 'LOCAL_SECRET=must-not-pack', [Text.UTF8Encoding]::new($false))
            $include = [string]::Format("`n  <ItemGroup>`n    <None Include=""{0}"" Pack=""true"" />`n  </ItemGroup>`n", $variant)
            [IO.File]::WriteAllText((Resolve-Path -LiteralPath $ProjectFile), $projectText.Replace('</Project>', $include + '</Project>'), [Text.UTF8Encoding]::new($false))
            & dotnet pack $ProjectFile --configuration Release --no-restore --output $scratch 2>$null
            if ($LASTEXITCODE -eq 0) { throw "Pack accepted sensitive/local input '$variant'." }
            Write-Output "PASS: $variant rejected at pack time."
        }
        finally {
            [IO.File]::WriteAllText((Resolve-Path -LiteralPath $ProjectFile), $projectText, [Text.UTF8Encoding]::new($false))
            if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
        }
    }
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
