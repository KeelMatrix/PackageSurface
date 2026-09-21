$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Set-Location -LiteralPath $root
$timer = [Diagnostics.Stopwatch]::StartNew()
$artifactRoot = Join-Path $root 'artifacts/gate'
New-Item -ItemType Directory -Force -Path $artifactRoot | Out-Null
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-gate-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

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

try {
    $env:KEELMATRIX_TELEMETRY = 'off'
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
    }
    Invoke-GateStep 'format verification' {
        & dotnet format (Join-Path $root 'KeelMatrix.PackageSurface.sln') --verify-no-changes --no-restore
    }
    Invoke-GateStep 'Release build' {
        & dotnet build (Join-Path $root 'KeelMatrix.PackageSurface.sln') --configuration Release --no-restore
    }
    Invoke-GateStep 'no-code-execution proof' {
        & pwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/verify-no-execution.ps1')
    }
    $cliTests = Join-Path $root 'tests/KeelMatrix.PackageSurface.Cli.Tests/KeelMatrix.PackageSurface.Cli.Tests.csproj'
    Invoke-GateStep 'CLI contract and resource tests' {
        & dotnet run --project $cliTests --configuration Release --no-build
    }

    $singleProject = Join-Path $root 'fixtures/consumer/SingleTarget'
    $singleAssets = Join-Path $singleProject 'obj/project.assets.json'
    $cliProject = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
    $feed = Join-Path $artifactRoot 'feed'
    New-Item -ItemType Directory -Force -Path $feed | Out-Null
    Invoke-GateStep 'package build' {
        & dotnet pack $cliProject --configuration Release --no-restore --output $feed
    }

    $nupkgs = @(Get-ChildItem -LiteralPath $feed -Filter 'KeelMatrix.PackageSurface.*.nupkg' -File)
    $snupkgs = @(Get-ChildItem -LiteralPath $feed -Filter 'KeelMatrix.PackageSurface.*.snupkg' -File)
    if ($nupkgs.Count -ne 1 -or $snupkgs.Count -ne 1) { throw 'The package build did not produce exactly one nupkg and one snupkg.' }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $package = [IO.Compression.ZipFile]::OpenRead($nupkgs[0].FullName)
    try {
        $names = @($package.Entries | ForEach-Object FullName | Sort-Object)
        Write-Output 'PACKAGE_FILE_SET:'
        $names | ForEach-Object { Write-Output $_ }
        $nuspecEntry = $package.GetEntry('KeelMatrix.PackageSurface.nuspec')
        if ($null -eq $nuspecEntry) { throw 'The package has no nuspec.' }
        $reader = [IO.StreamReader]::new($nuspecEntry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() } finally { $reader.Dispose() }
        if ($nuspec.package.metadata.id -ne 'KeelMatrix.PackageSurface' -or $nuspec.package.metadata.packageTypes.packageType.name -ne 'DotnetTool') { throw 'Package identity or type is incorrect.' }
        if ($nuspec.package.metadata.readme -ne 'README.md' -or $nuspec.package.metadata.license.type -ne 'expression' -or $nuspec.package.metadata.license.'#text' -ne 'MIT') { throw 'README or license metadata is incorrect.' }
        if ($nuspec.package.metadata.icon -ne 'icon.png') { throw 'Package icon metadata must point to icon.png.' }
        if ($nuspec.package.metadata.repository.url -ne 'https://github.com/KeelMatrix/PackageSurface') { throw 'Repository metadata is incorrect.' }
        $unwanted = @($names | Where-Object { $_ -match '(^|/)(fixtures|tests|research|\.env|obj|bin|\.phase0|nonshipping)' })
        if ($unwanted.Count -gt 0) { throw ('Unwanted package files: ' + ($unwanted -join ', ')) }
        $unexpected = @($names | Where-Object {
            $name = $_
            $name -notin @('[Content_Types].xml', 'KeelMatrix.PackageSurface.nuspec', 'README.md', 'LICENSE/LICENSE', 'icon.png') -and
            $name -notlike '_rels/*' -and
            $name -notlike 'package/*' -and
            $name -notlike 'tools/net8.0/any/*'
        })
        if ($unexpected.Count -gt 0) { throw ('Unexpected package files: ' + ($unexpected -join ', ')) }
        $icon = $package.GetEntry('icon.png')
        if ($null -eq $icon) {
            throw 'Package icon asset icon.png is missing.'
        }
    }
    finally { $package.Dispose() }

    $snupkg = [IO.Compression.ZipFile]::OpenRead($snupkgs[0].FullName)
    try {
        $symbolNames = @($snupkg.Entries | ForEach-Object FullName | Sort-Object)
        $symbolUnexpected = @($symbolNames | Where-Object {
            $name = $_
            $name -ne '[Content_Types].xml' -and
            $name -notlike '_rels/*' -and
            $name -notlike 'package/*' -and
            $name -notlike '*.nuspec' -and
            $name -notlike 'tools/net8.0/any/*.pdb'
        })
        if ($symbolUnexpected.Count -gt 0) { throw ('The symbols package contains unexpected payload: ' + ($symbolUnexpected -join ', ')) }
        if (@($symbolNames | Where-Object { $_ -like 'tools/net8.0/any/*.pdb' }).Count -eq 0) { throw 'The symbols package contains no PDB.' }
        Write-Output 'SYMBOLS_FILE_SET:'
        $symbolNames | ForEach-Object { Write-Output $_ }
    }
    finally { $snupkg.Dispose() }

    Invoke-GateStep 'dependency vulnerability audit' {
        & dotnet list $cliProject package --vulnerable --include-transitive --configfile (Join-Path $root 'NuGet.config')
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
    Invoke-GateStep 'installed tool version' { & $tool --version }
    Invoke-GateStep 'installed tool scan' { & $tool scan $singleProject --format json --no-telemetry }

    $baselineA = Join-Path $scratch 'baseline-a.json'
    $baselineB = Join-Path $scratch 'baseline-b.json'
    Invoke-GateStep 'installed tool baseline' { & $tool baseline $singleProject --output $baselineA --strict-content --format json --no-telemetry }
    Invoke-GateStep 'deterministic baseline repeat' { & $tool baseline $singleProject --output $baselineB --strict-content --format json --no-telemetry }
    if ((Get-FileHash -LiteralPath $baselineA -Algorithm SHA256).Hash -ne (Get-FileHash -LiteralPath $baselineB -Algorithm SHA256).Hash) { throw 'Repeated baseline creation was not byte-identical.' }
    Invoke-GateStep 'installed tool passing check' { & $tool check $singleProject --baseline $baselineA --strict-content --format json --no-telemetry }

    $changedBaseline = Join-Path $scratch 'baseline-changed.json'
    $changed = Get-Content -LiteralPath $baselineA -Raw | ConvertFrom-Json
    $firstActive = @($changed.entries | Where-Object active | Select-Object -First 1)
    if ($firstActive.Count -ne 1) { throw 'The fixture baseline did not contain an active entry for the deliberate-difference check.' }
    $changed.entries = @($changed.entries | Where-Object { $_.packageRelativePath -ne $firstActive[0].packageRelativePath -or $_.packageId -ne $firstActive[0].packageId })
    [IO.File]::WriteAllText($changedBaseline, ($changed | ConvertTo-Json -Depth 20) + [Environment]::NewLine, [Text.UTF8Encoding]::new($false))
    Invoke-GateStep 'deliberate capability difference' { & $tool check $singleProject --baseline $changedBaseline --strict-content --format text --no-telemetry } 1

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
