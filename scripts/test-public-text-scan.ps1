param(
    [switch] $SelfTest,
    [string] $TermFile,
    [switch] $ScanTrackedFiles,
    [string[]] $ArchivePath = @(),
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$scanner = Join-Path $PSScriptRoot 'PublicTextScan.ps1'
if (-not (Test-Path -LiteralPath $scanner -PathType Leaf)) {
    throw 'The text scanner implementation is missing.'
}
. $scanner

function Assert-Contains([string] $Name, [string] $Source, [string] $Term) {
    if (-not (Test-PublicTextForTerm -Text $Source -Term $Term)) {
        throw "Representation regression failed: $Name was not recognized."
    }
}

if ($SelfTest) {
    $sample = 'sampletoken'
    $codes = @([char[]]$sample | ForEach-Object { [int]$_ })
    $decimalSource = 'codes(' + ($codes -join ',') + ')'
    $hexSource = 'codes(' + (($codes | ForEach-Object { '0x{0:X2}' -f $_ }) -join ',') + ')'
    $decimalCastSource = (@($codes | ForEach-Object { '[char]{0}' -f $_ }) -join '+')
    $hexCastSource = (@($codes | ForEach-Object { '[char]0x{0:X2}' -f $_ }) -join '+')
    $unicodeSource = -join @([char[]]$sample | ForEach-Object { '\u{0:X4}' -f [int]$_ })
    $hexEscapeSource = -join @([char[]]$sample | ForEach-Object { '\x{0:X2}' -f [int]$_ })
    $htmlDecimalSource = -join @([char[]]$sample | ForEach-Object { '&#{0};' -f [int]$_ })
    $htmlHexSource = -join @([char[]]$sample | ForEach-Object { '&#x{0:X};' -f [int]$_ })
    $base64Source = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($sample))
    $unpaddedBase64Source = $base64Source.TrimEnd('=')
    $utf16Base64Source = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($sample))
    $plusSource = "'{0}' + '{1}'" -f $sample.Substring(0, 6), $sample.Substring(6)
    $joinSource = "-join @('{0}','{1}')" -f $sample.Substring(0, 6), $sample.Substring(6)
    $concatSource = 'string.Concat("{0}","{1}")' -f $sample.Substring(0, 6), $sample.Substring(6)

    Assert-Contains 'decimal character codes' $decimalSource $sample
    Assert-Contains 'hexadecimal character codes' $hexSource $sample
    Assert-Contains 'decimal character casts' $decimalCastSource $sample
    Assert-Contains 'hexadecimal character casts' $hexCastSource $sample
    Assert-Contains 'Unicode escapes' $unicodeSource $sample
    Assert-Contains 'hexadecimal escapes' $hexEscapeSource $sample
    Assert-Contains 'decimal character references' $htmlDecimalSource $sample
    Assert-Contains 'hexadecimal character references' $htmlHexSource $sample
    Assert-Contains 'Base64 text' $base64Source $sample
    Assert-Contains 'unpadded Base64 text' $unpaddedBase64Source $sample
    Assert-Contains 'UTF-16 Base64 text' $utf16Base64Source $sample
    Assert-Contains 'plus-concatenated string fragments' $plusSource $sample
    Assert-Contains 'joined string fragments' $joinSource $sample
    Assert-Contains 'string.Concat fragments' $concatSource $sample
    if (Test-PublicTextForTerm -Text "'sample' + variable" -Term $sample) {
        throw 'The bounded concatenation scan interpreted a dynamic expression.'
    }
    Write-Output 'PUBLIC_TEXT_SCAN_REGRESSIONS=PASS numeric codes, escapes, Base64, and bounded string concatenation.'
}

if ($ScanTrackedFiles -or $ArchivePath.Count -gt 0) {
    if ([string]::IsNullOrWhiteSpace($TermFile)) {
        throw 'A temporary term file is required for content scanning.'
    }
    $terms = @(Get-Content -LiteralPath $TermFile -Raw | ConvertFrom-Json)
    $terms = @($terms | ForEach-Object { [string]$_ } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($terms.Count -eq 0) { throw 'The temporary term file contains no search terms.' }

    $matches = [System.Collections.Generic.List[string]]::new()
    if ($ScanTrackedFiles) {
        $trackedPaths = @(& git -C $RepositoryRoot ls-files)
        if ($LASTEXITCODE -ne 0) { throw 'Unable to enumerate tracked files for the current-tree scan.' }
        foreach ($relativePath in $trackedPaths) {
            $path = Join-Path $RepositoryRoot $relativePath
            if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
                throw "A tracked file is missing: $relativePath"
            }
            $bytes = [IO.File]::ReadAllBytes($path)
            foreach ($text in (Get-PublicTextVariants -Bytes $bytes)) {
                if (Test-PublicTextForAnyTerm -Text $text -Terms $terms) {
                    $matches.Add("tracked file: $relativePath")
                    break
                }
            }
        }
    }

    foreach ($archive in $ArchivePath) {
        $archivePath = (Resolve-Path -LiteralPath $archive).Path
        $zip = [IO.Compression.ZipFile]::OpenRead($archivePath)
        try {
            foreach ($entry in $zip.Entries) {
                $stream = $entry.Open()
                $memory = [IO.MemoryStream]::new()
                try {
                    $stream.CopyTo($memory)
                    $bytes = $memory.ToArray()
                }
                finally {
                    $memory.Dispose()
                    $stream.Dispose()
                }
                foreach ($text in (Get-PublicTextVariants -Bytes $bytes)) {
                    if (Test-PublicTextForAnyTerm -Text $text -Terms $terms) {
                        $matches.Add("archive entry: $([IO.Path]::GetFileName($archivePath))!/$($entry.FullName)")
                        break
                    }
                }
            }
        }
        finally { $zip.Dispose() }
    }

    if ($matches.Count -gt 0) {
        throw "Public text scan found disallowed material:`n$($matches -join "`n")"
    }
    Write-Output "PUBLIC_TEXT_SCAN=PASS tracked_files=$($trackedPaths.Count) archives=$($ArchivePath.Count) terms=$($terms.Count)."
}
