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

function Invoke-LocalGateVersionValidation {
    param([AllowEmptyString()] [string] $Version)

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $localGatePath,
        '-ExpectedVersion',
        $Version
    )
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

function New-ArtifactSetDirectory {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string[]] $Files)
    $directory = Join-Path $scratch $Name
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    foreach ($file in $Files) { [IO.File]::WriteAllText((Join-Path $directory $file), '') }
    return $directory
}

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
        [string] $CheckoutOrder = 'before-download',
        [ValidateSet('none', 'before-download', 'after-download', 'between-scripts')]
        [string] $SecondCheckoutOrder = 'none',
        [string] $SecondCheckoutUses = 'actions/checkout@v4',
        [string] $SecondCheckoutRepository,
        [string] $SecondCheckoutPath,
        [string] $SecondCheckoutRef = '${{ github.sha }}',
        [switch] $ConditionalSecondCheckout,
        [switch] $SecondArtifactDownload,
        [switch] $FinalValidCheckout
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
    $includeSecondCheckoutRepository = $PSBoundParameters.ContainsKey('SecondCheckoutRepository')
    $includeSecondCheckoutPath = $PSBoundParameters.ContainsKey('SecondCheckoutPath')
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'before-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    if ($SecondCheckoutOrder -eq 'before-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $SecondCheckoutUses -CheckoutRepository $SecondCheckoutRepository -IncludeCheckoutRepository:$includeSecondCheckoutRepository -CheckoutPath $SecondCheckoutPath -IncludeCheckoutPath:$includeSecondCheckoutPath -CheckoutRef $SecondCheckoutRef -ConditionalCheckout:$ConditionalSecondCheckout
    }
    Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'after-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    if ($SecondCheckoutOrder -eq 'after-download') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $SecondCheckoutUses -CheckoutRepository $SecondCheckoutRepository -IncludeCheckoutRepository:$includeSecondCheckoutRepository -CheckoutPath $SecondCheckoutPath -IncludeCheckoutPath:$includeSecondCheckoutPath -CheckoutRef $SecondCheckoutRef -ConditionalCheckout:$ConditionalSecondCheckout
    }
    if ($SecondArtifactDownload) { Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent }
    if ($FinalValidCheckout) {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses 'actions/checkout@v4' -CheckoutRepository '${{ github.repository }}' -IncludeCheckoutRepository -CheckoutPath '.' -IncludeCheckoutPath -CheckoutRef '${{ github.sha }}'
    }
    Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -ScriptPaths $ScriptPaths
    if ($SecondCheckoutOrder -eq 'between-scripts') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $SecondCheckoutUses -CheckoutRepository $SecondCheckoutRepository -IncludeCheckoutRepository:$includeSecondCheckoutRepository -CheckoutPath $SecondCheckoutPath -IncludeCheckoutPath:$includeSecondCheckoutPath -CheckoutRef $SecondCheckoutRef -ConditionalCheckout:$ConditionalSecondCheckout
        Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -ScriptPaths $ScriptPaths
    }
    if (-not $OmitCheckout -and $CheckoutOrder -eq 'after-script') {
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses $CheckoutUses -CheckoutRepository $CheckoutRepository -IncludeCheckoutRepository:$includeCheckoutRepository -CheckoutPath $CheckoutPath -IncludeCheckoutPath:$includeCheckoutPath -CheckoutRef $CheckoutRef -ConditionalCheckout:$ConditionalCheckout
    }
    return ($lines -join [Environment]::NewLine)
}

function Invoke-ReleaseWorkflowScriptSourceGuard {
    param(
        [Parameter(Mandatory)] [object[]] $Workflows,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [string[]] $RequiredConsumerNames = @()
    )

    . (Join-Path $PSScriptRoot 'ReleaseWorkflowSourceGuard.ps1')
    return Assert-ArtifactScriptConsumersHaveSource -Workflows $Workflows -RepositoryRoot $RepositoryRoot -RequiredConsumerNames $RequiredConsumerNames
}

function Assert-WorkflowScriptSourceRegressionCoverage {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $CurrentConsumers,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    $script = if ($CurrentConsumers.Count -gt 0) { [string]$CurrentConsumers[0].Scripts[0] } else { 'scripts/validate-release-artifact-set.ps1' }
    $backslash = [string][char]92
    $dotBackslash = '.' + $backslash
    $slashBackslash = './' + $backslash
    $dotBackslashDotSlash = $dotBackslash + './'

    $fourSpaceJob = New-WorkflowScriptConsumerFixture -ScriptPath $script -JobIndent 4
    $fourSpaceResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'four-space.yml'; Text = $fourSpaceJob }) -RepositoryRoot $RepositoryRoot)
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
            $rootResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'portable-root.yml'; Text = $rootFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('portable-root.yml/future-consumer'))
            if ($rootResult.Count -ne 1 -or $rootResult[0].Scripts -notcontains $script) {
                throw "Portable workspace-root checkout scalar '$scalar' was rejected."
            }
        }
    }

    $quotedUses = New-WorkflowScriptConsumerFixture -ScriptPath $script -CheckoutUses "'actions/checkout@v4'" -CheckoutPath "'./.'"
    $quotedResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'quoted-uses.yml'; Text = $quotedUses }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('quoted-uses.yml/future-consumer'))
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
        @{ Name = 'later checkout after artifact download replaces pinned revision'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRef 'main') },
        @{ Name = 'later checkout before artifact download replaces pinned revision'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'before-download' -SecondCheckoutRef 'main') },
        @{ Name = 'checkout state before a later artifact download remains required'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRef 'main' -SecondArtifactDownload -FinalValidCheckout) },
        @{ Name = 'later checkout after artifact download selects another repository'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRepository 'other/repository') },
        @{ Name = 'later checkout after artifact download uses a non-root path'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutPath 'source') },
        @{ Name = 'conditional later checkout may replace pinned revision'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRef 'main' -ConditionalSecondCheckout) },
        @{ Name = 'later checkout between guarded script invocations'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'between-scripts' -SecondCheckoutRef 'main') },
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
            $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'regression.yml'; Text = $case.Text }) -RepositoryRoot $RepositoryRoot
        }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted a consumer with $($case.Name)." }
    }

    $finalValidFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRef 'main' -FinalValidCheckout
    $finalValidResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'final-valid-source.yml'; Text = $finalValidFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('final-valid-source.yml/future-consumer'))
    if ($finalValidResult.Count -ne 1 -or $finalValidResult[0].Scripts -notcontains $script) {
        throw 'An unconditional final checkout pinned to github.sha must restore the effective source state before script invocation.'
    }

    $conditionalValidFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -ConditionalSecondCheckout
    $conditionalValidResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'conditional-valid-source.yml'; Text = $conditionalValidFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('conditional-valid-source.yml/future-consumer'))
    if ($conditionalValidResult.Count -ne 1 -or $conditionalValidResult[0].Scripts -notcontains $script) {
        throw 'A conditional checkout to the already-pinned repository, revision, and root path must preserve the effective source state.'
    }

    $unsupportedFlow = "jobs:`n  future-consumer:`n    steps: []`n"
    $unsupportedRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'unsupported.yml'; Text = $unsupportedFlow }) -RepositoryRoot $RepositoryRoot }
    catch { $unsupportedRejected = $true }
    if (-not $unsupportedRejected) { throw 'Workflow guard must fail closed on unsupported flow-style steps.' }
    $unsupportedAlias = "jobs:`n  future-consumer:`n    steps:`n      - *shared-step`n"
    $unsupportedRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'unsupported-alias.yml'; Text = $unsupportedAlias }) -RepositoryRoot $RepositoryRoot }
    catch { $unsupportedRejected = $true }
    if (-not $unsupportedRejected) { throw 'Workflow guard must fail closed on unsupported step aliases.' }

    if ($CurrentConsumers.Count -eq 0) { return }

    $multiJobLines = [Collections.Generic.List[string]]::new()
    $multiJobLines.Add('jobs:')
    foreach ($consumer in $CurrentConsumers) {
        $fixture = New-WorkflowScriptConsumerFixture -ScriptPaths $consumer.Scripts -JobName $consumer.Name
        $fixtureLines = @($fixture -split '\r?\n')
        $multiJobLines.AddRange([string[]]$fixtureLines[1..($fixtureLines.Count - 1)])
    }
    $requiredConsumerNames = @($CurrentConsumers | ForEach-Object { "$($_.Workflow)/$($_.Name)" })
    $enumerated = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'release.yml'; Text = ($multiJobLines -join [Environment]::NewLine) }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames $requiredConsumerNames)
    if ($enumerated.Count -ne $CurrentConsumers.Count) { throw 'Regression coverage did not enumerate every current script-consuming job.' }
    foreach ($consumer in $CurrentConsumers) {
        $actual = @($enumerated | Where-Object { $_.Name -eq $consumer.Name -and $_.Workflow -eq $consumer.Workflow })
        if ($actual.Count -ne 1) { throw "Regression coverage omitted '$($consumer.Workflow)/$($consumer.Name)'." }
        foreach ($currentScript in $consumer.Scripts) {
            if ($actual[0].Scripts -notcontains $currentScript) { throw "Regression coverage omitted '$currentScript' from '$($consumer.Workflow)/$($consumer.Name)'." }
        }
    }
}

function Get-WorkflowIndent([string] $Line) {
    return ([regex]::Match($Line, '^[ \t]*')).Length
}

function Get-WorkflowJobSections([string] $WorkflowText) {
    $lines = @($WorkflowText -split "`r?`n")
    $jobsLineIndex = -1
    $jobsIndent = -1
    for ($index = 0; $index -lt $lines.Count; $index++) {
        $match = [regex]::Match([string]$lines[$index], '^(?<indent>[ \t]*)jobs:\s*(?:#.*)?$')
        if ($match.Success) {
            $jobsLineIndex = $index
            $jobsIndent = $match.Groups['indent'].Value.Length
            break
        }
    }
    if ($jobsLineIndex -lt 0) { throw 'Workflow has no jobs mapping.' }

    $jobIndent = -1
    $jobName = $null
    $jobLines = [Collections.Generic.List[string]]::new()
    $jobs = [Collections.Generic.List[object]]::new()
    for ($index = $jobsLineIndex + 1; $index -lt $lines.Count; $index++) {
        $line = [string]$lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        $indent = Get-WorkflowIndent $line
        if ($indent -le $jobsIndent) { break }
        if ($jobIndent -lt 0) { $jobIndent = $indent }

        $jobMatch = [regex]::Match($line, '^[ \t]*(?<name>[A-Za-z0-9_-]+):\s*(?:#.*)?$')
        if ($indent -eq $jobIndent -and $jobMatch.Success) {
            if ($null -ne $jobName) {
                $jobs.Add([pscustomobject]@{ Name = $jobName; Indent = $jobIndent; Lines = $jobLines.ToArray() })
            }
            $jobName = $jobMatch.Groups['name'].Value
            $jobLines = [Collections.Generic.List[string]]::new()
            continue
        }

        if ($null -ne $jobName) { $jobLines.Add($line) }
    }
    if ($null -ne $jobName) {
        $jobs.Add([pscustomobject]@{ Name = $jobName; Indent = $jobIndent; Lines = $jobLines.ToArray() })
    }
    return $jobs.ToArray()
}

function Get-WorkflowSteps([string[]] $JobLines) {
    $stepsLineIndex = -1
    $stepsIndent = -1
    for ($index = 0; $index -lt $JobLines.Count; $index++) {
        $match = [regex]::Match([string]$JobLines[$index], '^(?<indent>[ \t]*)steps:\s*(?:#.*)?$')
        if ($match.Success) {
            $stepsLineIndex = $index
            $stepsIndent = $match.Groups['indent'].Value.Length
            break
        }
    }
    if ($stepsLineIndex -lt 0) { return @() }

    $stepIndent = -1
    $stepLines = [Collections.Generic.List[string]]::new()
    $steps = [Collections.Generic.List[object]]::new()
    for ($index = $stepsLineIndex + 1; $index -lt $JobLines.Count; $index++) {
        $line = [string]$JobLines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        $indent = Get-WorkflowIndent $line
        if ($indent -le $stepsIndent) { break }

        $listItem = [regex]::Match($line, '^[ \t]*-\s+')
        if ($stepIndent -lt 0 -and $listItem.Success) { $stepIndent = $indent }
        if ($listItem.Success -and $indent -eq $stepIndent) {
            if ($stepLines.Count -gt 0) { $steps.Add([pscustomobject]@{ Lines = $stepLines.ToArray() }) }
            $stepLines = [Collections.Generic.List[string]]::new()
        }
        if ($stepIndent -ge 0) { $stepLines.Add($line) }
    }
    if ($stepLines.Count -gt 0) { $steps.Add([pscustomobject]@{ Lines = $stepLines.ToArray() }) }
    return $steps.ToArray()
}

function Get-WorkflowRunCommands([string[]] $StepLines) {
    $commands = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $StepLines.Count; $index++) {
        $run = [regex]::Match([string]$StepLines[$index], '^[ \t]*(?:-\s*)?run:\s*(?<value>.*)$')
        if (-not $run.Success) { continue }

        $value = $run.Groups['value'].Value.Trim()
        if ($value -match '^(?:\||>)[+-]?(?:\s+#.*)?$') {
            $runIndent = Get-WorkflowIndent ([string]$StepLines[$index])
            for ($next = $index + 1; $next -lt $StepLines.Count; $next++) {
                $candidate = [string]$StepLines[$next]
                if (-not [string]::IsNullOrWhiteSpace($candidate) -and (Get-WorkflowIndent $candidate) -le $runIndent) { break }
                $commands.Add($candidate)
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($value)) {
            $commands.Add($value)
        }
    }
    return $commands.ToArray()
}

function Get-WorkflowDefaultWorkingDirectory([string[]] $ScopeLines, [int] $DefaultsIndent) {
    for ($index = 0; $index -lt $ScopeLines.Count; $index++) {
        $defaults = [regex]::Match([string]$ScopeLines[$index], '^(?<indent>[ \t]*)defaults:\s*(?:#.*)?$')
        if (-not $defaults.Success -or $defaults.Groups['indent'].Value.Length -ne $DefaultsIndent) { continue }
        $defaultsEnd = $ScopeLines.Count
        for ($end = $index + 1; $end -lt $ScopeLines.Count; $end++) {
            if (-not [string]::IsNullOrWhiteSpace([string]$ScopeLines[$end]) -and (Get-WorkflowIndent ([string]$ScopeLines[$end])) -le $DefaultsIndent) {
                $defaultsEnd = $end
                break
            }
        }
        $runIndent = -1
        for ($nested = $index + 1; $nested -lt $defaultsEnd; $nested++) {
            $run = [regex]::Match([string]$ScopeLines[$nested], '^(?<indent>[ \t]*)run:\s*(?:#.*)?$')
            if ($run.Success -and $run.Groups['indent'].Value.Length -eq ($DefaultsIndent + 2)) {
                $runIndent = $run.Groups['indent'].Value.Length
                continue
            }
            if ($runIndent -ge 0) {
                $directory = [regex]::Match([string]$ScopeLines[$nested], '^[ \t]*working-directory:\s*(?<value>[^#]+?)\s*(?:#.*)?$')
                if ($directory.Success -and (Get-WorkflowIndent ([string]$ScopeLines[$nested])) -eq ($runIndent + 2)) {
                    return $directory.Groups['value'].Value.Trim().Trim('"').Trim("'")
                }
            }
        }
    }
    return $null
}

function Resolve-WorkflowRelativePath([string] $BasePath, [string] $RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or $RelativePath -match '\$\{\{' -or $RelativePath -match '^(?:/|[A-Za-z]:[\\/])') { return $null }
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($component in @($BasePath, $RelativePath) -join '/' -split '/') {
        if ([string]::IsNullOrWhiteSpace($component) -or $component -eq '.') { continue }
        if ($component -eq '..') {
            if ($parts.Count -eq 0) { return $null }
            $parts.RemoveAt($parts.Count - 1)
            continue
        }
        $parts.Add($component)
    }
    return ($parts -join '/')
}

function Get-WorkflowArtifactScriptConsumers {
    param(
        [Parameter(Mandatory)] [string] $WorkflowText,
        [Parameter(Mandatory)] [string[]] $TrackedFiles
    )

    $allLines = @($WorkflowText -split "`r?`n")
    $jobs = @(Get-WorkflowJobSections -WorkflowText $WorkflowText)
    $jobsLineIndex = -1
    for ($index = 0; $index -lt $allLines.Count; $index++) {
        if ([string]$allLines[$index] -match '^[ \t]*jobs:\s*(?:#.*)?$') { $jobsLineIndex = $index; break }
    }
    $workflowScopeLines = if ($jobsLineIndex -gt 0) { @($allLines[0..($jobsLineIndex - 1)]) } else { @() }
    $workflowWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $workflowScopeLines -DefaultsIndent 0
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($file in $TrackedFiles) { [void]$tracked.Add(([string]$file).Replace('\', '/')) }
    $scriptPattern = '(?i)(?<path>(?:[A-Za-z0-9_.-]+[\\/])*scripts[\\/][A-Za-z0-9_.-]+(?:[\\/][A-Za-z0-9_.-]+)*\.(?:ps1|sh|cmd|bat))'

    $consumers = [Collections.Generic.List[object]]::new()
    foreach ($job in $jobs) {
        $steps = @(Get-WorkflowSteps -JobLines $job.Lines)
        $isArtifactConsumer = $false
        foreach ($step in $steps) {
            if ((@($step.Lines) -join "`n") -match '(?i)actions/download-artifact@') { $isArtifactConsumer = $true; break }
        }
        if (-not $isArtifactConsumer) { continue }

        $jobWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $job.Lines -DefaultsIndent ($job.Indent + 2)
        if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = $workflowWorkingDirectory }
        if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = '.' }
        $checkoutPaths = [Collections.Generic.List[string]]::new()
        $checkoutPathIssues = [Collections.Generic.List[string]]::new()
        $checkoutSeen = $false
        $scriptPaths = [Collections.Generic.List[string]]::new()
        $violations = [Collections.Generic.List[string]]::new()

        foreach ($step in $steps) {
            $stepText = @($step.Lines) -join "`n"
            $uses = [regex]::Match($stepText, '(?im)^[ \t]*(?:-\s*)?uses:\s*["'']?(?<value>[^\s"'']+)')
            if ($uses.Success -and $uses.Groups['value'].Value -match '(?i)^actions/checkout@') {
                $checkoutSeen = $true
                $checkoutPath = '.'
                $pathMatch = [regex]::Match($stepText, '(?im)^\s*path:\s*(?<value>[^#\r\n]+?)\s*(?:#.*)?$')
                if ($pathMatch.Success) { $checkoutPath = $pathMatch.Groups['value'].Value.Trim().Trim('"').Trim("'") }
                $resolvedCheckoutPath = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $checkoutPath
                if ($null -eq $resolvedCheckoutPath) {
                    $checkoutPathIssues.Add("checkout path '$checkoutPath' cannot be resolved")
                }
                else {
                    $checkoutPaths.Add($resolvedCheckoutPath)
                }
            }

            foreach ($command in @(Get-WorkflowRunCommands -StepLines $step.Lines)) {
                $normalizedCommand = $command.Replace('\', '/')
                foreach ($scriptMatch in [regex]::Matches($normalizedCommand, $scriptPattern)) {
                    $invocationPath = $scriptMatch.Groups['path'].Value.Replace('\', '/')
                    $scriptIndex = $invocationPath.LastIndexOf('scripts/', [StringComparison]::OrdinalIgnoreCase)
                    if ($scriptIndex -lt 0) { continue }
                    $trackedScriptPath = $invocationPath.Substring($scriptIndex)
                    if (-not $tracked.Contains($trackedScriptPath)) { continue }
                    if (-not $scriptPaths.Contains($trackedScriptPath)) { $scriptPaths.Add($trackedScriptPath) }

                    $stepWorkingDirectory = [regex]::Match($stepText, '(?im)^\s*working-directory:\s*(?<value>[^#\r\n]+?)\s*(?:#.*)?$')
                    $workingDirectory = if ($stepWorkingDirectory.Success) { $stepWorkingDirectory.Groups['value'].Value.Trim().Trim('"').Trim("'") } else { $jobWorkingDirectory }
                    $resolvedWorkingDirectory = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $workingDirectory
                    $resolvedInvocation = if ($null -eq $resolvedWorkingDirectory) { $null } else { Resolve-WorkflowRelativePath -BasePath $resolvedWorkingDirectory -RelativePath $invocationPath }
                    if (-not $checkoutSeen) {
                        $violation = "tracked script '$trackedScriptPath' runs with no preceding repository checkout"
                        if (-not $violations.Contains($violation)) { $violations.Add($violation) }
                        continue
                    }

                    $resolvesInsideCheckout = $false
                    if ($null -ne $resolvedInvocation) {
                        foreach ($checkoutPath in $checkoutPaths) {
                            $relativeInvocation = if ([string]::IsNullOrEmpty($checkoutPath)) {
                                $resolvedInvocation
                            }
                            elseif ($resolvedInvocation.StartsWith("$checkoutPath/", [StringComparison]::OrdinalIgnoreCase)) {
                                $resolvedInvocation.Substring($checkoutPath.Length + 1)
                            }
                            elseif ($resolvedInvocation.Equals($checkoutPath, [StringComparison]::OrdinalIgnoreCase)) {
                                ''
                            }
                            else {
                                $null
                            }
                            if ($null -ne $relativeInvocation -and $relativeInvocation.Equals($trackedScriptPath, [StringComparison]::OrdinalIgnoreCase)) {
                                $resolvesInsideCheckout = $true
                                break
                            }
                        }
                    }
                    if (-not $resolvesInsideCheckout) {
                        $violation = if ($checkoutPathIssues.Count -gt 0) {
                            $checkoutPathIssues -join '; '
                        }
                        else {
                            "tracked script '$trackedScriptPath' does not resolve inside a checked-out repository from the step working directory"
                        }
                        if (-not $violations.Contains($violation)) { $violations.Add($violation) }
                    }
                }
            }
        }

        $consumers.Add([pscustomobject]@{
            Name = $job.Name
            ScriptPaths = $scriptPaths.ToArray()
            Violations = $violations.ToArray()
        })
    }
    return $consumers.ToArray()
}

try {
    $projectText = Get-Content -LiteralPath $projectFile -Raw
    $versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if (-not $versionMatch.Success) { throw 'The real packable project has no explicit package version.' }
    $version = $versionMatch.Groups['version'].Value.Trim()
    $realChangelogText = Get-Content -LiteralPath $realChangelog -Raw
    $releaseHeader = [regex]::Match($realChangelogText, "(?m)^##\s+\[$([regex]::Escape($version))\]\s+-\s+(?<date>\d{4}-\d{2}-\d{2})\s*$")
    if (-not $releaseHeader.Success) { throw 'The real changelog has no dated entry for the package version.' }
    $releaseDate = $releaseHeader.Groups['date'].Value

    if (-not (Test-Path -LiteralPath $ciWorkflowPath -PathType Leaf)) { throw 'CI workflow is missing.' }
    if (-not (Test-Path -LiteralPath $releaseWorkflowPath -PathType Leaf)) { throw 'Release workflow is missing.' }
    $ciWorkflow = Get-Content -LiteralPath $ciWorkflowPath -Raw
    $releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
    # Current downstream artifact consumers use inline checks. Keep the source guard
    # exercised through synthetic jobs while the workflow parser below enforces that
    # the real consumers do not invoke repository scripts after downloading artifacts.
    Assert-WorkflowScriptSourceRegressionCoverage -CurrentConsumers @() -RepositoryRoot $root
    Write-Output 'PASS: checkout-source guard tracks effective root checkout state through each guarded script invocation.'
    $trackedRepositoryFiles = @(git -C $root ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked repository files for workflow-consumer checks.' }

    $currentArtifactConsumers = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $releaseWorkflow -TrackedFiles $trackedRepositoryFiles)
    $currentArtifactConsumerNames = @($currentArtifactConsumers | ForEach-Object Name)
    foreach ($expectedConsumer in @('publish', 'github-release')) {
        if ($expectedConsumer -notin $currentArtifactConsumerNames) { throw "Release workflow artifact consumer '$expectedConsumer' was not enumerated." }
    }
    foreach ($consumer in $currentArtifactConsumers) {
        if ($consumer.Violations.Count -gt 0) {
            throw "Release workflow artifact consumer '$($consumer.Name)' is invalid: $($consumer.Violations -join '; ')"
        }
    }
    $currentScriptConsumers = @($currentArtifactConsumers | Where-Object { $_.ScriptPaths.Count -gt 0 } | ForEach-Object Name)
    if ($currentScriptConsumers.Count -gt 0) {
        throw "Downstream artifact jobs must not call tracked repository scripts on clean runners: $($currentScriptConsumers -join ', ')."
    }
    Write-Output "RELEASE_ARTIFACT_CONSUMERS=$($currentArtifactConsumerNames -join ',')"
    Write-Output "RELEASE_SCRIPT_ARTIFACT_CONSUMERS=$(if ($currentScriptConsumers.Count -eq 0) { '<none>' } else { $currentScriptConsumers -join ',' })"

    $fourSpaceWithoutCheckout = @"
name: Future consumer with four-space job indentation
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - name: Validate downloaded package
          shell: pwsh
          run: |
            & ./scripts/validate-release.ps1 -Version $version
"@
    $fourSpaceConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $fourSpaceWithoutCheckout -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($fourSpaceConsumer.Count -ne 1 -or $fourSpaceConsumer[0].ScriptPaths.Count -ne 1 -or
        @($fourSpaceConsumer[0].Violations | Where-Object { $_ -match 'no preceding repository checkout' }).Count -ne 1) {
        throw 'Four-space-indented artifact consumers must be discovered and rejected when they run a tracked script without checkout.'
    }

    $sourceCheckoutFromWorkspaceRoot = @"
name: Future consumer with source checkout
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Validate downloaded package from workspace root
          shell: pwsh
          run: |
            & ./scripts/validate-release.ps1 -Version $version
"@
    $sourceRootConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $sourceCheckoutFromWorkspaceRoot -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($sourceRootConsumer.Count -ne 1 -or
        @($sourceRootConsumer[0].Violations | Where-Object { $_ -match 'does not resolve inside a checked-out repository' }).Count -ne 1) {
        throw 'A workspace-root script invocation must be rejected when checkout placed the repository under source/. '
    }

    $sourceCheckoutWithWorkingDirectory = @"
name: Future consumer with source working directory
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Validate downloaded package inside checkout
          shell: pwsh
          working-directory: source
          run: |
            & ./scripts/validate-release.ps1 -Version $version
"@
    $sourceWorkingDirectoryConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $sourceCheckoutWithWorkingDirectory -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($sourceWorkingDirectoryConsumer.Count -ne 1 -or $sourceWorkingDirectoryConsumer[0].Violations.Count -ne 0) {
        throw 'A tracked script invocation from the source checkout working directory must pass.'
    }

    $sourceCheckoutFromWorkspaceRootWithQualifiedPath = @"
name: Future consumer with qualified source script path
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Validate downloaded package from workspace root
          shell: pwsh
          run: |
            & .\source\scripts\validate-release.ps1 -Version $version
"@
    $qualifiedSourceConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $sourceCheckoutFromWorkspaceRootWithQualifiedPath -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($qualifiedSourceConsumer.Count -ne 1 -or $qualifiedSourceConsumer[0].Violations.Count -ne 0) {
        throw 'A tracked script invocation qualified with the source checkout path must pass from the workspace root.'
    }

    $rootCheckoutConsumerWorkflow = @"
name: Future consumer with root checkout
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
        - name: Validate downloaded package
          shell: pwsh
          run: pwsh ./scripts/validate-release.ps1 -Version $version
"@
    $rootCheckoutConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $rootCheckoutConsumerWorkflow -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($rootCheckoutConsumer.Count -ne 1 -or $rootCheckoutConsumer[0].Violations.Count -ne 0) {
        throw 'A tracked script invocation from the default workspace root checkout must pass.'
    }

    $jobDefaultWorkingDirectoryWorkflow = @"
name: Future consumer with job run defaults
jobs:
    artifact-check:
      needs: prepare
      defaults:
        run:
          working-directory: source
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Validate downloaded package
          shell: pwsh
          run: pwsh ./scripts/validate-release.ps1 -Version $version
"@
    $jobDefaultConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $jobDefaultWorkingDirectoryWorkflow -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($jobDefaultConsumer.Count -ne 1 -or $jobDefaultConsumer[0].Violations.Count -ne 0) {
        throw 'A job-level run working directory must resolve scripts inside a non-root checkout.'
    }

    $workflowDefaultWorkingDirectoryWorkflow = @"
name: Future consumer with workflow run defaults
defaults:
  run:
    working-directory: source
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Validate downloaded package
          shell: pwsh
          run: pwsh ./scripts/validate-release.ps1 -Version $version
"@
    $workflowDefaultConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $workflowDefaultWorkingDirectoryWorkflow -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($workflowDefaultConsumer.Count -ne 1 -or $workflowDefaultConsumer[0].Violations.Count -ne 0) {
        throw 'A workflow-level run working directory must resolve scripts inside a non-root checkout.'
    }

    $stepDirectoryDoesNotLeakWorkflow = @"
name: Future consumer with step-scoped directory
jobs:
    artifact-check:
      needs: prepare
      steps:
        - uses: actions/download-artifact@v4
        - uses: actions/checkout@v4
          with:
            path: source
        - name: Run a command from the checkout
          working-directory: source
          run: pwsh -NoLogo -Command 'Get-Location'
        - name: Validate downloaded package from workspace root
          shell: pwsh
          run: pwsh ./scripts/validate-release.ps1 -Version $version
"@
    $stepScopedConsumer = @(Get-WorkflowArtifactScriptConsumers -WorkflowText $stepDirectoryDoesNotLeakWorkflow -TrackedFiles $trackedRepositoryFiles | Where-Object Name -eq 'artifact-check')
    if ($stepScopedConsumer.Count -ne 1 -or
        @($stepScopedConsumer[0].Violations | Where-Object { $_ -match 'does not resolve inside a checked-out repository' }).Count -ne 1) {
        throw 'A step-level working directory must not leak into later script invocations.'
    }

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
        './scripts/run-local-gate.ps1 -ArtifactDirectory artifacts/release -ExpectedVersion $version',
        './scripts/validate-release-artifact-set.ps1 -Version $env:RELEASE_VERSION -ArtifactDirectory artifacts/release',
        'path: artifacts/release/*',
        'NuGet/login@v1',
        'user: dmitriyzen',
        'needs: validate',
        'needs: [validate, publish]',
        'dotnet nuget push',
        'gh release create',
        '--verify-tag',
        '--repo "$RELEASE_REPOSITORY"',
        '[IO.File]::AppendAllText($env:GITHUB_OUTPUT',
        'version=$version',
        'RELEASE_VERSION: ${{ steps.release_contract.outputs.version }}',
        'RELEASE_VERSION: ${{ needs.validate.outputs.version }}',
        'if ([string]::IsNullOrWhiteSpace($version))',
        'if ($env:RELEASE_TAG -cne "v$version")',
        'Validate package set before NuGet authentication',
        'Downloaded release artifact set is not exact',
        'expected_package="KeelMatrix.PackageSurface.$RELEASE_VERSION.nupkg"',
        'RELEASE_REPOSITORY: ${{ github.repository }}'
    )) {
        if (-not $releaseWorkflow.Contains($required, [StringComparison]::Ordinal)) { throw "Release workflow is missing '$required'." }
    }
    $validatedOutputMapping = 'RELEASE_VERSION: ${{ needs.validate.outputs.version }}'
    if ([regex]::Matches($releaseWorkflow, [regex]::Escape($validatedOutputMapping)).Count -ne 2) {
        throw 'NuGet publish and GitHub Release must both receive the validated job output.'
    }
    if ($releaseWorkflow.Contains("PACKAGE_VERSION:", [StringComparison]::Ordinal)) { throw 'Release workflow maintains a second hard-coded package-version definition.' }
    if (-not $releaseWorkflow.Contains('steps.release_contract.outputs.version', [StringComparison]::Ordinal)) {
        throw 'Artifact validation does not receive the release-contract step output.'
    }
    if ($releaseWorkflow.Contains('run-phase0.ps1', [StringComparison]::Ordinal)) { throw 'Release workflow runs a mutating standalone Phase 0 step.' }
    if ($releaseWorkflow -match '(?m)dotnet\s+run\s+--project\s+tests/') { throw 'Release workflow duplicates console leaf tests beside the canonical gate.' }
    if ($releaseWorkflow -match '(?m)^\s*run:\s*dotnet\s+pack\b') { throw 'Release workflow repacks a separately validated artifact.' }
    $artifactSetValidationIndex = $releaseWorkflow.IndexOf('./scripts/validate-release-artifact-set.ps1 -Version $env:RELEASE_VERSION -ArtifactDirectory artifacts/release', [StringComparison]::Ordinal)
    $artifactUploadIndex = $releaseWorkflow.IndexOf('uses: actions/upload-artifact@v4', [StringComparison]::Ordinal)
    if ($artifactSetValidationIndex -lt 0 -or $artifactUploadIndex -lt 0 -or $artifactSetValidationIndex -ge $artifactUploadIndex) {
        throw 'The generated package set must pass the shared validator before upload.'
    }
    if ($releaseWorkflow -notmatch '(?s)validate:.*outputs:.*version:.*release_contract') { throw 'Release workflow does not pass the validated tag version to downstream jobs.' }
    if ($releaseWorkflow -notmatch '(?s)publish:.*needs:\s*validate.*NuGet/login@v1') { throw 'Publish does not depend on validation and Trusted Publishing.' }
    if ($releaseWorkflow -notmatch '(?s)github-release:.*needs:\s*\[validate, publish\]') { throw 'GitHub Release is not ordered after validation and publication.' }
    $publishValidationIndex = $releaseWorkflow.IndexOf('Validate package set before NuGet authentication', [StringComparison]::Ordinal)
    $nugetLoginIndex = $releaseWorkflow.IndexOf('uses: NuGet/login@v1', [StringComparison]::Ordinal)
    if ($publishValidationIndex -lt 0 -or $nugetLoginIndex -lt 0 -or $publishValidationIndex -ge $nugetLoginIndex) {
        throw 'Downloaded artifacts must be validated before NuGet Trusted Publishing authentication.'
    }
    $publishPreflight = $releaseWorkflow.Substring($publishValidationIndex, $nugetLoginIndex - $publishValidationIndex)
    if (-not $publishPreflight.Contains('if ([string]::IsNullOrWhiteSpace($version))', [StringComparison]::Ordinal) -or
        -not $publishPreflight.Contains('if ($env:RELEASE_TAG -cne "v$version")', [StringComparison]::Ordinal)) {
        throw 'NuGet authentication must be preceded by empty-version and tag-version checks.'
    }
    if (-not $releaseWorkflow.Contains('if [[ "$RELEASE_TAG" != "v$RELEASE_VERSION" ]]', [StringComparison]::Ordinal) -or
        $releaseWorkflow.IndexOf('if [[ "$RELEASE_TAG" != "v$RELEASE_VERSION" ]]', [StringComparison]::Ordinal) -ge
        $releaseWorkflow.IndexOf('gh release create', [StringComparison]::Ordinal)) {
        throw 'GitHub Release must reject an empty or tag-mismatched version before release creation.'
    }

    $localGate = Get-Content -LiteralPath $localGatePath -Raw
    foreach ($requiredGate in @(
        'scripts/test-release-contract.ps1',
        'scripts/test-vulnerability-audit.ps1',
        'validate-package-artifact.ps1',
        'dotnet pack',
        '[string] $ExpectedVersion',
        'scripts/validate-release.ps1',
        'dotnet msbuild $cliProject -getProperty:Version',
        '$suppliedPackageVersion = if ($PSBoundParameters.ContainsKey(''ExpectedVersion'')) { [string]$ExpectedVersion } else { $projectVersion }',
        '$packageVersion -cne $projectVersion',
        '--version $packageVersion',
        '-ExpectedVersion $packageVersion',
        'Compare-Object -CaseSensitive'
    )) {
        if (-not $localGate.Contains($requiredGate, [StringComparison]::Ordinal)) { throw "Canonical local gate is missing '$requiredGate'." }
    }
    if ($localGate -notmatch '(?s)finally\s*\{.*\$scratch') { throw 'Canonical local gate does not clean run-owned scratch state in finally.' }
    if ($localGate -notmatch 'ReadAllBytes|Read-ZipEntryBytes') { throw 'Canonical local gate does not validate package bytes.' }

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
    Assert-ValidationRejects 'local gate empty expected version' (Invoke-LocalGateVersionValidation -Version '')
    Assert-ValidationRejects 'local gate whitespace expected version' (Invoke-LocalGateVersionValidation -Version ' ')
    Assert-ValidationRejects 'local gate mismatched expected version' (Invoke-LocalGateVersionValidation -Version '9.9.9')

    $workflowOutputPath = Join-Path $scratch 'github-output'
    $serializedOutput = 'version=' + $version + [Environment]::NewLine
    [IO.File]::AppendAllText($workflowOutputPath, $serializedOutput, [Text.UTF8Encoding]::new($false))
    $workflowOutputBytes = [IO.File]::ReadAllBytes($workflowOutputPath)
    if ($workflowOutputBytes.Length -ge 3 -and $workflowOutputBytes[0] -eq 239 -and $workflowOutputBytes[1] -eq 187 -and $workflowOutputBytes[2] -eq 191) {
        throw 'GitHub Actions release output must be written without a UTF-8 byte-order mark.'
    }
    if ([IO.File]::ReadAllText($workflowOutputPath) -cne $serializedOutput) {
        throw 'Release-contract output does not serialize the validated version exactly.'
    }

    $missingUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.missing-unreleased.md'
    $missingUnreleasedFirstRelease = "# Changelog`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($missingUnreleasedChangelog, $missingUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release missing Unreleased section' (Invoke-ReleaseValidation -Version $version -ChangelogPath $missingUnreleasedChangelog -RequireFinalized -FirstRelease)

    $alternateUnreleasedChangelog = Join-Path $scratch 'CHANGELOG.alternate-unreleased.md'
    $alternateUnreleasedFirstRelease = "# Changelog`n`n## [Next]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($alternateUnreleasedChangelog, $alternateUnreleasedFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release alternate Unreleased heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $alternateUnreleasedChangelog -RequireFinalized -FirstRelease)

    $nestedReleaseChangelog = Join-Path $scratch 'CHANGELOG.nested-release.md'
    $nestedReleaseFirstRelease = "# Changelog`n`n## [Unreleased]`n`n### [$version] - $releaseDate`n`n#### Added`n`n- Initial release capability review.`n"
    [IO.File]::WriteAllText($nestedReleaseChangelog, $nestedReleaseFirstRelease, [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release entry nested under Unreleased' (Invoke-ReleaseValidation -Version $version -ChangelogPath $nestedReleaseChangelog -RequireFinalized -FirstRelease)

    $candidateChangelog = Join-Path $scratch 'CHANGELOG.finalized.md'
    $candidateFirstRelease = "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n"
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

    $wholeWordChangelog = Join-Path $scratch 'CHANGELOG.whole-word.md'
    [IO.File]::WriteAllText($wholeWordChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Adds package prefixes and nowhere-only documentation examples.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release whole-word marker boundaries' (Invoke-ReleaseValidation -Version $version -ChangelogPath $wholeWordChangelog -RequireFinalized -FirstRelease)

    foreach ($marker in @('now', 'no longer', 'previously', 'formerly', 'used to', 'fixed', 'fixes', 'corrected', 'resolved', 'addressed', 'this removes', 'this fixes', 'changed from')) {
        $markerChangelog = Join-Path $scratch ('CHANGELOG.marker-' + ($marker -replace '[^A-Za-z0-9]+', '-') + '.md')
        [IO.File]::WriteAllText($markerChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- $marker release wording.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release remediation marker '$marker'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $markerChangelog -RequireFinalized -FirstRelease)
    }

    foreach ($category in @('Changed', 'Fixed', 'Deprecated', 'Removed', 'Security', 'Compatibility')) {
        $categoryChangelog = Join-Path $scratch ('CHANGELOG.category-' + $category + '.md')
        [IO.File]::WriteAllText($categoryChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`n### $category`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release category '$category'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $categoryChangelog -RequireFinalized -FirstRelease)
    }
    foreach ($heading in @('Notes', 'Metadata')) {
        $headingChangelog = Join-Path $scratch ('CHANGELOG.heading-' + $heading + '.md')
        [IO.File]::WriteAllText($headingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`n#### $heading`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
        Assert-ValidationRejects "first-release unknown heading '$heading'" (Invoke-ReleaseValidation -Version $version -ChangelogPath $headingChangelog -RequireFinalized -FirstRelease)
    }
    $h1HeadingChangelog = Join-Path $scratch 'CHANGELOG.h1-heading.md'
    [IO.File]::WriteAllText($h1HeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`n# Notes`n`n- Release note.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release unknown level-one heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1HeadingChangelog -RequireFinalized -FirstRelease)
    $h1AddedChangelog = Join-Path $scratch 'CHANGELOG.h1-added.md'
    [IO.File]::WriteAllText($h1AddedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n# Added`n`n- Initial release capability review.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release level-one Added heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $h1AddedChangelog -RequireFinalized -FirstRelease)

    $setextHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-heading.md'
    [IO.File]::WriteAllText($setextHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`nNotes`n-----`n`n- This heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext category heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextHeadingChangelog -RequireFinalized -FirstRelease)

    $setextDocumentHeadingChangelog = Join-Path $scratch 'CHANGELOG.setext-document-heading.md'
    [IO.File]::WriteAllText($setextDocumentHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`nNotes`n=====`n`n- This document heading must be rejected.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'first-release setext document heading' (Invoke-ReleaseValidation -Version $version -ChangelogPath $setextDocumentHeadingChangelog -RequireFinalized -FirstRelease)

    $fencedHeadingChangelog = Join-Path $scratch 'CHANGELOG.fenced-heading.md'
    [IO.File]::WriteAllText($fencedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`n~~~markdown`n# Notes`n`nNotes`n-----`n~~~`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release fenced headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $fencedHeadingChangelog -RequireFinalized -FirstRelease)

    $indentedHeadingChangelog = Join-Path $scratch 'CHANGELOG.indented-heading.md'
    [IO.File]::WriteAllText($indentedHeadingChangelog, "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Initial release capability review.`n`n    # Notes`n    Notes`n    -----`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationPass 'first-release indented code headings ignored' (Invoke-ReleaseValidation -Version $version -ChangelogPath $indentedHeadingChangelog -RequireFinalized -FirstRelease)

    $mismatchedChangelog = Join-Path $scratch 'CHANGELOG.mismatched.md'
    [IO.File]::WriteAllText($mismatchedChangelog, "# Changelog`n`n## [Unreleased]`n`n## [9.9.9] - $releaseDate`n`n- Wrong version.`n", [Text.UTF8Encoding]::new($false))
    Assert-ValidationRejects 'changelog/version disagreement' (Invoke-ReleaseValidation -Version $version -ChangelogPath $mismatchedChangelog -RequireFinalized -FirstRelease)

    Write-Output 'PASS: restored CI/release workflows use one shared fail-closed release contract; real candidate, finalized, and version-mismatch cases behave as required.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
