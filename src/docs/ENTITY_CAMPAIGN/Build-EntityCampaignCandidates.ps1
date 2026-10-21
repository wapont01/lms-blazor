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

$tennesseeRoot = Join-Path $docsRoot 'TENNESSEE\XLXS'
$tennesseeFirmFile = 'Tennessee_Nashville_Active_Firm_Campaign_20261010.csv'
$tennesseeSourceUrl = 'https://search.cloud.commerce.tn.gov/'
$tennesseeFirmRows = @(Import-Csv -LiteralPath (Join-Path $tennesseeRoot $tennesseeFirmFile))
if ($tennesseeFirmRows.Count -ne 20) {
    throw "Unexpected Tennessee firm sample count: $($tennesseeFirmRows.Count)."
}
foreach ($row in $tennesseeFirmRows) {
    if ($row.State -ne 'Tennessee' -or $row.SourceLicenseType -ne 'Real Estate Firm' -or $row.SourceStatus -ne 'Active') {
        throw "Unexpected Tennessee firm campaign row for license $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Tennessee' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Tennessee active real-estate firm; internal research lead only' `
        -SourceStatus $row.SourceStatus -QueryCity $row.QueryCity -BusinessAddress $row.BusinessAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.LicenseExpiryOrRenewal -ParentFirmLicenseNumber '' -BranchEvidence $row.BranchEvidence `
        -SourceFile "TENNESSEE/XLXS/$tennesseeFirmFile" -OfficialSourceUrl $tennesseeSourceUrl `
        -CampaignNotes "Nashville firm search captured 2026-10-10; active firm status and expiration are from official public search result cards. This is the first 20 results, not a representative sample of all 538 active Nashville firms. Result-card data was not detail-page verified except for the first sampled firm. Do not infer branch or parent relationships. Individual broker and affiliate-broker samples are maintained separately. Recheck current status and resolve source-use/outreach requirements before operational use."
}

$connecticutRoot = Join-Path $docsRoot 'CONNECTICUT\LICENSING\XLXS'
$connecticutFirmFile = 'Connecticut_Hartford_Active_Firm_Campaign_Sample_20261011.csv'
$connecticutSourceUrl = 'https://www.elicense.ct.gov/lookup/licenselookup.aspx'
$connecticutFirmRows = @(Import-Csv -LiteralPath (Join-Path $connecticutRoot $connecticutFirmFile))
if ($connecticutFirmRows.Count -ne 22) {
    throw "Unexpected Connecticut firm campaign sample count: $($connecticutFirmRows.Count)."
}
$connecticutLicenseNumbers = @($connecticutFirmRows | Select-Object -ExpandProperty LicenseNumber -Unique)
if ($connecticutLicenseNumbers.Count -ne 22) {
    throw "Connecticut firm campaign license numbers are not unique."
}
foreach ($row in $connecticutFirmRows) {
    if ($row.State -ne 'Connecticut' -or
        $row.SourceLicenseType -ne 'REAL ESTATE BROKER' -or
        $row.SourceStatus -ne 'ACTIVE' -or
        $row.ProfileVerifiedOn -ne '2026-10-11' -or
        $row.QueryTotalRecords -ne '83' -or
        $row.QueryActiveRecords -ne '67') {
        throw "Unexpected Connecticut firm campaign row for license $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Connecticut' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.DBA `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Connecticut active business-name broker license; detail-verified internal research lead' `
        -SourceStatus $row.SourceStatus -QueryCity $row.QueryCity -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' `
        -BranchEvidence 'No branch or parent-firm relationship established in the official profile.' `
        -SourceFile "CONNECTICUT/LICENSING/XLXS/$connecticutFirmFile" -OfficialSourceUrl $connecticutSourceUrl `
        -CampaignNotes "Hartford REAL ESTATE BROKER query returned 83 records (67 ACTIVE, 16 QUALIFIED) across nine pages on 2026-10-11. This purposive 22-profile business-name sample is not representative or exhaustive; each included profile was individually checked for exact license type and ACTIVE status. The broker category also contains individuals. Personal broker and salesperson samples remain separate. No branch or parent-firm relationship, direct contact method, or outreach permission is inferred. Recheck status and resolve source-use/outreach requirements before operational use."
}

$delawareRoot = Join-Path $docsRoot 'DELAWARE\LICENSING\XLXS'
$delawareFirmFile = 'Delaware_Wilmington_Active_Main_Office_Campaign_Sample_20261011.csv'
$delawareSourceUrl = 'https://delpros.delaware.gov/oh_verifylicense'
$delawareFirmRows = @(Import-Csv -LiteralPath (Join-Path $delawareRoot $delawareFirmFile))
if ($delawareFirmRows.Count -ne 20) {
    throw "Unexpected Delaware firm campaign sample count: $($delawareFirmRows.Count)."
}
$delawareLicenseNumbers = @($delawareFirmRows | Select-Object -ExpandProperty LicenseNumber -Unique)
if ($delawareLicenseNumbers.Count -ne 20) {
    throw "Delaware firm campaign license numbers are not unique."
}
foreach ($row in $delawareFirmRows) {
    if ($row.State -ne 'Delaware' -or
        $row.SourceLicenseType -ne 'Main Office Permit' -or
        $row.SourceStatus -ne 'Active' -or
        $row.QueryCity -ne 'Wilmington' -or
        $row.SourceAsOfDate -ne '2026-10-11' -or
        $row.QueryTotalRecords -ne '82' -or
        $row.QueryActiveRecords -ne '42') {
        throw "Unexpected Delaware firm campaign row for license $($row.LicenseNumber)."
    }
    $addressParts = @($row.Address, $row.City, $row.StateAbbreviation, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $businessAddress = $addressParts -join ', '
    Add-Candidate -State 'Delaware' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.DBA `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Delaware active real-estate main office permit; internal research lead only' `
        -SourceStatus $row.SourceStatus -QueryCity $row.QueryCity -BusinessAddress $businessAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Main Office Permit; no parent or branch relationship inferred.' `
        -SourceFile "DELAWARE/LICENSING/XLXS/$delawareFirmFile" -OfficialSourceUrl $delawareSourceUrl `
        -CampaignNotes "Wilmington facility search returned 82 Main Office Permit records (42 Active) on 2026-10-11. This purposive 20-record midpoint-quantile sample is not representative or statewide. Three selected profiles were opened; the remaining 17 were captured from official result cards. Personal Broker and Salesperson samples are maintained separately. Branch Office Permit records are not included; no parent/branch relationship or outreach permission is inferred. Recheck status and source-use requirements before operational use."
}

$newJerseyRoot = Join-Path $docsRoot 'NEW JERSEY\LICENSING\XLXS'
$newJerseyFile = 'New_Jersey_Realty_Active_Company_Branch_Campaign_Sample_20261011.csv'
$newJerseySourceUrl = 'https://www-dobi.nj.gov/DOBI_LicSearch/recSearch.jsp'
$newJerseyRows = @(Import-Csv -LiteralPath (Join-Path $newJerseyRoot $newJerseyFile))
if ($newJerseyRows.Count -ne 22 -or
    @($newJerseyRows | Select-Object -ExpandProperty LicenseNumber -Unique).Count -ne 22 -or
    @($newJerseyRows | Where-Object SourceLicenseType -eq 'REAL ESTATE COMPANY').Count -ne 20 -or
    @($newJerseyRows | Where-Object SourceLicenseType -eq 'BRANCH OFFICE').Count -ne 2) {
    throw 'Unexpected New Jersey company/branch campaign sample count, uniqueness, or type distribution.'
}
foreach ($row in $newJerseyRows) {
    if ($row.State -ne 'New Jersey' -or
        $row.LicenseNumber -notmatch '^\d{7}$' -or
        [string]::IsNullOrWhiteSpace($row.EntityName) -or
        $row.SourceStatus -ne 'ACTIVELY LICENSED' -or
        $row.ProfileVerifiedOn -ne '2026-10-11' -or
        $row.QueryName -ne 'REALTY' -or
        $row.LicenseExpiryOrRenewal -ne '06/30/2027') {
        throw "Unexpected New Jersey campaign row for license $($row.LicenseNumber)."
    }
    $segment = 'New Jersey published real-estate company license; legal form not inferred'
    $branchEvidence = 'No branch or parent-firm relationship established.'
    if ($row.SourceLicenseType -eq 'BRANCH OFFICE') {
        if ($row.QueryTotalRecords -ne '2' -or $row.QueryUniqueLicenses -ne '2' -or
            $row.PublishedEmployerReferenceNumber -notmatch '^\d{7}$') {
            throw "Unexpected New Jersey branch query metadata for license $($row.LicenseNumber)."
        }
        $segment = 'New Jersey real-estate branch office; detail-verified internal research lead'
        $branchEvidence = "Official profile publishes employer $($row.PublishedEmployerName), reference $($row.PublishedEmployerReferenceNumber); no independently verified parent-company relationship."
    }
    elseif ($row.QueryTotalRecords -ne '85' -or $row.QueryUniqueLicenses -ne '78') {
        throw "Unexpected New Jersey company query metadata for license $($row.LicenseNumber)."
    }
    $notes = "Name-filtered REALTY query captured 2026-10-11; 20 company licenses sampled from 78 unique licenses (85 alias rows), plus both matching branch licenses. All included status profiles were opened and exact type/status/expiration checked. This is not a statewide or representative roster. Published search names: $($row.PublishedSearchNames). Published broker of record: $($row.BrokerOfRecordName). Personal SMITH-name Broker of Record, Broker/Salesperson, and Salesperson samples remain separate. Published company category does not establish corporate legal form. Portal changes appear the next day; recheck status and source-use/outreach requirements before operational use."
    Add-Candidate -State 'New Jersey' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $segment -SourceStatus $row.SourceStatus `
        -QueryCity '' -BusinessAddress $row.BusinessAddress -BusinessPhone $row.BusinessPhone `
        -LicenseExpiryOrRenewal $row.LicenseExpiryOrRenewal -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "NEW JERSEY/LICENSING/XLXS/$newJerseyFile" -OfficialSourceUrl $newJerseySourceUrl `
        -CampaignNotes $notes
}

$marylandRoot = Join-Path $docsRoot 'MARYLAND\XLXS'
$marylandFile = 'Maryland_Baltimore_Broker_Linked_Location_Campaign_Sample_20261011.csv'
$marylandRows = @(Import-Csv -LiteralPath (Join-Path $marylandRoot $marylandFile))
$marylandPeople = @(Import-Csv -LiteralPath (Join-Path $marylandRoot 'Maryland_Baltimore_Location_Personal_Licensees_20261011.csv'))
$marylandLocations = @(Import-Csv -LiteralPath (Join-Path $marylandRoot 'Maryland_Baltimore_Business_Location_Crawl_Coverage_20261011.csv'))
if ($marylandRows.Count -ne 20 -or @($marylandRows.LicenseNumber | Sort-Object -Unique).Count -ne 20) {
    throw 'Unexpected Maryland campaign sample count or uniqueness.'
}
foreach ($row in $marylandRows) {
    $location = @($marylandLocations | Where-Object {
        $_.RegistrationNumber -eq $row.LicenseNumber -and $_.Suffix -eq $row.LocationSuffix -and
        $_.CrawlOutcome -eq 'Captured' -and $_.TradeName -ceq $row.EntityName -and
        $_.DetailUrl -ceq $row.SourceUrl -and $_.SourceStatusBasis -ceq $row.SourceStatusBasis -and
        $_.RosterRowCount -eq $row.RosterRowCount -and
        "$($_.Address), $($_.City), $($_.State) $($_.Zip)" -ceq $row.BusinessAddress
    })
    $broker = @($marylandPeople | Where-Object {
        $_.Category -eq 'BROKER' -and $_.RegistrationNumber -eq $row.BrokerRegistrationNumber -and
        $_.LocationRegistrationNumber -eq $row.LicenseNumber -and $_.LocationSuffix -eq $row.LocationSuffix -and
        $_.Name -ceq $row.BrokerName -and $_.Expiration -eq $row.BrokerExpiration -and
        $_.TradeName -ceq $row.TradeName -and $_.SourceUrl -ceq $row.SourceUrl -and
        "$($_.Address), $($_.City), $($_.State) $($_.Zip)" -ceq $row.BusinessAddress -and
        $_.PracticeRestriction -eq ''
    })
    if ($row.State -ne 'Maryland' -or $row.SourceLicenseType -ne 'BROKER' -or
        $row.LocationSuffix -ne 'N/A' -or $row.LicenseNumber -notmatch '^\d+$' -or
        $row.LicenseNumber -ne $row.BrokerRegistrationNumber -or $row.TradeName -cne $row.EntityName -or
        $row.QueryCity -ne 'Baltimore' -or $row.CaptureDate -ne '2026-10-11' -or
        $row.SourceStatusBasis -ne 'Active-license public query; inactive licensees excluded' -or
        $location.Count -ne 1 -or $broker.Count -ne 1 -or
        [datetime]::ParseExact($row.BrokerExpiration, 'yyyy-MM-dd', [cultureinfo]::InvariantCulture) -lt [datetime]'2026-10-11') {
        throw "Unexpected Maryland broker-linked location $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Maryland' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType 'BROKER' -CampaignSegment 'Maryland broker-linked business location; personal broker credential, not company license' `
        -SourceStatus $row.SourceStatusBasis -QueryCity $row.QueryCity -BusinessAddress $row.BusinessAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Published location suffix N/A; no branch or parent relationship inferred.' `
        -SourceFile "MARYLAND/XLXS/$marylandFile" -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes "Broker $($row.BrokerName), registration $($row.BrokerRegistrationNumber), personal BROKER expiration $($row.BrokerExpiration), is returned at the exactly matching business location. No separate entity credential, entity expiration or exact per-row status established. The location roster contains $($row.RosterRowCount) people. Captured 2026-10-11; 20 midpoint-quantile locations selected from 256 eligible unsuffixed locations in the 307-result Baltimore query. Personal samples remain separate. Not statewide, representative or authorized for outreach; recheck current status and source-use requirements."
}

$ohioRoot = Join-Path $docsRoot 'OHIO\XLXS'
$ohioFile = 'Ohio_Active_Entity_Branch_Campaign_Development_Sample_20261010.csv'
$ohioSourceFile = 'repl_LicenseLookUpwithAffiliations.xlsx'
$ohioSourceUrl = 'https://elicense3.com.ohio.gov/Lookup/DownloadRoster.aspx'
$ohioLookupUrl = 'https://elicense3.com.ohio.gov/Lookup/LicenseLookup.aspx'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $ohioRoot $ohioFile))) {
    if ($row.Status -ne 'Active' -or [string]::IsNullOrWhiteSpace($row.ExpirationDate)) { continue }
    $segment = switch ($row.LicenseType) {
        'Brokerage' { 'Ohio brokerage campaign candidate; dated active roster' }
        'Branch Office' { 'Ohio branch-office campaign candidate; explicit parent reference' }
        'Sole Proprietor' { 'Ohio sole-proprietor license candidate; retain published type' }
        default { throw "Unexpected Ohio campaign license type: $($row.LicenseType)" }
    }
    if ($row.LicenseType -eq 'Branch Office' -and [string]::IsNullOrWhiteSpace($row.ParentFirmLicenseNumber)) {
        throw "Ohio branch office $($row.LicenseNumber) is missing its explicit parent license reference."
    }
    $notes = "Status and expiration are from the Ohio eLicense roster dated 2026-09-04; recheck in the live lookup before operational use. The statewide export has no address, city, phone, or email fields. Internal development/research lead only; no outreach authorization or permission to account-match was established."
    Add-Candidate -State 'Ohio' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment $segment `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate `
        -ParentFirmLicenseNumber $row.ParentFirmLicenseNumber `
        -BranchEvidence $(if ($row.LicenseType -eq 'Branch Office') { 'The dated official roster explicitly supplies a Primary License reference to the parent brokerage; no name-based relationship was inferred.' } else { '' }) `
        -SourceFile "OHIO/XLXS/$ohioFile; OHIO/XLXS/$ohioSourceFile" `
        -OfficialSourceUrl "$ohioSourceUrl; $ohioLookupUrl" `
        -CampaignNotes $notes
}

$massachusettsRoot = Join-Path $docsRoot 'MASSACHUSETTS\XLXS'
$massachusettsFile = 'Massachusetts_Active_Entity_Campaign_Development_Sample_20261011.csv'
$massachusettsSourceUrl = 'https://occupationallicensingandpermitting.mass.gov/madol/s/license-search-page'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $massachusettsRoot $massachusettsFile))) {
    if ($row.Status -ne 'Active') { continue }
    Add-Candidate -State 'Massachusetts' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Massachusetts active business-license research candidate' `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "MASSACHUSETTS/XLXS/$massachusettsFile" -OfficialSourceUrl $massachusettsSourceUrl `
        -CampaignNotes 'Statewide exact-Active DOL search-result sample captured 2026-10-11. No branch relationship, business address, or outreach authorization was verified. DOL warns records may not show all prior discipline; recheck before any operational use.'
}

$missouriRoot = Join-Path $docsRoot 'MISSOURI\XLXS'
$missouriFile = 'Missouri_Active_Entity_Campaign_Development_Sample_20261011.csv'
$missouriSourceUrl = 'https://mopro.mo.gov/license/s/license-search'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $missouriRoot $missouriFile))) {
    if ($row.Status -ne 'Active' -or $row.LicenseType -ne 'Real Estate Corporation') { continue }
    Add-Candidate -State 'Missouri' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Missouri active real-estate corporation; internal research lead only' `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "MISSOURI/XLXS/$missouriFile" -OfficialSourceUrl $missouriSourceUrl `
        -CampaignNotes 'Jackson County Active Licensee Only query; profile detail-checked 2026-10-11. No parent/branch relationship, direct contact information, or outreach authorization was verified. Recheck status and discipline before operational use.'
}

$northCarolinaRoot = Join-Path $docsRoot 'NORTH CAROLINA\LICENSING\XLXS'
$northCarolinaFile = 'North_Carolina_Charlotte_Active_Firm_Development_Sample_20261011.csv'
$northCarolinaSourceUrl = 'https://license.ncrec.gov/ncrec/oecgi3.exe/O4W_FIRM_SEARCH_NEW'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $northCarolinaRoot $northCarolinaFile))) {
    if ($row.Status -ne 'Active') { continue }
    Add-Candidate -State 'North Carolina' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'North Carolina active firm; internal research lead only' `
        -SourceStatus $row.Status -QueryCity $row.QueryCity -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "NORTH CAROLINA/LICENSING/XLXS/$northCarolinaFile" -OfficialSourceUrl $northCarolinaSourceUrl `
        -CampaignNotes "Charlotte firm-search capture dated $($row.SourceAsOfDate); captured status was ACTIVE. Recheck live status before any operational use. No branch relationship or outreach authorization is asserted."
}

$georgiaRoot = Join-Path $docsRoot 'GEORGIA\LICENSING\XLXS'
$georgiaFile = 'Georgia_Atlanta_Firm_Development_Sample_20261013.csv'
$georgiaSourceUrl = 'https://ata.grec.state.ga.us/Account/Search'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $georgiaRoot $georgiaFile))) {
    if ($row.Status -ne 'ACTIVE') { continue }
    Add-Candidate -State 'Georgia' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.DBA `
        -SourceLicenseType $row.OrganizationType -CampaignSegment 'Georgia active real-estate firm; internal research lead only' `
        -SourceStatus $row.Status -QueryCity $row.QueryCity -BusinessAddress $row.AddressOfRecord -BusinessPhone $row.BusinessPhone `
        -LicenseExpiryOrRenewal $row.RenewalDue -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "GEORGIA/LICENSING/XLXS/$georgiaFile" -OfficialSourceUrl $georgiaSourceUrl `
        -CampaignNotes "GREC public 'Find a Real Estate Agent' Atlanta company-search capture dated $($row.SourceAsOfDate); captured status was ACTIVE. GREC exposes no public firm-to-agent roster, so sponsored-agent counts (ActiveAgents=$($row.ActiveAgents)) are a displayed count only, not a resolved relationship list. Qualifying broker license ($($row.QualifyingBrokerLicense)) is the portal-displayed qualifying broker, not a verified affiliation edge. Recheck live status before any operational use; no outreach authorization is asserted."
}

$newHampshireRoot = Join-Path $docsRoot 'NEW HAMPSHIRE\XLXS'
$newHampshireFile = 'New_Hampshire_Active_Firm_Development_Sample_20261011.csv'
$newHampshireSourceUrl = 'https://www.oplc.nh.gov/list-oplc-licensees-and-their-license-types'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $newHampshireRoot $newHampshireFile))) {
    if ($row.Status -notmatch '^Active' -or $row.LicenseType -ne 'Firm') { continue }
    Add-Candidate -State 'New Hampshire' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'New Hampshire active real-estate firm; internal research lead only' `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "NEW HAMPSHIRE/XLXS/$newHampshireFile" -OfficialSourceUrl $newHampshireSourceUrl `
        -CampaignNotes "Statewide OPLC real-estate roster dated $($row.SourceAsOfDate); exact published type=Firm. The source is not real-time; recheck status before operational use. No licensee affiliation or outreach authorization was verified."
}

$alaskaRoot = Join-Path $docsRoot 'ALASKA\XLXS'
$alaskaOfficeFile = 'Alaska_Open_Office_Development_Sample_20261011.csv'
$alaskaSourceUrl = 'https://www.commerce.alaska.gov/cbp/main/'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $alaskaRoot $alaskaOfficeFile))) {
    if ($row.Status -ne 'Office Open' -or $row.LicenseType -notin @('Real Estate Office Main', 'Real Estate Office Branch')) { continue }
    Add-Candidate -State 'Alaska' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Alaska open real-estate office license; internal research lead only' `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress $row.BusinessAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "ALASKA/XLXS/$alaskaOfficeFile" -OfficialSourceUrl $alaskaSourceUrl `
        -CampaignNotes "Statewide CBPL professional-license CSV captured locally on 2026-10-03; exact status=Office Open and published type=$($row.LicenseType). CBPL disclaims accuracy/reliability; recheck status before use. No broker-office affiliation, branch parent, direct contact details, or outreach authorization is asserted."
}

$floridaRoot = Join-Path $docsRoot 'FLORIDA\XLXS'
$floridaEntityFile = 'Florida_Active_Entity_Development_Sample_20261011.csv'
$floridaSourceUrl = 'https://www2.myfloridalicense.com/sto/file_download/extracts/RealEstateCorpLicense.csv'
$floridaRows = @(Import-Csv -LiteralPath (Join-Path $floridaRoot $floridaEntityFile))
if ($floridaRows.Count -ne 60) {
    throw "Unexpected Florida campaign sample count: $($floridaRows.Count)."
}
$floridaTypes = @(
    '2502 Real Estate Corporation',
    '2503 Real Estate Partnership',
    '2504 Real Estate Branch Office'
)
foreach ($licenseType in $floridaTypes) {
    $matching = @($floridaRows | Where-Object {
        $_.LicenseCategory -eq $licenseType -and
        $_.PrimaryStatus -eq 'Current' -and
        $_.SecondaryStatus -eq 'Active'
    })
    if ($matching.Count -ne 20) {
        throw "Unexpected Florida sample count for '$licenseType': $($matching.Count)."
    }
}
foreach ($row in $floridaRows) {
    if ($row.PrimaryStatus -ne 'Current' -or $row.SecondaryStatus -ne 'Active' -or $row.LicenseCategory -notin $floridaTypes) {
        throw "Unexpected Florida campaign sample row for license $($row.LicenseNumber)."
    }
    $branchEvidence = ''
    if ($row.LicenseCategory -eq '2504 Real Estate Branch Office') {
        $branchEvidence = 'DBPR labels the published license type Real Estate Branch Office; no parent license is inferred.'
    }
    Add-Candidate -State 'Florida' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseCategory -CampaignSegment "Florida Current/Active $($row.LicenseCategory); internal research lead only" `
        -SourceStatus "$($row.PrimaryStatus); $($row.SecondaryStatus)" -QueryCity $row.City -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "FLORIDA/XLXS/$floridaEntityFile; $($row.SourceFile)" -OfficialSourceUrl $floridaSourceUrl `
        -CampaignNotes "Statewide DBPR public-record download captured $($row.SourceAsOfDate); exact Primary Status=$($row.PrimaryStatus) and Secondary Status=$($row.SecondaryStatus). Employer fields are retained as source text only; no parent-firm, branch-parent, or individual-licensee affiliation is inferred. DBPR updates downloads weekly but cautions maintenance may delay refresh. Verify current status and clear source-use/outreach requirements before operational use."
}

$illinoisRoot = Join-Path $docsRoot 'ILLINOIS\LICENSING\XLXS'
$illinoisEntityFile = 'Illinois_Active_Entity_Development_Sample_20261011.csv'
$illinoisSourceUrl = 'https://data.illinois.gov/Business-and-Workforce/Professional-Licensing/pzzh-kp68'
$illinoisRows = @(Import-Csv -LiteralPath (Join-Path $illinoisRoot $illinoisEntityFile))
$illinoisTypes = @(
    'LICENSED REAL ESTATE BROKER CORPORATION',
    'LICENSED REAL ESTATE LIMITED LIABILITY FIRM',
    'LICENSED REAL ESTATE BROKER PARTNERSHIP',
    'LICENSED REAL ESTATE BRANCH OFFICE',
    'LICENSED REAL ESTATE VIRTUAL OFFICE'
)
if ($illinoisRows.Count -ne 100) {
    throw "Unexpected Illinois campaign sample count: $($illinoisRows.Count)."
}
foreach ($licenseType in $illinoisTypes) {
    $matching = @($illinoisRows | Where-Object {
        $_.LicenseType -eq $licenseType -and $_.Status -eq 'ACTIVE'
    })
    if ($matching.Count -ne 20) {
        throw "Unexpected Illinois sample count for '$licenseType': $($matching.Count)."
    }
}
foreach ($row in $illinoisRows) {
    if ($row.Status -ne 'ACTIVE' -or $row.LicenseType -notin $illinoisTypes) {
        throw "Unexpected Illinois campaign sample row for license $($row.LicenseNumber)."
    }
    $branchEvidence = ''
    if ($row.LicenseType -eq 'LICENSED REAL ESTATE BRANCH OFFICE') {
        $branchEvidence = 'IDFPR labels the published license type Licensed Real Estate Branch Office; no parent license is inferred.'
    }
    $disciplineNote = if ($row.EverDisciplined -eq 'Y') {
        'The source Ever Disciplined field is Y; review current public discipline records before operational use.'
    }
    else {
        "The source Ever Disciplined field is $($row.EverDisciplined)."
    }
    Add-Candidate -State 'Illinois' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment "Illinois Active $($row.LicenseType); internal research lead only" `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "ILLINOIS/LICENSING/XLXS/$illinoisEntityFile; $($row.SourceFile)" -OfficialSourceUrl $illinoisSourceUrl `
        -CampaignNotes "Statewide IDFPR Professional Licensing dataset captured $($row.SourceAsOfDate); exact published type and status retained. $disciplineNote Repeated disciplinary-action rows were collapsed only when license identity fields agreed. No parent-firm, branch-parent, or individual-licensee relationship is inferred. Recheck status and resolve source-use/outreach requirements before operational use."
}

$newYorkRoot = Join-Path $docsRoot 'NEW YORK\LICENSING\XLXS'
$newYorkEntityFile = 'New_York_Active_Entity_Development_Sample_20261011.csv'
$newYorkSourceUrl = 'https://data.ny.gov/Economic-Development/Active-Real-Estate-Salespersons-and-Brokers/yg7h-zjbf'
$newYorkRows = @(Import-Csv -LiteralPath (Join-Path $newYorkRoot $newYorkEntityFile))
$newYorkTypes = @(
    'CORPORATE BROKER',
    'LIMITED LIABILITY BROKER',
    'PARTNERSHIP BROKER',
    'REAL ESTATE BRANCH OFFICE',
    'REAL ESTATE PRINCIPAL OFFICE'
)
if ($newYorkRows.Count -ne 100) {
    throw "Unexpected New York campaign sample count: $($newYorkRows.Count)."
}
foreach ($licenseType in $newYorkTypes) {
    $matching = @($newYorkRows | Where-Object {
        $_.LicenseType -eq $licenseType -and $_.Status -eq 'Active'
    })
    if ($matching.Count -ne 20) {
        throw "Unexpected New York sample count for '$licenseType': $($matching.Count)."
    }
}
foreach ($row in $newYorkRows) {
    if ($row.Status -ne 'Active' -or $row.LicenseType -notin $newYorkTypes) {
        throw "Unexpected New York campaign sample row for license $($row.LicenseNumber)."
    }
    $branchEvidence = ''
    if ($row.LicenseType -eq 'REAL ESTATE BRANCH OFFICE') {
        $branchEvidence = 'The official source labels this license REAL ESTATE BRANCH OFFICE; no parent license is inferred.'
    }
    Add-Candidate -State 'New York' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment "New York active-source $($row.LicenseType); internal research lead only" `
        -SourceStatus 'Active (active-only dataset; no separate status field)' -QueryCity $row.City -BusinessAddress $row.BusinessAddress -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "NEW YORK/LICENSING/XLXS/$newYorkEntityFile; $($row.SourceFile)" -OfficialSourceUrl $newYorkSourceUrl `
        -CampaignNotes "Statewide NY DOS active-only dataset captured $($row.SourceAsOfDate); exact published license type retained. Source does not provide a separate status field, so Active reflects dataset scope. Branch and principal offices remain separate; no parent-firm or broker affiliation is inferred. Recheck status and resolve source-use/outreach requirements before operational use."
}

$pennsylvaniaRoot = Join-Path $docsRoot 'PENNSYLVANIA\XLXS'
$pennsylvaniaEntityFile = 'Pennsylvania_Active_Entity_Development_Sample_20261011.csv'
$pennsylvaniaSourceUrl = 'https://www.pals.pa.gov/#!/page/search'
$pennsylvaniaRows = @(Import-Csv -LiteralPath (Join-Path $pennsylvaniaRoot $pennsylvaniaEntityFile))
$pennsylvaniaTypeCounts = @{
    'Broker (Corp LLC Partner)-Standard' = 16
    'Branch Office' = 19
    'Broker (Sole Proprietor)-Standard' = 14
}
if ($pennsylvaniaRows.Count -ne 49) {
    throw "Unexpected Pennsylvania campaign sample count: $($pennsylvaniaRows.Count)."
}
foreach ($licenseType in $pennsylvaniaTypeCounts.Keys) {
    $matching = @($pennsylvaniaRows | Where-Object {
        $_.LicenseType -eq $licenseType -and $_.Status -eq 'Active'
    })
    if ($matching.Count -ne $pennsylvaniaTypeCounts[$licenseType]) {
        throw "Unexpected Pennsylvania sample count for '$licenseType': $($matching.Count)."
    }
}
foreach ($row in $pennsylvaniaRows) {
    if ($row.Status -ne 'Active' -or $row.LicenseType -notin $pennsylvaniaTypeCounts.Keys) {
        throw "Unexpected Pennsylvania campaign sample row for license $($row.LicenseNumber)."
    }

    $campaignSegment = "Pennsylvania active $($row.LicenseType); internal research lead only"
    $branchEvidence = ''
    if ($row.LicenseType -eq 'Branch Office') {
        $branchEvidence = 'PALS publishes this license type as Branch Office; no parent office or firm relationship is inferred.'
    }
    if ($row.LicenseType -eq 'Broker (Sole Proprietor)-Standard') {
        $campaignSegment = 'Pennsylvania active sole-proprietor broker; separate small-business research segment'
    }

    Add-Candidate -State 'Pennsylvania' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment $campaignSegment `
        -SourceStatus $row.Status -QueryCity $row.QueryCity -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "PENNSYLVANIA/XLXS/$pennsylvaniaEntityFile" -OfficialSourceUrl $pennsylvaniaSourceUrl `
        -CampaignNotes "PALS public verification search captured 2026-10-11; exact Active results from Philadelphia city-filtered facility searches only. PALS does not provide a public bulk roster download. Sole-proprietor brokers are individual licensees and remain separate from firms; branch status is retained as published with no parent inferred. No direct contact or outreach authorization is supplied. Recheck current status and resolve source-use/outreach requirements before operational use."
}

$vermontRoot = Join-Path $docsRoot 'VERMONT\XLXS'
$vermontEntityFile = 'Vermont_Active_Entity_Development_Sample_20261011.csv'
$vermontSourceUrl = 'https://sos.vermont.gov/opr/find-a-professional/'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $vermontRoot $vermontEntityFile))) {
    if ($row.Status -ne 'Active' -or $row.LicenseType -notin @(
        'Real Estate Brokerage Firm - Main Office',
        'Real Estate Brokerage Firm - Branch Office'
    )) { continue }

    $addressParts = @($row.Address1, $row.Address2, $row.City, $row.State, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $branchEvidence = ''
    $notes = "Statewide Vermont OPR roster captured 2026-10-11; exact status=Active and published type=$($row.LicenseType). The download gives no separate data-through date; recheck current status and discipline before use. No outreach authorization is asserted."
    if ($row.LicenseType -eq 'Real Estate Brokerage Firm - Branch Office') {
        $branchEvidence = 'The official roster labels the license Real Estate Brokerage Firm - Branch Office; parent firm/license was not confirmed.'
        if (-not [string]::IsNullOrWhiteSpace($row.BranchName)) {
            $notes += " Published Branch Name: $($row.BranchName)."
        }
        $notes += ' No parent office or broker relationship is inferred.'
    }

    Add-Candidate -State 'Vermont' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment "Vermont active $($row.LicenseType) license; internal research lead only" `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress ($addressParts -join ', ') -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.LicenseExpiration -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "VERMONT/XLXS/$vermontEntityFile" -OfficialSourceUrl $vermontSourceUrl `
        -CampaignNotes $notes
}

$coloradoRoot = Join-Path $docsRoot 'COLORADO\XLXS'
$coloradoEntityFile = 'Colorado_Active_Entity_Development_Sample_20261011.csv'
$coloradoIndividualFile = 'Colorado_Active_Individual_Development_Sample_20261011.csv'
$coloradoSourceUrl = 'https://apps2.colorado.gov/dre/licensing/Lookup/GenerateRoster.aspx'
$coloradoCompanies = @(Import-Csv -LiteralPath (Join-Path $coloradoRoot $coloradoEntityFile))
$coloradoIndividualSamples = @(Import-Csv -LiteralPath (Join-Path $coloradoRoot $coloradoIndividualFile))
$coloradoProprietors = @($coloradoIndividualSamples | Where-Object LicenseCategory -eq 'Active Individual Proprietors')
if ($coloradoCompanies.Count -ne 40 -or $coloradoProprietors.Count -ne 40) {
    throw "Unexpected Colorado campaign sample counts: $($coloradoCompanies.Count) companies, $($coloradoProprietors.Count) proprietors."
}
foreach ($prefix in @('EC', 'IC')) {
    if (@($coloradoCompanies | Where-Object { $_.LicenseTypePrefix -eq $prefix -and $_.Status -eq 'Active' }).Count -ne 20) {
        throw "Unexpected Colorado company sample for prefix $prefix."
    }
}
foreach ($prefix in @('EI', 'II')) {
    if (@($coloradoProprietors | Where-Object { $_.LicenseTypePrefix -eq $prefix -and $_.Status -eq 'Active' }).Count -ne 20) {
        throw "Unexpected Colorado proprietor sample for prefix $prefix."
    }
}
foreach ($row in $coloradoCompanies) {
    if ($row.Status -ne 'Active' -or $row.LicenseTypePrefix -notin @('EC', 'IC')) {
        throw "Unexpected Colorado company sample row for license $($row.LicenseNumber)."
    }
    $addressParts = @($row.AddressLine1, $row.AddressLine2, $row.City, $row.State, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    Add-Candidate -State 'Colorado' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseTypePrefix -CampaignSegment "Colorado active real-estate company [$($row.LicenseTypePrefix)]; internal research lead only" `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress ($addressParts -join ', ') -BusinessPhone $row.Phone `
        -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "COLORADO/XLXS/$coloradoEntityFile; $($row.SourceFile)" -OfficialSourceUrl $coloradoSourceUrl `
        -CampaignNotes "Statewide DRE eLicense roster captured $($row.SourceAsOfDate); exact status=Active and published prefix=$($row.LicenseTypePrefix). Supervisor and supervision fields are retained as source evidence only; no individual-to-company, parent/branch, or cross-roster relationship is inferred. Recheck status and clear source-use/outreach requirements before operational use."
}
foreach ($row in $coloradoProprietors) {
    if ($row.Status -ne 'Active' -or $row.LicenseTypePrefix -notin @('EI', 'II')) {
        throw "Unexpected Colorado proprietor sample row for license $($row.LicenseNumber)."
    }
    $addressParts = @($row.AddressLine1, $row.AddressLine2, $row.City, $row.State, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    Add-Candidate -State 'Colorado' -LicenseNumber $row.LicenseNumber -EntityName $row.LicenseeName -TradeName $row.DBA `
        -SourceLicenseType $row.LicenseTypePrefix -CampaignSegment "Colorado individual proprietor [$($row.LicenseTypePrefix)]; separate small-business research segment" `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress ($addressParts -join ', ') -BusinessPhone $row.Phone `
        -LicenseExpiryOrRenewal $row.LicenseExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence '' `
        -SourceFile "COLORADO/XLXS/$coloradoIndividualFile; $($row.SourceFile)" -OfficialSourceUrl $coloradoSourceUrl `
        -CampaignNotes "Statewide DRE eLicense roster captured $($row.SourceAsOfDate); exact status=Active and published individual-proprietor prefix=$($row.LicenseTypePrefix). This is an individual broker cohort with a DBA, not a separately licensed company. No DBA-to-company license, employer, supervisor, or parent/branch relationship is inferred. Recheck status and clear source-use/outreach requirements before operational use."
}

$utahRoot = Join-Path $docsRoot 'UTAH\XLXS'
$utahEntityFile = 'Utah_Active_Entity_Development_Sample_20261011.csv'
$utahSourceUrl = 'https://secure.utah.gov/rer/relv/search.html'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $utahRoot $utahEntityFile))) {
    if ($row.Status -ne 'Active' -or $row.LicenseType -notin @('Real Estate Company', 'Branch Office', 'Property Management Company')) { continue }
    $branchEvidence = ''
    $notes = "Statewide Utah public summary download captured $($row.SourceAsOfDate); exact status=Active and published type=$($row.LicenseType). Recheck current status before use. Summary has no direct contact fields; no outreach authorization is asserted."
    if ($row.LicenseType -eq 'Branch Office') {
        $branchEvidence = 'The official source labels this license Branch Office; no parent firm license is inferred.'
        $notes += ' The source Broker License # mapping is not treated as a parent-company relationship.'
    }
    Add-Candidate -State 'Utah' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment "Utah active $($row.LicenseType) license; internal research lead only" `
        -SourceStatus $row.Status -QueryCity '' -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "UTAH/XLXS/$utahEntityFile" -OfficialSourceUrl $utahSourceUrl -CampaignNotes $notes
}

$arizonaRoot = Join-Path $docsRoot 'ARIZONA\XLXS'
$arizonaEntityFile = 'Arizona_Active_Entity_Development_Sample_20261011.csv'
$arizonaSourceUrl = 'https://services.azre.gov/PdbWeb/List/ViewLists'
foreach ($row in (Import-Csv -LiteralPath (Join-Path $arizonaRoot $arizonaEntityFile))) {
    if ($row.Status -ne 'Active' -or [string]::IsNullOrWhiteSpace($row.LicenseNumber)) { continue }
    $branchEvidence = ''
    $notes = "Statewide Arizona ADRE public database list downloaded 2026-10-11; exact status=Active and published type=$($row.LicenseType). Recheck current status before use. No outreach permission is asserted."
    if ($row.LicenseType -like 'Branch,*') {
        $branchEvidence = "The ADRE source labels this license type '$($row.LicenseType)'; no parent firm license is inferred."
        $notes += ' The source designated-broker license number is not treated as a branch-parent relationship or verified current supervision.'
    }
    Add-Candidate -State 'Arizona' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.LicenseType -CampaignSegment "Arizona active $($row.LicenseType) entity license; internal research lead only" `
        -SourceStatus $row.Status -QueryCity $row.City -BusinessAddress $row.BusinessAddress -BusinessPhone $row.BusinessPhone `
        -LicenseExpiryOrRenewal $row.ExpirationDate -ParentFirmLicenseNumber '' -BranchEvidence $branchEvidence `
        -SourceFile "ARIZONA/XLXS/$arizonaEntityFile" -OfficialSourceUrl $arizonaSourceUrl -CampaignNotes $notes
}

$kentuckyRoot = Join-Path $docsRoot 'KENTUCKY\XLXS'
$kentuckyFile = 'Kentucky_Active_Entity_Development_Sample_20261011.csv'
$kentuckySourceFile = 'Kentucky_Active_Registered_Firm_Branch_All_Regions_20261011.csv'
$kentuckyRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $kentuckyRoot $kentuckyFile))
$kentuckySource = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $kentuckyRoot $kentuckySourceFile))
if ($kentuckyRows.Count -ne 20 -or @($kentuckyRows.LicenseNumber | Sort-Object -Unique).Count -ne 20 -or
    $kentuckySource.Count -ne 2814) {
    throw 'Unexpected Kentucky entity sample or source count.'
}
foreach ($row in $kentuckyRows) {
    $sourceIndex = [int]$row.SourceRow - 1
    if ($sourceIndex -lt 0 -or $sourceIndex -ge $kentuckySource.Count) { throw 'Invalid Kentucky source row.' }
    $source = $kentuckySource[$sourceIndex]
    foreach ($column in $source.PSObject.Properties.Name) {
        if ($row.$column -cne $source.$column) { throw "Kentucky source mapping mismatch: $($row.LicenseNumber) / $column" }
    }
    if ($row.State -cne 'Kentucky' -or $row.RecordKind -cne 'Business license' -or
        $row.SourceLicenseType -cne 'Registered Firm Branch' -or $row.SourceStatus -cne 'Active' -or
        $row.LicenseNumber -cne $source.'License ID' -or $row.PublishedName -cne $source.Name -or
        $row.SourceFile -cne 'Kentucky_Active_Registered_Firm_Branch_All_Regions_20261011.xls') {
        throw "Unexpected Kentucky entity sample mapping $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Kentucky' -LicenseNumber $row.LicenseNumber -EntityName $row.PublishedName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Kentucky Registered Firm Branch; development/testing only' `
        -SourceStatus $row.SourceStatus -QueryCity '' -BusinessAddress $row.'Business Address' -BusinessPhone '' `
        -LicenseExpiryOrRenewal $row.'Expiration Date' -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Published Registered Firm Branch category retained; no separate main-firm classification or parent/broker link inferred.' `
        -SourceFile "KENTUCKY/XLXS/$kentuckyFile; KENTUCKY/XLXS/$($row.SourceFile)" `
        -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes 'Twenty midpoint-quantile samples from the 2814-row all-regions Active Directory business export captured 2026-10-11. Source License ID used as identity; only one separate nonsample profile individually verified. HTML-table .xls original retained. Export address labels differ from visible directory ordering; values/labels retained, not validated delivery addresses. No discipline clearance, legal-form assertion, parent affiliation, production use or outreach permission. Separate full directory development campaign includes personal records and explicit non-Active status variants; shared entity rows overlap it.'
}

$louisianaRoot = Join-Path $docsRoot 'LOUISIANA\XLXS'
$michiganRoot = Join-Path $docsRoot 'MICHIGAN\XLXS'
$michiganFile = 'Michigan_Exact_Active_Entities_20261012.csv'
$michiganRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $michiganRoot $michiganFile))
$michiganRawFile = 'Michigan_Real_Estate_Active_Licenses_Raw_20261012.csv'
$michiganRaw = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $michiganRoot $michiganRawFile))
$michiganEdges = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $michiganRoot 'Michigan_Published_Broker_Relationships_20261012.csv'))
$michiganParents = @{}
foreach ($edge in $michiganEdges) {
    if ($edge.FromType -cne 'Branch Office') { continue }
    if ($michiganParents.ContainsKey($edge.FromLicenseNumber)) { throw 'Multiple Michigan branch broker references require review.' }
    $michiganParents[$edge.FromLicenseNumber] = $edge
}
if ($michiganRows.Count -ne 7639 -or $michiganRaw.Count -ne 63427 -or $michiganEdges.Count -ne 53442) {
    throw 'Unexpected Michigan campaign/source/reference totals.'
}
foreach ($row in $michiganRows) {
    $index = [int]$row.SourceRow - 2
    if ($index -lt 0 -or $index -ge $michiganRaw.Count) { throw 'Invalid Michigan source row.' }
    $source = $michiganRaw[$index]
    $payload = $row.SourceRecordJson | ConvertFrom-Json
    foreach ($column in $source.PSObject.Properties.Name) {
        if ([string]$source.$column -cne [string]$payload.$column) { throw 'Michigan full payload/source mapping mismatch.' }
    }
    if ($row.SourceStatus -cne 'Active' -or $row.RecordKind -cne 'Business license' -or
        $row.SourceLicenseType -cnotin @('Branch Office','Broker Company') -or
        $row.LicenseNumber -cne $source.'License No' -or $row.SourceLicenseType -cne $source.Type) {
        throw 'Unexpected Michigan business identity/type/status.'
    }
    $notes = 'All 7639 exact-Active business credentials, no downsampling; full state output retains 7642 businesses including two Suspended and one Active - In Late Renewal. All 63425 credentials and published broker references retained separately. Official Active-branded reports are not all-status history. No individual profile verification, inferred DBA, production import or outreach permission.'
    $parent = ''
    if ($michiganParents.ContainsKey($row.LicenseNumber)) {
        $edge = $michiganParents[$row.LicenseNumber]
        if ($edge.EndpointResolution -ceq 'Both endpoints in licensing report' -and $edge.ToType -cin @('Broker Company','Broker Individual')) {
            $parent = $edge.ToLicenseNumber
        }
        $notes += " Published associated broker $($edge.ToLicenseNumber); broker status $($edge.PublishedBrokerStatus); resolution $($edge.EndpointResolution). Only resolved Broker Company/Individual endpoints populate parent field; no number-prefix inference."
    }
    Add-Candidate -State 'Michigan' -LicenseNumber $row.LicenseNumber -EntityName $row.PublishedName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $row.CampaignSegment -SourceStatus $row.SourceStatus `
        -QueryCity '' -BusinessAddress $row.BusinessAddress -BusinessPhone '' -LicenseExpiryOrRenewal $row.Expiration `
        -ParentFirmLicenseNumber $parent -BranchEvidence 'Published Branch Office/associated broker categories retained; separate relationship CSV preserves original IDs, dates, statuses and unresolved targets.' `
        -SourceFile "MICHIGAN/XLXS/$michiganFile; MICHIGAN/XLXS/$($row.SourceFile)" -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes ($notes + " Name basis: $($row.NameBasis). $($row.DateReview)")
}

$maineRoot = Join-Path $docsRoot 'MAINE\XLXS'
$maineFile = 'Maine_Full_Active_Entities_20261012.csv'
$maineRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $maineRoot $maineFile))
$maineSourceFile = 'Maine_Active_Companies_All_Locations_20261012.csv'
$maineSource = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $maineRoot $maineSourceFile))
if ($maineRows.Count -ne 1101 -or $maineSource.Count -ne 1804 -or
    @($maineRows.LicenseNumber | Sort-Object -Unique).Count -ne 1101) { throw 'Unexpected Maine business counts.' }
foreach ($row in $maineRows) {
    $raw = $row.SourceRecordJson | ConvertFrom-Json
    $positions = @($row.SourceRows -split ';')
    if ($positions.Count -ne $raw.Count -or $positions.Count -ne [int]$row.SourceRowCount -or
        $row.SourceStatus -cne 'ACTIVE' -or $row.RecordKind -cne 'Business license') { throw 'Unexpected Maine business provenance.' }
    for ($i = 0; $i -lt $positions.Count; $i++) {
        $index = [int]$positions[$i] - 1
        if ($index -lt 0 -or $index -ge $maineSource.Count) { throw 'Invalid Maine source position.' }
        $source = $maineSource[$index]
        foreach ($column in $source.PSObject.Properties.Name) {
            if ($source.$column -cne $raw[$i].$column) { throw 'Maine original payload mismatch.' }
        }
        if ($source.'License Number' -cne $row.LicenseNumber -or $source.Profession -cne $row.SourceLicenseType) {
            throw 'Maine original identity mismatch.'
        }
    }
    $names = $row.PublishedNamesJson | ConvertFrom-Json
    $entityName = if ($row.ProfileLegalName) { $row.ProfileLegalName } else { $names[0] }
    $tradeName = ''
    if ($row.LicenseNumber -eq 'AL90603691') { $tradeName = '117 REALTY' }
    if ($row.LicenseNumber -eq 'AB90603149') { $tradeName = 'CENTURY 21 NORTH EAST' }
    Add-Candidate -State 'Maine' -LicenseNumber $row.LicenseNumber -EntityName $entityName -TradeName $tradeName `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $row.CampaignSegment -SourceStatus $row.SourceStatus `
        -QueryCity '' -BusinessAddress $row.BusinessAddress -BusinessPhone $row.BusinessPhone -LicenseExpiryOrRenewal $row.Expiration `
        -ParentFirmLicenseNumber $row.ParentFirmLicenseNumber `
        -BranchEvidence 'Exact published category retained. Only AB90603149 has individually captured Main Office AL90602886; no other parent inferred. Responsible Individual references retained in separate full relationship file.' `
        -SourceFile "MAINE/XLXS/$maineFile; MAINE/XLXS/$maineSourceFile" -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes 'All 1101 unique Active business credentials from unrestricted official export; no downsampling. Original 1804 name rows retained. Name variants are not extra licenses; first sorted source name used for display unless saved profile verifies legal name. All 8198 credentials and 962 published relationship references saved separately; two targets absent from Active exports. Full Active query coverage, not all-status history or complete relationship graph. No production import or outreach permission.'
}

$louisianaFile = 'Louisiana_Verified_Active_Entities_20261012.csv'
$louisianaRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $louisianaRoot $louisianaFile))
$louisianaProfiles = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $louisianaRoot 'Louisiana_Verified_Profiles_20261012.json') | ConvertFrom-Json
if ($louisianaRows.Count -ne 2) { throw 'Unexpected Louisiana verified Active entity count.' }
foreach ($row in $louisianaRows) {
    $profile = @($louisianaProfiles | Where-Object id -ceq $row.LicenseNumber)
    if ($profile.Count -ne 1 -or $profile[0].status -cne 'Active' -or
        $profile[0].type -cne $row.SourceLicenseType -or $profile[0].name.Trim() -cne $row.PublishedName.Trim() -or
        $row.SourceStatus -cne 'Active' -or $row.RecordKind -cne 'Business license') {
        throw 'Louisiana saved profile/entity mapping mismatch.'
    }
    Add-Candidate -State 'Louisiana' -LicenseNumber $row.LicenseNumber -EntityName $row.PublishedName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $row.CampaignSegment -SourceStatus $row.SourceStatus `
        -QueryCity $row.QueryCity -BusinessAddress '' -BusinessPhone '' -LicenseExpiryOrRenewal $row.Expiration `
        -ParentFirmLicenseNumber '' -BranchEvidence 'Published category only; no parent firm inferred. Sponsor relationship retained in separate Louisiana relationship evidence.' `
        -SourceFile "LOUISIANA/XLXS/$louisianaFile; LOUISIANA/XLXS/$($row.ProfileFile)" `
        -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes 'Only detail-verified Active entities enter shared active smoke-test subset. All 561 collected unique accounts, including 69 businesses, remain in full Louisiana development output. Two same-filter 300-row responses differ; not complete city/state coverage. Branch profile address is Covington despite Baton Rouge query. Status not inferred from account suffix. Paid statewide roster quote not purchased. No production import or outreach permission.'
}

$indianaRoot = Join-Path $docsRoot 'INDIANA\XLXS'
$indianaFile = 'Indiana_Indianapolis_Active_Entity_Development_Sample_20261011.csv'
$indianaRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $indianaRoot $indianaFile))
$indianaEvidence = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $indianaRoot 'Indiana_License_Result_Captures_20261011.json') | ConvertFrom-Json
if ($indianaRows.Count -ne 40 -or @($indianaRows.LicenseNumber | Sort-Object -Unique).Count -ne 40) {
    throw 'Unexpected Indiana entity sample count or uniqueness.'
}
foreach ($type in @('Real Estate Broker Company', 'Real Estate Branch Office')) {
    if (@($indianaRows | Where-Object SourceLicenseType -ceq $type).Count -ne 20) {
        throw "Unexpected Indiana entity type count: $type."
    }
}
foreach ($row in $indianaRows) {
    $query = @($indianaEvidence | Where-Object { $_.query.type -ceq $row.SourceLicenseType })
    if ($query.Count -ne 1) { throw 'Indiana source query identity mismatch.' }
    $source = @($query[0].rows | Where-Object { $_.cells[1] -ceq $row.LicenseNumber })
    if ($source.Count -ne 1 -or $row.State -cne 'Indiana' -or
        $row.RecordKind -cne 'Business license' -or $row.SourceStatus -cne 'Active' -or
        $row.QueryCity -cne 'Indianapolis' -or $row.PublishedName -cne $source[0].cells[0] -or
        $row.SourceLicenseType -cne $source[0].cells[3] -or $row.SourceStatus -cne $source[0].cells[4] -or
        $row.Address -cne $source[0].cells[5]) {
        throw "Unexpected Indiana entity source mapping $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Indiana' -LicenseNumber $row.LicenseNumber -EntityName $row.PublishedName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment "Indiana $($row.SourceLicenseType); development/testing only" `
        -SourceStatus $row.SourceStatus -QueryCity $row.QueryCity -BusinessAddress $row.Address -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Exact facility type retained; parent company or broker affiliation not verified.' `
        -SourceFile "INDIANA/XLXS/$indianaFile; INDIANA/XLXS/Indiana_License_Result_Captures_20261011.json" `
        -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes 'First twenty alphabetical Active Indianapolis facility result rows per exact type, captured 2026-10-11. Not a complete city query or statewide roster. No individual detail verification, expiry or discipline evidence, parent-company links or outreach authorization. Paid bulk download not purchased. Personal Broker samples remain separate; Active searches for legacy Sales Person, Associate Broker and Professional Corp returned no rows.'
}

$idahoRoot = Join-Path $docsRoot 'IDAHO\XLXS'
$idahoFile = 'Idaho_Active_Entity_Development_Sample_20261011.csv'
$idahoRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $idahoRoot $idahoFile))
$idahoSource = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $idahoRoot 'Idaho_REC_Active_All_Types_20261011.csv'))
if ($idahoRows.Count -ne 33 -or @($idahoRows.LicenseNumber | Sort-Object -Unique).Count -ne 33 -or
    @($idahoRows | Where-Object SourceLicenseType -ceq 'Real Estate Company License').Count -ne 20 -or
    @($idahoRows | Where-Object SourceLicenseType -ceq 'Real Estate Branch Office License').Count -ne 13) {
    throw 'Unexpected Idaho company/branch sample count or uniqueness.'
}
foreach ($row in $idahoRows) {
    $sourceIndex = [int]$row.SourceRow - 1
    if ($sourceIndex -lt 0 -or $sourceIndex -ge $idahoSource.Count) { throw 'Invalid Idaho source row.' }
    $source = $idahoSource[$sourceIndex]
    if ($row.State -cne 'Idaho' -or $row.RecordKind -cne 'Business license' -or
        $row.SourceStatus -cne 'Active' -or $source.Status -cne 'Active' -or
        $row.LicenseNumber -cne $source.'License Number' -or $row.SourceLicenseType -cne $source.Type -or
        $row.BusinessName -cne $source.'Business Name' -or $row.DBA -cne $source.DBA -or
        $row.Expiration -cne $source.Expiration -or $row.Discipline -cne $source.Discipline) {
        throw "Unexpected Idaho entity sample mapping $($row.LicenseNumber)."
    }
    $address = @($row.Street, $row.Street2, $row.UnitType, $row.Unit, $row.City, $row.AddressState, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    Add-Candidate -State 'Idaho' -LicenseNumber $row.LicenseNumber -EntityName $row.PublishedName -TradeName $row.DBA `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment "Idaho $($row.SourceLicenseType); development/testing only" `
        -SourceStatus $row.SourceStatus -QueryCity '' -BusinessAddress ($address -join ', ') -BusinessPhone $row.PhoneNumber `
        -LicenseExpiryOrRenewal $row.Expiration -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Exact source company/branch type retained; no parent or broker relationship inferred.' `
        -SourceFile "IDAHO/XLXS/$idahoFile; IDAHO/XLXS/Idaho_REC_Active_All_Types_20261011.ods" `
        -OfficialSourceUrl $row.SourceUrl `
        -CampaignNotes "Active Real Estate Commission board-wide export captured 2026-10-11 without geography restriction. Twenty company midpoint samples and all thirteen branches. Name basis: $($row.NameBasis). Company type does not establish corporate legal form. Discipline=$($row.Discipline); preserve and review any published discipline. List updates weekday mornings/evenings, not individual real-time verification. No data-through timestamp, parent/affiliate inference, production use or outreach permission. Personal and cooperative-broker development records are separate."
}

$washingtonRoot = Join-Path $docsRoot 'WASHINGTON\XLXS'
$washingtonFile = 'Washington_Seattle_Active_Firm_Development_Sample_20261011.csv'
$washingtonSourceFile = 'Washington_Seattle_Active_Firm_Query_20261011.csv'
$washingtonSourceUrl = 'https://professions.dol.wa.gov/s/license-lookup'
$washingtonRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $washingtonRoot $washingtonFile))
$washingtonSource = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $washingtonRoot $washingtonSourceFile))
$washingtonEligible = @($washingtonSource | Where-Object {
    $_.Business_Name -match '(?i)\b(LLC|L\.?L\.?C\.?|INC\.?|INCORPORATED|CORP\.?|CORPORATION|LTD\.?|LIMITED|L\.?P\.?)\s*$'
})
if ($washingtonRows.Count -ne 20 -or $washingtonSource.Count -ne 497 -or
    $washingtonEligible.Count -ne 440 -or
    @($washingtonRows.LicenseNumber | Sort-Object -Unique).Count -ne 20 -or
    @($washingtonSource.License_Number | Sort-Object -Unique).Count -ne 497) {
    throw 'Unexpected Washington firm sample/source count or uniqueness.'
}
for ($i = 0; $i -lt $washingtonRows.Count; $i++) {
    $row = $washingtonRows[$i]
    $index = [int][math]::Floor(($i + 0.5) * $washingtonEligible.Count / 20)
    $source = $washingtonEligible[$index]
    if ($row.State -cne 'Washington' -or $row.SourceLicenseType -cne 'Real Estate Firm' -or
        $row.SourceStatus -cne 'Active' -or $row.QueryCity -cne 'Seattle' -or
        $row.SourceAsOfDate -cne '2026-10-11' -or $row.SourceUrl -cne $washingtonSourceUrl -or
        $row.LicenseNumber -cne $source.License_Number -or $row.EntityName -cne $source.Business_Name -or
        $row.TradeName -cne $source.Doing_Business_As -or $row.AssumedNames -cne $source.Assumed_Names -or
        $row.UbiNumber -cne $source.UBI_Number -or $row.City -cne $source.City -or
        $source.Status -cne 'Active' -or $source.License_Type -cne 'Real Estate Firm' -or
        $source.City -ine 'Seattle' -or $row.DisciplinaryAction -cne $source.Discipline -or
        $row.SubStatus -cne 'None') {
        throw "Unexpected Washington firm campaign row for license $($row.LicenseNumber)."
    }
    Add-Candidate -State 'Washington' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment 'Washington Seattle firm; development/testing only; not approved for production or outreach' `
        -SourceStatus $row.SourceStatus -QueryCity $row.QueryCity -BusinessAddress '' -BusinessPhone '' `
        -LicenseExpiryOrRenewal '' -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Real Estate Firm; no separate branch category or parent relationship established.' `
        -SourceFile "WASHINGTON/XLXS/$washingtonFile; WASHINGTON/XLXS/$washingtonSourceFile" `
        -OfficialSourceUrl $washingtonSourceUrl `
        -CampaignNotes "User authorized development/testing integration, with no production, marketing or outreach. Seattle Active firm query captured 2026-10-11: 497 records, all 50 displayed pages matched; 20 midpoint samples from 440 company-suffixed names. Name suffixes do not establish legal form. DetailVerified=$($row.DetailVerified); three selected core business profiles verified, remaining samples supported by export/result rows. Published UBI=$($row.UbiNumber); Assumed Names=$($row.AssumedNames); Sub Status=$($row.SubStatus); Disciplinary Action=$($row.DisciplinaryAction). Washington prohibits commercial use of individual lists under Chapter 42.56 RCW; no authorization or permitted commercial reuse is inferred. Personal samples/downloads remain outside this campaign. Recheck current status and establish applicable source-use permission before production or outreach."
}

$newMexicoRoot = Join-Path $docsRoot 'NEW_MEXICO\XLXS'
$newMexicoFile = 'New_Mexico_Full_Active_Entities_20261012.csv'
$newMexicoRawFile = 'New_Mexico_Active_Trade_Names_Raw_20261012.json'
$newMexicoRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $newMexicoRoot $newMexicoFile))
$newMexicoRaw = Get-Content -Raw -Encoding UTF8 -LiteralPath (Join-Path $newMexicoRoot $newMexicoRawFile) | ConvertFrom-Json
if ($newMexicoRows.Count -ne 5116 -or $newMexicoRaw.Count -ne 5116 -or
    @($newMexicoRows.LicenseNumber | Sort-Object -Unique).Count -ne 5116) {
    throw 'Unexpected New Mexico entity counts or uniqueness.'
}
foreach ($row in $newMexicoRows) {
    if ($row.State -cne 'New Mexico' -or $row.RecordKind -cne 'Business license' -or
        $row.SourceStatus -cne 'Active' -or $row.SourceLicenseType -cne 'Trade Name') {
        throw "Unexpected New Mexico business identity/status: $($row.LicenseNumber)"
    }
    Add-Candidate -State 'New Mexico' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName $row.TradeName `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $row.CampaignSegment -SourceStatus $row.SourceStatus `
        -QueryCity '' -BusinessAddress $row.BusinessAddress -BusinessPhone '' -LicenseExpiryOrRenewal $row.Expiration `
        -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Trade Name (firm brokerage credential); individual broker affiliation not verified in bulk export.' `
        -SourceFile "NEW_MEXICO/XLXS/$newMexicoFile; NEW_MEXICO/XLXS/$newMexicoRawFile" `
        -OfficialSourceUrl $row.OfficialSourceUrl `
        -CampaignNotes "Full statewide Active Trade Name query captured 2026-10-12: 5,116 records, all 103 displayed pages matched and captured without downsampling. County=$($row.County); SalesforceId=$($row.SalesforceId); PublishedIssueDate=$($row.PublishedIssueDate). Development/testing only; no authorization or permitted commercial reuse is inferred. Recheck current status and establish applicable source-use permission before production or outreach."
}

$oklahomaRoot = Join-Path $docsRoot 'OKLAHOMA\XLXS'
$oklahomaFile = 'Oklahoma_Exact_Active_Entities_20261012.csv'
$oklahomaRawFile = 'Oklahoma_Real_Estate_Business_Entities_Raw_20261012.csv'
$oklahomaRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $oklahomaRoot $oklahomaFile))
if ($oklahomaRows.Count -ne 3272 -or @($oklahomaRows.LicenseNumber | Sort-Object -Unique).Count -ne 3272) {
    throw 'Unexpected Oklahoma entity counts or uniqueness.'
}
foreach ($row in $oklahomaRows) {
    if ($row.State -cne 'Oklahoma' -or $row.RecordKind -cne 'Business license' -or
        $row.SourceStatus -cne 'Active') {
        throw "Unexpected Oklahoma business identity/status: $($row.LicenseNumber)"
    }
    Add-Candidate -State 'Oklahoma' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.SourceLicenseType -CampaignSegment $row.CampaignSegment -SourceStatus $row.SourceStatus `
        -QueryCity $row.City -BusinessAddress '' -BusinessPhone '' -LicenseExpiryOrRenewal '' `
        -ParentFirmLicenseNumber '' `
        -BranchEvidence $(if ($row.IsBranchOffice -eq 'True') { 'Published Branch Office (BO) notation; individual broker affiliation not verified in bulk export.' } else { '' }) `
        -SourceFile "OKLAHOMA/XLXS/$oklahomaFile; OKLAHOMA/XLXS/$oklahomaRawFile" `
        -OfficialSourceUrl $row.OfficialSourceUrl `
        -CampaignNotes $row.CampaignNotes
}

$rhodeIslandRoot = Join-Path $docsRoot 'RHODE_ISLAND\XLXS'
$rhodeIslandFile = 'Rhode_Island_Exact_Active_Entities_20261012.csv'
$rhodeIslandRawFile = 'Rhode_Island_Full_Captured_Business_Entities_20261012.csv'
$rhodeIslandRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $rhodeIslandRoot $rhodeIslandFile))
if ($rhodeIslandRows.Count -ne 293 -or @($rhodeIslandRows.LicenseNumber | Sort-Object -Unique).Count -ne 293) {
    throw 'Unexpected Rhode Island entity counts or uniqueness.'
}
foreach ($row in $rhodeIslandRows) {
    if ($row.Status -cne 'ACTIVE' -or $row.CredentialType -cne 'Real Estate Corporation') {
        throw "Unexpected Rhode Island business identity/status: $($row.LicenseNumber)"
    }
    $addrParts = @($row.AddressLine1, $row.AddressLine2, $row.City, $row.State, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $businessAddr = $addrParts -join ', '
    $notes = "Official statewide DBR Real Estate Corporation query captured 2026-10-12 via Tyler Technologies Eastern Cloud portal. Qualifier=$($row.BrokerQualifierName); InternalId=$($row.InternalId); InitialDate=$($row.InitialCredentialDate). Development/testing only; no outreach permission inferred. Recheck current status before production or outreach."

    Add-Candidate -State 'Rhode Island' -LicenseNumber $row.LicenseNumber -EntityName $row.BusinessName -TradeName '' `
        -SourceLicenseType $row.CredentialType -CampaignSegment 'Licensed real-estate corporate entity' -SourceStatus $row.Status `
        -QueryCity $row.City -BusinessAddress $businessAddr -BusinessPhone '' -LicenseExpiryOrRenewal $row.ExpirationDate `
        -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Real Estate Corporation credential; principal broker qualifier identified in portal details.' `
        -SourceFile "RHODE_ISLAND/XLXS/$rhodeIslandFile; RHODE_ISLAND/XLXS/$rhodeIslandRawFile" `
        -OfficialSourceUrl 'https://ridbrprod-search.state-reg-eastern.tylerapp.com/search' `
        -CampaignNotes $notes
}

$wyomingRoot = Join-Path $docsRoot 'WYOMING\XLXS'
$wyomingFile = 'Wyoming_Exact_Active_Entities_20261012.csv'
$wyomingRawFile = 'Wyoming_Full_Captured_Business_Entities_20261012.csv'
$wyomingRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $wyomingRoot $wyomingFile))
if ($wyomingRows.Count -ne 723) {
    throw 'Unexpected Wyoming entity count.'
}
foreach ($row in $wyomingRows) {
    if ($row.Status -cne 'Active' -or $row.LicenseType -cne 'Real Estate License') {
        throw "Unexpected Wyoming business identity/status: $($row.LicenseNumber)"
    }
    # Filter out state database test records and canceled placeholder rows
    if ($row.LicenseNumber -eq 'N/A' -or $row.EntityName -like '*Test*' -or $row.EntityName -like '*Canceled*' -or $row.EntityName -eq 'N/A') {
        continue
    }
    $addrParts = @($row.AddressLine1, $row.AddressLine2, $row.City, $row.State, $row.ZipCode) |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    $businessAddr = $addrParts -join ', '
    $phone = if ($row.CompanyPhone) { $row.CompanyPhone } elseif ($row.ResponsibleBrokerPhone) { $row.ResponsibleBrokerPhone } else { '' }
    $notes = "Official statewide WREC Real Estate License query captured 2026-10-12 via online.wyrec.org API. Responsible Broker=$($row.ResponsibleBrokerName) (Lic #$($row.ResponsibleBrokerLicenseNumber)); Email=$($row.CompanyEmail); Phone=$($row.CompanyPhone); Expiration=$($row.ExpirationDate); Affiliated Count=$($row.AssociatedLicenseeCount). Development/testing only; no outreach permission inferred. Recheck current status before production or outreach."

    Add-Candidate -State 'Wyoming' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Wyoming active licensed real-estate firm' -SourceStatus $row.Status `
        -QueryCity $row.City -BusinessAddress $businessAddr -BusinessPhone $phone -LicenseExpiryOrRenewal $row.ExpirationDate `
        -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Real estate brokerage firm; responsible broker designated in official WREC registry.' `
        -SourceFile "WYOMING/XLXS/$wyomingFile; WYOMING/XLXS/$wyomingRawFile" `
        -OfficialSourceUrl 'https://online.wyrec.org/#/verifylicense' `
        -CampaignNotes $notes
}

$puertoRicoRoot = Join-Path $docsRoot 'PUERTO RICO\XLXS'
$puertoRicoFile = 'Puerto_Rico_Exact_Active_Entities_20261012.csv'
$puertoRicoRawFile = 'Puerto_Rico_Full_Captured_Business_Entities_20261012.csv'
$puertoRicoRows = @(Import-Csv -Encoding UTF8 -LiteralPath (Join-Path $puertoRicoRoot $puertoRicoFile))
if ($puertoRicoRows.Count -ne 174) {
    throw 'Unexpected Puerto Rico entity count.'
}
foreach ($row in $puertoRicoRows) {
    if ($row.LicenseStatus -cne 'Active' -or $row.LicenseType -cne 'Real Estate Brokerage Entity (Empresa)') {
        throw "Unexpected Puerto Rico business identity/status: $($row.LicenseNumber)"
    }
    $notes = "Official statewide Puerto Rico licensing lookup captured 2026-10-12 via pr.pcshq.com/lookup/es/. Credential type=EMPRESA; Board=Junta de Bienes Raices de Puerto Rico; Original Status=$($row.OriginalStatus); Issue Date=$($row.IssueDate); Expiration Date=$($row.ExpirationDate). Development/testing only; no outreach permission inferred. Recheck current status before production or outreach."

    Add-Candidate -State 'Puerto Rico' -LicenseNumber $row.LicenseNumber -EntityName $row.EntityName -TradeName '' `
        -SourceLicenseType $row.LicenseType -CampaignSegment 'Puerto Rico active licensed real-estate brokerage entity (Empresa)' -SourceStatus $row.LicenseStatus `
        -QueryCity $row.City -BusinessAddress $row.City -BusinessPhone '' -LicenseExpiryOrRenewal $row.ExpirationDate `
        -ParentFirmLicenseNumber '' `
        -BranchEvidence 'Institutional real estate firm credential (EMPRESA) under Ley Num. 10 de 1994.' `
        -SourceFile "PUERTO RICO/XLXS/$puertoRicoFile; PUERTO RICO/XLXS/$puertoRicoRawFile" `
        -OfficialSourceUrl 'https://pr.pcshq.com/lookup/es/' `
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

$groupedCandidates = [System.Collections.Specialized.OrderedDictionary]::new()
foreach ($c in $candidates) {
    $key = "$($c.State)|$($c.LicenseNumber)|$($c.SourceLicenseType)"
    $list = $groupedCandidates[$key]
    if ($null -eq $list) {
        $list = [System.Collections.Generic.List[object]]::new()
        $groupedCandidates[$key] = $list
    }
    $list.Add($c)
}

$deduplicated = @(
    foreach ($entry in $groupedCandidates.GetEnumerator()) {
        $items = $entry.Value
        $first = $items[0]
        if ($items.Count -eq 1) {
            [pscustomobject][ordered]@{
                State = $first.State
                LicenseNumber = $first.LicenseNumber
                EntityName = $first.EntityName
                OtherSourceNames = ''
                TradeName = [string]$first.TradeName
                SourceLicenseType = $first.SourceLicenseType
                CampaignSegment = [string]$first.CampaignSegment
                SourceStatus = [string]$first.SourceStatus
                QueryCity = [string]$first.QueryCity
                BusinessAddress = [string]$first.BusinessAddress
                BusinessPhone = [string]$first.BusinessPhone
                LicenseExpiryOrRenewal = [string]$first.LicenseExpiryOrRenewal
                ParentFirmLicenseNumber = [string]$first.ParentFirmLicenseNumber
                BranchEvidence = [string]$first.BranchEvidence
                TrecProfileName = [string]$first.TrecProfileName
                TrecProfileStatus = [string]$first.TrecProfileStatus
                TrecProfileExpiration = [string]$first.TrecProfileExpiration
                TrecSponsoredSalesAgentCount = [string]$first.TrecSponsoredSalesAgentCount
                TrecProfileVerifiedOn = [string]$first.TrecProfileVerifiedOn
                SourceFiles = [string]$first.SourceFiles
                OfficialSourceUrl = [string]$first.OfficialSourceUrl
                CampaignNotes = [string]$first.CampaignNotes
                SourceRowCount = 1
            }
        } else {
            $namesSeen = @{}
            $names = [System.Collections.Generic.List[string]]::new()
            foreach ($it in $items) {
                if (-not [string]::IsNullOrWhiteSpace($it.EntityName) -and -not $namesSeen.ContainsKey($it.EntityName)) {
                    $namesSeen[$it.EntityName] = $true
                    $names.Add($it.EntityName)
                }
            }
            [pscustomobject][ordered]@{
                State = $first.State
                LicenseNumber = $first.LicenseNumber
                EntityName = if ($names.Count -gt 0) { $names[0] } else { '' }
                OtherSourceNames = if ($names.Count -gt 1) { ($names.GetRange(1, $names.Count - 1)) -join '; ' } else { '' }
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
    }
)

$expectedMinimums = @{
    Kentucky = 20
    Indiana = 40
    Idaho = 33
    Washington = 20
    Connecticut = 22
    Delaware = 20
    'New Jersey' = 22
    Maryland = 20
    Iowa = 20
    Minnesota = 30
    Mississippi = 21
    'North Dakota' = 15
    Oregon = 4493
    Ohio = 60
    Massachusetts = 63
    Missouri = 20
    'North Carolina' = 20
    'New Hampshire' = 20
    Arizona = 127
    Colorado = 80
    Florida = 60
    Illinois = 100
    Pennsylvania = 49
    Tennessee = 20
    'New York' = 100
    Vermont = 40
    Utah = 60
    'South Dakota' = 120
    Texas = 20
}
foreach ($state in $expectedMinimums.Keys) {
    $count = @($deduplicated | Where-Object { $_.State -eq $state }).Count
    if ($count -lt $expectedMinimums[$state]) { throw "Unexpectedly low $state candidate count: $count" }
}
$newYorkCandidateCount = @($deduplicated | Where-Object State -eq 'New York').Count
if ($newYorkCandidateCount -ne 100) {
    throw "Unexpected New York candidate count after deduplication: $newYorkCandidateCount"
}
$illinoisCandidateCount = @($deduplicated | Where-Object State -eq 'Illinois').Count
if ($illinoisCandidateCount -ne 100) {
    throw "Unexpected Illinois candidate count after deduplication: $illinoisCandidateCount"
}
$floridaCandidateCount = @($deduplicated | Where-Object State -eq 'Florida').Count
if ($floridaCandidateCount -ne 60) {
    throw "Unexpected Florida candidate count after deduplication: $floridaCandidateCount"
}
$coloradoCandidateCount = @($deduplicated | Where-Object State -eq 'Colorado').Count
if ($coloradoCandidateCount -ne 80) {
    throw "Unexpected Colorado candidate count after deduplication: $coloradoCandidateCount"
}
$pennsylvaniaCandidateCount = @($deduplicated | Where-Object State -eq 'Pennsylvania').Count
if ($pennsylvaniaCandidateCount -ne 49) {
    throw "Unexpected Pennsylvania candidate count after deduplication: $pennsylvaniaCandidateCount"
}
$tennesseeCandidateCount = @($deduplicated | Where-Object State -eq 'Tennessee').Count
if ($tennesseeCandidateCount -ne 20) {
    throw "Unexpected Tennessee candidate count after deduplication: $tennesseeCandidateCount"
}
$connecticutCandidateCount = @($deduplicated | Where-Object State -eq 'Connecticut').Count
if ($connecticutCandidateCount -ne 22) {
    throw "Unexpected Connecticut candidate count after deduplication: $connecticutCandidateCount"
}
$delawareCandidateCount = @($deduplicated | Where-Object State -eq 'Delaware').Count
if ($delawareCandidateCount -ne 20) {
    throw "Unexpected Delaware candidate count after deduplication: $delawareCandidateCount"
}
$newJerseyCandidateCount = @($deduplicated | Where-Object State -eq 'New Jersey').Count
if ($newJerseyCandidateCount -ne 22) {
    throw "Unexpected New Jersey candidate count after deduplication: $newJerseyCandidateCount"
}
$dupCheck = @{}
$duplicateKeys = [System.Collections.Generic.List[string]]::new()
foreach ($d in $deduplicated) {
    $k = "$($d.State)|$($d.LicenseNumber)|$($d.SourceLicenseType)"
    if ($dupCheck.ContainsKey($k)) {
        $duplicateKeys.Add($k)
    } else {
        $dupCheck[$k] = $true
    }
}
$marylandCandidateCount = @($deduplicated | Where-Object State -eq 'Maryland').Count
if ($marylandCandidateCount -ne 20) {
    throw "Unexpected Maryland candidate count after deduplication: $marylandCandidateCount"
}
$washingtonCandidateCount = @($deduplicated | Where-Object State -eq 'Washington').Count
if ($washingtonCandidateCount -ne 20) {
    throw "Unexpected Washington candidate count after deduplication: $washingtonCandidateCount"
}
$idahoCandidateCount = @($deduplicated | Where-Object State -eq 'Idaho').Count
if ($idahoCandidateCount -ne 33) {
    throw "Unexpected Idaho candidate count after deduplication: $idahoCandidateCount"
}
$indianaCandidateCount = @($deduplicated | Where-Object State -eq 'Indiana').Count
if ($indianaCandidateCount -ne 40) {
    throw "Unexpected Indiana candidate count after deduplication: $indianaCandidateCount"
}
$kentuckyCandidateCount = @($deduplicated | Where-Object State -eq 'Kentucky').Count
if ($kentuckyCandidateCount -ne 20) {
    throw "Unexpected Kentucky candidate count after deduplication: $kentuckyCandidateCount"
}
if (@($deduplicated | Where-Object State -eq 'Louisiana').Count -ne 2) { throw 'Unexpected Louisiana shared candidate count.' }
if (@($deduplicated | Where-Object State -eq 'Maine').Count -ne 1101) { throw 'Unexpected Maine shared candidate count.' }
if (@($deduplicated | Where-Object State -eq 'Michigan').Count -ne 7639) { throw 'Unexpected Michigan shared candidate count.' }
if (@($deduplicated | Where-Object State -eq 'New Mexico').Count -ne 5116) { throw 'Unexpected New Mexico shared candidate count.' }
if (@($deduplicated | Where-Object State -eq 'Oklahoma').Count -ne 3272) { throw 'Unexpected Oklahoma shared candidate count.' }
if (@($deduplicated | Where-Object State -eq 'Puerto Rico').Count -ne 174) { throw 'Unexpected Puerto Rico candidate count.' }
$maineByNumber = @{}
foreach ($row in $maineRows) { $maineByNumber[$row.LicenseNumber] = $row }
foreach ($candidate in $deduplicated | Where-Object State -eq 'Maine') {
    $names = $maineByNumber[$candidate.LicenseNumber].PublishedNamesJson | ConvertFrom-Json
    $candidate.OtherSourceNames = (@($names | Where-Object { $_ -cne $candidate.EntityName }) -join '; ')
}
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
