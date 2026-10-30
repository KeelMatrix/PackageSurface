param(
    [string] $ResultsDirectory,
    [switch] $RunNegativeControls
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($ResultsDirectory)) {
    $ResultsDirectory = Join-Path $root 'artifacts/test-results'
}
$ResultsDirectory = [IO.Path]::GetFullPath($ResultsDirectory)
New-Item -ItemType Directory -Force -Path $ResultsDirectory | Out-Null

$projects = @(
    @{
        Name = 'KeelMatrix.PackageSurface.Cli.Tests'
        Project = Join-Path $root 'tests/KeelMatrix.PackageSurface.Cli.Tests/KeelMatrix.PackageSurface.Cli.Tests.csproj'
        MinimumCount = 52
    },
    @{
        Name = 'KeelMatrix.PackageSurface.Probe.Tests'
        Project = Join-Path $root 'tests/KeelMatrix.PackageSurface.Probe.Tests/KeelMatrix.PackageSurface.Probe.Tests.csproj'
        MinimumCount = 14
    }
)

function Invoke-DiscoveredTestProject {
    param(
        [Parameter(Mandatory)] [string] $Name,
        [Parameter(Mandatory)] [string] $Project,
        [Parameter(Mandatory)] [int] $MinimumCount,
        [Parameter(Mandatory)] [string] $OutputDirectory,
        [switch] $BuildProject
    )

    New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
    $resultName = "$Name.trx"
    if ($BuildProject) {
        $output = @(& dotnet test $Project --configuration Release --no-restore --results-directory $OutputDirectory --logger "console;verbosity=normal" --logger "trx;LogFileName=$resultName" 2>&1)
    }
    else {
        $output = @(& dotnet test $Project --configuration Release --no-build --no-restore --results-directory $OutputDirectory --logger "console;verbosity=normal" --logger "trx;LogFileName=$resultName" 2>&1)
    }
    $exitCode = $LASTEXITCODE
    $outputText = ($output | ForEach-Object { [string]$_ }) -join [Environment]::NewLine
    if (-not [string]::IsNullOrEmpty($outputText)) { Write-Host $outputText }
    if ($exitCode -ne 0) {
        throw "Discovered test project '$Name' failed with exit code $exitCode.`n$outputText"
    }

    $resultPath = Join-Path $OutputDirectory $resultName
    if (-not (Test-Path -LiteralPath $resultPath -PathType Leaf)) {
        throw "Discovered test project '$Name' did not produce a TRX result file."
    }

    [xml] $result = [IO.File]::ReadAllText($resultPath)
    $counters = $result.TestRun.ResultSummary.Counters
    $total = [int]$counters.total
    $executed = [int]$counters.executed
    $failed = [int]$counters.failed
    if ($total -lt $MinimumCount -or $executed -lt $MinimumCount) {
        throw "Discovered test project '$Name' ran $executed of $total tests; at least $MinimumCount are required."
    }
    if ($failed -ne 0) {
        throw "Discovered test project '$Name' reported $failed failed tests."
    }

    Write-Host "TEST_COUNT: $Name discovered=$total executed=$executed failed=$failed"
    return [pscustomobject]@{ Name = $Name; Total = $total; Executed = $executed; Failed = $failed; ResultPath = $resultPath }
}

foreach ($project in $projects) {
    Invoke-DiscoveredTestProject -Name $project.Name -Project $project.Project -MinimumCount $project.MinimumCount -OutputDirectory (Join-Path $ResultsDirectory $project.Name) | Out-Null
}

if ($RunNegativeControls) {
    $scratch = Join-Path ([IO.Path]::GetTempPath()) ('packagesurface-test-discovery-' + [Guid]::NewGuid().ToString('N'))
    $nugetConfig = Join-Path $root 'NuGet.config'
    New-Item -ItemType Directory -Path $scratch | Out-Null

    function New-ControlProject {
        param(
            [Parameter(Mandatory)] [string] $Name,
            [Parameter(Mandatory)] [AllowEmptyString()] [string] $Source
        )

        $directory = Join-Path $scratch $Name
        New-Item -ItemType Directory -Path $directory | Out-Null
        $project = Join-Path $directory "$Name.csproj"
        $projectText = @'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <IsTestProject>true</IsTestProject>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.10.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="4.0.0" />
  </ItemGroup>
</Project>
'@
        [IO.File]::WriteAllText($project, $projectText, [Text.UTF8Encoding]::new($false))
        if (-not [string]::IsNullOrEmpty($Source)) {
            [IO.File]::WriteAllText((Join-Path $directory 'ControlTest.cs'), $Source, [Text.UTF8Encoding]::new($false))
        }

        $restoreOutput = @(& dotnet restore $project --configfile $nugetConfig 2>&1)
        $restoreExitCode = $LASTEXITCODE
        $restoreOutput | ForEach-Object { Write-Output ([string]$_) }
        if ($restoreExitCode -ne 0) {
            throw "Test-discovery negative-control restore failed for '$Name' with exit code $restoreExitCode."
        }

        return [pscustomobject]@{ Name = $Name; Project = $project; Directory = $directory }
    }

    try {
        $empty = New-ControlProject -Name 'EmptyTests' -Source ''
        $emptyRejected = $false
        try {
            Invoke-DiscoveredTestProject -Name $empty.Name -Project $empty.Project -MinimumCount 1 -OutputDirectory (Join-Path $empty.Directory 'TestResults') -BuildProject | Out-Null
        }
        catch {
            $message = $_.Exception.Message
            $emptyRejected = $message.Contains('at least 1 are required', [StringComparison]::Ordinal) -or
                $message.Contains('did not produce a TRX result file', [StringComparison]::Ordinal) -or
                $message.Contains('No test is available', [StringComparison]::OrdinalIgnoreCase) -or
                $message.Contains('no test', [StringComparison]::OrdinalIgnoreCase)
            if (-not $emptyRejected) { throw }
        }
        if (-not $emptyRejected) {
            throw 'The missing-test negative control passed without discovering a test.'
        }
        Write-Output 'NEGATIVE_CONTROL: a project with no tests was rejected.'

        $failingSource = @'
using Xunit;

public sealed class DeliberatelyFailingTest
{
    [Fact]
    public void FailureIsReportedByScenarioName() => Assert.Fail("deliberate negative-control assertion");
}
'@
        $failing = New-ControlProject -Name 'FailingTests' -Source $failingSource
        $failureRejected = $false
        try {
            Invoke-DiscoveredTestProject -Name $failing.Name -Project $failing.Project -MinimumCount 1 -OutputDirectory (Join-Path $failing.Directory 'TestResults') -BuildProject | Out-Null
        }
        catch {
            $message = $_.Exception.Message
            $failureRejected = $message.Contains('DeliberatelyFailingTest.FailureIsReportedByScenarioName', [StringComparison]::Ordinal) -and
                $message.Contains('deliberate negative-control assertion', [StringComparison]::Ordinal)
            if (-not $failureRejected) { throw }
        }
        if (-not $failureRejected) {
            throw 'The failing-test negative control passed despite its deliberate assertion failure.'
        }
        Write-Output 'NEGATIVE_CONTROL: an assertion failure was rejected by the test gate.'
    }
    finally {
        if (Test-Path -LiteralPath $scratch) {
            Remove-Item -LiteralPath $scratch -Recurse -Force
        }
    }
}
