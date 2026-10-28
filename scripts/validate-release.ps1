param(
    [Parameter(Mandatory)] [string] $Version,
    [switch] $RequireFinalized,
    [switch] $RequireUnreleased,
    [switch] $FirstRelease,
    [string] $ProjectFile = (Join-Path $PSScriptRoot '..\src\KeelMatrix.PackageSurface.Cli\KeelMatrix.PackageSurface.Cli.csproj'),
    [string] $ChangelogPath = (Join-Path $PSScriptRoot '..\CHANGELOG.md')
)
$ErrorActionPreference = 'Stop'

if ($RequireFinalized -and $RequireUnreleased) {
    throw 'Release validation cannot require both finalized and unreleased changelog states.'
}

if ($FirstRelease -and -not $RequireFinalized) {
    throw 'First-release validation requires a finalized changelog entry.'
}

if ($Version -notmatch '^\d+\.\d+\.\d+(?:[-+][0-9A-Za-z.-]+)?$') {
    throw 'Release version must be a semantic three-part version.'
}

$projectText = Get-Content -LiteralPath $ProjectFile -Raw
$versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $versionMatch.Success -or $versionMatch.Groups['version'].Value.Trim() -ne $Version) {
    throw 'Project package version does not match the requested release version.'
}

foreach ($propertyName in @('PackageVersion', 'VersionPrefix')) {
    $match = [regex]::Match($projectText, "<$propertyName>\s*(?<version>[^<]+?)\s*</$propertyName>", [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($match.Success -and $match.Groups['version'].Value.Trim() -ne $Version) {
        throw "$propertyName does not match the requested release version."
    }
}

$packageId = [regex]::Match($projectText, '<PackageId>\s*(?<id>[^<]+?)\s*</PackageId>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if ($packageId.Success -and $packageId.Groups['id'].Value.Trim() -ne 'KeelMatrix.PackageSurface') {
    throw 'Project package ID does not match the intended package.'
}

$commandLinePath = Join-Path (Split-Path -Parent $ProjectFile) 'CommandLine.cs'
if (Test-Path -LiteralPath $commandLinePath -PathType Leaf) {
    $commandLineText = Get-Content -LiteralPath $commandLinePath -Raw
    $toolVersion = [regex]::Match($commandLineText, 'ToolVersion\s*=\s*"(?<version>[^"]+)"')
    if ($toolVersion.Success -and $toolVersion.Groups['version'].Value -ne $Version) {
        throw 'CLI tool version does not match the requested release version.'
    }
}

$changelog = Get-Content -LiteralPath $ChangelogPath -Raw
$targetVersionPattern = '(?<![0-9A-Za-z.+-])(?:v)?' + [regex]::Escape($Version) + '(?![0-9A-Za-z.+-])'

function ConvertFrom-MarkdownHeadingText([string] $Text) {
    [Net.WebUtility]::HtmlDecode($Text.Replace('\.', '.'))
}

function Remove-HtmlComments([string] $Line, [bool] $InHtmlComment) {
    $visible = [Text.StringBuilder]::new()
    $position = 0
    while ($position -lt $Line.Length) {
        if ($InHtmlComment) {
            $commentEnd = $Line.IndexOf('-->', $position, [StringComparison]::Ordinal)
            if ($commentEnd -lt 0) { break }
            $position = $commentEnd + 3
            $InHtmlComment = $false
            continue
        }

        $commentStart = $Line.IndexOf('<!--', $position, [StringComparison]::Ordinal)
        if ($commentStart -lt 0) {
            [void]$visible.Append($Line.Substring($position))
            break
        }
        if ($commentStart -gt $position) {
            [void]$visible.Append($Line.Substring($position, $commentStart - $position))
        }
        $position = $commentStart + 4
        $InHtmlComment = $true
    }

    [pscustomobject]@{
        Text = $visible.ToString()
        InHtmlComment = $InHtmlComment
    }
}

function Find-HtmlTagEnd([string] $Text, [int] $StartIndex) {
    $quote = [char]0
    for ($index = $StartIndex; $index -lt $Text.Length; $index++) {
        $character = $Text[$index]
        if ($quote -ne [char]0) {
            if ($character -eq $quote) { $quote = [char]0 }
            continue
        }
        if ($character -eq '"' -or $character -eq "'") {
            $quote = $character
            continue
        }
        if ($character -eq '>') { return $index }
    }

    return -1
}

function Test-IsGfmBlankLine([AllowEmptyString()] [string] $Line) {
    for ($index = 0; $index -lt $Line.Length; $index++) {
        $character = [int]$Line[$index]
        if ($character -ne 0x0020 -and $character -ne 0x0009) { return $false }
    }

    return $true
}

function Test-IsCompleteHtmlTagLine([string] $Line) {
    $tagStart = [regex]::Match($Line, '^ {0,3}</?(?<name>[A-Za-z][A-Za-z0-9-]*)')
    if (-not $tagStart.Success) { return $false }
    if ($tagStart.Groups['name'].Value -match '^(?i:script|style|pre)$') { return $false }
    $tagEnd = Find-HtmlTagEnd -Text $Line -StartIndex $tagStart.Length
    return $tagEnd -ge 0 -and (Test-IsGfmBlankLine -Line $Line.Substring($tagEnd + 1))
}

function Get-RawHtmlBlockStart([string] $Line, [bool] $PreviousLineWasParagraph) {
    $rawText = [regex]::Match($Line, '^ {0,3}<(script|pre|style)(?=[\t >]|$)', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($rawText.Success) { return [pscustomobject]@{ EndKind = 'RawText'; Tag = $rawText.Groups[1].Value } }
    if ($Line -match '^ {0,3}<\?') { return [pscustomobject]@{ EndKind = 'ProcessingInstruction'; Tag = $null } }
    if ($Line -match '^ {0,3}<!\[CDATA\[') { return [pscustomobject]@{ EndKind = 'CData'; Tag = $null } }
    if ($Line -cmatch '^ {0,3}<![A-Z]') { return [pscustomobject]@{ EndKind = 'Declaration'; Tag = $null } }

    $blockTags = '^(?:address|article|aside|base|basefont|blockquote|body|caption|center|col|colgroup|dd|details|dialog|dir|div|dl|dt|fieldset|figcaption|figure|footer|form|frame|frameset|h1|h2|h3|h4|h5|h6|head|header|hr|html|iframe|legend|li|link|main|menu|menuitem|nav|noframes|ol|optgroup|option|p|param|section|source|summary|table|tbody|td|tfoot|th|thead|title|tr|track|ul)$'
    $tag = [regex]::Match($Line, '^ {0,3}</?(?<tag>[A-Za-z][A-Za-z0-9-]*)(?=[\t />]|$)')
    if ($tag.Success -and $tag.Groups['tag'].Value -match $blockTags) {
        return [pscustomobject]@{ EndKind = 'BlankLine'; Tag = $tag.Groups['tag'].Value }
    }

    if (-not $PreviousLineWasParagraph -and (Test-IsCompleteHtmlTagLine -Line $Line)) {
        return [pscustomobject]@{ EndKind = 'BlankLine'; Tag = $null }
    }

    return $null
}

function Test-RawHtmlBlockEnd($Block, [string] $Line) {
    switch ($Block.EndKind) {
        'RawText' { return [regex]::IsMatch($Line, '</(?:script|pre|style)\s*>', [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
        'HtmlComment' { return $Line.Contains('-->', [StringComparison]::Ordinal) }
        'ProcessingInstruction' { return $Line.Contains('?>', [StringComparison]::Ordinal) }
        'CData' { return $Line.Contains(']]>', [StringComparison]::Ordinal) }
        'Declaration' { return $Line.Contains('>', [StringComparison]::Ordinal) }
        'BlankLine' { return Test-IsGfmBlankLine -Line $Line }
        default { throw "Unknown raw HTML block end kind '$($Block.EndKind)'." }
    }
}

function Get-MarkdownListLineInfo {
    param(
        [Parameter(Mandatory)] [AllowEmptyString()] [string] $Line,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[object]] $ListStack,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [Collections.Generic.List[object]] $BlockQuoteListStack
    )

    $contentLine = $Line
    $inBlockQuote = $false
    $quotePrefix = [regex]::Match($contentLine, '^ {0,3}>[ \t]?')
    if ($quotePrefix.Success) {
        $inBlockQuote = $true
        while ($quotePrefix.Success) {
            $contentLine = $contentLine.Substring($quotePrefix.Length)
            $quotePrefix = [regex]::Match($contentLine, '^ {0,3}>[ \t]?')
        }
        $stack = $BlockQuoteListStack
    }
    else {
        $BlockQuoteListStack.Clear()
        $stack = $ListStack
    }

    if (Test-IsGfmBlankLine -Line $contentLine) {
        return [pscustomobject]@{
            NormalizedText = $contentLine
            IsList = ($stack.Count -gt 0)
            IsListItem = $false
            ListItemContent = ''
            InBlockQuote = $inBlockQuote
            IsTopLevel = ($stack.Count -eq 0 -and -not $inBlockQuote)
            IsIndentedCode = $false
        }
    }

    $indent = [regex]::Match($contentLine, '^ *').Length
    $marker = [regex]::Match($contentLine, '^(?<indent> *)(?<marker>[-+*]|[0-9]{1,9}[.)])(?<gap>[ \t]*)(?<content>.*)$')
    $isListMarker = $marker.Success -and $marker.Groups['indent'].Length -le 3 -and
        ($marker.Groups['gap'].Length -gt 0 -or $marker.Groups['content'].Length -eq 0)

    while ($stack.Count -gt 0) {
        $top = $stack[$stack.Count - 1]
        $sameLevelSibling = $isListMarker -and $indent -eq $top.MarkerIndent
        if ($indent -lt $top.ContentIndent -and -not $sameLevelSibling) {
            $stack.RemoveAt($stack.Count - 1)
            continue
        }
        break
    }

    $isListItem = $false
    $listItemContent = ''
    if ($isListMarker) {
        if ($stack.Count -gt 0 -and $indent -eq $stack[$stack.Count - 1].MarkerIndent) {
            $stack.RemoveAt($stack.Count - 1)
        }

        $gapColumns = 0
        $column = $indent + $marker.Groups['marker'].Length
        foreach ($character in $marker.Groups['gap'].Value.ToCharArray()) {
            if ($character -eq "`t") { $gapColumns += 4 - (($column + $gapColumns) % 4) }
            else { $gapColumns++ }
        }
        $contentIndent = $indent + $marker.Groups['marker'].Length +
            $(if ($gapColumns -eq 0 -or $gapColumns -gt 4) { 1 } else { $gapColumns })
        $stack.Add([pscustomobject]@{ MarkerIndent = $indent; ContentIndent = $contentIndent })
        $isListItem = $true
        $listItemContent = $marker.Groups['content'].Value
        $normalizedText = $listItemContent
        $itemIndent = [regex]::Match($listItemContent, '^(?: {4,}|\t)').Success
        $isIndentedCode = $itemIndent
    }
    else {
        $isInList = $stack.Count -gt 0 -and $indent -ge $stack[$stack.Count - 1].ContentIndent
        if ($isInList) {
            $normalizedText = $contentLine.Substring($stack[$stack.Count - 1].ContentIndent)
            $isIndentedCode = ([regex]::Match($normalizedText, '^(?: {4,}|\t)').Success)
        }
        else {
            $normalizedText = $contentLine
            $isIndentedCode = ([regex]::Match($contentLine, '^(?: {4,}|\t)').Success)
        }
    }

    [pscustomobject]@{
        NormalizedText = $normalizedText
        IsList = ($isListItem -or ($stack.Count -gt 0 -and $indent -ge $stack[$stack.Count - 1].ContentIndent))
        IsListItem = $isListItem
        ListItemContent = $listItemContent
        InBlockQuote = $inBlockQuote
        IsTopLevel = (-not $inBlockQuote -and -not ($isListItem -or ($stack.Count -gt 0 -and $indent -ge $stack[$stack.Count - 1].ContentIndent)))
        IsIndentedCode = $isIndentedCode
    }
}

function Get-MarkdownVisibleLines([string] $Text, [switch] $IncludeRawHtmlBlocks) {
    $lines = [regex]::Split($Text, "\r\n|\n|\r")
    $lineStart = 0
    $inFence = $false
    $fenceCharacter = $null
    $fenceLength = 0
    $inHtmlComment = $false
    $rawHtmlBlock = $null
    $previousLineWasParagraph = $false
    $listStack = [Collections.Generic.List[object]]::new()
    $blockQuoteListStack = [Collections.Generic.List[object]]::new()

    for ($index = 0; $index -lt $lines.Count; $index++) {
        $rawLine = [string]$lines[$index]
        $lineEnd = $lineStart + $rawLine.Length
        $nextLineStart = $lineEnd
        if ($index + 1 -lt $lines.Count) {
            if ($Text[$lineEnd] -eq "`r" -and $lineEnd + 1 -lt $Text.Length -and $Text[$lineEnd + 1] -eq "`n") {
                $nextLineStart += 2
            }
            else {
                $nextLineStart++
            }
        }

        if ($null -ne $rawHtmlBlock) {
            $commentResult = if ($rawHtmlBlock.EndKind -eq 'HtmlComment') {
                [pscustomobject]@{ Text = ''; InHtmlComment = $false }
            }
            else {
                Remove-HtmlComments -Line $rawLine -InHtmlComment $inHtmlComment
            }
            $inHtmlComment = $commentResult.InHtmlComment
            if ($IncludeRawHtmlBlocks -and $rawHtmlBlock.EndKind -ne 'HtmlComment') {
                [pscustomobject]@{
                    Text = $commentResult.Text
                    Start = $lineStart
                    End = $nextLineStart
                    Kind = 'Html'
                }
            }
            if (Test-RawHtmlBlockEnd -Block $rawHtmlBlock -Line $rawLine) { $rawHtmlBlock = $null }
            $lineStart = $nextLineStart
            continue
        }

        if ($inFence) {
            $closingFence = [regex]::Match($rawLine, '^(?<indent> {0,3})(?<marker>`{3,}|~{3,})(?<remainder>.*)$')
            if ($closingFence.Success) {
                $marker = $closingFence.Groups['marker'].Value
                if ($marker.StartsWith($fenceCharacter, [StringComparison]::Ordinal) -and
                    $marker.Length -ge $fenceLength -and $closingFence.Groups['remainder'].Value -match '^[ \t]*$') {
                    $inFence = $false
                    $fenceCharacter = $null
                    $fenceLength = 0
                    $previousLineWasParagraph = $false
                }
            }
            $lineStart = $nextLineStart
            continue
        }

        if (-not $inHtmlComment -and $rawLine -match '^ {0,3}<!--') {
            $rawHtmlBlock = [pscustomobject]@{ EndKind = 'HtmlComment'; Tag = $null }
            $previousLineWasParagraph = $false
            if (Test-RawHtmlBlockEnd -Block $rawHtmlBlock -Line $rawLine) { $rawHtmlBlock = $null }
            $lineStart = $nextLineStart
            continue
        }

        $commentResult = Remove-HtmlComments -Line $rawLine -InHtmlComment $inHtmlComment
        $line = $commentResult.Text
        $inHtmlComment = $commentResult.InHtmlComment

        $container = Get-MarkdownListLineInfo -Line $line -ListStack $listStack -BlockQuoteListStack $blockQuoteListStack
        $normalizedLine = $container.NormalizedText

        if (-not $inHtmlComment -and $container.IsIndentedCode) {
            $previousLineWasParagraph = $false
            $lineStart = $nextLineStart
            continue
        }

        if (-not $inHtmlComment -and $normalizedLine -match '^ {0,3}<!--') {
            $rawHtmlBlock = [pscustomobject]@{ EndKind = 'HtmlComment'; Tag = $null }
            $previousLineWasParagraph = $false
            if (Test-RawHtmlBlockEnd -Block $rawHtmlBlock -Line $rawLine) { $rawHtmlBlock = $null }
            $lineStart = $nextLineStart
            continue
        }

        $htmlBlockStart = Get-RawHtmlBlockStart -Line $normalizedLine -PreviousLineWasParagraph:$previousLineWasParagraph
        if ($null -ne $htmlBlockStart) {
            $rawHtmlBlock = $htmlBlockStart
            if ($IncludeRawHtmlBlocks) {
                [pscustomobject]@{
                    Text = $line
                    Start = $lineStart
                    End = $nextLineStart
                    Kind = 'Html'
                }
            }
            if (Test-RawHtmlBlockEnd -Block $rawHtmlBlock -Line $rawLine) { $rawHtmlBlock = $null }
            $previousLineWasParagraph = $false
            $lineStart = $nextLineStart
            continue
        }

        $openingFence = [regex]::Match($normalizedLine, '^(?<indent> {0,3})(?<marker>`{3,}|~{3,})(?<remainder>.*)$')
        if ($openingFence.Success) {
            $marker = $openingFence.Groups['marker'].Value
            $inFence = $true
            $fenceCharacter = $marker.Substring(0, 1)
            $fenceLength = $marker.Length
            $previousLineWasParagraph = $false
            $lineStart = $nextLineStart
            continue
        }

        [pscustomobject]@{
            Text = $line
            NormalizedText = $normalizedLine
            Start = $lineStart
            End = $nextLineStart
            Kind = 'Markdown'
            IsList = $container.IsList
            IsListItem = $container.IsListItem
            ListItemContent = $container.ListItemContent
            InBlockQuote = $container.InBlockQuote
            IsTopLevel = $container.IsTopLevel
        }
        $previousLineWasParagraph = -not (Test-IsGfmBlankLine -Line $line) -and
            $normalizedLine -notmatch '^[ \t]{0,3}(?:#{1,6}[ \t]+|(?:[-+*]|[0-9]{1,9}[.)])[ \t]+|>[ \t]?|(?:\*{3,}|-{3,}|_{3,})[ \t]*$)'
        $lineStart = $nextLineStart
    }
}

$headers = @(
    foreach ($line in @(Get-MarkdownVisibleLines -Text $changelog)) {
        if (-not $line.IsTopLevel) { continue }
        $heading = [regex]::Match($line.NormalizedText, '^[ \t]{0,3}##(?!#)[ \t]+(?<text>.*?)[ \t]*#*[ \t]*$')
        if (-not $heading.Success) { continue }

        $headingText = $heading.Groups['text'].Value.Trim()
        $normalizedHeadingText = ConvertFrom-MarkdownHeadingText -Text $headingText
        $bracketedLabel = [regex]::Match($normalizedHeadingText, '^\[\s*(?<label>[^\]\r\n]+?)\s*\](?<suffix>.*)$')
        $suffix = if ($bracketedLabel.Success) { $bracketedLabel.Groups['suffix'].Value } else { '' }
        $dateMatch = [regex]::Match($suffix, '^[ \t]*-[ \t]+(?<date>[^\s]+)[ \t]*$')
        $label = if ($bracketedLabel.Success -and ($suffix.Trim().Length -eq 0 -or $dateMatch.Success)) {
            $bracketedLabel.Groups['label'].Value.Trim()
        }
        else {
            $headingText
        }
        $date = $null
        if ($dateMatch.Success) { $date = $dateMatch.Groups['date'].Value }

        if ([regex]::IsMatch($normalizedHeadingText, $targetVersionPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
            # Any level-two heading that names the target version belongs in the
            # release-state check, even when its brackets, separator, or date are malformed.
            $label = $Version
            $canonicalTarget = [regex]::Match(
                $normalizedHeadingText,
                '^\[\s*(?:v)?' + [regex]::Escape($Version) + '\s*\](?<suffix>.*)$',
                [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if ($canonicalTarget.Success) {
                $targetDateMatch = [regex]::Match($canonicalTarget.Groups['suffix'].Value, '^[ \t]*-[ \t]+(?<date>[^\s]+)[ \t]*$')
                $date = if ($targetDateMatch.Success) { $targetDateMatch.Groups['date'].Value } else { $null }
            }
            else {
                $date = $null
            }
        }

        [pscustomobject]@{
            Index = $line.Start
            BodyStart = $line.End
            Label = $label
            Date = $date
        }
    }
)
$sections = for ($index = 0; $index -lt $headers.Count; $index++) {
    $header = $headers[$index]
    $bodyStart = $header.BodyStart
    $bodyEnd = if ($index + 1 -lt $headers.Count) { $headers[$index + 1].Index } else { $changelog.Length }
    [pscustomobject]@{
        Label = $header.Label
        Date = $header.Date
        Body = $changelog.Substring($bodyStart, $bodyEnd - $bodyStart)
    }
}

function Get-MeaningfulReleaseNotes([string] $Body) {
    @(
        Get-MarkdownVisibleLines -Text $Body |
            Where-Object { $_.IsListItem -and -not [string]::IsNullOrWhiteSpace($_.ListItemContent) }
    )
}

function Get-MarkdownHeadings([string] $Text, [switch] $IncludeNested) {
    $lines = @(Get-MarkdownVisibleLines -Text $Text)
    for ($index = 0; $index -lt $lines.Count; $index++) {
        if (-not $IncludeNested -and -not $lines[$index].IsTopLevel) { continue }
        $line = [string]$lines[$index].NormalizedText
        $atx = [regex]::Match($line, '^(?<indent> {0,3})(?<marks>#{1,6})(?:[ \t]+(?<text>.*?))?[ \t]*$')
        if ($atx.Success) {
            $headingText = $atx.Groups['text'].Value.Trim() -replace '\s+#+\s*$', ''
            if (-not [string]::IsNullOrWhiteSpace($headingText)) {
                [pscustomobject]@{ Level = $atx.Groups['marks'].Value.Length; Text = $headingText }
            }
            continue
        }

        if ($index + 1 -ge $lines.Count -or [string]::IsNullOrWhiteSpace($line) -or
            $line -match '^ {4}' -or $line.Trim() -match '^(?:[-+*]\s+|[0-9]{1,9}[.)]\s+|>\s+)') {
            continue
        }
        if (-not $IncludeNested -and -not $lines[$index + 1].IsTopLevel) { continue }
        $setext = [regex]::Match([string]$lines[$index + 1].NormalizedText, '^(?<indent> {0,3})(?<marker>=+|-+)[ \t]*$')
        if ($setext.Success -and $line.Trim() -notmatch '^(?:`{3,}|~{3,})$') {
            [pscustomobject]@{
                Level = if ($setext.Groups['marker'].Value[0] -eq '=') { 1 } else { 2 }
                Text = $line.Trim()
            }
            [void]$index++
        }
    }
}

function Remove-MarkdownCodeSpans([string] $Text) {
    $visible = [Text.StringBuilder]::new()
    $position = 0
    while ($position -lt $Text.Length) {
        if ($Text[$position] -ne '`') {
            [void]$visible.Append($Text[$position])
            $position++
            continue
        }

        $runEnd = $position
        while ($runEnd -lt $Text.Length -and $Text[$runEnd] -eq '`') { $runEnd++ }
        $runLength = $runEnd - $position
        $closingEnd = -1
        $search = $runEnd
        while ($search -lt $Text.Length) {
            if ($Text[$search] -ne '`') {
                $search++
                continue
            }
            $candidateEnd = $search
            while ($candidateEnd -lt $Text.Length -and $Text[$candidateEnd] -eq '`') { $candidateEnd++ }
            if (($candidateEnd - $search) -eq $runLength) {
                $closingEnd = $candidateEnd
                break
            }
            $search = $candidateEnd
        }

        if ($closingEnd -lt 0) {
            [void]$visible.Append($Text.Substring($position, $runLength))
            $position = $runEnd
            continue
        }
        $position = $closingEnd
    }

    return $visible.ToString()
}

function Get-HtmlHeadings([string] $Text) {
    $headings = [System.Collections.Generic.List[object]]::new()
    $activeLevel = 0
    $headingText = [Text.StringBuilder]::new()
    $rawTextTag = $null
    $index = 0
    while ($index -lt $Text.Length) {
        if ($null -ne $rawTextTag) {
            $closingTag = [regex]::Match($Text.Substring($index), '</' + [regex]::Escape($rawTextTag) + '\s*>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
            if (-not $closingTag.Success) { break }
            $index += $closingTag.Index + $closingTag.Length
            $rawTextTag = $null
            continue
        }

        if ($Text[$index] -ne '<') {
            if ($activeLevel -gt 0) { [void]$headingText.Append($Text[$index]) }
            $index++
            continue
        }

        if ($Text.Substring($index).StartsWith('<![CDATA[', [StringComparison]::OrdinalIgnoreCase)) {
            $end = $Text.IndexOf(']]>', $index + 9, [StringComparison]::Ordinal)
            $index = if ($end -ge 0) { $end + 3 } else { $Text.Length }
            continue
        }
        if ($Text.Substring($index).StartsWith('<?', [StringComparison]::Ordinal)) {
            $end = $Text.IndexOf('?>', $index + 2, [StringComparison]::Ordinal)
            $index = if ($end -ge 0) { $end + 2 } else { $Text.Length }
            continue
        }
        if ($Text.Substring($index).StartsWith('<!', [StringComparison]::Ordinal)) {
            $end = $Text.IndexOf('>', $index + 2, [StringComparison]::Ordinal)
            $index = if ($end -ge 0) { $end + 1 } else { $Text.Length }
            continue
        }

        $tag = [regex]::Match($Text.Substring($index), '^<(?<close>/)?(?<name>[A-Za-z][A-Za-z0-9-]*)')
        if (-not $tag.Success) {
            if ($activeLevel -gt 0) { [void]$headingText.Append($Text[$index]) }
            $index++
            continue
        }

        $tagEnd = Find-HtmlTagEnd -Text $Text -StartIndex ($index + $tag.Length)
        if ($tagEnd -lt 0) { break }
        $tagName = $tag.Groups['name'].Value.ToLowerInvariant()
        $isClosing = $tag.Groups['close'].Success
        if ($activeLevel -gt 0) {
            if ($isClosing -and $tagName -ceq ('h' + $activeLevel)) {
                $headings.Add([pscustomobject]@{
                    Level = $activeLevel
                    Text = [Net.WebUtility]::HtmlDecode($headingText.ToString()).Trim()
                })
                $activeLevel = 0
                [void]$headingText.Clear()
            }
        }
        elseif (-not $isClosing -and $tagName -match '^h[1-6]$') {
            $activeLevel = [int]$tagName.Substring(1, 1)
            [void]$headingText.Clear()
        }

        if (-not $isClosing -and $tagName -in @('script', 'style', 'textarea', 'title')) {
            $rawTextTag = $tagName
        }
        $index = $tagEnd + 1
    }

    if ($activeLevel -gt 0) {
        $headings.Add([pscustomobject]@{
            Level = $activeLevel
            Text = [Net.WebUtility]::HtmlDecode($headingText.ToString()).Trim()
        })
    }

    return @($headings)
}

function Get-VisibleHtmlHeadings([string] $Text) {
    $visibleLines = @(Get-MarkdownVisibleLines -Text $Text -IncludeRawHtmlBlocks)
    $htmlLines = foreach ($line in $visibleLines) {
        if ($line.Kind -eq 'Markdown') {
            Remove-MarkdownCodeSpans -Text $line.Text
        }
        else {
            $line.Text
        }
    }
    return @(Get-HtmlHeadings -Text ([string]::Join("`n", [string[]]$htmlLines)))
}

$targetSections = @($sections | Where-Object { $_.Label -ceq $Version })
$targetHeadings = @(
    @((Get-MarkdownHeadings -Text $changelog -IncludeNested) + (Get-VisibleHtmlHeadings -Text $changelog)) |
        Where-Object { [regex]::IsMatch((ConvertFrom-MarkdownHeadingText -Text $_.Text), $targetVersionPattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase) }
)
$unreleasedSections = @($sections | Where-Object { $_.Label -ceq 'Unreleased' })
if ($unreleasedSections.Count -gt 1) {
    throw 'Changelog contains duplicate Unreleased sections.'
}

if ($RequireUnreleased) {
    if ($targetHeadings.Count -gt 0) {
        throw 'The target release must remain under Unreleased until release approval.'
    }

    if ($unreleasedSections.Count -ne 1) {
        throw 'Candidate changelog must contain exactly one ## [Unreleased] section.'
    }

    if (@(Get-MeaningfulReleaseNotes $unreleasedSections[0].Body).Count -eq 0) {
        throw 'Candidate release notes must remain under Unreleased until release approval.'
    }
}

if ($RequireFinalized) {
    if ($targetSections.Count -ne 1 -or $targetHeadings.Count -ne 1) {
        throw 'Exactly one finalized target release section is required.'
    }

    $target = $targetSections[0]
    if ([string]::IsNullOrWhiteSpace($target.Date) -or $target.Date -notmatch '^\d{4}-\d{2}-\d{2}$') {
        throw 'Finalized release changelog entry with an ISO date is required.'
    }

    $parsedDate = [DateTime]::MinValue
    if (-not [DateTime]::TryParseExact($target.Date, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::None, [ref]$parsedDate)) {
        throw 'Finalized release changelog date is not a valid calendar date.'
    }

    $todayUtc = [DateTime]::UtcNow.Date
    if ($parsedDate -lt $todayUtc) {
        throw 'Finalized release changelog date is stale relative to the current UTC date.'
    }
    if ($parsedDate -gt $todayUtc) {
        throw 'Finalized release changelog date is in the future relative to the current UTC date.'
    }

    if (@(Get-MeaningfulReleaseNotes $target.Body).Count -eq 0) {
        throw 'Finalized target release section must contain externally meaningful release notes.'
    }

    if ($target.Body -match '(?i)\b(?:planned|tbd|unreleased|not[\s-]+yet[\s-]+published|not[\s-]+published)\b') {
        throw 'Finalized target release section contains an unfinalized release marker.'
    }

    if ($FirstRelease) {
        if ($unreleasedSections.Count -ne 1) {
            throw 'First-release changelog must contain exactly one ## [Unreleased] section.'
        }

        $releaseHeadings = @(Get-MarkdownHeadings -Text $target.Body)
        if (@($releaseHeadings | Where-Object { $_.Level -eq 1 }).Count -gt 0) {
            throw 'First-release notes must use a release subsection heading, not a document heading.'
        }
        $releaseCategories = @($releaseHeadings | ForEach-Object { $_.Text.Trim().TrimEnd(':').Trim() })
        if (@($releaseCategories | Where-Object { $_ -ceq 'Added' }).Count -ne 1) {
            throw 'First-release notes must contain an Added section.'
        }

        $prohibitedMarkers = @(
            '\bnow\b',
            '\bno\s+longer\b',
            '\bpreviously\b',
            '\bformerly\b',
            '\bused\s+to\b',
            '\bfixed\b',
            '\bfixes\b',
            '\bcorrected\b',
            '\bresolved\b',
            '\baddressed\b',
            '\bthis\s+removes\b',
            '\bthis\s+fixes\b',
            '\bchanged\s+from\b'
        )
        foreach ($marker in $prohibitedMarkers) {
            if ($target.Body -match "(?i)$marker") {
                throw "First-release notes contain prohibited remediation wording: $marker."
            }
        }

        $unexpectedCategories = @($releaseCategories | Where-Object { $_ -cne 'Added' })
        if ($unexpectedCategories.Count -gt 0) {
            throw "First-release notes contain non-Added release categories: $($unexpectedCategories -join ', ')."
        }
    }

    if ($unreleasedSections.Count -eq 1 -and @(Get-MeaningfulReleaseNotes $unreleasedSections[0].Body).Count -gt 0) {
        throw 'Release notes for the finalized target remain under Unreleased.'
    }
}
elseif (-not $RequireUnreleased -and $targetSections.Count -eq 0 -and $unreleasedSections.Count -eq 0) {
    throw 'Changelog must contain either the requested version entry or an Unreleased entry during candidate review.'
}

Write-Output "RELEASE_CONTRACT=PASS VERSION=$Version FINALIZED=$RequireFinalized"
