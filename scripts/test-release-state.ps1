$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$validator = Join-Path $PSScriptRoot 'validate-release.ps1'
$projectFile = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/KeelMatrix.PackageSurface.Cli.csproj'
$projectText = Get-Content -LiteralPath $projectFile -Raw
$versionMatch = [regex]::Match($projectText, '<Version>\s*(?<version>[^<]+?)\s*</Version>', [Text.RegularExpressions.RegexOptions]::IgnoreCase)
if (-not $versionMatch.Success) { throw 'The packable project has no explicit package version.' }
$version = $versionMatch.Groups['version'].Value.Trim()
$releaseDate = [DateTime]::UtcNow.Date.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-release-state-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $scratch | Out-Null

function ConvertTo-PlainTextOutput {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string] $Text)

    $withoutAnsi = [regex]::Replace($Text, '\x1B\[[0-?]*[ -/]*[@-~]', '')
    $withoutPowerShellGutters = [regex]::Replace($withoutAnsi, '(?<=\w)\s*\|\s*(?=\w)', ' ')
    return [regex]::Replace($withoutPowerShellGutters, '\s+', ' ').Trim()
}

function Invoke-ReleaseValidation {
    param(
        [Parameter(Mandatory)] [string] $ChangelogPath,
        [switch] $RequireFinalized,
        [switch] $RequireUnreleased,
        [switch] $FirstRelease
    )

    $arguments = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $validator,
        '-Version',
        $version,
        '-ProjectFile',
        $projectFile,
        '-ChangelogPath',
        $ChangelogPath
    )
    if ($RequireFinalized) { $arguments += '-RequireFinalized' }
    if ($RequireUnreleased) { $arguments += '-RequireUnreleased' }
    if ($FirstRelease) { $arguments += '-FirstRelease' }
    $output = @(Invoke-NestedPwsh -ArgumentList $arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $outputText = ($output | ForEach-Object { [string]$_ }) -join ' '
    [pscustomobject]@{
        ExitCode = $exitCode
        Output = ConvertTo-PlainTextOutput $outputText
    }
}

function Assert-ReleasePass {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [psobject] $Result)
    if ($Result.ExitCode -ne 0 -or $Result.Output -notmatch 'RELEASE_CONTRACT=PASS') {
        throw "Release-state case '$Name' did not pass. Exit code: $($Result.ExitCode). Output: $($Result.Output)"
    }
}

function Assert-ReleaseRejects {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [psobject] $Result,
        [Parameter(Mandatory)] [string] $Message
    )
    if ($Result.ExitCode -eq 0 -or $Result.Output -notmatch [regex]::Escape($Message)) {
        throw "Release-state case '$Name' did not fail with '$Message'. Exit code: $($Result.ExitCode). Output: $($Result.Output)"
    }
}

function New-ReleaseFixture {
    param([Parameter(Mandatory)] [string] $Name, [Parameter(Mandatory)] [AllowEmptyString()] [string] $Content)
    $path = Join-Path $scratch $Name
    [IO.File]::WriteAllText($path, $Content, [Text.UTF8Encoding]::new($false))
    return $path
}

function New-ReleaseStateFixture {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [ValidateSet('Unreleased', 'Finalized')] [string] $State,
        [Parameter(Mandatory)] [string] $Body,
        [Parameter(Mandatory)] [string] $LineEnding
    )

    $lines = @('# Changelog', '', '## [Unreleased]')
    if ($State -eq 'Finalized') {
        $lines += @('', "## [$version] - $releaseDate", '', '### Added')
    }
    else {
        $lines += @('', '### Added')
    }
    $normalizedBody = $Body.Replace("`n", $LineEnding)
    return New-ReleaseFixture -Name $Name -Content (($lines + @('', $normalizedBody, '')) -join $LineEnding)
}

$ansi = [char]27
$renderedDiagnostic = $ansi + '[31;1m' + 'Finalized target release section must contain externally meaningful' + $ansi + '[0m' + ' | release notes.'
if ((ConvertTo-PlainTextOutput $renderedDiagnostic) -cne 'Finalized target release section must contain externally meaningful release notes.') {
    throw 'PowerShell-rendered diagnostics were not normalized.'
}

try {
    $staleDate = [DateTime]::UtcNow.Date.AddDays(-1).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $futureDate = [DateTime]::UtcNow.Date.AddDays(1).ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $headingForms = @(
        "## [$version] - $releaseDate (pending finalization)",
        " ##    [$version]  --  $releaseDate",
        "## [ $version ] — $releaseDate",
        "## [v$version] - $releaseDate",
        "## [0\.1\.0] - $releaseDate",
        "## [0&#46;1&#46;0] - $releaseDate",
        "## [$version] -",
        "## [$version] - not-a-date",
        '## [0.1.0] - 2026-02-30',
        "## [$version] - $staleDate",
        "## [$version] - $releaseDate",
        "## [$version] - $futureDate",
        "## [$version - $releaseDate",
        "## $version - $releaseDate",
        "### [$version] - $releaseDate"
    )
    for ($index = 0; $index -lt $headingForms.Count; $index++) {
        $path = New-ReleaseFixture -Name "target-heading-$index.md" -Content (
            "# Changelog`n`n## [Unreleased]`n`n- Candidate note.`n`n$($headingForms[$index])`n`n### Added`n`n- Finalized note.`n")
        $result = Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
        Assert-ReleaseRejects "pre-release target heading form $index" $result 'The target release must remain under Unreleased'
    }

    $hiddenTargetExampleContent = "# Changelog`n`n## [Unreleased]`n`n- Candidate note.`n`n" +
        '```markdown' + "`n" + "## [$version] - $releaseDate" + "`n" + '```' + "`n`n" +
        "<!-- hidden`n## [$version] - $releaseDate`n-->`n"
    $hiddenTargetExample = New-ReleaseFixture -Name 'hidden-target-examples.md' -Content $hiddenTargetExampleContent
    Assert-ReleasePass 'hidden target-heading examples do not change candidate state' (Invoke-ReleaseValidation -ChangelogPath $hiddenTargetExample -RequireUnreleased)

    $htmlCommentWithTrailingNote = New-ReleaseStateFixture -Name 'html-comment-trailing-note.md' -State Unreleased -Body '<!-- ignored comment --> - Hidden release note.' -LineEnding "`n"
    Assert-ReleaseRejects 'Markdown-like text after a block HTML comment stays hidden' (Invoke-ReleaseValidation -ChangelogPath $htmlCommentWithTrailingNote -RequireUnreleased) 'Candidate release notes must remain under Unreleased'

    $htmlHeadingForms = @(
        [pscustomobject]@{ Name = 'h1-lowercase'; Text = "<h1 class='release'>[$version]</h1>" },
        [pscustomobject]@{ Name = 'h2-uppercase-attributes'; Text = "<H2 class=`"release`" data-label=`"x > y`"> [$version] - $releaseDate </h2>" },
        [pscustomobject]@{ Name = 'h3-multiline-open-tag'; Text = ('<h3' + "`n" + ' class="release"' + "`n" + " data-version=`"$version`">" + "`n" + "  [$version] - $releaseDate" + "`n" + '</h3>') },
        [pscustomobject]@{ Name = 'h4-leading-whitespace'; Text = "   <H4 id='release'>[$version] - $releaseDate</h4>" },
        [pscustomobject]@{ Name = 'h5-nested-inline-tag'; Text = "<h5 data-kind='release'><span>[$version]</span> - $releaseDate</h5>" },
        [pscustomobject]@{ Name = 'h6-whitespace'; Text = "<h6  data-version = '$version' > [$version] </h6>" }
    )
    foreach ($lineEnding in @("`n", "`r`n", "`r")) {
        $lineEndingName = if ($lineEnding -eq "`n") { 'lf' } elseif ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
        foreach ($headingForm in $htmlHeadingForms) {
            $candidatePath = New-ReleaseStateFixture -Name "raw-$($headingForm.Name)-candidate-$lineEndingName.md" -State Unreleased -Body $headingForm.Text -LineEnding $lineEnding
            $candidateResult = Invoke-ReleaseValidation -ChangelogPath $candidatePath -RequireUnreleased
            Assert-ReleaseRejects "rendered $($headingForm.Name) heading in candidate mode ($lineEndingName)" $candidateResult 'The target release must remain under Unreleased'

            $finalizedPath = New-ReleaseStateFixture -Name "raw-$($headingForm.Name)-finalized-$lineEndingName.md" -State Finalized -Body ("- Public release note.`n`n" + $headingForm.Text) -LineEnding $lineEnding
            $finalizedResult = Invoke-ReleaseValidation -ChangelogPath $finalizedPath -RequireFinalized -FirstRelease
            Assert-ReleaseRejects "duplicate rendered $($headingForm.Name) heading in finalized mode ($lineEndingName)" $finalizedResult 'Exactly one finalized target release section'
        }
    }

    $hiddenHtmlHeadingBody = @(
        '- Candidate note.'
        ''
        '```html'
        "<h2>[$version] - $releaseDate</h2>"
        '```'
        ''
        '<!--'
        "<H3 class='hidden'>[$version]</H3>"
        '-->'
        ''
        "    <h4>[$version]</h4>"
        ''
        "- Inline code example ``<h5>[$version]</h5>`` stays visible as code."
        ''
        '<script>'
        "<h5>[$version]</h5>"
        '</script>'
        ''
        "<div data-title=`"<h6>[$version]</h6>`">"
        'No heading tag occurs outside the quoted attribute.'
        '</div>'
        ''
        '<div>'
        "## [$version] - $releaseDate"
        '</div>'
    ) -join "`n"
    $hiddenHtmlHeadingCandidate = New-ReleaseStateFixture -Name 'hidden-html-headings-candidate.md' -State Unreleased -Body $hiddenHtmlHeadingBody -LineEnding "`n"
    Assert-ReleasePass 'HTML heading lookalikes in fences, comments, indented code, inline code, and HTML-block Markdown stay hidden in candidate mode' (Invoke-ReleaseValidation -ChangelogPath $hiddenHtmlHeadingCandidate -RequireUnreleased)
    $hiddenHtmlHeadingFinalized = New-ReleaseStateFixture -Name 'hidden-html-headings-finalized.md' -State Finalized -Body $hiddenHtmlHeadingBody -LineEnding "`n"
    Assert-ReleasePass 'HTML heading lookalikes in non-rendered contexts stay hidden in finalized mode' (Invoke-ReleaseValidation -ChangelogPath $hiddenHtmlHeadingFinalized -RequireFinalized -FirstRelease)

    $finalizedControl = New-ReleaseStateFixture -Name 'finalized-visible-control.md' -State Finalized -Body '- Public release note.' -LineEnding "`n"
    Assert-ReleasePass 'canonical finalized release with a visible note' (Invoke-ReleaseValidation -ChangelogPath $finalizedControl -RequireFinalized -FirstRelease)

    $nestedListHeaders = @(
        [pscustomobject]@{ Name = 'tight-unordered'; Body = "- item`n  ## [$version] - $releaseDate`n  ### Added`n  - Nested release note." },
        [pscustomobject]@{ Name = 'loose-unordered'; Body = "- item`n`n  ## [$version] - $releaseDate`n  ### Added`n  - Nested release note." },
        [pscustomobject]@{ Name = 'tight-ordered'; Body = "1. item`n   ## [$version] - $releaseDate`n   ### Added`n   - Nested release note." },
        [pscustomobject]@{ Name = 'loose-ordered'; Body = "1. item`n`n   ## [$version] - $releaseDate`n   ### Added`n   - Nested release note." },
        [pscustomobject]@{ Name = 'setext-unordered'; Body = "- item`n  [$version] - $releaseDate`n  ----------------`n  - Nested release note." },
        [pscustomobject]@{ Name = 'setext-blockquote-list'; Body = "> - item`n>   [$version] - $releaseDate`n>   ----------------`n>   - Nested release note." },
        [pscustomobject]@{ Name = 'nested-list'; Body = "- outer`n  - inner`n    ## [$version] - $releaseDate`n    ### Added`n    - Nested release note." },
        [pscustomobject]@{ Name = 'list-in-blockquote'; Body = "> - item`n>   ## [$version] - $releaseDate`n>   ### Added`n>   - Nested release note." }
    )
    foreach ($case in $nestedListHeaders) {
        $candidateBody = "- Candidate note.`n`n$($case.Body)"
        $candidatePath = New-ReleaseStateFixture -Name "nested-$($case.Name)-candidate.md" -State Unreleased -Body $candidateBody -LineEnding "`n"
        Assert-ReleaseRejects "nested $($case.Name) target heading in candidate mode" (Invoke-ReleaseValidation -ChangelogPath $candidatePath -RequireUnreleased) 'The target release must remain under Unreleased'

        $finalizedPath = New-ReleaseStateFixture -Name "nested-$($case.Name)-finalized.md" -State Finalized -Body $case.Body -LineEnding "`n"
        Assert-ReleaseRejects "nested $($case.Name) target heading in finalized mode" (Invoke-ReleaseValidation -ChangelogPath $finalizedPath -RequireFinalized -FirstRelease) 'Exactly one finalized target release section'
    }

    foreach ($emptyEntry in @('-', '- <!-- comment-only list entry -->')) {
        $emptyFixtureName = if ($emptyEntry -eq '-') { 'empty-unreleased-list-entry.md' } else { 'comment-only-unreleased-list-entry.md' }
        $emptyUnreleased = New-ReleaseFixture -Name $emptyFixtureName -Content (
            "# Changelog`n`n## [Unreleased]`n`n$emptyEntry`n  ## [$version] - $releaseDate`n  ### Added`n`n  - Nested release note.`n")
        Assert-ReleaseRejects "finalized mode ignores Unreleased $emptyEntry nested heading" (Invoke-ReleaseValidation -ChangelogPath $emptyUnreleased -RequireFinalized -FirstRelease) 'Exactly one finalized target release section'
        Assert-ReleaseRejects "candidate mode rejects Unreleased $emptyEntry nested heading" (Invoke-ReleaseValidation -ChangelogPath $emptyUnreleased -RequireUnreleased) 'The target release must remain under Unreleased'

        $noteOnlyUnreleased = New-ReleaseStateFixture -Name 'empty-unreleased-entry-only.md' -State Unreleased -Body $emptyEntry -LineEnding "`n"
        Assert-ReleaseRejects "candidate mode requires visible notes after $emptyEntry" (Invoke-ReleaseValidation -ChangelogPath $noteOnlyUnreleased -RequireUnreleased) 'Candidate release notes must remain under Unreleased'
    }

    foreach ($indent in 0..3) {
        $spaces = ' ' * $indent
        $topLevelIndented = New-ReleaseFixture -Name "top-level-release-heading-indent-$indent.md" -Content (
            "# Changelog`n`n## [Unreleased]`n`n${spaces}## [$version] - $releaseDate`n`n### Added`n`n- Public release note.`n")
        Assert-ReleasePass "top-level release heading with $indent leading spaces" (Invoke-ReleaseValidation -ChangelogPath $topLevelIndented -RequireFinalized -FirstRelease)
    }

    $outdentedListBoundary = New-ReleaseFixture -Name 'outdented-list-boundary.md' -Content (
        "# Changelog`n`n## [Unreleased]`n`n-`n ## [$version] - $releaseDate`n`n### Added`n`n- Public release note.`n")
    Assert-ReleasePass 'under-indented heading closes the preceding list' (Invoke-ReleaseValidation -ChangelogPath $outdentedListBoundary -RequireFinalized -FirstRelease)

    $fourSpaceCodeHeading = New-ReleaseFixture -Name 'four-space-code-heading.md' -Content (
        "# Changelog`n`n## [Unreleased]`n`n- Candidate note.`n`nParagraph ends the list.`n`n    ## [$version] - $releaseDate`n`n    ### Added`n`n    - Not a release note.`n")
    Assert-ReleasePass 'four-space code heading does not change candidate state' (Invoke-ReleaseValidation -ChangelogPath $fourSpaceCodeHeading -RequireUnreleased)
    Assert-ReleaseRejects 'four-space code heading cannot finalize a release' (Invoke-ReleaseValidation -ChangelogPath $fourSpaceCodeHeading -RequireFinalized -FirstRelease) 'Exactly one finalized target release section'

    foreach ($malformedHeading in @(
        "## [$version] - $releaseDate (pending finalization)",
        "## [$version] -- $releaseDate",
        "## [$version] -",
        "## [$version] - not-a-date",
        "## [$version - $releaseDate",
        "### [$version] - $releaseDate"
    )) {
        $path = New-ReleaseFixture -Name ('finalized-malformed-' + [Guid]::NewGuid().ToString('N') + '.md') -Content (
            "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n### Added`n`n- Public release note.`n")
        $text = Get-Content -LiteralPath $path -Raw
        $text = $text.Replace("## [$version] - $releaseDate", $malformedHeading)
        [IO.File]::WriteAllText($path, $text, [Text.UTF8Encoding]::new($false))
        $result = Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
        if ($malformedHeading -match '^###') {
            Assert-ReleaseRejects 'nested target heading cannot finalize a release section' $result 'Exactly one finalized target release section'
        }
        else {
            Assert-ReleaseRejects 'malformed finalized target heading date' $result 'Finalized release changelog entry with an ISO date'
        }
    }

    $hiddenNoteBodies = @(
        [pscustomobject]@{ Name = 'three-backtick-fence'; Text = ('```markdown' + "`n" + '- Hidden note.' + "`n" + '```') },
        [pscustomobject]@{ Name = 'four-backtick-fence-with-short-close'; Text = ('````markdown' + "`n" + '- Hidden note.' + "`n" + '```' + "`n" + '- Still hidden.' + "`n" + '````') },
        [pscustomobject]@{ Name = 'three-tilde-fence'; Text = ('~~~markdown' + "`n" + '- Hidden note.' + "`n" + '~~~') },
        [pscustomobject]@{ Name = 'five-tilde-fence-with-short-close'; Text = ('  ~~~~~markdown' + "`n" + '- Hidden note.' + "`n" + '~~~' + "`n" + '- Still hidden.' + "`n" + '~~~~~') },
        [pscustomobject]@{ Name = 'four-space-indented-code'; Text = ('    - Hidden note.' + "`n" + "`n" + '    1. Also hidden.') },
        [pscustomobject]@{ Name = 'tab-indented-code'; Text = "`t- Hidden note." },
        [pscustomobject]@{ Name = 'inline-html-comment'; Text = '- <!-- Hidden note. -->' },
        [pscustomobject]@{ Name = 'multiline-html-comment'; Text = ('<!-- hidden start' + "`n" + '- Hidden note.' + "`n" + 'hidden end -->') }
    )
    foreach ($hiddenNote in $hiddenNoteBodies) {
        foreach ($state in @('Unreleased', 'Finalized')) {
            $path = New-ReleaseStateFixture -Name "hidden-note-$($hiddenNote.Name)-$state.md" -State $state -Body $hiddenNote.Text -LineEnding "`n"
            $result = if ($state -eq 'Finalized') {
                Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
            }
            else {
                Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
            }
            $expected = if ($state -eq 'Finalized') {
                'Finalized target release section must contain externally meaningful release'
            }
            else {
                'Candidate release notes must remain under Unreleased'
            }
            Assert-ReleaseRejects "$state hidden note: $($hiddenNote.Name)" $result $expected
        }
    }

    $rawHtmlHiddenNoteBlocks = @(
        [pscustomobject]@{ Name = 'pre-type-one'; Text = ('<pre>' + "`n" + '- Hidden release note.' + "`n" + '</pre>') },
        [pscustomobject]@{ Name = 'script-type-one'; Text = ('<script type="text/plain">' + "`n" + '- Hidden release note.' + "`n" + '</script>') },
        [pscustomobject]@{ Name = 'style-type-one'; Text = ('<STYLE>' + "`n" + '- Hidden release note.' + "`n" + '</style>') },
        [pscustomobject]@{ Name = 'div-type-six'; Text = ('<DIV class="release">' + "`n" + '- Hidden release note.' + "`n" + '</DIV>') },
        [pscustomobject]@{ Name = 'article-type-six'; Text = ('<article>' + "`n" + '- Hidden release note.' + "`n" + '</article>') },
        [pscustomobject]@{ Name = 'table-type-six'; Text = ('<table>' + "`n" + '- Hidden release note.' + "`n" + '</table>') },
        [pscustomobject]@{ Name = 'ul-type-six'; Text = ('<ul>' + "`n" + '- Hidden release note.' + "`n" + '</ul>') },
        [pscustomobject]@{ Name = 'custom-tag-type-seven'; Text = ('<Warning class="release">' + "`n" + '- Hidden release note.' + "`n" + '</Warning>') },
        [pscustomobject]@{ Name = 'processing-instruction-type-three'; Text = ('<?xml' + "`n" + '- Hidden release note.' + "`n" + '?>') },
        [pscustomobject]@{ Name = 'declaration-type-four'; Text = ('<!DOCTYPE html' + "`n" + '- Hidden release note.' + "`n" + '>') },
        [pscustomobject]@{ Name = 'cdata-type-five'; Text = ('<![CDATA[' + "`n" + '- Hidden release note.' + "`n" + ']]>') }
    )
    foreach ($hiddenBlock in $rawHtmlHiddenNoteBlocks) {
        foreach ($state in @('Unreleased', 'Finalized')) {
            $lineEndings = if ($hiddenBlock.Name -in @('pre-type-one', 'div-type-six')) { @("`n", "`r`n", "`r") } else { @("`n") }
            foreach ($lineEnding in $lineEndings) {
                $lineEndingName = if ($lineEnding -eq "`n") { 'lf' } elseif ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
                $path = New-ReleaseStateFixture -Name "html-hidden-note-$($hiddenBlock.Name)-$state-$lineEndingName.md" -State $state -Body $hiddenBlock.Text -LineEnding $lineEnding
                $result = if ($state -eq 'Finalized') {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
                }
                else {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
                }
                $expected = if ($state -eq 'Finalized') {
                    'Finalized target release section must contain externally meaningful release'
                }
                else {
                    'Candidate release notes must remain under Unreleased'
                }
                Assert-ReleaseRejects "$state raw HTML block note is not a visible Markdown item: $($hiddenBlock.Name) ($lineEndingName)" $result $expected
            }
        }
    }

    $unicodeWhitespaceCodePoints = @(
        0x000B, 0x000C, 0x0085, 0x00A0, 0x1680,
        0x2000, 0x2001, 0x2002, 0x2003, 0x2004, 0x2005, 0x2006, 0x2007, 0x2008, 0x2009, 0x200A,
        0x2028, 0x2029, 0x202F, 0x205F, 0x3000
    )
    $unicodeWhitespaceCases = @(
        foreach ($codePoint in $unicodeWhitespaceCodePoints) {
            $character = [char]$codePoint
            if (-not [char]::IsWhiteSpace($character) -or -not [string]::IsNullOrWhiteSpace([string]$character)) {
                throw ('The U+{0:X4} fixture is not accepted by the previous .NET whitespace check.' -f $codePoint)
            }
            [pscustomobject]@{
                CodePoint = 'U+{0:X4}' -f $codePoint
                Character = [string]$character
            }
        }
    )
    $gfmRawHtmlBlockTypes = @(
        [pscustomobject]@{ Name = 'type-six'; StartTag = '<div>' },
        [pscustomobject]@{ Name = 'type-seven'; StartTag = '<Warning>' }
    )
    $lineEndings = @("`n", "`r`n", "`r")
    foreach ($blockType in $gfmRawHtmlBlockTypes) {
        $unicodeBlockLines = [System.Collections.Generic.List[string]]::new()
        foreach ($whitespace in $unicodeWhitespaceCases) {
            $unicodeBlockLines.Add($blockType.StartTag)
            $unicodeBlockLines.Add('- Hidden release note.')
            $unicodeBlockLines.Add($whitespace.Character)
            $unicodeBlockLines.Add("- Visible only if $($whitespace.CodePoint) incorrectly ends the HTML block.")
            $unicodeBlockLines.Add('')
        }
        $unicodeBlockBody = $unicodeBlockLines -join "`n"
        foreach ($state in @('Unreleased', 'Finalized')) {
            foreach ($lineEnding in $lineEndings) {
                $lineEndingName = if ($lineEnding -eq "`n") { 'lf' } elseif ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
                $path = New-ReleaseStateFixture -Name "gfm-$($blockType.Name)-unicode-whitespace-$state-$lineEndingName.md" -State $state -Body $unicodeBlockBody -LineEnding $lineEnding
                $result = if ($state -eq 'Finalized') {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
                }
                else {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
                }
                $expected = if ($state -eq 'Finalized') {
                    'Finalized target release section must contain externally meaningful release'
                }
                else {
                    'Candidate release notes must remain under Unreleased'
                }
                $codePoints = $unicodeWhitespaceCases.CodePoint -join ', '
                Assert-ReleaseRejects "$state $($blockType.Name) notes remain hidden after every .NET whitespace character ($codePoints; $lineEndingName)" $result $expected
            }
        }
    }

    $gfmBlankLineControls = @(
        [pscustomobject]@{ Name = 'empty'; Line = '' },
        [pscustomobject]@{ Name = 'ascii-spaces'; Line = '   ' },
        [pscustomobject]@{ Name = 'ascii-tabs'; Line = "`t`t" }
    )
    foreach ($blockType in $gfmRawHtmlBlockTypes) {
        foreach ($control in $gfmBlankLineControls) {
            foreach ($state in @('Unreleased', 'Finalized')) {
                foreach ($lineEnding in $lineEndings) {
                    $lineEndingName = if ($lineEnding -eq "`n") { 'lf' } elseif ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
                    $bodyLines = @($blockType.StartTag, '- Hidden release note.', $control.Line, '- Visible Markdown release note after a GFM blank line.')
                    $body = $bodyLines -join "`n"
                    $path = New-ReleaseStateFixture -Name "gfm-$($blockType.Name)-$($control.Name)-visible-$state-$lineEndingName.md" -State $state -Body $body -LineEnding $lineEnding
                    $result = if ($state -eq 'Finalized') {
                        Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
                    }
                    else {
                        Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
                    }
                    Assert-ReleasePass "$state $($blockType.Name) block ends at a valid $($control.Name) GFM blank line ($lineEndingName)" $result
                }
            }
        }
    }

    $nonBreakingSpace = [string][char]0x00A0
    $typeSevenWhitespaceBoundaryBodies = @(
        [pscustomobject]@{
            Name = 'unicode-trailing-tag-space'
            Text = ('<Warning>' + $nonBreakingSpace + "`n" + '- Visible Markdown release note.')
        },
        [pscustomobject]@{
            Name = 'unicode-space-does-not-separate-paragraph'
            Text = (@('A preceding paragraph.', $nonBreakingSpace, '<Warning>', '- Visible Markdown release note.', '</Warning>') -join "`n")
        }
    )
    foreach ($boundary in $typeSevenWhitespaceBoundaryBodies) {
        foreach ($state in @('Unreleased', 'Finalized')) {
            foreach ($lineEnding in $lineEndings) {
                $lineEndingName = if ($lineEnding -eq "`n") { 'lf' } elseif ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
                $path = New-ReleaseStateFixture -Name "gfm-type-seven-$($boundary.Name)-$state-$lineEndingName.md" -State $state -Body $boundary.Text -LineEnding $lineEnding
                $result = if ($state -eq 'Finalized') {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
                }
                else {
                    Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
                }
                Assert-ReleasePass "$state type-seven boundary remains visible with Unicode whitespace ($($boundary.Name); $lineEndingName)" $result
            }
        }
    }

    $visibleAfterHtmlBlockBody = ('<div class="release">' + "`n" + '- Hidden release note.' + "`n" + '</div>' + "`n`n" + '- Visible release note after the HTML block.')
    foreach ($state in @('Unreleased', 'Finalized')) {
        $path = New-ReleaseStateFixture -Name "visible-note-after-html-block-$state.md" -State $state -Body $visibleAfterHtmlBlockBody -LineEnding "`r`n"
        $result = if ($state -eq 'Finalized') {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
        }
        else {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
        }
        Assert-ReleasePass "$state visible Markdown note after a terminated HTML block" $result
    }

    $typeSevenInlineControlBody = @(
        'A paragraph keeps this custom tag inline.'
        '<notice>'
        '- Visible Markdown release note.'
        '</notice>'
    ) -join "`n"
    foreach ($state in @('Unreleased', 'Finalized')) {
        $path = New-ReleaseStateFixture -Name "type-seven-inline-control-$state.md" -State $state -Body $typeSevenInlineControlBody -LineEnding "`n"
        $result = if ($state -eq 'Finalized') {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
        }
        else {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
        }
        Assert-ReleasePass "$state type-seven HTML after a paragraph does not hide a rendered Markdown item" $result
    }

    foreach ($lineEnding in @("`r`n", "`r")) {
        $lineEndingName = if ($lineEnding -eq "`r`n") { 'crlf' } else { 'cr' }
        $commentBody = '<!-- hidden start' + "`n" + '- Hidden note.' + "`n" + 'hidden end -->'
        foreach ($state in @('Unreleased', 'Finalized')) {
            $path = New-ReleaseStateFixture -Name "hidden-comment-$lineEndingName-$state.md" -State $state -Body $commentBody -LineEnding $lineEnding
            $result = if ($state -eq 'Finalized') {
                Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
            }
            else {
                Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
            }
            $expected = if ($state -eq 'Finalized') {
                'Finalized target release section must contain externally meaningful release'
            }
            else {
                'Candidate release notes must remain under Unreleased'
            }
            Assert-ReleaseRejects "$state hidden multiline comment with $lineEndingName line endings" $result $expected
        }
    }

    $visibleNoteBody = '```markdown' + "`n" + '- Hidden code sample.' + "`n" + '```' + "`n" +
        '<!-- hidden comment start' + "`n" + '- Hidden commented item.' + "`n" + '-->' + "`n" +
        '   - Visible release note <!-- editorial detail --> remains visible.' + "`n" +
        '1. A visible numbered note.'
    foreach ($state in @('Unreleased', 'Finalized')) {
        $path = New-ReleaseStateFixture -Name "visible-notes-$state.md" -State $state -Body $visibleNoteBody -LineEnding "`n"
        $result = if ($state -eq 'Finalized') {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireFinalized -FirstRelease
        }
        else {
            Invoke-ReleaseValidation -ChangelogPath $path -RequireUnreleased
        }
        Assert-ReleasePass "$state visible list-item controls" $result
    }

    $hiddenAddedHeading = New-ReleaseFixture -Name 'hidden-added-heading.md' -Content (
        "# Changelog`n`n## [Unreleased]`n`n## [$version] - $releaseDate`n`n<!-- hidden`n### Added`n-->`n`n- Visible release note.`n")
    Assert-ReleaseRejects 'hidden Added heading cannot satisfy finalized structure' (Invoke-ReleaseValidation -ChangelogPath $hiddenAddedHeading -RequireFinalized -FirstRelease) 'First-release notes must contain an Added section.'

    Write-Output 'PASS: target headings stay in release-state checks; code and comments do not satisfy visible-note requirements; visible controls pass.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
