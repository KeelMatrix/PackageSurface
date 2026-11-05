$websiteRepositoryUrl = 'https://github.com/KeelMatrix/PackageSurface'

function Assert-WebsiteObject {
    param([System.Text.Json.JsonElement] $Value, [string[]] $Fields)
    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        throw 'Website metadata must contain JSON objects.'
    }
    $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($property in $Value.EnumerateObject()) {
        if (-not $seen.Add($property.Name) -or $Fields -cnotcontains $property.Name) {
            throw 'Website metadata contains a duplicate or unknown field.'
        }
    }
    if ($seen.Count -ne $Fields.Count) { throw 'Website metadata is missing a required field.' }
}

function Read-WebsiteManifest {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw 'Website classification manifest is missing.' }
    $document = [System.Text.Json.JsonDocument]::Parse([IO.File]::ReadAllText($Path))
    try {
        $rootValue = $document.RootElement
        Assert-WebsiteObject $rootValue @('schemaVersion', 'packages')
        $schema = $rootValue.GetProperty('schemaVersion')
        $version = 0
        if ($schema.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            -not $schema.TryGetInt32([ref]$version) -or $version -ne 1) {
            throw 'Website metadata schemaVersion must be 1.'
        }
        $packages = $rootValue.GetProperty('packages')
        if ($packages.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) { throw 'Website packages must be an object.' }
        $result = [Collections.Generic.Dictionary[string, object]]::new([StringComparer]::OrdinalIgnoreCase)
        foreach ($package in $packages.EnumerateObject()) {
            if ([string]::IsNullOrWhiteSpace($package.Name) -or $result.ContainsKey($package.Name)) {
                throw 'Website metadata contains an empty or duplicate package ID.'
            }
            Assert-WebsiteObject $package.Value @('visibility', 'role')
            $visibility = $package.Value.GetProperty('visibility').GetString()
            $role = $package.Value.GetProperty('role').GetString()
            if ($visibility -cnotin @('public-product', 'internal-package') -or $role -cnotin @('primary', 'component')) {
                throw 'Website metadata contains an invalid visibility or role.'
            }
            $result.Add($package.Name, @{ visibility = $visibility; role = $role })
        }
        return ,$result
    }
    finally { $document.Dispose() }
}

function Assert-WebsitePackageMetadata {
    param([Collections.Generic.Dictionary[string, object]] $Manifest, [System.Collections.IDictionary] $Package)
    $id = [string]$Package.PackageId
    if ([string]::IsNullOrWhiteSpace($id) -or -not $Manifest.ContainsKey($id)) {
        throw "Website manifest entry is missing for package '$id'."
    }
    $tags = @(([string]$Package.PackageTags -split '[;\s]+') | Where-Object { $_.Length -gt 0 })
    $sentinels = @('keelmatrix-public-product', 'keelmatrix-internal-package', 'keelmatrix-primary', 'keelmatrix-component')
    foreach ($tag in $tags) {
        if ($tag.StartsWith('keelmatrix-', [StringComparison]::OrdinalIgnoreCase) -and $sentinels -cnotcontains $tag) {
            throw "Package '$id' contains an unknown or incorrectly cased classification sentinel."
        }
    }
    $visibility = @($tags | Where-Object { $_ -cin @('keelmatrix-public-product', 'keelmatrix-internal-package') })
    $role = @($tags | Where-Object { $_ -cin @('keelmatrix-primary', 'keelmatrix-component') })
    if ($visibility.Count -ne 1 -or $role.Count -ne 1) {
        throw "Package '$id' must have exactly one visibility sentinel and one role sentinel."
    }
    $entry = $Manifest[$id]
    if ($visibility[0] -cne "keelmatrix-$($entry.visibility)" -or $role[0] -cne "keelmatrix-$($entry.role)") {
        throw "Package '$id' classification disagrees with the website manifest."
    }
    if ($Package.PackageProjectUrl -cne $websiteRepositoryUrl -or $Package.RepositoryUrl -cne $websiteRepositoryUrl -or
        $Package.RepositoryType -cne 'git' -or $Package.PublishRepositoryUrl -ine 'true') {
        throw "Package '$id' must use the canonical project and repository URL $websiteRepositoryUrl with published git metadata."
    }
    if ([string]::IsNullOrWhiteSpace($Package.Description) -or $Package.Description -eq 'Package Description' -or
        $Package.Authors -cne 'KeelMatrix' -or [string]::IsNullOrWhiteSpace($Package.PackageReadmeFile) -or
        ([string]::IsNullOrWhiteSpace($Package.PackageLicenseExpression) -and [string]::IsNullOrWhiteSpace($Package.PackageLicenseFile))) {
        throw "Package '$id' is missing required description, author, README, or license metadata."
    }
    if ($Package.PackAsTool -ieq 'true' -and [string]::IsNullOrWhiteSpace($Package.ToolCommandName)) {
        throw "Package '$id' is missing its .NET tool command."
    }
}

function Assert-WebsiteMetadata {
    param([Collections.Generic.Dictionary[string, object]] $Manifest, [System.Collections.IDictionary[]] $Packages)
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($package in $Packages) {
        if (-not $ids.Add([string]$package.PackageId)) { throw 'Multiple shipping projects declare the same package ID.' }
        Assert-WebsitePackageMetadata $Manifest $package
    }
    foreach ($id in $Manifest.Keys) {
        if (-not $ids.Contains($id)) { throw "Website manifest names package '$id' that is not a shipping package in this repository." }
    }
    $entries = @($Manifest.Values)
    $primaries = @($entries | Where-Object role -CEQ 'primary')
    $public = @($entries | Where-Object visibility -CEQ 'public-product')
    $publicPrimaries = @($public | Where-Object role -CEQ 'primary')
    if ($primaries.Count -gt 1 -or ($public.Count -gt 0 -and $publicPrimaries.Count -ne 1)) {
        throw 'Website product family must have exactly one public primary package.'
    }
    if ($Packages.Count -eq 0) { throw 'No shipping packages were found for website metadata validation.' }
}
