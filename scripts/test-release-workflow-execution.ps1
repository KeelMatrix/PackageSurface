$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
. (Join-Path $PSScriptRoot 'ReleaseWorkflowSourceGuard.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-workflow-' + [Guid]::NewGuid().ToString('N'))
$scratch = [IO.Path]::GetFullPath($scratch)
if (-not $scratch.StartsWith([IO.Path]::GetFullPath([IO.Path]::GetTempPath()), [StringComparison]::Ordinal)) { throw 'Fixture directory is outside temporary storage.' }
$utf8 = [Text.UTF8Encoding]::new($false)
$caseCount = 0

function Assert-Case([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Write-Fixture([string] $Path, [string] $Text) {
    [void][IO.Directory]::CreateDirectory((Split-Path -Parent $Path))
    [IO.File]::WriteAllText($Path, $Text, $utf8)
}

function Quote-Pwsh([string] $Value) { return "'" + $Value.Replace("'", "''") + "'" }

function Invoke-FixtureBash {
    param([Parameter(ValueFromRemainingArguments)] [string[]] $ArgumentList)
    if ($IsWindows) { & 'C:/Program Files/Git/bin/bash.exe' @ArgumentList }
    else { & bash @ArgumentList }
    $global:LASTEXITCODE = $LASTEXITCODE
}

# These selectors support this repository's blocks, not arbitrary workflow YAML.
# RunText always comes from the existing workflow reader, unchanged.
function Get-Step($Job, [string] $Name) {
    $rawSteps = @([regex]::Matches(($Job.Lines -join "`n"), '(?ms)^      - name: (?<name>[^\n]+)\n(?<body>.*?)(?=^      - name: |\z)'))
    $steps = @(Get-WorkflowSteps -Job $Job)
    Assert-Case ($rawSteps.Count -eq $steps.Count) "Unsupported step layout in $($Job.Name)."
    $indices = @(0..($rawSteps.Count - 1) | Where-Object { $rawSteps[$_].Groups['name'].Value.Trim() -ceq $Name })
    Assert-Case ($indices.Count -eq 1) "Expected exactly one step named '$Name'."
    $index = $indices[0]
    return [pscustomobject]@{ Step = $steps[$index]; Raw = $rawSteps[$index].Value }
}

function Get-OneValue([string] $Text, [string] $Pattern) {
    $matches = @([regex]::Matches($Text, $Pattern))
    Assert-Case ($matches.Count -eq 1) "Expected one workflow mapping matching $Pattern."
    return $matches[0].Groups['value'].Value.Trim()
}

function Resolve-StepVersion([string] $Expression, $Contract, [string] $OutputText) {
    $id = Get-OneValue $Contract.Raw '(?m)^        id: (?<value>\S+)\s*$'
    $reference = [regex]::Match($Expression, '^\$\{\{ steps\.(?<id>[\w-]+)\.outputs\.(?<key>[\w-]+) \}\}$')
    Assert-Case ($reference.Success -and $reference.Groups['id'].Value -ceq $id) 'Step output points at the wrong step.'
    $records = @($OutputText -split "`n" | Where-Object { $_.Length -gt 0 })
    Assert-Case ($records.Count -eq 1) 'Successful validation must emit exactly one output record.'
    $record = [regex]::Match($records[0], '^(?<key>[^=]+)=(?<value>[^\r\n]+)$')
    Assert-Case ($record.Success -and $record.Groups['key'].Value -ceq $reference.Groups['key'].Value) 'Version output is missing or mapped to the wrong key.'
    return $record.Groups['value'].Value
}

function Resolve-JobVersion([string] $Expression, [string] $JobVersion) {
    Assert-Case ($Expression -ceq '${{ needs.validate.outputs.version }}') 'Downstream version is not mapped to the validate job output.'
    return $JobVersion
}

function Assert-Rejected([scriptblock] $Action, [string] $Name) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    Assert-Case $rejected "Negative control was not detected: $Name."
}

function Invoke-Block([string] $Text, [hashtable] $Environment = @{}, [string] $State = 'unset', [switch] $Bash) {
    $script:caseCount++
    $scriptPath = Join-Path $scratch ("step-$caseCount" + $(if ($Bash) { '.sh' } else { '.ps1' }))
    $outputPath = Join-Path $scratch "output-$caseCount"
    $logPath = Join-Path $scratch "calls-$caseCount"
    $values = @{
        RELEASE_TAG = "v$version"; RELEASE_VERSION = $version; GITHUB_OUTPUT = $outputPath
        RELEASE_REPOSITORY = 'example/offline'; NUGET_API_KEY = 'offline-placeholder'
        GH_TOKEN = ''; GH_HOST = ''; GITHUB_TOKEN = ''; GH_ENTERPRISE_TOKEN = ''
        FIXTURE_LOG = $logPath; FIXTURE_VERSION = $version; FIXTURE_GATE = 'success'
        FIXTURE_SET = 'exact'; FIXTURE_NATIVE_EXIT = '0'; FIXTURE_AUTH_EXIT = '0'
    }
    foreach ($key in $Environment.Keys) { $values[$key] = $Environment[$key] }
    Write-Fixture $outputPath ''
    $prefix = [Collections.Generic.List[string]]::new()
    if ($Bash) {
        $prefix.Add('set -euo pipefail')
        $values['FIXTURE_LOG'] = $logPath.Replace('\', '/')
        foreach ($key in $values.Keys) { $prefix.Add("export $key='" + ([string]$values[$key]).Replace("'", "'\''") + "'") }
        # PATH is closed: gh is a local executable, never the provider launcher.
        $prefix.Add("export PATH='" + ($shimDirectory.Replace('\', '/') -replace '^([A-Za-z]):', '/$1') + ":/usr/bin:/bin'")
        $prefix.Add("cd '" + $fixture.Replace('\', '/') + "'")
    }
    else {
        $prefix.Add('$ErrorActionPreference = ''Stop''')
        $prefix.Add('Set-Location -LiteralPath ' + (Quote-Pwsh $fixture))
        foreach ($key in $values.Keys) { $prefix.Add('$env:' + $key + ' = ' + (Quote-Pwsh ([string]$values[$key]))) }
        $prefix.Add('. ' + (Quote-Pwsh (Join-Path $root 'build/Invoke-NestedPwsh.ps1')))
        # The function forwards to a real child process; its native result is exported by the helper.
        $prefix.Add('function dotnet { Invoke-NestedPwsh -NoLogo -NoProfile -File ' + (Quote-Pwsh $nativeFixture) + ' @args }')
        $prefix.Add($(switch ($State) {
            'unset' { 'Remove-Variable LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue' }
            'zero' { '$global:LASTEXITCODE = 0' }
            'nonzero' { '$global:LASTEXITCODE = 23' }
            default { throw "Unknown initial exit state $State." }
        }))
    }
    $suffix = if ($Bash) { '' } else { 'if (Test-Path -LiteralPath variable:\LASTEXITCODE) { exit $LASTEXITCODE }' }
    Write-Fixture $scriptPath (($prefix -join "`n") + "`n" + $Text + "`n" + $suffix + "`n")
    $timer = [Diagnostics.Stopwatch]::StartNew()
    if ($Bash) { $messages = @(Invoke-FixtureBash --noprofile --norc $scriptPath 2>&1) }
    else { $messages = @(Invoke-NestedPwsh -NoLogo -NoProfile -NonInteractive -Command ('. ' + (Quote-Pwsh $scriptPath)) 2>&1) }
    $exitCode = $LASTEXITCODE
    $bytes = [IO.File]::ReadAllBytes($outputPath)
    $calls = if (Test-Path -LiteralPath $logPath) { [IO.File]::ReadAllText($logPath) } else { '' }
    Write-Output "CASE=$caseCount STATE=$State EXIT=$exitCode DURATION_MS=$($timer.ElapsedMilliseconds)"
    return [pscustomobject]@{ Exit = $exitCode; Output = [Text.Encoding]::UTF8.GetString($bytes); Bytes = $bytes; Messages = $messages -join "`n"; Calls = $calls }
}

function Invoke-Case([string] $Text, [hashtable] $Environment = @{}, [string] $State = 'unset', [switch] $Bash) {
    # Keep the case receipt visible while returning only the process observation.
    $items = @(Invoke-Block $Text $Environment $State -Bash:$Bash)
    $items | Where-Object { $_ -is [string] } | ForEach-Object { Write-Host $_ }
    return $items[-1]
}

function Assert-Stopped($Result, [string] $Name, [string] $Forbidden = 'AFTER_BLOCK') {
    Assert-Case ($Result.Exit -ne 0 -and $Result.Messages -notmatch $Forbidden) "$Name did not stop the step: $($Result.Messages)"
}

try {
    $release = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/release.yml'))
    $ci = [IO.File]::ReadAllText((Join-Path $root '.github/workflows/ci.yml'))
    $jobs = @(Get-WorkflowJobs $release)
    Assert-Case ($jobs.Count -eq 3) 'Expected the three release jobs.'
    $validate = @($jobs | Where-Object Name -CEQ 'validate')[0]
    $publish = @($jobs | Where-Object Name -CEQ 'publish')[0]
    $releaseJob = @($jobs | Where-Object Name -CEQ 'github-release')[0]
    $contract = Get-Step $validate 'Validate tag, version, and finalized changelog'
    $build = Get-Step $validate 'Build and validate exact release artifacts'
    $preflight = Get-Step $publish 'Validate package set before NuGet authentication'
    $push = Get-Step $publish 'Publish exact validated package with short-lived credential'
    $createRelease = Get-Step $releaseJob 'Create release after successful publication'
    $ciJob = @(Get-WorkflowJobs $ci)[0]
    $restore = Get-Step $ciJob 'Restore with repository NuGet configuration'
    $ciGate = Get-Step $ciJob 'Run the repository validation gate'
    $version = [regex]::Match([IO.File]::ReadAllText((Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj')), '<Version>([^<]+)</Version>').Groups[1].Value
    Assert-Case (-not [string]::IsNullOrEmpty($version)) 'Project version is missing.'
    $fixture = Join-Path $scratch 'repository'
    $shimDirectory = Join-Path $scratch 'bin'
    $nativeFixture = Join-Path $scratch 'native.ps1'
    $authFixture = Join-Path $scratch 'auth.ps1'
    # The release Bash job runs on Ubuntu and uses GNU mapfile/find. Git Bash
    # supplies the same tools on Windows; macOS keeps the PowerShell cases.
    $runReleaseBash = $IsLinux -or $IsWindows
    $changelogPath = Join-Path $fixture 'CHANGELOG.md'
    $changelog = [IO.File]::ReadAllText((Join-Path $root 'CHANGELOG.md'))
    Write-Fixture $changelogPath $changelog
    Write-Fixture (Join-Path $fixture 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj') "<Project><PropertyGroup><Version>$version</Version></PropertyGroup></Project>"
    foreach ($script in @('validate-release.ps1', 'validate-release-artifact-set.ps1')) {
        Write-Fixture (Join-Path $fixture "scripts/$script") ([IO.File]::ReadAllText((Join-Path $root "scripts/$script")))
    }
    $gateFixture = @'
param([string] $ArtifactDirectory = 'artifacts/release', [string] $ExpectedVersion)
$ErrorActionPreference = 'Stop'
[IO.File]::AppendAllText($env:FIXTURE_LOG, "GATE version=$ExpectedVersion`n")
if ($env:FIXTURE_GATE -eq 'throw') { throw 'Deliberate gate assertion failure.' }
if ($env:FIXTURE_GATE -eq 'failure') { exit 37 }
if ($ExpectedVersion -cne $env:FIXTURE_VERSION) { throw 'Unexpected gate version.' }
[void][IO.Directory]::CreateDirectory($ArtifactDirectory)
$names = @("KeelMatrix.PackageSurface.$ExpectedVersion.nupkg", "KeelMatrix.PackageSurface.$ExpectedVersion.snupkg")
switch ($env:FIXTURE_SET) {
    'missing-package' { $names = @($names[1]) }
    'missing-symbols' { $names = @($names[0]) }
    'extra' { $names += 'unexpected.txt' }
    'case' { $names[0] = $names[0].ToLowerInvariant() }
}
foreach ($name in $names) { [IO.File]::WriteAllText((Join-Path $ArtifactDirectory $name), '') }
if ($env:FIXTURE_SET -eq 'directory') { [void][IO.Directory]::CreateDirectory((Join-Path $ArtifactDirectory 'unexpected')) }
exit 0
'@
    Write-Fixture (Join-Path $fixture 'scripts/run-local-gate.ps1') $gateFixture
    Write-Fixture $nativeFixture @'
[IO.File]::AppendAllText($env:FIXTURE_LOG, "NATIVE $($args -join '|')`n")
exit ([int]$env:FIXTURE_NATIVE_EXIT)
'@
    Write-Fixture $authFixture @'
[IO.File]::AppendAllText($env:FIXTURE_LOG, "AUTH_SUBSTITUTE`n")
exit ([int]$env:FIXTURE_AUTH_EXIT)
'@
    Write-Fixture (Join-Path $shimDirectory 'gh') @'
#!/usr/bin/env bash
printf 'GH' >> "$FIXTURE_LOG"
printf '|%s' "$@" >> "$FIXTURE_LOG"
printf '\n' >> "$FIXTURE_LOG"
exit "$FIXTURE_NATIVE_EXIT"
'@
    if ($runReleaseBash) {
        Invoke-FixtureBash --noprofile --norc -c 'chmod +x "$1"' fixture ((Join-Path $shimDirectory 'gh').Replace('\', '/'))
        Assert-Case ($LASTEXITCODE -eq 0) 'Cannot prepare the offline gh executable.'
    }
    else { Write-Output 'BASH_RELEASE_EXECUTION=NOT_RUN (Ubuntu job; GNU Bash/find required)' }

    # Run the real validator through the exact Actions block in all ambient states.
    foreach ($state in @('unset', 'zero', 'nonzero')) {
        $good = Invoke-Case $contract.Step.RunText @{} $state
        Assert-Case ($good.Exit -eq 0 -and $good.Messages.Contains("RELEASE_CONTRACT=PASS VERSION=$version FINALIZED=True")) "Successful contract failed with prior $state."
        Assert-Case ($good.Output -ceq "version=$version`n") "Successful contract did not emit one exact version with prior $state."
        Assert-Case ([Convert]::ToHexString($good.Bytes) -ceq [Convert]::ToHexString($utf8.GetBytes("version=$version`n"))) 'Output encoding or BOM differs.'
        foreach ($tag in @('', $version, 'v', "V$version", 'v9.9.9', 'vlatest')) {
            $bad = Invoke-Case $contract.Step.RunText @{ RELEASE_TAG = $tag } $state
            Assert-Case ($bad.Exit -ne 0 -and $bad.Output.Length -eq 0) "Invalid tag '$tag' emitted a version with prior $state."
        }
        foreach ($text in @($changelog.Replace("## [$version] -", "## [9.9.9] -"), $changelog.Replace('## [Unreleased]', "## [Unreleased]`n- Pending work."))) {
            Write-Fixture $changelogPath $text
            $bad = Invoke-Case $contract.Step.RunText @{} $state
            Assert-Case ($bad.Exit -ne 0 -and $bad.Output.Length -eq 0) 'Unfinalized or missing release was accepted.'
        }
        Write-Fixture $changelogPath $changelog
        Remove-Item -LiteralPath $changelogPath
        $bad = Invoke-Case $contract.Step.RunText @{} $state
        Assert-Case ($bad.Exit -ne 0 -and $bad.Output.Length -eq 0) 'Missing changelog file was accepted.'
        Write-Fixture $changelogPath $changelog
    }
    foreach ($path in @('', (Join-Path $scratch 'absent/output'), $fixture)) {
        $bad = Invoke-Case $contract.Step.RunText @{ GITHUB_OUTPUT = $path }
        Assert-Case ($bad.Exit -ne 0 -and $bad.Output.Length -eq 0) 'Missing or unwritable output path was accepted.'
    }

    # Resolve what the YAML actually maps; never manufacture the version output.
    $jobExpression = Get-OneValue ($validate.Lines -join "`n") '(?m)^      version: (?<value>.+)$'
    $jobVersion = Resolve-StepVersion $jobExpression $contract $good.Output
    $buildVersion = Resolve-StepVersion $build.Step.EnvironmentValues['RELEASE_VERSION'] $contract $good.Output
    $publishVersion = Resolve-JobVersion (Get-WorkflowScopeEnvironmentEntries -ScopeLines $publish.Lines -DirectIndent $publish.DirectIndent)['RELEASE_VERSION'] $jobVersion
    $releaseVersion = Resolve-JobVersion (Get-WorkflowScopeEnvironmentEntries -ScopeLines $releaseJob.Lines -DirectIndent $releaseJob.DirectIndent)['RELEASE_VERSION'] $jobVersion
    Assert-Case ($jobVersion -ceq $version -and $buildVersion -ceq $version -and $publishVersion -ceq $version -and $releaseVersion -ceq $version) 'Version mappings disagree.'
    foreach ($expression in @($jobExpression.Replace('release_contract', 'missing'), $jobExpression.Replace('outputs.version', 'outputs.missing'))) {
        Assert-Rejected { Resolve-StepVersion $expression $contract $good.Output } 'wrong step id/key'
    }
    Assert-Rejected { Resolve-StepVersion $jobExpression $contract '' } 'missing output'
    foreach ($expression in @('${{ needs.missing.outputs.version }}', '${{ needs.validate.outputs.missing }}')) {
        Assert-Rejected { Resolve-JobVersion $expression $jobVersion } 'wrong downstream mapping'
    }

    # Native gate results and genuine artifact validator failures stop the same block.
    $artifactDirectory = Join-Path $fixture 'artifacts/release'
    Assert-Case ([IO.Path]::GetFullPath($artifactDirectory).StartsWith($scratch + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) 'Artifact fixture escapes its temporary root.'
    foreach ($state in @('unset', 'zero', 'nonzero')) {
        foreach ($mode in @('success', 'failure', 'throw')) {
            foreach ($set in @('exact', 'missing-package', 'missing-symbols', 'extra', 'directory', 'case')) {
                if (Test-Path $artifactDirectory) { Remove-Item -LiteralPath $artifactDirectory -Recurse -Force }
                $result = Invoke-Case ($build.Step.RunText + "`n'AFTER_BLOCK'") @{ RELEASE_VERSION = $buildVersion; FIXTURE_GATE = $mode; FIXTURE_SET = $set } $state
                if ($mode -eq 'success' -and $set -eq 'exact') {
                    Assert-Case ($result.Exit -eq 0 -and $result.Messages.Contains('RELEASE_ARTIFACT_SET=PASS') -and $result.Messages.Contains('AFTER_BLOCK')) "Gate/artifact success stopped with prior $state."
                }
                else {
                    Assert-Stopped $result "Gate=$mode set=$set state=$state"
                    if ($mode -ne 'success') { Assert-Case (-not $result.Messages.Contains('RELEASE_ARTIFACT_SET=PASS')) 'Gate failure reached artifact success.' }
                }
            }
        }
    }
    # Reset exact artifacts through the same successful build block.
    if (Test-Path $artifactDirectory) { Remove-Item -LiteralPath $artifactDirectory -Recurse -Force }
    $built = Invoke-Case $build.Step.RunText @{ RELEASE_VERSION = $buildVersion }
    Assert-Case ($built.Exit -eq 0 -and $built.Calls.Contains("GATE version=$version")) 'Mapped build version failed.'
    foreach ($badVersion in @('', ' ', " $version", "$version ", 'latest', '9.9.9')) {
        Assert-Stopped (Invoke-Case $build.Step.RunText @{ RELEASE_VERSION = $badVersion }) "Build version '$badVersion'"
        Assert-Stopped (Invoke-Case $preflight.Step.RunText @{ RELEASE_VERSION = $badVersion }) "Preflight version '$badVersion'"
    }

    # Only the current three-job graph with default success conditions is supported.
    $publishNeeds = Get-OneValue ($publish.Lines -join "`n") '(?m)^    needs: (?<value>.+)$'
    $releaseNeeds = Get-OneValue ($releaseJob.Lines -join "`n") '(?m)^    needs: (?<value>.+)$'
    Assert-Case ($publishNeeds -ceq 'validate' -and $releaseNeeds -ceq '[validate, publish]') 'Release dependency graph changed.'
    foreach ($job in $jobs) {
        Assert-Case (-not (($job.Lines -join "`n") -match '(?m)^    (?:if|continue-on-error):')) 'Release jobs must retain default success conditions.'
        foreach ($step in @(Get-WorkflowSteps $job)) {
            Assert-Case (-not $step.HasIf -and -not $step.ContinueOnError) 'Release steps must retain fail-closed defaults.'
        }
    }
    foreach ($validateResult in @('success', 'failure', 'skipped', 'cancelled')) {
        foreach ($publishResult in @('success', 'failure', 'skipped', 'cancelled')) {
            $calls = [Collections.Generic.List[string]]::new()
            # Apply the documented default success rule to the verified needs edges.
            $results = @{ validate = $validateResult; publish = $publishResult }
            if ($results[$publishNeeds] -ceq 'success' -and $publishResult -notin @('skipped', 'cancelled')) {
                $checked = Invoke-Case $preflight.Step.RunText @{ RELEASE_VERSION = $publishVersion }
                if ($checked.Exit -eq 0) {
                    $authenticated = Invoke-Case ('Invoke-NestedPwsh -NoLogo -NoProfile -File ' + (Quote-Pwsh $authFixture))
                    Assert-Case ($authenticated.Exit -eq 0 -and $authenticated.Calls.Contains('AUTH_SUBSTITUTE')) 'Offline authentication process failed.'
                    $calls.Add($authenticated.Calls)
                    $pushed = Invoke-Case $push.Step.RunText @{ RELEASE_VERSION = $publishVersion; FIXTURE_NATIVE_EXIT = $(if ($publishResult -eq 'failure') { '41' } else { '0' }) }
                    $calls.Add($pushed.Calls)
                    Assert-Case ($pushed.Calls.Contains("nuget|push|artifacts/release/KeelMatrix.PackageSurface.$version.nupkg|")) 'Push did not consume the mapped version.'
                    if ($publishResult -eq 'failure') { Assert-Stopped $pushed 'Native push failure' }
                }
            }
            $needs = $releaseNeeds.Trim('[', ']') -split ',\s*'
            $canRelease = @($needs | Where-Object { $results[$_] -cne 'success' }).Count -eq 0
            if ($canRelease -and $runReleaseBash) {
                $released = Invoke-Case $createRelease.Step.RunText @{ RELEASE_VERSION = $releaseVersion } -Bash
                Assert-Case ($released.Exit -eq 0 -and $released.Calls.Contains("|v$version|artifacts/release/KeelMatrix.PackageSurface.$version.nupkg|artifacts/release/KeelMatrix.PackageSurface.$version.snupkg|")) "Release did not consume the mapped version/artifacts: $($released.Messages)"
                $calls.Add($released.Calls)
            }
            if ($validateResult -ne 'success') { Assert-Case ($calls.Count -eq 0) 'A failed/skipped/cancelled validation reached external substitutes despite present output.' }
            Assert-Case ($canRelease -eq ($validateResult -eq 'success' -and $publishResult -eq 'success')) 'Incomplete publication became a completed release.'
        }
    }
    foreach ($set in @('missing-package', 'missing-symbols', 'extra', 'directory', 'case')) {
        Remove-Item -LiteralPath $artifactDirectory -Recurse -Force
        $null = Invoke-Case $build.Step.RunText @{ FIXTURE_SET = $set }
        $checked = Invoke-Case ($preflight.Step.RunText + "`n'AUTH_SUBSTITUTE'")
        Assert-Stopped $checked "Downloaded set $set" 'AUTH_SUBSTITUTE'
        if ($runReleaseBash) {
            $released = Invoke-Case $createRelease.Step.RunText -Bash
            Assert-Case ($released.Exit -ne 0 -and $released.Calls.Length -eq 0) "Downloaded set $set reached release."
        }
        if ($set -eq 'missing-package') {
            $pushed = Invoke-Case $push.Step.RunText
            Assert-Case ($pushed.Exit -ne 0 -and $pushed.Calls.Length -eq 0) 'Missing package reached native push.'
        }
    }
    Remove-Item -LiteralPath $artifactDirectory -Recurse -Force
    $null = Invoke-Case $build.Step.RunText
    if ($runReleaseBash) {
        foreach ($badVersion in @('', ' ', " $version", 'latest', '9.9.9')) {
            $released = Invoke-Case $createRelease.Step.RunText @{ RELEASE_VERSION = $badVersion } -Bash
            Assert-Case ($released.Exit -ne 0 -and $released.Calls.Length -eq 0) 'Invalid version reached release.'
        }
    }
    $authFailure = Invoke-Case ('Invoke-NestedPwsh -NoLogo -NoProfile -File ' + (Quote-Pwsh $authFixture) + "`n" + 'if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }' + "`n" + $push.Step.RunText) @{ FIXTURE_AUTH_EXIT = '59' }
    Assert-Case ($authFailure.Exit -ne 0 -and $authFailure.Calls.Contains('AUTH_SUBSTITUTE') -and -not $authFailure.Calls.Contains('NATIVE')) 'Failed authentication reached push.'
    foreach ($text in @($preflight.Step.RunText, $push.Step.RunText)) {
        $bad = Invoke-Case $text @{ RELEASE_TAG = 'v9.9.9' }
        Assert-Case ($bad.Exit -ne 0 -and $bad.Calls.Length -eq 0) 'Mismatched tag reached auth/push.'
    }
    if ($runReleaseBash) {
        $failedRelease = Invoke-Case ($createRelease.Step.RunText + "`necho AFTER_BLOCK") @{ FIXTURE_NATIVE_EXIT = '61' } -Bash
        Assert-Stopped $failedRelease 'Native release command failure'
    }

    # Execute the real gate function and selected action ASTs, without running a build.
    $gateText = [IO.File]::ReadAllText((Join-Path $root 'scripts/run-local-gate.ps1'))
    $tokens = $null; $errors = $null
    $gateAst = [Management.Automation.Language.Parser]::ParseInput($gateText, [ref]$tokens, [ref]$errors)
    Assert-Case ($errors.Count -eq 0) 'Cannot parse the local gate.'
    $startupEnd = $gateText.IndexOf('Set-Location -LiteralPath $root', [StringComparison]::Ordinal)
    Assert-Case ($startupEnd -gt 0) 'Cannot locate the local gate startup boundary.'
    $startupPath = Join-Path $fixture 'scripts/startup.ps1'
    Write-Fixture $startupPath ($gateText.Substring(0, $startupEnd) + "`n'STARTUP_COMPLETE'")
    Write-Fixture (Join-Path $fixture 'build/Invoke-NestedPwsh.ps1') ([IO.File]::ReadAllText((Join-Path $root 'build/Invoke-NestedPwsh.ps1')))
    Write-Fixture (Join-Path $fixture 'build/Test-NestedPwshLaunch.ps1') @'
param([switch] $SelfTest)
[IO.File]::AppendAllText($env:FIXTURE_LOG, "GUARD self=$SelfTest`n")
$target = if ($SelfTest) { 'self' } else { 'scan' }
if ($env:FIXTURE_GUARD -eq "$target-failure") { exit 51 }
if ($env:FIXTURE_GUARD -eq "$target-throw") { throw 'Deliberate launch guard failure.' }
exit 0
'@
    foreach ($state in @('unset', 'zero', 'nonzero')) {
        foreach ($mode in @('success', 'self-failure', 'self-throw', 'scan-failure', 'scan-throw')) {
            $result = Invoke-Case ('Invoke-NestedPwsh -NoLogo -NoProfile -File ' + (Quote-Pwsh $startupPath)) @{ FIXTURE_GUARD = $mode } $state
            if ($mode -eq 'success') {
                Assert-Case ($result.Exit -eq 0 -and $result.Messages.Contains('STARTUP_COMPLETE') -and $result.Calls.Contains('GUARD self=False')) 'Startup success inherited stale shell state.'
            }
            else {
                Assert-Case ($result.Exit -ne 0 -and -not $result.Messages.Contains('STARTUP_COMPLETE') -and $result.Messages.Contains('Nested PowerShell launch guard')) 'Startup guard failure did not stop progression.'
                if ($mode.StartsWith('self-')) { Assert-Case (-not $result.Calls.Contains('GUARD self=False')) 'Failed self-test reached the guard scan.' }
            }
        }
    }
    $gateFunction = @($gateAst.FindAll({ param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -ceq 'Invoke-GateStep' }, $true))
    Assert-Case ($gateFunction.Count -eq 1) 'Expected one actual Invoke-GateStep function.'
    $actions = @($gateAst.FindAll({ param($n) $n -is [Management.Automation.Language.CommandAst] -and $n.GetCommandName() -ceq 'Invoke-GateStep' }, $true))
    foreach ($name in @('Release build', 'discovered CLI and probe tests with negative controls', 'release workflow execution regressions')) {
        $action = @($actions | Where-Object { $_.CommandElements[1].Value -ceq $name })
        Assert-Case ($action.Count -eq 1) "Gate must invoke '$name' exactly once."
        $body = $action[0].Extent.Text
        $gatePrefix = '. ' + (Quote-Pwsh (Join-Path $root 'build/Invoke-NestedPwsh.ps1')) + "`n" +
            'function dotnet { Invoke-NestedPwsh -NoLogo -NoProfile -File ' + (Quote-Pwsh $nativeFixture) + ' @args }' + "`n" +
            $gateFunction[0].Extent.Text + "`n" + '$root = ' + (Quote-Pwsh $fixture) + "`n" +
            '$solution = ''offline.sln''; $testDiscoveryScript = Join-Path $root ''scripts/test-discovered-tests.ps1''' + "`n"
        foreach ($script in @('test-discovered-tests.ps1', 'test-release-workflow-execution.ps1')) {
            Write-Fixture (Join-Path $fixture "scripts/$script") 'throw ''Deliberate assertion failure.'''
        }
        $result = Invoke-Case ($gatePrefix + $body + "`n'AFTER_BLOCK'") @{ FIXTURE_NATIVE_EXIT = '43' }
        Assert-Stopped $result "Required gate action $name"
        Assert-Case ($result.Messages -match 'Deliberate assertion failure|failed with exit code 43') "Gate action $name failed for an unexpected reason: $($result.Messages)"
        # Feed that same failed gate through the unchanged CI gate block.
        Write-Fixture (Join-Path $fixture 'scripts/run-local-gate.ps1') ($gatePrefix + $body)
        $result = Invoke-Case ($ciGate.Step.RunText + "`n'AFTER_BLOCK'") @{ FIXTURE_NATIVE_EXIT = '43' }
        Assert-Stopped $result "CI propagation for $name"
        Assert-Case ($result.Messages -match 'Deliberate assertion failure|failed with exit code 43') "CI action $name failed for an unexpected reason: $($result.Messages)"
    }
    foreach ($expected in @(0, 2)) {
        $text = $gateFunction[0].Extent.Text + "`nInvoke-GateStep 'expected code' { & dotnet offline } -ExpectedExitCode $expected`n'AFTER_BLOCK'"
        $result = Invoke-Case $text @{ FIXTURE_NATIVE_EXIT = [string]$expected }
        # -Command dot-sourcing reports 1 for a nonzero final exit. The gate's
        # captured native code and reached sentinel prove expected-code handling.
        Assert-Case ($result.Messages.Contains("COMMAND_EXIT_CODE: $expected") -and $result.Messages.Contains('AFTER_BLOCK')) 'Expected negative exit code was rejected by the gate.'
    }
    Assert-Stopped (Invoke-Case ($restore.Step.RunText + "`n'AFTER_BLOCK'") @{ FIXTURE_NATIVE_EXIT = '47' }) 'CI restore failure'
    $trx = Get-Step $ciJob 'Retain discovered test results'
    Assert-Case ($trx.Raw -match '(?m)^        if: always\(\)' -and $trx.Step.With['path'] -ceq 'artifacts/test-results/**/*.trx') 'Failed tests must retain TRX diagnostics.'

    # Prove these assertions distinguish the old branch and disconnected output.
    $old = $contract.Step.RunText.Replace('& pwsh -NoLogo -NoProfile -File ./scripts/validate-release.ps1', '& ./scripts/validate-release.ps1')
    Assert-Case ($old -cne $contract.Step.RunText) 'Could not construct the old-call negative control.'
    foreach ($state in @('unset', 'nonzero')) {
        $result = Invoke-Case $old @{} $state
        Assert-Rejected { Assert-Case ($result.Exit -eq 0 -and $result.Output -ceq "version=$version`n") 'Old exit-state defect.' } "old call with prior $state"
    }
    $withoutAppend = ($contract.Step.RunText -split "`n" | Where-Object { $_ -notmatch 'AppendAllText' }) -join "`n"
    $result = Invoke-Case $withoutAppend
    Assert-Rejected { Resolve-StepVersion $jobExpression $contract $result.Output } 'omitted output append'
    Write-Output "RELEASE_WORKFLOW_EXECUTION=PASS CASES=$caseCount"
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
