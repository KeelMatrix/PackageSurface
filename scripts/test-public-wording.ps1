param(
    [string] $RepositoryRoot,
    [string] $PackagePath
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    $RepositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

function From-CodePoints([int[]] $Points) {
    return -join ($Points | ForEach-Object { [char]$_ })
}

$wordA = From-CodePoints @(102,111,117,110,100,101,114)
$wordB = From-CodePoints @(111,119,110,101,114)
$wordC = From-CodePoints @(99,111,109,112,97,110,121)
$wordD = From-CodePoints @(111,119,110,101,100)
$wordE = From-CodePoints @(97,117,116,104,111,114,105,122,101,100)
$phrases = @(
    "$wordA-$wordD",
    "$wordA $wordD",
    "$wordB-$wordD",
    "$wordC-$wordD",
    "$wordA-$wordE",
    "$wordA $wordE"
)
$pattern = ($phrases | ForEach-Object { [regex]::Escape($_) }) -join '|'

$tracked = @(& git -C $RepositoryRoot ls-files)
if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) {
    throw 'The tracked public-file list could not be read.'
}

$matches = @(& git -C $RepositoryRoot grep -n -I -i -E $pattern -- @tracked 2>$null)
if ($LASTEXITCODE -eq 0 -and $matches.Count -gt 0) {
    throw 'Disallowed wording was found in tracked public material.'
}
if ($LASTEXITCODE -notin @(0, 1)) {
    throw 'The tracked public wording search failed.'
}

if (-not [string]::IsNullOrWhiteSpace($PackagePath)) {
    $archive = [IO.Compression.ZipFile]::OpenRead((Resolve-Path -LiteralPath $PackagePath).Path)
    try {
        foreach ($entry in $archive.Entries | Where-Object { $_.FullName -match '\.(md|nuspec|xml|txt|json)$' }) {
            $reader = [IO.StreamReader]::new($entry.Open())
            try { $text = $reader.ReadToEnd() } finally { $reader.Dispose() }
            if ([regex]::IsMatch($text, $pattern, [Text.RegularExpressions.RegexOptions]::IgnoreCase)) {
                throw "Disallowed wording was found in package content '$($entry.FullName)'."
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

Write-Output 'PASS: public wording contains no disallowed phrasing.'
