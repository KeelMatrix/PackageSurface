$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $root
$timer = [Diagnostics.Stopwatch]::StartNew()
$artifactRoot = Join-Path $root 'artifacts/gate'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-gate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null
$solution = Join-Path $root 'KeelMatrix.PackageSurface.sln'
$shippingPackages = Join-Path $scratch 'shipping-packages'
$nugetConfig = Join-Path $root 'NuGet.config'

function Invoke-GateStep {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [scriptblock] $Action,
        [int] $ExpectedExitCode = 0
    )

    $stepTimer = [Diagnostics.Stopwatch]::StartNew()
    $output = @(& $Action 2>&1)
    $exitCode = $LASTEXITCODE
    $stepTimer.Stop()
    Write-Output "STEP: $Name"
    Write-Output "COMMAND_EXIT_CODE: $exitCode"
    Write-Output "DURATION_MS: $($stepTimer.ElapsedMilliseconds)"
    if ($output.Count -gt 0) { $output | ForEach-Object { Write-Output ([string]$_) } }
    if ($exitCode -ne $ExpectedExitCode) {
        throw "Gate step '$Name' failed with exit code $exitCode; expected $ExpectedExitCode."
    }
}

function Ensure-DotnetRootForInstalledTool {
    if ($IsWindows -or -not [string]::IsNullOrWhiteSpace($env:DOTNET_ROOT)) {
        return
    }

    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop | Select-Object -First 1
    $dotnetPath = $dotnetCommand.Source
    try {
        $dotnetPath = (Resolve-Path -LiteralPath $dotnetPath -ErrorAction Stop).Path
    }
    catch {
        # The command path is still useful when the host exposes no resolvable symlink target.
    }

    $candidate = Split-Path -Parent $dotnetPath
    while (-not [string]::IsNullOrWhiteSpace($candidate)) {
        if (Test-Path -LiteralPath (Join-Path $candidate 'host/fxr') -PathType Container) {
            $env:DOTNET_ROOT = $candidate
            Write-Output "DOTNET_ROOT_FOR_TOOL: $candidate"
            return
        }

        $parent = Split-Path -Parent $candidate
        if ([string]::IsNullOrWhiteSpace($parent) -or $parent -eq $candidate) {
            break
        }
        $candidate = $parent
    }

    throw "Unable to derive DOTNET_ROOT for the installed Linux tool from '$dotnetPath'. Set DOTNET_ROOT to the directory containing host/fxr and rerun the gate."
}

try {
    $env:KEELMATRIX_TELEMETRY = 'off'
    $env:NUGET_PACKAGES = $shippingPackages
    Invoke-GateStep 'shipping restore' {
        & dotnet restore $solution --configfile $nugetConfig --packages $shippingPackages --no-cache --force
    }
    $phase0Evidence = Join-Path $root 'evidence/phase0.md'
    $phase0EvidenceCopy = Join-Path $scratch 'phase0.md'
    $hadPhase0Evidence = Test-Path -LiteralPath $phase0Evidence -PathType Leaf
    if ($hadPhase0Evidence) {
        Copy-Item -LiteralPath $phase0Evidence -Destination $phase0EvidenceCopy -Force
    }
    try {
        Invoke-GateStep 'controlled restore and permanent fixture suite' {
            & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/run-phase0.ps1')
        }
    }
    finally {
        if ($hadPhase0Evidence) {
            Copy-Item -LiteralPath $phase0EvidenceCopy -Destination $phase0Evidence -Force
        }
        elseif (Test-Path -LiteralPath $phase0Evidence) {
            Remove-Item -LiteralPath $phase0Evidence -Force
        }
        $env:NUGET_PACKAGES = $shippingPackages
    }
    Invoke-GateStep 'format verification' {
        & dotnet format $solution --verify-no-changes --no-restore
    }
    Invoke-GateStep 'Release build' {
        & dotnet build $solution --configuration Release --no-restore
    }
    Invoke-GateStep 'no-code-execution proof' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/verify-no-execution.ps1')
    }
    $cliTests = Join-Path $root 'tests/KeelMatrix.PackageSurface.Cli.Tests/KeelMatrix.PackageSurface.Cli.Tests.csproj'
    Invoke-GateStep 'CLI contract and resource tests' {
        & dotnet run --project $cliTests --configuration Release --no-build
    }
    Invoke-GateStep 'release contract regressions' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-release-contract.ps1')
    }
    Invoke-GateStep 'vulnerability audit regressions' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-vulnerability-audit.ps1')
    }

    $singleProject = Join-Path $root 'fixtures/consumer/SingleTarget'
    $singleAssets = Join-Path $singleProject 'obj/project.assets.json'
    $cliProject = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
    $feed = Join-Path $artifactRoot 'feed'
    New-Item -ItemType Directory -Force -Path $feed | Out-Null
    Invoke-GateStep 'package build' {
        & dotnet pack $cliProject --configuration Release --no-restore --output $feed
    }

    $packageVersion = '0.1.0'
    $allArtifacts = @(Get-ChildItem -LiteralPath $feed -File)
    $expectedArtifactNames = @("KeelMatrix.PackageSurface.$packageVersion.nupkg", "KeelMatrix.PackageSurface.$packageVersion.snupkg")
    $actualArtifactNames = @($allArtifacts | Select-Object -ExpandProperty Name | Sort-Object)
    if (@(Compare-Object ($expectedArtifactNames | Sort-Object) $actualArtifactNames).Count -ne 0) { throw ('The package output set is not exact: ' + ($actualArtifactNames -join ', ')) }
    $nupkgs = @($allArtifacts | Where-Object Name -eq "KeelMatrix.PackageSurface.$packageVersion.nupkg")
    $snupkgs = @($allArtifacts | Where-Object Name -eq "KeelMatrix.PackageSurface.$packageVersion.snupkg")

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    function Read-ZipEntryBytes([IO.Compression.ZipArchive] $Archive, [string] $Name) {
        $entry = $Archive.GetEntry($Name)
        if ($null -eq $entry) { throw "Required package entry '$Name' is missing." }
        $memory = [IO.MemoryStream]::new()
        try {
            $stream = $entry.Open()
            try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
            return $memory.ToArray()
        }
        finally { $memory.Dispose() }
    }

    $package = [IO.Compression.ZipFile]::OpenRead($nupkgs[0].FullName)
    try {
        $names = @($package.Entries | ForEach-Object FullName | Sort-Object)
        Write-Output 'PACKAGE_FILE_SET:'
        $names | ForEach-Object { Write-Output $_ }
        $nuspecEntry = $package.GetEntry('KeelMatrix.PackageSurface.nuspec')
        if ($null -eq $nuspecEntry) { throw 'The package has no nuspec.' }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne 'KeelMatrix.PackageSurface' -or $nuspec.package.metadata.version -ne $packageVersion -or $nuspec.package.metadata.packageTypes.packageType.name -ne 'DotnetTool') { throw 'Package identity, version, or type is incorrect.' }
        if ($nuspec.package.metadata.readme -ne 'README.md' -or $nuspec.package.metadata.license.type -ne 'expression' -or $nuspec.package.metadata.license.'#text' -ne 'MIT') { throw 'README or license metadata is incorrect.' }
        if ($nuspec.package.metadata.icon -ne 'icon.png') { throw 'Package icon metadata must point to icon.png.' }
        if ($nuspec.package.metadata.repository.url -ne 'https://github.com/KeelMatrix/PackageSurface') { throw 'Repository metadata is incorrect.' }
        $unwanted = @($names | Where-Object { $_ -match '(^|/)(fixtures|tests|research|\.env|obj|bin|\.phase0|nonshipping|appsettings\.local|secrets)' })
        if ($unwanted.Count -gt 0) { throw ('Unwanted package files: ' + ($unwanted -join ', ')) }
        $requiredToolFiles = @(
            'tools/net8.0/any/DotnetToolSettings.xml',
            'tools/net8.0/any/KeelMatrix.PackageSurface.Core.dll',
            'tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb',
            'tools/net8.0/any/KeelMatrix.PackageSurface.deps.json',
            'tools/net8.0/any/KeelMatrix.PackageSurface.dll',
            'tools/net8.0/any/KeelMatrix.PackageSurface.runtimeconfig.json',
            'tools/net8.0/any/KeelMatrix.Telemetry.dll'
        )
        if (@(Compare-Object ($requiredToolFiles | Sort-Object) @($names | Where-Object { $_ -like 'tools/net8.0/any/*' } | Sort-Object)).Count -ne 0) { throw 'The package runtime payload is not exact.' }
        $unexpected = @($names | Where-Object { $_ -notin @('[Content_Types].xml', 'KeelMatrix.PackageSurface.nuspec', 'README.md', 'LICENSE/LICENSE', 'icon.png', '_rels/.rels') -and $_ -notlike 'package/services/metadata/core-properties/*.psmdcp' -and $_ -notin $requiredToolFiles })
        if ($unexpected.Count -gt 0) { throw ('Unexpected package files: ' + ($unexpected -join ', ')) }
        if (@($names | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' }).Count -ne 1) { throw 'The package metadata payload is not exact.' }
        if ([Convert]::ToBase64String((Read-ZipEntryBytes $package 'README.md')) -ne [Convert]::ToBase64String([IO.File]::ReadAllBytes((Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/README.md')))) { throw 'Packed README does not match its configured source.' }
        $sourceIconHash = (Get-FileHash -LiteralPath (Join-Path $root 'icon.png') -Algorithm SHA256).Hash
        $packedIconHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData((Read-ZipEntryBytes $package 'icon.png')))
        if ($sourceIconHash -ne $packedIconHash) { throw 'Packed icon does not match the founder-owned repository icon.' }
    }
    finally { $package.Dispose() }

    $snupkg = [IO.Compression.ZipFile]::OpenRead($snupkgs[0].FullName)
    try {
        $symbolNames = @($snupkg.Entries | ForEach-Object FullName | Sort-Object)
        $expectedSymbolFixed = @('[Content_Types].xml', '_rels/.rels', 'KeelMatrix.PackageSurface.nuspec')
        $expectedSymbolPdb = @('tools/net8.0/any/KeelMatrix.PackageSurface.Core.pdb')
        $expectedSymbolNames = $expectedSymbolFixed + $expectedSymbolPdb + @($symbolNames | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' })
        if (@(Compare-Object ($expectedSymbolNames | Sort-Object) $symbolNames).Count -ne 0 -or @($symbolNames | Where-Object { $_ -like 'package/services/metadata/core-properties/*.psmdcp' }).Count -ne 1) { throw ('The symbols package file set is not exact: ' + ($symbolNames -join ', ')) }
        Write-Output 'SYMBOLS_FILE_SET:'
        $symbolNames | ForEach-Object { Write-Output $_ }
    }
    finally { $snupkg.Dispose() }

    Invoke-GateStep 'package-content negative regressions' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-package-content-gate.ps1') -PackagePath $nupkgs[0].FullName -ArtifactDirectory $feed
    }

    Invoke-GateStep 'sensitive/local pack-input regressions' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-sensitive-pack-inputs.ps1') -ProjectFile $cliProject
    }

    $auditTimer = [Diagnostics.Stopwatch]::StartNew()
    $auditOutput = @(& dotnet list $cliProject package --vulnerable --include-transitive --configfile (Join-Path $root 'NuGet.config') --format json 2>&1)
    $auditExitCode = $LASTEXITCODE
    $auditTimer.Stop()
    Write-Output 'STEP: dependency vulnerability audit'
    Write-Output "COMMAND_EXIT_CODE: $auditExitCode"
    Write-Output "DURATION_MS: $($auditTimer.ElapsedMilliseconds)"
    $auditText = $auditOutput -join [Environment]::NewLine
    if ($auditOutput.Count -gt 0) { $auditOutput | ForEach-Object { Write-Output ([string]$_) } }
    if ($auditExitCode -ne 0) { throw "Vulnerability audit failed with exit code $auditExitCode." }
    $auditFile = Join-Path $scratch 'vulnerability-report.json'
    [IO.File]::WriteAllText($auditFile, $auditText, [Text.UTF8Encoding]::new($false))
    Invoke-GateStep 'vulnerability finding policy' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/assert-no-vulnerabilities.ps1') -InputPath $auditFile
    }

    $toolConfig = Join-Path $scratch 'tool.config'
    $toolRoot = Join-Path $scratch 'tool'
    $toolXml = "<configuration><packageSources><clear /><add key='local' value='$feed' /><add key='nuget.org' value='https://api.nuget.org/v3/index.json' /></packageSources><packageSourceMapping><packageSource key='local'><package pattern='KeelMatrix.PackageSurface' /></packageSource><packageSource key='nuget.org'><package pattern='KeelMatrix.Telemetry' /></packageSource></packageSourceMapping></configuration>"
    [IO.File]::WriteAllText($toolConfig, $toolXml, [Text.UTF8Encoding]::new($false))
    New-Item -ItemType Directory -Force -Path $toolRoot | Out-Null
    Invoke-GateStep 'isolated tool install' {
        & dotnet tool install --tool-path $toolRoot --configfile $toolConfig --no-cache KeelMatrix.PackageSurface --version 0.1.0
    }
    $toolName = if ($IsWindows) { 'package-surface.exe' } else { 'package-surface' }
    $tool = Join-Path $toolRoot $toolName
    if (-not (Test-Path -LiteralPath $tool -PathType Leaf)) { throw 'The isolated tool command was not installed.' }
    Ensure-DotnetRootForInstalledTool
    Invoke-GateStep 'installed tool version' { & $tool --version }
    Invoke-GateStep 'installed tool scan' { & $tool scan $singleProject --format json --no-telemetry }
    Invoke-GateStep 'package XML primitive inspection' {
        $inspectionOutput = @(& $tool scan $singleProject --format json --no-telemetry)
        if ($LASTEXITCODE -ne 0) { throw 'Installed package XML inspection scan failed.' }
        $inspection = ($inspectionOutput -join [Environment]::NewLine) | ConvertFrom-Json
        $observed = @($inspection.entries | ForEach-Object { $_.observedPrimitives } | Where-Object { $_ } | Sort-Object -Unique)
        $expected = @('Exec', 'Import', 'InlineTaskFactory', 'UsingTask')
        if (@(Compare-Object ($expected | Sort-Object) $observed).Count -ne 0) { throw ('Package XML primitive observations were not exact: ' + ($observed -join ', ')) }
    }

    $baselineA = Join-Path $scratch 'baseline-a.json'
    $baselineB = Join-Path $scratch 'baseline-b.json'
    Invoke-GateStep 'installed tool baseline' { & $tool baseline $singleProject --output $baselineA --strict-content --format json --no-telemetry }
    Invoke-GateStep 'deterministic baseline repeat' { & $tool baseline $singleProject --output $baselineB --strict-content --format json --no-telemetry }
    if ((Get-FileHash -LiteralPath $baselineA -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $baselineB -Algorithm SHA256).Hash) { throw 'Repeated baseline creation was not byte-identical.' }
    if ([IO.File]::ReadAllText($baselineA, [Text.UTF8Encoding]::new($false)).Contains("`r`n", [StringComparison]::Ordinal)) { throw 'Baseline output used a non-canonical CRLF newline.' }
    Invoke-GateStep 'installed tool passing check' { & $tool check $singleProject --baseline $baselineA --strict-content --format json --no-telemetry }
    $nonStrictBaseline = Join-Path $scratch 'baseline-nonstrict.json'
    Invoke-GateStep 'installed tool non-strict baseline' { & $tool baseline $singleProject --output $nonStrictBaseline --format json --no-telemetry }
    Invoke-GateStep 'strict flag against non-strict baseline' { & $tool check $singleProject --baseline $nonStrictBaseline --strict-content --format text --no-telemetry } 2

    $baselineCases = @{
        'malformed baseline' = '{not-json'
        'null-entry baseline' = '{"schemaVersion":1,"toolVersion":"0.1.0","strictContent":false,"entries":[null],"incompleteReasons":[]}'
        'invalid-hash baseline' = '{"schemaVersion":1,"toolVersion":"0.1.0","strictContent":true,"entries":[{"project":"SingleTarget.csproj","context":"Target","targetFramework":"net8.0","runtimeIdentifier":null,"packageId":"Example","version":"1.0.0","relationship":"direct","capability":"BuildProps","packageRelativePath":"build/example.props","present":true,"active":true,"sha256":"bad","incomplete":false,"incompleteReason":null}],"incompleteReasons":[]}'
        'incomplete baseline' = '{"schemaVersion":1,"toolVersion":"0.1.0","strictContent":false,"entries":[],"incompleteReasons":["restore evidence missing"]}'
    }
    foreach ($case in $baselineCases.GetEnumerator()) {
        $casePath = Join-Path $scratch (($case.Key -replace '[^A-Za-z0-9]+', '-') + '.json')
        [IO.File]::WriteAllText($casePath, $case.Value, [Text.UTF8Encoding]::new($false))
        Invoke-GateStep $case.Key { & $tool check $singleProject --baseline $casePath --format text --no-telemetry } 2
    }
    $generatedProps = @(Get-ChildItem -LiteralPath (Join-Path $singleProject 'obj') -Filter '*.nuget.g.props' -File | Select-Object -First 1)
    if ($generatedProps.Count -ne 1) { throw 'The restored consumer did not produce a generated NuGet props file for the missing-import regression.' }
    $generatedPropsBackup = Join-Path $scratch 'generated-props.backup'
    Copy-Item -LiteralPath $generatedProps[0].FullName -Destination $generatedPropsBackup -Force
    try {
        Remove-Item -LiteralPath $generatedProps[0].FullName -Force
        Invoke-GateStep 'deleted required generated imports' { & $tool check $singleProject --baseline $baselineA --format text --no-telemetry } 2
    }
    finally { Copy-Item -LiteralPath $generatedPropsBackup -Destination $generatedProps[0].FullName -Force }
    $wrongGeneratedProps = Join-Path (Split-Path -Parent $generatedProps[0].FullName) 'Other.csproj.nuget.g.props'
    if (Test-Path -LiteralPath $wrongGeneratedProps) { throw 'The wrong-name generated NuGet props test target already exists.' }
    try {
        Move-Item -LiteralPath $generatedProps[0].FullName -Destination $wrongGeneratedProps
        [IO.File]::WriteAllText($wrongGeneratedProps, '<Project />', [Text.UTF8Encoding]::new($false))
        Invoke-GateStep 'wrong generated import filename' { & $tool check $singleProject --baseline $baselineA --format text --no-telemetry } 2
    }
    finally {
        if (Test-Path -LiteralPath $wrongGeneratedProps) { Remove-Item -LiteralPath $wrongGeneratedProps -Force }
        Copy-Item -LiteralPath $generatedPropsBackup -Destination $generatedProps[0].FullName -Force
    }
    $oversizedBaseline = Join-Path $scratch 'oversized-baseline.json'
    [IO.File]::WriteAllText($oversizedBaseline, ('x' * (16 * 1024 * 1024 + 1)), [Text.UTF8Encoding]::new($false))
    Invoke-GateStep 'oversized baseline' { & $tool check $singleProject --baseline $oversizedBaseline --format text --no-telemetry } 2

    $assetsDocument = Get-Content -LiteralPath $singleAssets -Raw | ConvertFrom-Json
    $packageFolder = @($assetsDocument.packageFolders.psobject.Properties.Name | Select-Object -First 1)
    $buildPropsLibrary = $assetsDocument.libraries.psobject.Properties['KeelMatrix.Phase0.BuildProps/1.0.0']
    if ($null -eq $buildPropsLibrary) { throw 'The strict-content regression package is missing from the restored graph.' }
    $inactiveAsset = Join-Path (Join-Path $packageFolder $buildPropsLibrary.Value.path) 'build/net9.0/KeelMatrix.Phase0.BuildProps.props'
    if (-not (Test-Path -LiteralPath $inactiveAsset -PathType Leaf)) { throw "The strict-content regression asset is missing: $inactiveAsset" }
    $inactiveAssetBackup = Join-Path $scratch 'inactive-build-props.backup'
    Copy-Item -LiteralPath $inactiveAsset -Destination $inactiveAssetBackup -Force
    try {
        [IO.File]::AppendAllText($inactiveAsset, '<!-- strict-inactive-repro -->' + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
        Invoke-GateStep 'strict inactive content remains informational' { & $tool check $singleProject --baseline $baselineA --format text --no-telemetry }
    }
    finally { Copy-Item -LiteralPath $inactiveAssetBackup -Destination $inactiveAsset -Force }

    $toolLibrary = $assetsDocument.libraries.psobject.Properties['KeelMatrix.Phase0.ToolScript/1.0.0']
    $toolAsset = Join-Path (Join-Path $packageFolder $toolLibrary.Value.path) 'tools/phase0-tool.ps1'
    $toolBackup = Join-Path $scratch 'phase0-tool.ps1.backup'
    Copy-Item -LiteralPath $toolAsset -Destination $toolBackup -Force
    try {
        [IO.File]::WriteAllBytes($toolAsset, [byte[]]::new(16 * 1024 * 1024 + 1))
        Invoke-GateStep 'strict retry ordinary scan remains complete' { & $tool scan $singleProject --format json --no-telemetry }
        Invoke-GateStep 'strict retry informational tool content remains complete' { & $tool check $singleProject --baseline $baselineA --format text --no-telemetry }
    }
    finally { Copy-Item -LiteralPath $toolBackup -Destination $toolAsset -Force }

    $changedBaseline = Join-Path $scratch 'baseline-changed.json'
    $changed = Get-Content -LiteralPath $baselineA -Raw | ConvertFrom-Json
    $firstActive = @($changed.entries | Where-Object active | Select-Object -First 1)
    if ($firstActive.Count -ne 1) { throw 'The fixture baseline did not contain an active entry for the deliberate-difference check.' }
    $changed.entries = @($changed.entries | Where-Object { $_.packageRelativePath -ne $firstActive[0].packageRelativePath -or $_.packageId -ne $firstActive[0].packageId })
    [IO.File]::WriteAllText($changedBaseline, ($changed | ConvertTo-Json -Depth 20) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Invoke-GateStep 'deliberate capability difference' { & $tool check $singleProject --baseline $changedBaseline --strict-content --format text --no-telemetry } 1

    $dependencyProject = Join-Path $root 'fixtures/consumer/RidTarget'
    $dependencyProjectFile = Join-Path $dependencyProject 'RidTarget.csproj'
    $dependencyBaseline = Join-Path $scratch 'dependency-baseline.json'
    Invoke-GateStep 'installed dependency original baseline' { & $tool baseline $dependencyProject --output $dependencyBaseline --strict-content --format json --no-telemetry }
    $projectBackup = Join-Path $scratch 'RidTarget.csproj.backup'
    Copy-Item -LiteralPath $dependencyProjectFile -Destination $projectBackup -Force
    try {
        $projectText = Get-Content -LiteralPath $dependencyProjectFile -Raw
        $dependencyLine = '    <PackageReference Include="KeelMatrix.Phase0.BuildBoth" Version="1.0.0" />' + [Environment]::NewLine + '  </ItemGroup>'
        $projectText = $projectText -replace '  </ItemGroup>', $dependencyLine
        [IO.File]::WriteAllText($dependencyProjectFile, $projectText, [Text.UTF8Encoding]::new($false))
        Invoke-GateStep 'installed consumer dependency restore' { & dotnet restore $dependencyProjectFile --configfile $nugetConfig --packages $shippingPackages --force-evaluate }
        Invoke-GateStep 'installed dependency capability difference' { & $tool check $dependencyProject --baseline $dependencyBaseline --format text --no-telemetry } 1
    }
    finally {
        Copy-Item -LiteralPath $projectBackup -Destination $dependencyProjectFile -Force
        & dotnet restore $dependencyProjectFile --configfile $nugetConfig --packages $shippingPackages --force-evaluate | Out-Null
    }

    $incompleteRoot = Join-Path $scratch 'incomplete'
    New-Item -ItemType Directory -Force -Path $incompleteRoot | Out-Null
    Invoke-GateStep 'incomplete restore fail-closed' { & $tool check $incompleteRoot --baseline $baselineA --format text --no-telemetry } 2

    $env:HTTP_PROXY = 'http://127.0.0.1:1'
    $env:HTTPS_PROXY = 'http://127.0.0.1:1'
    Invoke-GateStep 'no-network scan with disabled telemetry' { & $tool scan $singleProject --format json --no-telemetry }
    Remove-Item Env:HTTP_PROXY -ErrorAction SilentlyContinue
    Remove-Item Env:HTTPS_PROXY -ErrorAction SilentlyContinue

    $commandLineSource = Get-Content -LiteralPath (Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs') -Raw
    $telemetryCalls = [regex]::Matches($commandLineSource, '\.Track(?:Activation|Heartbeat)\s*\((?<arguments>[^)]*)\)')
    if ($telemetryCalls.Count -ne 1 -or $telemetryCalls[0].Groups['arguments'].Value.Trim().Length -ne 0) {
        throw 'Telemetry call path must contain exactly one argument-free activation call.'
    }
    $privacyText = Get-Content -LiteralPath (Join-Path $root 'PRIVACY.md') -Raw
    foreach ($requiredText in @(
        'https://github.com/KeelMatrix/Telemetry/blob/main/PRIVACY.md',
        'analyzed dependency package IDs or versions',
        'content hashes',
        'anonymous `project_hash` and `installation_hash`')) {
        if (-not $privacyText.Contains($requiredText, [StringComparison]::Ordinal)) {
            throw "Product privacy contract is missing '$requiredText'."
        }
    }
    Write-Output 'TELEMETRY_PRIVACY=PASS PackageSurface passes no analyzed dependency identity or content to the shared client; the shared policy documents its anonymous hash fields. Local gate uses --no-telemetry.'

    Invoke-GateStep 'working tree cleanliness' {
        $status = @(git status --porcelain --untracked-files=all)
        if ($LASTEXITCODE -ne 0) {
            throw "Unable to verify working tree cleanliness (git exit code $LASTEXITCODE)."
        }
        if ($status.Count -ne 0) {
            throw "Working tree is not clean after the gate:`n$($status -join "`n")"
        }
        Write-Output 'WORKTREE_STATUS=CLEAN'
    }

    $timer.Stop()
    Write-Output "LOCAL_GATE=PASS"
    Write-Output "LOCAL_GATE_DURATION_MS=$($timer.ElapsedMilliseconds)"
    exit 0
}
catch {
    $timer.Stop()
    Write-Error $_
    Write-Output 'LOCAL_GATE=FAIL'
    Write-Output "LOCAL_GATE_DURATION_MS=$($timer.ElapsedMilliseconds)"
    exit 1
}
