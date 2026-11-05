param([string] $RepositoryRoot = (Join-Path $PSScriptRoot '..'))

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'WebsiteMetadata.ps1')
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$manifest = Read-WebsiteManifest (Join-Path $root 'keelmatrix.website.json')
$properties = 'IsPackable,PackageId,Description,PackageTags,Authors,PackageLicenseExpression,PackageLicenseFile,PackageReadmeFile,PackageProjectUrl,RepositoryUrl,RepositoryType,PublishRepositoryUrl,PackAsTool,ToolCommandName'
$packages = @(
    Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object {
        $_.Extension -in @('.csproj', '.fsproj', '.vbproj') -and
        $_.FullName.Substring($root.Length + 1) -notmatch '(^|[\\/])(bin|obj|artifacts|\.phase0|\.git)[\\/]' -and
        # These packages and consumers build only the controlled local test feed.
        $_.FullName.Substring($root.Length + 1) -notmatch '^fixtures[\\/]'
    } | ForEach-Object {
        $output = @(& dotnet msbuild $_.FullName -nologo -property:Configuration=Release "-getProperty:$properties" 2>&1)
        if ($LASTEXITCODE -ne 0) { throw "Cannot evaluate website package metadata for '$($_.Name)': $($output -join [Environment]::NewLine)" }
        $metadata = (($output -join [Environment]::NewLine) | ConvertFrom-Json -AsHashtable).Properties
        if ($metadata.IsPackable -ieq 'true') { $metadata }
    }
)
Assert-WebsiteMetadata $manifest $packages
Write-Output "WEBSITE_METADATA=PASS PACKAGES=$($packages.PackageId -join ',')"
