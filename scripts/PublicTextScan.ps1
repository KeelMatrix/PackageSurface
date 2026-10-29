# Bounded text expansion for static source and package inspection. This does not run or
# interpret candidate code. It handles Unicode/hex/percent escapes, numeric character
# lists and casts, UTF-8/UTF-16LE/UTF-16BE standard or URL-safe Base64 text, and quoted
# string literals joined with +, -join, or string.Concat. Dynamic programs and arbitrary
# codecs are out of scope.

function Convert-PublicTextEscapes([string] $Text) {
    $pattern = '\\u(?<unicode>[0-9A-Fa-f]{4})|\\U(?<unicode32>[0-9A-Fa-f]{8})|\\x(?<hex>[0-9A-Fa-f]{2,4})|&#(?<decimal>[0-9]{1,7});|&#x(?<htmlhex>[0-9A-Fa-f]{1,6});|%(?<percent>[0-9A-Fa-f]{2})'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        $group = @('unicode', 'unicode32', 'hex', 'decimal', 'htmlhex', 'percent') | Where-Object { $match.Groups[$_].Success } | Select-Object -First 1
        $raw = $match.Groups[$group].Value
        try {
            $number = if ($group -eq 'decimal') { [int]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture) } else { [Convert]::ToInt32($raw, 16) }
            if ($number -lt 0 -or $number -gt 0x10FFFF -or ($number -ge 0xD800 -and $number -le 0xDFFF)) { return $match.Value }
            return [char]::ConvertFromUtf32($number)
        }
        catch { return $match.Value }
    }
    return [regex]::Replace($Text, $pattern, $evaluator)
}

function Convert-PublicTextNumericLists([string] $Text) {
    $number = '(?:0[xX][0-9A-Fa-f]{1,6}|[0-9]{1,7})'
    $pattern = "(?<![A-Za-z0-9])(?<run>$number(?:\s*,\s*$number){3,})(?![A-Za-z0-9])"
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        $builder = [Text.StringBuilder]::new()
        foreach ($part in $match.Groups['run'].Value.Split(',')) {
            $part = $part.Trim()
            try {
                $value = if ($part -match '^0[xX]') { [Convert]::ToInt32($part.Substring(2), 16) } else { [int]::Parse($part, [Globalization.CultureInfo]::InvariantCulture) }
                if ($value -lt 0 -or $value -gt 0x10FFFF -or ($value -ge 0xD800 -and $value -le 0xDFFF)) { return $match.Value }
                [void]$builder.Append([char]::ConvertFromUtf32($value))
            }
            catch { return $match.Value }
        }
        return $builder.ToString()
    }
    $result = [regex]::Replace($Text, $pattern, $evaluator)
    $castPattern = '(?i)(?:(?:\[(?:System\.)?char\]|\((?:System\.)?char\))\s*(?<code>0[xX][0-9A-F]{1,6}|[0-9]{1,7})\s*(?:[+,]\s*)?){4,}'
    $castEvaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        $builder = [Text.StringBuilder]::new()
        foreach ($code in [regex]::Matches($match.Value, '(?i)(?:\[(?:System\.)?char\]|\((?:System\.)?char\))\s*(?<value>0[xX][0-9A-F]{1,6}|[0-9]{1,7})')) {
            $raw = $code.Groups['value'].Value
            try {
                $value = if ($raw -match '^0[xX]') { [Convert]::ToInt32($raw.Substring(2), 16) } else { [int]::Parse($raw, [Globalization.CultureInfo]::InvariantCulture) }
                if ($value -lt 0 -or $value -gt 0x10FFFF -or ($value -ge 0xD800 -and $value -le 0xDFFF)) { return $match.Value }
                [void]$builder.Append([char]::ConvertFromUtf32($value))
            }
            catch { return $match.Value }
        }
        return $builder.ToString()
    }
    return [regex]::Replace($result, $castPattern, $castEvaluator)
}

function Test-PublicTextIsReadable([string] $Text) {
    if ($Text.Length -lt 3) { return $false }
    return -not [regex]::IsMatch($Text, '[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]')
}

function Get-PublicTextAsciiScore([string] $Text) {
    if (-not (Test-PublicTextIsReadable $Text)) { return -1.0 }
    $printableAsciiCount = [regex]::Matches($Text, '[\x20-\x7E]').Count
    return [double]$printableAsciiCount / $Text.Length
}

function Convert-PublicTextBase64([string] $Text) {
    $pattern = '(?<![-A-Za-z0-9+/_])(?<value>[-A-Za-z0-9+/_]{8,}={0,2})(?![-A-Za-z0-9+/_=])'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        $value = $match.Groups['value'].Value.Replace('-', '+').Replace('_', '/')
        switch ($value.Length % 4) {
            1 { return $match.Value }
            2 { $value += '==' }
            3 { $value += '=' }
        }
        try {
            $bytes = [Convert]::FromBase64String($value)
            $encodings = @(
                [Text.UTF8Encoding]::new($false, $true),
                [Text.UnicodeEncoding]::new($false, $false, $true),
                [Text.UnicodeEncoding]::new($true, $false, $true)
            )
            # A wrong-endian decode can still be printable Unicode; score every supported decoder.
            $bestDecoded = $null
            $bestScore = -1.0
            foreach ($encoding in $encodings) {
                try { $decoded = $encoding.GetString($bytes) }
                catch { continue }
                $score = Get-PublicTextAsciiScore $decoded
                if ($score -gt $bestScore) {
                    $bestDecoded = $decoded
                    $bestScore = $score
                }
            }
            if ($null -ne $bestDecoded) { return $bestDecoded }
        }
        catch { }
        return $match.Value
    }
    return [regex]::Replace($Text, $pattern, $evaluator)
}

function Get-PublicQuotedLiteralPattern {
    $single = "'(?:''|[^'\r\n]){0,2048}'"
    $double = '"(?:\\.|[^"\\\r\n]){0,2048}"'
    return "(?:$single|$double)"
}

function Convert-PublicQuotedLiteral([string] $Literal) {
    if ($Literal.Length -lt 2) { return $Literal }
    $value = $Literal.Substring(1, $Literal.Length - 2)
    if ($Literal[0] -eq [char]39) { return $value.Replace("''", "'") }
    $slash = [string][char]92
    return $value.Replace(($slash + '"'), '"').Replace(($slash + $slash), $slash)
}

function Join-PublicQuotedLiterals([string] $Body) {
    $literalPattern = Get-PublicQuotedLiteralPattern
    $literalMatches = [regex]::Matches($Body, $literalPattern)
    if ($literalMatches.Count -lt 2) { return $Body }
    $remainder = [regex]::Replace($Body, $literalPattern, '')
    $remainder = [regex]::Replace($remainder, '[+,\s]', '')
    if ($remainder.Length -ne 0) { return $Body }
    $builder = [Text.StringBuilder]::new()
    foreach ($match in $literalMatches) { [void]$builder.Append((Convert-PublicQuotedLiteral $match.Value)) }
    return $builder.ToString()
}

function Convert-PublicTextConcatenations([string] $Text) {
    $literal = Get-PublicQuotedLiteralPattern
    $plusPattern = $literal + '(?:\s*\+\s*' + $literal + ')+'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        return Join-PublicQuotedLiterals $match.Value
    }
    $result = [regex]::Replace($Text, $plusPattern, $evaluator)

    $joinPattern = '(?is)-join\s+@\((?<body>[^)]{1,8192})\)'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        return Join-PublicQuotedLiterals $match.Groups['body'].Value
    }
    $result = [regex]::Replace($result, $joinPattern, $evaluator)

    $concatPattern = '(?is)(?:string|String)\.Concat\((?<body>[^)]{1,8192})\)'
    $evaluator = [System.Text.RegularExpressions.MatchEvaluator] {
        param([System.Text.RegularExpressions.Match] $match)
        return Join-PublicQuotedLiterals $match.Groups['body'].Value
    }
    return [regex]::Replace($result, $concatPattern, $evaluator)
}

function Get-PublicTextCandidates([string] $Text) {
    if ($Text.Length -gt 8388608) { throw 'Text scan input exceeds the 8 MiB per-item bound.' }
    $queue = [System.Collections.Generic.Queue[string]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $queue.Enqueue($Text)
    [void]$seen.Add($Text)
    $processed = 0
    while ($queue.Count -gt 0 -and $processed -lt 128 -and $seen.Count -lt 64) {
        $current = $queue.Dequeue()
        $processed++
        $derived = @(
            (Convert-PublicTextEscapes $current),
            (Convert-PublicTextNumericLists $current),
            (Convert-PublicTextBase64 $current),
            (Convert-PublicTextConcatenations $current)
        )
        foreach ($candidate in $derived) {
            if (-not [string]::IsNullOrEmpty($candidate) -and $candidate.Length -le 8388608 -and $seen.Add($candidate)) {
                $queue.Enqueue($candidate)
            }
        }
    }
    if ($queue.Count -gt 0) { throw 'The text scan exceeded its bounded representation-expansion budget.' }
    return ,@($seen)
}

function Test-PublicTextForTerm([string] $Text, [string] $Term) {
    foreach ($candidate in (Get-PublicTextCandidates $Text)) {
        if ($candidate.IndexOf($Term, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    }
    return $false
}

function Test-PublicTextForAnyTerm([string] $Text, [string[]] $Terms) {
    foreach ($candidate in (Get-PublicTextCandidates $Text)) {
        foreach ($term in $Terms) {
            if ($candidate.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
        }
    }
    return $false
}

function Get-PublicTextVariants([byte[]] $Bytes) {
    $variants = [System.Collections.Generic.List[string]]::new()
    $utf8 = [Text.Encoding]::UTF8.GetString($Bytes)
    $variants.Add($utf8)
    $utf16le = [Text.Encoding]::Unicode.GetString($Bytes)
    $utf16be = [Text.Encoding]::BigEndianUnicode.GetString($Bytes)
    foreach ($text in @($utf16le, $utf16be)) {
        $runs = [regex]::Matches($text, '[^\x00-\x1F\x7F]{4,}') | ForEach-Object Value
        if ($runs.Count -gt 0) { $variants.Add(($runs -join "`n")) }
    }
    $ascii = [Text.Encoding]::ASCII.GetString($Bytes)
    $asciiRuns = [regex]::Matches($ascii, '[\x20-\x7E]{4,}') | ForEach-Object Value
    if ($asciiRuns.Count -gt 0) { $variants.Add(($asciiRuns -join "`n")) }
    return ,@($variants)
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
