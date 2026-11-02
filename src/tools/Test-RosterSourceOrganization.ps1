[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$organizer = Join-Path $PSScriptRoot 'Organize-RosterSources.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('lms-roster-organization-' + [Guid]::NewGuid().ToString('N'))

function Assert-True([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Assert-Failure([scriptblock] $Action, [string] $Expected) {
    $observed = $false
    try { & $Action | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        $observed = $true
    }
    Assert-True $observed "Expected failure containing '$Expected'."
}

try {
    $legacy = Join-Path $fixture 'src\docs\TEST STATE\XLXS\Roster.csv'
    $storage = Join-Path $fixture 'src\data\rosters'
    $catalog = Join-Path $storage 'source-catalog.csv'
    $canonical = Join-Path $storage 'test-state\2026-10-24\raw\XLXS\Roster.csv'
    $manifest = Join-Path $storage 'test-state\2026-10-24\manifest.json'
    [IO.Directory]::CreateDirectory((Split-Path $legacy -Parent)) | Out-Null
    [IO.Directory]::CreateDirectory($storage) | Out-Null
    [IO.File]::WriteAllBytes($legacy, [byte[]] @(239, 187, 191, 65, 44, 66, 13, 10, 49, 44, 50, 13, 10))
    $originalHash = (Get-FileHash -LiteralPath $legacy -Algorithm SHA256).Hash
    $entry = [pscustomobject] @{
        Jurisdiction = 'Test State'
        LegacyRelativePath = 'TEST STATE\XLXS\Roster.csv'
        CaptureDate = '2026-10-24'
        Role = 'raw'
        OfficialSourceUrl = 'https://example.test/commission'
        Coverage = 'Synthetic test source only'
    }
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation

    $preview = & $organizer -RepositoryRoot $fixture
    Assert-True ($preview.CatalogFiles -eq 1 -and $preview.Copied -eq 0) 'Preview totals differ.'
    Assert-True (-not [IO.File]::Exists($canonical)) 'Preview copied a file.'
    Assert-Failure { & $organizer -RepositoryRoot $fixture -VerifyOnly } 'Canonical source is missing'

    $first = & $organizer -RepositoryRoot $fixture -Apply
    Assert-True ($first.Copied -eq 1) 'Initial apply did not copy exactly one file.'
    Assert-True ((Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash -eq $originalHash) 'Copy changed source bytes.'
    Assert-True ([IO.File]::Exists($legacy)) 'Legacy source was moved.'
    $evidence = Get-Content -LiteralPath $manifest -Raw | ConvertFrom-Json
    Assert-True ($evidence.Files[0].Sha256 -eq $originalHash) 'Manifest lost source hash.'
    Assert-True ($evidence.Files[0].LegacyRelativePath -eq $entry.LegacyRelativePath) 'Manifest lost original path.'
    $manifestHash = (Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash
    $manifestTime = (Get-Item -LiteralPath $manifest).LastWriteTimeUtc
    & $organizer -RepositoryRoot $fixture -VerifyOnly | Out-Null
    $repeat = & $organizer -RepositoryRoot $fixture -Apply
    Assert-True ($repeat.Copied -eq 0 -and $repeat.AlreadyPresent -eq 1) 'Repeat was not idempotent.'
    Assert-True ((Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash -eq $manifestHash) 'Repeat changed manifest bytes.'
    Assert-True ((Get-Item -LiteralPath $manifest).LastWriteTimeUtc -eq $manifestTime) 'Repeat rewrote the manifest.'

    [IO.File]::AppendAllText($canonical, 'tampered')
    Assert-Failure { & $organizer -RepositoryRoot $fixture -VerifyOnly } 'existing files are never overwritten'
    [IO.File]::Copy($legacy, $canonical, $true)
    [IO.File]::AppendAllText($legacy, 'changed')
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'existing files are never overwritten'
    Assert-True ((Get-FileHash -LiteralPath $canonical -Algorithm SHA256).Hash -eq $originalHash) 'Changed source overwrote canonical data.'
    [IO.File]::Delete($canonical)
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'recorded source was changed or removed'
    Assert-True (-not [IO.File]::Exists($canonical)) 'A conflicting manifest allowed a partial copy.'

    $entry.CaptureDate = '2026-02-30'
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Unsupported capture date'
    $entry.CaptureDate = 'undated'
    $entry.LegacyRelativePath = '..\outside.csv'
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Relative traversal is not allowed'
    $entry.LegacyRelativePath = 'TEST STATE\XLXS\missing.csv'
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Catalog source does not exist'
    $entry.LegacyRelativePath = 'TEST STATE\XLXS\Roster.csv'
    @($entry, $entry) | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Duplicate catalog source'
    $entry.Role = 'individuals'
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Unsupported source role'
    $entry.Role = 'raw'
    $entry.OfficialSourceUrl = 'file:///C:/private'
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Invalid official source URL'
    $entry.OfficialSourceUrl = ''
    $entry | Export-Csv -LiteralPath $catalog -NoTypeInformation
    $undated = & $organizer -RepositoryRoot $fixture -Apply
    Assert-True ($undated.Copied -eq 1) 'Undated source with unknown URL did not organize.'
    $alias = [pscustomobject] @{
        Jurisdiction = 'Test-State'
        LegacyRelativePath = 'TEST STATE\XLXS\another.csv'
        CaptureDate = 'undated'
        Role = 'raw'
        OfficialSourceUrl = ''
        Coverage = 'Synthetic'
    }
    @($entry, $alias) | Export-Csv -LiteralPath $catalog -NoTypeInformation
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply } 'Different jurisdiction names map to the same folder'
    Assert-Failure { & $organizer -RepositoryRoot $fixture -Apply -VerifyOnly } 'Choose either'

    'PASS: preview, byte-preserving copy, provenance, verification, idempotence, tamper detection, immutable-source guard, preflight, dates, traversal, missing sources, duplicate sources, roles, URLs, undated sources, jurisdiction collisions and mode validation.'
}
finally {
    if ([IO.Directory]::Exists($fixture)) {
        Remove-Item -LiteralPath $fixture -Recurse -Force
    }
}
