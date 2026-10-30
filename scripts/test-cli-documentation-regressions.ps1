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
    $script:LastValidatorFailureOutput = $output -join [Environment]::NewLine
    Write-Output "NEGATIVE_CONTROL=$Name"
    Write-Output 'RAW_RESULT_BEGIN'
    Write-Output "COMMAND_EXIT_CODE=$exitCode"
    $output | ForEach-Object { Write-Output ([string]$_) }
    Write-Output 'RAW_RESULT_END'
    if ($exitCode -eq 0) { throw "Documentation validator unexpectedly accepted negative control '$Name'." }
}

try {
    $goodHelpPath = Join-Path $scratch 'help-complete.txt'
    $goodHelp = @(& dotnet $builtHelpPath --help 2>&1)
    if ($LASTEXITCODE -ne 0) { throw "Built CLI --help invocation failed with exit code $LASTEXITCODE." }
    $goodHelpText = $goodHelp -join [Environment]::NewLine
    Write-Utf8 $goodHelpPath $goodHelpText

    $brokenHelpPath = Join-Path $scratch 'help-missing-option.txt'
    $brokenHelpText = $goodHelpText.Replace('--strict-content', '')
    if ($brokenHelpText -eq $goodHelpText) { throw 'The built-help negative control did not remove its required option.' }
    Write-Utf8 $brokenHelpPath $brokenHelpText
    Invoke-ExpectedValidatorFailure 'built-help-missing-option' $root $brokenHelpPath
    if ($script:LastValidatorFailureOutput -notmatch '--strict-content') { throw 'The help negative control did not report the missing option.' }

    $markdownRoot = Join-Path $scratch 'markdown-missing-reference-detail'
    $relativeFiles = @(
        'README.md', 'CHANGELOG.md', 'SECURITY.md', 'PRIVACY.md', 'CONTRIBUTING.md',
        'docs/DEV.md', 'docs/restore-and-baseline-safety.md',
        'src/KeelMatrix.PackageSurface.Cli/README.md',
        'src/KeelMatrix.PackageSurface.Cli/CommandLine.cs'
    )
    foreach ($relativeFile in $relativeFiles) {
        $destination = Join-Path $markdownRoot $relativeFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root $relativeFile) -Destination $destination
    }
    $referencePath = Join-Path $markdownRoot 'docs/restore-and-baseline-safety.md'
    $referenceText = (Get-Content -Raw $referencePath).Replace('canonical restore-identity index', 'restore index')
    if ($referenceText -eq (Get-Content -Raw $referencePath)) { throw 'The Markdown negative control did not remove its required reference clause.' }
    Write-Utf8 $referencePath $referenceText
    Invoke-ExpectedValidatorFailure 'canonical-reference-missing-restore-rule' $markdownRoot $goodHelpPath
    if ($script:LastValidatorFailureOutput -notmatch 'canonical safety reference') { throw 'The reference negative control did not report the missing safety rule.' }

    Write-Output 'CLI_DOCUMENTATION_REGRESSIONS=PASS built-help option and canonical-reference omissions are rejected.'
}
finally {
    if (Test-Path -LiteralPath $scratch) { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
