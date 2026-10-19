function Get-WorkflowIndent([string] $Line) {
    if ($Line -match '^ *\t') { throw 'Workflow YAML indentation must use spaces, not tabs.' }
    return ([regex]::Match($Line, '^ *')).Length
}

function Get-WorkflowJobs {
    param([Parameter(Mandatory)] [string] $WorkflowText)

    $lines = @($WorkflowText -split "`r?`n")
    $jobsHeaders = [Collections.Generic.List[int]]::new()
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if ([string]$lines[$index] -match '^jobs:\s*(?:#.*)?$') { $jobsHeaders.Add($index) }
    }
    if ($jobsHeaders.Count -ne 1) { throw 'Workflow must have one block-style jobs mapping at the document root.' }
    $jobsLineIndex = $jobsHeaders[0]

    $jobsEnd = $lines.Count
    for ($index = $jobsLineIndex + 1; $index -lt $lines.Count; $index++) {
        $line = [string]$lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        if ((Get-WorkflowIndent $line) -eq 0) { $jobsEnd = $index; break }
    }

    $jobIndent = -1
    $jobName = $null
    $jobLines = [Collections.Generic.List[string]]::new()
    $jobs = [Collections.Generic.List[object]]::new()
    $jobNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    for ($index = $jobsLineIndex + 1; $index -lt $jobsEnd; $index++) {
        $line = [string]$lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        $indent = Get-WorkflowIndent $line
        if ($jobIndent -lt 0) { $jobIndent = $indent }
        if ($indent -lt $jobIndent) { throw 'Workflow jobs mapping contains an unsupported indentation form.' }

        if ($indent -eq $jobIndent) {
            $jobMatch = [regex]::Match($line, '^(?<indent> *)(?<name>[A-Za-z0-9_-]+):\s*(?:#.*)?$')
            if (-not $jobMatch.Success) { throw 'Workflow jobs mapping contains an unsupported job key or flow-style value.' }
            if ($null -ne $jobName) {
                $directIndent = [int]::MaxValue
                foreach ($jobLine in $jobLines) {
                    if (-not [string]::IsNullOrWhiteSpace($jobLine) -and -not $jobLine.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
                        $directIndent = [Math]::Min($directIndent, (Get-WorkflowIndent $jobLine))
                    }
                }
                if ($directIndent -eq [int]::MaxValue) { throw "Workflow job '$jobName' has no mapping body." }
                $jobs.Add([pscustomobject]@{ Name = $jobName; Indent = $jobIndent; DirectIndent = $directIndent; Lines = $jobLines.ToArray() })
            }
            $jobName = $jobMatch.Groups['name'].Value
            if (-not $jobNames.Add($jobName)) { throw "Workflow job '$jobName' is declared more than once." }
            $jobLines = [Collections.Generic.List[string]]::new()
            continue
        }

        if ($null -eq $jobName) { throw 'Workflow jobs mapping has content before its first job key.' }
        $jobLines.Add($line)
    }

    if ($null -ne $jobName) {
        $directIndent = [int]::MaxValue
        foreach ($jobLine in $jobLines) {
            if (-not [string]::IsNullOrWhiteSpace($jobLine) -and -not $jobLine.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
                $directIndent = [Math]::Min($directIndent, (Get-WorkflowIndent $jobLine))
            }
        }
        if ($directIndent -eq [int]::MaxValue) { throw "Workflow job '$jobName' has no mapping body." }
        $jobs.Add([pscustomobject]@{ Name = $jobName; Indent = $jobIndent; DirectIndent = $directIndent; Lines = $jobLines.ToArray() })
    }
    if ($jobs.Count -eq 0) { throw 'Workflow jobs mapping is empty or uses an unsupported form.' }
    return $jobs.ToArray()
}

function Get-WorkflowScalarValue {
    param([Parameter(Mandatory)] [string] $Value, [Parameter(Mandatory)] [string] $Context)

    $trimmed = $Value.Trim()
    if ($trimmed.StartsWith("'", [StringComparison]::Ordinal)) {
        $single = [regex]::Match($trimmed, "^'(?<value>(?:[^']|'')*)'(?:\s+#.*)?$")
        if (-not $single.Success) { throw "Workflow $Context uses an unsupported single-quoted scalar." }
        return $single.Groups['value'].Value.Replace("''", "'")
    }
    if ($trimmed.StartsWith('"', [StringComparison]::Ordinal)) {
        $double = [regex]::Match($trimmed, '^"(?<value>(?:\\.|[^"\\])*)"(?:\s+#.*)?$')
        if (-not $double.Success) { throw "Workflow $Context uses an unsupported double-quoted scalar." }
        try { return [string](ConvertFrom-Json -InputObject ('"' + $double.Groups['value'].Value + '"')) }
        catch { throw "Workflow $Context has an invalid double-quoted scalar." }
    }

    $plain = [regex]::Replace($trimmed, '\s+#.*$', '').Trim()
    if ($plain.StartsWith('[', [StringComparison]::Ordinal) -or $plain.StartsWith('{', [StringComparison]::Ordinal) -or
        $plain.StartsWith('&', [StringComparison]::Ordinal) -or $plain.StartsWith('*', [StringComparison]::Ordinal) -or
        $plain.StartsWith('!', [StringComparison]::Ordinal)) {
        throw "Workflow $Context uses an unsupported flow, anchor, alias, or tagged scalar."
    }
    return $plain
}

function Get-WorkflowDefaultWorkingDirectory {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]] $ScopeLines, [Parameter(Mandatory)] [int] $DirectIndent)

    $defaultsIndex = -1
    for ($index = 0; $index -lt $ScopeLines.Count; $index++) {
        $match = [regex]::Match([string]$ScopeLines[$index], '^(?<indent> *)defaults:\s*(?<value>.*)$')
        if ($match.Success -and $match.Groups['indent'].Value.Length -eq $DirectIndent) {
            if ($defaultsIndex -ge 0) { throw 'Workflow scope has duplicate defaults mappings.' }
            if (-not [string]::IsNullOrWhiteSpace($match.Groups['value'].Value) -and $match.Groups['value'].Value -notmatch '^#') {
                throw 'Workflow defaults must use block-style YAML mappings.'
            }
            $defaultsIndex = $index
        }
    }
    if ($defaultsIndex -lt 0) { return $null }

    $defaultsEnd = $ScopeLines.Count
    for ($index = $defaultsIndex + 1; $index -lt $ScopeLines.Count; $index++) {
        $line = [string]$ScopeLines[$index]
        if (-not [string]::IsNullOrWhiteSpace($line) -and -not $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal) -and
            (Get-WorkflowIndent $line) -le $DirectIndent) { $defaultsEnd = $index; break }
    }

    $runIndex = -1
    $runIndent = -1
    for ($index = $defaultsIndex + 1; $index -lt $defaultsEnd; $index++) {
        $run = [regex]::Match([string]$ScopeLines[$index], '^(?<indent> +)run:\s*(?<value>.*)$')
        if ($run.Success -and $run.Groups['indent'].Value.Length -gt $DirectIndent) {
            if ($runIndex -ge 0) { throw 'Workflow defaults contain duplicate run mappings.' }
            if (-not [string]::IsNullOrWhiteSpace($run.Groups['value'].Value) -and $run.Groups['value'].Value -notmatch '^#') {
                throw 'Workflow defaults.run must use a block-style YAML mapping.'
            }
            $runIndex = $index
            $runIndent = $run.Groups['indent'].Value.Length
        }
    }
    if ($runIndex -lt 0) { return $null }

    $workingDirectories = [Collections.Generic.List[string]]::new()
    for ($index = $runIndex + 1; $index -lt $defaultsEnd; $index++) {
        $directory = [regex]::Match([string]$ScopeLines[$index], '^(?<indent> +)working-directory:\s*(?<value>.*)$')
        if ($directory.Success -and $directory.Groups['indent'].Value.Length -gt $runIndent) {
            $workingDirectories.Add((Get-WorkflowScalarValue -Value $directory.Groups['value'].Value -Context 'defaults.run.working-directory'))
        }
    }
    if ($workingDirectories.Count -gt 1) { throw 'Workflow defaults.run has duplicate working-directory keys.' }
    if ($workingDirectories.Count -eq 0) { return $null }
    return $workingDirectories[0]
}

function ConvertTo-WorkflowStep {
    param(
        [Parameter(Mandatory)] [int] $StepNumber,
        [Parameter(Mandatory)] [int] $SequenceIndent,
        [AllowEmptyString()] [string] $FirstRest,
        [Parameter(Mandatory)] [string[]] $Lines,
        [Parameter(Mandatory)] [string] $JobName
    )

    $mapIndent = $SequenceIndent + 2
    $fields = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::Ordinal)
    $withFields = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::Ordinal)
    $runParts = [Collections.Generic.List[string]]::new()
    $lastField = $null
    $runBlock = $false

    $fieldLines = [Collections.Generic.List[string]]::new()
    if (-not [string]::IsNullOrWhiteSpace($FirstRest)) { $fieldLines.Add((' ' * $mapIndent) + $FirstRest) }
    foreach ($line in $Lines) { $fieldLines.Add($line) }

    foreach ($line in $fieldLines) {
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            if ($runBlock -and $lastField -eq 'run') { $runParts.Add('') }
            continue
        }
        $indent = Get-WorkflowIndent $line
        if ($indent -eq $mapIndent) {
            $entry = [regex]::Match($line, '^(?<indent> *)(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $entry.Success) { throw "Workflow job '$JobName' step $StepNumber has an unsupported step field." }
            $key = $entry.Groups['key'].Value
            if ($fields.ContainsKey($key)) { throw "Workflow job '$JobName' step $StepNumber repeats '$key'." }
            $value = $entry.Groups['value'].Value
            if ($key -eq 'with' -and -not [string]::IsNullOrWhiteSpace($value) -and $value -notmatch '^#') {
                throw "Workflow job '$JobName' step $StepNumber uses an unsupported flow-style with mapping."
            }
            $fields.Add($key, $value)
            $lastField = $key
            $runBlock = $key -eq 'run' -and $value.Trim() -match '^[|>](?:[1-9])?[+-]?(?:\s+#.*)?$'
            if ($key -eq 'run') {
                if ([string]::IsNullOrWhiteSpace($value)) { throw "Workflow job '$JobName' step $StepNumber has an unsupported empty run value." }
                if (-not $runBlock -and $value.TrimStart() -match '^[\[\{&*!]') { throw "Workflow job '$JobName' step $StepNumber uses an unsupported run alias or flow scalar." }
                if (-not $runBlock) { $runParts.Add($value) }
            }
            continue
        }

        if ($indent -lt $mapIndent) { throw "Workflow job '$JobName' step $StepNumber has an unsupported nested mapping." }
        if ($lastField -eq 'with') {
            if ($indent -ne ($mapIndent + 2)) { throw "Workflow job '$JobName' step $StepNumber uses an unsupported nested with mapping." }
            $entry = [regex]::Match($line, '^(?<indent> *)(?<key>[A-Za-z0-9_-]+):\s*(?<value>.*)$')
            if (-not $entry.Success) { throw "Workflow job '$JobName' step $StepNumber has an unsupported with value." }
            $withKey = $entry.Groups['key'].Value
            if ($withFields.ContainsKey($withKey)) { throw "Workflow job '$JobName' step $StepNumber repeats with.$withKey." }
            $withFields.Add($withKey, $entry.Groups['value'].Value)
            continue
        }
        if ($lastField -eq 'run' -and $runBlock) { $runParts.Add($line.Substring($mapIndent + 1)); continue }
        if ($lastField -eq 'run') { throw "Workflow job '$JobName' step $StepNumber uses an unsupported multiline run value." }
    }

    $uses = $null
    if ($fields.ContainsKey('uses')) { $uses = Get-WorkflowScalarValue -Value ([string]$fields['uses']) -Context "job '$JobName' step $StepNumber uses" }
    $workingDirectory = $null
    if ($fields.ContainsKey('working-directory')) {
        $workingDirectory = Get-WorkflowScalarValue -Value ([string]$fields['working-directory']) -Context "job '$JobName' step $StepNumber working-directory"
    }
    return [pscustomobject]@{
        Uses = $uses
        HasIf = $fields.ContainsKey('if')
        With = $withFields
        WorkingDirectory = $workingDirectory
        RunText = $runParts -join [Environment]::NewLine
    }
}

function Get-WorkflowSteps {
    param([Parameter(Mandatory)] [object] $Job)

    foreach ($line in $Job.Lines) {
        $jobUses = [regex]::Match([string]$line, '^(?<indent> *)uses:\s*(?<value>.*)$')
        if ($jobUses.Success -and $jobUses.Groups['indent'].Value.Length -eq $Job.DirectIndent) {
            throw "Workflow job '$($Job.Name)' uses an unsupported reusable workflow form."
        }
    }

    $stepsHeaderIndex = -1
    for ($index = 0; $index -lt $Job.Lines.Count; $index++) {
        $match = [regex]::Match([string]$Job.Lines[$index], '^(?<indent> *)steps:\s*(?<value>.*)$')
        if ($match.Success -and $match.Groups['indent'].Value.Length -eq $Job.DirectIndent) {
            if ($stepsHeaderIndex -ge 0) { throw "Workflow job '$($Job.Name)' has duplicate steps mappings." }
            if (-not [string]::IsNullOrWhiteSpace($match.Groups['value'].Value) -and $match.Groups['value'].Value -notmatch '^#') {
                throw "Workflow job '$($Job.Name)' uses an unsupported flow-style steps value."
            }
            $stepsHeaderIndex = $index
        }
    }
    if ($stepsHeaderIndex -lt 0) { return @() }

    $sequenceIndent = -1
    $stepsEnd = $Job.Lines.Count
    for ($index = $stepsHeaderIndex + 1; $index -lt $Job.Lines.Count; $index++) {
        $line = [string]$Job.Lines[$index]
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        $indent = Get-WorkflowIndent $line
        if ($indent -le $Job.DirectIndent) { $stepsEnd = $index; break }
        if ($sequenceIndent -lt 0) { $sequenceIndent = $indent }
    }
    if ($sequenceIndent -lt 0) { throw "Workflow job '$($Job.Name)' has an empty or unsupported steps list." }

    $steps = [Collections.Generic.List[object]]::new()
    $stepLines = [Collections.Generic.List[string]]::new()
    $stepRest = ''
    $stepNumber = 0
    for ($index = $stepsHeaderIndex + 1; $index -le $stepsEnd; $index++) {
        $line = if ($index -lt $stepsEnd) { [string]$Job.Lines[$index] } else { '' }
        $marker = [regex]::Match($line, '^(?<indent> *)-(?:[ ]+(?<rest>.*))?\s*$')
        if ($marker.Success -and $marker.Groups['indent'].Value.Length -eq $sequenceIndent) {
            if ($stepNumber -gt 0) {
                $steps.Add((ConvertTo-WorkflowStep -StepNumber $stepNumber -SequenceIndent $sequenceIndent -FirstRest $stepRest -Lines $stepLines.ToArray() -JobName $Job.Name))
            }
            $stepNumber++
            $stepRest = $marker.Groups['rest'].Value
            $stepLines = [Collections.Generic.List[string]]::new()
            continue
        }
        if ($index -eq $stepsEnd) { break }
        if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) {
            $stepLines.Add($line)
            continue
        }
        if ((Get-WorkflowIndent $line) -lt $sequenceIndent) { throw "Workflow job '$($Job.Name)' has an unsupported steps indentation form." }
        if ($stepNumber -eq 0) { throw "Workflow job '$($Job.Name)' has content before its first step." }
        $stepLines.Add($line)
    }
    if ($stepNumber -gt 0) {
        $steps.Add((ConvertTo-WorkflowStep -StepNumber $stepNumber -SequenceIndent $sequenceIndent -FirstRest $stepRest -Lines $stepLines.ToArray() -JobName $Job.Name))
    }
    return $steps.ToArray()
}

function Resolve-WorkflowRelativePath {
    param([AllowEmptyString()] [string] $BasePath, [Parameter(Mandatory)] [AllowEmptyString()] [string] $RelativePath)

    if ($null -eq $RelativePath -or $RelativePath -match '\$\{\{' -or $RelativePath -match '^(?:/|[A-Za-z]:[\\/])') { return $null }
    $parts = [Collections.Generic.List[string]]::new()
    foreach ($component in (@($BasePath, $RelativePath) -join '/') -split '/') {
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

function Assert-ArtifactScriptConsumersHaveSource {
    param(
        [Parameter(Mandatory)] [object[]] $Workflows,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [string[]] $RequiredConsumerNames = @()
    )

    $trackedFiles = @(& git -C $RepositoryRoot ls-files)
    if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files for workflow script checks.' }
    $tracked = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($file in $trackedFiles) { [void]$tracked.Add(([string]$file).Replace('\', '/')) }
    $consumers = [Collections.Generic.List[object]]::new()
    $scriptPattern = '(?i)(?<path>(?:[A-Za-z0-9_.-]+[\\/])*scripts[\\/][A-Za-z0-9_.-]+(?:[\\/][A-Za-z0-9_.-]+)*\.[A-Za-z0-9]+)'

    foreach ($workflow in $Workflows) {
        $workflowLines = @(([string]$workflow.Text) -split "`r?`n")
        $jobsLineIndex = -1
        for ($index = 0; $index -lt $workflowLines.Count; $index++) {
            if ([string]$workflowLines[$index] -match '^jobs:\s*(?:#.*)?$') { $jobsLineIndex = $index; break }
        }
        if ($jobsLineIndex -lt 0) { throw "Workflow '$($workflow.Name)' has no root jobs mapping." }
        $workflowWorkingDirectory = $null
        if ($jobsLineIndex -gt 0) {
            $workflowScope = @($workflowLines[0..($jobsLineIndex - 1)])
            $workflowWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $workflowScope -DirectIndent 0
        }

        $parsedJobs = [Collections.Generic.List[object]]::new()
        $runTexts = [Collections.Generic.List[string]]::new()
        foreach ($candidateJob in @(Get-WorkflowJobs -WorkflowText $workflow.Text)) {
            $candidateSteps = @(Get-WorkflowSteps -Job $candidateJob)
            $parsedJobs.Add([pscustomobject]@{ Job = $candidateJob; Steps = $candidateSteps })
            foreach ($candidateStep in $candidateSteps) {
                if (-not [string]::IsNullOrWhiteSpace($candidateStep.RunText)) { $runTexts.Add($candidateStep.RunText) }
            }
        }
        $workflowScriptReferenceCount = [regex]::Matches([string]$workflow.Text, '(?i)scripts[\\/]').Count
        $runScriptReferenceCount = 0
        foreach ($runText in $runTexts) { $runScriptReferenceCount += [regex]::Matches($runText, '(?i)scripts[\\/]').Count }
        if ($workflowScriptReferenceCount -ne $runScriptReferenceCount) {
            throw "Workflow '$($workflow.Name)' has script path references outside supported run commands."
        }

        foreach ($parsedJob in $parsedJobs) {
            $job = $parsedJob.Job
            $steps = @($parsedJob.Steps)
            $downloadIndexes = [Collections.Generic.List[int]]::new()
            for ($index = 0; $index -lt $steps.Count; $index++) {
                if ($null -ne $steps[$index].Uses -and $steps[$index].Uses -match '(?i)^actions/download-artifact@') {
                    if ($steps[$index].HasIf) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has a conditional artifact download." }
                    $downloadIndexes.Add($index)
                }
            }
            if ($downloadIndexes.Count -eq 0) { continue }

            $jobWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $job.Lines -DirectIndent $job.DirectIndent
            if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = $workflowWorkingDirectory }
            if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = '.' }
            $resolvedJobWorkingDirectory = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $jobWorkingDirectory
            if ($null -eq $resolvedJobWorkingDirectory) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported run working-directory." }

            $scriptPaths = [Collections.Generic.List[string]]::new()
            for ($stepIndex = 0; $stepIndex -lt $steps.Count; $stepIndex++) {
                $step = $steps[$stepIndex]
                if ($null -ne $step.Uses -and ($step.Uses.StartsWith('./', [StringComparison]::Ordinal) -or $step.Uses.StartsWith('.\', [StringComparison]::Ordinal))) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' uses a local action whose script calls are unsupported by the workflow guard."
                }
                if ([string]::IsNullOrWhiteSpace($step.RunText)) { continue }
                $precedingDownloads = @($downloadIndexes | Where-Object { $_ -lt $stepIndex })
                if ($precedingDownloads.Count -eq 0) { continue }

                $scriptMatches = [regex]::Matches($step.RunText, $scriptPattern)
                if ($step.RunText -match '(?i)scripts[\\/]' -and $scriptMatches.Count -eq 0) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' contains an unsupported dynamic script path."
                }
                if ($scriptMatches.Count -eq 0) { continue }

                $stepWorkingDirectory = if ($null -ne $step.WorkingDirectory) { $step.WorkingDirectory } else { $resolvedJobWorkingDirectory }
                $resolvedStepWorkingDirectory = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $stepWorkingDirectory
                if ($null -eq $resolvedStepWorkingDirectory) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported step working-directory." }
                $downloadIndex = ($precedingDownloads | Measure-Object -Maximum).Maximum

                $validCheckout = $false
                $checkoutFailures = [Collections.Generic.List[string]]::new()
                for ($checkoutIndex = 0; $checkoutIndex -lt $downloadIndex; $checkoutIndex++) {
                    $checkout = $steps[$checkoutIndex]
                    if ($null -eq $checkout.Uses -or $checkout.Uses -notmatch '(?i)^actions/checkout@') { continue }
                    if ($checkout.HasIf) { $checkoutFailures.Add('checkout is conditional'); continue }

                    $checkoutPath = if ($checkout.With.ContainsKey('path')) {
                        Get-WorkflowScalarValue -Value $checkout.With['path'] -Context "job '$($workflow.Name)/$($job.Name)' checkout path"
                    }
                    else { '.' }
                    # Keep accepted spellings portable across Windows and POSIX runners.
                    # Backslashes and mixed separators can resolve to child paths on POSIX.
                    if ($checkoutPath -notin @('.', './', './.')) { $checkoutFailures.Add("checkout path '$checkoutPath' is not a portable workspace-root path"); continue }

                    $checkoutRepository = if ($checkout.With.ContainsKey('repository')) {
                        Get-WorkflowScalarValue -Value $checkout.With['repository'] -Context "job '$($workflow.Name)/$($job.Name)' checkout repository"
                    }
                    else { '${{ github.repository }}' }
                    if ($checkoutRepository -notmatch '^\$\{\{\s*github\.repository\s*\}\}$') { $checkoutFailures.Add('checkout does not select github.repository'); continue }

                    $checkoutRef = if ($checkout.With.ContainsKey('ref')) {
                        Get-WorkflowScalarValue -Value $checkout.With['ref'] -Context "job '$($workflow.Name)/$($job.Name)' checkout ref"
                    }
                    else { '' }
                    if ($checkoutRef -notmatch '^\$\{\{\s*github\.sha\s*\}\}$') { $checkoutFailures.Add('checkout does not pin github.sha'); continue }

                    $validCheckout = $true
                    break
                }
                if (-not $validCheckout) {
                    if ($checkoutFailures.Count -gt 0) {
                        throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has no unconditional workspace-root checkout of github.sha before artifact download: $($checkoutFailures -join '; ')."
                    }
                    $anyCheckout = @($steps | Where-Object { $null -ne $_.Uses -and $_.Uses -match '(?i)^actions/checkout@' }).Count -gt 0
                    if ($anyCheckout) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' checks out source after artifact download." }
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' invokes a tracked repository script without checkout."
                }

                foreach ($scriptMatch in $scriptMatches) {
                    $invocationPath = $scriptMatch.Groups['path'].Value.Replace('\', '/')
                    $scriptIndex = $invocationPath.LastIndexOf('scripts/', [StringComparison]::OrdinalIgnoreCase)
                    if ($scriptIndex -lt 0) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported repository script path." }
                    $relativePath = $invocationPath.Substring($scriptIndex)
                    if (-not $tracked.Contains($relativePath)) { throw "Artifact consumer invokes repository script '$relativePath' that is not tracked." }
                    $absoluteScript = Join-Path $RepositoryRoot ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    if (-not (Test-Path -LiteralPath $absoluteScript -PathType Leaf)) { throw "Artifact consumer invokes missing repository script '$relativePath'." }

                    $resolvedInvocation = Resolve-WorkflowRelativePath -BasePath $resolvedStepWorkingDirectory -RelativePath $invocationPath
                    if ($null -eq $resolvedInvocation -or -not $resolvedInvocation.Equals($relativePath, [StringComparison]::Ordinal)) {
                        throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' does not invoke '$relativePath' from its workspace-root checkout."
                    }
                    if (-not $scriptPaths.Contains($relativePath)) { $scriptPaths.Add($relativePath) }
                }
            }

            if ($scriptPaths.Count -gt 0) {
                $consumers.Add([pscustomobject]@{
                    Name = $job.Name
                    Workflow = $workflow.Name
                    Scripts = @($scriptPaths | Sort-Object -Unique)
                })
            }
        }
    }

    foreach ($requiredName in $RequiredConsumerNames) {
        if (-not @($consumers | Where-Object { "$($_.Workflow)/$($_.Name)" -eq $requiredName })) {
            throw "Expected artifact-consuming repository-script job '$requiredName' was not enumerated."
        }
    }
    return $consumers.ToArray()
}
