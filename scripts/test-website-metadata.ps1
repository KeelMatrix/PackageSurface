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

    # Use actual NuGet packing, with no build output or external dependencies.
    $manifest = Read-WebsiteManifest $manifestPath
    $packProject = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <PackAsTool>false</PackAsTool><IncludeBuildOutput>false</IncludeBuildOutput>
    <NoPackageAnalysis>true</NoPackageAnalysis><Version>0.1.0</Version>
  </PropertyGroup>
  <ItemGroup><None Include="README.md" Pack="true" PackagePath="" /></ItemGroup>
</Project>
'@
    Write-Fixture $project $packProject
    Write-Fixture (Join-Path (Split-Path $project) 'README.md') 'Metadata test package.'
    Write-Fixture (Join-Path $scratch 'NuGet.config') '<configuration><packageSources><clear /></packageSources></configuration>'
    $output = @(& dotnet restore $project --configfile (Join-Path $scratch 'NuGet.config') 2>&1)
    Assert-Case ($LASTEXITCODE -eq 0) "Metadata fixture restore failed: $output"

    function Assert-PackCase([string] $Name, [string[]] $Arguments, [string] $Reason = '', [string] $ProjectPath = $project) {
        $destination = Join-Path $scratch "artifacts/$Name"
        $packArguments = @('pack', $ProjectPath, '--no-build', '--no-restore', '--output', $destination) + $Arguments
        $timer = [Diagnostics.Stopwatch]::StartNew()
        $output = @(& dotnet @packArguments 2>&1)
        $exitCode = $LASTEXITCODE
        $text = $output -join "`n"
        $archives = @(Get-ChildItem -LiteralPath $destination -Filter '*.nupkg' -ErrorAction SilentlyContinue)
        Write-Output "PACK_CASE=$Name EXIT=$exitCode DURATION_MS=$($timer.ElapsedMilliseconds) ARGS=$($Arguments -join ' ')"
        if ($Reason) {
            Assert-Case ($exitCode -ne 0 -and $text -match $Reason -and $text -notmatch 'WEBSITE_METADATA=PASS') "Pack accepted $Name or failed for another reason: $text"
            Assert-Case ($archives.Count -eq 0) "Pack produced an invalid archive for $Name."
        }
        else {
            Assert-Case ($exitCode -eq 0 -and $text -match 'WEBSITE_METADATA=PASS' -and $archives.Count -eq 1) "Valid pack context $Name failed: $text"
            $zip = [IO.Compression.ZipFile]::OpenRead($archives[0].FullName)
            try {
                $reader = [IO.StreamReader]::new(@($zip.Entries | Where-Object FullName -Like '*.nuspec')[0].Open())
                try { $nuspec = [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
                $actual = New-Package
                $actual.PackageId = $nuspec.package.metadata.id
                $actual.PackageTags = $nuspec.package.metadata.tags
                $actual.PackageProjectUrl = $nuspec.package.metadata.projectUrl
                $actual.RepositoryUrl = $nuspec.package.metadata.repository.url
                $actual.RepositoryType = $nuspec.package.metadata.repository.type
                $actual.Description = $nuspec.package.metadata.description
                $actual.Authors = $nuspec.package.metadata.authors
                $actual.PackageReadmeFile = $nuspec.package.metadata.readme
                $actual.PackageLicenseExpression = $nuspec.package.metadata.license.'#text'
                $actual.PackAsTool = 'false'
                Assert-WebsitePackageMetadata $manifest $actual
                $script:caseCount++
            }
            finally { $zip.Dispose() }
        }
    }

    Assert-PackCase 'release-control' @('--configuration', 'Release')
    Assert-PackCase 'debug-control' @('--configuration', 'Debug')
    Assert-PackCase 'global-control' @('--configuration', 'Release', '-p:PackageId=KeelMatrix.PackageSurface', '-p:PackageTags=nuget%3Bkeelmatrix-public-product%3Bkeelmatrix-primary', "-p:PackageProjectUrl=$websiteRepositoryUrl", "-p:RepositoryUrl=$websiteRepositoryUrl")
    Assert-PackCase 'escaped-description-control' @('--configuration', 'Review', '-p:PackageDescription=Reviews A&B''s "quoted" metadata%3B safely.')
    $index = 0
    foreach ($tags in @(
        'nuget', 'keelmatrix-primary', 'keelmatrix-public-product',
        'keelmatrix-public-product;keelmatrix-public-product;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-primary;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-internal-package;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-primary;keelmatrix-component',
        'keelmatrix-internal-package;keelmatrix-primary',
        'keelmatrix-public-product;keelmatrix-component'
    )) {
        Assert-PackCase "global-tags-$index" @('--configuration', 'Release', "-p:PackageTags=$($tags.Replace(';', '%3B'))") 'sentinel|disagrees'
        $index++
    }
    foreach ($property in @('PackageProjectUrl', 'RepositoryUrl')) {
        Assert-PackCase "global-$property" @('--configuration', 'Release', "-p:${property}=https://github.com/Other/PackageSurface") 'canonical'
    }
    Assert-PackCase 'global-id' @('--configuration', 'Release', '-p:PackageId=KeelMatrix.Undeclared') 'entry is missing'
    Assert-PackCase 'custom-nuspec' @('--configuration', 'Release', '-p:NuspecFile=custom.nuspec') 'does not support custom NuspecFile'
    Write-Fixture $project $packProject.Replace('</Project>', '<PropertyGroup Condition="''$(Configuration)'' == ''Debug''"><PackageTags>keelmatrix-internal-package;keelmatrix-primary</PackageTags></PropertyGroup></Project>')
    Assert-PackCase 'conditioned-tags-release' @('--configuration', 'Release')
    Assert-PackCase 'conditioned-tags-debug' @('--configuration', 'Debug') 'disagrees'
    Write-Fixture $project $packProject
    $siblingText = $packProject.Replace('</Project>', '<PropertyGroup><IsPackable>false</IsPackable><IsPackable Condition="''$(Configuration)'' == ''Debug'' Or ''$(ShippingComponent)'' == ''true''">true</IsPackable><PackageId>KeelMatrix.UndeclaredComponent</PackageId><PackageTags>keelmatrix-public-product;keelmatrix-component</PackageTags></PropertyGroup></Project>')
    Write-Fixture $sibling $siblingText
    Assert-PackCase 'conditioned-membership-release' @('--configuration', 'Release')
    Assert-PackCase 'conditioned-membership-debug' @('--configuration', 'Debug') 'entry is missing'
    Assert-PackCase 'global-membership-release' @('--configuration', 'Release', '-p:ShippingComponent=true') 'entry is missing'
    Write-Fixture (Join-Path (Split-Path $sibling) 'README.md') 'Metadata sibling.'
    $output = @(& dotnet restore $sibling --configfile (Join-Path $scratch 'NuGet.config') 2>&1)
    Assert-Case ($LASTEXITCODE -eq 0) "Metadata sibling restore failed: $output"
    Assert-PackCase 'direct-debug-member' @('--configuration', 'Debug') 'entry is missing' $sibling
    $familyManifest = $baseline | ConvertFrom-Json -AsHashtable
    $familyManifest.packages['KeelMatrix.UndeclaredComponent'] = @{ visibility = 'public-product'; role = 'component' }
    Write-Fixture $manifestPath ($familyManifest | ConvertTo-Json -Depth 5)
    $manifest = Read-WebsiteManifest $manifestPath
    Assert-PackCase 'declared-debug-family-control' @('--configuration', 'Debug')
    Assert-PackCase 'declared-debug-member-control' @('--configuration', 'Debug') '' $sibling
    Assert-PackCase 'declared-global-family-control' @('--configuration', 'Release', '-p:ShippingComponent=true')
    Assert-PackCase 'missing-conditioned-family-member' @('--configuration', 'Release') 'not a shipping package'
    Write-Fixture $manifestPath $baseline
    $manifest = Read-WebsiteManifest $manifestPath
    Remove-Item -LiteralPath $sibling

    # Changes in pack preparation must be read from the current executing instance.
    Write-Fixture $project $packProject.Replace('</Project>', '<Target Name="ChangePackTags" BeforeTargets="_GetPackageFiles"><PropertyGroup><PackageTags>keelmatrix-internal-package;keelmatrix-primary</PackageTags></PropertyGroup></Target></Project>')
    Assert-PackCase 'pack-preparation-tags' @('--configuration', 'Release') 'disagrees'
    $cachedOutputPath = Join-Path $scratch 'artifacts/cached-metadata'
    $output = @(& dotnet msbuild $project '-target:_GetWebsitePackageMetadata;Pack' -property:NoBuild=true -property:Configuration=Release "-property:PackageOutputPath=$cachedOutputPath" 2>&1)
    Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match 'disagrees' -and ($output -join "`n") -notmatch 'WEBSITE_METADATA=PASS') "Cached metadata hid pack-preparation changes: $output"
    Assert-Case (@(Get-ChildItem -LiteralPath $cachedOutputPath -Filter '*.nupkg' -ErrorAction SilentlyContinue).Count -eq 0) 'Cached metadata allowed an invalid archive.'
    Write-Fixture $project $packProject

    $fixtureProject = Join-Path $scratch 'fixtures/packages/Fixture.csproj'
    Write-Fixture $fixtureProject $packProject.Replace('</Project>', '<PropertyGroup><IsPackable>true</IsPackable><PackageId>Local.Fixture</PackageId><PackageTags>fixture</PackageTags></PropertyGroup></Project>')
    Write-Fixture (Join-Path (Split-Path $fixtureProject) 'README.md') 'Controlled local fixture.'
    $output = @(& dotnet restore $fixtureProject --configfile (Join-Path $scratch 'NuGet.config') 2>&1)
    Assert-Case ($LASTEXITCODE -eq 0) "Local fixture restore failed: $output"
    $output = @(& dotnet pack $fixtureProject --no-build --no-restore --configuration Debug --output (Join-Path $scratch 'artifacts/fixture-control') 2>&1)
    Assert-Case ($LASTEXITCODE -eq 0 -and ($output -join "`n") -notmatch 'WEBSITE_METADATA') "Controlled fixture packing was not excluded: $output"

    $ci = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/ci.yml'))
    $ciJob = @(Get-WorkflowJobs $ci)[0]
    $ciSteps = @(Get-WorkflowSteps -Job $ciJob | Where-Object { $_.RunText -match '-File ./scripts/run-local-gate.ps1' })
    Assert-Case ($ciSteps.Count -eq 1) 'CI must run the shared repository gate exactly once.'
    $ciBlockPath = Join-Path $scratch 'ci-block.ps1'
    Write-Fixture $ciBlockPath ($ciSteps[0].RunText + "`nthrow 'CI continued after invalid metadata.'")
    $release = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/release.yml'))
    $releaseJob = @(Get-WorkflowJobs $release)[0]
    $releaseSteps = @(Get-WorkflowSteps -Job $releaseJob | Where-Object { $_.RunText -match '-File ./scripts/run-local-gate.ps1' })
    Assert-Case ($releaseSteps.Count -eq 1) 'Release CI must run the shared repository gate exactly once.'
    $releaseBlockPath = Join-Path $scratch 'release-block.ps1'
    Write-Fixture $releaseBlockPath ('$env:RELEASE_VERSION = ''0.1.0''; $env:RELEASE_TAG = ''v0.1.0''' + "`n" + $releaseSteps[0].RunText + "`nthrow 'Release continued after invalid metadata.'")
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
            $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $releaseBlockPath 2>&1)
            Assert-Case ($LASTEXITCODE -ne 0 -and ($output -join "`n") -match $reason -and ($output -join "`n") -notmatch 'Release continued after') "Release CI did not propagate $invalid metadata failure."
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
