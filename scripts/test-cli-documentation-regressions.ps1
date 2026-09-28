param(
    [string] $RepositoryRoot
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '../build/Invoke-NestedPwsh.ps1')
$root = if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
}
else {
    (Resolve-Path -LiteralPath $RepositoryRoot).Path
}

$validator = Join-Path $root 'scripts/test-cli-documentation.ps1'
$builtHelpPath = Join-Path $root 'src/KeelMatrix.PackageSurface.Cli/bin/Release/net8.0/KeelMatrix.PackageSurface.dll'
if (-not (Test-Path -LiteralPath $builtHelpPath -PathType Leaf)) {
    throw "Built CLI help assembly '$builtHelpPath' was not found. Build the Release CLI before running documentation regressions."
}

$scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-cli-documentation-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $scratch -Force | Out-Null

function Write-Utf8([string] $Path, [string] $Text) {
    [IO.File]::WriteAllText($Path, $Text, [Text.UTF8Encoding]::new($false))
}

function Invoke-ExpectedValidatorFailure([string] $Name, [string] $TestRoot, [string] $HelpTextPath) {
    $output = @(Invoke-NestedPwsh -NoLogo -NoProfile -File $validator -RepositoryRoot $TestRoot -BuiltHelpTextPath $HelpTextPath 2>&1)
    $exitCode = $LASTEXITCODE
    Write-Output "NEGATIVE_CONTROL=$Name"
    Write-Output 'RAW_RESULT_BEGIN'
    Write-Output "COMMAND_EXIT_CODE=$exitCode"
    $output | ForEach-Object { Write-Output ([string]$_) }
    Write-Output 'RAW_RESULT_END'
    if ($exitCode -eq 0) {
        throw "Documentation validator unexpectedly accepted negative control '$Name'."
    }
    return ($output -join [Environment]::NewLine)
}

try {
    $goodHelpPath = Join-Path $scratch 'help-complete.txt'
    $goodHelp = @(& dotnet $builtHelpPath --help 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Built CLI --help invocation failed with exit code $LASTEXITCODE." }
    $goodHelpText = $goodHelp -join [Environment]::NewLine
    Write-Utf8 $goodHelpPath $goodHelpText

    $brokenHelpPath = Join-Path $scratch 'help-missing-tfm-rid.txt'
    $brokenHelpText = $goodHelpText.Replace('every TFM/RID, ', '')
    if ($brokenHelpText -eq $goodHelpText) { throw 'The built-help negative control did not remove its required clause.' }
    Write-Utf8 $brokenHelpPath $brokenHelpText
    $helpFailure = Invoke-ExpectedValidatorFailure 'built-help-missing-clause' $root $brokenHelpPath
    if ($helpFailure -notmatch 'TFM and RID coverage') { throw 'The built-help negative control did not report the missing clause class.' }

    $markdownRoot = Join-Path $scratch 'markdown-missing-clause'
    $relativeFiles = @(
        'README.md',
        'CHANGELOG.md',
        'SECURITY.md',
        'docs/DEV.md',
        'src/KeelMatrix.PackageSurface.Cli/README.md',
        'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs'
    )
    foreach ($relativeFile in $relativeFiles) {
        $destination = Join-Path $markdownRoot $relativeFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relativeFile) -Destination $destination
    }
    $developerDocs = Join-Path $markdownRoot 'docs/DEV.md'
    $developerText = (Get-Content -Raw $developerDocs).Replace('every TFM/RID, ', '')
    if ($developerText -eq (Get-Content -Raw $developerDocs)) { throw 'The Markdown negative control did not remove its required clause.' }
    Write-Utf8 $developerDocs $developerText
    $markdownFailure = Invoke-ExpectedValidatorFailure 'markdown-missing-clause' $markdownRoot $goodHelpPath
    if ($markdownFailure -notmatch 'TFM and RID coverage') { throw 'The Markdown negative control did not report the missing clause class.' }

    Write-Output 'CLI_DOCUMENTATION_REGRESSIONS=PASS built-help and Markdown omissions are rejected.'
}
finally {
    if (Test-Path -LiteralPath $scratch) {
        Remove-Item -LiteralPath $scratch -Recurse -Force
    }
}
