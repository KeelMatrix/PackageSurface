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
    $shell = $null
    if ($fields.ContainsKey('shell')) { $shell = Get-WorkflowScalarValue -Value ([string]$fields['shell']) -Context "job '$JobName' step $StepNumber shell" }
    $continueOnError = $null
    if ($fields.ContainsKey('continue-on-error')) {
        $continueOnError = Get-WorkflowScalarValue -Value ([string]$fields['continue-on-error']) -Context "job '$JobName' step $StepNumber continue-on-error"
    }
    return [pscustomobject]@{
        Uses = $uses
        HasIf = $fields.ContainsKey('if')
        With = $withFields
        WorkingDirectory = $workingDirectory
        Shell = $shell
        ContinueOnError = $continueOnError
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

function Get-WorkflowCheckoutFailure {
    param([Parameter(Mandatory)] [object] $Checkout, [Parameter(Mandatory)] [string] $Context)

    $failures = [Collections.Generic.List[string]]::new()
    if ($null -ne $Checkout.ContinueOnError -and $Checkout.ContinueOnError -notmatch '^(?i:false|no|0)$') {
        $failures.Add('checkout can fail and continue without establishing source')
    }
    $checkoutPath = if ($Checkout.With.ContainsKey('path')) {
        Get-WorkflowScalarValue -Value $Checkout.With['path'] -Context "$Context checkout path"
    }
    else { '.' }
    # Keep accepted spellings portable across Windows and POSIX runners.
    # Backslashes and mixed separators can resolve to child paths on POSIX.
    if ($checkoutPath -notin @('.', './', './.')) { $failures.Add("checkout path '$checkoutPath' is not a portable workspace-root path") }

    $checkoutRepository = if ($Checkout.With.ContainsKey('repository')) {
        Get-WorkflowScalarValue -Value $Checkout.With['repository'] -Context "$Context checkout repository"
    }
    else { '${{ github.repository }}' }
    if ($checkoutRepository -notmatch '^\$\{\{\s*github\.repository\s*\}\}$') { $failures.Add('checkout does not select github.repository') }

    $checkoutRef = if ($Checkout.With.ContainsKey('ref')) {
        Get-WorkflowScalarValue -Value $Checkout.With['ref'] -Context "$Context checkout ref"
    }
    else { '' }
    if ($checkoutRef -notmatch '^\$\{\{\s*github\.sha\s*\}\}$') { $failures.Add('checkout does not pin github.sha') }

    return ($failures -join '; ')
}

function ConvertFrom-WorkflowShellToken {
    param([Parameter(Mandatory)] [string] $Token)

    $trimmed = $Token.Trim()
    if ($trimmed.Length -ge 2 -and
        (($trimmed.StartsWith('"', [StringComparison]::Ordinal) -and $trimmed.EndsWith('"', [StringComparison]::Ordinal)) -or
            ($trimmed.StartsWith("'", [StringComparison]::Ordinal) -and $trimmed.EndsWith("'", [StringComparison]::Ordinal)))) {
        return $trimmed.Substring(1, $trimmed.Length - 2)
    }
    return $trimmed
}

function Get-WorkflowShellTokens {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $tokens = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text, '"[^"`r`n]*"|''[^''`r`n]*''|[^\s;&|]+')) {
        $tokens.Add($match.Value)
    }
    return $tokens.ToArray()
}

function Test-WorkflowRootWorkingDirectory {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory)

    $resolved = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $WorkingDirectory
    return $null -ne $resolved -and $resolved.Length -eq 0
}

function Test-WorkflowRootGitDirectory {
    param(
        [Parameter(Mandatory)] [string] $GitDirectoryToken,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory,
        [AllowNull()] [string] $Shell
    )

    $value = ConvertFrom-WorkflowShellToken -Token $GitDirectoryToken
    if ($value -match '^\$\{\{\s*github\.workspace\s*\}\}$') { return $true }
    if ($GitDirectoryToken -notmatch "^'") {
        if ($Shell -match '^(?i:bash|sh)$' -and $value -in @('$GITHUB_WORKSPACE', '${GITHUB_WORKSPACE}')) { return $true }
        if ($Shell -match '^(?i:pwsh|powershell)$' -and $value -match '^\$env:GITHUB_WORKSPACE$') { return $true }
    }
    if ($value -notin @('.', './', './.')) { return $false }
    return Test-WorkflowRootWorkingDirectory -WorkingDirectory $WorkingDirectory
}

function Test-WorkflowPinnedGitRef {
    param([Parameter(Mandatory)] [string] $RefToken, [AllowNull()] [string] $Shell)

    $value = ConvertFrom-WorkflowShellToken -Token $RefToken
    if ($value -match '^\$\{\{\s*github\.sha\s*\}\}$') { return $true }
    if ($RefToken -match "^'") { return $false }
    if ($Shell -match '^(?i:bash|sh)$') { return $value -in @('$GITHUB_SHA', '${GITHUB_SHA}') }
    if ($Shell -match '^(?i:pwsh|powershell)$') { return $value -match '^\$env:GITHUB_SHA$' }
    return $false
}

function Test-WorkflowStandalonePinnedGitRestore {
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory
    )

    $runText = ([string]$Step.RunText).Trim()
    if ([string]::IsNullOrWhiteSpace($runText) -or $runText -match '[\r\n;&|]') { return $false }
    if ($runText -notmatch '^(?i:(?:&\s*)?git(?:\.exe)?)\s+') { return $false }

    $words = @(Get-WorkflowShellTokens -Text ($runText -replace '^(?i:(?:&\s*)?git(?:\.exe)?)\s+', ''))
    if ($words.Count -eq 0) { return $false }
    $index = 0
    $gitDirectory = $null
    if ((ConvertFrom-WorkflowShellToken -Token $words[$index]) -eq '-C') {
        if ($words.Count -lt 3) { return $false }
        $gitDirectory = $words[$index + 1]
        $index += 2
    }
    elseif ((ConvertFrom-WorkflowShellToken -Token $words[$index]).StartsWith('-', [StringComparison]::Ordinal)) {
        return $false
    }

    if ($gitDirectory) {
        if (-not (Test-WorkflowRootGitDirectory -GitDirectoryToken $gitDirectory -WorkingDirectory $WorkingDirectory -Shell $Step.Shell)) { return $false }
    }
    elseif (-not (Test-WorkflowRootWorkingDirectory -WorkingDirectory $WorkingDirectory)) { return $false }

    $verb = (ConvertFrom-WorkflowShellToken -Token $words[$index]).ToLowerInvariant()
    $arguments = @($words | Select-Object -Skip ($index + 1) | ForEach-Object { ConvertFrom-WorkflowShellToken -Token $_ })
    if ($verb -eq 'checkout') {
        if ($arguments.Count -eq 1) { $refToken = $words[$index + 1] }
        elseif ($arguments.Count -eq 2 -and $arguments[0] -eq '--detach') { $refToken = $words[$index + 2] }
        else { return $false }
    }
    elseif ($verb -eq 'switch') {
        if ($arguments.Count -ne 2 -or $arguments[0] -ne '--detach') { return $false }
        $refToken = $words[$index + 2]
    }
    else { return $false }

    return Test-WorkflowPinnedGitRef -RefToken $refToken -Shell $Step.Shell
}

function Get-WorkflowRunSourceEffect {
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory
    )

    $runText = [string]$Step.RunText
    $gitMatches = [regex]::Matches($runText, '(?i)(?<![A-Za-z0-9_.-])git(?:\.exe)?(?=\s|$)')
    $hasSourceMutation = $false
    $nonSourceChangingGitVerbs = @('status', 'log', 'diff', 'show', 'rev-parse', 'ls-files', 'describe', 'cat-file', 'fetch')
    $sourceChangingGitVerbs = @('checkout', 'switch', 'restore', 'reset', 'pull', 'merge', 'rebase', 'cherry-pick', 'revert', 'am', 'apply', 'read-tree', 'clean', 'worktree', 'submodule', 'clone')

    foreach ($match in $gitMatches) {
        $tail = $runText.Substring($match.Index + $match.Length)
        $separator = [regex]::Match($tail, '[;&|\r\n]')
        if ($separator.Success) { $tail = $tail.Substring(0, $separator.Index) }
        $words = @(Get-WorkflowShellTokens -Text $tail)
        if ($words.Count -eq 0) { $hasSourceMutation = $true; continue }
        $index = 0
        if ((ConvertFrom-WorkflowShellToken -Token $words[$index]) -eq '-C') {
            if ($words.Count -lt 3) { $hasSourceMutation = $true; continue }
            $index += 2
        }
        elseif ((ConvertFrom-WorkflowShellToken -Token $words[$index]).StartsWith('-', [StringComparison]::Ordinal)) {
            $hasSourceMutation = $true
            continue
        }

        $verb = (ConvertFrom-WorkflowShellToken -Token $words[$index]).ToLowerInvariant()
        if ($verb -in $sourceChangingGitVerbs -or $verb -notin $nonSourceChangingGitVerbs) { $hasSourceMutation = $true }
    }

    if ($runText -match '(?i)(?<![A-Za-z0-9_.-])(?:hg\s+(?:update|checkout|clone|pull)|svn\s+(?:switch|checkout|update)|gh\s+repo\s+clone)\b') {
        $hasSourceMutation = $true
    }
    if (-not $hasSourceMutation) { return 'None' }
    if ($gitMatches.Count -gt 0 -and (Test-WorkflowStandalonePinnedGitRestore -Step $Step -WorkingDirectory $WorkingDirectory)) { return 'Restore' }
    return 'Mutation'
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
                $effectiveCheckoutValid = $false
                $effectiveCheckoutFailure = 'no checkout has established a pinned workspace-root source'
                $latestDownloadHadValidSource = $false
                $latestDownloadFailure = $effectiveCheckoutFailure
                for ($stateIndex = 0; $stateIndex -le $stepIndex; $stateIndex++) {
                    $stateStep = $steps[$stateIndex]
                    if ($null -ne $stateStep.Uses -and $stateStep.Uses -match '(?i)^actions/download-artifact@') {
                        $latestDownloadHadValidSource = $effectiveCheckoutValid
                        $latestDownloadFailure = $effectiveCheckoutFailure
                        continue
                    }
                    if ($null -ne $stateStep.Uses -and $stateStep.Uses -match '(?i)^actions/checkout@') {
                        $checkoutFailure = Get-WorkflowCheckoutFailure -Checkout $stateStep -Context "job '$($workflow.Name)/$($job.Name)'"
                        if ($stateStep.HasIf) {
                            if (-not $effectiveCheckoutValid -or -not [string]::IsNullOrWhiteSpace($checkoutFailure)) {
                                $effectiveCheckoutValid = $false
                                $effectiveCheckoutFailure = if ([string]::IsNullOrWhiteSpace($checkoutFailure)) {
                                    'conditional checkout can leave the source state unchanged'
                                }
                                else { "conditional checkout may replace the pinned source: $checkoutFailure" }
                            }
                        }
                        else {
                            $effectiveCheckoutValid = [string]::IsNullOrWhiteSpace($checkoutFailure)
                            $effectiveCheckoutFailure = if ($effectiveCheckoutValid) {
                                ''
                            }
                            else { $checkoutFailure }
                        }
                        continue
                    }

                    if ([string]::IsNullOrWhiteSpace($stateStep.RunText)) { continue }
                    $stateWorkingDirectory = if ($null -ne $stateStep.WorkingDirectory) { $stateStep.WorkingDirectory } else { $resolvedJobWorkingDirectory }
                    $resolvedStateWorkingDirectory = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $stateWorkingDirectory
                    if ($null -eq $resolvedStateWorkingDirectory) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported run working-directory." }
                    $sourceEffect = Get-WorkflowRunSourceEffect -Step $stateStep -WorkingDirectory $resolvedStateWorkingDirectory
                    if ($sourceEffect -eq 'Restore') {
                        $restoreCanFailAndContinue = $null -ne $stateStep.ContinueOnError -and $stateStep.ContinueOnError -notmatch '^(?i:false|no|0)$'
                        if ($restoreCanFailAndContinue) {
                            $effectiveCheckoutValid = $false
                            $effectiveCheckoutFailure = 'continue-on-error shell checkout cannot establish source'
                        }
                        elseif ($stateStep.HasIf) {
                            if (-not $effectiveCheckoutValid) {
                                $effectiveCheckoutValid = $false
                                $effectiveCheckoutFailure = 'conditional shell checkout cannot restore an invalid source state'
                            }
                        }
                        else {
                            $effectiveCheckoutValid = $true
                            $effectiveCheckoutFailure = ''
                        }
                    }
                    elseif ($sourceEffect -eq 'Mutation') {
                        $effectiveCheckoutValid = $false
                        $effectiveCheckoutFailure = 'a run step contains a source-changing or unsupported Git command'
                    }
                }

                if (-not $latestDownloadHadValidSource) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has no valid workspace-root checkout of github.repository at github.sha before its last preceding artifact download: $latestDownloadFailure."
                }
                if (-not $effectiveCheckoutValid) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' does not have an effective pinned workspace-root source when the script runs: $effectiveCheckoutFailure."
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
