using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

/// <summary>
/// Imports browser-captured state roster samples from src/docs into approved regulatory snapshots
/// so captured license dates can drive regulatory directory views and reminders.
/// </summary>
public static class StateRosterCaptureSeed
{
    private const string NewYorkSourceUrl = "https://appext20.dos.ny.gov/nydos/selSearchType.do";
    private const string TexasSourceUrl = "https://www.trec.texas.gov/license-search/";
    private const string ConnecticutSourceUrl = "https://www.elicense.ct.gov/lookup/licenselookup.aspx";
    private const string IllinoisSourceUrl = "https://online-dfpr.micropact.com/lookup/licenselookup.aspx";
    private const string ArkansasSourceUrl = "https://labor.arkansas.gov/licensing/real-estate-commission/arec-roster/agent-search/";
    private const string VirginiaSourceUrl = "https://www.dpor.virginia.gov/LicenseLookup";
    private const string HawaiiSourceUrl = "https://mypvl.dcca.hawaii.gov/public-license-search/";
    private const string GeorgiaSourceUrl = "https://ata.grec.state.ga.us/Account/Search";
    private const string SouthCarolinaSourceUrl = "https://verify.llronline.com/LicLookup/Rec/Rec.aspx?div=19";
    private const string WestVirginiaSourceUrl = "https://www.arello.com/";
    private const string WestVirginiaRosterSourceUrl = "https://rec.wv.gov/About/Pages/Active-Licensee-Rosters.aspx";

    private static readonly DateTime NewYorkRetrievedAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TexasRetrievedAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ConnecticutRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime IllinoisRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ArkansasRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime VirginiaRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime HawaiiRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime GeorgiaRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SouthCarolinaRetrievedAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WestVirginiaRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime WestVirginiaRosterRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);

    public static async Task SeedAsync(ApplicationDbContext dbContext, string? workspaceRoot = null)
    {
        var root = CaptureSeedRoot.Resolve(workspaceRoot);
        if (root is null)
        {
            return;
        }

        await SeedNewYorkAsync(dbContext, root);
        await SeedTexasAsync(dbContext, root);
        await SeedConnecticutAsync(dbContext, root);
        await SeedIllinoisAsync(dbContext, root);
        await SeedArkansasAsync(dbContext, root);
        await SeedVirginiaAsync(dbContext, root);
        await SeedHawaiiAsync(dbContext, root);
        await SeedGeorgiaAsync(dbContext, root);
        await SeedSouthCarolinaAsync(dbContext, root);
        await SeedWestVirginiaAsync(dbContext, root);
        await SeedWestVirginiaActiveRostersAsync(dbContext, root);
    }

    private static async Task SeedNewYorkAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "New York", NewYorkSourceUrl, NewYorkRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "NEW YORK", "LICENSING", "TXTs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }

        var snapshot = CreateSnapshot("New York", "NYS DOS eAccessNY public license search (Beacon city crawl)", NewYorkSourceUrl, NewYorkRetrievedAtUtc, "nydos-beacon-city-crawl-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var firmsByName = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        foreach (var path in EnumerateFiles(docsDir, "NY Beacon Principal Office Crawl Chunk *.json"))
        {
            foreach (var record in ReadRecords(path))
            {
                var licenseNumber = GetString(record, "licenseNumber");
                var name = GetString(record, "name");
                if (licenseNumber is null || name is null || firms.ContainsKey(licenseNumber))
                {
                    continue;
                }

                var firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "New York",
                    LicenseNumber = licenseNumber,
                    LegalName = name,
                    LicenseStatus = GetString(record, "status"),
                    Address = GetString(record, "address")
                };
                firms[licenseNumber] = firm;
                firmsByName.TryAdd(name, firm);
            }
        }

        foreach (var record in ReadRecords(Path.Combine(docsDir, "NY Beacon Branch Office Crawl Complete.json")))
        {
            var licenseNumber = GetString(record, "licenseNumber");
            var name = GetString(record, "name");
            if (licenseNumber is null || name is null || firms.ContainsKey(licenseNumber))
            {
                continue;
            }

            var firm = new RegulatoryFirm
            {
                Snapshot = snapshot,
                State = "New York",
                LicenseNumber = licenseNumber,
                LegalName = name,
                TradeName = "Branch Office",
                LicenseStatus = GetString(record, "status")
            };
            firms[licenseNumber] = firm;
            firmsByName.TryAdd(name, firm);
        }

        var licenseeFiles = new (string Pattern, string Level, bool IsBroker)[]
        {
            ("NY Beacon Salesperson Crawl Page *.json", "Salesperson", false),
            ("NY Beacon Associate Broker Crawl Page *.json", "Associate Broker", false),
            ("NY Beacon LLC Broker Crawl Chunk *.json", "Limited Liability Company Broker", true),
            ("NY Beacon Trade Name Broker Crawl.json", "Trade Name Broker", true),
            ("NY Beacon Individual Broker Detail Chunk *.json", "Individual Broker", true),
            ("NY Beacon Corporate Broker Crawl Complete.json", "Corporate Broker", true)
        };

        foreach (var (pattern, level, isBroker) in licenseeFiles)
        {
            foreach (var path in EnumerateFiles(docsDir, pattern))
            {
                foreach (var record in ReadRecords(path))
                {
                    var licenseNumber = GetString(record, "licenseNumber");
                    var name = GetString(record, "licensee") ?? GetString(record, "name");
                    if (licenseNumber is null || name is null || licensees.ContainsKey(licenseNumber))
                    {
                        continue;
                    }

                    var licensee = new RegulatoryLicensee
                    {
                        Snapshot = snapshot,
                        State = "New York",
                        LicenseNumber = licenseNumber,
                        Name = name,
                        LicenseType = level,
                        LicenseLevel = level,
                        LicenseStatus = GetString(record, "status"),
                        LicenseRenewalDate = ParseDate(GetString(record, "licenseExpiry") ?? GetString(record, "expiry"))
                    };
                    licensees[licenseNumber] = licensee;

                    var firmName = GetString(record, "firm") ?? GetString(record, "relatedFirm");
                    if (firmName is not null && firmsByName.TryGetValue(firmName, out var firm))
                    {
                        affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                        {
                            Snapshot = snapshot,
                            Firm = firm,
                            Licensee = licensee,
                            IsBrokerInCharge = isBroker,
                            BrokerInChargeName = isBroker ? name : null
                        });
                    }
                }
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedTexasAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Texas", TexasSourceUrl, TexasRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "TEXAS", "LICENSING", "TXTs");
        var samplePath = Path.Combine(docsDir, "TX TREC Active Broker Sales Agent Roster Strategy Sample 01.json");
        var detailsPath = Path.Combine(docsDir, "TX TREC Active Business Broker Entity Sample 03 Details.json");
        if (!File.Exists(samplePath) || !File.Exists(detailsPath))
        {
            return;
        }

        var snapshot = CreateSnapshot("Texas", "TREC license holder search (active broker/sales agent sample)", TexasSourceUrl, TexasRetrievedAtUtc, "trec-active-entity-sample-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        using (var sample = JsonDocument.Parse(File.ReadAllText(samplePath)))
        {
            foreach (var record in sample.RootElement.GetProperty("firstPageSample").EnumerateArray())
            {
                var licenseNumber = GetString(record, "licenseNumber");
                var name = GetString(record, "name");
                var licenseType = GetString(record, "licenseType") ?? string.Empty;
                if (licenseNumber is null || name is null)
                {
                    continue;
                }

                if (licenseType.Contains("Business Broker", StringComparison.OrdinalIgnoreCase))
                {
                    if (!firms.TryGetValue(licenseNumber, out var businessFirm))
                    {
                        businessFirm = new RegulatoryFirm
                        {
                            Snapshot = snapshot,
                            State = "Texas",
                            LicenseNumber = licenseNumber,
                            LegalName = name,
                            LicenseStatus = GetString(record, "status")
                        };
                        firms[licenseNumber] = businessFirm;
                    }

                    if (record.TryGetProperty("detailProfile", out var firmDetail) && firmDetail.ValueKind == JsonValueKind.Object)
                    {
                        var designatedName = GetString(firmDetail, "designatedBroker");
                        var designatedLicense = GetString(firmDetail, "designatedBrokerLicense");
                        businessFirm.QualifyingBrokerName ??= designatedName;
                        if (designatedName is not null && designatedLicense is not null)
                        {
                            var designatedBroker = GetOrAddLicensee(licensees, snapshot, "Texas", designatedLicense, designatedName, "B", "Individual Broker", ParseDate(GetString(firmDetail, "designatedBrokerExpiration")));
                            affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                            {
                                Snapshot = snapshot,
                                Firm = businessFirm,
                                Licensee = designatedBroker,
                                IsBrokerInCharge = true,
                                BrokerInChargeName = designatedName
                            });
                        }
                    }

                    continue;
                }

                var licensee = new RegulatoryLicensee
                {
                    Snapshot = snapshot,
                    State = "Texas",
                    LicenseNumber = licenseNumber,
                    Name = name,
                    LicenseType = licenseNumber.EndsWith("-B", StringComparison.OrdinalIgnoreCase) ? "B" : "SA",
                    LicenseLevel = licenseType.Contains("Individual Broker", StringComparison.OrdinalIgnoreCase) ? "Individual Broker" : "Sales Agent",
                    LicenseStatus = GetString(record, "status"),
                    LicenseRenewalDate = ParseDate(GetString(record, "expirationDate"))
                };
                licensees.TryAdd(licenseNumber, licensee);

                // Sponsoring broker on a sales-agent detail creates a firm stub plus the affiliation.
                if (record.TryGetProperty("detailProfile", out var detail) && detail.ValueKind == JsonValueKind.Object)
                {
                    var sponsorLicense = GetString(detail, "sponsoringBrokerLicense");
                    var sponsorName = GetString(detail, "sponsoringBroker");
                    if (sponsorLicense is not null && sponsorName is not null)
                    {
                        if (!firms.TryGetValue(sponsorLicense, out var sponsorFirm))
                        {
                            sponsorFirm = new RegulatoryFirm
                            {
                                Snapshot = snapshot,
                                State = "Texas",
                                LicenseNumber = sponsorLicense,
                                LegalName = sponsorName,
                                LicenseStatus = "Active"
                            };
                            firms[sponsorLicense] = sponsorFirm;
                        }

                        affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                        {
                            Snapshot = snapshot,
                            Firm = sponsorFirm,
                            Licensee = licensee
                        });
                    }
                }
            }

            if (sample.RootElement.TryGetProperty("additionalEntityProfile", out var entityProfile)
                && entityProfile.ValueKind == JsonValueKind.Object)
            {
                var entityLicense = GetString(entityProfile, "licenseNumber");
                if (entityLicense is not null && firms.TryGetValue(entityLicense, out var entityFirm))
                {
                    entityFirm.Address = GetString(entityProfile, "businessAddress");
                    entityFirm.QualifyingBrokerName = GetString(entityProfile, "designatedBroker");
                }
            }
        }

        using (var details = JsonDocument.Parse(File.ReadAllText(detailsPath)))
        {
            foreach (var record in details.RootElement.GetProperty("records").EnumerateArray())
            {
                var firmLicense = GetString(record, "licenseNumber");
                var firmName = GetString(record, "name");
                if (firmLicense is null || firmName is null)
                {
                    continue;
                }

                if (!firms.TryGetValue(firmLicense, out var firm))
                {
                    firm = new RegulatoryFirm
                    {
                        Snapshot = snapshot,
                        State = "Texas",
                        LicenseNumber = firmLicense,
                        LegalName = firmName,
                        LicenseStatus = "Active"
                    };
                    firms[firmLicense] = firm;
                }

                firm.TradeName ??= GetString(record, "dba");

                if (record.TryGetProperty("designatedBroker", out var designated) && designated.ValueKind == JsonValueKind.Object)
                {
                    var brokerLicense = GetString(designated, "licenseNumber");
                    var brokerName = GetString(designated, "name");
                    if (brokerLicense is not null && brokerName is not null)
                    {
                        firm.QualifyingBrokerName ??= brokerName;
                        var broker = GetOrAddLicensee(licensees, snapshot, "Texas", brokerLicense, brokerName, "B", "Individual Broker", ParseDate(GetString(designated, "expirationDate")));
                        affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                        {
                            Snapshot = snapshot,
                            Firm = firm,
                            Licensee = broker,
                            IsBrokerInCharge = true,
                            BrokerInChargeName = brokerName
                        });
                    }
                }

                if (record.TryGetProperty("sponsoredAgents", out var agents) && agents.ValueKind == JsonValueKind.Array)
                {
                    foreach (var agent in agents.EnumerateArray())
                    {
                        var agentLicense = GetString(agent, "licenseNumber");
                        var agentName = GetString(agent, "name");
                        if (agentLicense is null || agentName is null)
                        {
                            continue;
                        }

                        var sponsored = GetOrAddLicensee(licensees, snapshot, "Texas", agentLicense, agentName, "SA", "Sales Agent", ParseDate(GetString(agent, "expirationDate")));
                        affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                        {
                            Snapshot = snapshot,
                            Firm = firm,
                            Licensee = sponsored
                        });
                    }
                }
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedConnecticutAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Connecticut", ConnecticutSourceUrl, ConnecticutRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "CONNECTICUT", "LICENSING", "TXTs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }

        var snapshot = CreateSnapshot("Connecticut", "CT eLicense lookup (Hartford firm profile batches)", ConnecticutSourceUrl, ConnecticutRetrievedAtUtc, "ct-elicense-hartford-firm-profiles-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in EnumerateFiles(docsDir, "CT Hartford Firm Profiles Batch *.json"))
        {
            foreach (var record in ReadRecords(path))
            {
                var licenseNumber = GetString(record, "credential");
                var name = GetString(record, "legalName");
                if (licenseNumber is null || name is null || firms.ContainsKey(licenseNumber))
                {
                    continue;
                }

                firms[licenseNumber] = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "Connecticut",
                    LicenseNumber = licenseNumber,
                    LegalName = name,
                    TradeName = NullIfEmpty(GetString(record, "dbaOrAlias")),
                    LicenseStatus = GetString(record, "status"),
                    Address = GetString(record, "address"),
                    LicenseIssuedDate = ParseDate(GetString(record, "firstIssuanceDateOnRecord")),
                    QualifyingBrokerName = NullIfEmpty(GetString(record, "brokerDesignee"))
                };
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, Array.Empty<RegulatoryLicensee>(), Array.Empty<RegulatoryFirmLicenseeAffiliation>());
    }

    private static async Task SeedIllinoisAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Illinois", IllinoisSourceUrl, IllinoisRetrievedAtUtc))
        {
            return;
        }

        var path = Path.Combine(root, "docs", "ILLINOIS", "LICENSING", "TXTs", "IL Chicago Active Real Estate Firm Crawl 01.json");
        if (!File.Exists(path))
        {
            return;
        }

        var snapshot = CreateSnapshot("Illinois", "IDFPR license lookup (Chicago active real estate sample)", IllinoisSourceUrl, IllinoisRetrievedAtUtc, "idfpr-chicago-active-sample-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        foreach (var record in ReadRecords(path))
        {
            if (!record.TryGetProperty("primaryLicense", out var primary) || primary.ValueKind != JsonValueKind.Object
                || !record.TryGetProperty("firm", out var firmElement) || firmElement.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var licenseNumber = GetString(primary, "licenseNumber");
            var name = GetString(record, "licenseeName");
            var firmLicense = GetString(firmElement, "licenseNumber");
            var firmName = GetString(firmElement, "name");
            if (licenseNumber is null || name is null || firmLicense is null || firmName is null)
            {
                continue;
            }

            if (!firms.TryGetValue(firmLicense, out var firm))
            {
                firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "Illinois",
                    LicenseNumber = firmLicense,
                    LegalName = firmName,
                    LicenseStatus = GetString(firmElement, "status")
                };
                firms[firmLicense] = firm;
            }

            if (!licensees.TryGetValue(licenseNumber, out var licensee))
            {
                licensee = new RegulatoryLicensee
                {
                    Snapshot = snapshot,
                    State = "Illinois",
                    LicenseNumber = licenseNumber,
                    Name = name,
                    LicenseType = "Broker",
                    LicenseLevel = GetString(primary, "description"),
                    LicenseStatus = GetString(primary, "status"),
                    LicenseIssuedDate = ParseDate(GetString(primary, "firstEffectiveDate")),
                    LicenseRenewalDate = ParseDate(GetString(primary, "expirationDate"))
                };
                licensees[licenseNumber] = licensee;
            }

            affiliations.Add(new RegulatoryFirmLicenseeAffiliation
            {
                Snapshot = snapshot,
                Firm = firm,
                Licensee = licensee,
                BrokerInChargeName = GetString(primary, "managingBroker")
            });
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedArkansasAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Arkansas", ArkansasSourceUrl, ArkansasRetrievedAtUtc))
        {
            return;
        }

        var path = Path.Combine(root, "docs", "ARKANSAS", "LICENSING", "TXTs", "AREC Little Rock Agent Capture 01.json");
        if (!File.Exists(path))
        {
            return;
        }

        var snapshot = CreateSnapshot("Arkansas", "AREC agent search (Little Rock active sample)", ArkansasSourceUrl, ArkansasRetrievedAtUtc, "arec-little-rock-agent-v1");
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);

        foreach (var record in ReadRecords(path))
        {
            var licenseNumber = GetString(record, "licenseNumber");
            var name = GetString(record, "name");
            if (licenseNumber is null || name is null || licensees.ContainsKey(licenseNumber))
            {
                continue;
            }

            // AREC agent search exposes firm names only (no firm license numbers), so firms are not imported.
            var prefix = licenseNumber.Length >= 2 ? licenseNumber[..2].ToUpperInvariant() : licenseNumber;
            licensees[licenseNumber] = new RegulatoryLicensee
            {
                Snapshot = snapshot,
                State = "Arkansas",
                LicenseNumber = licenseNumber,
                Name = name,
                LicenseType = prefix,
                LicenseLevel = prefix switch
                {
                    "PB" => "Principal Broker",
                    "EB" => "Executive Broker",
                    "AB" => "Associate Broker",
                    "SA" => "Salesperson",
                    _ => null
                },
                LicenseStatus = GetString(record, "status"),
                LicenseRenewalDate = ParseDate(GetString(record, "expirationDate"))
            };
        }

        await PersistAsync(dbContext, snapshot, Array.Empty<RegulatoryFirm>(), licensees.Values, Array.Empty<RegulatoryFirmLicenseeAffiliation>());
    }

    private static async Task SeedVirginiaAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Virginia", VirginiaSourceUrl, VirginiaRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "VIRGINIA", "LICENSING", "TXTs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }

        var snapshot = CreateSnapshot("Virginia", "DPOR license lookup (Richmond firm and licensee sample)", VirginiaSourceUrl, VirginiaRetrievedAtUtc, "va-dpor-richmond-sample-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        RegulatoryFirm? AddFirm(JsonElement element)
        {
            var licenseNumber = GetString(element, "licenseNumber");
            var name = GetString(element, "name");
            if (licenseNumber is null || name is null)
            {
                return null;
            }

            if (!firms.TryGetValue(licenseNumber, out var firm))
            {
                firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "Virginia",
                    LicenseNumber = licenseNumber,
                    LegalName = name,
                    TradeName = NullIfEmpty(GetString(element, "dba")),
                    Address = GetString(element, "address"),
                    LicenseIssuedDate = ParseDate(GetString(element, "initialCertificationDate")),
                    QualifyingBrokerName = NullIfEmpty(GetString(element, "inChargeOf"))
                };
                firms[licenseNumber] = firm;
            }

            return firm;
        }

        var firmListPath = Path.Combine(docsDir, "VA DPOR Richmond Firm Capture 01.json");
        if (File.Exists(firmListPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(firmListPath));
            if (document.RootElement.TryGetProperty("firms", out var firmArray) && firmArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in firmArray.EnumerateArray())
                {
                    AddFirm(element);
                }
            }
        }

        foreach (var path in EnumerateFiles(docsDir, "VA DPOR Richmond Firm Licensee CE Capture *.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            var rootElement = document.RootElement;
            RegulatoryFirm? firm = null;
            if (rootElement.TryGetProperty("firm", out var firmElement) && firmElement.ValueKind == JsonValueKind.Object)
            {
                firm = AddFirm(firmElement);
            }

            if (rootElement.TryGetProperty("relatedFirmFromLicensee", out var relatedFirm) && relatedFirm.ValueKind == JsonValueKind.Object)
            {
                AddFirm(relatedFirm);
            }

            if (firm is null || !rootElement.TryGetProperty("relatedLicensees", out var related) || related.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in related.EnumerateArray())
            {
                var licenseNumber = GetString(element, "licenseNumber");
                var name = GetString(element, "name");
                if (licenseNumber is null || name is null)
                {
                    continue;
                }

                var relationType = GetString(element, "relationType");
                var licensee = GetOrAddLicensee(licensees, snapshot, "Virginia", licenseNumber, name,
                    "Real Estate Individual", relationType ?? "Licensee", ParseDate(GetString(element, "expirationDate")));
                licensee.LicenseIssuedDate ??= ParseDate(GetString(element, "initialCertificationDate"));

                var isSupervisory = relationType?.Contains("Supervisory", StringComparison.OrdinalIgnoreCase) ?? false;
                affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                {
                    Snapshot = snapshot,
                    Firm = firm,
                    Licensee = licensee,
                    IsBrokerInCharge = isSupervisory,
                    BrokerInChargeName = isSupervisory ? name : firm.QualifyingBrokerName
                });
            }

            // Individual detail page enriches an already-listed licensee with issue date.
            if (rootElement.TryGetProperty("individualDetail", out var detail) && detail.ValueKind == JsonValueKind.Object)
            {
                var detailLicense = GetString(detail, "licenseNumber");
                if (detailLicense is not null && licensees.TryGetValue(detailLicense, out var detailLicensee))
                {
                    detailLicensee.LicenseIssuedDate ??= ParseDate(GetString(detail, "initialCertificationDate"));
                    detailLicensee.LicenseStatus = GetString(detail, "status") ?? detailLicensee.LicenseStatus;
                }
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedHawaiiAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Hawaii", HawaiiSourceUrl, HawaiiRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "HAWAII", "LICENSING", "TXTs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }

        var snapshot = CreateSnapshot("Hawaii", "MyPVL public license search (business and employee sample)", HawaiiSourceUrl, HawaiiRetrievedAtUtc, "hi-mypvl-business-employee-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        RegulatoryFirm? AddFirm(JsonElement element, string legalName)
        {
            var licenseNumber = GetString(element, "licenseId");
            if (licenseNumber is null)
            {
                return null;
            }

            if (!firms.TryGetValue(licenseNumber, out var firm))
            {
                var licenseType = GetString(element, "licenseType") ?? string.Empty;
                var isBranch = licenseType.Contains("BRANCH", StringComparison.OrdinalIgnoreCase);
                firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "Hawaii",
                    LicenseNumber = licenseNumber,
                    LegalName = legalName,
                    TradeName = NullIfEmpty(GetString(element, "tradeName")) ?? (isBranch ? "Branch Office" : null),
                    LicenseStatus = GetString(element, "activeStatus"),
                    Address = GetString(element, "businessAddress"),
                    LicenseIssuedDate = ParseDate(GetString(element, "originalLicenseDate"))
                };
                firms[licenseNumber] = firm;
            }

            return firm;
        }

        void AddEmployees(JsonElement container, string propertyName, RegulatoryFirm? firm)
        {
            if (!container.TryGetProperty(propertyName, out var employees) || employees.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (var element in employees.EnumerateArray())
            {
                var licenseNumber = GetString(element, "licenseId");
                var name = GetString(element, "name");
                if (licenseNumber is null || name is null)
                {
                    continue;
                }

                var type = GetString(element, "type") ?? GetString(element, "licenseType") ?? string.Empty;
                var level = type.Contains("SALESPERSON", StringComparison.OrdinalIgnoreCase) ? "Salesperson" : "Broker";
                var licensee = GetOrAddLicensee(licensees, snapshot, "Hawaii", licenseNumber, name, type, level, ParseDate(GetString(element, "expirationDate")));
                if (firm is not null)
                {
                    affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                    {
                        Snapshot = snapshot,
                        Firm = firm,
                        Licensee = licensee,
                        IsBrokerInCharge = false
                    });
                }
            }
        }

        var batchPath = Path.Combine(docsDir, "HI MyPVL Business Employee Capture 01.json");
        if (File.Exists(batchPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(batchPath));
            if (document.RootElement.TryGetProperty("businesses", out var businesses) && businesses.ValueKind == JsonValueKind.Array)
            {
                foreach (var business in businesses.EnumerateArray())
                {
                    var legalName = GetString(business, "query") ?? "Unknown";
                    RegulatoryFirm? entityFirm = null;
                    if (business.TryGetProperty("records", out var records) && records.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var record in records.EnumerateArray())
                        {
                            var licenseType = GetString(record, "licenseType") ?? string.Empty;
                            if (licenseType.Contains("ENTITY", StringComparison.OrdinalIgnoreCase)
                                || licenseType.Contains("BRANCH", StringComparison.OrdinalIgnoreCase))
                            {
                                var firm = AddFirm(record, legalName);
                                if (firm is not null && !licenseType.Contains("BRANCH", StringComparison.OrdinalIgnoreCase))
                                {
                                    entityFirm = firm;
                                }
                            }
                            else
                            {
                                var licenseNumber = GetString(record, "licenseId");
                                if (licenseNumber is not null)
                                {
                                    var licensee = GetOrAddLicensee(licensees, snapshot, "Hawaii", licenseNumber, legalName, licenseType, "Broker", ParseDate(GetString(record, "expirationDate")));
                                    licensee.LicenseIssuedDate ??= ParseDate(GetString(record, "originalLicenseDate"));
                                }
                            }
                        }
                    }

                    AddEmployees(business, "employeeLinks", entityFirm);
                    // History records are imported as licensees only; no current affiliation is claimed.
                    AddEmployees(business, "linkedHistoryRecords", null);
                }
            }
        }

        var detailPath = Path.Combine(docsDir, "HI MyPVL License Capture RB-22161.json");
        if (File.Exists(detailPath))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(detailPath));
            var rootElement = document.RootElement;
            RegulatoryFirm? firm = null;
            if (rootElement.TryGetProperty("business", out var business) && business.ValueKind == JsonValueKind.Object)
            {
                firm = AddFirm(business, GetString(business, "name") ?? "Unknown");
            }

            AddEmployees(rootElement, "employees", firm);
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedGeorgiaAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "Georgia", GeorgiaSourceUrl, GeorgiaRetrievedAtUtc))
        {
            return;
        }

        var path = Path.Combine(root, "docs", "GEORGIA", "LICENSING", "TXTs", "GA GREC Atlanta Company Broker Capture 01.json");
        if (!File.Exists(path))
        {
            return;
        }

        var snapshot = CreateSnapshot("Georgia", "GREC license search (Atlanta company broker sample)", GeorgiaSourceUrl, GeorgiaRetrievedAtUtc, "ga-grec-atlanta-company-broker-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var firmsByName = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        // Certification-of-licensure PDFs supply original license dates and addresses missing from the search rows.
        var firmDetails = new Dictionary<string, (DateOnly? Issued, string? Address)>(StringComparer.OrdinalIgnoreCase);
        var brokerIssueDates = new Dictionary<string, DateOnly?>(StringComparer.OrdinalIgnoreCase);
        var detailsPath = Path.Combine(root, "docs", "GEORGIA", "LICENSING", "TXTs", "GA GREC Atlanta Certification Details 01.json");
        if (File.Exists(detailsPath))
        {
            using var detailsDocument = JsonDocument.Parse(File.ReadAllText(detailsPath));
            if (detailsDocument.RootElement.TryGetProperty("firms", out var detailFirms) && detailFirms.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in detailFirms.EnumerateArray())
                {
                    var license = GetString(element, "license");
                    if (license is not null)
                    {
                        firmDetails[license] = (ParseDate(GetString(element, "originalLicenseDate")), GetString(element, "addressOfRecord"));
                    }
                }
            }

            if (detailsDocument.RootElement.TryGetProperty("brokers", out var detailBrokers) && detailBrokers.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in detailBrokers.EnumerateArray())
                {
                    var license = GetString(element, "license");
                    if (license is not null)
                    {
                        brokerIssueDates[license] = ParseDate(GetString(element, "originalLicenseDate"));
                    }
                }
            }
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("firms", out var firmArray) && firmArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in firmArray.EnumerateArray())
            {
                var licenseNumber = GetString(element, "license");
                var name = GetString(element, "name");
                if (licenseNumber is null || name is null || firms.ContainsKey(licenseNumber))
                {
                    continue;
                }

                var firm = new RegulatoryFirm
                {
                    Snapshot = snapshot,
                    State = "Georgia",
                    LicenseNumber = licenseNumber,
                    LegalName = name,
                    TradeName = NullIfEmpty(GetString(element, "dba")),
                    LicenseStatus = GetString(element, "status"),
                    LicenseIssuedDate = firmDetails.TryGetValue(licenseNumber, out var detail) ? detail.Issued : null,
                    Address = firmDetails.TryGetValue(licenseNumber, out var addressDetail) ? addressDetail.Address : null
                };
                firms[licenseNumber] = firm;
                firmsByName.TryAdd(name, firm);
            }
        }

        if (document.RootElement.TryGetProperty("brokers", out var brokerArray) && brokerArray.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in brokerArray.EnumerateArray())
            {
                var licenseNumber = GetString(element, "license");
                var name = GetString(element, "name");
                if (licenseNumber is null || name is null)
                {
                    continue;
                }

                var broker = GetOrAddLicensee(licensees, snapshot, "Georgia", licenseNumber, name,
                    GetString(element, "type") ?? "BRKR", "Broker", ParseDate(GetString(element, "renewalDue")));
                broker.LicenseStatus = GetString(element, "status") ?? broker.LicenseStatus;
                broker.LicenseIssuedDate ??= brokerIssueDates.GetValueOrDefault(licenseNumber);

                if (!element.TryGetProperty("firms", out var brokerFirms) || brokerFirms.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var brokerFirm in brokerFirms.EnumerateArray())
                {
                    var firmName = GetString(brokerFirm, "name");
                    if (firmName is null || !firmsByName.TryGetValue(firmName, out var firm))
                    {
                        continue;
                    }

                    var isQualifying = string.Equals(GetString(brokerFirm, "role"), "QUALIFYING", StringComparison.OrdinalIgnoreCase);
                    if (isQualifying)
                    {
                        firm.QualifyingBrokerName ??= name;
                    }

                    affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                    {
                        Snapshot = snapshot,
                        Firm = firm,
                        Licensee = broker,
                        IsBrokerInCharge = isQualifying,
                        BrokerInChargeName = isQualifying ? name : null
                    });
                }
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static async Task SeedSouthCarolinaAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "South Carolina", SouthCarolinaSourceUrl, SouthCarolinaRetrievedAtUtc))
        {
            return;
        }

        var docsDir = Path.Combine(root, "docs", "SOUTH CAROLINA", "LICENSING", "TXTs");
        if (!Directory.Exists(docsDir))
        {
            return;
        }
        var profilesDir = Path.Combine(root, "docs", "SOUTH CAROLINA", "LICENSING", "LICENSEES", "TXTS");

        // Firm "n" concatenates name and street address; split at the first street-number token before ", SC zip".
        var firmNameAddress = new System.Text.RegularExpressions.Regex(@"^(?<name>.+?)\s+(?<addr>\d[\w-]*\s.+,\s*SC\s*\d{5}(?:-\d{4})?)$");
        var trailingDate = new System.Text.RegularExpressions.Regex(@"^(?<name>.+?)\s+(?<date>\d{2}/\d{2}/\d{4})$");

        var classifications = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var typesPath = Path.Combine(docsDir, "SC LLR Columbia 20 License Types.json");
        if (File.Exists(typesPath))
        {
            using var typesDocument = JsonDocument.Parse(File.ReadAllText(typesPath));
            if (typesDocument.RootElement.TryGetProperty("classifications", out var classificationArray) && classificationArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var element in classificationArray.EnumerateArray())
                {
                    var license = GetString(element, "l");
                    var type = GetString(element, "t");
                    if (license is not null && type is not null)
                    {
                        classifications.TryAdd(license, type);
                    }
                }
            }
        }

        var profiles = LoadSouthCarolinaLicenseeProfiles(profilesDir);
        var snapshot = CreateSnapshot("South Carolina", "LLR licensee lookup (Columbia office sample with licensee profiles)", SouthCarolinaSourceUrl, SouthCarolinaRetrievedAtUtc, "sc-llr-columbia20-profiles-v2");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();

        foreach (var path in EnumerateFiles(docsDir, "SC LLR Columbia 20 Links *.json"))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (!document.RootElement.TryGetProperty("firms", out var firmArray) || firmArray.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var element in firmArray.EnumerateArray())
            {
                var licenseNumber = GetString(element, "l");
                var rawName = GetString(element, "n");
                if (licenseNumber is null || rawName is null)
                {
                    continue;
                }

                if (!firms.TryGetValue(licenseNumber, out var firm))
                {
                    var nameMatch = firmNameAddress.Match(rawName);
                    firm = new RegulatoryFirm
                    {
                        Snapshot = snapshot,
                        State = "South Carolina",
                        LicenseNumber = licenseNumber,
                        LegalName = nameMatch.Success ? nameMatch.Groups["name"].Value : rawName,
                        Address = nameMatch.Success ? nameMatch.Groups["addr"].Value : null,
                        LicenseStatus = GetString(element, "s"),
                        LicenseIssuedDate = ParseDate(GetString(element, "i"))
                    };
                    firms[licenseNumber] = firm;
                }

                if (!element.TryGetProperty("r", out var related) || related.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var relatedElement in related.EnumerateArray())
                {
                    var relatedLicense = GetString(relatedElement, "l");
                    var relatedRaw = GetString(relatedElement, "n");
                    if (relatedLicense is null || relatedRaw is null)
                    {
                        continue;
                    }

                    var dateMatch = trailingDate.Match(relatedRaw);
                    var relatedName = dateMatch.Success ? dateMatch.Groups["name"].Value : relatedRaw;
                    var renewalDate = dateMatch.Success ? ParseDate(dateMatch.Groups["date"].Value) : null;
                    var classification = classifications.GetValueOrDefault(relatedLicense);
                    profiles.TryGetValue(relatedLicense, out var profile);
                    var licenseType = profile?.LicenseType ?? classification ?? "Licensee";

                    var licensee = GetOrAddLicensee(licensees, snapshot, "South Carolina", relatedLicense, relatedName,
                        licenseType, licenseType, profile?.LicenseRenewalDate ?? renewalDate,
                        profile?.LicenseStatus ?? "Active", profile?.LicenseIssuedDate);

                    var isInCharge = classification?.Contains("in Charge", StringComparison.OrdinalIgnoreCase) ?? false;
                    if (isInCharge && classification!.StartsWith("Broker", StringComparison.OrdinalIgnoreCase))
                    {
                        firm.QualifyingBrokerName ??= relatedName;
                    }

                    affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                    {
                        Snapshot = snapshot,
                        Firm = firm,
                        Licensee = licensee,
                        IsBrokerInCharge = isInCharge,
                        BrokerInChargeName = isInCharge ? relatedName : null
                    });
                }
            }
        }

        await PersistAsync(dbContext, snapshot, firms.Values, licensees.Values, affiliations);
    }

    private static Dictionary<string, SouthCarolinaLicenseeProfile> LoadSouthCarolinaLicenseeProfiles(string directory)
    {
        var profiles = new Dictionary<string, SouthCarolinaLicenseeProfile>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(directory))
        {
            return profiles;
        }

        foreach (var path in Directory.EnumerateFiles(directory, "*.txt", SearchOption.TopDirectoryOnly)
            .OrderBy(file => file, StringComparer.OrdinalIgnoreCase))
        {
            var profile = ParseSouthCarolinaLicenseeProfile(File.ReadAllText(path));
            if (profile is null || string.Equals(profile.LicenseType, "Real Estate Office", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!profiles.TryAdd(profile.LicenseNumber, profile))
            {
                throw new InvalidDataException($"Multiple South Carolina individual profiles have license number {profile.LicenseNumber}.");
            }
        }

        return profiles;
    }

    private static SouthCarolinaLicenseeProfile? ParseSouthCarolinaLicenseeProfile(string text)
    {
        var fields = Regex.Matches(text,
            @"(?im)(?<!\S)(?<name>License number|License type|Date of Initial Licensure|Status|Expiration|Office):[ \t]*");
        if (fields.Count == 0)
        {
            return null;
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            var end = index + 1 < fields.Count ? fields[index + 1].Index : text.Length;
            var raw = text[(field.Index + field.Length)..end];
            var value = raw.Split(["\r\n", "\n"], StringSplitOptions.None)
                .Select(line => line.Trim())
                .FirstOrDefault(line => line.Length > 0) ?? string.Empty;
            values.TryAdd(field.Groups["name"].Value, value);
        }

        var licenseNumber = Regex.Match(values.GetValueOrDefault("License number") ?? string.Empty, @"^\d+$");
        var licenseType = NullIfEmpty(values.GetValueOrDefault("License type"));
        if (!licenseNumber.Success || licenseType is null)
        {
            return null;
        }

        return new SouthCarolinaLicenseeProfile(
            licenseNumber.Value,
            licenseType,
            NullIfEmpty(values.GetValueOrDefault("Status")),
            ParseDate(values.GetValueOrDefault("Date of Initial Licensure")),
            ParseDate(values.GetValueOrDefault("Expiration")));
    }

    private static async Task SeedWestVirginiaAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "West Virginia", WestVirginiaSourceUrl, WestVirginiaRetrievedAtUtc))
        {
            return;
        }

        var path = Path.Combine(root, "docs", "WEST VIRGINIA", "LICENSING", "TXTs", "WV ARELLO Expiration Samples.json");
        if (!File.Exists(path))
        {
            return;
        }

        var snapshot = CreateSnapshot("West Virginia", "ARELLO verified license samples", WestVirginiaSourceUrl, WestVirginiaRetrievedAtUtc, "wv-arello-expiration-sample-v1");
        var licensees = new Dictionary<string, RegulatoryLicensee>(StringComparer.OrdinalIgnoreCase);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (document.RootElement.TryGetProperty("samples", out var samples) && samples.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in samples.EnumerateArray())
            {
                var licenseNumber = GetString(element, "licenseNumber");
                var name = GetString(element, "name");
                if (licenseNumber is null || name is null || licensees.ContainsKey(licenseNumber))
                {
                    continue;
                }

                // ARELLO exposes office names only (no office license numbers), so firms are not imported.
                licensees[licenseNumber] = new RegulatoryLicensee
                {
                    Snapshot = snapshot,
                    State = "West Virginia",
                    LicenseNumber = licenseNumber,
                    Name = name,
                    LicenseType = GetString(element, "licenseType"),
                    LicenseLevel = string.Equals(GetString(element, "licenseType"), "SalesPerson", StringComparison.OrdinalIgnoreCase) ? "Salesperson" : GetString(element, "licenseType"),
                    LicenseStatus = GetString(element, "status"),
                    LicenseIssuedDate = ParseDate(GetString(element, "firstIssuedDate")),
                    LicenseRenewalDate = ParseDate(GetString(element, "expirationDate"))
                };
            }
        }

        await PersistAsync(dbContext, snapshot, Array.Empty<RegulatoryFirm>(), licensees.Values, Array.Empty<RegulatoryFirmLicenseeAffiliation>());
    }

    private static async Task SeedWestVirginiaActiveRostersAsync(ApplicationDbContext dbContext, string root)
    {
        if (await SnapshotExistsAsync(dbContext, "West Virginia", WestVirginiaRosterSourceUrl, WestVirginiaRosterRetrievedAtUtc))
        {
            return;
        }

        var directory = Path.Combine(root, "docs", "WEST VIRGINIA", "LICENSING", "XLXS");
        if (!Directory.Exists(directory))
        {
            return;
        }

        var rosterFiles = new[]
        {
            new WestVirginiaRosterFile("Salespersons Active.xlsx", "Salesperson", "BranchID", false),
            new WestVirginiaRosterFile("AB Active.xlsx", "Associate Broker", "BranchNumber", true),
            new WestVirginiaRosterFile("Broker Active.xlsx", "Broker", "BranchID", false)
        };
        var paths = rosterFiles.Select(file => Path.Combine(directory, file.FileName)).ToArray();
        var missingPath = paths.FirstOrDefault(path => !File.Exists(path));
        if (missingPath is not null)
        {
            throw new FileNotFoundException("The West Virginia active-roster capture is incomplete.", missingPath);
        }

        var snapshot = CreateSnapshot(
            "West Virginia",
            "West Virginia Real Estate Commission active licensee rosters",
            WestVirginiaRosterSourceUrl,
            WestVirginiaRosterRetrievedAtUtc,
            "wv-active-licensee-rosters-v1");
        var firms = new Dictionary<string, RegulatoryFirm>(StringComparer.OrdinalIgnoreCase);
        var licensees = new Dictionary<string, WestVirginiaLicenseeAggregate>(StringComparer.OrdinalIgnoreCase);
        var affiliations = new List<RegulatoryFirmLicenseeAffiliation>();
        var seenAffiliations = new HashSet<(string LicenseNumber, string FirmNumber)>(new WestVirginiaAffiliationComparer());

        foreach (var rosterFile in rosterFiles)
        {
            var records = ReadXlsxRecords(Path.Combine(directory, rosterFile.FileName), rosterFile);
            if (records.Count == 0)
            {
                throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' contains no licensee rows.");
            }

            foreach (var record in records)
            {
                var licenseNumber = RequiredValue(record, "LicenseNumber", rosterFile.FileName);
                var companyNumber = RequiredValue(record, "CompanyNumber", rosterFile.FileName);
                var branchNumber = RequiredValue(record, rosterFile.BranchColumn, rosterFile.FileName);
                var companyName = RequiredValue(record, "CompanyName", rosterFile.FileName);
                var name = string.Join(" ", new[]
                {
                    record.GetValueOrDefault("FirstName"),
                    record.GetValueOrDefault("MiddleName"),
                    record.GetValueOrDefault("LastName")
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
                if (string.IsNullOrWhiteSpace(name))
                {
                    throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' contains license {licenseNumber} with no name.");
                }

                var licenseType = rosterFile.HasLicenseType
                    ? RequiredValue(record, "LicenseType", rosterFile.FileName)
                    : rosterFile.LicenseLevel;
                if (!string.Equals(licenseType, rosterFile.LicenseLevel, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        $"The West Virginia roster '{rosterFile.FileName}' contains unexpected license type '{licenseType}'.");
                }

                if (!licensees.TryGetValue(licenseNumber, out var licensee))
                {
                    licensee = new WestVirginiaLicenseeAggregate(snapshot, licenseNumber);
                    licensees.Add(licenseNumber, licensee);
                }

                licensee.Add(name, rosterFile.LicenseLevel);

                var firmNumber = $"{companyNumber}-{branchNumber}";
                var address = JoinAddress(
                    record.GetValueOrDefault("CompanyStreet"),
                    record.GetValueOrDefault("CompanyCity"),
                    record.GetValueOrDefault("CompanyState"),
                    record.GetValueOrDefault("CompanyZip"));
                if (firms.TryGetValue(firmNumber, out var firm))
                {
                    if (!string.Equals(firm.LegalName, companyName, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(NormalizeCompanyAddress(firm.Address), NormalizeCompanyAddress(address), StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException(
                            $"The West Virginia rosters contain inconsistent company details for branch '{firmNumber}'.");
                    }

                    if ((address?.Length ?? 0) > (firm.Address?.Length ?? 0))
                    {
                        firm.Address = address;
                    }
                }
                else
                {
                    firm = new RegulatoryFirm
                    {
                        Snapshot = snapshot,
                        State = "West Virginia",
                        LicenseNumber = firmNumber,
                        LegalName = companyName,
                        Address = address
                    };
                    firms.Add(firmNumber, firm);
                }

                if (!seenAffiliations.Add((licenseNumber, firmNumber)))
                {
                    throw new InvalidDataException(
                        $"The West Virginia rosters repeat the same license-to-branch relationship for {licenseNumber} at {firmNumber}.");
                }

                affiliations.Add(new RegulatoryFirmLicenseeAffiliation
                {
                    Snapshot = snapshot,
                    Firm = firm,
                    Licensee = licensee.Licensee,
                    ContactEmail = NullIfEmpty(record.GetValueOrDefault("Email"))
                });
            }
        }

        foreach (var licensee in licensees.Values)
        {
            licensee.ApplyMergedCategories();
        }

        await PersistAsync(
            dbContext,
            snapshot,
            firms.Values,
            licensees.Values.Select(licensee => licensee.Licensee),
            affiliations);
    }

    private static List<Dictionary<string, string>> ReadXlsxRecords(string path, WestVirginiaRosterFile rosterFile)
    {
        using var archive = ZipFile.OpenRead(path);
        XNamespace spreadsheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        XNamespace relationshipsNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        XNamespace packageRelationshipsNs = "http://schemas.openxmlformats.org/package/2006/relationships";

        var workbook = LoadXlsxXml(archive, "xl/workbook.xml");
        var relationshipDocument = LoadXlsxXml(archive, "xl/_rels/workbook.xml.rels");
        var firstSheet = workbook.Root?.Element(spreadsheetNs + "sheets")?.Elements(spreadsheetNs + "sheet").FirstOrDefault()
            ?? throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' has no worksheet.");
        var relationshipId = (string?)firstSheet.Attribute(relationshipsNs + "id")
            ?? throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' has no worksheet relationship.");
        var target = relationshipDocument.Root?
            .Elements(packageRelationshipsNs + "Relationship")
            .FirstOrDefault(element => string.Equals((string?)element.Attribute("Id"), relationshipId, StringComparison.Ordinal))?
            .Attribute("Target")?.Value
            ?? throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' has an invalid worksheet relationship.");
        var worksheetPath = target.Replace('\\', '/').TrimStart('/');
        if (!worksheetPath.StartsWith("xl/", StringComparison.OrdinalIgnoreCase))
        {
            worksheetPath = $"xl/{worksheetPath}";
        }

        var sharedStringsEntry = archive.GetEntry("xl/sharedStrings.xml");
        var sharedStrings = sharedStringsEntry is null
            ? Array.Empty<string>()
            : LoadXlsxXml(sharedStringsEntry).Root?
                .Elements(spreadsheetNs + "si")
                .Select(item => string.Concat(item.Descendants(spreadsheetNs + "t").Select(text => text.Value)))
                .ToArray() ?? [];
        var worksheet = LoadXlsxXml(archive, worksheetPath);
        var rows = worksheet.Root?
            .Element(spreadsheetNs + "sheetData")?
            .Elements(spreadsheetNs + "row")
            .Select(row => ReadXlsxRow(row, spreadsheetNs, sharedStrings))
            .Where(row => row.Values.Any(value => !string.IsNullOrWhiteSpace(value)))
            .ToList() ?? [];
        if (rows.Count == 0)
        {
            throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' is empty.");
        }

        var headers = new Dictionary<int, string>();
        foreach (var (column, value) in rows[0])
        {
            if (!string.IsNullOrWhiteSpace(value) && !headers.TryAdd(column, value.Trim()))
            {
                throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' has duplicate header columns.");
            }
        }

        var requiredHeaders = new[]
        {
            "FirstName", "MiddleName", "LastName", "LicenseNumber", "CompanyNumber", "CompanyName",
            rosterFile.BranchColumn, "Email", "CompanyStreet", "CompanyCity", "CompanyState", "CompanyZip"
        };
        if (rosterFile.HasLicenseType)
        {
            requiredHeaders = [.. requiredHeaders, "LicenseType"];
        }

        var columnByHeader = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (column, header) in headers)
        {
            if (!columnByHeader.TryAdd(header, column))
            {
                throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' has duplicate header '{header}'.");
            }
        }

        var missingHeader = requiredHeaders.FirstOrDefault(header => !columnByHeader.ContainsKey(header));
        if (missingHeader is not null)
        {
            throw new InvalidDataException($"The West Virginia roster '{rosterFile.FileName}' is missing required column '{missingHeader}'.");
        }

        return rows.Skip(1)
            .Select(row => columnByHeader.ToDictionary(
                header => header.Key,
                header => row.GetValueOrDefault(header.Value, string.Empty),
                StringComparer.OrdinalIgnoreCase))
            .ToList();
    }

    private static Dictionary<int, string> ReadXlsxRow(XElement row, XNamespace spreadsheetNs, IReadOnlyList<string> sharedStrings)
    {
        var values = new Dictionary<int, string>();
        foreach (var cell in row.Elements(spreadsheetNs + "c"))
        {
            var reference = (string?)cell.Attribute("r")
                ?? throw new InvalidDataException("A West Virginia roster worksheet cell is missing its column reference.");
            var columnName = new string(reference.TakeWhile(char.IsLetter).ToArray());
            if (columnName.Length == 0)
            {
                throw new InvalidDataException($"A West Virginia roster worksheet has an invalid cell reference '{reference}'.");
            }

            var column = 0;
            foreach (var character in columnName)
            {
                column = checked(column * 26 + char.ToUpperInvariant(character) - 'A' + 1);
            }

            var cellType = (string?)cell.Attribute("t");
            var value = cellType switch
            {
                "s" => GetSharedString(cell, sharedStrings, reference, spreadsheetNs),
                "inlineStr" => string.Concat(cell.Descendants(spreadsheetNs + "t").Select(text => text.Value)),
                _ => cell.Element(spreadsheetNs + "v")?.Value ?? string.Empty
            };
            values.Add(column - 1, value.Trim());
        }

        return values;
    }

    private static string GetSharedString(XElement cell, IReadOnlyList<string> sharedStrings, string reference, XNamespace spreadsheetNs)
    {
        var value = cell.Element(spreadsheetNs + "v")?.Value;
        if (!int.TryParse(value, out var index) || index < 0 || index >= sharedStrings.Count)
        {
            throw new InvalidDataException($"West Virginia roster cell '{reference}' refers to an invalid shared string.");
        }

        return sharedStrings[index];
    }

    private static XDocument LoadXlsxXml(ZipArchive archive, string path)
    {
        var entry = archive.GetEntry(path)
            ?? throw new InvalidDataException($"The West Virginia roster workbook is missing '{path}'.");
        return LoadXlsxXml(entry);
    }

    private static XDocument LoadXlsxXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        return XDocument.Load(stream);
    }

    private static string RequiredValue(IReadOnlyDictionary<string, string> record, string column, string fileName)
    {
        var value = record.GetValueOrDefault(column);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidDataException($"The West Virginia roster '{fileName}' contains a row with a blank {column}.");
        }

        return value.Trim();
    }

    private static string? JoinAddress(params string?[] parts)
    {
        var value = string.Join(", ", parts.Where(part => !string.IsNullOrWhiteSpace(part)).Select(part => part!.Trim()));
        return NullIfEmpty(value);
    }

    private static string? NormalizeWhitespace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? NormalizeCompanyAddress(string? value)
    {
        var normalized = NormalizeWhitespace(value);
        return normalized is null ? null : Regex.Replace(normalized, @"\b(\d{5})-\d{4}\b", "$1");
    }

    private sealed record WestVirginiaRosterFile(string FileName, string LicenseLevel, string BranchColumn, bool HasLicenseType);

    private sealed class WestVirginiaLicenseeAggregate
    {
        private readonly Dictionary<string, int> _names = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _categories = new(StringComparer.OrdinalIgnoreCase);

        public RegulatoryLicensee Licensee { get; }

        public WestVirginiaLicenseeAggregate(RegulatoryDataSnapshot snapshot, string licenseNumber)
        {
            Licensee = new RegulatoryLicensee
            {
                Snapshot = snapshot,
                State = "West Virginia",
                LicenseNumber = licenseNumber,
                LicenseStatus = null,
                LicenseIssuedDate = null,
                LicenseRenewalDate = null
            };
        }

        public void Add(string name, string category)
        {
            var nameKey = NormalizePersonName(name);
            if (_names.Keys.Any(existing => !string.Equals(NormalizePersonName(existing), nameKey, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidDataException(
                    $"The West Virginia rosters contain conflicting names for license {Licensee.LicenseNumber}.");
            }

            _names[name] = _names.GetValueOrDefault(name) + 1;
            _categories.Add(category);
        }

        public void ApplyMergedCategories()
        {
            Licensee.Name = _names
                .OrderByDescending(pair => pair.Value)
                .ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .First().Key;
            var orderedCategories = _categories
                .OrderByDescending(GetCategoryRank)
                .ToArray();
            Licensee.LicenseLevel = orderedCategories[0];
            Licensee.LicenseType = string.Join(", ", orderedCategories);
        }

        private static string NormalizePersonName(string name) =>
            string.Concat(name.Where(character => !char.IsWhiteSpace(character) && character != '.'));

        private static int GetCategoryRank(string category) => category switch
        {
            "Broker" => 3,
            "Associate Broker" => 2,
            "Salesperson" => 1,
            _ => 0
        };
    }

    private sealed class WestVirginiaAffiliationComparer : IEqualityComparer<(string LicenseNumber, string FirmNumber)>
    {
        public bool Equals((string LicenseNumber, string FirmNumber) x, (string LicenseNumber, string FirmNumber) y) =>
            string.Equals(x.LicenseNumber, y.LicenseNumber, StringComparison.OrdinalIgnoreCase)
            && string.Equals(x.FirmNumber, y.FirmNumber, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string LicenseNumber, string FirmNumber) value) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.LicenseNumber),
                StringComparer.OrdinalIgnoreCase.GetHashCode(value.FirmNumber));
    }

    private static RegulatoryLicensee GetOrAddLicensee(
        Dictionary<string, RegulatoryLicensee> licensees,
        RegulatoryDataSnapshot snapshot,
        string state,
        string licenseNumber,
        string name,
        string licenseType,
        string licenseLevel,
        DateOnly? renewalDate,
        string? licenseStatus = "Active",
        DateOnly? issuedDate = null)
    {
        if (licensees.TryGetValue(licenseNumber, out var existing))
        {
            return existing;
        }

        var licensee = new RegulatoryLicensee
        {
            Snapshot = snapshot,
            State = state,
            LicenseNumber = licenseNumber,
            Name = name,
            LicenseType = licenseType,
            LicenseLevel = licenseLevel,
            LicenseStatus = licenseStatus,
            LicenseIssuedDate = issuedDate,
            LicenseRenewalDate = renewalDate
        };
        licensees[licenseNumber] = licensee;
        return licensee;
    }

    private sealed record SouthCarolinaLicenseeProfile(
        string LicenseNumber,
        string LicenseType,
        string? LicenseStatus,
        DateOnly? LicenseIssuedDate,
        DateOnly? LicenseRenewalDate);

    private static RegulatoryDataSnapshot CreateSnapshot(string state, string sourceName, string sourceUrl, DateTime retrievedAtUtc, string parserVersion) => new()
    {
        State = state,
        SourceName = sourceName,
        SourceUrl = sourceUrl,
        RetrievedAtUtc = retrievedAtUtc,
        ParserVersion = parserVersion,
        Status = RegulatorySnapshotStatuses.Approved,
        ReviewedAtUtc = retrievedAtUtc
    };

    private static Task<bool> SnapshotExistsAsync(ApplicationDbContext dbContext, string state, string sourceUrl, DateTime retrievedAtUtc) =>
        dbContext.RegulatoryDataSnapshots.AnyAsync(snapshot =>
            snapshot.State == state && snapshot.SourceUrl == sourceUrl && snapshot.RetrievedAtUtc == retrievedAtUtc);

    private static async Task PersistAsync(
        ApplicationDbContext dbContext,
        RegulatoryDataSnapshot snapshot,
        IEnumerable<RegulatoryFirm> firms,
        IEnumerable<RegulatoryLicensee> licensees,
        IEnumerable<RegulatoryFirmLicenseeAffiliation> affiliations)
    {
        var firmList = firms.ToList();
        var licenseeList = licensees.ToList();
        if (firmList.Count == 0 && licenseeList.Count == 0)
        {
            return;
        }

        dbContext.RegulatoryDataSnapshots.Add(snapshot);
        dbContext.RegulatoryFirms.AddRange(firmList);
        dbContext.RegulatoryLicensees.AddRange(licenseeList);
        dbContext.RegulatoryFirmLicenseeAffiliations.AddRange(affiliations);
        await dbContext.SaveChangesAsync();
    }

    private static IEnumerable<string> EnumerateFiles(string directory, string pattern)
    {
        if (pattern.Contains('*'))
        {
            return Directory.Exists(directory)
                ? Directory.EnumerateFiles(directory, pattern).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                : [];
        }

        var single = Path.Combine(directory, pattern);
        return File.Exists(single) ? [single] : [];
    }

    private static IEnumerable<JsonElement> ReadRecords(string path)
    {
        if (!File.Exists(path))
        {
            yield break;
        }

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        if (!document.RootElement.TryGetProperty("records", out var records) || records.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var record in records.EnumerateArray())
        {
            yield return record.Clone();
        }
    }

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var value = property.GetString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        return null;
    }

    private static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    private static DateOnly? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (DateOnly.TryParseExact(trimmed, "MM/dd/yyyy", out var parsed))
        {
            return parsed;
        }

        if (DateOnly.TryParseExact(trimmed, "M/d/yyyy", out var shortParsed))
        {
            return shortParsed;
        }

        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", out var isoParsed) ? isoParsed : null;
    }
}
