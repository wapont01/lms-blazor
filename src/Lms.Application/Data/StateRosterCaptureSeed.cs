using System.Text.Json;
using Lms.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Lms.Application.Data;

/// <summary>
/// Imports the browser-captured state roster samples (NY Beacon, TX TREC, CT Hartford, IL Chicago)
/// from src/docs into approved regulatory snapshots so authoritative expiration dates can drive reminders.
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

    private static readonly DateTime NewYorkRetrievedAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime TexasRetrievedAtUtc = new(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ConnecticutRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime IllinoisRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ArkansasRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime VirginiaRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime HawaiiRetrievedAtUtc = new(2026, 9, 19, 0, 0, 0, DateTimeKind.Utc);

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

    private static RegulatoryLicensee GetOrAddLicensee(
        Dictionary<string, RegulatoryLicensee> licensees,
        RegulatoryDataSnapshot snapshot,
        string state,
        string licenseNumber,
        string name,
        string licenseType,
        string licenseLevel,
        DateOnly? renewalDate)
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
            LicenseStatus = "Active",
            LicenseRenewalDate = renewalDate
        };
        licensees[licenseNumber] = licensee;
        return licensee;
    }

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

        return DateOnly.TryParseExact(trimmed, "yyyy-MM-dd", out var isoParsed) ? isoParsed : null;
    }
}
