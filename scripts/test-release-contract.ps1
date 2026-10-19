$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $root 'scripts/validate-release.ps1'
$projectFile = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
$realChangelog = Join-Path $root 'CHANGELOG.md'
$workflowDirectory = Join-Path $root '.github/workflows'
$ciWorkflowPath = Join-Path $workflowDirectory 'ci.yml'
$releaseWorkflowPath = Join-Path $workflowDirectory 'release.yml'
$localGatePath = Join-Path $root 'scripts/run-local-gate.ps1'
$builtCliPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
$artifactSetValidatorPath = Join-Path $root 'scripts/validate-release-artifact-set.ps1'
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-release-contract-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function Invoke-ReleaseValidation {
    param(
        [Parameter(Mandatory)] [string] $Version,
        [Parameter(Mandatory)] [string] $ChangelogPath,
        [switch] $RequireFinalized,
        [switch] $FirstRelease
    )

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $validator,
        '-Version',
        $Version,
        '-ProjectFile',
        $projectFile,
        '-ChangelogPath',
        $ChangelogPath
    )
    if ($RequireFinalized) { $arguments += '-RequireFinalized' }
    if ($FirstRelease) { $arguments += '-FirstRelease' }
    $null = Invoke-NestedPwsh -ArgumentList $arguments 2>$null
    return $LASTEXITCODE
}

function Assert-ValidationPass {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [int] $ExitCode)
    if ($ExitCode -ne 0) { throw "Release-contract case '$Name' failed with exit code $ExitCode." }
}

function Assert-ValidationRejects {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [int] $ExitCode)
    if ($ExitCode -eq 0) { throw "Release-contract case '$Name' was accepted." }
}

function Invoke-ArtifactSetValidation {
    param([AllowEmptyString()] [string] $Version, [Parameter(Mandatory)] [string] $Directory)
    $arguments = @('-NoLogo', '-NoProfile', '-File', $artifactSetValidatorPath, '-Version', $Version, '-ArtifactDirectory', $Directory)
    $null = Invoke-NestedPwsh -ArgumentList $arguments 2>$null
    return $LASTEXITCODE
}

function Invoke-LocalGateVersionValidation {
    param([Parameter(Mandatory)] [string] $Version)
    $directory = Join-Path $scratch ('invalid-local-gate-version-' + [Guid]::NewGuid().ToString('N'))
    $arguments = @('-NoLogo', '-NoProfile', '-File', $localGatePath, '-ArtifactDirectory', $directory, '-ExpectedVersion', $Version)
    $null = Invoke-NestedPwsh -ArgumentList $arguments 2>$null
    return $LASTEXITCODE
}

function New-ArtifactSetDirectory {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string[]] $Files)
    $directory = Join-Path $scratch $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    foreach ($file in $Files) { [IO.File]::WriteAllText((Join-Path $directory $file), '') }
    return $directory
}

. (Join-Path $PSScriptRoot 'ReleaseWorkflowSourceGuard.ps1')

function Add-WorkflowFixtureCheckout {
    param(
        [System.Collections.Generic.List[string]] $Lines,
        [int] $SequenceIndent,
        [int] $FieldIndent,
        [string] $CheckoutUses,
        [string] $CheckoutRepository,
        [switch] $IncludeCheckoutRepository,
        [string] $CheckoutPath,
        [switch] $IncludeCheckoutPath,
        [string] $CheckoutRef,
        [switch] $ConditionalCheckout
    )

    $Lines.Add((' ' * $SequenceIndent) + '- name: Check out source')
    $Lines.Add((' ' * $FieldIndent) + 'uses: ' + $CheckoutUses)
    if ($ConditionalCheckout) { $Lines.Add((' ' * $FieldIndent) + 'if: ${{ always() }}') }
    $Lines.Add((' ' * $FieldIndent) + 'with:')
    if ($IncludeCheckoutRepository) { $Lines.Add((' ' * ($FieldIndent + 2)) + 'repository: ' + $CheckoutRepository) }
    if ($IncludeCheckoutPath) { $Lines.Add((' ' * ($FieldIndent + 2)) + 'path: ' + $CheckoutPath) }
    $Lines.Add((' ' * ($FieldIndent + 2)) + 'ref: ' + $CheckoutRef)
}

function Add-WorkflowFixtureDownload {
    param([System.Collections.Generic.List[string]] $Lines, [int] $SequenceIndent, [int] $FieldIndent)

    $Lines.Add((' ' * $SequenceIndent) + '- name: Download packages')
    $Lines.Add((' ' * $FieldIndent) + "uses: 'actions/download-artifact@v4'")
}

function Add-WorkflowFixtureScripts {
    param([System.Collections.Generic.List[string]] $Lines, [int] $SequenceIndent, [int] $FieldIndent, [string[]] $ScriptPaths)

    $Lines.Add((' ' * $SequenceIndent) + '- name: Validate packages')
    $Lines.Add((' ' * $FieldIndent) + 'run: |')
    foreach ($currentScript in $ScriptPaths) {
        $Lines.Add((' ' * ($FieldIndent + 2)) + 'pwsh -NoLogo -NoProfile -File ./' + $currentScript)
    }
}

function New-WorkflowScriptConsumerFixture {
    param(
        [string] $ScriptPath,
        [string[]] $ScriptPaths = @(),
        [string] $JobName = 'future-consumer',
        [int] $JobIndent = 2,
        [string] $CheckoutUses = 'actions/checkout@v4',
        [string] $CheckoutRepository,
        [string] $CheckoutPath,
        [string] $CheckoutRef = '${{ github.sha }}',
        [switch] $ConditionalCheckout,
        [switch] $OmitCheckout,
        [ValidateSet('before-download', 'after-download', 'after-script')]
        [string] $CheckoutOrder = 'before-download'
    )

    if ($ScriptPaths.Count -eq 0) { $ScriptPaths = @($ScriptPath) }
    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('jobs:')
    $lines.Add((' ' * $JobIndent) + $JobName + ':')
    $lines.Add((' ' * ($JobIndent + 2)) + 'steps:')
    $sequenceIndent = $JobIndent + 4
    $fieldIndent = $sequenceIndent + 2
    $includeCheckoutRepository = $PSBoundParameters.ContainsKey('CheckoutRepository')
    $includeCheckoutPath = $PSBoundParameters.ContainsKey('CheckoutPath')
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'before-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'after-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -ScriptPaths $ScriptPaths
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'after-script') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    return ($lines -join [Environment]::NewLine)
}

function Assert-WorkflowScriptSourceRegressionCoverage {
    param(
        [Parameter(Mandatory)] [object[]] $CurrentConsumers,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    if ($CurrentConsumers.Count -eq 0) { throw 'Regression coverage requires at least one discovered artifact-consuming script job.' }
    $script = [string]$CurrentConsumers[0].Scripts[0]
    $backslash = [string][char]92
    $dotBackslash = '.' + $backslash
    $slashBackslash = './' + $backslash
    $dotBackslashDotSlash = $dotBackslash + './'

    $fourSpaceJob = New-WorkflowScriptConsumerFixture -ScriptPath $script -JobIndent 4
    $fourSpaceResult = @(Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'four-space.yml'; Text = $fourSpaceJob }) -RepositoryRoot $RepositoryRoot)
    if ($fourSpaceResult.Count -ne 1 -or $fourSpaceResult[0].Scripts -notcontains $script) {
        throw 'A valid four-space-indented job must be discovered and its tracked script validated.'
    }

    foreach ($rootPath in @('.', './', './.')) {
        $scalars = [Collections.Generic.List[string]]::new()
        $scalars.Add($rootPath)
        $scalars.Add("'" + $rootPath + "'")
        $scalars.Add('"' + $rootPath + '"')
        foreach ($scalar in $scalars) {
            $rootFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath $scalar
            $rootResult = @(Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'portable-root.yml'; Text = $rootFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('portable-root.yml/future-consumer'))
            if ($rootResult.Count -ne 1 -or $rootResult[0].Scripts -notcontains $script) {
                throw "Portable workspace-root checkout scalar '$scalar' was rejected."
            }
        }
    }

    $quotedUses = New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutUses "'actions/checkout@v4'" -CheckoutPath "'./.'"
    $quotedResult = @(Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'quoted-uses.yml'; Text = $quotedUses }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('quoted-uses.yml/future-consumer'))
    if ($quotedResult.Count -ne 1 -or $quotedResult[0].Scripts -notcontains $script) { throw 'Quoted action uses and a quoted portable workspace-root path must pass.' }

    $rejectedCases = @(
        @{ Name = 'dot plus backslash checkout path'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath ("'" + $dotBackslash + "'")) },
        @{ Name = 'mixed-separator checkout path'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath ("'" + $slashBackslash + "'")) },
        @{ Name = 'mixed-separator dot checkout path'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath ("'" + $dotBackslashDotSlash + "'")) },
        @{ Name = 'checkout into a subdirectory'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath 'source') },
        @{ Name = 'quoted checkout into a subdirectory'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath "'./source'") },
        @{ Name = 'checkout path that normalizes through a subdirectory'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutPath 'source/..') },
        @{ Name = 'conditional checkout'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -ConditionalCheckout) },
        @{ Name = 'checkout after artifact download'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutOrder 'after-download') },
        @{ Name = 'checkout after script invocation'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutOrder 'after-script') },
        @{ Name = 'checkout of a different revision'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutRef 'main') },
        @{ Name = 'checkout of a different repository'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutRepository 'other/repository') },
        @{ Name = 'script path with different casing'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script.ToUpperInvariant()) },
        @{ Name = 'missing checkout'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -OmitCheckout) },
        @{ Name = 'four-space job with missing checkout'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -JobIndent 4 -OmitCheckout) }
    )
    foreach ($case in $rejectedCases) {
        $rejected = $false
        try {
            $null = Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'regression.yml'; Text = $case.Text }) -RepositoryRoot $RepositoryRoot
        }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted a consumer with $($case.Name)." }
    }

    $unsupportedFlow = "jobs:`n  future-consumer:`n    steps: []`n"
    $unsupportedRejected = $false
    try { $null = Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'unsupported.yml'; Text = $unsupportedFlow }) -RepositoryRoot $RepositoryRoot }
    catch { $unsupportedRejected = $true }
    if (-not $unsupportedRejected) { throw 'Workflow guard must fail closed on unsupported flow-style steps.' }
    $unsupportedAlias = "jobs:`n  future-consumer:`n    steps:`n      - *shared-step`n"
    $unsupportedRejected = $false
    try { $null = Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'unsupported-alias.yml'; Text = $unsupportedAlias }) -RepositoryRoot $RepositoryRoot }
    catch { $unsupportedRejected = $true }
    if (-not $unsupportedRejected) { throw 'Workflow guard must fail closed on unsupported step aliases.' }

    $multiJobLines = [Collections.Generic.List[string]]::new()
    $multiJobLines.Add('jobs:')
    foreach ($consumer in $CurrentConsumers) {
        $fixture = New-WorkflowScriptConsumerFixture -ScriptPaths $consumer.Scripts -JobName $consumer.Name
        $fixtureLines = @($fixture -split '\r?\n')
        $multiJobLines.AddRange([string[]]$fixtureLines[1..($fixtureLines.Count - 1)])
    }
    $requiredConsumerNames = @($CurrentConsumers | ForEach-Object { "$($_.Workflow)/$($_.Name)" })
    $enumerated = @(Assert-ArtifactScriptConsumersHaveSource -Workflows @(@{ Name = 'release.yml'; Text = ($multiJobLines -join [Environment]::NewLine) }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames $requiredConsumerNames)
    if ($enumerated.Count -ne $CurrentConsumers.Count) { throw 'Regression coverage did not enumerate every current script-consuming job.' }
    foreach ($consumer in $CurrentConsumers) {
        $actual = @($enumerated | Where-Object { $_.Name -eq $consumer.Name -and $_.Workflow -eq $consumer.Workflow })
        if ($actual.Count -ne 1) { throw "Regression coverage omitted '$($consumer.Workflow)/$($consumer.Name)'." }
        foreach ($currentScript in $consumer.Scripts) {
            if ($actual[0].Scripts -notcontains $currentScript) { throw "Regression coverage omitted '$currentScript' from '$($consumer.Workflow)/$($consumer.Name)'." }
        }
    }
}
try {
    $projectText = Get-Content -LiteralPath $projectFile -Raw
    $versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $versionMatch.Success) { throw 'The real packable project has no explicit package version.' }
    $version = $versionMatch.Groups['version'].Value.Trim()

    if (-not (Test-Path -LiteralPath $ciWorkflowPath -PathType Leaf)) { throw 'CI workflow is missing.' }
    if (-not (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf)) { throw 'Release workflow is missing.' }
    $ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw
    $releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
    $workflowInputs = @(Get-ChildItem -LiteralPath $workflowDirectory -File | Where-Object { $_.Extension -in @('.yml', '.yaml') } | Sort-Object Name | ForEach-Object {
        @{ Name = $_.Name; Text = Get-Content -LiteralPath $_.FullName -Raw }
    })
    $artifactScriptConsumers = @(Assert-ArtifactScriptConsumersHaveSource -Workflows $workflowInputs -RepositoryRoot $root -RequiredConsumerNames @('release.yml/publish', 'release.yml/github-release'))
    foreach ($consumerName in @('release.yml/publish', 'release.yml/github-release')) {
        $consumer = @($artifactScriptConsumers | Where-Object { "$($_.Workflow)/$($_.Name)" -eq $consumerName })[0]
        if ($consumer.Scripts -notcontains 'scripts/validate-release-artifact-set.ps1') {
            throw "Artifact consumer '$consumerName' does not enumerate the release artifact validator script."
        }
    }
    Assert-WorkflowScriptSourceRegressionCoverage -CurrentConsumers $artifactScriptConsumers -RepositoryRoot $root

    foreach ($required in @(
        'push:',
        'branches:',
        '- main',
        'pull_request:',
        'workflow_dispatch:',
        'commit_sha:',
        'required: false',
        'type: string',
        'inputs.commit_sha || github.sha',
        'fetch-depth: 0',
        'persist-credentials: false',
        'global-json-file: global.json',
        'dotnet restore ./KeelMatrix.PackageSurface.sln --configfile ./NuGet.config --force',
        'pwsh -NoLogo -NoProfile -File ./scripts/run-local-gate.ps1',
        'windows-latest',
        'ubuntu-latest',
        'macos-latest',
        'fail-fast: false',
        'KEELMATRIX_TELEMETRY: ''off''',
        'permissions:',
        'contents: read',
        'cancel-in-progress: true',
        'timeout-minutes: 45',
        'commit_sha must be a full 40-character commit SHA.',
        'git rev-parse HEAD'
    )) {
        if (-not $ciWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "CI workflow is missing '$required'." }
    }

    if ($releaseWorkflow -notmatch "(?ms)^on:\s*\r?\n\s+push:\s*\r?\n\s+tags:\s*\r?\n\s+- 'v\*\.\*\.\*'") {
        throw 'Release workflow must be tag-triggered only for v*.*.*.'
    }
    foreach ($required in @(
        'global-json-file: global.json',
        'timeout-minutes: 45',
        'actions/download-artifact@v4',
        'KEELMATRIX_TELEMETRY: ''off''',
        './scripts/validate-release.ps1 -Version $version -RequireFinalized -FirstRelease',
        './scripts/run-local-gate.ps1 -ArtifactDirectory artifacts/release -ExpectedVersion $env:RELEASE_VERSION',
        './scripts/validate-release-artifact-set.ps1 -Version $env:RELEASE_VERSION -ArtifactDirectory artifacts/release',
        'RELEASE_VERSION: ${{ steps.release_contract.outputs.version }}',
        'RELEASE_VERSION: ${{ needs.validate.outputs.version }}',
        '[IO.File]::AppendAllText($env:GITHUB_OUTPUT,',
        'path: artifacts/release/*',
        'NuGet/login@v1',
        'user: dmitriyzen',
        'needs: validate',
        'needs: [validate, publish]',
        'dotnet nuget push',
        'gh release create',
        '--verify-tag',
        '--repo "$RELEASE_REPOSITORY"'
    )) {
        if (-not $releaseWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "Release workflow is missing '$required'." }
    }
    if ($releaseWorkflow.Contains("PACKAGE_VERSION:", [StringComparison]::Ordinal)) { throw 'Release workflow maintains a second hard-coded package-version definition.' }
    if ($releaseWorkflow.Contains('run-phase0.ps1', [StringComparison]::Ordinal)) { throw 'Release workflow runs a mutating standalone Phase 0 step.' }
    if ($releaseWorkflow -match '(?m)dotnet\s+run\s+--project\s+tests/') { throw 'Release workflow duplicates console leaf tests beside the canonical gate.' }
    if ($releaseWorkflow -match '(?m)^\s*run:\s*dotnet\s+pack\b') { throw 'Release workflow repacks a separately validated artifact.' }
    if ($releaseWorkflow -notmatch '(?s)outputs:\s*\r?\n\s+version:\s*\$\{\{\s*steps\.release_contract\.outputs\.version\s*\}\}') { throw 'Release workflow does not expose the validated tag version as a job output.' }
    if ($releaseWorkflow -match '\$version\s*=\s*["'']\$\{\{\s*(?:steps\.release_contract|needs\.validate)\.outputs\.version') { throw 'Release workflow interpolates a possibly empty version directly into script text.' }
    if ($releaseWorkflow -notmatch '(?s)publish:.*needs:\s*validate.*NuGet/login@v1') { throw 'Publish does not depend on validation and Trusted Publishing.' }
    if ($releaseWorkflow -notmatch '(?s)github-release:.*needs:\s*\[validate, publish\]') { throw 'GitHub Release is not ordered after validation and publication.' }

    $publishStart = $releaseWorkflow.IndexOf("`n  publish:", [StringComparison]::Ordinal)
    $publishValidation = $releaseWorkflow.IndexOf('Revalidate downloaded package set before OIDC', $publishStart, [StringComparison]::Ordinal)
    $publishLogin = $releaseWorkflow.IndexOf('uses: NuGet/login@v1', $publishStart, [StringComparison]::Ordinal)
    if ($publishStart -lt 0 -or $publishValidation -lt $publishStart -or $publishLogin -lt $publishValidation) { throw 'Publish must revalidate exact downloaded artifacts before requesting OIDC.' }

    $releaseStart = $releaseWorkflow.IndexOf("`n  github-release:", [StringComparison]::Ordinal)
    $releaseValidation = $releaseWorkflow.IndexOf('Revalidate downloaded release artifact set', $releaseStart, [StringComparison]::Ordinal)
    $createRelease = $releaseWorkflow.IndexOf('gh release create', $releaseStart, [StringComparison]::Ordinal)
    if ($releaseStart -lt 0 -or $releaseValidation -lt $releaseStart -or $createRelease -lt $releaseValidation) { throw 'GitHub Release must revalidate exact downloaded artifacts before creating a release.' }
    if ($releaseWorkflow.IndexOf('validate-release-artifact-set.ps1', [StringComparison]::Ordinal) -gt $releaseWorkflow.IndexOf('uses: actions/upload-artifact@v4', [StringComparison]::Ordinal)) { throw 'Release artifacts must be validated before upload.' }

    $localGate = Get-Content -LiteralPath $localGatePath -Raw
    foreach ($requiredGate in @('scripts/test-release-contract.ps1', 'scripts/test-vulnerability-audit.ps1', 'validate-package-artifact.ps1', 'dotnet pack')) {
        if (-not $localGate.Contains($requiredGate, [StringComparison]::Ordinal)) { throw "Canonical local gate is missing '$requiredGate'." }
    }
    if ($localGate -notmatch '(?s)finally\s*\{.*\$scratch') { throw 'Canonical local gate does not clean run-owned scratch state in finally.' }
    if ($localGate -notmatch 'ReadAllBytes|Read-ZipEntryBytes') { throw 'Canonical local gate does not validate package bytes.' }
    if ($localGate.Contains("`$packageVersion = '0.1.0'", [StringComparison]::Ordinal)) { throw 'Canonical local gate hard-codes a package version.' }
    if ($localGate -notmatch '\$PSBoundParameters\.ContainsKey\(''ExpectedVersion''\)') { throw 'Canonical local gate does not distinguish an absent version override from an empty one.' }
    if ($localGate -notmatch 'dotnet tool install[^\r\n]*--version \$packageVersion') { throw 'Installed-tool smoke does not use the validated package version.' }
    if ($localGate -match '"toolVersion":"0\.1\.0"') { throw 'Local-gate baseline regressions hard-code a package version.' }
    if ($localGate -notmatch 'if \(\[string\]::IsNullOrWhiteSpace\(\$suppliedPackageVersion\)\).*empty') { throw 'Canonical local gate does not reject an empty supplied version.' }

    $packageArtifactValidator = Get-Content -LiteralPath (Join-Path $root 'scripts/validate-package-artifact.ps1') -Raw
    if ($packageArtifactValidator -match '\$ExpectedVersion\s*=\s*["'']0\.1\.0') { throw 'Package validator hard-codes a release version.' }
    if ($packageArtifactValidator -notmatch '\[Parameter\(Mandatory\)\].*\$ExpectedVersion') { throw 'Package validator must require its expected release version.' }

    if (-not (Test-Path -LiteralPath $artifactSetValidatorPath -PathType Leaf)) { throw 'Release artifact-set validator is missing.' }
    $artifactNames = @("KeelMatrix.PackageSurface.$version.nupkg", "KeelMatrix.PackageSurface.$version.snupkg")
    $exactArtifactDirectory = New-ArtifactSetDirectory 'artifact-set-exact' $artifactNames
    Assert-ValidationPass 'exact release artifact set' (Invoke-ArtifactSetValidation -Version $version -Directory $exactArtifactDirectory)
    Assert-ValidationRejects 'empty release version' (Invoke-ArtifactSetValidation -Version '' -Directory $exactArtifactDirectory)
    Assert-ValidationRejects 'whitespace release version' (Invoke-ArtifactSetValidation -Version '   ' -Directory $exactArtifactDirectory)
    Assert-ValidationRejects 'padded release version' (Invoke-ArtifactSetValidation -Version " $version" -Directory $exactArtifactDirectory)
    Assert-ValidationRejects 'malformed release version' (Invoke-ArtifactSetValidation -Version 'latest' -Directory $exactArtifactDirectory)
    $mismatchedVersion = if ($version -eq '0.0.0') { '0.0.1' } else { '0.0.0' }
    Assert-ValidationRejects 'artifact version mismatch' (Invoke-ArtifactSetValidation -Version $mismatchedVersion -Directory $exactArtifactDirectory)
    Assert-ValidationRejects 'local gate package/project version mismatch' (Invoke-LocalGateVersionValidation -Version $mismatchedVersion)
    Assert-ValidationRejects 'local gate padded package version' (Invoke-LocalGateVersionValidation -Version " $version")

    $missingPackageDirectory = New-ArtifactSetDirectory 'artifact-set-missing-package' @($artifactNames[1])
    Assert-ValidationRejects 'missing package artifact' (Invoke-ArtifactSetValidation -Version $version -Directory $missingPackageDirectory)
    $missingSymbolsDirectory = New-ArtifactSetDirectory 'artifact-set-missing-symbols' @($artifactNames[0])
    Assert-ValidationRejects 'missing symbols artifact' (Invoke-ArtifactSetValidation -Version $version -Directory $missingSymbolsDirectory)
    $extraFileDirectory = New-ArtifactSetDirectory 'artifact-set-extra-file' ($artifactNames + 'unexpected.txt')
    Assert-ValidationRejects 'extra artifact file' (Invoke-ArtifactSetValidation -Version $version -Directory $extraFileDirectory)
    $extraDirectory = New-ArtifactSetDirectory 'artifact-set-extra-directory' $artifactNames
    [void][IO.Directory]::CreateDirectory((Join-Path $extraDirectory 'unexpected'))
    Assert-ValidationRejects 'extra artifact directory' (Invoke-ArtifactSetValidation -Version $version -Directory $extraDirectory)
    $wrongCaseDirectory = New-ArtifactSetDirectory 'artifact-set-wrong-case' @("keelmatrix.packagesurface.$version.nupkg", $artifactNames[1])
    Assert-ValidationRejects 'artifact filename case mismatch' (Invoke-ArtifactSetValidation -Version $version -Directory $wrongCaseDirectory)

    # The finalized repository changelog must pass both candidate and first-release tag validation.
    Assert-ValidationPass 'real finalized candidate' (Invoke-ReleaseValidation -Version $version -ChangelogPath $realChangelog)
    Assert-ValidationPass 'real finalized first-release tag gate' (Invoke-ReleaseValidation -Version $version -ChangelogPath $realChangelog -RequireFinalized -FirstRelease)

    $missingUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.missing-unreleased.md'
    $missingUnreleasedFirstRelease = "# Changelog`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($missingUnreleasedChangelog, $missingUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release missing Unreleased section' (Invoke-ReleaseValidation -Version $version -ChangelogPath $missingUnreleasedChangelog -RequireFinalized -FirstRelease)

    $alternateUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.alternate-unreleased.md'
    $alternateUnreleasedFirstRelease = "# Changelog`n`n## [Next]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($alternateUnreleasedChangelog, $alternateUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release alternate Unreleased heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $alternateUnreleasedChangelog -RequireFinalized -FirstRelease)

    $nestedReleaseChangelog = Join-Path $scratch 'CHANGELOG.nested-release.md'
    $nestedReleaseFirstRelease = "# Changelog`n`n## [Unreleased]`n`n### [$version] - 2026-09-22`n`n#### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($nestedReleaseChangelog, $nestedReleaseFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release entry nested under Unreleased' (Invoke-ReleaseValidation -Version $version -ChangelogPath $nestedReleaseChangelog -RequireFinalized -FirstRelease)

    $candidateChangelog = Join-Path $scratch 'CHANGELOG.finalized.md'
    $candidateFirstRelease = "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($candidateChangelog, $candidateFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'real project with finalized version-consistent first-release changelog' (Invoke-ReleaseValidation -Version $version -ChangelogPath $candidateChangelog -RequireFinalized -FirstRelease)

    # Compose the same documentation and release-contract checks used by the
    # publication gate against a concise finalized first-release entry. The
    # changelog is intentionally not copied into fixed-sentence parity: release
    # validation owns its semantic/version contract.
    if (-not (Test-Path -LiteralPath $builtCliPath -PathType Leaf)) { throw 'Built CLI is missing for the composed release-gate test.' }
    $helpPath = Join-Path $scratch 'built-help.txt'
    $help = @(& dotnet $builtCliPath --help 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Built CLI help failed with exit code $LASTEXITCODE." }
    [IO.File]::WriteAllText($helpPath, ($help -join [Environment]::NewLine), [Text.UTF8Encoding]::new($false))

    $composedRoot = Join-Path $scratch 'composed-release-gate'
    foreach ($relative in @(
        'README.md',
        'CHANGELOG.md',
        'SECURITY.md',
        'docs/DEV.md',
        'src/KeelMatrix.PackageSurface.Cli/README.md',
        'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs'
    )) {
        $destination = Join-Path $composedRoot $relative
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relative) -Destination $destination
    }
    Copy-Item -LiteralPath $candidateChangelog -Destination (Join-Path $composedRoot 'CHANGELOG.md') -Force
    $documentationOutput = @(Invoke-NestedPwsh -NoLogo -NoProfile -File (Join-Path $root 'scripts/test-cli-documentation.ps1') -RepositoryRoot $composedRoot -BuiltHelpTextPath $helpPath 2>&1)
    $documentationExitCode = $LASTEXITCODE
    if ($documentationExitCode -ne 0) {
        throw "Composed release documentation gate failed with exit code ${documentationExitCode}: $($documentationOutput -join [Environment]::NewLine)"
    }
    Assert-ValidationPass 'composed finalized first-release gate' (Invoke-ReleaseValidation -Version $version -ChangelogPath (Join-Path $composedRoot 'CHANGELOG.md') -RequireFinalized -FirstRelease)

    $workflowOutputPath = Join-Path $scratch 'github-output'
    [IO.File]::AppendAllText($workflowOutputPath, "version=$version`n", [Text.UTF8Encoding]::new($false))
    if ([IO.File]::ReadAllText($workflowOutputPath) -cne "version=$version`n") { throw 'Release-contract version was not serialized exactly for GitHub Actions output.' }

    $wholeWordChangelog = Join-Path $scratch 'CHANGELOG.whole-word.md'
    [IO.File]::WriteAllText($wholeWordChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Adds package prefixes and nowhere-only documentation examples.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release whole-word marker boundaries' (Invoke-ReleaseValidation -Version $version -ChangelogPath $wholeWordChangelog -RequireFinalized -FirstRelease)

    foreach ($marker in @('now', 'no longer', 'previously', 'formerly', 'used to', 'fixed', 'fixes', 'corrected', 'resolved', 'addressed', 'this removes', 'this fixes', 'changed from')) {
        $markerChangelog = Join-Path $scratch ('CHANGELOG.marker-' + ($marker -replace '[^A-Za-z0-9]+', '-') + '.md')
        [IO.File]::WriteAllText($markerChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- $marker release wording.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release remediation marker '$marker'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $markerChangelog -RequireFinalized -FirstRelease)
    }

    foreach ($category in @('Changed', 'Fixed', 'Deprecated', 'Removed', 'Security', 'Compatibility')) {
        $categoryChangelog = Join-Path $scratch ('CHANGELOG.category-' + $category + '.md')
        [IO.File]::WriteAllText($categoryChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n### $category`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release category '$category'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $categoryChangelog -RequireFinalized -FirstRelease)
    }
    foreach ($heading in @('Notes', 'Metadata')) {
        $headingChangelog = Join-Path $scratch ('CHANGELOG.heading-' + $heading + '.md')
        [IO.File]::WriteAllText($headingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n#### $heading`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release unknown heading '$heading'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $headingChangelog -RequireFinalized -FirstRelease)
    }
    $h1HeadingChangelog = Join-Path $scratch 'CHANGELOG.h1-heading.md'
    [IO.File]::WriteAllText($h1HeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n# Notes`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release unknown level-one heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1HeadingChangelog -RequireFinalized -FirstRelease)
    $h1AddedChangelog = Join-Path $scratch 'CHANGELOG.h1-added.md'
    [IO.File]::WriteAllText($h1AddedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n# Added`n`n- Initial release capability review.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release level-one Added heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1AddedChangelog -RequireFinalized -FirstRelease)

    $setextHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-heading.md'
    [IO.File]::WriteAllText($setextHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`nNotes`n-----`n`n- This heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext category heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextHeadingChangelog -RequireFinalized -FirstRelease)

    $setextDocumentHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-document-heading.md'
    [IO.File]::WriteAllText($setextDocumentHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`nNotes`n=====`n`n- This document heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext document heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextDocumentHeadingChangelog -RequireFinalized -FirstRelease)

    $fencedHeadingChangelog = Join-Path $scratch 'CHANGELOG.fenced-heading.md'
    [IO.File]::WriteAllText($fencedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n~~~markdown`n# Notes`n`nNotes`n-----`n~~~`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release fenced headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $fencedHeadingChangelog -RequireFinalized -FirstRelease)

    $indentedHeadingChangelog = Join-Path $scratch 'CHANGELOG.indented-heading.md'
    [IO.File]::WriteAllText($indentedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - 2026-09-22`n`n### Added`n`n- Initial release capability review.`n`n    # Notes`n    Notes`n    -----`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release indented code headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $indentedHeadingChangelog -RequireFinalized -FirstRelease)

    $mismatchedChangelog = Join-Path $scratch 'CHANGELOG.mismatched.md'
    [IO.File]::WriteAllText($mismatchedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [9.9.9] - 2026-09-22`n`n- Wrong version.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'changelog/version disagreement' (Invoke-ReleaseValidation -Version $version -ChangelogPath $mismatchedChangelog -RequireFinalized -FirstRelease)

    $consumerSummary = @($artifactScriptConsumers | ForEach-Object { "$($_.Workflow)/$($_.Name) [$($_.Scripts -join ', ')]" }) -join '; '
    Write-Output "PASS: artifact-consuming repository-script jobs are covered: $consumerSummary. Future jobs without same-commit source checkout are rejected."
    Write-Output 'PASS: one validated version reaches package and release artifact checks; empty, malformed, mismatched, missing, extra, and case-mismatched artifact sets are rejected.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
