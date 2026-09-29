[CmdletBinding()]
param(
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$helperPath = Join-Path $PSScriptRoot 'Invoke-NestedPwsh.ps1'
$guardPath = $PSCommandPath

function Get-ParsedCommandRecords(
    [string]$Text,
    [string]$Path,
    [System.Management.Automation.Language.Ast]$InitialAst
) {
    $pending = [System.Collections.Generic.Queue[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $pending.Enqueue([pscustomobject]@{
            Text = $Text
            Ast = $InitialAst
            BaseLine = 0
            Embedded = $false
        })

    while ($pending.Count -gt 0) {
        $item = $pending.Dequeue()
        if ([string]::IsNullOrWhiteSpace($item.Text) -or -not $seen.Add($item.Text)) {
            continue
        }

        $ast = $item.Ast
        if ($null -eq $ast) {
            $tokens = $null
            $parseErrors = $null
            $ast = [System.Management.Automation.Language.Parser]::ParseInput(
                $item.Text,
                [ref]$tokens,
                [ref]$parseErrors)
            if ($parseErrors.Count -gt 0) {
                continue
            }
        }

        foreach ($command in @($ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.CommandAst] }, $true))) {
            [pscustomobject]@{
                Path = $Path
                BaseLine = $item.BaseLine
                Command = $command
            }
        }

        foreach ($stringAst in @($ast.FindAll({
                    param($node)
                    $node -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
                    $node -is [System.Management.Automation.Language.ExpandableStringExpressionAst]
                }, $true))) {
            $value = [string]$stringAst.Value
            $isHereString = $stringAst -is [System.Management.Automation.Language.StringConstantExpressionAst] -and
                ([string]$stringAst.StringConstantType -match 'HereString')
            $isScriptLike = $value -match '(?i)(?:\r?\n|(?:^|\s)(?:Start-Process|pwsh(?:\.exe)?|powershell(?:\.exe)?|Invoke-Expression)\b\s+\S)'
            if (-not ($isHereString -or ($item.Embedded -and $isScriptLike))) {
                continue
            }

            $pending.Enqueue([pscustomobject]@{
                    Text = $value
                    Ast = $null
                    BaseLine = $item.BaseLine + $stringAst.Extent.StartLineNumber - 1
                    Embedded = $true
                })
        }
    }
}

function Get-LatestPowerShellAssignment(
    [object[]]$Assignments,
    [string]$VariableName,
    [int]$Offset
) {
    $latest = @($Assignments | Where-Object {
            $_.Extent.StartOffset -lt $Offset -and
            $_.Left -is [System.Management.Automation.Language.VariableExpressionAst] -and
            $_.Left.VariablePath.UserPath -eq $VariableName
        } | Sort-Object { $_.Extent.StartOffset } | Select-Object -Last 1)
    if ($latest.Count -eq 0) { return $null }
    return $latest[0]
}

function Get-LatestPowerShellStringAssignment(
    [object[]]$Assignments,
    [string]$VariableName,
    [int]$Offset
) {
    $assignment = Get-LatestPowerShellAssignment -Assignments $Assignments -VariableName $VariableName -Offset $Offset
    if ($null -eq $assignment) { return $null }
    $right = $assignment.Right
    if ($right -is [System.Management.Automation.Language.CommandExpressionAst]) { $right = $right.Expression }
    if ($right -is [System.Management.Automation.Language.StringConstantExpressionAst] -or
        $right -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
        return [string]$right.Value
    }
    return $null
}

function Get-NormalizedPowerShellCommandName([string]$Name) {
    if ([string]::IsNullOrWhiteSpace($Name)) { return $null }
    $lastSeparator = [Math]::Max($Name.LastIndexOf('\'), $Name.LastIndexOf('/'))
    if ($lastSeparator -ge 0) { return $Name.Substring($lastSeparator + 1) }
    return $Name
}

function Get-LaunchViolations([string]$Path) {
    $tokens = $null
    $parseErrors = $null
    $ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$parseErrors)
    if ($parseErrors.Count -gt 0) {
        return @("${Path} contains PowerShell parse errors.")
    }

    $violations = [System.Collections.Generic.List[string]]::new()
    $source = [IO.File]::ReadAllText($Path)
    $commands = @(Get-ParsedCommandRecords -Text $source -Path $Path -InitialAst $ast)
    $assignments = @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.AssignmentStatementAst]
            }, $true))
    $scriptBlockParameters = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($parameter in @($ast.FindAll({
                param($node)
                $node -is [System.Management.Automation.Language.ParameterAst]
            }, $true))) {
        if ($parameter.StaticType -eq [scriptblock] -or $parameter.StaticType.FullName -eq 'System.Management.Automation.ScriptBlock') {
            [void]$scriptBlockParameters.Add($parameter.Name.VariablePath.UserPath)
        }
    }
    foreach ($record in $commands) {
        $command = $record.Command
        $lineNumber = $record.BaseLine + $command.Extent.StartLineNumber
        $nameAst = $command.CommandElements[0]
        $dynamicCommandVariable = $null
        $commandName = if ($nameAst -is [System.Management.Automation.Language.StringConstantExpressionAst]) {
            $nameAst.Value
        }
        elseif ($nameAst -is [System.Management.Automation.Language.ExpandableStringExpressionAst]) {
            $nameAst.Value
        }
        elseif ($nameAst -is [System.Management.Automation.Language.VariableExpressionAst]) {
            $dynamicCommandVariable = $nameAst.VariablePath.UserPath
            Get-LatestPowerShellStringAssignment -Assignments $assignments -VariableName $dynamicCommandVariable -Offset $command.Extent.StartOffset
        }
        else {
            $null
        }

        if ($null -eq $commandName -and $null -ne $dynamicCommandVariable -and $command.Extent.Text.TrimStart().StartsWith('&')) {
            $assignment = Get-LatestPowerShellAssignment -Assignments $assignments -VariableName $dynamicCommandVariable -Offset $command.Extent.StartOffset
            $assignmentText = if ($null -eq $assignment) { '' } else { $assignment.Right.Extent.Text }
            $isKnownScriptBlock = $scriptBlockParameters.Contains($dynamicCommandVariable)
            $isKnownPathExpression = $assignmentText -match '(?i)\b(?:Join-Path|Resolve-Path)\b' -and
                $assignmentText -notmatch '(?i)\b(?:pwsh|powershell|Start-Process|Invoke-Expression|\bstart\b|\bsaps\b)\b'
            $isPathParameter = $dynamicCommandVariable -match '(?i)(?:path|filepath)$' -and $null -eq $assignment
            if (-not ($isKnownScriptBlock -or $isKnownPathExpression -or $isPathParameter)) {
                [void]$violations.Add("${Path}:$lineNumber`: dynamic PowerShell command '$dynamicCommandVariable' cannot be audited")
                continue
            }
            continue
        }
        if ($null -eq $commandName -and $command.Extent.Text.TrimStart().StartsWith('&')) {
            [void]$violations.Add("${Path}:$lineNumber`: non-literal PowerShell command cannot be audited")
            continue
        }
        $normalizedCommandName = Get-NormalizedPowerShellCommandName $commandName

        if ($normalizedCommandName -match '^(?i:pwsh|powershell)(?:\.exe)?$') {
            [void]$violations.Add("${Path}:$lineNumber`: direct nested PowerShell launch")
            continue
        }

        if ($normalizedCommandName -match '^(?i:invoke-expression)$') {
            [void]$violations.Add("${Path}:$lineNumber`: dynamic Invoke-Expression execution cannot be audited")
            continue
        }

        $literalArguments = @($command.CommandElements | Select-Object -Skip 1 | Where-Object {
                $_ -is [System.Management.Automation.Language.StringConstantExpressionAst]
            } | ForEach-Object { $_.Value })
        if ($normalizedCommandName -notmatch '^(?i:Invoke-NestedPwsh|Invoke-NestedProcess)$' -and
            $literalArguments | Where-Object { $_ -match '^(?i:pwsh|powershell)(?:\.exe)?$' }) {
            [void]$violations.Add("${Path}:$lineNumber`: nested PowerShell executable passed to '$commandName'")
        }

        $hasHiddenContainment = $command.Extent.Text -match '(?i)(?:-\s*WindowStyle\s*(?:=|\s)\s*[''"]?Hidden[''"]?(?=\s|$)|(?<!\w)-NoNewWindow(?=\s|$))'
        $isStartProcess = $normalizedCommandName -match '^(?i:Start-Process|start|saps)$'
        if (-not $hasHiddenContainment -and $isStartProcess) {
            foreach ($splat in @($command.CommandElements | Where-Object {
                        $_ -is [System.Management.Automation.Language.VariableExpressionAst] -and $_.Splatted
                    })) {
                $variableName = $splat.VariablePath.UserPath
                $basePattern = '^\$' + [regex]::Escape($variableName) + '$'
                $stylePattern = '^\$' + [regex]::Escape($variableName) + '(?:\.(?:WindowStyle|NoNewWindow)|\[[''"](?:WindowStyle|NoNewWindow)[''"]\])$'
                $latestBase = @($assignments | Where-Object {
                            $_.Extent.StartOffset -lt $command.Extent.StartOffset -and
                            $_.Left.Extent.Text -match $basePattern
                        } | Sort-Object { $_.Extent.StartOffset } | Select-Object -Last 1)
                $latestStyle = @($assignments | Where-Object {
                            $_.Extent.StartOffset -lt $command.Extent.StartOffset -and
                            $_.Left.Extent.Text -match $stylePattern
                        } | Sort-Object { $_.Extent.StartOffset } | Select-Object -Last 1)
                if (($latestBase.Count -gt 0 -and $latestBase[0].Right.Extent.Text -match '(?i)(?:WindowStyle\s*=\s*[''"]Hidden[''"]|NoNewWindow\s*=\s*\$true)') -or
                    ($latestStyle.Count -gt 0 -and $latestStyle[0].Right.Extent.Text -match '(?i)^(?:[''"]Hidden[''"]|\$true)$')) {
                    $hasHiddenContainment = $true
                    break
                }
            }
        }
        if ($isStartProcess -and -not $hasHiddenContainment) {
            [void]$violations.Add("${Path}:$lineNumber`: Start-Process lacks hidden-window containment")
        }
    }

    return $violations.ToArray()
}

function Append-CSharpMaskedRange(
    [Text.StringBuilder]$Builder,
    [string]$Text,
    [int]$Start,
    [int]$Length
) {
    for ($offset = 0; $offset -lt $Length; $offset++) {
        $character = $Text[$Start + $offset]
        if ($character -eq "`r" -or $character -eq "`n") { [void]$Builder.Append($character) }
        else { [void]$Builder.Append(' ') }
    }
}

function Get-CSharpInterpolatedPrefix([string]$Text, [int]$Index) {
    $dollarStart = $Index
    if ($Text[$Index] -eq '@' -and $Index + 1 -lt $Text.Length -and $Text[$Index + 1] -eq '$') { $dollarStart = $Index + 1 }
    elseif ($Text[$Index] -ne '$') { return $null }

    $dollarCount = 0
    while ($dollarStart + $dollarCount -lt $Text.Length -and $Text[$dollarStart + $dollarCount] -eq '$') { $dollarCount++ }
    $quoteStart = $dollarStart + $dollarCount
    $verbatim = $false
    if ($quoteStart -lt $Text.Length -and $Text[$quoteStart] -eq '@') { $verbatim = $true; $quoteStart++ }
    if ($quoteStart -ge $Text.Length -or $Text[$quoteStart] -ne '"') { return $null }

    $quoteCount = 0
    while ($quoteStart + $quoteCount -lt $Text.Length -and $Text[$quoteStart + $quoteCount] -eq '"') { $quoteCount++ }
    if ($quoteCount -eq 0) { return $null }
    return [pscustomobject]@{
        Start = $Index
        DollarCount = $dollarCount
        QuoteStart = $quoteStart
        QuoteCount = $quoteCount
        Verbatim = $verbatim
        Raw = $quoteCount -ge 3
    }
}

function Find-CSharpInterpolationEnd([string]$Text, [int]$Start, [int]$CloseBraceCount) {
    $depth = 1
    $state = 'Code'
    for ($index = $Start; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        $nextCharacter = if ($index + 1 -lt $Text.Length) { $Text[$index + 1] } else { [char]0 }
        if ($state -eq 'LineComment') { if ($character -eq "`r" -or $character -eq "`n") { $state = 'Code' }; continue }
        if ($state -eq 'BlockComment') { if ($character -eq '*' -and $nextCharacter -eq '/') { $index++; $state = 'Code' }; continue }
        if ($state -eq 'String') { if ($character -eq '\') { $index++ } elseif ($character -eq '"') { $state = 'Code' }; continue }
        if ($state -eq 'VerbatimString') { if ($character -eq '"' -and $nextCharacter -eq '"') { $index++ } elseif ($character -eq '"') { $state = 'Code' }; continue }
        if ($state -eq 'Char') { if ($character -eq '\') { $index++ } elseif ($character -eq "'") { $state = 'Code' }; continue }
        if ($character -eq '/' -and $nextCharacter -eq '/') { $index++; $state = 'LineComment'; continue }
        if ($character -eq '/' -and $nextCharacter -eq '*') { $index++; $state = 'BlockComment'; continue }
        if ($character -eq '@' -and $nextCharacter -eq '"') { $index++; $state = 'VerbatimString'; continue }
        if ($character -eq '"') { $state = 'String'; continue }
        if ($character -eq "'") { $state = 'Char'; continue }

        $openCount = 0
        while ($index + $openCount -lt $Text.Length -and $Text[$index + $openCount] -eq '{') { $openCount++ }
        if ($openCount -ge $CloseBraceCount) { $depth++; $index += $CloseBraceCount - 1; continue }
        $closeCount = 0
        while ($index + $closeCount -lt $Text.Length -and $Text[$index + $closeCount] -eq '}') { $closeCount++ }
        if ($closeCount -ge $CloseBraceCount) {
            $depth--
            if ($depth -eq 0) { return $index }
            $index += $CloseBraceCount - 1
        }
    }
    return -1
}

function Mask-CSharpInterpolatedString([string]$Text, [int]$Start, [pscustomobject]$Prefix) {
    $builder = [Text.StringBuilder]::new()
    $prefixLength = $Prefix.QuoteStart + $Prefix.QuoteCount - $Start
    Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $Start -Length $prefixLength
    $index = $Prefix.QuoteStart + $Prefix.QuoteCount
    $closeDelimiter = '"' * $Prefix.QuoteCount
    while ($index -lt $Text.Length) {
        if ($Prefix.Raw -and $index + $Prefix.QuoteCount -le $Text.Length -and $Text.Substring($index, $Prefix.QuoteCount) -eq $closeDelimiter) {
            Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $index -Length $Prefix.QuoteCount
            $index += $Prefix.QuoteCount
            return [pscustomobject]@{ Mask = $builder.ToString(); End = $index }
        }
        if (-not $Prefix.Raw -and $Text[$index] -eq '"') {
            if ($Prefix.Verbatim -and $index + 1 -lt $Text.Length -and $Text[$index + 1] -eq '"') {
                Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $index -Length 2; $index += 2; continue
            }
            Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $index -Length 1
            $index++
            return [pscustomobject]@{ Mask = $builder.ToString(); End = $index }
        }

        $openCount = 0
        while ($index + $openCount -lt $Text.Length -and $Text[$index + $openCount] -eq '{') { $openCount++ }
        $requiredOpenCount = if ($Prefix.Raw) { $Prefix.DollarCount } else { 1 }
        $isExpression = $openCount -ge $requiredOpenCount -and ($Prefix.Raw -or $openCount -eq 1 -or $openCount % 2 -eq 1)
        if ($isExpression) {
            $expressionStart = $index + $requiredOpenCount
            $expressionEnd = Find-CSharpInterpolationEnd -Text $Text -Start $expressionStart -CloseBraceCount $requiredOpenCount
            if ($expressionEnd -ge 0) {
                Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $index -Length $requiredOpenCount
                [void]$builder.Append((Get-CSharpCodeMask $Text.Substring($expressionStart, $expressionEnd - $expressionStart)))
                Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $expressionEnd -Length $requiredOpenCount
                $index = $expressionEnd + $requiredOpenCount
                continue
            }
        }
        Append-CSharpMaskedRange -Builder $builder -Text $Text -Start $index -Length 1
        $index++
    }
    return [pscustomobject]@{ Mask = $builder.ToString(); End = $index }
}

function Get-CSharpCodeMask([string]$Text) {
    $builder = [Text.StringBuilder]::new($Text.Length)
    $state = 'Code'
    $index = 0
    while ($index -lt $Text.Length) {
        $character = $Text[$index]
        $nextCharacter = if ($index + 1 -lt $Text.Length) { $Text[$index + 1] } else { [char]0 }
        if ($state -eq 'LineComment') {
            if ($character -eq "`r" -or $character -eq "`n") {
                [void]$builder.Append($character)
                $state = 'Code'
            }
            else {
                [void]$builder.Append(' ')
            }
            $index++
            continue
        }
        if ($state -eq 'BlockComment') {
            if ($character -eq '*' -and $nextCharacter -eq '/') {
                [void]$builder.Append('  ')
                $index += 2
                $state = 'Code'
            }
            elseif ($character -eq "`r" -or $character -eq "`n") {
                [void]$builder.Append($character)
                $index++
            }
            else {
                [void]$builder.Append(' ')
                $index++
            }
            continue
        }
        if ($state -eq 'String') {
            if ($character -eq '\') {
                [void]$builder.Append(' ')
                if ($index + 1 -lt $Text.Length) {
                    if ($Text[$index + 1] -eq "`r" -or $Text[$index + 1] -eq "`n") {
                        [void]$builder.Append($Text[$index + 1])
                    }
                    else {
                        [void]$builder.Append(' ')
                    }
                    $index += 2
                }
                else {
                    $index++
                }
            }
            elseif ($character -eq '"') {
                [void]$builder.Append(' ')
                $index++
                $state = 'Code'
            }
            elseif ($character -eq "`r" -or $character -eq "`n") {
                [void]$builder.Append($character)
                $index++
                $state = 'Code'
            }
            else {
                [void]$builder.Append(' ')
                $index++
            }
            continue
        }
        if ($state -eq 'VerbatimString') {
            if ($character -eq '"' -and $nextCharacter -eq '"') {
                [void]$builder.Append('  ')
                $index += 2
            }
            elseif ($character -eq '"') {
                [void]$builder.Append(' ')
                $index++
                $state = 'Code'
            }
            elseif ($character -eq "`r" -or $character -eq "`n") {
                [void]$builder.Append($character)
                $index++
            }
            else {
                [void]$builder.Append(' ')
                $index++
            }
            continue
        }
        if ($state -eq 'Char') {
            if ($character -eq '\') {
                [void]$builder.Append(' ')
                if ($index + 1 -lt $Text.Length) {
                    [void]$builder.Append(' ')
                    $index += 2
                }
                else {
                    $index++
                }
            }
            elseif ($character -eq "'") {
                [void]$builder.Append(' ')
                $index++
                $state = 'Code'
            }
            elseif ($character -eq "`r" -or $character -eq "`n") {
                [void]$builder.Append($character)
                $index++
                $state = 'Code'
            }
            else {
                [void]$builder.Append(' ')
                $index++
            }
            continue
        }

        $interpolatedPrefix = Get-CSharpInterpolatedPrefix -Text $Text -Index $index
        if ($null -ne $interpolatedPrefix) {
            $masked = Mask-CSharpInterpolatedString -Text $Text -Start $index -Prefix $interpolatedPrefix
            [void]$builder.Append($masked.Mask)
            $index = $masked.End
            continue
        }

        if ($character -eq '/' -and $nextCharacter -eq '/') {
            [void]$builder.Append('  ')
            $index += 2
            $state = 'LineComment'
        }
        elseif ($character -eq '/' -and $nextCharacter -eq '*') {
            [void]$builder.Append('  ')
            $index += 2
            $state = 'BlockComment'
        }
        elseif ($character -eq '@' -and $nextCharacter -eq '"') {
            [void]$builder.Append('  ')
            $index += 2
            $state = 'VerbatimString'
        }
        elseif ($character -eq '"') {
            [void]$builder.Append(' ')
            $index++
            $state = 'String'
        }
        elseif ($character -eq "'") {
            [void]$builder.Append(' ')
            $index++
            $state = 'Char'
        }
        else {
            [void]$builder.Append($character)
            $index++
        }
    }
    return $builder.ToString()
}

function Normalize-CSharpIdentifierEscapes([string]$Code) {
    return [regex]::Replace($Code, '\\u([0-9A-Fa-f]{4})|\\U([0-9A-Fa-f]{8})', {
            param($match)
            $hex = if ($match.Groups[1].Success) { $match.Groups[1].Value } else { $match.Groups[2].Value }
            $codePoint = [Convert]::ToInt32($hex, 16)
            if ($codePoint -gt 0x10FFFF -or ($codePoint -ge 0xD800 -and $codePoint -le 0xDFFF)) {
                return $match.Value
            }
            return [char]::ConvertFromUtf32($codePoint)
        })
}

function Get-CSharpInitializerSpan([string]$Code, [int]$StartIndex, [int]$MatchLength) {
    $openBrace = $Code.IndexOf('{', $StartIndex + $MatchLength)
    $semicolon = $Code.IndexOf(';', $StartIndex + $MatchLength)
    if ($openBrace -lt 0 -or ($semicolon -ge 0 -and $semicolon -lt $openBrace)) {
        return [pscustomobject]@{ Body = $null; End = $StartIndex + $MatchLength }
    }
    $depth = 0
    for ($index = $openBrace; $index -lt $Code.Length; $index++) {
        if ($Code[$index] -eq '{') { $depth++ }
        elseif ($Code[$index] -eq '}') {
            $depth--
            if ($depth -eq 0) { return [pscustomobject]@{ Body = $Code.Substring($openBrace + 1, $index - $openBrace - 1); End = $index + 1 } }
        }
    }
    return [pscustomobject]@{ Body = $null; End = $StartIndex + $MatchLength }
}

function Get-CSharpLineNumber([string]$Source, [int]$Index) {
    return 1 + ($Source.Substring(0, $Index) -split "`n").Count - 1
}

function Get-CSharpLaunchAudit([string]$Path) {
    $source = [IO.File]::ReadAllText($Path)
    $code = Normalize-CSharpIdentifierEscapes (Get-CSharpCodeMask $source)
    $violations = [System.Collections.Generic.List[string]]::new()
    $constructorRecords = [System.Collections.Generic.List[object]]::new()
    $seenConstructorIndexes = [System.Collections.Generic.HashSet[int]]::new()
    $processStartInfoTypePattern = '(?:global::)?(?:[A-Za-z_]\w*(?:\.|::))*ProcessStartInfo\??'
    $explicitConstructors = @([regex]::Matches($code, '(?<![\w.])new\s+(?:global::)?(?:[A-Za-z_]\w*(?:\.|::))*ProcessStartInfo\b'))
    $targetTypedAssignments = @([regex]::Matches($code, '(?m)\b(?:var|' + $processStartInfoTypePattern + ')\s+[A-Za-z_]\w*\s*=\s*new\s*\('))
    $targetTypedReturns = @([regex]::Matches($code, '(?m)\b' + $processStartInfoTypePattern + '\s+[A-Za-z_]\w*\s*\([^;{}]*\)\s*=>\s*new\s*\('))
    $constructorMatches = [System.Collections.Generic.List[object]]::new()
    foreach ($match in $explicitConstructors) { [void]$constructorMatches.Add($match) }
    foreach ($match in $targetTypedAssignments) {
        $variableMatch = [regex]::Match($match.Value, '\bvar\s+(?<name>[A-Za-z_]\w*)\s*=')
        if ($variableMatch.Success) {
            $variableName = $variableMatch.Groups['name'].Value
            $remainingCode = $code.Substring($match.Index + $match.Length)
            if ($remainingCode -notmatch '(?s)Process\s*\.\s*Start\s*\(\s*' + [regex]::Escape($variableName) + '\b') { continue }
        }
        $newOffset = $match.Value.LastIndexOf('new')
        if ($newOffset -ge 0) { [void]$constructorMatches.Add([pscustomobject]@{ Index = $match.Index + $newOffset; Length = 3; Value = 'new(' }) }
    }
    foreach ($match in $targetTypedReturns) {
        $newOffset = $match.Value.LastIndexOf('new')
        if ($newOffset -ge 0) { [void]$constructorMatches.Add([pscustomobject]@{ Index = $match.Index + $newOffset; Length = 3; Value = 'new(' }) }
    }

    foreach ($constructor in ($constructorMatches | Sort-Object Index)) {
        if (-not $seenConstructorIndexes.Add([int]$constructor.Index)) { continue }
        $span = Get-CSharpInitializerSpan -Code $code -StartIndex $constructor.Index -MatchLength $constructor.Length
        $body = $span.Body
        $shell = if ($null -eq $body) { @() } else { @([regex]::Matches($body, '\bUseShellExecute\s*=\s*(?<value>[^,;}]*)')) }
        $window = if ($null -eq $body) { @() } else { @([regex]::Matches($body, '\bCreateNoWindow\s*=\s*(?<value>[^,;}]*)')) }
        $hasSafeShell = $shell.Count -eq 1 -and $shell[0].Groups['value'].Value.Trim() -ceq 'false'
        $hasNoWindow = $window.Count -eq 1 -and $window[0].Groups['value'].Value.Trim() -ceq 'true'
        $assignment = [regex]::Match($code.Substring(0, $constructor.Index), '(?ms)(?:^|[;{}])\s*(?:var|' + $processStartInfoTypePattern + ')\s+(?<name>[A-Za-z_]\w*)\s*=\s*$')
        $variableName = if ($assignment.Success) { $assignment.Groups['name'].Value } else { $null }
        $record = [pscustomobject]@{
            Index = [int]$constructor.Index
            End = [int]$span.End
            Safe = $hasSafeShell -and $hasNoWindow -and $null -ne $body
            Variable = $variableName
            DeclarationIndex = if ($assignment.Success) { $constructor.Index - 1 } else { -1 }
        }
        [void]$constructorRecords.Add($record)
        if (-not $record.Safe) {
            $lineNumber = Get-CSharpLineNumber -Source $source -Index $constructor.Index
            [void]$violations.Add("${Path}:$lineNumber`: ProcessStartInfo containment must be proven with exact UseShellExecute = false and CreateNoWindow = true values")
        }
    }

    $safeVariables = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($record in $constructorRecords) { if ($record.Safe -and $record.Variable) { [void]$safeVariables.Add($record.Variable) } }

    foreach ($propertyWrite in @([regex]::Matches($code, '\b(?<property>UseShellExecute|CreateNoWindow)\s*='))) {
        $insideInitializer = @($constructorRecords | Where-Object { $_.Index -le $propertyWrite.Index -and $propertyWrite.Index -lt $_.End }).Count -gt 0
        if (-not $insideInitializer) {
            $lineNumber = Get-CSharpLineNumber -Source $source -Index $propertyWrite.Index
            [void]$violations.Add("${Path}:$lineNumber`: ProcessStartInfo property '$($propertyWrite.Groups['property'].Value)' must not be changed outside its constructor initializer")
        }
    }

    foreach ($record in $constructorRecords | Where-Object { $_.Safe -and $_.Variable }) {
        foreach ($occurrence in @([regex]::Matches($code, '(?<![\w])' + [regex]::Escape($record.Variable) + '(?![\w])'))) {
            $before = $code.Substring(0, $occurrence.Index)
            $afterOccurrence = $code.Substring($occurrence.Index + $occurrence.Length).TrimStart()
            $isDirectStartArgument = $before -match '(?s)Process\s*\.\s*Start\s*\(\s*$'
            $isDeclaration = ($occurrence.Index -ge $record.Index -and $occurrence.Index -le $record.End) -or
                $before -match ('(?ms)(?:^|[;{}])\s*(?:var|' + $processStartInfoTypePattern + ')\s*$')
            $isMemberAccess = $afterOccurrence.StartsWith('.') -or $afterOccurrence.StartsWith('[')
            if (-not $isDirectStartArgument -and -not $isDeclaration -and -not $isMemberAccess) {
                $lineNumber = Get-CSharpLineNumber -Source $source -Index $occurrence.Index
                [void]$violations.Add("${Path}:$lineNumber`: ProcessStartInfo dataflow through aliases, helpers, fields, arrays, or closures is not proven")
            }
        }
    }

    $startTokens = @([regex]::Matches($code, '\bStart\b'))
    foreach ($start in $startTokens) {
        $before = $code.Substring(0, $start.Index)
        $after = $code.Substring($start.Index + $start.Length)
        $receiverMatch = [regex]::Match($before, '(?<receiver>(?:global::)?[A-Za-z_]\w*(?:(?:\.|::)[A-Za-z_]\w*)*)\s*\.\s*$')
            $receiver = if ($receiverMatch.Success) { $receiverMatch.Groups['receiver'].Value } else { $null }
        $trimmedAfter = $after.TrimStart()
        if ($receiver -ceq 'Process' -and $trimmedAfter.StartsWith('(')) {
            $argument = [regex]::Match($trimmedAfter.Substring(1), '^\s*(?<value>[A-Za-z_]\w*)\s*(?:,|\))')
            $directNewOffset = $after.IndexOf('new')
            $directConstructorIndex = if ($directNewOffset -ge 0) { $start.Index + $start.Length + $directNewOffset } else { -1 }
            $directConstructor = @($constructorRecords | Where-Object { $_.Index -eq $directConstructorIndex })
            $isSafeDirectConstructor = $directConstructor.Count -gt 0 -and $directConstructor[0].Safe -and -not $directConstructor[0].Variable
            if ($argument.Success -and $safeVariables.Contains($argument.Groups['value'].Value)) { continue }
            if ($isSafeDirectConstructor -and $trimmedAfter -match '^\(\s*new\b') { continue }
            $lineNumber = Get-CSharpLineNumber -Source $source -Index $start.Index
            [void]$violations.Add("${Path}:$lineNumber`: Process.Start must use a contained ProcessStartInfo whose object state and call reachability are proven")
            continue
        }

        $lineNumber = Get-CSharpLineNumber -Source $source -Index $start.Index
        [void]$violations.Add("${Path}:$lineNumber`: unsupported C# process-launch form '$($start.Value.Trim())'")
    }

    return [pscustomobject]@{
        Path = $Path
        LaunchCount = $constructorRecords.Count + $startTokens.Count
        Violations = $violations.ToArray()
    }
}

if ($SelfTest) {
    $selfTestRoot = Join-Path ([IO.Path]::GetTempPath()) "nested-pwsh-guard-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Path $selfTestRoot -Force | Out-Null
    try {
        $directPath = Join-Path $selfTestRoot 'direct.ps1'
        $processPath = Join-Path $selfTestRoot 'process.ps1'
        $embeddedPath = Join-Path $selfTestRoot 'embedded.ps1'
        $embeddedSafePath = Join-Path $selfTestRoot 'embedded-safe.ps1'
        $splatSafePath = Join-Path $selfTestRoot 'splat-safe.ps1'
        $splatNoNewWindowPath = Join-Path $selfTestRoot 'splat-nonewwindow-safe.ps1'
        $unsafeCSharpPath = Join-Path $selfTestRoot 'unsafe.cs'
        $unsafeMutationCSharpPath = Join-Path $selfTestRoot 'unsafe-mutation.cs'
        $unsafeInstanceCSharpPath = Join-Path $selfTestRoot 'unsafe-instance.cs'
        $unsafeQualifiedCSharpPath = Join-Path $selfTestRoot 'unsafe-qualified.cs'
        $unsafeAliasCSharpPath = Join-Path $selfTestRoot 'unsafe-alias.cs'
        $unsafeConditionalCSharpPath = Join-Path $selfTestRoot 'unsafe-conditional.cs'
        $unsafeNullForgivingCSharpPath = Join-Path $selfTestRoot 'unsafe-null-forgiving.cs'
        $unsafeParenthesizedCSharpPath = Join-Path $selfTestRoot 'unsafe-parenthesized.cs'
        $unsafeDirectConstructorCSharpPath = Join-Path $selfTestRoot 'unsafe-direct-constructor.cs'
        $unsafeStaticImportCSharpPath = Join-Path $selfTestRoot 'unsafe-static-import.cs'
        $unsafeEscapedMemberCSharpPath = Join-Path $selfTestRoot 'unsafe-escaped-member.cs'
        $unsafeLongEscapedMemberCSharpPath = Join-Path $selfTestRoot 'unsafe-long-escaped-member.cs'
        $safeEscapedMemberCSharpPath = Join-Path $selfTestRoot 'safe-escaped-member.cs'
        $unsafeInterpolatedCSharpPath = Join-Path $selfTestRoot 'unsafe-interpolated.cs'
        $unsafeVerbatimInterpolatedCSharpPath = Join-Path $selfTestRoot 'unsafe-verbatim-interpolated.cs'
        $unsafeRawInterpolatedCSharpPath = Join-Path $selfTestRoot 'unsafe-raw-interpolated.cs'
        $unsafeCompoundCSharpPath = Join-Path $selfTestRoot 'unsafe-compound.cs'
        $unsafeDataflowCSharpPath = Join-Path $selfTestRoot 'unsafe-dataflow.cs'
        $unsafeTargetTypedCSharpPath = Join-Path $selfTestRoot 'unsafe-target-typed.cs'
        $unsafeMethodGroupCSharpPath = Join-Path $selfTestRoot 'unsafe-method-group.cs'
        $safeQualifiedTargetTypedCSharpPath = Join-Path $selfTestRoot 'safe-qualified-target-typed.cs'
        $dynamicPwshPath = Join-Path $selfTestRoot 'dynamic-pwsh.ps1'
        $unknownDynamicCommandPath = Join-Path $selfTestRoot 'unknown-dynamic-command.ps1'
        $dynamicStartProcessPath = Join-Path $selfTestRoot 'dynamic-start-process.ps1'
        $subexpressionCommandPath = Join-Path $selfTestRoot 'subexpression-command.ps1'
        $qualifiedStartProcessPath = Join-Path $selfTestRoot 'qualified-start-process.ps1'
        $aliasStartProcessPath = Join-Path $selfTestRoot 'alias-start-process.ps1'
        $safeQualifiedStartProcessPath = Join-Path $selfTestRoot 'safe-qualified-start-process.ps1'
        $invokeExpressionPath = Join-Path $selfTestRoot 'invoke-expression.ps1'
        $safeCSharpPath = Join-Path $selfTestRoot 'safe.cs'
        $safePath = Join-Path $selfTestRoot 'safe.ps1'
        [IO.File]::WriteAllText($directPath, '& pwsh -NoProfile')
        [IO.File]::WriteAllText($processPath, "Start-Process 'example.exe'")
        [IO.File]::WriteAllText($embeddedPath, @'
$nested = @"
Start-Process -FilePath 'example.exe'
"@
Invoke-NestedPwsh -ArgumentList $nested
'@)
        [IO.File]::WriteAllText($embeddedSafePath, @'
$nested = @"
Start-Process -FilePath 'example.exe' -WindowStyle Hidden
"@
Invoke-NestedPwsh -ArgumentList $nested
'@)
        [IO.File]::WriteAllText($splatSafePath, @'
$parameters = @{ FilePath = 'example.exe' }
$parameters.WindowStyle = 'Hidden'
Start-Process @parameters
'@)
        [IO.File]::WriteAllText($splatNoNewWindowPath, @'
$parameters = @{ FilePath = 'example.exe' }
$parameters.NoNewWindow = $true
Start-Process @parameters
'@)
        [IO.File]::WriteAllText($unsafeCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh") { UseShellExecute = true };
Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeMutationCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
startInfo.UseShellExecute = true;
Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeInstanceCSharpPath, @'
using System.Diagnostics;
var process = new Process();
process.Start();
'@)
        [IO.File]::WriteAllText($unsafeQualifiedCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
global::System.Diagnostics.Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeAliasCSharpPath, @'
using ProcessAlias = System.Diagnostics.Process;
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
ProcessAlias.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeConditionalCSharpPath, @'
using System.Diagnostics;
var process = new Process();
process?.Start();
'@)
        [IO.File]::WriteAllText($unsafeNullForgivingCSharpPath, @'
using System.Diagnostics;
var process = new Process();
process!.Start();
'@)
        [IO.File]::WriteAllText($unsafeParenthesizedCSharpPath, @'
using System.Diagnostics;
var process = new Process();
(process).Start();
'@)
        [IO.File]::WriteAllText($unsafeDirectConstructorCSharpPath, @'
using System.Diagnostics;
new Process().Start();
'@)
        [IO.File]::WriteAllText($unsafeStaticImportCSharpPath, @'
using static System.Diagnostics.Process;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeEscapedMemberCSharpPath, @'
using System.Diagnostics;
var info = GetUnsafeInfo();
Process.\u0053tart(info);
static ProcessStartInfo GetUnsafeInfo() => new("pwsh");
'@)
        [IO.File]::WriteAllText($unsafeLongEscapedMemberCSharpPath, @'
using System.Diagnostics;
var info = GetUnsafeInfo();
\U00000050rocess.\U00000053tart(info);
static ProcessStartInfo GetUnsafeInfo() => new("pwsh");
'@)
        [IO.File]::WriteAllText($safeEscapedMemberCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
\u0050rocess.\u0053tart(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeInterpolatedCSharpPath, @'
using System.Diagnostics;
var value = $"{Process.Start("pwsh")}";
'@)
        [IO.File]::WriteAllText($unsafeVerbatimInterpolatedCSharpPath, @'
using System.Diagnostics;
var value = $@"{Process.Start("pwsh")}";
'@)
        [IO.File]::WriteAllText($unsafeRawInterpolatedCSharpPath, @'
using System.Diagnostics;
var value = $"""{Process.Start("pwsh")}""";
'@)
        [IO.File]::WriteAllText($unsafeCompoundCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false || true,
    CreateNoWindow = true && false
};
Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeDataflowCSharpPath, @'
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
Mutate(startInfo);
Process.Start(startInfo);
static void Mutate(ProcessStartInfo startInfo) => startInfo.UseShellExecute = true;
'@)
        [IO.File]::WriteAllText($unsafeTargetTypedCSharpPath, @'
using System.Diagnostics;
static ProcessStartInfo Build() => new("pwsh");
var startInfo = Build();
Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($unsafeMethodGroupCSharpPath, @'
using System;
using System.Diagnostics;
var startInfo = new ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
        Func<ProcessStartInfo, Process> start = Process.Start;
start(startInfo);
'@)
        [IO.File]::WriteAllText($safeQualifiedTargetTypedCSharpPath, @'
#nullable enable
using System.Diagnostics;
global::System.Diagnostics.ProcessStartInfo? startInfo = new("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
};
Process.Start(startInfo);
'@)
        [IO.File]::WriteAllText($dynamicPwshPath, @'
$exe = 'pwsh'
& $exe -NoProfile
'@)
        [IO.File]::WriteAllText($unknownDynamicCommandPath, @'
$mystery = Get-Command pwsh
& $mystery -NoProfile
'@)
        [IO.File]::WriteAllText($dynamicStartProcessPath, @'
$exe = 'example.exe'
$command = 'Start-Process'
& $command -FilePath $exe
'@)
        [IO.File]::WriteAllText($subexpressionCommandPath, @'
& (Get-Command pwsh) -NoProfile
& (Get-Command Start-Process) -FilePath 'example.exe'
'@)
        [IO.File]::WriteAllText($qualifiedStartProcessPath, @'
Microsoft.PowerShell.Management\Start-Process -FilePath 'example.exe'
'@)
        [IO.File]::WriteAllText($aliasStartProcessPath, @'
start -FilePath 'example.exe'
saps -FilePath 'example.exe'
'@)
        [IO.File]::WriteAllText($safeQualifiedStartProcessPath, @'
Microsoft.PowerShell.Management\Start-Process -FilePath 'example.exe' -WindowStyle Hidden
start -FilePath 'example.exe' -WindowStyle Hidden
saps -FilePath 'example.exe' -NoNewWindow
'@)
        [IO.File]::WriteAllText($invokeExpressionPath, @'
Invoke-Expression 'pwsh -NoProfile'
'@)
        [IO.File]::WriteAllText($safeCSharpPath, @'
using System.Diagnostics;
Process.Start(new System.Diagnostics.ProcessStartInfo("pwsh")
{
    UseShellExecute = false,
    CreateNoWindow = true
});
'@)
        [IO.File]::WriteAllText($safePath, "Invoke-NestedPwsh -ArgumentList @('-NoProfile')")
        if (@(Get-LaunchViolations $directPath).Count -eq 0) {
            throw 'The guard self-test did not reject a direct nested PowerShell launch.'
        }
        if (@(Get-LaunchViolations $processPath).Count -eq 0) {
            throw 'The guard self-test did not reject a visible Start-Process launch.'
        }
        if (@(Get-LaunchViolations $embeddedPath).Count -eq 0) {
            throw 'The guard self-test did not reject a visible Start-Process launch embedded in a here-string.'
        }
        if (@(Get-LaunchViolations $embeddedSafePath).Count -ne 0) {
            throw 'The guard self-test rejected a hidden Start-Process launch embedded in a here-string.'
        }
        if (@(Get-LaunchViolations $splatSafePath).Count -ne 0) {
            throw 'The guard self-test rejected a hidden Start-Process launch supplied through a splatted parameter set.'
        }
        if (@(Get-LaunchViolations $splatNoNewWindowPath).Count -ne 0) {
            throw 'The guard self-test rejected a no-new-window Start-Process launch supplied through a splatted parameter set.'
        }
        if (@(Get-LaunchViolations $safePath).Count -ne 0) {
            throw 'The guard self-test rejected a helper-mediated launch.'
        }
        if (@(Get-LaunchViolations $safeQualifiedStartProcessPath).Count -ne 0) {
            throw 'The guard self-test rejected a contained module-qualified or aliased Start-Process launch.'
        }
        foreach ($dynamicCase in @(
                    @{ Name = 'dynamic PowerShell command'; Path = $dynamicPwshPath },
                    @{ Name = 'unknown dynamic command'; Path = $unknownDynamicCommandPath },
                    @{ Name = 'dynamic Start-Process command'; Path = $dynamicStartProcessPath },
                    @{ Name = 'subexpression command'; Path = $subexpressionCommandPath },
                    @{ Name = 'module-qualified Start-Process command'; Path = $qualifiedStartProcessPath },
                    @{ Name = 'Start-Process aliases'; Path = $aliasStartProcessPath },
                    @{ Name = 'Invoke-Expression command'; Path = $invokeExpressionPath }
                )) {
            if (@(Get-LaunchViolations $dynamicCase.Path).Count -eq 0) {
                throw "The guard self-test did not reject the unsupported $($dynamicCase.Name) form."
            }
        }
        $unsafeCSharpAudit = Get-CSharpLaunchAudit $unsafeCSharpPath
        if ($unsafeCSharpAudit.LaunchCount -ne 2 -or @($unsafeCSharpAudit.Violations).Count -eq 0) {
            throw 'The guard self-test did not reject an unsafe C# process launch.'
        }
        $unsafeMutationCSharpAudit = Get-CSharpLaunchAudit $unsafeMutationCSharpPath
        if ($unsafeMutationCSharpAudit.LaunchCount -ne 2 -or @($unsafeMutationCSharpAudit.Violations).Count -eq 0) {
            throw 'The guard self-test did not reject post-construction C# process-launch mutation.'
        }
        $unsafeInstanceCSharpAudit = Get-CSharpLaunchAudit $unsafeInstanceCSharpPath
        if ($unsafeInstanceCSharpAudit.LaunchCount -ne 1 -or @($unsafeInstanceCSharpAudit.Violations).Count -eq 0) {
            throw 'The guard self-test did not reject an instance C# process launch.'
        }
        $unsafeQualifiedCSharpAudit = Get-CSharpLaunchAudit $unsafeQualifiedCSharpPath
        if ($unsafeQualifiedCSharpAudit.LaunchCount -ne 2 -or @($unsafeQualifiedCSharpAudit.Violations).Count -eq 0) {
            throw 'The guard self-test did not reject an unsupported qualified C# process launch form.'
        }
        $unsafeAliasCSharpAudit = Get-CSharpLaunchAudit $unsafeAliasCSharpPath
        if ($unsafeAliasCSharpAudit.LaunchCount -ne 2 -or @($unsafeAliasCSharpAudit.Violations).Count -eq 0) {
            throw 'The guard self-test did not reject an aliased C# process launch form.'
        }
        $equivalentLaunchCases = @(
            @{ Name = 'conditional access'; Path = $unsafeConditionalCSharpPath; Count = 1 },
            @{ Name = 'null-forgiving access'; Path = $unsafeNullForgivingCSharpPath; Count = 1 },
            @{ Name = 'parenthesized receiver'; Path = $unsafeParenthesizedCSharpPath; Count = 1 },
            @{ Name = 'direct constructor receiver'; Path = $unsafeDirectConstructorCSharpPath; Count = 1 },
            @{ Name = 'static import'; Path = $unsafeStaticImportCSharpPath; Count = 2 },
            @{ Name = 'escaped member with target-typed helper'; Path = $unsafeEscapedMemberCSharpPath; Count = 2 },
            @{ Name = 'long escaped member with target-typed helper'; Path = $unsafeLongEscapedMemberCSharpPath; Count = 2 }
        )
        foreach ($case in $equivalentLaunchCases) {
            $audit = Get-CSharpLaunchAudit $case.Path
            if ($audit.LaunchCount -ne $case.Count -or @($audit.Violations).Count -eq 0) {
                throw "The guard self-test did not reject the unsupported $($case.Name) C# process launch form."
            }
        }
        $safeEscapedMemberCSharpAudit = Get-CSharpLaunchAudit $safeEscapedMemberCSharpPath
        if ($safeEscapedMemberCSharpAudit.LaunchCount -ne 2 -or @($safeEscapedMemberCSharpAudit.Violations).Count -ne 0) {
            throw 'The guard self-test rejected a contained C# process launch with escaped identifiers.'
        }
        $safeCSharpAudit = Get-CSharpLaunchAudit $safeCSharpPath
        if ($safeCSharpAudit.LaunchCount -ne 2 -or @($safeCSharpAudit.Violations).Count -ne 0) {
            throw 'The guard self-test rejected a contained C# process launch.'
        }
        $safeQualifiedTargetTypedCSharpAudit = Get-CSharpLaunchAudit $safeQualifiedTargetTypedCSharpPath
        if ($safeQualifiedTargetTypedCSharpAudit.LaunchCount -ne 2 -or @($safeQualifiedTargetTypedCSharpAudit.Violations).Count -ne 0) {
            throw 'The guard self-test rejected a contained qualified nullable target-typed C# process launch.'
        }
        foreach ($csharpCase in @(
                    @{ Name = 'regular interpolated expression'; Path = $unsafeInterpolatedCSharpPath; Count = 1 },
                    @{ Name = 'verbatim interpolated expression'; Path = $unsafeVerbatimInterpolatedCSharpPath; Count = 1 },
                    @{ Name = 'raw interpolated expression'; Path = $unsafeRawInterpolatedCSharpPath; Count = 1 },
                    @{ Name = 'compound containment expression'; Path = $unsafeCompoundCSharpPath; Count = 2 },
                    @{ Name = 'ProcessStartInfo dataflow'; Path = $unsafeDataflowCSharpPath; Count = 2 },
                    @{ Name = 'target-typed ProcessStartInfo'; Path = $unsafeTargetTypedCSharpPath; Count = 2 },
                    @{ Name = 'method-group Process.Start'; Path = $unsafeMethodGroupCSharpPath; Count = 2 }
                )) {
            $audit = Get-CSharpLaunchAudit $csharpCase.Path
            if ($audit.LaunchCount -ne $csharpCase.Count -or @($audit.Violations).Count -eq 0) {
                throw "The guard self-test did not reject the unsupported $($csharpCase.Name) form."
            }
        }

    }
    finally {
        Remove-Item -LiteralPath $selfTestRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
    Write-Output 'Nested PowerShell launch guard self-test passed.'
    exit 0
}

if (-not (Test-Path -LiteralPath $helperPath -PathType Leaf)) {
    throw "Shared nested PowerShell launch helper is missing: $helperPath"
}

$scriptFiles = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter '*.ps1' |
    Where-Object {
        $_.FullName -notin @($helperPath, $guardPath) -and
        $_.FullName -notmatch '[\\/]((\.git)|(bin)|(obj)|(artifacts)|_probe[\\/]corpus)([\\/]|$)'
    }
$violations = @($scriptFiles | ForEach-Object { Get-LaunchViolations $_.FullName })
if ($violations.Count -gt 0) {
    throw "Visible child process launch sites must use the shared containment helper."
}

$csharpFiles = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Filter '*.cs' |
    Where-Object {
        $_.FullName -notmatch '[\\/]((\.git)|(bin)|(obj)|(artifacts)|_probe[\\/]corpus)([\\/]|$)'
    }
$csharpAudits = @($csharpFiles | ForEach-Object { Get-CSharpLaunchAudit $_.FullName })
$csharpLaunchCount = ($csharpAudits | ForEach-Object { $_.LaunchCount } | Measure-Object -Sum).Sum
if ($csharpFiles.Count -eq 0 -or $csharpLaunchCount -eq 0) {
    throw 'C# process-launch guard found no ProcessStartInfo or Process.Start site to audit.'
}
$csharpViolations = @($csharpAudits | ForEach-Object { $_.Violations })
if ($csharpViolations.Count -gt 0) {
    throw "C# process-launch sites must use contained ProcessStartInfo instances.`n$($csharpViolations -join [Environment]::NewLine)"
}

Write-Output 'Nested PowerShell launch guard passed.'
