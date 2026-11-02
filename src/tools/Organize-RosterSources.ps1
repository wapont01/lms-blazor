[CmdletBinding()]
param(
    [string] $RepositoryRoot,
    [switch] $Apply,
    [switch] $VerifyOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
}
if ($Apply -and $VerifyOnly) {
    throw 'Choose either -Apply or -VerifyOnly, not both.'
}

$repository = [IO.Path]::GetFullPath($RepositoryRoot)
$docsRoot = Join-Path $repository 'src\docs'
$rosterRoot = Join-Path $repository 'src\data\rosters'
$catalogPath = Join-Path $rosterRoot 'source-catalog.csv'
$catalog = @(Import-Csv -LiteralPath $catalogPath)
if ($catalog.Count -eq 0) {
    throw "The roster source catalog is empty: $catalogPath"
}

function Resolve-ChildPath([string] $Root, [string] $RelativePath) {
    if ([string]::IsNullOrWhiteSpace($RelativePath) -or [IO.Path]::IsPathRooted($RelativePath)) {
        throw "Expected a relative source path: $RelativePath"
    }
    $segments = $RelativePath -split '[\\/]'
    if ($segments -contains '..' -or $segments -contains '.') {
        throw "Relative traversal is not allowed: $RelativePath"
    }
    $path = [IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    if (-not $path.StartsWith($Root.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside its source root: $RelativePath"
    }
    return $path
}

function Read-Hash([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash
}

$seenSources = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenDestinations = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$jurisdictions = [Collections.Generic.Dictionary[string, string]]::new([StringComparer]::OrdinalIgnoreCase)
$plan = @(
    foreach ($row in $catalog) {
        foreach ($column in @('Jurisdiction', 'LegacyRelativePath', 'CaptureDate', 'Role', 'OfficialSourceUrl', 'Coverage')) {
            if (-not ($row.PSObject.Properties.Name -contains $column)) {
                throw "Missing source catalog column: $column"
            }
        }
        $slug = ($row.Jurisdiction.ToLowerInvariant() -replace '[^a-z0-9]+', '-').Trim('-')
        if ([string]::IsNullOrWhiteSpace($slug)) {
            throw 'A jurisdiction name is required for every source.'
        }
        if ($jurisdictions.ContainsKey($slug) -and $jurisdictions[$slug] -cne $row.Jurisdiction) {
            throw "Different jurisdiction names map to the same folder: $($row.Jurisdiction)"
        }
        $jurisdictions[$slug] = $row.Jurisdiction
        if ($row.Role -notin @('raw', 'prepared')) {
            throw "Unsupported source role '$($row.Role)': $($row.LegacyRelativePath)"
        }
        $captureDate = [DateTime]::MinValue
        if ($row.CaptureDate -ne 'undated' -and -not [DateTime]::TryParseExact(
            $row.CaptureDate, 'yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture,
            [Globalization.DateTimeStyles]::None, [ref] $captureDate)) {
            throw "Unsupported capture date '$($row.CaptureDate)': $($row.LegacyRelativePath)"
        }
        if ($row.OfficialSourceUrl) {
            $url = $null
            if (-not [Uri]::TryCreate($row.OfficialSourceUrl, [UriKind]::Absolute, [ref] $url) -or
                $url.Scheme -notin @('http', 'https')) {
                throw "Invalid official source URL: $($row.OfficialSourceUrl)"
            }
        }
        $source = Resolve-ChildPath $docsRoot $row.LegacyRelativePath
        if (-not $seenSources.Add($source)) {
            throw "Duplicate catalog source: $($row.LegacyRelativePath)"
        }
        if (-not [IO.File]::Exists($source)) {
            throw "Catalog source does not exist: $source"
        }
        $sourceParts = $row.LegacyRelativePath -split '[\\/]'
        if ($sourceParts.Length -lt 2) {
            throw "Expected a jurisdiction/source folder: $($row.LegacyRelativePath)"
        }
        $capture = Join-Path $slug $row.CaptureDate
        $relative = Join-Path $row.Role ($sourceParts[1..($sourceParts.Length - 1)] -join '\')
        $captureRoot = Resolve-ChildPath $rosterRoot $capture
        $destination = Resolve-ChildPath $captureRoot $relative
        if (-not $seenDestinations.Add($destination)) {
            throw "Two source files map to the same destination: $destination"
        }
        $hash = Read-Hash $source
        $exists = [IO.File]::Exists($destination)
        if ($exists -and (Read-Hash $destination) -ne $hash) {
            throw "Canonical source differs; existing files are never overwritten: $destination"
        }
        if ($VerifyOnly -and -not $exists) {
            throw "Canonical source is missing: $destination"
        }
        [pscustomobject] @{
            Jurisdiction = $row.Jurisdiction
            CaptureDate = $row.CaptureDate
            CaptureRoot = $captureRoot
            Source = $source
            Destination = $destination
            Exists = $exists
            RelativePath = $relative
            LegacyRelativePath = $row.LegacyRelativePath
            Role = $row.Role
            OfficialSourceUrl = $row.OfficialSourceUrl
            Coverage = $row.Coverage
            Sha256 = $hash
            Bytes = (Get-Item -LiteralPath $source).Length
        }
    }
)

$manifests = @(
    foreach ($group in ($plan | Group-Object CaptureRoot)) {
        $first = $group.Group[0]
        $manifestPath = Join-Path $group.Name 'manifest.json'
        $files = @(
            foreach ($entry in ($group.Group | Sort-Object RelativePath)) {
                [ordered] @{
                    Path = $entry.RelativePath
                    Role = $entry.Role
                    LegacyRelativePath = $entry.LegacyRelativePath
                    Sha256 = $entry.Sha256
                    Bytes = $entry.Bytes
                    OfficialSourceUrl = $entry.OfficialSourceUrl
                    Coverage = $entry.Coverage
                }
            }
        )
        $manifest = [ordered] @{
            SchemaVersion = 1
            Jurisdiction = $first.Jurisdiction
            CaptureDate = $first.CaptureDate
            CaptureDateMeaning = 'Recorded source capture date; undated means not established, never the file-copy date.'
            Approval = 'Storage only. Import approval and license ownership are separate decisions.'
            LegacySourceRoot = 'src\docs'
            Files = $files
        }
        $json = ($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine
        if ([IO.File]::Exists($manifestPath)) {
            $existing = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            if ($existing.SchemaVersion -ne 1) {
                throw "Unsupported existing roster manifest: $manifestPath"
            }
            foreach ($previous in $existing.Files) {
                $matching = @($files | Where-Object { $_.Path -eq $previous.Path })
                if ($matching.Count -ne 1 -or $matching[0].Sha256 -ne $previous.Sha256) {
                    throw "A recorded source was changed or removed; use a new capture instead: $manifestPath / $($previous.Path)"
                }
            }
        }
        if ($VerifyOnly -and (-not [IO.File]::Exists($manifestPath) -or
            [IO.File]::ReadAllText($manifestPath) -cne $json)) {
            throw "Canonical provenance manifest is missing or differs from the catalog: $manifestPath"
        }
        [pscustomobject] @{ Path = $manifestPath; Json = $json }
    }
)

$copied = 0
if ($Apply) {
    foreach ($entry in $plan) {
        if ($entry.Exists) { continue }
        [IO.Directory]::CreateDirectory((Split-Path $entry.Destination -Parent)) | Out-Null
        $temporaryPath = $entry.Destination + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::Copy($entry.Source, $temporaryPath, $false)
            if ((Read-Hash $temporaryPath) -ne $entry.Sha256) {
                throw "Source copy failed SHA-256 verification: $($entry.Source)"
            }
            [IO.File]::Move($temporaryPath, $entry.Destination)
            $copied++
        }
        finally {
            if ([IO.File]::Exists($temporaryPath)) {
                [IO.File]::Delete($temporaryPath)
            }
        }
    }
    foreach ($manifest in $manifests) {
        if ([IO.File]::Exists($manifest.Path) -and [IO.File]::ReadAllText($manifest.Path) -ceq $manifest.Json) {
            continue
        }
        [IO.Directory]::CreateDirectory((Split-Path $manifest.Path -Parent)) | Out-Null
        $temporaryPath = $manifest.Path + '.' + [Guid]::NewGuid().ToString('N') + '.tmp'
        try {
            [IO.File]::WriteAllText($temporaryPath, $manifest.Json, [Text.UTF8Encoding]::new($false))
            if ([IO.File]::Exists($manifest.Path)) {
                [IO.File]::Replace($temporaryPath, $manifest.Path, $null)
            }
            else {
                [IO.File]::Move($temporaryPath, $manifest.Path)
            }
        }
        finally {
            if ([IO.File]::Exists($temporaryPath)) {
                [IO.File]::Delete($temporaryPath)
            }
        }
    }
}

[pscustomobject] @{
    Mode = $(if ($Apply) { 'Apply' } elseif ($VerifyOnly) { 'VerifyOnly' } else { 'Plan' })
    CatalogFiles = $plan.Count
    CaptureFolders = $manifests.Count
    Jurisdictions = @($plan.Jurisdiction | Sort-Object -Unique).Count
    TotalBytes = ($plan | Measure-Object Bytes -Sum).Sum
    AlreadyPresent = @($plan | Where-Object Exists).Count
    Copied = $copied
    SourceFilesMovedOrDeleted = 0
    DatabaseModified = $false
}
