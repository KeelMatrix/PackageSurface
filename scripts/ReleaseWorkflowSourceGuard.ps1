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

function Get-WorkflowFlowMappingText {
    param(
        [Parameter(Mandatory)] [string] $InitialText,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ScopeLines,
        [Parameter(Mandatory)] [int] $StartIndex,
        [Parameter(Mandatory)] [int] $EnvironmentIndent,
        [switch] $IncludeEndIndex
    )

    $text = [Text.StringBuilder]::new()
    $quote = [char]0
    $opened = $false
    $lineIndex = $StartIndex - 1
    $lineText = $InitialText
    while ($true) {
        $line = [string]$lineText
        for ($characterIndex = 0; $characterIndex -lt $line.Length; $characterIndex++) {
            $character = $line[$characterIndex]
            if ($quote -eq '"') {
                [void]$text.Append($character)
                if ($character -eq '\') {
                    if (($characterIndex + 1) -ge $line.Length) { throw 'Workflow env flow mapping has an unsupported escaped line break.' }
                    $characterIndex++
                    [void]$text.Append($line[$characterIndex])
                }
                elseif ($character -eq '"') { $quote = [char]0 }
                continue
            }
            if ($quote -eq "'") {
                [void]$text.Append($character)
                if ($character -eq "'" -and ($characterIndex + 1) -lt $line.Length -and $line[$characterIndex + 1] -eq "'") {
                    $characterIndex++
                    [void]$text.Append($line[$characterIndex])
                }
                elseif ($character -eq "'") { $quote = [char]0 }
                continue
            }

            if ($character -eq '#' -and ($characterIndex -eq 0 -or [char]::IsWhiteSpace($line[$characterIndex - 1]))) { break }
            if ($character -eq '"' -or $character -eq "'") {
                $quote = $character
                [void]$text.Append($character)
                continue
            }
            if ($character -eq '{') {
                if ($opened) { throw 'Workflow env flow mapping contains an unsupported nested mapping.' }
                $opened = $true
                [void]$text.Append($character)
                continue
            }
            if ($character -eq '}') {
                if (-not $opened) { throw 'Workflow env flow mapping has an unsupported closing brace.' }
                [void]$text.Append($character)
                $tail = $line.Substring($characterIndex + 1)
                if ($tail -match '^\s*(?:#.*)?$') {
                    if ($IncludeEndIndex) { return [pscustomobject]@{ Text = $text.ToString(); EndIndex = $lineIndex } }
                    return $text.ToString()
                }
                throw 'Workflow env flow mapping has unsupported content after its closing brace.'
            }
            [void]$text.Append($character)
        }

        if ($quote -ne [char]0) { throw 'Workflow env flow mapping uses a multiline quoted scalar.' }
        [void]$text.Append(' ')
        $lineIndex++
        if ($lineIndex -ge $ScopeLines.Count) { break }
        $lineText = [string]$ScopeLines[$lineIndex]
        if ([string]::IsNullOrWhiteSpace($lineText) -or $lineText.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
        if ((Get-WorkflowIndent $lineText) -le $EnvironmentIndent -and $lineText.TrimStart() -notmatch '^\}(?:\s*(?:#.*)?)?$') { break }
    }

    throw 'Workflow env flow mapping is incomplete.'
}

function Get-WorkflowFlowMappingEntries {
    param([Parameter(Mandatory)] [string] $FlowText)

    $inner = $FlowText.Substring(1, $FlowText.Length - 2)
    $segments = [Collections.Generic.List[string]]::new()
    $segment = [Text.StringBuilder]::new()
    $quote = [char]0
    for ($index = 0; $index -lt $inner.Length; $index++) {
        $character = $inner[$index]
        if ($quote -eq '"') {
            [void]$segment.Append($character)
            if ($character -eq '\') {
                if (($index + 1) -ge $inner.Length) { throw 'Workflow env flow mapping has an unsupported escaped value.' }
                $index++
                [void]$segment.Append($inner[$index])
            }
            elseif ($character -eq '"') { $quote = [char]0 }
            continue
        }
        if ($quote -eq "'") {
            [void]$segment.Append($character)
            if ($character -eq "'" -and ($index + 1) -lt $inner.Length -and $inner[$index + 1] -eq "'") {
                $index++
                [void]$segment.Append($inner[$index])
            }
            elseif ($character -eq "'") { $quote = [char]0 }
            continue
        }
        if ($character -eq '"' -or $character -eq "'") {
            $quote = $character
            [void]$segment.Append($character)
        }
        elseif ($character -eq ',') {
            $segments.Add($segment.ToString())
            $segment = [Text.StringBuilder]::new()
        }
        else { [void]$segment.Append($character) }
    }
    if ($quote -ne [char]0) { throw 'Workflow env flow mapping has an unterminated quoted scalar.' }
    $segments.Add($segment.ToString())

    if ($segments.Count -gt 0 -and [string]::IsNullOrWhiteSpace($segments[$segments.Count - 1])) { $segments.RemoveAt($segments.Count - 1) }
    $entries = [Collections.Generic.List[object]]::new()
    foreach ($item in $segments) {
        if ([string]::IsNullOrWhiteSpace($item)) { throw 'Workflow env flow mapping contains an empty entry.' }
        $entry = [regex]::Match($item.Trim(), '^(?:"(?<double>(?:\\.|[^"\\])*)"|''(?<single>(?:[^'']|'''')*)''|(?<bare>[A-Za-z_][A-Za-z0-9_-]*))\s*:\s*(?<value>.*)$')
        if (-not $entry.Success) { throw 'Workflow env flow mapping contains an unsupported key or value.' }
        $key = if ($entry.Groups['double'].Success) {
            Get-WorkflowScalarValue -Value ('"' + $entry.Groups['double'].Value + '"') -Context 'env key'
        }
        elseif ($entry.Groups['single'].Success) {
            Get-WorkflowScalarValue -Value ("'" + $entry.Groups['single'].Value + "'") -Context 'env key'
        }
        else { $entry.Groups['bare'].Value }
        $entries.Add([pscustomobject]@{ Key = [string]$key; Value = [string]$entry.Groups['value'].Value.Trim() })
    }
    return $entries.ToArray()
}

function Get-WorkflowScopeEnvironmentEntries {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ScopeLines,
        [Parameter(Mandatory)] [int] $DirectIndent
    )

    $entries = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
    $environmentHeaderIndex = -1
    $environmentIndent = -1
    $environmentValue = ''
    for ($index = 0; $index -lt $ScopeLines.Count; $index++) {
        $header = [regex]::Match([string]$ScopeLines[$index], '^(?<indent> *)env:\s*(?<value>.*)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $header.Success -or $header.Groups['indent'].Value.Length -ne $DirectIndent) { continue }
        if ($environmentHeaderIndex -ge 0) { throw 'Workflow scope has duplicate env mappings.' }
        $environmentHeaderIndex = $index
        $environmentIndent = $DirectIndent
        $environmentValue = $header.Groups['value'].Value.Trim()
    }
    if ($environmentHeaderIndex -lt 0) { return $entries }

    $blockEntryPattern = @'
^(?<indent> +)(?:"(?<double>(?:\\.|[^"\\])*)"|'(?<single>(?:[^']|'')*)'|(?<bare>[A-Za-z_][A-Za-z0-9_-]*)):\s*(?<value>.*)$
'@
    if ($environmentValue.StartsWith('{', [StringComparison]::Ordinal)) {
        $flowText = Get-WorkflowFlowMappingText -InitialText $environmentValue -ScopeLines $ScopeLines -StartIndex ($environmentHeaderIndex + 1) -EnvironmentIndent $environmentIndent
        if ($flowText -match '(?i)(?:^|[,{]\s*)(?:<<\s*:|[&*!])') { throw 'Workflow env flow mapping uses an unsupported merge, anchor, alias, or tagged key.' }
        foreach ($entry in (Get-WorkflowFlowMappingEntries -FlowText $flowText)) {
            $key = [string]$entry.Key
            if ($entries.ContainsKey($key)) { throw "Workflow env mapping repeats '$key'." }
            $rawValue = [string]$entry.Value
            $value = if ([string]::IsNullOrWhiteSpace($rawValue)) { '' } else { Get-WorkflowScalarValue -Value $rawValue -Context "env.$key" }
            $entries.Add($key, [string]$value)
        }
        return $entries
    }

    if ([string]::IsNullOrWhiteSpace($environmentValue) -or $environmentValue.StartsWith('#', [StringComparison]::Ordinal)) {
        for ($index = $environmentHeaderIndex + 1; $index -lt $ScopeLines.Count; $index++) {
            $line = [string]$ScopeLines[$index]
            if ([string]::IsNullOrWhiteSpace($line) -or $line.TrimStart().StartsWith('#', [StringComparison]::Ordinal)) { continue }
            $indent = Get-WorkflowIndent $line
            if ($indent -le $environmentIndent) { break }
            $entry = [regex]::Match($line, $blockEntryPattern)
            if (-not $entry.Success -or $indent -ne ($environmentIndent + 2)) { throw 'Workflow env mapping has an unsupported nested value.' }
            $key = if ($entry.Groups['double'].Success) { Get-WorkflowScalarValue -Value ('"' + $entry.Groups['double'].Value + '"') -Context 'env key' } elseif ($entry.Groups['single'].Success) { Get-WorkflowScalarValue -Value ("'" + $entry.Groups['single'].Value + "'") -Context 'env key' } else { $entry.Groups['bare'].Value }
            if ($entries.ContainsKey($key)) { throw "Workflow env mapping repeats '$key'." }
            $rawValue = $entry.Groups['value'].Value.Trim()
            $value = if ([string]::IsNullOrWhiteSpace($rawValue)) { '' } else { Get-WorkflowScalarValue -Value $rawValue -Context "env.$key" }
            $entries.Add($key, [string]$value)
        }
        return $entries
    }

    throw 'Workflow env mapping uses an unsupported alias or scalar form.'
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

    $environmentFlowStartIndex = -1
    $environmentFlowEndIndex = -1
    for ($lineIndex = 0; $lineIndex -lt $fieldLines.Count; $lineIndex++) {
        $environmentHeader = [regex]::Match([string]$fieldLines[$lineIndex], '^(?<indent> *)env:\s*(?<value>.*)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $environmentHeader.Success -or $environmentHeader.Groups['indent'].Value.Length -ne $mapIndent -or
            -not $environmentHeader.Groups['value'].Value.Trim().StartsWith('{', [StringComparison]::Ordinal)) { continue }
        if ($environmentFlowStartIndex -ge 0) { throw "Workflow job '$JobName' step $StepNumber repeats 'env'." }
        $flowMapping = Get-WorkflowFlowMappingText -InitialText $environmentHeader.Groups['value'].Value.Trim() -ScopeLines $fieldLines.ToArray() -StartIndex ($lineIndex + 1) -EnvironmentIndent $mapIndent -IncludeEndIndex
        $environmentFlowStartIndex = $lineIndex
        $environmentFlowEndIndex = [int]$flowMapping.EndIndex
    }

    for ($lineIndex = 0; $lineIndex -lt $fieldLines.Count; $lineIndex++) {
        if ($environmentFlowStartIndex -ge 0 -and $lineIndex -gt $environmentFlowStartIndex -and $lineIndex -le $environmentFlowEndIndex) { continue }
        $line = [string]$fieldLines[$lineIndex]
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
    $environmentValues = Get-WorkflowScopeEnvironmentEntries -ScopeLines $fieldLines.ToArray() -DirectIndent $mapIndent
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
        EnvironmentValues = $environmentValues
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
    if ($Checkout.With.ContainsKey('clean')) {
        $cleanInput = Get-WorkflowScalarValue -Value $Checkout.With['clean'] -Context "$Context checkout clean"
        if ($cleanInput -notmatch '^(?i:true|yes|1)$') { $failures.Add('checkout does not prove that tracked workspace content is restored') }
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

function ConvertTo-WorkflowLogicalRunText {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $logical = $Text
    if ($Shell -match '^(?i:bash|sh)$') { $logical = [regex]::Replace($logical, '\\\r?\n', ' ') }
    elseif ($Shell -match '^(?i:pwsh|powershell)$') { $logical = [regex]::Replace($logical, ([char]96).ToString() + '\r?\n', ' ') }
    $lines = @($logical -split '\r?\n' | ForEach-Object { ([string]$_).TrimStart() })
    return $lines -join [Environment]::NewLine
}
function Get-WorkflowCurlWriteOutTargets {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Format)

    $value = ConvertFrom-WorkflowShellToken -Token $Format
    if ($value.StartsWith('@', [StringComparison]::Ordinal)) { return @('') }

    $targets = [Collections.Generic.List[string]]::new()
    $searchAt = 0
    while ($searchAt -lt $value.Length) {
        $directiveAt = $value.IndexOf('%output{', $searchAt, [StringComparison]::OrdinalIgnoreCase)
        if ($directiveAt -lt 0) { break }
        $pathStart = $directiveAt + '%output{'.Length
        $depth = 1
        $pathEnd = $pathStart
        while ($pathEnd -lt $value.Length -and $depth -gt 0) {
            if ($value[$pathEnd] -eq '{') { $depth++ }
            elseif ($value[$pathEnd] -eq '}') { $depth-- }
            if ($depth -gt 0) { $pathEnd++ }
        }
        if ($depth -ne 0) { $targets.Add(''); break }
        $path = $value.Substring($pathStart, $pathEnd - $pathStart)
        if ($path.StartsWith('>>', [StringComparison]::Ordinal)) { $path = $path.Substring(2) }
        if ([string]::IsNullOrWhiteSpace($path)) { $path = '' }
        $targets.Add($path)
        $searchAt = $pathEnd + 1
    }

    if ($targets.Count -eq 0 -and $value -match '[\$`]|\$\(') { $targets.Add('') }
    return $targets.ToArray()
}

function Get-WorkflowShellTokens {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $tokens = [Collections.Generic.List[string]]::new()
    foreach ($match in [regex]::Matches($Text, '"[^"\r\n]*"|''[^''\r\n]*''|[^\s;&|]+')) {
        $tokens.Add($match.Value)
    }
    return $tokens.ToArray()
}

function Test-WorkflowPinnedScriptArgument {
    param([Parameter(Mandatory)] [string] $Token, [AllowNull()] [string] $Shell)

    $value = ConvertFrom-WorkflowShellToken -Token $Token
    if ($Shell -match '^(?i:pwsh|powershell)$') { $value = $value.Replace('\', '/') }
    elseif ($value.Contains('\')) { return $false }
    $normalized = $value -replace '^\./', ''
    $dynamicPathCharacters = '[\$' + [char]96 + '*?]'
    if ($normalized -match $dynamicPathCharacters -or $normalized -match '(?:^|/)\.\.?(/|$)') { return $false }
    return $normalized -match '(?i)^scripts/(?:[A-Za-z0-9_.-]+/)*[A-Za-z0-9_.-]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl)$'
}

function Get-WorkflowShellCommandSegments {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $segments = [Collections.Generic.List[string]]::new()
    $start = 0
    $quote = [char]0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ($quote -ne [char]0) {
            if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote $quote -Escape $character -Next $Text[$index + 1])) { $index++; continue }
            if ($character -eq $quote) { $quote = [char]0; continue }
            continue
        }
        if ($character -eq '"' -or $character -eq "'") { $quote = $character; continue }
        if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote ([char]0) -Escape $character -Next $Text[$index + 1])) { $index++; continue }
        if ($character -eq '#' -and ($index -eq 0 -or [char]::IsWhiteSpace($Text[$index - 1]))) {
            while ($index -lt $Text.Length -and $Text[$index] -ne [char]10) { $index++ }
            $segments.Add($Text.Substring($start, $index - $start))
            $start = [Math]::Min($index + 1, $Text.Length)
            continue
        }
        if ($character -notin @(';', '|', '&', [char]13, [char]10, '(', ')')) { continue }
        $segments.Add($Text.Substring($start, $index - $start))
        $start = $index + 1
    }
    if ($start -lt $Text.Length) { $segments.Add($Text.Substring($start)) }
    return $segments.ToArray()
}

function Test-WorkflowRunnerTempReference {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Value)

    $normalized = $Value.Replace('\', '/')
    return $normalized -match '(?i)(?:\$\{\{\s*runner\.temp\s*\}\}|\$env:runner_temp|\$\{?runner_temp\}?|%runner_temp%)(?:/|$|"|'')'
}

function Normalize-WorkflowAbsolutePath {
    param([Parameter(Mandatory)] [string] $Path)

    $normalized = $Path.Replace('\', '/')
    if ($normalized -match '^//[?.]/') { return $null }
    $prefix = ''
    $remaining = $normalized
    $minimumParts = 0
    if ($normalized -match '^(?<drive>[A-Za-z]:)/') {
        $prefix = $Matches['drive'] + '/'
        $remaining = $normalized.Substring($prefix.Length)
    }
    elseif ($normalized.StartsWith('//', [StringComparison]::Ordinal)) {
        $prefix = '//'
        $remaining = $normalized.TrimStart('/')
        $minimumParts = 2
    }
    elseif ($normalized.StartsWith('/', [StringComparison]::Ordinal)) {
        $prefix = '/'
        $remaining = $normalized.TrimStart('/')
    }
    else { return $null }

    $parts = [Collections.Generic.List[string]]::new()
    foreach ($component in ($remaining -split '/')) {
        if ([string]::IsNullOrEmpty($component) -or $component -eq '.') { continue }
        if ($component -eq '..') {
            if ($parts.Count -le $minimumParts) { return $null }
            $parts.RemoveAt($parts.Count - 1)
            continue
        }
        $parts.Add($component)
    }
    if ($minimumParts -eq 2 -and $parts.Count -lt 2) { return $null }
    if ($parts.Count -eq 0) { return $prefix }
    if ($prefix -eq '//') { return $prefix + ($parts -join '/') }
    return $prefix + ($parts -join '/')
}

function Normalize-WorkflowAbsolutePathForRepository {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $RepositoryRoot)

    $root = Normalize-WorkflowAbsolutePath -Path $RepositoryRoot
    if ($null -eq $root) { return $null }
    $pathText = $Path.Replace('\', '/')
    $rootDrive = [regex]::Match($root, '^(?<drive>[A-Za-z]:)/')
    $rootIsUnc = $root.StartsWith('//', [StringComparison]::Ordinal)
    $explicitUnc = $Path.StartsWith('\\', [StringComparison]::Ordinal)

    if ($null -ne $rootDrive -and $rootDrive.Success -and $pathText.StartsWith('/', [StringComparison]::Ordinal) -and -not $pathText.StartsWith('//', [StringComparison]::Ordinal)) {
        $pathText = $rootDrive.Groups['drive'].Value + $pathText
    }
    elseif ($rootIsUnc -and $pathText.StartsWith('/', [StringComparison]::Ordinal) -and -not $pathText.StartsWith('//', [StringComparison]::Ordinal) -and -not $explicitUnc) {
        return $null
    }
    elseif (-not $rootDrive.Success -and -not $rootIsUnc -and -not $explicitUnc -and $pathText.StartsWith('//', [StringComparison]::Ordinal)) {
        $pathText = '/' + $pathText.TrimStart('/')
    }

    return Normalize-WorkflowAbsolutePath -Path $pathText
}

function Get-WorkflowAbsoluteWorkspacePath {
    param([Parameter(Mandatory)] [string] $Path, [Parameter(Mandatory)] [string] $RepositoryRoot)

    $root = Normalize-WorkflowAbsolutePath -Path $RepositoryRoot
    if ($null -eq $root) { return $null }
    $rootIsWindows = $root -match '^(?:[A-Za-z]:/|//)'
    $candidate = Normalize-WorkflowAbsolutePathForRepository -Path $Path -RepositoryRoot $RepositoryRoot
    if ($null -eq $candidate) { return $null }
    $candidateIsWindows = $candidate -match '^(?:[A-Za-z]:/|//)'
    if ($candidateIsWindows -ne $rootIsWindows) { return $null }
    $comparison = if ($rootIsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
    if ($candidate.Equals($root, $comparison)) { return '' }

    $rootWithoutTrailingSeparator = $root.TrimEnd('/')
    if ([string]::IsNullOrEmpty($rootWithoutTrailingSeparator)) {
        if (-not $candidate.StartsWith('/', [StringComparison]::Ordinal)) { return $null }
        return Resolve-WorkflowRelativePath -BasePath '' -RelativePath $candidate.TrimStart('/')
    }
    $prefix = $rootWithoutTrailingSeparator + '/'
    if (-not $candidate.StartsWith($prefix, $comparison)) { return $null }
    return Resolve-WorkflowRelativePath -BasePath '' -RelativePath $candidate.Substring($prefix.Length)
}

function Resolve-WorkflowWorkspacePath {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $BasePath,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Path,
        [Parameter(Mandatory)] [string] $RepositoryRoot
    )

    if ($Path -match '^[A-Za-z]:[^/\\]') { return $null }
    if ($Path.StartsWith('/', [StringComparison]::Ordinal) -or $Path.StartsWith('\', [StringComparison]::Ordinal) -or $Path -match '^[A-Za-z]:[\\/]') {
        return Get-WorkflowAbsoluteWorkspacePath -Path $Path -RepositoryRoot $RepositoryRoot
    }
    return Resolve-WorkflowRelativePath -BasePath $BasePath -RelativePath $Path
}

function Test-WorkflowCmdShell {
    param([AllowNull()] [string] $Shell)

    if ([string]::IsNullOrWhiteSpace($Shell)) { return $false }
    return $Shell -match '(?i)(?:^|[\\/\"''])cmd(?:\.exe)?(?:[\"'']|\s|$)'
}

function Test-WorkflowCmdBuiltin {
    param([Parameter(Mandatory)] [string] $Name)

    $builtInName = -join ([char[]]@(112, 114, 111, 109, 112, 116))
    $builtInAlternation = 'assoc|break|call|cd|chcp|chdir|cls|color|copy|date|del|dir|echo|endlocal|erase|exit|for|ftype|goto|if|md|mkdir|mklink|move|path|pause|popd|' + $builtInName + '|pushd|rd|rem|ren|rename|rmdir|set|setlocal|shift|start|time|title|type|ver|verify|vol'
    return $Name -match ('^(?i:' + $builtInAlternation + ')$')
}

function Get-WorkflowRunnerPathFlavor {
    param([Parameter(Mandatory)] [object] $Job)

    $runnerValue = $null
    foreach ($line in @($Job.Lines)) {
        $entry = [regex]::Match([string]$line, '^(?<indent> *)runs-on:\s*(?<value>.*)$', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
        if (-not $entry.Success -or $entry.Groups['indent'].Value.Length -ne [int]$Job.DirectIndent) { continue }
        if ($null -ne $runnerValue) { throw "Workflow job '$($Job.Name)' has duplicate runs-on keys." }
        $rawValue = [string]$entry.Groups['value'].Value
        if ([string]::IsNullOrWhiteSpace($rawValue) -or $rawValue.TrimStart().StartsWith('[', [StringComparison]::Ordinal) -or $rawValue.TrimStart().StartsWith('{', [StringComparison]::Ordinal)) {
            return 'unknown'
        }
        try { $runnerValue = Get-WorkflowScalarValue -Value $rawValue -Context "job '$($Job.Name)' runs-on" }
        catch { return 'unknown' }
    }

    if ([string]::IsNullOrWhiteSpace($runnerValue)) { return 'unknown' }
    if ($runnerValue -match '(?i)windows') { return 'windows' }
    if ($runnerValue -match '(?i)(?:ubuntu|linux|macos)') { return 'posix' }
    return 'unknown'
}

function Get-WorkflowRepositoryPathFlavor {
    param([Parameter(Mandatory)] [string] $RepositoryRoot)

    $root = Normalize-WorkflowAbsolutePath -Path $RepositoryRoot
    if ($null -eq $root) { return 'unknown' }
    if ($root -match '^(?:[A-Za-z]:/|//)') { return 'windows' }
    return 'posix'
}

function Get-WorkflowArtifactOutputLocation {
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [string] $Context,
        [Parameter(Mandatory)] [string] $RepositoryRoot,
        [ValidateSet('posix', 'windows', 'unknown')] [string] $RunnerPathFlavor = 'unknown'
    )

    $value = if ($Step.With.ContainsKey('path')) {
        Get-WorkflowScalarValue -Value $Step.With['path'] -Context "$Context artifact output path"
    }
    else { '.' }
    if ([string]::IsNullOrWhiteSpace($value)) { $value = '.' }
    if ($RunnerPathFlavor -ne 'windows' -and ($value.Contains('\') -or $value -match '^[A-Za-z]:')) {
        throw "$Context artifact output path uses Windows path syntax for a non-Windows or unknown runner."
    }
    $normalized = $value.Replace('\', '/')

    if ($normalized -match '^\$\{\{\s*github\.workspace\s*\}\}(?:/(?<suffix>.*))?$') {
        $suffix = if ($Matches.ContainsKey('suffix')) { [string]$Matches['suffix'] } else { '' }
        $relative = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $suffix
        if ($null -eq $relative) { throw "$Context artifact output path escapes the pinned workspace." }
        return [pscustomobject]@{ Kind = 'workspace'; Path = $relative; RepositoryRoot = $RepositoryRoot; RunnerPathFlavor = $RunnerPathFlavor }
    }
    if ($normalized -match '^\$\{\{\s*runner\.temp\s*\}\}(?:/(?<suffix>.*))?$') {
        $suffix = if ($Matches.ContainsKey('suffix')) { [string]$Matches['suffix'] } else { '' }
        $relative = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $suffix
        if ($null -eq $relative) { throw "$Context artifact output path escapes runner.temp." }
        return [pscustomobject]@{ Kind = 'runner-temp'; Path = $relative; RepositoryRoot = $RepositoryRoot; RunnerPathFlavor = $RunnerPathFlavor }
    }
    if ($normalized -match '\$|`|%|\$\{|\*|\?|~') { throw "$Context artifact output path is dynamic and cannot establish executable provenance." }
    if ($normalized.StartsWith('/', [StringComparison]::Ordinal) -or $normalized -match '^[A-Za-z]:[\\/]') {
        $repositoryPathFlavor = Get-WorkflowRepositoryPathFlavor -RepositoryRoot $RepositoryRoot
        if ($RunnerPathFlavor -eq 'unknown' -or $RunnerPathFlavor -ne $repositoryPathFlavor) {
            throw "$Context absolute artifact output path cannot be resolved against the runner path flavor."
        }
        $absolute = Normalize-WorkflowAbsolutePathForRepository -Path $normalized -RepositoryRoot $RepositoryRoot
        if ($null -eq $absolute) { throw "$Context artifact output path is not a normalized absolute path." }
        $workspacePath = Get-WorkflowAbsoluteWorkspacePath -Path $absolute -RepositoryRoot $RepositoryRoot
        if ($null -ne $workspacePath) { return [pscustomobject]@{ Kind = 'workspace'; Path = $workspacePath; RepositoryRoot = $RepositoryRoot; RunnerPathFlavor = $RunnerPathFlavor } }
        return [pscustomobject]@{ Kind = 'absolute'; Path = $absolute; RepositoryRoot = $RepositoryRoot; RunnerPathFlavor = $RunnerPathFlavor }
    }
    if ($normalized -match '^[A-Za-z]:') { throw "$Context artifact output path is drive-relative and cannot establish executable provenance." }

    $relative = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $normalized
    if ($null -eq $relative) { throw "$Context artifact output path escapes the pinned workspace." }
    return [pscustomobject]@{ Kind = 'workspace'; Path = $relative; RepositoryRoot = $RepositoryRoot; RunnerPathFlavor = $RunnerPathFlavor }
}

function Test-WorkflowPathIsAtOrBelow {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Candidate, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Root)

    $candidatePath = $Candidate.Replace('\', '/').TrimEnd('/')
    $rootPath = $Root.Replace('\', '/').TrimEnd('/')
    if ([string]::IsNullOrEmpty($rootPath)) { return $true }
    if ($candidatePath.Equals($rootPath, [StringComparison]::OrdinalIgnoreCase)) { return $true }
    return $candidatePath.StartsWith($rootPath + '/', [StringComparison]::OrdinalIgnoreCase)
}

function Test-WorkflowTokenTargetsArtifactOutput {
    param(
        [Parameter(Mandatory)] [string] $Token,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory,
        [AllowNull()] [string] $Shell,
        [switch] $CheckCmdLookup,
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    if ($ArtifactOutputPaths.Count -eq 0) { return $false }
    $value = ConvertFrom-WorkflowShellToken -Token $Token
    if ([string]::IsNullOrWhiteSpace($value)) { return $false }
    $runnerPathFlavor = 'windows'
    if ($ArtifactOutputPaths[0].PSObject.Properties['RunnerPathFlavor']) { $runnerPathFlavor = [string]$ArtifactOutputPaths[0].RunnerPathFlavor }
    if ($runnerPathFlavor -ne 'windows' -and ($Token.Contains('\') -or $value -match '^[A-Za-z]:')) {
        if (-not (Test-WorkflowPinnedScriptArgument -Token $Token -Shell $Shell)) { return $true }
    }
    $normalized = $value.Replace('\', '/')
    $workspaceCandidate = $null
    $absoluteCandidate = $null

    if ($normalized -match '^(?i:\$\{\{\s*github\.workspace\s*\}\}|\$GITHUB_WORKSPACE|\$\{GITHUB_WORKSPACE\}|\$env:GITHUB_WORKSPACE|%GITHUB_WORKSPACE%)(?:/(?<suffix>.*))?$') {
        $suffix = if ($Matches.ContainsKey('suffix')) { [string]$Matches['suffix'] } else { '' }
        $workspaceCandidate = Resolve-WorkflowRelativePath -BasePath '' -RelativePath $suffix
        if ($null -ne $workspaceCandidate) { $absoluteCandidate = Normalize-WorkflowAbsolutePath -Path (Join-Path ([string]$ArtifactOutputPaths[0].RepositoryRoot) $workspaceCandidate) }
    }
    elseif ($normalized -match '(?i)runner[._-]temp|\$env:RUNNER_TEMP|%RUNNER_TEMP%') {
        return @($ArtifactOutputPaths | Where-Object { $_.Kind -eq 'runner-temp' }).Count -gt 0
    }
    elseif ($normalized -match '\$|`|%|\$\{|\*|\?') {
        return $true
    }
    elseif ($normalized -match '^[A-Za-z]:') {
        return $true
    }
    elseif ($normalized.StartsWith('/', [StringComparison]::Ordinal) -or $normalized -match '^[A-Za-z]:[\\/]') {
        if ($runnerPathFlavor -eq 'unknown' -or $runnerPathFlavor -ne (Get-WorkflowRepositoryPathFlavor -RepositoryRoot ([string]$ArtifactOutputPaths[0].RepositoryRoot))) { return $true }
        $absoluteCandidate = Normalize-WorkflowAbsolutePathForRepository -Path $normalized -RepositoryRoot ([string]$ArtifactOutputPaths[0].RepositoryRoot)
        if ($null -eq $absoluteCandidate) { return $true }
        $workspaceCandidate = Get-WorkflowAbsoluteWorkspacePath -Path $absoluteCandidate -RepositoryRoot ([string]$ArtifactOutputPaths[0].RepositoryRoot)
    }
    elseif ($normalized.Contains('/')) {
        $base = Resolve-WorkflowWorkspacePath -BasePath '' -Path $WorkingDirectory -RepositoryRoot ([string]$ArtifactOutputPaths[0].RepositoryRoot)
        if ($null -eq $base) { return $true }
        $workspaceCandidate = Resolve-WorkflowRelativePath -BasePath $base -RelativePath $normalized
        if ($null -ne $workspaceCandidate) { $absoluteCandidate = Normalize-WorkflowAbsolutePath -Path (Join-Path ([string]$ArtifactOutputPaths[0].RepositoryRoot) $workspaceCandidate) }
    }
    else {
        if (-not $CheckCmdLookup -or -not (Test-WorkflowCmdShell -Shell $Shell) -or $NoDefaultCurrentDirectoryInExePath) { return $false }
        $programName = ($normalized -split '[\\/]')[-1]
        if (Test-WorkflowCmdBuiltin -Name $programName) { return $false }
        $cwd = Resolve-WorkflowWorkspacePath -BasePath '' -Path $WorkingDirectory -RepositoryRoot ([string]$ArtifactOutputPaths[0].RepositoryRoot)
        if ($null -eq $cwd) { return $true }
        foreach ($outputPath in $ArtifactOutputPaths) {
            if ($outputPath.Kind -eq 'workspace' -and (Test-WorkflowPathIsAtOrBelow -Candidate $cwd -Root ([string]$outputPath.Path))) { return $true }
        }
        return $false
    }

    if ($null -eq $workspaceCandidate -and $null -eq $absoluteCandidate) { return $true }
    foreach ($outputPath in $ArtifactOutputPaths) {
        if ($outputPath.Kind -eq 'workspace' -and $null -ne $workspaceCandidate -and (Test-WorkflowPathIsAtOrBelow -Candidate $workspaceCandidate -Root ([string]$outputPath.Path))) { return $true }
        if ($outputPath.Kind -eq 'absolute' -and $null -ne $absoluteCandidate -and (Test-WorkflowPathIsAtOrBelow -Candidate $absoluteCandidate -Root ([string]$outputPath.Path))) { return $true }
    }
    return $false
}

function Test-WorkflowRunMutatesPath {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $patterns = @(
        '(?i)(?:^|[;&|({\r\n]\s*)(?:export\s+)?PATH\s*\+?=',
        '(?i)\$env:PATH\s*\+?=',
        '(?i)(?:^|[;&|({\r\n]\s*)env(?:\.exe)?\s+(?:[^;&|\r\n]*\s)?PATH\s*=',
        '(?i)(?:Set-Item|New-Item|Remove-Item|Clear-Item)\s+[^;&|\r\n]*\b(?:Env:)?PATH\b',
        '(?i)(?:^|[;&|({\r\n]\s*)@?\s*set(?:\.exe)?\s+["'']?PATH\s*=',
        '(?i)(?:^|[;&|({\r\n]\s*)@?\s*setx(?:\.exe)?\s+["'']?PATH\b',
        '(?i)(?:^|[;&|({\r\n]\s*)unset(?:\s+-\w+)?\s+PATH\b',
        '(?i)(?:^|[;&|({\r\n]\s*)@?\s*path(?:\.exe)?\s+\S',
        '(?i)\bGITHUB_PATH\b|\bGITHUB_ENV\b|::add-path::|::set-env\s+name\s*=\s*PATH::|\baddPath\s*\(|\bexportVariable\s*\(\s*["'']PATH["'']',
        '(?is)\bSetEnvironmentVariable\s*\(\s*["'']PATH["'']|(?:PATH\s*=.{0,160}GITHUB_ENV|GITHUB_ENV.{0,160}PATH\s*=)|NoDefaultCurrentDirectoryInExePath'
    )
    foreach ($pattern in $patterns) {
        if ($Text -match $pattern) { return $true }
    }
    return $false
}

function Test-WorkflowRunAddsPersistentPath {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    return $Text -match '(?i)\bGITHUB_PATH\b|\bGITHUB_ENV\b|::add-path::|::set-env\s+name\s*=\s*PATH::|\baddPath\s*\(|\bexportVariable\s*\(\s*["'']PATH["'']|\bsetx(?:\.exe)?\s+["'']?PATH\b|(?is)\bSetEnvironmentVariable\s*\(\s*["'']PATH["'']|(?:PATH\s*=.{0,160}GITHUB_ENV|GITHUB_ENV.{0,160}PATH\s*=)|NoDefaultCurrentDirectoryInExePath'
}

function Test-WorkflowScopeDefinesPathEnvironment {
    param([Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $ScopeLines, [int] $DirectIndent = 0)

    return (Get-WorkflowScopeEnvironmentEntries -ScopeLines $ScopeLines -DirectIndent $DirectIndent).ContainsKey('PATH')
}

function Test-WorkflowEnvironmentValueDisablesCmdCurrentDirectory {
    param([AllowEmptyString()] [string] $Value)

    # Windows checks whether NoDefaultCurrentDirectoryInExePath exists, not its value.
    # An env mapping with any value therefore removes cmd's implicit current directory.
    return $true
}

function Test-WorkflowArtifactOutputOverlapsTrackedFiles {
    param([Parameter(Mandatory)] [object] $OutputPath, [Parameter(Mandatory)] [string[]] $TrackedFiles)

    if ($OutputPath.Kind -eq 'absolute') {
        $repositoryInsideOutput = Get-WorkflowAbsoluteWorkspacePath -Path ([string]$OutputPath.RepositoryRoot) -RepositoryRoot ([string]$OutputPath.Path)
        return $null -ne $repositoryInsideOutput
    }
    if ($OutputPath.Kind -ne 'workspace') { return $false }
    foreach ($trackedFile in $TrackedFiles) {
        $normalizedFile = ([string]$trackedFile).Replace('\', '/')
        if (Test-WorkflowPathIsAtOrBelow -Candidate $normalizedFile -Root ([string]$OutputPath.Path)) { return $true }
    }
    return $false
}

function Test-WorkflowInterpreterInvocation {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Arguments,
        [AllowNull()] [string] $Shell,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [string] $WorkingDirectory = '.'
    )

    foreach ($argument in $Arguments) {
        if (Test-WorkflowTokenTargetsArtifactOutput -Token ([string]$argument) -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory -Shell $Shell) { return $true }
    }

    $arguments = $Arguments -join ' '
    if ($arguments -match '(?i)(?:^|\s)(?:-s|--stdin|-|/dev/stdin|/dev/fd/\d+|CON)(?=\s|$)|(?:<<?<?\s*(?:/dev/stdin|/dev/fd/\d+|CON))|(?:<<<?)|(?:\$\(|<\()') { return $true }
    if ($arguments -match '(?i)(?:^|\s)-(?:c|command|commandwithargs|encodedcommand|enc)(?:=|\s|$)|(?:^|\s)(?:-[A-Za-z]*c[A-Za-z]*)(?=\s|$)') { return $true }
    $scriptArguments = [regex]::Matches($arguments, '(?:"[^"\r\n]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl)"|''[^''\r\n]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl)''|[^\s;&|]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl))')
    foreach ($scriptArgument in $scriptArguments) {
        if (-not (Test-WorkflowPinnedScriptArgument -Token $scriptArgument.Value -Shell $Shell)) { return $true }
    }
    if ($scriptArguments.Count -gt 0) { return $false }
    if ($Arguments.Count -eq 0) { return $true }
    $singleArgument = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[0])
    $safeInformationArguments = @('--help', '--version', '-h', '-?')
    if ($Name -match '^(?i:python(?:\d+(?:\.\d+)*)?)$') { $safeInformationArguments += '-V' }
    if ($Name -match '^(?i:node(?:js)?)$') { $safeInformationArguments += '-v' }
    return $Arguments.Count -ne 1 -or $singleArgument -notin $safeInformationArguments
}

function Test-WorkflowPowerShellUtilityCommandName {
    param([Parameter(Mandatory)] [string] $Name, [AllowNull()] [string] $Shell)

    if ($Shell -notmatch '^(?i:pwsh|powershell)$') { return $false }
    return $Name -match '^(?i:Microsoft\.PowerShell\.Utility)\\(?:Invoke-WebRequest|Invoke-RestMethod)$'
}

function Test-WorkflowCommandContainsUnsupportedExecution {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [string[]] $Tokens,
        [AllowNull()] [string] $Shell,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [string] $WorkingDirectory = '.',
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    $current = @($Tokens)
    for ($depth = 0; $depth -lt 16 -and $current.Count -gt 0; $depth++) {
        while ($current.Count -gt 0) {
            $leading = ConvertFrom-WorkflowShellToken -Token ([string]$current[0])
            if ($leading -in @('!', '&', 'if', 'then', 'do', 'while', 'until', 'elif', 'else')) { $current = @($current | Select-Object -Skip 1); continue }
            if ($leading -match '^[A-Za-z_][A-Za-z0-9_]*=') { $current = @($current | Select-Object -Skip 1); continue }
            break
        }
        if ($current.Count -eq 0) { return $false }

        $program = ConvertFrom-WorkflowShellToken -Token ([string]$current[0])
        $moduleQualifiedPowerShellCommand = Test-WorkflowPowerShellUtilityCommandName -Name $program -Shell $Shell
        if (-not $moduleQualifiedPowerShellCommand -and (Test-WorkflowTokenTargetsArtifactOutput -Token $program -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory -Shell $Shell -CheckCmdLookup -NoDefaultCurrentDirectoryInExePath:$NoDefaultCurrentDirectoryInExePath)) { return $true }
        $programName = ($program -split '[\\/]')[-1].ToLowerInvariant()
        $arguments = @($current | Select-Object -Skip 1)
        if ($program.Contains('$') -or $program.Contains([char]96)) { return $true }

        if ($programName -eq 'env' -or $programName -eq 'env.exe') {
            $index = 0
            $splitCommandFound = $false
            while ($index -lt $arguments.Count) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                if ($option -eq '--') { $index++; break }
                $splitText = $null
                if ($option -in @('-S', '--split-string')) {
                    if ($index + 1 -ge $arguments.Count) { return $true }
                    $splitText = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[++$index])
                }
                elseif ($option -match '^--split-string=(?<value>.*)$') { $splitText = $Matches['value'] }
                elseif ($option -match '^-S=?(?<value>.+)$') { $splitText = $Matches['value'] }
                if ($null -ne $splitText) {
                    # GNU env expands brace-delimited values from its environment before executing.
                    # Do not guess through inherited or inline environment values here.
                    if ($splitText.Contains('$')) { return $true }
                    $nested = @(Get-WorkflowShellTokens -Text $splitText)
                    $current = @($nested) + @($arguments | Select-Object -Skip ($index + 1))
                    if ($current.Count -eq 0) { return $true }
                    $splitCommandFound = $true
                    break
                }
                if ($option -in @('-i', '--ignore-environment', '-0', '--null', '-v', '--debug', '--list-signal-handling')) { $index++; continue }
                if ($option -match '^(?:-u|--unset|-C|--chdir|-a|--argv0|--block-signal|--default-signal|--ignore-signal)$') { $index += 2; continue }
                if ($option -match '^(?:--unset|--chdir|--argv0|--block-signal|--default-signal|--ignore-signal)=') { $index++; continue }
                if ($option -match '^[A-Za-z_][A-Za-z0-9_]*=') { $index++; continue }
                if ($option.StartsWith('-', [StringComparison]::Ordinal)) { return $true }
                break
            }
            if ($splitCommandFound) { continue }
            $current = @($arguments | Select-Object -Skip $index)
            continue
        }
        if ($programName -in @('timeout', 'timeout.exe', 'gtimeout', 'gtimeout.exe')) {
            $index = 0
            $durationFound = $false
            while ($index -lt $arguments.Count) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                if ($option -eq '--') {
                    # The terminator ends option parsing, but timeout still consumes
                    # its duration operand before the command that must be inspected.
                    $index++
                    if ($index -ge $arguments.Count) { return $true }
                    $duration = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                    if ($duration.StartsWith('-', [StringComparison]::Ordinal)) { return $true }
                    $index++
                    $durationFound = $true
                    break
                }
                if ($option -in @('--foreground', '--preserve-status', '--verbose')) { $index++; continue }
                if ($option -in @('-k', '--kill-after', '-s', '--signal')) {
                    if ($index + 1 -ge $arguments.Count) { return $true }
                    $optionValue = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index + 1])
                    if ($optionValue.StartsWith('-', [StringComparison]::Ordinal)) { return $true }
                    $index += 2
                    continue
                }
                if ($option -match '^(?:--kill-after|--signal)=') {
                    if ($option -match '^(?:--kill-after|--signal)=$') { return $true }
                    $index++
                    continue
                }
                if ($option -match '^-[ks].+') { $index++; continue }
                if ($option.StartsWith('-', [StringComparison]::Ordinal)) { return $true }
                $index++ # timeout duration
                $durationFound = $true
                break
            }
            if (-not $durationFound -or $index -ge $arguments.Count) { return $true }
            $current = @($arguments | Select-Object -Skip $index)
            continue
        }
        if ($programName -eq 'exec') {
            $index = 0
            while ($index -lt $arguments.Count) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                if ($option -eq '--') { $index++; break }
                if ($option -match '^-[cl]+$') { $index++; continue }
                if ($option -eq '-a' -or $option -match '^-[cl]*a$') {
                    if ($index + 1 -ge $arguments.Count) { return $true }
                    $index += 2
                    continue
                }
                if ($option -match '^-[cl]*a.+$') { $index++; continue }
                if ($option.StartsWith('-', [StringComparison]::Ordinal)) { return $true }
                break
            }
            $current = @($arguments | Select-Object -Skip $index)
            continue
        }
        if ($programName -eq 'time' -or $programName -eq 'time.exe') {
            $index = 0
            while ($index -lt $arguments.Count) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                if ($option -eq '--') { $index++; break }
                if ($option -match '^(?:--append|--portability|--verbose|--quiet|--help|--version)$') { $index++; continue }
                if ($option -match '^(?:--output|--format)$') {
                    if ($index + 1 -ge $arguments.Count) { return $true }
                    $index += 2
                    continue
                }
                if ($option -match '^(?:--output|--format)=') { $index++; continue }
                if ($option.StartsWith('--', [StringComparison]::Ordinal)) { return $true }
                if ($option.StartsWith('-', [StringComparison]::Ordinal)) {
                    $shortOptions = $option.Substring(1)
                    for ($shortIndex = 0; $shortIndex -lt $shortOptions.Length; $shortIndex++) {
                        $shortOption = [char]::ToString($shortOptions[$shortIndex])
                        if ($shortOption -in @('a', 'p', 'v')) { continue }
                        if ($shortOption -in @('o', 'f')) {
                            if ($shortIndex -eq $shortOptions.Length - 1) {
                                if ($index + 1 -ge $arguments.Count) { return $true }
                                $index++
                            }
                            break
                        }
                        return $true
                    }
                    $index++
                    continue
                }
                break
            }
            $current = @($arguments | Select-Object -Skip $index)
            continue
        }
        if ($programName -in @('command', 'nohup', 'nice', 'stdbuf', 'setsid', 'sudo', 'doas', 'busybox')) {
            if ($programName -eq 'command' -and @($arguments | Where-Object { (ConvertFrom-WorkflowShellToken -Token ([string]$_)) -in @('-v', '-V') }).Count -gt 0) { return $false }
            $index = 0
            while ($index -lt $arguments.Count) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$arguments[$index])
                if ($option -eq '--') { $index++; break }
                if (-not $option.StartsWith('-', [StringComparison]::Ordinal)) { break }
                if (($programName -eq 'sudo' -or $programName -eq 'doas') -and $option -match '^(?:-u|-g|-h|-p|-C|-D|-R|-T|-U|-r|-a|--user|--group|--host|--pro(?:mpt)|--close-from|--chdir|--chroot|--command-timeout|--other-user|--role|--type)$') { $index += 2; continue }
                if ($programName -eq 'nice' -and $option -in @('-n', '--adjustment')) { $index += 2; continue }
                if ($programName -eq 'stdbuf' -and $option -match '^(?:-i|-o|-e)$') { $index += 2; continue }
                if ($programName -eq 'busybox') { $index++; break }
                if ($option -match '^-[a-zA-Z]+(?:=.*)?$' -or $option -match '^--[a-zA-Z0-9-]+(?:=.*)?$') { $index++; continue }
                return $true
            }
            $current = @($arguments | Select-Object -Skip $index)
            continue
        }

        if ($programName -match '^(?:bash|sh|dash|zsh|pwsh|powershell|python(?:\d+(?:\.\d+)*)?|node(?:js)?|ruby|perl)(?:\.exe)?$') {
            return Test-WorkflowInterpreterInvocation -Name $programName -Arguments $arguments -Shell $Shell -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory
        }
        if (Test-WorkflowRunnerTempReference -Value $program) { return $true }
        return $false
    }
    return $current.Count -gt 0
}

function Test-WorkflowRunContainsWrappedUnsupportedExecution {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [AllowNull()] [string] $Shell,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [string] $WorkingDirectory = '.',
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    if ($ArtifactOutputPaths.Count -gt 0 -and (Test-WorkflowRunMutatesPath -Text $Text)) { return $true }

    $runnerTempDirectory = $false
    $directoryStack = [Collections.Generic.Stack[bool]]::new()
    foreach ($segment in (Get-WorkflowShellCommandSegments -Text $Text -Shell $Shell)) {
        $tokens = @(Get-WorkflowShellTokens -Text $segment)
        if ($tokens.Count -eq 0) { continue }
        $program = (ConvertFrom-WorkflowShellToken -Token ([string]$tokens[0])).ToLowerInvariant()
        if ($program -in @('cd', 'chdir', 'sl', 'set-location', 'pushd', 'push-location', 'popd', 'pop-location')) {
            if ($program -in @('pushd', 'push-location')) { $directoryStack.Push($runnerTempDirectory) }
            if ($program -in @('popd', 'pop-location')) {
                if ($directoryStack.Count -gt 0) { $runnerTempDirectory = $directoryStack.Pop() }
                else { $runnerTempDirectory = $false }
                continue
            }
            if (Test-WorkflowRunnerTempReference -Value $segment) { $runnerTempDirectory = $true }
            elseif ($segment -match '(?i)GITHUB_WORKSPACE|github\.workspace') { $runnerTempDirectory = $false }
            continue
        }
        $programPath = ConvertFrom-WorkflowShellToken -Token ([string]$tokens[0])
        if ($runnerTempDirectory -and $programPath -match '^(?:\./|\.\\)[^\\/]+') { return $true }
        if ($tokens.Count -gt 0 -and (Test-WorkflowCommandContainsUnsupportedExecution -Tokens $tokens -Shell $Shell -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory -NoDefaultCurrentDirectoryInExePath:$NoDefaultCurrentDirectoryInExePath)) { return $true }
    }
    return $false
}
function Test-WorkflowRunContainsUnsupportedEvaluator {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Text,
        [AllowNull()] [string] $Shell,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [string] $WorkingDirectory = '.',
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    $runText = ConvertTo-WorkflowLogicalRunText -Text $Text -Shell $Shell
    if ($ArtifactOutputPaths.Count -gt 0 -and $runText -match '(?im)(?:^|[;&|({\r\n]\s*)@?\s*(?:cd|chdir|pushd|popd|sl|Set-Location|Push-Location|Pop-Location)\b') { return $true }
    if ($ArtifactOutputPaths.Count -gt 0 -and $runText -match '(?i)(?:\[(?:System\.)?Environment\]::CurrentDirectory\s*=|\[(?:System\.)?(?:IO\.)?Directory\]::SetCurrentDirectory\s*\()') { return $true }
    if ($ArtifactOutputPaths.Count -gt 0 -and (Test-WorkflowCmdShell -Shell $Shell) -and $runText -match '(?im)(?:^|[;&|({\r\n]\s*)@?\s*(?:call|start|for|if)\b') { return $true }
    $pathMutationPatterns = @(
        '(?i)(?:^|[;&|({\r\n]\s*)(?:export\s+)?PATH\s*\+?=\s*[^;\r\n]*(?:runner\.temp|runner_temp|%runner_temp%)',
        '(?i)(?:^|[;&|({\r\n]\s*)\$env:PATH\s*\+?=\s*[^;\r\n]*(?:runner\.temp|runner_temp|%runner_temp%)',
        '(?i)(?:^|[;&|({\r\n]\s*)env(?:\.exe)?\s+(?:[^;&|\r\n]*\s)?PATH\s*=\s*[^;\r\n]*(?:runner\.temp|runner_temp|%runner_temp%)',
        '(?i)(?:^|[;&|({\r\n]\s*)(?:Set-Item|New-Item)\s+[^;&|\r\n]*(?:Env:)?PATH\b[^;&|\r\n]*(?:runner\.temp|runner_temp|%runner_temp%)'
    )
    foreach ($pattern in $pathMutationPatterns) {
        if ($runText -match $pattern) { return $true }
    }
    $patterns = @(
        '(?i)(?<![A-Za-z0-9_.-])(?:bash|sh|dash|zsh)(?:\.exe)?["'']?\s+(?:[^;&|\r\n]*?\s)?(?:-[A-Za-z]*c[A-Za-z]*|--command(?:=|\s))(?=\s|$)',
        '(?i)(?<![A-Za-z0-9_.-])(?:pwsh|powershell)(?:\.exe)?["'']?\s+(?:[^;&|\r\n]*?\s)?-(?:c|commandwithargs|command|encodedcommand|enc|e)(?:=|\s|$)',
        '(?i)(?<![A-Za-z0-9_.-])cmd(?:\.exe)?["'']?\s+(?:[^;&|\r\n]*?\s)?/(?:c|k)(?=\s|$)',
        '(?i)(?<![A-Za-z0-9_.-])(?:python(?:\d+(?:\.\d+)*)?|node|ruby|perl)(?:\.exe)?["'']?\s+(?:[^;&|\r\n]*?\s)?-(?:c|e)(?=\s|$)',
        '(?im)(?:^|[;&|({]\s*|\b(?:then|do|if|while|until)\s+)(?:&\s*)?(?:Invoke-Expression|iex|Invoke-Command|icm|Start-Process|eval|source)(?=\s|$|[;&|])',
        '(?i)(?<![A-Za-z0-9_.-])ScriptBlock\s*::\s*Create\s*\('
    )
    foreach ($pattern in $patterns) {
        if ($runText -match $pattern) { return $true }
    }
    if (Test-WorkflowRunContainsWrappedUnsupportedExecution -Text $runText -Shell $Shell -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory -NoDefaultCurrentDirectoryInExePath:$NoDefaultCurrentDirectoryInExePath) { return $true }

    # The source guard cannot inspect code supplied on stdin, through a here-document,
    # or through a script outside the pinned checkout. Only static scripts/ paths are
    # allowed as interpreter inputs; the workflow parser checks those paths are tracked.
    $supportedInterpreters = '(?:bash|sh|dash|zsh|pwsh|powershell|python(?:\d+(?:\.\d+)*)?|node(?:js)?|ruby|perl)'
    $absoluteInterpreterPattern = '(?im)(?:^|[;&|({\r\n]\s*|\b(?:then|do|if|while|until)\s+)(?:(?:env|command|exec|time|nohup|sudo)(?:\.exe)?\s+)*(?:&\s*)?(?:''[^''\r\n]*[\\/]' + $supportedInterpreters + '(?:\.exe)?''|"[^"\r\n]*[\\/]' + $supportedInterpreters + '(?:\.exe)?"|(?:[A-Za-z]:)?[\\/](?:[^\\/\s;&|()]+[\\/])*' + $supportedInterpreters + '(?:\.exe)?)(?=\s|$)'
    if ($runText -match $absoluteInterpreterPattern) { return $true }
    $interpreterPattern = '(?im)(?:^|[;&|({\r\n]\s*|\b(?:then|do|if|while|until)\s+)(?:!\s*)?(?:(?:env|command|exec|time|nohup|sudo)(?:\.exe)?\s+)*(?:&\s*)?(?<interpreter>' + $supportedInterpreters + ')(?:\.exe)?(?<arguments>[^;&|)\r\n]*)'
    foreach ($invocation in [regex]::Matches($runText, $interpreterPattern)) {
        $arguments = [string]$invocation.Groups['arguments'].Value
        $scriptArguments = [regex]::Matches($arguments, '(?i)(?:"[^"\r\n]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl)"|''[^''\r\n]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl)''|[^\s;&|]+\.(?:ps1|sh|cmd|bat|py|js|rb|pl))')
        $unsafeScriptArgument = $false
        foreach ($scriptArgument in $scriptArguments) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $scriptArgument.Value -Shell $Shell)) { $unsafeScriptArgument = $true; break }
        }
        if ($unsafeScriptArgument) { return $true }
        if ($scriptArguments.Count -gt 0) { continue }

        if ($arguments -match '(?i)(?:^|\s)(?:-s|--stdin|-|/dev/stdin|/dev/fd/\d+|CON)(?=\s|$)|(?:<<?<?\s*(?:/dev/stdin|/dev/fd/\d+|CON))|(?:<<<?)|(?:\$\(|<\()') { return $true }
        if ($runText -match '<<<?' -and [string]::IsNullOrWhiteSpace($arguments)) { return $true }
        $nonVersionArguments = $arguments.Trim() -replace '(?i)(?:^|\s)(?:--?(?:version|help)|-v)(?=\s|$)', ''
        if (-not [string]::IsNullOrWhiteSpace($nonVersionArguments) -or [string]::IsNullOrWhiteSpace($arguments)) { return $true }
    }

    if ($Shell -match '^(?i:bash|sh|dash|zsh)$') {
        foreach ($dotSource in [regex]::Matches($runText, '(?im)(?:^|[;&|({]\s*)\.\s+(?<path>"[^"\r\n]+"|''[^''\r\n]+''|[^\s;&|]+)')) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $dotSource.Groups['path'].Value -Shell $Shell)) { return $true }
        }
    }
    if ($Shell -match '^(?i:pwsh|powershell)$') {
        foreach ($fileArgument in [regex]::Matches($runText, '(?i)(?<![A-Za-z0-9_.-])(?:pwsh|powershell)(?:\.exe)?[^;&|\r\n]*?\s-(?:file|f)(?:=|\s+)(?<path>"[^"\r\n]*"|''[^''\r\n]*''|[^\s;&|]+)')) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $fileArgument.Groups['path'].Value -Shell $Shell)) { return $true }
        }
        foreach ($dotSource in [regex]::Matches($runText, '(?im)(?:^|[;&|({]\s*)\.\s+(?<path>"[^"\r\n]+"|''[^''\r\n]+''|[^\s;&|]+)')) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $dotSource.Groups['path'].Value -Shell $Shell)) { return $true }
        }
        foreach ($scriptInvocation in [regex]::Matches($runText, '(?im)(?:^|[;&|({]\s*)&\s*(?<path>"[^"\r\n]+"|''[^''\r\n]+''|[^\s;&|]+)')) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $scriptInvocation.Groups['path'].Value -Shell $Shell)) { return $true }
        }
        foreach ($directScript in [regex]::Matches($runText, '(?im)(?:^|[;&|({]\s*)(?<path>"[^"\r\n]+\.ps1"|''[^''\r\n]+\.ps1''|[^\s;&|]+\.ps1)(?=\s|$|[;&|])')) {
            if (-not (Test-WorkflowPinnedScriptArgument -Token $directScript.Groups['path'].Value -Shell $Shell)) { return $true }
        }
    }
    return $false
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

function Test-WorkflowWriteTargetIsOutsideWorkspace {
    param([AllowEmptyString()] [string] $Token, [switch] $RunnerTempMayBeRedirected)

    if ([string]::IsNullOrWhiteSpace($Token)) { return $false }
    $raw = $Token.Trim()
    $value = ConvertFrom-WorkflowShellToken -Token $raw
    $normalized = $value.Replace('\', '/')
    $singleQuotedLiteral = $raw.StartsWith("'", [StringComparison]::Ordinal) -and $raw.EndsWith("'", [StringComparison]::Ordinal)
    if ($singleQuotedLiteral -and $normalized -match '(?:\$|%[A-Za-z_][A-Za-z0-9_]*%)') { return $false }

    if ($normalized -match '^(?i:NUL(?::)?|/dev/null)$') { return $true }
    $safeExternalRoot = $null

    # Only the immutable workflow expression proves an external temp path. Shell RUNNER_TEMP is mutable.
    if ($null -eq $safeExternalRoot -and $normalized -match '^(?i:(?<root>\$\{\{\s*runner\.temp\s*\}\})(?<suffix>/.*)?$)') {
        if ($singleQuotedLiteral) { return $false }
        if ($RunnerTempMayBeRedirected) { return $false }
        $safeExternalRoot = $Matches['root']
        $suffix = $Matches['suffix']
    }
    if ($null -ne $safeExternalRoot) {
        if ([string]$suffix -match '[\$`%*?\[\]{}]') { return $false }
        $depth = 0
        foreach ($component in ([string]$suffix).TrimStart('/') -split '/') {
            if ([string]::IsNullOrEmpty($component) -or $component -eq '.') { continue }
            if ($component -eq '..') { $depth--; if ($depth -lt 0) { return $false }; continue }
            $depth++
        }
        return $true
    }

    # Workspace expressions are known to target the checked-out source tree.
    if ($normalized -match '^(?i:\$env:GITHUB_WORKSPACE|\$GITHUB_WORKSPACE|\$\{GITHUB_WORKSPACE\}|\$\{\{\s*github\.workspace\s*\}\})(?:/|$)') { return $false }

    # A dynamic, absolute, or otherwise unrecognized target cannot be proven safe.
    return $false
}

function Test-WorkflowCpInvocationCreatesPathRedirect {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $runText = ConvertTo-WorkflowLogicalRunText -Text $Text -Shell $Shell
    foreach ($segment in (Get-WorkflowShellCommandSegments -Text $runText -Shell $Shell)) {
        $tokens = @(Get-WorkflowShellTokens -Text $segment)
        for ($commandIndex = 0; $commandIndex -lt $tokens.Count; $commandIndex++) {
            $program = (ConvertFrom-WorkflowShellToken -Token ([string]$tokens[$commandIndex])).Replace('\', '/')
            $programName = ($program -split '/')[-1]
            if ($programName -notmatch '^(?i:cp)(?:\.exe)?$') { continue }

            for ($optionIndex = $commandIndex + 1; $optionIndex -lt $tokens.Count; $optionIndex++) {
                $option = ConvertFrom-WorkflowShellToken -Token ([string]$tokens[$optionIndex])
                if ($option -ceq '--') { break }

                $skipNextToken = $false
                if ($option.StartsWith('--', [StringComparison]::Ordinal)) {
                    $longOption = ($option -split '=', 2)[0]
                    if ($longOption -ceq '--link' -or
                        ($longOption.Length -ge 3 -and '--link'.StartsWith($longOption, [StringComparison]::Ordinal)) -or
                        $longOption -ceq '--symbolic-link' -or
                        ($longOption.Length -ge 4 -and '--symbolic-link'.StartsWith($longOption, [StringComparison]::Ordinal))) {
                        return $true
                    }

                    # GNU getopt_long accepts unique prefixes. Keep the minimum
                    # unique prefixes for these argument-taking cp options here:
                    # --t, --su, and --refe (not the ambiguous --ref).
                    if ($option -notmatch '=' -and (
                        $longOption -ceq '--target-directory' -or
                        ($longOption.Length -ge 3 -and '--target-directory'.StartsWith($longOption, [StringComparison]::Ordinal)) -or
                        $longOption -ceq '--suffix' -or
                        ($longOption.Length -ge 4 -and '--suffix'.StartsWith($longOption, [StringComparison]::Ordinal)) -or
                        $longOption -ceq '--reference' -or
                        ($longOption.Length -ge 6 -and '--reference'.StartsWith($longOption, [StringComparison]::Ordinal))
                    )) {
                        $skipNextToken = $true
                    }
                }
                elseif ($option.Length -gt 1 -and $option.StartsWith('-', [StringComparison]::Ordinal)) {
                    $shortOptions = $option.Substring(1)
                    for ($shortIndex = 0; $shortIndex -lt $shortOptions.Length; $shortIndex++) {
                        $shortOption = [char]::ToString($shortOptions[$shortIndex])
                        if ($shortOption -ceq 'l' -or $shortOption -ceq 's') { return $true }
                        if ($shortOption -ceq 'S' -or $shortOption -ceq 't') {
                            $skipNextToken = $shortIndex -eq ($shortOptions.Length - 1)
                            break
                        }
                        if ($shortOption -ceq 'Z') { break }
                    }
                }
                else { continue }

                if ($skipNextToken) { $optionIndex++ }
            }
        }
    }
    return $false
}

function Test-WorkflowRunCreatesPathRedirect {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $runText = ConvertTo-WorkflowLogicalRunText -Text $Text -Shell $Shell
    if ($runText -match '(?i)(?<![A-Za-z0-9_.-])(?:ln|mklink)(?:\.exe)?["'']?(?=\s|$|[;&|])') { return $true }
    if ($runText -match '(?i)(?<![A-Za-z0-9_.-])fsutil(?:\.exe)?\s+hardlink\s+create\b') { return $true }
    if ($runText -match '(?i)(?<![A-Za-z0-9_.-])mount(?:\.exe)?\s+[^;&|\r\n]*(?:--bind\b|-o\s*(?:=\s*)?bind\b)') { return $true }
    if (Test-WorkflowCpInvocationCreatesPathRedirect -Text $runText -Shell $Shell) { return $true }
    if ($runText -match '(?i)(?:CreateSymbolicLink|CreateHardLink|CreateJunction)\s*\(') { return $true }
    if ($runText -match '(?is)(?<![A-Za-z0-9_.-])(?:New-Item|ni)(?:\.exe)?\b(?:(?![;&|\r\n]).)*?-(?:I(?:t(?:e(?:m(?:t(?:y(?:p(?:e)?)?)?)?)?)?)?|T(?:y(?:p(?:e)?)?)?)\s*(?:=|:)?\s*["'']?(?:Junction|SymbolicLink|HardLink|ReparsePoint)(?=["'']?\s|$|[;&|])') { return $true }
    if ($runText -match '(?is)(?<![A-Za-z0-9_.-])(?:New-Item|ni)(?:\.exe)?\b(?:(?![;&|\r\n]).)*?-(?:I(?:t(?:e(?:m(?:t(?:y(?:p(?:e)?)?)?)?)?)?)?|T(?:y(?:p(?:e)?)?)?)\s*(?:=|:)?\s*["'']?\$(?:env:)?[A-Za-z_]') { return $true }
    return $false
}

function Test-WorkflowRunMayRedirectRunnerTemp {
    param([Parameter(Mandatory)] [object] $Step)

    $runText = ConvertTo-WorkflowLogicalRunText -Text ([string]$Step.RunText) -Shell $Step.Shell
    if (Test-WorkflowRunContainsUnsupportedEvaluator -Text $runText -Shell $Step.Shell) { return $true }
    if (-not (Test-WorkflowRunCreatesPathRedirect -Text $runText -Shell $Step.Shell)) { return $false }
    $workingDirectory = if ($null -ne $Step.WorkingDirectory) { [string]$Step.WorkingDirectory } else { '' }
    $pathContext = $runText + "`n" + $workingDirectory
    return $pathContext -match '(?i)runner[._-]temp|runner\.temp'
}

function Test-WorkflowShellEscapeSequence {
    param([AllowNull()] [string] $Shell, [char] $Quote, [char] $Escape, [char] $Next)

    if ($Shell -match '^(?i:pwsh|powershell)$') { return $Escape -eq '`' }
    if ($Shell -match '^(?i:bash|sh)$' -and $Escape -eq '\') {
        if ($Quote -eq "'") { return $false }
        if ($Quote -eq '"') { return $Next -in @('"', '$', '\', '`', "`n") }
        return $true
    }
    return $false
}

function Get-WorkflowShellRedirectionTargets {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $targets = [Collections.Generic.List[string]]::new()
    $quote = [char]0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ($quote -ne [char]0) {
            if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote $quote -Escape $character -Next $Text[$index + 1])) { $index++; continue }
            if ($character -eq $quote) { $quote = [char]0; continue }
            continue
        }
        if ($character -eq '"' -or $character -eq "'") { $quote = $character; continue }
        if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote ([char]0) -Escape $character -Next $Text[$index + 1])) { $index++; continue }
        if ($character -eq '#' -and ($index -eq 0 -or [char]::IsWhiteSpace($Text[$index - 1]))) {
            while ($index -lt $Text.Length -and $Text[$index] -ne "`n") { $index++ }
            continue
        }
        if ($character -ne '>' -or ($index -gt 0 -and $Text[$index - 1] -eq '<')) { continue }

        $operatorEnd = $index + 1
        if ($operatorEnd -lt $Text.Length -and $Text[$operatorEnd] -eq '>') { $operatorEnd++ }
        $targetStart = $operatorEnd
        while ($targetStart -lt $Text.Length -and [char]::IsWhiteSpace($Text[$targetStart])) { $targetStart++ }
        if ($targetStart -lt $Text.Length -and $Text[$targetStart] -eq '&') {
            $descriptor = [regex]::Match($Text.Substring($targetStart), '^&(?:\d+|-)(?=$|[\s;&|])')
            if ($descriptor.Success) { $index = $targetStart + $descriptor.Length - 1; continue }
        }

        if ($targetStart -ge $Text.Length -or $Text[$targetStart] -in @(';', '|', '&', "`r", "`n")) {
            $targets.Add('')
            $index = $operatorEnd - 1
            continue
        }

        $targetEnd = $targetStart
        if ($Text[$targetStart] -eq '"' -or $Text[$targetStart] -eq "'") {
            $targetQuote = $Text[$targetStart]
            $targetEnd++
            while ($targetEnd -lt $Text.Length) {
                if ($targetEnd + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote $targetQuote -Escape $Text[$targetEnd] -Next $Text[$targetEnd + 1])) { $targetEnd += 2; continue }
                if ($Text[$targetEnd] -eq $targetQuote) { $targetEnd++; break }
                $targetEnd++
            }
            $targets.Add($Text.Substring($targetStart, [Math]::Min($targetEnd, $Text.Length) - $targetStart))
        }
        else {
            while ($targetEnd -lt $Text.Length -and -not [char]::IsWhiteSpace($Text[$targetEnd]) -and $Text[$targetEnd] -notin @(';', '|', '&')) { $targetEnd++ }
            $targets.Add($Text.Substring($targetStart, $targetEnd - $targetStart))
        }
        $index = [Math]::Max($index, $targetEnd - 1)
    }
    return $targets.ToArray()
}

function Get-WorkflowDirectWriterInvocations {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text, [AllowNull()] [string] $Shell)

    $names = @('Invoke-RestMethod', 'Invoke-WebRequest', 'Copy-Item', 'Set-Content', 'Add-Content', 'Clear-Content', 'Out-File', 'Tee-Object', 'New-Item', 'Move-Item', 'Remove-Item', 'Rename-Item', 'robocopy', 'xcopy', 'curl.exe', 'wget.exe', 'curl', 'wget', 'truncate', 'install', 'touch', 'erase', 'rename', 'copy', 'move', 'iwr', 'irm', 'cp', 'mv', 'rm', 'del', 'tee', 'sed', 'perl', 'time.exe', 'time')
    $names = @($names | Sort-Object { $_.Length } -Descending)
    $invocations = [Collections.Generic.List[object]]::new()
    if ($Shell -match '^(?i:pwsh|powershell)$') {
        $quotedCommands = [regex]::Matches($Text, '(?i)&\s*["''](?:[A-Za-z0-9_.-]+\\)?(?<name>Invoke-WebRequest|Invoke-RestMethod|iwr|irm)["''](?=\s|$|[;&|])')
        foreach ($quotedCommand in $quotedCommands) {
            $invocations.Add([pscustomobject]@{ Name = $quotedCommand.Groups['name'].Value; End = $quotedCommand.Index + $quotedCommand.Length })
        }
    }
    $quote = [char]0
    for ($index = 0; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ($quote -ne [char]0) {
            if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote $quote -Escape $character -Next $Text[$index + 1])) { $index++; continue }
            if ($character -eq $quote) { $quote = [char]0; continue }
            continue
        }
        if ($character -eq '"' -or $character -eq "'") { $quote = $character; continue }
        if ($index + 1 -lt $Text.Length -and (Test-WorkflowShellEscapeSequence -Shell $Shell -Quote ([char]0) -Escape $character -Next $Text[$index + 1])) { $index++; continue }
        if ($character -eq '#' -and ($index -eq 0 -or [char]::IsWhiteSpace($Text[$index - 1]))) {
            while ($index -lt $Text.Length -and $Text[$index] -ne "`n") { $index++ }
            continue
        }
        if ($index -gt 0 -and -not [char]::IsWhiteSpace($Text[$index - 1]) -and $Text[$index - 1] -notin @(';', '|', '&', '{', '}', '(', ')', '=', '\', '/')) { continue }

        foreach ($name in $names) {
            if ($index + $name.Length -gt $Text.Length -or -not $Text.Substring($index, $name.Length).Equals($name, [StringComparison]::OrdinalIgnoreCase)) { continue }
            $end = $index + $name.Length
            if ($end -lt $Text.Length -and -not [char]::IsWhiteSpace($Text[$end]) -and $Text[$end] -notin @(';', '|', '&', '}', ')')) { continue }
            $invocations.Add([pscustomobject]@{ Name = $name; End = $end })
            $index = $end - 1
            break
        }
    }
    return $invocations.ToArray()
}

function Get-WorkflowDirectWriterTargets {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [string[]] $Arguments)

    $lowerName = $Name.ToLowerInvariant()
    if ($lowerName -in @('time', 'time.exe')) {
        $targets = [Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $value = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[$index])
            if ($value -match '^(?i:--output)=(?<path>.*)$') {
                $targets.Add([string]$Matches['path'])
                continue
            }
            if ($value -eq '--output' -or $value -eq '-o') {
                if ($index + 1 -lt $Arguments.Count) { $targets.Add((ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]))) }
                else { $targets.Add('') }
                continue
            }
            if ($value -match '^(?i:-o)(?<path>.+)$') {
                $targets.Add([string]$Matches['path'])
            }
        }
        return $targets.ToArray()
    }
    if ($lowerName -in @('curl', 'curl.exe')) {
        $targets = [Collections.Generic.List[string]]::new()
        $responseTargets = [Collections.Generic.List[string]]::new()
        $outputDirectory = $null
        $hasRemoteName = $false
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $value = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[$index])
            if ($value -notmatch '^-') { continue }
            if ($value -match '^(?i:--)(?<option>[a-z0-9-]+)(?:=(?<inline>.*))?$') {
                $option = $Matches['option'].ToLowerInvariant()
                $inline = if ($Matches.ContainsKey('inline') -and $value.Contains('=')) { [string]$Matches['inline'] } else { $null }
                if ($option -eq 'config') {
                    if ($null -eq $inline -and $index + 1 -lt $Arguments.Count) { $index++ }
                    # A config file can redirect the response, headers, stderr, and
                    # other curl sinks. Its contents are not part of this command's
                    # statically proven arguments, so fail closed.
                    $targets.Add('')
                    continue
                }
                if ($option -eq 'remote-name' -or $option -eq 'remote-name-all') { $hasRemoteName = $true; continue }
                if ($option -eq 'output-dir') {
                    if ($null -ne $inline) { $outputDirectory = $inline }
                    elseif ($index + 1 -lt $Arguments.Count) { $outputDirectory = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]) }
                    else { $outputDirectory = '' }
                    continue
                }
                if ($option -notin @('output', 'dump-header', 'stderr', 'trace', 'trace-ascii', 'cookie-jar', 'libcurl', 'write-out', 'alt-svc', 'etag-save', 'hsts')) { continue }
                if ($null -eq $inline) {
                    if ($index + 1 -lt $Arguments.Count) { $inline = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]) }
                    else { $inline = '' }
                }
                if ($option -eq 'write-out') { foreach ($target in (Get-WorkflowCurlWriteOutTargets -Format ([string]$inline))) { $targets.Add([string]$target) }; continue }
                if ($option -eq 'output' -or $option -eq 'dump-header') { $responseTargets.Add([string]$inline); continue }
                if ($option -in @('libcurl', 'stderr', 'trace', 'trace-ascii', 'cookie-jar', 'alt-svc', 'etag-save', 'hsts')) { $targets.Add([string]$inline); continue }
                continue
            }
            if ($value.Length -lt 2 -or $value[0] -ne '-') { continue }
            $flags = $value.Substring(1)
            if ($flags.StartsWith('-', [StringComparison]::Ordinal)) { continue }
            for ($flagIndex = 0; $flagIndex -lt $flags.Length; $flagIndex++) {
                $flag = [char]::ToString($flags[$flagIndex])
                if ($flag -ceq 'O') { $hasRemoteName = $true; continue }
                if ($flag -ceq 'K') {
                    if ($flagIndex -eq $flags.Length - 1 -and $index + 1 -lt $Arguments.Count) { $index++ }
                    $targets.Add('')
                    break
                }
                if ($flag -cnotin @('o', 'D', 'c', 'w')) { continue }
                $argument = $flags.Substring($flagIndex + 1)
                if ($argument.StartsWith('=', [StringComparison]::Ordinal)) { $argument = $argument.Substring(1) }
                if ([string]::IsNullOrEmpty($argument)) {
                    if ($index + 1 -lt $Arguments.Count) { $argument = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]) }
                    else { $argument = '' }
                }
                if ($flag -ceq 'w') { foreach ($target in (Get-WorkflowCurlWriteOutTargets -Format ([string]$argument))) { $targets.Add([string]$target) } }
                elseif ($flag -ceq 'o' -or $flag -ceq 'D') { $responseTargets.Add([string]$argument) }
                else { $targets.Add([string]$argument) }
                break
            }
        }
        if ($hasRemoteName) { $responseTargets.Add('__remote_name__') }
        $hasFileResponseTarget = @($responseTargets | Where-Object { $_ -ne '-' }).Count -gt 0
        if ($null -ne $outputDirectory -and $hasFileResponseTarget) { $targets.Add(''); return $targets.ToArray() }
        foreach ($target in $responseTargets) {
            if ($target -eq '-') { continue }
            if ($target -eq '__remote_name__') { $targets.Add('') }
            else { $targets.Add([string]$target) }
        }
        return $targets.ToArray()
    }

    if ($lowerName -in @('wget', 'wget.exe')) {
        $targets = [Collections.Generic.List[string]]::new()
        $hasDocumentOutput = $false
        $hasSpider = $false
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $value = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[$index])
            if ($value -match '^(?i:--)(?<option>[a-z0-9-]+)(?:=(?<inline>.*))?$') {
                $option = $Matches['option'].ToLowerInvariant()
                $inline = if ($Matches.ContainsKey('inline') -and $value.Contains('=')) { [string]$Matches['inline'] } else { $null }
                if ($option -eq 'spider') { $hasSpider = $true; continue }
                if ($option -eq 'warc-cdx') { $targets.Add(''); continue }
                if ($option -notin @('output-document', 'output-file', 'append-output', 'save-cookies', 'warc-file', 'warc-tempdir')) { continue }
                if ($null -eq $inline) {
                    if ($index + 1 -lt $Arguments.Count) { $inline = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]) }
                    else { $inline = '' }
                }
                if ($option -eq 'output-document') { $hasDocumentOutput = $true }
                # Wget documents `-` as stdout only for --output-document. For all
                # other file sinks, treat it as a possible workspace filename.
                if ($option -ne 'output-document' -or $inline -ne '-') { $targets.Add([string]$inline) }
                continue
            }
            if ($value.Length -lt 2 -or $value[0] -ne '-') { continue }
            $flags = $value.Substring(1)
            if ($flags.StartsWith('-', [StringComparison]::Ordinal)) { continue }
            for ($flagIndex = 0; $flagIndex -lt $flags.Length; $flagIndex++) {
                $flag = [char]::ToString($flags[$flagIndex])
                if ($flag -cnotin @('O', 'o', 'a')) { continue }
                $argument = $flags.Substring($flagIndex + 1)
                if ($argument.StartsWith('=', [StringComparison]::Ordinal)) { $argument = $argument.Substring(1) }
                if ([string]::IsNullOrEmpty($argument)) {
                    if ($index + 1 -lt $Arguments.Count) { $argument = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[++$index]) }
                    else { $argument = '' }
                }
                if ($flag -ceq 'O') { $hasDocumentOutput = $true }
                # Only -O - is documented as stdout. Log/cookie sinks may write
                # a literal file named '-', so keep those targets fail-closed.
                if ($flag -cne 'O' -or $argument -ne '-') { $targets.Add([string]$argument) }
                break
            }
        }
        if (-not $hasDocumentOutput -and -not $hasSpider) { $targets.Add('') }
        return $targets.ToArray()
    }

    if ($lowerName -in @('invoke-webrequest', 'invoke-restmethod', 'iwr', 'irm')) {
        $targets = [Collections.Generic.List[string]]::new()
        for ($index = 0; $index -lt $Arguments.Count; $index++) {
            $value = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[$index])
            $separator = $value.IndexOfAny(@('=', ':'))
            $option = if ($separator -ge 0) { $value.Substring(0, $separator).ToLowerInvariant() } else { $value.ToLowerInvariant() }
            if ($option -eq '-out-file') { $matchesOutFile = $true }
            else {
                $parameter = $option.TrimStart('-')
                $parameterCandidates = @('outfile', 'outbuffer', 'outvariable')
                $matchingParameters = @($parameterCandidates | Where-Object { $_.StartsWith($parameter, [StringComparison]::OrdinalIgnoreCase) })
                $matchesOutFile = $matchingParameters.Count -eq 1 -and $matchingParameters[0] -eq 'outfile'
                if (-not $matchesOutFile -and $matchingParameters -contains 'outfile') { $targets.Add(''); continue }
            }
            if (-not $matchesOutFile) { continue }
            if ($separator -ge 0) {
                $target = $value.Substring($separator + 1)
                if ([string]::IsNullOrEmpty($target) -and $index + 1 -lt $Arguments.Count) {
                    $index++
                    $target = [string]$Arguments[$index]
                }
                $targets.Add($target)
            }
            elseif ($index + 1 -lt $Arguments.Count) {
                $index++
                $targets.Add([string]$Arguments[$index])
            }
            else { $targets.Add('') }
        }
        return $targets.ToArray()
    }
    $pathOptions = @('-path', '-literalpath', '-filepath', '-destination', '-target', '--target-directory', '-t')
    $namedTargets = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $argument = [string]$Arguments[$index]
        $value = ConvertFrom-WorkflowShellToken -Token $argument
        $separator = $value.IndexOfAny(@('=', ':'))
        $option = if ($separator -ge 0) { $value.Substring(0, $separator).ToLowerInvariant() } else { $value.ToLowerInvariant() }
        if ($option -notin $pathOptions) { continue }
        if ($separator -ge 0) {
            $target = $value.Substring($separator + 1)
            if ([string]::IsNullOrEmpty($target) -and $index + 1 -lt $Arguments.Count) {
                $index++
                $target = [string]$Arguments[$index]
            }
            $namedTargets.Add($target)
        }
        elseif ($index + 1 -lt $Arguments.Count) {
            $index++
            $namedTargets.Add([string]$Arguments[$index])
        }
        else { $namedTargets.Add('') }
    }
    if ($namedTargets.Count -gt 0) { return $namedTargets.ToArray() }

    $positional = [Collections.Generic.List[string]]::new()
    for ($index = 0; $index -lt $Arguments.Count; $index++) {
        $value = ConvertFrom-WorkflowShellToken -Token ([string]$Arguments[$index])
        if ([string]::IsNullOrWhiteSpace($value) -or $value.StartsWith('-', [StringComparison]::Ordinal) -or $value -in @('{', '}', 'then', 'fi', 'do', 'done')) { continue }
        $positional.Add([string]$Arguments[$index])
    }

    $lowerName = $Name.ToLowerInvariant()
    if ($lowerName -in @('copy-item', 'copy', 'cp', 'xcopy', 'robocopy')) {
        if ($positional.Count -lt 2) { return @('') }
        return @($positional[$positional.Count - 1])
    }
    if ($lowerName -in @('set-content', 'add-content', 'clear-content', 'out-file', 'tee-object', 'new-item')) {
        if ($positional.Count -eq 0) { return @('') }
        return @($positional[0])
    }
    if ($lowerName -in @('sed', 'perl')) {
        if (@($Arguments | Where-Object { (ConvertFrom-WorkflowShellToken -Token ([string]$_)) -match '^-i(?:.*)?$' }).Count -eq 0) { return @() }
        if ($positional.Count -eq 0) { return @('') }
        return $positional.ToArray()
    }
    if ($positional.Count -eq 0) { return @('') }
    return $positional.ToArray()
}

function Test-WorkflowRunHasWorkspaceWrite {
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory,
        [switch] $RunnerTempMayBeRedirected,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    $runText = ConvertTo-WorkflowLogicalRunText -Text ([string]$Step.RunText) -Shell $Step.Shell
    if (Test-WorkflowRunContainsUnsupportedEvaluator -Text $runText -Shell $Step.Shell -ArtifactOutputPaths $ArtifactOutputPaths -WorkingDirectory $WorkingDirectory -NoDefaultCurrentDirectoryInExePath:$NoDefaultCurrentDirectoryInExePath) { return $true }
    if (Test-WorkflowRunCreatesPathRedirect -Text $runText -Shell $Step.Shell) { return $true }
    foreach ($target in (Get-WorkflowShellRedirectionTargets -Text $runText -Shell $Step.Shell)) {
        if (-not (Test-WorkflowWriteTargetIsOutsideWorkspace -Token $target -RunnerTempMayBeRedirected:$RunnerTempMayBeRedirected)) { return $true }
    }

    $gitMatches = [regex]::Matches($runText, '(?i)(?<![A-Za-z0-9_.-])git(?:\.exe)?(?=\s|$)')
    foreach ($match in $gitMatches) {
        $tail = $runText.Substring($match.Index + $match.Length)
        $separator = [regex]::Match($tail, '[;&|\r\n]')
        if ($separator.Success) { $tail = $tail.Substring(0, $separator.Index) }
        $words = @(Get-WorkflowShellTokens -Text $tail)
        for ($index = 0; $index -lt $words.Count; $index++) {
            $word = ConvertFrom-WorkflowShellToken -Token ([string]$words[$index])
            $target = $null
            if ($word -match '^(?i:--out(?:put(?:-file)?)?)(?:=(?<path>.*))?$') {
                if ($Matches.ContainsKey('path') -and $word.Contains('=')) {
                    $target = $Matches['path']
                    if ([string]::IsNullOrEmpty($target) -and $index + 1 -lt $words.Count) { $index++; $target = [string]$words[$index] }
                }
                elseif ($index + 1 -lt $words.Count) { $index++; $target = [string]$words[$index] }
                else { return $true }
            }
            if ($null -ne $target -and -not (Test-WorkflowWriteTargetIsOutsideWorkspace -Token $target -RunnerTempMayBeRedirected:$RunnerTempMayBeRedirected)) { return $true }
        }
    }

    foreach ($invocation in (Get-WorkflowDirectWriterInvocations -Text $runText -Shell $Step.Shell)) {
        $tailStart = [int]$invocation.End
        $tailEnd = $runText.Length
        $quote = [char]0
        for ($index = $tailStart; $index -lt $runText.Length; $index++) {
            $character = $runText[$index]
            if ($quote -ne [char]0) {
                if ($index + 1 -lt $runText.Length -and (Test-WorkflowShellEscapeSequence -Shell $Step.Shell -Quote $quote -Escape $character -Next $runText[$index + 1])) { $index++; continue }
                if ($character -eq $quote) { $quote = [char]0; continue }
                continue
            }
            if ($character -eq '"' -or $character -eq "'") { $quote = $character; continue }
            if ($index + 1 -lt $runText.Length -and (Test-WorkflowShellEscapeSequence -Shell $Step.Shell -Quote ([char]0) -Escape $character -Next $runText[$index + 1])) { $index++; continue }
            if ($character -in @(';', '|', '&', "`r", "`n")) { $tailEnd = $index; break }
        }
        $argumentsText = $runText.Substring($tailStart, $tailEnd - $tailStart)
        $arguments = @(Get-WorkflowShellTokens -Text $argumentsText)
        $targets = @(Get-WorkflowDirectWriterTargets -Name ([string]$invocation.Name) -Arguments $arguments)
        foreach ($target in $targets) {
            if (-not (Test-WorkflowWriteTargetIsOutsideWorkspace -Token ([string]$target) -RunnerTempMayBeRedirected:$RunnerTempMayBeRedirected)) { return $true }
        }
    }
    return $false
}

function Get-WorkflowRunSourceEffect {
    param(
        [Parameter(Mandatory)] [object] $Step,
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $WorkingDirectory,
        [switch] $RunnerTempMayBeRedirected,
        [AllowEmptyCollection()] [object[]] $ArtifactOutputPaths = @(),
        [switch] $NoDefaultCurrentDirectoryInExePath
    )

    $runText = ConvertTo-WorkflowLogicalRunText -Text ([string]$Step.RunText) -Shell $Step.Shell
    $gitMatches = [regex]::Matches($runText, '(?i)(?<![A-Za-z0-9_.-])git(?:\.exe)?(?=\s|$)')
    $hasSourceMutation = $false
    $hasWorkspaceWrite = Test-WorkflowRunHasWorkspaceWrite -Step $Step -WorkingDirectory $WorkingDirectory -RunnerTempMayBeRedirected:$RunnerTempMayBeRedirected -ArtifactOutputPaths $ArtifactOutputPaths -NoDefaultCurrentDirectoryInExePath:$NoDefaultCurrentDirectoryInExePath
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
    if (-not $hasSourceMutation -and -not $hasWorkspaceWrite) { return 'None' }
    if ($hasWorkspaceWrite) { return 'Mutation' }
    if ($gitMatches.Count -gt 0 -and (Test-WorkflowStandalonePinnedGitRestore -Step $Step -WorkingDirectory $WorkingDirectory)) { return 'Restore' }
    return 'Mutation'
}

function Assert-WorkflowEnvironmentHasNoBashStartupOverride {
    param(
        [Parameter(Mandatory)] [Collections.Generic.Dictionary[string, string]] $Environment,
        [Parameter(Mandatory)] [string] $Context
    )

    foreach ($name in @('BASH_ENV', 'BASHOPTS')) {
        if ($Environment.ContainsKey($name)) {
            throw "$Context defines '$name'; Bash startup behavior cannot be proven safe."
        }
    }
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
    $scriptPattern = '(?i)(?<path>(?:[A-Za-z]:)?[\\/]*(?:[A-Za-z0-9_.-]+[\\/])*scripts[\\/][A-Za-z0-9_.-]+(?:[\\/][A-Za-z0-9_.-]+)*\.[A-Za-z0-9]+)'

    foreach ($workflow in $Workflows) {
        $workflowLines = @(([string]$workflow.Text) -split "`r?`n")
        $jobsLineIndex = -1
        for ($index = 0; $index -lt $workflowLines.Count; $index++) {
            if ([string]$workflowLines[$index] -match '^jobs:\s*(?:#.*)?$') { $jobsLineIndex = $index; break }
        }
        if ($jobsLineIndex -lt 0) { throw "Workflow '$($workflow.Name)' has no root jobs mapping." }
        $workflowWorkingDirectory = $null
        $workflowScope = @()
        if ($jobsLineIndex -gt 0) {
            $workflowScope = @($workflowLines[0..($jobsLineIndex - 1)])
            $workflowWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $workflowScope -DirectIndent 0
        }
        $workflowEnvironment = Get-WorkflowScopeEnvironmentEntries -ScopeLines $workflowScope -DirectIndent 0
        $workflowDefinesPath = $workflowEnvironment.ContainsKey('PATH')

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
            Assert-WorkflowEnvironmentHasNoBashStartupOverride -Environment $workflowEnvironment -Context "Workflow '$($workflow.Name)' environment"
            $runnerPathFlavor = Get-WorkflowRunnerPathFlavor -Job $job
            $jobEnvironment = Get-WorkflowScopeEnvironmentEntries -ScopeLines $job.Lines -DirectIndent $job.DirectIndent
            Assert-WorkflowEnvironmentHasNoBashStartupOverride -Environment $jobEnvironment -Context "Artifact-consuming job '$($workflow.Name)/$($job.Name)' environment"
            if ($workflowDefinesPath -or $jobEnvironment.ContainsKey('PATH')) {
                throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' defines PATH in workflow YAML; executable lookup cannot be proven safe."
            }

            $jobWorkingDirectory = Get-WorkflowDefaultWorkingDirectory -ScopeLines $job.Lines -DirectIndent $job.DirectIndent
            if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = $workflowWorkingDirectory }
            if ($null -eq $jobWorkingDirectory) { $jobWorkingDirectory = '.' }
            $resolvedJobWorkingDirectory = Resolve-WorkflowWorkspacePath -BasePath '' -Path $jobWorkingDirectory -RepositoryRoot $RepositoryRoot
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
                $resolvedStepWorkingDirectory = Resolve-WorkflowWorkspacePath -BasePath '' -Path $stepWorkingDirectory -RepositoryRoot $RepositoryRoot
                if ($null -eq $resolvedStepWorkingDirectory) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported step working-directory." }
                $effectiveCheckoutValid = $false
                $effectiveCheckoutFailure = 'no checkout has established a pinned workspace-root source'
                $latestDownloadHadValidSource = $false
                $latestDownloadFailure = $effectiveCheckoutFailure
                $runnerTempMayBeRedirected = $false
                $artifactOutputPaths = [Collections.Generic.List[object]]::new()
                $persistentPathMayResolveArtifact = $false
                for ($stateIndex = 0; $stateIndex -le $stepIndex; $stateIndex++) {
                    $stateStep = $steps[$stateIndex]
                    Assert-WorkflowEnvironmentHasNoBashStartupOverride -Environment $stateStep.EnvironmentValues -Context "Artifact-consuming job '$($workflow.Name)/$($job.Name)' step $($stateIndex + 1) environment"
                    if ($stateStep.RunText -match '(?i)\bGITHUB_ENV\b') {
                        throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' step $($stateIndex + 1) references GITHUB_ENV; persistent Bash startup state cannot be proven safe."
                    }
                    if ($stateStep.EnvironmentValues.ContainsKey('PATH')) {
                        throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' step $($stateIndex + 1) defines PATH in its environment."
                    }
                    if ($null -ne $stateStep.Uses -and $stateStep.Uses -match '(?i)^actions/download-artifact@') {
                        $latestDownloadHadValidSource = $effectiveCheckoutValid
                        $latestDownloadFailure = $effectiveCheckoutFailure
                        $artifactOutputPath = Get-WorkflowArtifactOutputLocation -Step $stateStep -Context "job '$($workflow.Name)/$($job.Name)' step $($stateIndex + 1)" -RepositoryRoot $RepositoryRoot -RunnerPathFlavor $runnerPathFlavor
                        $artifactOutputPaths.Add($artifactOutputPath)
                        if (Test-WorkflowArtifactOutputOverlapsTrackedFiles -OutputPath $artifactOutputPath -TrackedFiles $trackedFiles) {
                            $effectiveCheckoutValid = $false
                            $effectiveCheckoutFailure = 'artifact output can replace tracked workspace source'
                        }
                        if ($persistentPathMayResolveArtifact) {
                            $effectiveCheckoutValid = $false
                            $effectiveCheckoutFailure = 'a persistent PATH entry may resolve a downloaded artifact executable'
                        }
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
                            if ($effectiveCheckoutValid) {
                                for ($artifactIndex = $artifactOutputPaths.Count - 1; $artifactIndex -ge 0; $artifactIndex--) {
                                    if ($artifactOutputPaths[$artifactIndex].Kind -eq 'workspace') { $artifactOutputPaths.RemoveAt($artifactIndex) }
                                }
                            }
                        }
                        continue
                    }

                    if ($null -ne $stateStep.Uses) {
                        $supportedAction = ([string]$stateStep.Uses -split '@', 2)[0]
                        if ($supportedAction -notin @('actions/setup-dotnet', 'actions/upload-artifact')) {
                            throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' uses unsupported action '$($stateStep.Uses)' before a guarded script."
                        }
                        continue
                    }

                    if ([string]::IsNullOrWhiteSpace($stateStep.RunText)) { continue }
                    if (Test-WorkflowRunAddsPersistentPath -Text ([string]$stateStep.RunText)) { $persistentPathMayResolveArtifact = $true }
                    $stateWorkingDirectory = if ($null -ne $stateStep.WorkingDirectory) { $stateStep.WorkingDirectory } else { $resolvedJobWorkingDirectory }
                    $resolvedStateWorkingDirectory = Resolve-WorkflowWorkspacePath -BasePath '' -Path $stateWorkingDirectory -RepositoryRoot $RepositoryRoot
                    if ($null -eq $resolvedStateWorkingDirectory) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has an unsupported run working-directory." }
                    if (Test-WorkflowRunMayRedirectRunnerTemp -Step $stateStep) { $runnerTempMayBeRedirected = $true }
                    $noDefaultCurrentDirectoryInExePath = $false
                    foreach ($environment in @($workflowEnvironment, $jobEnvironment, $stateStep.EnvironmentValues)) {
                        if ($environment.ContainsKey('NoDefaultCurrentDirectoryInExePath')) {
                            $noDefaultCurrentDirectoryInExePath = Test-WorkflowEnvironmentValueDisablesCmdCurrentDirectory -Value $environment['NoDefaultCurrentDirectoryInExePath']
                        }
                    }
                    $sourceEffect = Get-WorkflowRunSourceEffect -Step $stateStep -WorkingDirectory $resolvedStateWorkingDirectory -RunnerTempMayBeRedirected:$runnerTempMayBeRedirected -ArtifactOutputPaths $artifactOutputPaths.ToArray() -NoDefaultCurrentDirectoryInExePath:$noDefaultCurrentDirectoryInExePath
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
                            if (-not $effectiveCheckoutValid) {
                                $effectiveCheckoutFailure = 'pinned shell checkout does not restore modified tracked bytes'
                            }

                        }
                    }
                    elseif ($sourceEffect -eq 'Mutation') {
                        $effectiveCheckoutValid = $false
                        $effectiveCheckoutFailure = 'a run step can change workspace source or has an unsupported write destination'
                    }
                }

                if ($artifactOutputPaths.Count -gt 0 -and $persistentPathMayResolveArtifact) {
                    $effectiveCheckoutValid = $false
                    $effectiveCheckoutFailure = 'a persistent PATH entry may resolve a downloaded artifact executable'
                }

                if (-not $latestDownloadHadValidSource) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has no valid workspace-root checkout of github.repository at github.sha before its last preceding artifact download: $latestDownloadFailure."
                }
                if (-not $effectiveCheckoutValid) {
                    throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' does not have an effective pinned workspace-root source when the script runs: $effectiveCheckoutFailure."
                }

                foreach ($scriptMatch in $scriptMatches) {
                    $invocationPath = $scriptMatch.Groups['path'].Value.Replace('\', '/')
                    $commandPrefix = ($step.RunText.Substring(0, $scriptMatch.Index) -split '[;&|\r\n]' | Select-Object -Last 1)
                    if ($commandPrefix -match '\$|%|`') { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' has a dynamic repository script path." }
                    $relativePath = Resolve-WorkflowWorkspacePath -BasePath $resolvedStepWorkingDirectory -Path $invocationPath -RepositoryRoot $RepositoryRoot
                    if ($null -eq $relativePath) { throw "Artifact-consuming job '$($workflow.Name)/$($job.Name)' invokes a repository script outside its pinned workspace." }
                    if (-not $tracked.Contains($relativePath)) { throw "Artifact consumer invokes repository script '$relativePath' that is not tracked." }
                    $absoluteScript = Join-Path $RepositoryRoot ($relativePath.Replace('/', [IO.Path]::DirectorySeparatorChar))
                    if (-not (Test-Path -LiteralPath $absoluteScript -PathType Leaf)) { throw "Artifact consumer invokes missing repository script '$relativePath'." }

                    $resolvedInvocation = Resolve-WorkflowWorkspacePath -BasePath $resolvedStepWorkingDirectory -Path $invocationPath -RepositoryRoot $RepositoryRoot
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
