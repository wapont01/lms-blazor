[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$docsRoot = Split-Path -Parent $PSScriptRoot
$outputPath = Join-Path $PSScriptRoot 'Real_Estate_Entity_Campaign_Candidates_20261010.csv'
$oregonPortlandOutputPath = Join-Path $PSScriptRoot 'Oregon_Portland_RBN_RBO_Campaign_Candidates_20261010.csv'
$candidates = [System.Collections.Generic.List[object]]::new()

function Add-Candidate {
    param(
        [string] $State,
        [string] $LicenseNumber,
        [string] $EntityName,
        [string] $TradeName,
        [string] $SourceLicenseType,
        [string] $CampaignSegment,
        [string] $SourceStatus,
        [string] $QueryCity,
        [string] $BusinessAddress,
        [string] $BusinessPhone,
        [string] $LicenseExpiryOrRenewal,
        [string] $ParentFirmLicenseNumber,
        [string] $BranchEvidence,
        [string] $SourceFile,
        [string] $OfficialSourceUrl,
        [string] $TrecProfileName = '',
        [string] $TrecProfileStatus = '',
        [string] $TrecProfileExpiration = '',
        [string] $TrecSponsoredSalesAgentCount = '',
        [string] $TrecProfileVerifiedOn = '',
        [string] $CampaignNotes
    )

    $number = ([string]$LicenseNumber).Trim()
    if ([string]::IsNullOrWhiteSpace($number) -or $number -eq '0') { return }
    $candidates.Add([pscustomobject][ordered]@{
        State = $State
        LicenseNumber = $number
        EntityName = $EntityName
        OtherSourceNames = ''
        TradeName = $TradeName
        SourceLicenseType = $SourceLicenseType
        CampaignSegment = $CampaignSegment
        SourceStatus = $SourceStatus
        QueryCity = $QueryCity
        BusinessAddress = $BusinessAddress
        BusinessPhone = $BusinessPhone
        LicenseExpiryOrRenewal = $LicenseExpiryOrRenewal
        ParentFirmLicenseNumber = $ParentFirmLicenseNumber
        BranchEvidence = $BranchEvidence
        TrecProfileName = $TrecProfileName
        TrecProfileStatus = $TrecProfileStatus
        TrecProfileExpiration = $TrecProfileExpiration
        TrecSponsoredSalesAgentCount = $TrecSponsoredSalesAgentCount
        TrecProfileVerifiedOn = $TrecProfileVerifiedOn
        SourceFiles = $SourceFile
        OfficialSourceUrl = $OfficialSourceUrl
        CampaignNotes = $CampaignNotes
        SourceRowCount = 1
    })
}

$iowaRoot = Join-Path $docsRoot 'IOWA\XLXS'
$iowaFiles = @(
    'Iowa_Des_Moines_50309_AK_Real_Estate_Firms_20261010.csv',
    'Iowa_Des_Moines_50309_LZ_Additional_Businesses_20261010.csv',
    'Iowa_Des_Moines_50309_LZ_Page2_Additional_Businesses_20261010.csv'
)
foreach ($file in $iowaFiles) {
    foreach ($row in (Import-Csv -LiteralPath (Join-Path $iowaRoot $file))) {
        if ($row.LicenseStatus -ne 'Active') { continue }
        $parentNumber = ''
        $branchEvidence = ''
        if ($row.LicenseNumber -eq 'F05469001') {
            $parentNumber = 'F05469000'
            $branchEvidence = 'Official detail explicitly links this branch to parent firm F05469000.'
        }
        Add-Candidate -State 'Iowa' -LicenseNumber $row.LicenseNumber -EntityName $row.Name -TradeName '' `
            -SourceLicenseType $row.LicenseType -CampaignSegment 'Iowa real-estate business license' `
            -SourceStatus $row.LicenseStatus -QueryCity $row.QueryCity -BusinessAddress '' -BusinessPhone '' `
            -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber $parentNumber `
            -BranchEvidence $branchEvidence -SourceFile "IOWA/XLXS/$file" `
            -OfficialSourceUrl 'https://ia-plb.my.site.com/LicenseSearchPage' `
            -CampaignNotes 'Captured in active Des Moines ZIP 50309 sample; firm and branch types are retained as published.'
    }
}

$minnesotaRoot = Join-Path $docsRoot 'MINNESOTA\XLXS'
$mnCompanyFile = 'Minnesota_Saint_Paul_Pulse_Real_Estate_Firms_20261010.csv'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $minnesotaRoot $mnCompanyFile))) {
    if ($row.LicenseStatus -ne 'Active') { continue }
    Add-Candidate -State 'Minnesota' -LicenseNumber $row.LicenseNumber -EntityName $row.LegalName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Licensed real-estate company' -SourceStatus $row.LicenseStatus `
        -QueryCity $row.QueryCity -BusinessAddress $row.Address -BusinessPhone '' -LicenseExpiryOrRenewal $row.ExpirationDate `
        -ParentFirmLicenseNumber '' -BranchEvidence '' -SourceFile "MINNESOTA/XLXS/$mnCompanyFile" `
        -OfficialSourceUrl 'https://www.pulseportal.com/Inquiry/searchEntityForProducerInfoSimple.do?method=menuInit&criteriaNextAction=producerInformationSimple&moduleCode=PRDCR_LIC&serviceCode=RQST_CNSMR&accessCode=MN&functionCode=RE' `
        -CampaignNotes 'Active company profile detail-verified; no Primary Broker relationship inferred.'
}
$mnLimitedFile = 'Minnesota_Minneapolis_Pulse_Additional_Limited_Brokers_20261010.csv'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $minnesotaRoot $mnLimitedFile))) {
    Add-Candidate -State 'Minnesota' -LicenseNumber $row.LicenseNumber -EntityName $row.LegalName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Limited Broker profile; confirm business fit before corporate outreach' `
        -SourceStatus $row.LicenseStatus -QueryCity $row.QueryCity -BusinessAddress $row.BusinessLocationAddress `
        -BusinessPhone $row.BusinessPhone -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' `
        -BranchEvidence '' -SourceFile "MINNESOTA/XLXS/$mnLimitedFile" `
        -OfficialSourceUrl 'https://www.pulseportal.com/Inquiry/searchEntityForProducerInfoSimple.do?method=menuInit&criteriaNextAction=producerInformationSimple&moduleCode=PRDCR_LIC&serviceCode=RQST_CNSMR&accessCode=MN&functionCode=RE' `
        -CampaignNotes 'Exact PULSE label retained; this category includes both personal names and LLC/business profile names.'
}
$mnProprietorFile = 'Minnesota_Minneapolis_Pulse_Additional_Active_Individual_Proprietors_20261010.csv'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $minnesotaRoot $mnProprietorFile))) {
    Add-Candidate -State 'Minnesota' -LicenseNumber $row.LicenseNumber -EntityName $row.ProfileName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Individual proprietor profile; separate small-business segment' `
        -SourceStatus $row.LicenseStatus -QueryCity $row.QueryCity -BusinessAddress $row.BusinessLocationAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "MINNESOTA/XLXS/$mnProprietorFile" `
        -OfficialSourceUrl 'https://www.pulseportal.com/Inquiry/searchEntityForProducerInfoSimple.do?method=menuInit&criteriaNextAction=producerInformationSimple&moduleCode=PRDCR_LIC&serviceCode=RQST_CNSMR&accessCode=MN&functionCode=RE' `
        -CampaignNotes 'Supplemental PULSE query label, not one of the six guide categories; query city may differ from business location.'
}

$mississippiRoot = Join-Path $docsRoot 'MISSISSIPPI\LICENSING\XLXS'
$msEntityFiles = @(
    'Mississippi_Jackson_Active_Company_Sample_20261010.csv',
    'Mississippi_Jackson_Additional_Active_Company_Sample_20261010.csv',
    'Mississippi_Jackson_Active_Branch_Office_Sample_20261010.csv'
)
foreach ($file in $msEntityFiles) {
    foreach ($row in (Import-Csv -LiteralPath (Join-Path $mississippiRoot $file))) {
        if ($row.LicenseStatus -ne 'Active') { continue }
        $branchEvidence = ''
        $notes = 'Active official Jackson license profile detail-verified.'
        if ($row.LicenseType -eq 'Real Estate Branch Office') {
            $branchEvidence = 'Official source labels the license Real Estate Branch Office; parent-company license was not confirmed.'
            $notes = 'The active-company lookup did not confirm the parent company; do not infer or attach one.'
        }
        Add-Candidate -State 'Mississippi' -LicenseNumber $row.LicenseNumber -EntityName $row.Name -TradeName '' `
            -SourceLicenseType $row.LicenseType -CampaignSegment 'Licensed real-estate company or branch' `
            -SourceStatus $row.LicenseStatus -QueryCity $row.City -BusinessAddress $row.Address -BusinessPhone $row.WorkPhone `
            -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
            -SourceFile "MISSISSIPPI/LICENSING/XLXS/$file" -OfficialSourceUrl $row.SearchUrl -CampaignNotes $notes
    }
}

$ndRoot = Join-Path $docsRoot 'NORTH DAKOTA\XLXS'
$ndFiles = @(
    'North_Dakota_Bismarck_58501_Real_Estate_Firms_20261010.csv',
    'North_Dakota_Fargo_Real_Estate_Firms_20261010.csv'
)
foreach ($file in $ndFiles) {
    $licenseeFile = $file -replace '_Real_Estate_Firms_', '_Real_Estate_Licensees_'
    $licenseePath = Join-Path $ndRoot $licenseeFile
    $licensees = Import-Csv -LiteralPath $licenseePath
    foreach ($row in (Import-Csv -LiteralPath (Join-Path $ndRoot $file))) {
        $activeLicensees = @($licensees | Where-Object {
            $_.FirmLicenseNumber -eq $row.FirmLicenseNumber -and
            $_.City -eq $row.City -and
            $_.LicenseStatus -eq 'Active'
        })
        $branchEvidence = ''
        $notes = "Captured directory shows $($activeLicensees.Count) active affiliated individual licensee record(s) for this firm in $($row.City); this is not confirmation of firm-license status."
        if ($row.Company -eq 'Bitz Realty - Branch Office') {
            $branchEvidence = 'The official firm source labels this entry Bitz Realty - Branch Office; parent office/license is not identified.'
            $notes += ' The firm source labels this office as a branch; parent office/license is not identified.'
        }
        Add-Candidate -State 'North Dakota' -LicenseNumber $row.FirmLicenseNumber -EntityName $row.Company -TradeName '' `
            -SourceLicenseType 'Firm directory entry' -CampaignSegment 'Firm candidate; verify current status' `
            -SourceStatus 'Not displayed by captured source' -QueryCity $row.City -BusinessAddress '' -BusinessPhone '' `
            -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
            -SourceFile "NORTH DAKOTA/XLXS/$file; NORTH DAKOTA/XLXS/$licenseeFile" -OfficialSourceUrl 'https://services.realestatend.org/directory/' `
            -CampaignNotes $notes
    }
}

$oregonRoot = Join-Path $docsRoot 'OREGON\DERIVED'
$oregonFile = 'oregon_active_businesses_all.csv'
$oregonUrl = 'https://orea.elicense.micropact.com/Lookup/GenerateRoster.aspx'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $oregonRoot $oregonFile))) {
    $address = @($row.address_line_1, $row.address_line_2, $row.city, $row.state, $row.zip_code) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    Add-Candidate -State 'Oregon' -LicenseNumber $row.business_license_number -EntityName $row.business_name -TradeName '' `
        -SourceLicenseType $row.registration_type -CampaignSegment 'Oregon active business-roster entry' `
        -SourceStatus 'Active roster export; row-level status not supplied' -QueryCity $row.city `
        -BusinessAddress ($address -join ', ') -BusinessPhone $row.business_phone -LicenseExpiryOrRenewal $row.expiration_date `
        -ParentFirmLicenseNumber '' -BranchEvidence '' -SourceFile "OREGON/DERIVED/$oregonFile" `
        -OfficialSourceUrl $oregonUrl `
        -CampaignNotes 'Registration type is preserved as published; source export has no branch field, so no branch or parent relationship is inferred.'
}

$sdRoot = Join-Path $docsRoot 'SOUTH DAKOTA\XLXS'
$sdFiles = @(
    'South_Dakota_Real_Estate_City_Sample_Firms_20261010.csv',
    'South_Dakota_Rapid_City_Additional_Firms_20261010.csv'
)
foreach ($file in $sdFiles) {
    foreach ($row in (Import-Csv -LiteralPath (Join-Path $sdRoot $file))) {
        if ([string]::IsNullOrWhiteSpace($row.LicenseNumber) -or $row.LicenseNumber -eq '0') { continue }
        $notes = "Official firm roster row; current license status is not displayed. Published renewal year=$($row.RenewalYear); license-issued value=$($row.LicenseIssued)."
        Add-Candidate -State 'South Dakota' -LicenseNumber $row.LicenseNumber -EntityName $row.FirmName -TradeName '' `
            -SourceLicenseType 'Firm roster entry' -CampaignSegment 'Firm candidate; verify current status' `
            -SourceStatus 'Not displayed by captured source' -QueryCity $row.City -BusinessAddress $row.Address -BusinessPhone $row.Phone `
            -LicenseExpiryOrRenewal $row.RenewalYear -ParentFirmLicenseNumber '' `
            -BranchEvidence 'No branch marker or parent-office relationship is published in the captured firm roster.' `
            -SourceFile "SOUTH DAKOTA/XLXS/$file" -OfficialSourceUrl 'https://sdrec.sd.gov/registration/firmlist.aspx' `
            -CampaignNotes $notes
    }
}

$texasRoot = Join-Path $docsRoot 'TEXAS\LICENSING\XLXS'
$texasFile = 'Texas_Active_Broker_Company_Development_Sample_20261010.csv'
$texasProfileFile = 'Texas_Active_Broker_Company_TREC_Profile_Verification_20261010.csv'
$texasSourceUrl = 'https://data.texas.gov/dataset/Broker-and-Sales-Agent-License-Holder-Information/s7ft-44qi'
$texasProfileUrl = 'https://www.trec.texas.gov/license-search/?detail_id='
$texasProfilesByLicense = @{}
foreach ($profile in (Import-Csv -LiteralPath (Join-Path $texasRoot $texasProfileFile))) {
    $texasProfilesByLicense[$profile.LicenseNumber] = $profile
}
foreach ($row in (Import-Csv -LiteralPath (Join-Path $texasRoot $texasFile))) {
    if ($row.LicenseType -ne 'Broker Company' -or $row.Status -ne 'Active') { continue }
    $profile = $texasProfilesByLicense[$row.LicenseNumber]
    if (-not $profile -or $profile.ProfileStatus -ne 'Active') {
        throw "Missing or non-Active TREC detail verification for Texas license $($row.LicenseNumber)."
    }
    $notes = "Internal research lead only; TREC license-holder profile detail-verified Active on $($profile.VerifiedOn), expiration $($profile.ProfileExpirationDate). The profile displays a sponsored-agent count of $($profile.SponsoredSalesAgentsCount) when supplied; blank means the profile did not display a count, not zero. Snapshot County=$($row.County). TREC says company related-license data identifies an officer (related license $($row.RelatedLicenseNumber), $($row.RelatedLicenseFullName)); this is not branch or parent evidence. TREC public-file instructions say phone, email, and mailing/physical addresses are withheld due to SB 510. Official-source review found no explicit campaign-solicitation authorization; do not outreach or account-match until separately cleared."
    Add-Candidate -State 'Texas' -LicenseNumber $row.LicenseNumber -EntityName $row.FullName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Broker Company; internal firm-campaign research lead only' `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "TEXAS/LICENSING/XLXS/$texasFile; TEXAS/LICENSING/XLXS/$texasProfileFile" `
        -OfficialSourceUrl "$texasSourceUrl; https://www.trec.texas.gov/public/high-value-data-sets; https://www.trec.texas.gov/public/trec-public-information-files-instructions; $texasProfileUrl$($row.LicenseNumber)" `
        -TrecProfileName $profile.ProfileName -TrecProfileStatus $profile.ProfileStatus `
        -TrecProfileExpiration $profile.ProfileExpirationDate `
        -TrecSponsoredSalesAgentCount $profile.SponsoredSalesAgentsCount `
        -TrecProfileVerifiedOn $profile.VerifiedOn `
        -CampaignNotes $notes
}

function Join-UniqueValues {
    param([object[]] $Values)
    $seen = @{}
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($value in $Values) {
        $text = [string]$value
        if ([string]::IsNullOrWhiteSpace($text) -or $seen.ContainsKey($text)) { continue }
        $seen[$text] = $true
        $result.Add($text)
    }
    return $result -join '; '
}

$deduplicated = @(
    foreach ($group in ($candidates | Group-Object { "$($_.State)|$($_.LicenseNumber)|$($_.SourceLicenseType)" })) {
        $items = @($group.Group)
        $first = $items[0]
        $names = @($items | ForEach-Object { $_.EntityName } | Select-Object -Unique)
        [pscustomobject][ordered]@{
            State = $first.State
            LicenseNumber = $first.LicenseNumber
            EntityName = $names[0]
            OtherSourceNames = if ($names.Count -gt 1) { $names[1..($names.Count - 1)] -join '; ' } else { '' }
            TradeName = Join-UniqueValues @($items | ForEach-Object { $_.TradeName })
            SourceLicenseType = $first.SourceLicenseType
            CampaignSegment = Join-UniqueValues @($items | ForEach-Object { $_.CampaignSegment })
            SourceStatus = Join-UniqueValues @($items | ForEach-Object { $_.SourceStatus })
            QueryCity = Join-UniqueValues @($items | ForEach-Object { $_.QueryCity })
            BusinessAddress = Join-UniqueValues @($items | ForEach-Object { $_.BusinessAddress })
            BusinessPhone = Join-UniqueValues @($items | ForEach-Object { $_.BusinessPhone })
            LicenseExpiryOrRenewal = Join-UniqueValues @($items | ForEach-Object { $_.LicenseExpiryOrRenewal })
            ParentFirmLicenseNumber = Join-UniqueValues @($items | ForEach-Object { $_.ParentFirmLicenseNumber })
            BranchEvidence = Join-UniqueValues @($items | ForEach-Object { $_.BranchEvidence })
            TrecProfileName = Join-UniqueValues @($items | ForEach-Object { $_.TrecProfileName })
            TrecProfileStatus = Join-UniqueValues @($items | ForEach-Object { $_.TrecProfileStatus })
            TrecProfileExpiration = Join-UniqueValues @($items | ForEach-Object { $_.TrecProfileExpiration })
            TrecSponsoredSalesAgentCount = Join-UniqueValues @($items | ForEach-Object { $_.TrecSponsoredSalesAgentCount })
            TrecProfileVerifiedOn = Join-UniqueValues @($items | ForEach-Object { $_.TrecProfileVerifiedOn })
            SourceFiles = Join-UniqueValues @($items | ForEach-Object { $_.SourceFiles })
            OfficialSourceUrl = Join-UniqueValues @($items | ForEach-Object { $_.OfficialSourceUrl })
            CampaignNotes = Join-UniqueValues @($items | ForEach-Object { $_.CampaignNotes })
            SourceRowCount = $items.Count
        }
    }
)

$expectedMinimums = @{
    Iowa = 20
    Minnesota = 30
    Mississippi = 21
    'North Dakota' = 15
    Oregon = 4493
    'South Dakota' = 120
    Texas = 20
}
foreach ($state in $expectedMinimums.Keys) {
    $count = @($deduplicated | Where-Object { $_.State -eq $state }).Count
    if ($count -lt $expectedMinimums[$state]) { throw "Unexpectedly low $state candidate count: $count" }
}
$duplicateKeys = @($deduplicated | Group-Object { "$($_.State)|$($_.LicenseNumber)|$($_.SourceLicenseType)" } | Where-Object Count -gt 1)
if ($duplicateKeys.Count) { throw 'Candidate license/type keys are not unique after deduplication.' }

$deduplicated |
    Sort-Object State, CampaignSegment, EntityName, LicenseNumber |
    Export-Csv -LiteralPath $outputPath -NoTypeInformation -Encoding UTF8

Write-Output "Wrote $($deduplicated.Count) deduplicated entity campaign candidates to $outputPath"
$deduplicated | Group-Object State | Sort-Object Name | ForEach-Object {
    Write-Output "$($_.Name): $($_.Count)"
}

$oregonPortlandCandidates = @(
    $deduplicated | Where-Object {
        $_.State -eq 'Oregon' -and
        $_.QueryCity -eq 'Portland' -and
        $_.SourceLicenseType -in @('RBN', 'RBO')
    } | Sort-Object EntityName, LicenseNumber
)
if ($oregonPortlandCandidates.Count -ne 945) {
    throw "Unexpected Portland Oregon RBN/RBO candidate count: $($oregonPortlandCandidates.Count)"
}
$oregonPortlandCandidates |
    Export-Csv -LiteralPath $oregonPortlandOutputPath -NoTypeInformation -Encoding UTF8
Write-Output "Wrote $($oregonPortlandCandidates.Count) Portland Oregon RBN/RBO candidates to $oregonPortlandOutputPath"
