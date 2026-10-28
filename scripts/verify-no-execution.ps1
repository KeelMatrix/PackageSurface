$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location $root
$env:NUGET_PACKAGES = Join-Path $root '.phase0/packages'
$project = Join-Path $root 'tools/NoExecutionProof/NoExecutionProof.csproj'
$classifier = Join-Path $root 'src/KeelMatrix.PackageSurface.Core/bin/Release/net8.0/KeelMatrix.PackageSurface.Core.dll'
dotnet run --project $project --configuration Release -- $classifier
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (Select-String -Path (Join-Path $root 'src/KeelMatrix.PackageSurface.Probe/*.cs'), (Join-Path $root 'src/KeelMatrix.PackageSurface.Core/*.cs') -Pattern 'Assembly\.Load|AssemblyLoadContext|Process\.Start|Microsoft\.Build|HttpClient|WebRequest|Socket' -Quiet) {
    Write-Error 'Forbidden execution, MSBuild, or network API text found in classifier source.'
    exit 1
}
Write-Output "PASS: NUGET_PACKAGES=$env:NUGET_PACKAGES"
