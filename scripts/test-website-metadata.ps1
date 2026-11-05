param([string] $ScratchDirectory = ([IO.Path]::GetTempPath()))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WebsiteMetadata.ps1')
. (Join-Path $PSScriptRoot 'ReleaseWorkflowSourceGuard.ps1')
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path ([IO.Path]::GetFullPath($ScratchDirectory)) ("website metadata ' cases " + [Guid]::NewGuid().ToString('N'))
$manifestPath = Join-Path $scratch 'keelmatrix.website.json'
$baseline = [IO.File]::ReadAllText((Join-Path $root 'keelmatrix.website.json'))
$caseCount = 0

function Write-Fixture([string] $Path, [string] $Text) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Assert-Case([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
    $script:caseCount++
}

function Assert-Rejected([scriptblock] $Action, [string] $Name, [string] $Reason = '.') {
    $failure = $null
    try { & $Action } catch { $failure = $_ }
    Assert-Case ($null -ne $failure -and $failure.ToString() -match $Reason) "Expected rejection for $Name; observed: $failure"
}

function New-Package([string] $Id = 'KeelMatrix.PackageSurface', [string] $Visibility = 'public-product', [string] $Role = 'primary') {
    return @{
        PackageId = $Id; PackageTags = "nuget;keelmatrix-$Visibility;keelmatrix-$Role"
        PackageProjectUrl = $websiteRepositoryUrl; RepositoryUrl = $websiteRepositoryUrl
        RepositoryType = 'git'; PublishRepositoryUrl = 'true'; Authors = 'KeelMatrix'
        Description = 'Reviews changes to resolved package capabilities.'; PackageReadmeFile = 'README.md'
        PackageLicenseExpression = 'MIT'; PackAsTool = 'true'; ToolCommandName = 'package-surface'
    }
}

try {
    Write-Fixture $manifestPath $baseline
    $manifest = Read-WebsiteManifest $manifestPath
    Assert-WebsiteMetadata $manifest @((New-Package))
    $caseCount++
    Remove-Item -LiteralPath $manifestPath
    Assert-Rejected { Read-WebsiteManifest $manifestPath } 'missing manifest' 'manifest is missing'
    foreach ($text in @(
        '', '{', 'null', '[]', '{}',
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": 2'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": "1"'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": 1.5'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": null'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": true'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": 1, "schemaVersion": 1'),
        $baseline.Replace('"schemaVersion": 1', '"schemaVersion": 1, "version": "0.1.0"'),
        $baseline.Replace('"schemaVersion"', '"SchemaVersion"'),
        '{"schemaVersion":1,"packages":[]}', '{"schemaVersion":1,"packages":null}',
        $baseline.Replace('"public-product"', '"Public-Product"'),
        $baseline.Replace('"public-product"', 'null'),
        $baseline.Replace('"primary"', '"unknown"'),
        $baseline.Replace('"primary"', '"primary", "role": "primary"'),
        $baseline.Replace('"primary"', '"primary", "description": "extra"'),
        '{"schemaVersion":1,"packages":{"":{}}}'
    )) {
        Write-Fixture $manifestPath $text
        Assert-Rejected { Read-WebsiteManifest $manifestPath } 'malformed manifest'
    }
    foreach ($secondId in @('KeelMatrix.PackageSurface', 'keelmatrix.packagesurface')) {
        $entry = '"visibility":"public-product","role":"primary"'
        Write-Fixture $manifestPath ('{"schemaVersion":1,"packages":{"KeelMatrix.PackageSurface":{' + $entry + '},"' + $secondId + '":{' + $entry + '}}}')
        Assert-Rejected { Read-WebsiteManifest $manifestPath } 'duplicate package ID' 'duplicate package ID'
    }
    foreach ($tags in @(
        '', 'nuget', 'keelmatrix-primary', 'keelmatrix-public-product',
        'keelmatrix-public-product;keelmatrix-public-product;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-primary;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-internal-package;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-primary;keelmatrix-component',
        'keelmatrix-public-product;keelmatrix-internal-package;keelmatrix-primary;keelmatrix-component',
        'KeelMatrix-public-product;keelmatrix-primary', 'keelmatrix-public-product;KEELMATRIX-PRIMARY',
        'keelmatrix-public-product;keelmatrix-primary;keelmatrix-unknown'
    )) {
        $package = New-Package
        $package.PackageTags = $tags
        Assert-Rejected { Assert-WebsiteMetadata $manifest @($package) } 'sentinel count or spelling' 'sentinel'
    }
    foreach ($field in @('visibility', 'role')) {
        $changed = [Collections.Generic.Dictionary[string, object]]::new($manifest, [StringComparer]::OrdinalIgnoreCase)
        $changed['KeelMatrix.PackageSurface'] = @{ visibility = 'public-product'; role = 'primary' }
        $changed['KeelMatrix.PackageSurface'][$field] = if ($field -eq 'visibility') { 'internal-package' } else { 'component' }
        Assert-Rejected { Assert-WebsiteMetadata $changed @((New-Package)) } "$field disagreement" 'disagrees'
    }
    foreach ($field in @('PackageProjectUrl', 'RepositoryUrl')) {
        foreach ($url in @('', 'http://github.com/KeelMatrix/PackageSurface',
            'https://github.com/Other/PackageSurface', 'https://github.com/KeelMatrix/Other',
            'https://example.com/KeelMatrix/PackageSurface', "${websiteRepositoryUrl}.git",
            "$websiteRepositoryUrl/extra", "${websiteRepositoryUrl}?view=1", "$websiteRepositoryUrl#readme",
            'https://user@github.com/KeelMatrix/PackageSurface', 'https://github.com:444/KeelMatrix/PackageSurface')) {
            $package = New-Package
            $package[$field] = $url
            Assert-Rejected { Assert-WebsiteMetadata $manifest @($package) } "$field mismatch" 'canonical'
        }
    }
    foreach ($field in @('Description', 'Authors', 'PackageReadmeFile', 'PackageLicenseExpression', 'ToolCommandName', 'RepositoryType', 'PublishRepositoryUrl')) {
        $package = New-Package
        $package[$field] = ''
        Assert-Rejected { Assert-WebsiteMetadata $manifest @($package) } "missing $field"
    }
    Assert-Rejected { Assert-WebsiteMetadata $manifest @((New-Package 'KeelMatrix.Other')) } 'missing entry' 'entry is missing'
    Assert-Rejected { Assert-WebsiteMetadata $manifest @((New-Package), (New-Package)) } 'duplicate source ID' 'same package ID'
    $family = [Collections.Generic.Dictionary[string, object]]::new($manifest, [StringComparer]::OrdinalIgnoreCase)
    $family.Add('KeelMatrix.Component', @{ visibility = 'public-product'; role = 'component' })
    Assert-Rejected { Assert-WebsiteMetadata $family @((New-Package)) } 'extra manifest entry' 'not a shipping package'
    Assert-WebsiteMetadata $family @((New-Package), (New-Package 'KeelMatrix.Component' 'public-product' 'component'))
    $caseCount++
    $family['KeelMatrix.Component'].role = 'primary'
    Assert-Rejected { Assert-WebsiteMetadata $family @((New-Package), (New-Package 'KeelMatrix.Component')) } 'two primaries' 'exactly one public primary'
    $family['KeelMatrix.PackageSurface'].role = 'component'
    $family['KeelMatrix.Component'].role = 'component'
    Assert-Rejected { Assert-WebsiteMetadata $family @((New-Package -Role component), (New-Package 'KeelMatrix.Component' 'public-product' 'component')) } 'no primary' 'exactly one public primary'
    $family['KeelMatrix.Component'] = @{ visibility = 'internal-package'; role = 'primary' }
    Assert-Rejected { Assert-WebsiteMetadata $family @((New-Package -Role component), (New-Package 'KeelMatrix.Component' 'internal-package')) } 'internal primary cannot represent public family' 'exactly one public primary'
    $internal = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    $internal.Add('KeelMatrix.Infrastructure', @{ visibility = 'internal-package'; role = 'primary' })
    Assert-WebsiteMetadata $internal @((New-Package 'KeelMatrix.Infrastructure' 'internal-package'))
    $caseCount++
    $empty = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
    Assert-Rejected { Assert-WebsiteMetadata $empty @((New-Package)) } 'empty package map' 'entry is missing'
    Assert-Rejected { Assert-WebsiteMetadata $empty @() } 'no shipping packages' 'No shipping packages'

    # Exercise evaluated imports and real entry points in a disposable repository.
    foreach ($file in @('scripts/WebsiteMetadata.ps1', 'scripts/validate-website-metadata.ps1',
        'scripts/run-local-gate.ps1', 'Directory.Build.targets', 'global.json', 'build/Invoke-NestedPwsh.ps1')) {
        Write-Fixture (Join-Path $scratch $file) ([IO.File]::ReadAllText((Join-Path $root $file)))
    }
    $package = New-Package
    $metadataXml = ($package.Keys | ForEach-Object { "<$_>$([Security.SecurityElement]::Escape($package[$_]))</$_>" }) -join ''
    Write-Fixture (Join-Path $scratch 'Directory.Build.props') "<Project><PropertyGroup><TargetFramework>net8.0</TargetFramework>$metadataXml</PropertyGroup></Project>"
    $project = Join-Path $scratch 'src/Tool/Tool.csproj'
    Write-Fixture $project '<Project Sdk="Microsoft.NET.Sdk" />'

    Write-Fixture (Join-Path $scratch 'fixtures/packages/Fixture.csproj') '<Project><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>'
    Write-Fixture $manifestPath $baseline
    $validator = Join-Path $scratch 'scripts/validate-website-metadata.ps1'
    $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $validator 2>&1)
    Assert-Case ($LASTEXITCODE -eq 0 -and ($output -join "`n") -match 'WEBSITE_METADATA=PASS') 'Evaluated inherited source metadata failed.'
    $sibling = Join-Path $scratch 'src/Component/Component.csproj'
    Write-Fixture $sibling '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><PackageId>KeelMatrix.Component</PackageId><PackageTags>keelmatrix-public-product;keelmatrix-component</PackageTags></PropertyGroup></Project>'
    $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $validator 2>&1)
    Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match 'entry is missing') 'New packable sibling was not discovered.'
    Remove-Item -LiteralPath $sibling
    Write-Fixture $project '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><PackageTags>keelmatrix-public-product;keelmatrix-component</PackageTags></PropertyGroup></Project>'
    $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $validator 2>&1)
    Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match 'disagrees') 'Evaluated project override was ignored.'
    Write-Fixture $project '<Project Sdk="Microsoft.NET.Sdk" />'

    $ci = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/ci.yml'))
    $ciJob = @(Get-WorkflowJobs $ci)[0]
    $ciSteps = @(Get-WorkflowSteps -Job $ciJob | Where-Object { $_.RunText -match '-File ./scripts/run-local-gate.ps1' })
    Assert-Case ($ciSteps.Count -eq 1) 'CI must run the shared repository gate exactly once.'
    $ciBlockPath = Join-Path $scratch 'ci-block.ps1'
    Write-Fixture $ciBlockPath ($ciSteps[0].RunText + "`nthrow 'CI continued after invalid metadata.'")
    foreach ($invalid in @('missing', 'malformed', 'disagreement')) {
        if ($invalid -eq 'missing') { Remove-Item -LiteralPath $manifestPath }
        elseif ($invalid -eq 'malformed') { Write-Fixture $manifestPath '{' }
        else { Write-Fixture $manifestPath $baseline.Replace('public-product', 'internal-package') }
        $reason = switch ($invalid) { missing { 'manifest is missing' }; malformed { 'JsonReaderException|invalid start|Expected|depth' }; disagreement { 'disagrees' } }
        Push-Location -LiteralPath $scratch
        try {
            $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File (Join-Path $scratch 'scripts/run-local-gate.ps1') 2>&1)
            Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match $reason) "Local gate did not reject $invalid metadata for the expected reason."
            $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $ciBlockPath 2>&1)
            Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match $reason -and ($output -join "`n") -notmatch 'CI continued after') "CI did not propagate $invalid metadata failure."
            $output = @(& dotnet pack $project --no-build --no-restore --output (Join-Path $scratch 'artifacts') 2>&1)
            Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match $reason) "Pack did not reject $invalid metadata for the expected reason: $($output -join "`n")"
            Assert-Case (@(Get-ChildItem -LiteralPath (Join-Path $scratch 'artifacts') -Filter '*.nupkg' -ErrorAction SilentlyContinue).Count -eq 0) 'Invalid metadata produced a package.'
        }
        finally { Pop-Location }
        Write-Fixture $manifestPath $baseline
    }
    Write-Output "WEBSITE_METADATA_TESTS=PASS CASES=$caseCount"
}
finally {
    $resolvedScratch = [IO.Path]::GetFullPath($scratch)
    $allowedRoot = [IO.Path]::GetFullPath($ScratchDirectory).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolvedScratch.StartsWith($allowedRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture cleanup escaped its scratch directory.' }
    if (Test-Path -LiteralPath $resolvedScratch) { Remove-Item -LiteralPath $resolvedScratch -Recurse -Force }
}
